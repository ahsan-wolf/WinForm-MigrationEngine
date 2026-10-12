using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Migrator.Engine.Contracts;
using Migrator.Target.React.Translation;
using Migrator.Target.React.Spec;
using WebFormsMigrator.IR.Models;

namespace Migrator.Target.React.Generation;

public sealed class GeneratedPage
{
    public string ComponentName { get; init; } = "";
    public string Source { get; init; } = "";
    public string LegacySource { get; init; } = "";
    public List<GenerationIssue> Issues { get; } = new();
    public int Controls { get; init; }
    public int Handlers { get; init; }
    public int NavigationHandlers { get; init; }
    public List<string> NonVisual { get; init; } = new();

    // Translation (rules) results; all zero when translation is off.
    public int MethodsTotal { get; set; }
    public int MethodsComplete { get; set; }
    public int MethodsPartial { get; set; }
    public int MethodsNotTranslated { get; set; }
    public int MethodsDemoted { get; set; }
    public List<string> TodoReasons { get; } = new();

    /// <summary>Line ranges (1-based, inclusive) of each translated function in Source; the compile gate maps TypeScript errors back to methods through them.</summary>
    public List<(string TsName, int Start, int End)> MethodRanges { get; } = new();
}

/// <summary>
/// Turns one unit's IR into a React component that imports only `Wf*` components from the UI library. Markup, state, wiring and
/// navigation are deterministic; handler bodies are NOT translated (original C# stays as comments + TODO, per CLAUDE.md §6).
/// </summary>
public sealed partial class PageGenerator
{
    private static readonly HashSet<string> NonVisualTypes = new(StringComparer.Ordinal)
    {
        "ImageList", "ToolTip", "BindingSource", "Timer", "OpenFileDialog", "SaveFileDialog", "FolderBrowserDialog", "ColorDialog",
        "FontDialog", "NotifyIcon", "ErrorProvider", "BackgroundWorker", "HelpProvider", "PrintDocument", "PrintDialog",
        "PageSetupDialog", "ContextMenuStrip"
    };

    private static readonly (string Control, string Event)[] StateEvents =
    {
        ("WfTextBox", "valueChange"), ("WfComboBox", "selectionChange"), ("WfCheckBox", "checkedChange")
    };

    private static readonly JsonSerializerOptions JsonOut = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly SpecCatalog _spec;
    private readonly IReadOnlyDictionary<string, UnitIr> _units;
    private readonly IReadOnlyDictionary<string, string> _components;
    private readonly ISet<string> _generated;
    private readonly TranslationEnv? _translation;

    public PageGenerator(SpecCatalog spec, IReadOnlyDictionary<string, UnitIr> units, IReadOnlyDictionary<string, string> components, ISet<string> generated, TranslationEnv? translation = null)
    {
        _translation = translation;
        _spec = spec;
        _units = units;
        _components = components;
        _generated = generated;
    }

    private sealed class Handler
    {
        public string Name = "";
        public string ControlId = "";
        public string Event = "";
        public MemberIr Member = null!;
        public bool IsRoot;
    }

    private sealed class Ctx
    {
        public Ctx(UnitIr unit, string component)
        {
            Unit = unit;
            Component = component;
            Names = new NameScope(new[] { "onClose", "dialog", "useState", "useWfDialog", "useWfModel", "vm", "f", "props", "_props", component, component + "Props" });
            Methods = unit.Page.Members.Where(m => m.Kind == MemberKind.Method)
                .GroupBy(m => m.Name).ToDictionary(g => g.Key, g => g.FirstOrDefault(m => m.Parameters.Count == 2) ?? g.First());
        }

        public UnitIr Unit { get; }
        public string Component { get; }
        public NameScope Names { get; }
        public Dictionary<string, MemberIr> Methods { get; }
        public Dictionary<string, ControlNodeIr> ById { get; } = new();
        public SortedSet<string> Lib { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> LibTypes { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, string> PageImports { get; } = new(StringComparer.Ordinal);
        public List<string> States { get; } = new();
        public List<string> TopLevel { get; } = new();
        public Dictionary<string, Handler> Handlers { get; } = new();
        public HashSet<string> StatefulIds { get; } = new();
        public HashSet<string> ListViewIds { get; } = new();

        /// <summary>Control id -> WinForms property used in code -> TS initial value. Becomes `const vm = useWfModel({...})`.</summary>
        public SortedDictionary<string, SortedDictionary<string, string>> Model { get; } = new(StringComparer.Ordinal);
        public List<string> ModelComments { get; } = new();

        /// <summary>True once JSX or code references `vm`; the model is declared only then (an unused declaration fails lint).</summary>
        public bool UsesModel { get; set; }

        public PageTranslation? Translation { get; set; }

        public bool ModelHas(string id, string winFormsProp) => Model.TryGetValue(id, out var props) && props.ContainsKey(ModelKey(winFormsProp));

        public void ModelSet(string id, string winFormsProp, string initial)
        {
            if (!Model.TryGetValue(id, out var props)) Model[id] = props = new SortedDictionary<string, string>(StringComparer.Ordinal);
            props.TryAdd(ModelKey(winFormsProp), initial);
        }
        public HashSet<string> ButtonIds { get; } = new();
        public List<GenerationIssue> Issues { get; } = new();
        public List<string> NonVisual { get; } = new();
        public bool UsesDialog { get; set; }
        public bool UsesOnClose { get; set; }
        public bool ImageNoted { get; set; }

        public void Issue(string category, string message) => Issues.Add(new GenerationIssue(Unit.Name, category, message));
    }

    public GeneratedPage Generate(UnitIr unit, ISet<string>? demoted = null)
    {
        var root = unit.Page.RootControl ?? throw new InvalidDataException($"{unit.Name} has no control tree.");
        var component = _components[unit.Name];
        var c = new Ctx(unit, component);
        foreach (var n in Walk(root)) c.ById.TryAdd(n.Id, n);
        Prescan(c, root);
        if (_translation is not null)
            c.Translation = PageTranslator.Translate(unit, c.Names, _translation, WiredHandlers(c, root), demoted ?? new HashSet<string>());

        var jsx = RenderElement(root, c, isRoot: true) ?? throw new InvalidDataException($"{unit.Name}: root control produced no output.");
        var handlerCode = BuildFunctions(c, jsx);
        var usesFields = false;
        string? serviceImport = null;
        List<string> typeImports = new();
        if (c.Translation is { } tr)
        {
            // Everything the page declares or imports is decided from the text that is actually emitted: a method demoted by the
            // compile gate, or a statement that fell back, must not leave an unused `dialog`, `vm`, import or type behind.
            var fieldText = string.Join("\n", tr.FieldInitials.Values);
            var text = string.Join("\n", jsx.Concat(handlerCode).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            c.UsesDialog = Regex.IsMatch(text, @"\bdialog\.");
            c.UsesOnClose = Regex.IsMatch(text, @"\bonClose\b");
            c.UsesModel = Regex.IsMatch(text, @"\bvm\.");
            usesFields = Regex.IsMatch(text, @"\bf\.");
            foreach (var comp in tr.PageImports.Where(p => Regex.IsMatch(text, $@"<{p}\b"))) c.PageImports[comp] = comp;
            foreach (var comp in c.PageImports.Keys.ToList())
                if (!Regex.IsMatch(text, $@"<{comp}\b")) c.PageImports.Remove(comp);
            var instance = _translation!.Vocabulary.InstanceName;
            if (instance.Length > 0 && Regex.IsMatch(text, $@"\b{Regex.Escape(instance)}\."))
                serviceImport = $"import {{ {instance} }} from '{_translation.Vocabulary.ServiceImport}';";
            var typeText = text + "\n" + (usesFields ? fieldText : "");
            typeImports = tr.TypeImports.Where(t => Regex.IsMatch(typeText, $@"\b{Regex.Escape(t)}\b")).ToList();
            tr.UsesFormat = Regex.IsMatch(text, @"\bformatString\(");
            foreach (var helper in new[] { "wfListItem", "wfListItemFromArray" }) if (Regex.IsMatch(text, $@"\b{helper}\(")) c.Lib.Add(helper);
            if (Regex.IsMatch(text + "\n" + (usesFields ? fieldText : ""), @"\bWfListItem\b")) c.LibTypes.Add("WfListItem");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"// Generated by Migrator.Target.React from {unit.Name} ({unit.Page.RelativePath ?? "n/a"}). Not final: see MIGRATION_REPORT.md.");
        if (c.States.Count > 0) sb.AppendLine("import { useState } from 'react';");

        var lib = new List<string>(c.Lib);
        if (c.UsesDialog) lib.Add("useWfDialog");
        if (c.UsesModel || usesFields) lib.Add("useWfModel");
        lib.AddRange(c.LibTypes.Select(t => "type " + t));
        sb.AppendLine($"import {{ {string.Join(", ", lib)} }} from '@wfm/ui-react';");
        foreach (var (comp, file) in c.PageImports) sb.AppendLine($"import {{ {comp} }} from './{file}';");
        if (c.Translation is { UsesFormat: true }) sb.AppendLine("import { formatString } from '../lib/csharp';");
        if (serviceImport is not null) sb.AppendLine(serviceImport);
        if (typeImports.Count > 0 && _translation is not null)
            sb.AppendLine($"import type {{ {string.Join(", ", typeImports)} }} from '{_translation.Vocabulary.TypesImport}';");
        sb.AppendLine();
        foreach (var t in c.TopLevel) sb.AppendLine(t);
        if (c.TopLevel.Count > 0) sb.AppendLine();

        sb.AppendLine($"export interface {component}Props {{ onClose?: (result?: unknown) => void }}");
        sb.AppendLine();
        sb.AppendLine(c.UsesOnClose ? $"export function {component}({{ onClose }}: {component}Props) {{" : $"export function {component}(_props: {component}Props) {{");
        if (c.UsesDialog) sb.AppendLine("  const dialog = useWfDialog();");
        foreach (var s in c.States) sb.AppendLine("  " + s);
        if (c.UsesModel)
        {
            foreach (var comment in c.ModelComments) sb.AppendLine("  " + comment);
            sb.AppendLine("  const vm = useWfModel({");
            foreach (var (id, props) in c.Model)
                sb.AppendLine($"    {Prop(id)}: {{ {string.Join(", ", props.Select(kv => $"{kv.Key}: {kv.Value}"))} }},");
            sb.AppendLine("  });");
        }
        if (usesFields)
        {
            sb.AppendLine("  // Fields of the original form. Initial values come from their C# initializers (a default for the declared type otherwise).");
            sb.AppendLine("  const f = useWfModel({");
            foreach (var (name, initial) in c.Translation!.FieldInitials) sb.AppendLine($"    {Prop(name)}: {initial},");
            sb.AppendLine("  });");
        }
        if (c.UsesDialog || c.States.Count > 0 || c.UsesModel || usesFields) sb.AppendLine();
        foreach (var line in handlerCode) sb.AppendLine(line.Length == 0 ? "" : "  " + line);
        sb.AppendLine("  return (");
        foreach (var line in jsx) sb.AppendLine("    " + line);
        sb.AppendLine("  );");
        sb.AppendLine("}");

        var navHandlers = c.Handlers.Values.Count(h => unit.Page.Navigations.Any(n => n.SourceControlId == h.ControlId && n.TriggerEvent == h.Event));
        var page = new GeneratedPage
        {
            ComponentName = component,
            Source = sb.ToString(),
            LegacySource = BuildLegacy(unit),
            Controls = Walk(root).Count(),
            Handlers = c.Handlers.Count,
            NavigationHandlers = navHandlers,
            NonVisual = c.NonVisual
        };
        page.Issues.AddRange(c.Issues);
        if (c.Translation is { } stats)
        {
            page.MethodsTotal = stats.Methods.Count;
            page.MethodsComplete = stats.Methods.Count(m => m.Status == TranslationStatus.Complete);
            page.MethodsPartial = stats.Methods.Count(m => m.Status == TranslationStatus.Partial);
            page.MethodsNotTranslated = stats.Methods.Count(m => m.Status == TranslationStatus.NotTranslated);
            page.MethodsDemoted = stats.Methods.Count(m => m.Reasons.Contains("demoted by the TypeScript compile gate"));
            foreach (var m in stats.Methods)
                foreach (var reason in m.Reasons.Distinct()) page.TodoReasons.Add($"{m.Member.Name}: {reason}");
            AddRanges(page, stats);
        }
        return page;
    }

    // ------------------------------------------------------------------------------------------------ elements

    private List<string>? RenderElement(ControlNodeIr node, Ctx c, bool isRoot = false)
    {
        var type = ShortType(node.TagName);
        if (!isRoot && NonVisualTypes.Contains(type))
        {
            c.NonVisual.Add($"{node.Id} ({type})");
            return null;
        }

        var spec = isRoot ? RootSpec(node) : _spec.Find(node.TagName);
        if (spec is null) return RenderFallback(node, c, type);

        c.Lib.Add(spec.Name);
        var attrs = new List<string>();
        BuildProps(node, spec, c, attrs, isRoot);
        BuildEvents(node, spec, c, attrs, isRoot);
        var children = RenderChildren(node, spec, c);
        var lines = Element(spec.Name, attrs, children);

        // ContextMenuStrip="this.contextMenuStrip1" -> wrap the control.
        if (!isRoot && node.Attributes.TryGetValue("ContextMenuStrip", out var cm)
            && c.ById.TryGetValue(LastToken(cm), out var menuNode) && ShortType(menuNode.TagName) == "ContextMenuStrip")
        {
            c.Lib.Add("WfContextMenu");
            var menuLines = RenderSequential(menuNode.Children, c);
            var wrapped = new List<string> { "<WfContextMenu menu={(", "  <>" };
            wrapped.AddRange(Indent(Indent(menuLines)));
            wrapped.Add("  </>");
            wrapped.Add(")}>");
            wrapped.AddRange(Indent(lines));
            wrapped.Add("</WfContextMenu>");
            lines = wrapped;
        }

        // Visible: bound to the model when the designer hides the control or code toggles it.
        if (!isRoot && c.ModelHas(node.Id, "Visible"))
        {
            c.UsesModel = true;
            lines = new List<string> { $"{{vm.{node.Id}.visible && (" }.Concat(Indent(lines)).Append(")}").ToList();
        }
        return lines;
    }

    private SpecControl? RootSpec(ControlNodeIr root)
    {
        var spec = _spec.Find(root.TagName);
        if (spec != null) return spec;
        var name = root.TagName.EndsWith("UserControl", StringComparison.Ordinal) ? "WfPanel" : "WfForm"; // custom base classes behave as Form
        return _spec.Controls.FirstOrDefault(s => s.Name == name);
    }

    private List<string> RenderFallback(ControlNodeIr node, Ctx c, string type)
    {
        // A project UserControl whose tree was inlined: render its children inside a panel instead of hiding them.
        if (node.Children.Count > 0 && !node.TagName.StartsWith("System.Windows.Forms.", StringComparison.Ordinal))
        {
            c.Lib.Add("WfPanel");
            c.Issue("user-control-inlined", $"{node.Id} ({type}) rendered as a panel with its inlined children");
            return Element("WfPanel", new List<string>(), RenderArranged(node.Children, c));
        }

        c.Lib.Add("WfUnsupported");
        c.Issue("unsupported-control", $"{node.Id}: {node.TagName}");
        return new List<string>
        {
            "{/* TODO: no React equivalent generated — replace manually */}",
            $"<WfUnsupported type={Str(node.TagName)} reason=\"Not migrated — see MIGRATION_REPORT.md\" />"
        };
    }

    private static List<string> Element(string name, List<string> attrs, List<string> children)
    {
        var open = attrs.Count == 0 ? $"<{name}" : $"<{name} {string.Join(' ', attrs)}";
        if (children.Count == 0) return new List<string> { open + " />" };
        var lines = new List<string> { open + ">" };
        lines.AddRange(Indent(children));
        lines.Add($"</{name}>");
        return lines;
    }

    // --------------------------------------------------------------------------------------------- children

    private List<string> RenderChildren(ControlNodeIr node, SpecControl spec, Ctx c)
    {
        var children = node.Children.Where(ch => ch.Attributes.GetValueOrDefault("ParentSlot") != "Columns").ToList();
        return spec.Name switch
        {
            "WfDataGrid" or "WfListView" => new List<string>(),
            "WfSplit" => RenderSplit(children, c),
            "WfForm" or "WfPanel" => RenderArranged(children, c),
            _ => RenderSequential(children, c)
        };
    }

    private List<string> RenderSequential(IEnumerable<ControlNodeIr> children, Ctx c)
    {
        var lines = new List<string>();
        foreach (var child in children)
            if (RenderElement(child, c) is { } rendered) lines.AddRange(rendered);
        return lines;
    }

    private List<string> RenderSplit(List<ControlNodeIr> children, Ctx c)
    {
        c.Lib.Add("WfSplitPane");
        var lines = new List<string>();
        foreach (var slot in new[] { "Panel1", "Panel2" })
        {
            var inSlot = children.Where(ch => ch.Attributes.GetValueOrDefault("ParentSlot") == slot).ToList();
            lines.AddRange(Element("WfSplitPane", new List<string>(), RenderArranged(inSlot, c)));
        }
        return lines;
    }

    /// <summary>Dock/Location facts -> flex rows and columns (never pixels): top chrome, free rows, left/fill/right band, bottom chrome.</summary>
    private List<string> RenderArranged(IEnumerable<ControlNodeIr> children, Ctx c)
    {
        var arrangement = LayoutDeriver.Arrange(children, c.Unit.Layout);
        var lines = new List<string>();
        foreach (var n in arrangement.Top) AddRendered(lines, n, c);

        var body = new List<string>();
        foreach (var row in arrangement.Rows)
        {
            var rendered = row.Select(n => RenderElement(n, c)).Where(r => r != null).Select(r => r!).ToList();
            if (rendered.Count == 1) body.AddRange(rendered[0]);
            else if (rendered.Count > 1)
            {
                body.Add("<div className=\"wfm-row\">");
                foreach (var r in rendered) body.AddRange(Indent(r));
                body.Add("</div>");
            }
        }

        var band = new List<string>();
        foreach (var n in arrangement.Left) AddRendered(band, n, c);
        var fill = new List<string>();
        foreach (var n in arrangement.Fill) AddRendered(fill, n, c);
        if (fill.Count > 0) { band.Add("<div className=\"wfm-fill\">"); band.AddRange(Indent(fill)); band.Add("</div>"); }
        foreach (var n in arrangement.Right) AddRendered(band, n, c);
        if (band.Count > 0) { body.Add("<div className=\"wfm-main\">"); body.AddRange(Indent(band)); body.Add("</div>"); }

        if (body.Count > 0) { lines.Add("<div className=\"wfm-body\">"); lines.AddRange(Indent(body)); lines.Add("</div>"); }
        foreach (var n in arrangement.Bottom) AddRendered(lines, n, c);
        return lines;
    }

    private void AddRendered(List<string> into, ControlNodeIr node, Ctx c)
    {
        if (RenderElement(node, c) is { } rendered) into.AddRange(rendered);
    }

    // -------------------------------------------------------------------------------------------------- model

    private static string ModelKey(string winFormsProp) => char.ToLowerInvariant(winFormsProp[0]) + winFormsProp[1..];

    /// <summary>
    /// Decides which control properties live in the model: user-editable inputs, hidden controls, and every Text/Enabled/Visible/Checked/
    /// Value/ReadOnly the code-behind reads or writes (`btnSave.Enabled = false`, `this.Text = ...`), so translated code can use them 1:1.
    /// </summary>
    private void Prescan(Ctx c, ControlNodeIr root)
    {
        foreach (var node in c.ById.Values)
        {
            if (node == root) continue;
            var spec = _spec.Find(node.TagName);
            if (spec is not null && IsStateful(spec.Name))
            {
                c.StatefulIds.Add(node.Id);
                AddToModel(c, node, spec.Name == "WfCheckBox" ? "Checked" : "Text");
            }
            if (node.Attributes.GetValueOrDefault("Visible") == "false") AddToModel(c, node, "Visible");
            if (spec?.Name == "WfListView") { c.ListViewIds.Add(node.Id); AddListViewModel(c, node); }
        }

        var code = string.Join("\n", c.Unit.Page.Members.Where(m => m.Kind == MemberKind.Method && m.Body != null).Select(m => m.Body));
        foreach (Match m in UsedPropertyRegex().Matches(code))
        {
            var id = m.Groups[1].Value == "this" ? root.Id : m.Groups[1].Value;
            if (!c.ById.TryGetValue(id, out var node)) continue;
            if (node != root || m.Groups[2].Value is "Text" or "Enabled" or "Visible") AddToModel(c, node, m.Groups[2].Value);
        }
    }

    /// <summary>Columns (from the designer's ColumnHeader children) and items live in the model so code can Columns.Add / Items.Add / SelectedItems.</summary>
    private static void AddListViewModel(Ctx c, ControlNodeIr node)
    {
        c.LibTypes.Add("WfListColumn");
        c.LibTypes.Add("WfListItem");
        var cols = node.Children.Where(ch => ch.Attributes.GetValueOrDefault("ParentSlot") == "Columns")
            .Select(ch => $"{{ header: {Str(ch.Attributes.GetValueOrDefault("Text") ?? ch.Id)}{(int.TryParse(ch.Attributes.GetValueOrDefault("Width"), out var w) ? $", width: {w}" : "")} }}");
        c.ModelSet(node.Id, "Columns", $"[{string.Join(", ", cols)}] as WfListColumn[]");
        c.ModelSet(node.Id, "Items", "[] as WfListItem[]");
    }

    private static void AddToModel(Ctx c, ControlNodeIr node, string winFormsProp)
    {
        var raw = node.Attributes.GetValueOrDefault(winFormsProp);
        var initial = winFormsProp switch
        {
            "Text" => Str(raw ?? ""),
            "Enabled" or "Visible" => raw == "false" ? "false" : "true",
            "Checked" or "ReadOnly" => raw == "true" ? "true" : "false",
            "Value" => int.TryParse(raw, out var n) ? n.ToString() : "0",
            _ => "null"
        };
        c.ModelSet(node.Id, winFormsProp, initial);
    }

    [GeneratedRegex(@"\b(\w+)\s*\.\s*(Text|Enabled|Visible|Checked|Value|ReadOnly)\b")]
    private static partial Regex UsedPropertyRegex();

    // ------------------------------------------------------------------------------------------------ props

    private void BuildProps(ControlNodeIr node, SpecControl spec, Ctx c, List<string> attrs, bool isRoot)
    {
        if (c.ButtonIds.Contains(node.Id)) attrs.Add($"id={Str(node.Id)}");

        foreach (var prop in spec.Props.Where(p => p.From.Length > 0))
        {
            if (prop.Name == "visible") continue; // handled by the Visible wrapper
            if (prop.Name == "icon") { NoteImages(c); continue; }

            // A property that code reads/writes (or that is user-editable state) is bound to the model: vm.<id>.<prop>.
            var modelProp = prop.From.FirstOrDefault(f => c.ModelHas(node.Id, f));
            if (modelProp is not null)
            {
                c.UsesModel = true;
                var access = $"vm.{node.Id}.{ModelKey(modelProp)}";
                attrs.Add(prop.Transform == "invert" ? $"disabled={{!{access}}}" : $"{prop.Name}={{{access}}}");
                continue;
            }

            var key = prop.From.FirstOrDefault(f => node.Attributes.ContainsKey(f));
            if (key is null) continue;
            if (Convert(prop, key, node.Attributes[key], c, isRoot) is { } attr) attrs.Add(attr);
        }

        if (spec.Name == "WfDataGrid") AddGrid(node, c, attrs);
        if (spec.Name == "WfListView") AddListView(node, c, attrs);
    }

    private static void AddListView(ControlNodeIr node, Ctx c, List<string> attrs)
    {
        c.UsesModel = true;
        attrs.Add($"columns={{vm.{node.Id}.columns}}");
        attrs.Add($"items={{vm.{node.Id}.items}}");
        if (node.Attributes.GetValueOrDefault("MultiSelect") == "false") attrs.Add("multiSelect={false}");
    }

    private static void NoteImages(Ctx c)
    {
        if (c.ImageNoted) return;
        c.ImageNoted = true;
        c.Issue("image-resource", "designer images live in .resx and were not extracted; icons are omitted");
    }

    private string? Convert(SpecProp prop, string key, string raw, Ctx c, bool isRoot)
    {
        switch (prop.Name)
        {
            case "shortcut":
                var keys = KeysRegex().Matches(raw).Select(m => m.Groups[1].Value).ToList();
                return Attr("shortcut", keys.Count > 0 ? string.Join('+', keys) : raw);
            case "orientation":
                var o = LastToken(raw).ToLowerInvariant();
                return o is "horizontal" or "vertical" ? Attr("orientation", o) : null;
            case "fit":
                return LastToken(raw) switch
                {
                    "Zoom" => Attr("fit", "contain"), "StretchImage" => Attr("fit", "fill"),
                    "CenterImage" or "Normal" or "AutoSize" => Attr("fit", "none"), _ => null
                };
            case "selectionMode":
                return raw == "true" ? Attr("selectionMode", "multi") : null;
            case "acceptButtonId":
            case "cancelButtonId":
                var id = LastToken(raw);
                if (isRoot && id.Length > 0) { c.ButtonIds.Add(id); return Attr(prop.Name, id); }
                return null;
            case "items":
                return TryJsonStringArray(raw, out var json) ? $"items={{{json}}}" : null;
        }

        return prop.Transform switch
        {
            "invert" => raw == "false" ? "disabled" : null,
            "bool" => raw == "true" ? prop.Name : null,
            "int" => int.TryParse(raw, out var n) ? $"{prop.Name}={{{n}}}" : null,
            _ => Attr(prop.Name, raw)
        };
    }

    private void AddGrid(ControlNodeIr node, Ctx c, List<string> attrs)
    {
        c.LibTypes.Add("WfGridColumn");
        var baseName = Naming.StateName(node.Id);
        var colsName = c.Names.Unique(baseName + "Columns");

        var cols = node.Children.Where(ch => ch.Attributes.GetValueOrDefault("ParentSlot") == "Columns").ToList();
        var defs = new List<string>();
        var fields = new List<(string Field, string Header, bool Checkbox, string? Format)>();
        foreach (var col in cols)
        {
            var field = col.Attributes.GetValueOrDefault("DataPropertyName") is { Length: > 0 } dp ? dp : col.Attributes.GetValueOrDefault("Name") ?? col.Id;
            var header = col.Attributes.GetValueOrDefault("HeaderText") ?? col.Id;
            var format = col.Attributes.GetValueOrDefault("DefaultCellStyle.Format");
            var width = int.TryParse(col.Attributes.GetValueOrDefault("Width"), out var w) ? $", width: {w}" : "";
            defs.Add($"  {{ field: {Str(field)}, header: {Str(header)}{width}{(format is null ? "" : $", format: {Str(format)}")} }},");
            fields.Add((field, header, col.TagName.EndsWith("CheckBoxColumn", StringComparison.Ordinal), format));
        }

        if (cols.Count == 0)
        {
            c.TopLevel.Add($"// TODO: columns of {node.Id} are created in code (not in the designer); define them here.");
            c.Issue("grid-no-columns", $"{node.Id} has no designer columns; rows are empty");
        }
        c.TopLevel.Add($"const {colsName}: WfGridColumn[] = [");
        c.TopLevel.AddRange(defs);
        c.TopLevel.Add("];");

        var rows = Enumerable.Range(1, cols.Count == 0 ? 0 : 3).Select(i =>
            fields.ToDictionary(f => f.Field, f => (object)(f.Checkbox ? i % 2 == 1 : f.Format is "d" or "date" ? $"2026-01-{i:00}" : $"{f.Header} {i}"))).ToList();
        c.ModelComments.Add($"// MOCK — {node.Id} rows are fake sample data; replace with real implementation (inferred — check against backend contract)");
        c.ModelSet(node.Id, "Rows", $"{JsonSerializer.Serialize(rows, JsonOut)} as Record<string, unknown>[]");
        c.UsesModel = true;
        attrs.Add($"rows={{vm.{node.Id}.rows}}");
        attrs.Add($"columns={{{colsName}}}");
    }

    // ---------------------------------------------------------------------------------------------- events

    private void BuildEvents(ControlNodeIr node, SpecControl spec, Ctx c, List<string> attrs, bool isRoot)
    {
        foreach (var ev in spec.Events)
        {
            string? handlerName = null;
            MemberIr? handlerMember = null;
            if (node.Attributes.TryGetValue("On" + ev.From, out var method) && c.Methods.TryGetValue(method, out var member))
            {
                handlerMember = member;
                handlerName = GetHandler(node, ev.From, member, c, isRoot).Name;
            }

            // Button.DialogResult: clicking closes the dialog with that result (after the Click handler ran), with or without a handler.
            if (spec.Name == "WfButton" && ev.Name == "click" && ButtonDialogResult(node) is { } closeWith)
            {
                c.UsesOnClose = true;
                var isAsync = handlerMember is not null && c.Translation?.ByMember.TryGetValue(handlerMember, out var mt) == true && mt.IsAsync;
                var close = $"onClose?.({Str(closeWith)});";
                attrs.Add(handlerName is null ? $"{ev.React}={{() => {{ {close} }}}}"
                    : isAsync ? $"{ev.React}={{async () => {{ await {handlerName}(); {close} }}}}"
                    : $"{ev.React}={{() => {{ {handlerName}(); {close} }}}}");
                continue;
            }

            // ListView selection and check state are written back to the model rows so SelectedItems / Items[i].Checked read them immediately.
            if (spec.Name == "WfListView" && ev.Name is "selectionChange" or "itemCheckedChange" && c.ListViewIds.Contains(node.Id))
            {
                c.UsesModel = true;
                var call = handlerName is null ? "" : $" {handlerName}();";
                attrs.Add(ev.Name == "selectionChange"
                    ? $"{ev.React}={{(sel) => {{ vm.{node.Id}.items.forEach((it, i) => {{ it.selected = sel.includes(i); }});{call} }}}}"
                    : $"{ev.React}={{(i, v) => {{ vm.{node.Id}.items[i].checked = v;{call} }}}}");
                continue;
            }

            var isStateEvent = StateEvents.Contains((spec.Name, ev.Name));
            if (isStateEvent && c.StatefulIds.Contains(node.Id))
            {
                c.UsesModel = true;
                var target = spec.Name == "WfCheckBox" ? $"vm.{node.Id}.checked" : $"vm.{node.Id}.text";
                attrs.Add(handlerName == null ? $"{ev.React}={{(v) => {{ {target} = v; }}}}" : $"{ev.React}={{(v) => {{ {target} = v; {handlerName}(); }}}}");
            }
            else if (handlerName != null)
                attrs.Add($"{ev.React}={{{handlerName}}}");
        }

        foreach (var (key, value) in node.Attributes)
        {
            if (!EventRegex().IsMatch(key) || !c.Methods.ContainsKey(value)) continue;
            if (spec.Events.All(e => "On" + e.From != key))
                c.Issue("event-not-mapped", $"{node.Id}.{key[2..]} -> {value} (no ui-spec event for it)");
        }
    }

    /// <summary>The DialogResult a button closes its dialog with (OK, Cancel, Yes, ...); null for None/absent.</summary>
    private static string? ButtonDialogResult(ControlNodeIr node) =>
        node.Attributes.GetValueOrDefault("DialogResult") is { Length: > 0 } raw && LastToken(raw) is var token && token != "None" ? token : null;

    private Handler GetHandler(ControlNodeIr node, string eventName, MemberIr member, Ctx c, bool isRoot)
    {
        var key = $"{node.Id}.{eventName}";
        if (c.Handlers.TryGetValue(key, out var existing)) return existing;

        var name = c.Translation is not null && c.Translation.ByMember.TryGetValue(member, out var translated)
            ? translated.TsName
            : c.Names.Unique(Naming.HandlerName(node.Id, eventName, isRoot));
        var handler = new Handler { Name = name, ControlId = node.Id, Event = eventName, Member = member, IsRoot = isRoot };
        c.Handlers[key] = handler;
        return handler;
    }

    // ------------------------------------------------------------------------------------------- handlers

    /// <summary>Functions for the component body: translated methods (and stubs for untranslated helpers) when translation is on, handler stubs otherwise.</summary>
    private List<string> BuildFunctions(Ctx c, List<string> jsx)
    {
        if (c.Translation is not { } tr) return BuildHandlers(c, c.Handlers.Values);

        var lines = new List<string>();
        foreach (var t in tr.Methods.Where(t => t.Lines.Count > 0))
        {
            // A function nothing calls (dead code in the original, or an event the library has no prop for) would fail lint: say so explicitly.
            // Only OTHER code counts: its own body (recursion, a local with the same name, a log label) is not a use.
            // A function with a parameter of the same name shadows it: its body does not use this function either.
            var shadow = new System.Text.RegularExpressions.Regex($@"^(async )?function \w+\([^)]*(?<![\w.]){System.Text.RegularExpressions.Regex.Escape(t.TsName)}\s*[?:,)]");
            var others = PageTranslator.StripStrings(string.Join("\n", jsx.Concat(tr.Methods.Where(m => m != t && !m.Lines.Any(l => shadow.IsMatch(l))).SelectMany(m => m.Lines))
                .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal))));
            var unused = !System.Text.RegularExpressions.Regex.IsMatch(others, $@"(?<![\w.]){System.Text.RegularExpressions.Regex.Escape(t.TsName)}\b");
            foreach (var l in t.Lines)
            {
                if (unused && System.Text.RegularExpressions.Regex.IsMatch(l, $@"^(async )?function {System.Text.RegularExpressions.Regex.Escape(t.TsName)}\("))
                    lines.Add("// eslint-disable-next-line @typescript-eslint/no-unused-vars -- not wired to any control or call; kept for the original logic");
                lines.Add(l);
            }
        }

        // Wired handlers the rules could not translate keep the navigation-aware stub.
        var stubs = c.Handlers.Values
            .Where(h => tr.ByMember.TryGetValue(h.Member, out var mt) && mt.Status == TranslationStatus.NotTranslated && mt.Lines.Count == 0)
            .GroupBy(h => h.Name).Select(g => g.First());
        lines.AddRange(BuildHandlers(c, stubs));
        return lines;
    }

    private static Dictionary<string, WiredHandler> WiredHandlers(Ctx c, ControlNodeIr root)
    {
        var map = new Dictionary<string, WiredHandler>(StringComparer.Ordinal);
        foreach (var node in Walk(root))
            foreach (var (key, value) in node.Attributes)
                if (EventRegex().IsMatch(key) && c.Methods.ContainsKey(value) && !map.ContainsKey(value))
                    map[value] = new WiredHandler(node.Id, key[2..], ReferenceEquals(node, root));
        return map;
    }

    private static void AddRanges(GeneratedPage page, PageTranslation tr)
    {
        var lines = page.Source.Split('\n');
        var starts = new List<(string Name, int Line)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var m = System.Text.RegularExpressions.Regex.Match(lines[i], @"^  (?:async )?function (\w+)\(");
            if (m.Success) starts.Add((m.Groups[1].Value, i + 1));
        }
        var ret = Array.FindIndex(lines, l => l.StartsWith("  return (", StringComparison.Ordinal)) + 1;
        for (var i = 0; i < starts.Count; i++)
            page.MethodRanges.Add((starts[i].Name, starts[i].Line, (i + 1 < starts.Count ? starts[i + 1].Line : ret > 0 ? ret : lines.Length) - 1));
    }

    private List<string> BuildHandlers(Ctx c, IEnumerable<Handler> handlers)
    {
        var lines = new List<string>();
        foreach (var h in handlers)
        {
            var sites = c.Unit.Page.Navigations.Where(n => n.SourceControlId == h.ControlId && n.TriggerEvent == h.Event).ToList();
            var workflow = c.Unit.Page.Workflows.FirstOrDefault(w => w.TriggerControlId == h.ControlId && w.TriggerEvent == h.Event);
            var body = new List<string>();
            var isAsync = false;

            var shows = sites.Where(s => s.TargetType is "Show" or "ShowDialog").ToList();
            var targets = shows.Select(s => s.Target).Distinct().ToList();
            if (targets.Count > 0)
            {
                var target = targets[0];
                if (shows.Any(s => s.Target == target && s.TargetType == "Show"))
                    body.Add("// TODO: Show() is modeless in WinForms; it is opened as a dialog here");

                if (_generated.Contains(target) && _components.TryGetValue(target, out var comp))
                {
                    c.PageImports[comp] = comp;
                    c.UsesDialog = true;
                    isAsync = true;
                    body.Add($"await dialog.show<unknown>((close) => <{comp} onClose={{close}} />, {{ title: {Str(_units[target].Title)} }});");
                }
                else
                {
                    c.UsesDialog = true;
                    isAsync = true;
                    body.Add($"// TODO: form {target} is outside the generated scope (see MIGRATION_REPORT.md)");
                    body.Add($"await dialog.alert({Str($"Form {target} is not part of this generated app.")});");
                    c.Issue("nav-out-of-scope", $"{h.ControlId}.{h.Event} opens {target}, which was not generated");
                }

                if (targets.Count > 1)
                {
                    body.Add($"// TODO: this handler can open {targets.Count} forms ({string.Join(", ", targets)}); which one depends on conditions that are not translated. Only {target} is wired.");
                    c.Issue("nav-multi-target", $"{h.ControlId}.{h.Event} can open {string.Join(", ", targets)}; only {target} is wired");
                }
            }
            else if (sites.Any(s => s.TargetType is "Close" or "Hide"))
            {
                c.UsesOnClose = true;
                body.Add("onClose?.();");
            }
            else if (sites.Any(s => s.TargetType == "Exit"))
            {
                body.Add("// TODO: Application.Exit() has no browser equivalent; decide what 'exit' means for this app");
            }

            lines.Add($"// {h.ControlId}.{h.Event} -> {h.Member.Name}{(workflow is { Steps.Count: > 0 } ? " (steps: " + string.Join(" -> ", workflow.Steps.Select(s => s.MethodName)) + ")" : "")}");
            lines.Add($"{(isAsync ? "async " : "")}function {h.Name}() {{");
            foreach (var b in body) lines.Add("  " + b);
            lines.Add("  // TODO: handler logic not translated (LLM translation is a later phase) — original C# below");
            lines.Add($"  // {h.Member.Type} {h.Member.Name}({string.Join(", ", h.Member.Parameters.Select(p => p.Type + " " + p.Name))})");
            foreach (var l in (h.Member.Body ?? "").Split('\n')) lines.Add("  // " + l.TrimEnd('\r'));
            lines.Add("}");
            lines.Add("");
        }
        return lines;
    }

    private static string BuildLegacy(UnitIr unit)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// Untranslated code-behind of {unit.Name}, reconstructed from the IR (comments and formatting were not preserved).");
        sb.AppendLine("// Kept so no code is silently dropped; see MIGRATION_REPORT.md.");
        sb.AppendLine();
        foreach (var m in unit.Page.Members)
        {
            var vis = m.Visibility.ToString().ToLowerInvariant();
            if (m.Kind == MemberKind.Method)
            {
                sb.AppendLine($"{vis}{(m.IsStatic ? " static" : "")} {m.Type} {m.Name}({string.Join(", ", m.Parameters.Select(p => p.Type + " " + p.Name))})");
                sb.AppendLine(m.Body ?? "{ }");
            }
            else
            {
                sb.AppendLine($"{vis}{(m.IsStatic ? " static" : "")} {m.Kind.ToString().ToLowerInvariant()} {m.Type} {m.Name};");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------------------------------------ utils

    private static bool IsStateful(string specName) => specName is "WfTextBox" or "WfComboBox" or "WfCheckBox";

    private static string ShortType(string tagName) => tagName[(tagName.LastIndexOf('.') + 1)..];

    private static string LastToken(string text) => text[(text.LastIndexOf('.') + 1)..].Trim();

    private static IEnumerable<ControlNodeIr> Walk(ControlNodeIr node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var n in Walk(child))
                yield return n;
    }

    private static List<string> Indent(IEnumerable<string> lines) => lines.Select(l => l.Length == 0 ? l : "  " + l).ToList();

    private static string Str(string value) => JsonSerializer.Serialize(value, JsonOut);

    /// <summary>Object-literal key: bare identifier when valid, quoted otherwise.</summary>
    private static string Prop(string name) => IdentifierRegex().IsMatch(name) ? name : Str(name);

    [GeneratedRegex(@"^[A-Za-z_$][\w$]*$")]
    private static partial Regex IdentifierRegex();

    /// <summary>name="value" when it is safe in a JSX attribute, otherwise name={"value"} (JSX would decode entities in plain strings).</summary>
    private static string Attr(string name, string value) =>
        SafeAttrRegex().IsMatch(value) ? $"{name}=\"{value}\"" : $"{name}={{{Str(value)}}}";

    private static bool TryJsonStringArray(string raw, out string json)
    {
        json = "";
        try
        {
            var items = JsonSerializer.Deserialize<List<string>>(raw);
            if (items is null) return false;
            json = JsonSerializer.Serialize(items, JsonOut);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9 .,:;!?'()/_\-]*$")]
    private static partial Regex SafeAttrRegex();

    [GeneratedRegex(@"Keys\.(\w+)")]
    private static partial Regex KeysRegex();

    [GeneratedRegex(@"^On[A-Z]")]
    private static partial Regex EventRegex();
}
