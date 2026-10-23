using Migrator.Engine.Projections;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Migrator.Engine.Contracts;
using Migrator.Target.Core.Translation;
using Migrator.Target.Core.Spec;
using WebFormsMigrator.IR.Models;
using Migrator.Target.Core.Ir;
using Migrator.Target.Core.Generation;

namespace Migrator.Target.Core.Generation;

/// <summary>
/// Turns one unit's IR into a React component that imports only `Wf*` components from the UI library. Markup, state, wiring and
/// navigation are deterministic; handler bodies are NOT translated (original C# stays as comments + TODO, per CLAUDE.md §6).
/// </summary>
public sealed partial class PageGenerator : IPageGenerator
{
    private static readonly HashSet<string> NonVisualTypes = new(StringComparer.Ordinal)
    {
        "ImageList", "ToolTip", "BindingSource", "Timer", "OpenFileDialog", "SaveFileDialog", "FolderBrowserDialog", "ColorDialog",
        "FontDialog", "NotifyIcon", "ErrorProvider", "BackgroundWorker", "HelpProvider", "PrintDocument", "PrintDialog",
        "PageSetupDialog", "ContextMenuStrip"
    };

    private static readonly (string Control, string Event)[] StateEvents =
    {
        ("WfTextBox", "valueChange"), ("WfComboBox", "selectionChange"), ("WfCheckBox", "checkedChange"), ("WfRadioButton", "checkedChange"),
        ("WfDateTimePicker", "valueChange"), ("WfNumericUpDown", "valueChange"), ("WfTabs", "selectionChange"),
        ("WfMaskedTextBox", "valueChange"), ("WfRichTextBox", "valueChange"), ("WfMonthCalendar", "valueChange"), ("WfListBox", "selectionChange"),
        ("WfDomainUpDown", "selectionChange"), ("WfCheckedListBox", "selectionChange"), ("WfTrackBar", "valueChange"), ("WfScrollBar", "valueChange")
    };

    private static readonly JsonSerializerOptions JsonOut = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly SpecCatalog _spec;
    private readonly IReadOnlyDictionary<string, UnitIr> _units;
    private readonly IReadOnlyDictionary<string, string> _components;
    private readonly ISet<string> _generated;
    private readonly TranslationEnv? _translation;
    private readonly bool _preview;

    /// <summary>How a page becomes files for a framework other than React; null writes the React pair (markup + code-behind hook).</summary>
    private readonly IPageAssembler? _assembler;

    /// <summary>App shell only: a visual-only page still opens the forms its buttons and menu items open (and closes itself), through the dialog service. Nothing else runs.</summary>
    private readonly bool _previewNavigation;

    /// <summary>Helpers of the generated `src/lib/csharp.ts`, imported by whichever file calls them.</summary>

    /// <param name="preview">Visual-only pages for the component viewer: controls and layout as designed with placeholder data; no handlers, model, dialogs, translation or imports other than the UI library.</param>
    public PageGenerator(SpecCatalog spec, IReadOnlyDictionary<string, UnitIr> units, IReadOnlyDictionary<string, string> components, ISet<string> generated, TranslationEnv? translation = null, bool preview = false, bool previewNavigation = false, IPageAssembler? assembler = null)
    {
        _assembler = assembler;
        _preview = preview;
        _previewNavigation = previewNavigation;
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

        /// <summary>`designer`, or `code` when the wiring was read from a subscription in the code-behind.</summary>
        public string Source = "designer";
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
        public HashSet<string> TreeViewIds { get; } = new();
        /// <summary>Grids the code-behind mentions: their columns, rows and selection live in the model (`vm.<id>.columns`, `.rows`, `.selected`).</summary>
        public HashSet<string> GridIds { get; } = new();
        /// <summary>ListViews that nothing in the code or the events touches: rendered as a grid with the designer columns and sample rows.</summary>
        public HashSet<string> ListGrids { get; } = new();
        public Dictionary<string, bool> VisualLists { get; } = new();
        public HashSet<string> EditableGrids { get; } = new();
        public HashSet<string> ComboIds { get; } = new();
        /// <summary>Containers the code adds user controls to: their `controls` list lives in the model and a WfDynamicHost renders it after the designer children.</summary>
        public HashSet<string> DynamicIds { get; } = new();
        /// <summary>ImageList id -> its images as web resources (keys and urls); written under `public/images/{unit}/{id}/`.</summary>
        public Dictionary<string, (List<string> Keys, List<string> Urls)> ImageLists { get; } = new(StringComparer.Ordinal);

        /// <summary>Control id -> id of its parent control (radio buttons of one container form a group).</summary>
        public Dictionary<string, string> ParentOf { get; } = new(StringComparer.Ordinal);

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
        public List<(string Path, byte[] Data)> Assets { get; } = new();
        public int IconsExtracted { get; set; }
        public int IconsConverted { get; set; }
        public int IconsPlaceholder { get; set; }

        public void Issue(string category, string message) => Issues.Add(new GenerationIssue(Unit.Name, category, message));
    }

    public GeneratedPage Generate(UnitIr unit, ISet<string>? demoted = null)
    {
        if (_preview) return GeneratePreview(unit);

        var root = unit.Page.RootControl ?? throw new InvalidDataException($"{unit.Name} has no control tree.");
        var component = _components[unit.Name];
        var c = new Ctx(unit, component);
        ApplyFallbacks(c, root);
        foreach (var n in Walk(root)) c.ById.TryAdd(n.Id, n);
        FillParents(c, root);
        Prescan(c, root);
        PrepareImageLists(c, root);
        if (_translation is not null)
            c.Translation = PageTranslator.Translate(unit, c.Names, _translation, WiredHandlers(c, root), demoted ?? new HashSet<string>());

        var jsx = RenderElement(root, c, isRoot: true) ?? throw new InvalidDataException($"{unit.Name}: root control produced no output.");
        var handlerCode = BuildFunctions(c, jsx);
        SeedListViewSamples(c, handlerCode);
        SeedTreeViewSamples(c, handlerCode);
        SeedGridSamples(c, handlerCode);
        var usesFields = false;
        string? serviceImport = null;
        List<string> typeImports = new();
        if (c.Translation is { } tr)
        {
            foreach (var name in tr.ResourceRefs)
                if (_translation!.Resources.WebPath(name) is { } resourcePath && _translation.Resources.TryGet(name, out var resourceData, out var resourceFormat)
                    && IconConverter.ToWeb(resourceData, resourceFormat) is { } resource)
                    c.Assets.Add(("public/" + resourcePath, resource.Data));

            // Everything the page declares or imports is decided from the text that is actually emitted: a method demoted by the
            // compile gate, or a statement that fell back, must not leave an unused `dialog`, `vm`, import or type behind.
            var fieldText = string.Join("\n", tr.FieldInitials.Values);
            var text = string.Join("\n", jsx.Concat(handlerCode).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            c.UsesDialog = Regex.IsMatch(text, @"\bdialog\.");
            c.UsesOnClose = Regex.IsMatch(text, @"(?<![\w.])onClose\s*(\?\.|\()");
            c.UsesModel = Regex.IsMatch(text, @"\bvm\.");
            usesFields = Regex.IsMatch(text, @"\bf\.");
            foreach (var comp in tr.PageImports.Where(p => UsesComponent(text, p))) c.PageImports[comp] = comp;
            foreach (var comp in c.PageImports.Keys.ToList())
                if (!UsesComponent(text, comp)) c.PageImports.Remove(comp);
            var instance = _translation!.Vocabulary.InstanceName;
            if (instance.Length > 0 && Regex.IsMatch(text, $@"\b{Regex.Escape(instance)}\."))
                serviceImport = $"import {{ {instance} }} from '{_translation.Vocabulary.ServiceImport}';";
            var propsText = string.Join("\n", tr.CtorProps.Select(p => p.Type + " " + p.Default));
            var typeText = text + "\n" + (usesFields ? fieldText : "") + "\n" + propsText;
            typeImports = tr.TypeImports.Where(t => Regex.IsMatch(typeText, $@"\b{Regex.Escape(t)}\b")).ToList();
            tr.UsesFormat = Regex.IsMatch(text, @"\bformatString\(");
            foreach (var helper in new[] { "wfListItem", "wfListItemFromArray" }) if (Regex.IsMatch(text, $@"\b{helper}\(")) c.Lib.Add(helper);
            if (Regex.IsMatch(text + "\n" + (usesFields ? fieldText : ""), @"\bWfListItem\b")) c.LibTypes.Add("WfListItem");
            foreach (var helper in new[] { "wfImageListAdd", "wfImageListClear", "wfImageListRemoveAt", "wfImageListRemoveByKey", "wfImageListUrl", "wfPlaceholderImage", "wfDynamicControl", "wfTreeNode", "wfSetExpanded", "wfSetItemChecked", "wfGridAddColumn", "wfGridAddRow", "wfGridClearRows", "wfGridRemoveRow", "wfGridCell", "wfGridSetCell" }) if (Regex.IsMatch(text, $@"\b{helper}\(")) c.Lib.Add(helper);
            if (Regex.IsMatch(text + "\n" + (usesFields ? fieldText : ""), @"\bWfTreeNode\b")) c.LibTypes.Add("WfTreeNode");
        }

        if (_assembler is not null)
        {
            var input = new PageAssemblyInput
            {
                UnitName = unit.Name,
                RelativePath = unit.Page.RelativePath ?? "n/a",
                Component = component,
                View = jsx,
                HandlerCode = handlerCode,
                TopLevel = c.TopLevel,
                CtorProps = c.Translation?.CtorProps ?? new List<CtorProp>(),
                UsedProps = !usesFields ? new List<CtorProp>() : (c.Translation?.CtorProps ?? new List<CtorProp>()).Where(p => c.Translation!.FieldInitials.Values.Any(v => v == p.Local)).ToList(),
                CtorNotes = c.Translation?.CtorNotes ?? new List<string>(),
                UsesDialog = c.UsesDialog,
                UsesModel = c.UsesModel,
                UsesFields = usesFields,
                Model = c.Model.Select(kv => new ModelEntry(kv.Key, kv.Value.ToList())).ToList(),
                ModelComments = c.ModelComments,
                FieldInitials = usesFields ? c.Translation!.FieldInitials.ToList() : new List<KeyValuePair<string, string>>(),
                Wiring = c.Handlers.Values.OrderBy(h => h.ControlId, StringComparer.Ordinal).ThenBy(h => h.Event, StringComparer.Ordinal)
                    .Select(h => new WiringEntry(h.ControlId, h.Event, h.Name, h.Member.Name, h.Source)).ToList(),
                Lib = c.Lib,
                LibTypes = c.LibTypes,
                PageImports = c.PageImports.Keys.ToList(),
                TypeImports = typeImports,
                ServiceImport = serviceImport,
                ServiceInstance = _translation?.Vocabulary.InstanceName ?? "",
                TypesImportPath = _translation?.Vocabulary.TypesImport ?? "",
                FixturesImportPath = c.Translation?.FixturesImport ?? "",
                NeedsFixtures = c.Translation is { FixturesImport.Length: > 0 } fixtures && usesFields && fixtures.CtorProps.Any(p => p.Default != null && p.Default.Contains("fixtures.") && fixtures.FieldInitials.Values.Contains(p.Local)),
                SupportClasses = _translation?.Support?.Classes.Keys.ToList() ?? new List<string>()
            };
            var files = _assembler.Assemble(input);
            c.Issues.AddRange(input.Issues);
            return FinishPage(c, unit, component, root, files.View, files.Code, files.Component);
        }

        // ---- the page is two files, like the WinForms original: the JSX ("Designer") and the code-behind hook that holds model, handlers and event wiring.
        var ctorProps = c.Translation?.CtorProps ?? new List<CtorProp>();
        // Only props that seed a field are destructured (an unused binding fails lint); all stay in the interface.
        var usedProps = !usesFields ? new List<CtorProp>()
            : ctorProps.Where(p => c.Translation!.FieldInitials.Values.Any(v => v == p.Local)).ToList();
        var hookName = $"use{component}Code";
        var codeFile = $"./{component}.code";

        // ---- code-behind body (everything but the imports)
        var body = new List<string>();
        foreach (var t in c.TopLevel) body.Add(t.StartsWith("const ", StringComparison.Ordinal) ? "export " + t : t);
        if (c.TopLevel.Count > 0) body.Add("");
        // A dialog closes itself with dialog.close(), so a page takes props only for the constructor arguments of the original form.
        if (ctorProps.Count > 0)
        {
            body.Add($"export interface {component}Props {{");
            foreach (var p in ctorProps) body.Add($"  /** Constructor argument of the original form. */ {Prop(p.Key)}?: {p.Type};");
            body.Add("}");
            body.Add("");
        }
        body.Add("/** Event wiring of the original form (control.event -> handler). The markup binds these handlers by name; kept here so the wiring is easy to trace back to the WinForms code-behind. */");
        body.Add("export const wiring = [");
        foreach (var h in c.Handlers.Values.OrderBy(h => h.ControlId, StringComparer.Ordinal).ThenBy(h => h.Event, StringComparer.Ordinal))
            body.Add($"  {{ control: {Str(h.ControlId)}, event: {Str(h.Event)}, handler: {Str(h.Name)}, method: {Str(h.Member.Name)}, source: '{h.Source}' }},");
        body.Add("] as const;");
        body.Add("");

        var bindings = new List<string>();
        foreach (var p in usedProps)
            bindings.Add((p.Key == p.Local ? p.Key : $"{p.Key}: {p.Local}") + (p.Default is null ? "" : $" = {p.Default}"));
        if (usedProps.Any(p => p.Default?.Contains("fixtures.") == true))
            body.Add("// MOCK — constructor-argument defaults are fixture rows so the page opens on its own; the opener passes the real values");
        body.Add(bindings.Count > 0 ? $"export function {hookName}({{ {string.Join(", ", bindings)} }}: {component}Props) {{"
            : ctorProps.Count > 0 ? $"export function {hookName}(_props: {component}Props) {{" : $"export function {hookName}() {{");
        foreach (var note in c.Translation?.CtorNotes ?? new List<string>())
            body.Add($"  // TODO: constructor logic not translated: {note}");
        if (c.UsesDialog) body.Add("  const dialog = useWfDialog();");
        foreach (var s in c.States) body.Add("  " + s);
        if (c.UsesModel)
        {
            foreach (var comment in c.ModelComments) body.Add("  " + comment);
            body.Add("  const vm = useWfModel({");
            foreach (var (id, props) in c.Model)
                body.Add($"    {Prop(id)}: {{ {string.Join(", ", props.Select(kv => kv.Value.StartsWith("@raw:", StringComparison.Ordinal) ? kv.Value[5..] : $"{kv.Key}: {kv.Value}"))} }},");
            body.Add("  });");
        }
        if (usesFields)
        {
            body.Add("  // Fields of the original form. Initial values come from their C# initializers (a default for the declared type otherwise).");
            body.Add("  const f = useWfModel({");
            foreach (var (name, initial) in c.Translation!.FieldInitials) body.Add($"    {Prop(name)}: {initial},");
            body.Add("  });");
        }
        if (c.UsesDialog || c.States.Count > 0 || c.UsesModel || usesFields) body.Add("");
        foreach (var line in handlerCode) body.Add(line.Length == 0 ? "" : "  " + line);

        // What the markup needs from the hook: the model, field model, dialog service, state, props and handlers its JSX mentions.
        var viewText = PageTranslator.StripStrings(string.Join("\n", jsx.Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal))));
        viewText = Regex.Replace(viewText, @"/\*.*?\*/", "");
        var provided = new List<string>();
        if (c.UsesModel) provided.Add("vm");
        if (usesFields) provided.Add("f");
        if (c.UsesDialog) provided.Add("dialog");
        provided.AddRange(c.States.SelectMany(s => Regex.Match(s, @"const \[(\w+), (\w+)\]") is { Success: true } m ? new[] { m.Groups[1].Value, m.Groups[2].Value } : Array.Empty<string>()));
        provided.AddRange(usedProps.Select(p => p.Local));
        provided.AddRange(handlerCode.Select(l => Regex.Match(l, @"^(?:async )?function (\w+)\(")).Where(m => m.Success).Select(m => m.Groups[1].Value));
        var needed = provided.Distinct().Where(n => Regex.IsMatch(viewText, $@"(?<![\w.$]){Regex.Escape(n)}\b")).ToList();
        body.Add(needed.Count > 0 ? $"  return {{ {string.Join(", ", needed)} }};" : "  return {};");
        body.Add("}");

        // ---- imports, decided per file from the text that file really contains
        string Code(IEnumerable<string> lines) => Regex.Replace(PageTranslator.StripStrings(string.Join("\n", lines.Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)))), @"/\*.*?\*/", "");
        var codeText = Code(body);
        var topLevelNames = c.TopLevel.Select(t => Regex.Match(t, @"^const (\w+)")).Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();
        var fromCode = topLevelNames.Where(n => Regex.IsMatch(viewText, $@"(?<![\w.$]){Regex.Escape(n)}\b")).ToList();

        string Uses(string name, string text) => Regex.IsMatch(text, $@"(?<![\w.$]){Regex.Escape(name)}\b") ? name : "";
        IEnumerable<string> SupportUsed(string text) => _translation?.Support is { } sr
            ? sr.Classes.Keys.Where(n => Regex.IsMatch(text, $@"(?<![\w.]){Regex.Escape(n)}\b")).OrderBy(n => n, StringComparer.Ordinal) : Enumerable.Empty<string>();

        var code = new StringBuilder();
        code.AppendLine($"// Code-behind of {unit.Name} ({unit.Page.RelativePath ?? "n/a"}), generated by Migrator.Target.React: model, handlers, translated methods and the event wiring table. The markup is in {component}.tsx. Not final: see MIGRATION_REPORT.md.");
        if (c.States.Count > 0) code.AppendLine("import { useState } from 'react';");
        var codeLib = new List<string>();
        codeLib.AddRange(c.Lib.Where(n => char.IsLower(n[0]) && Regex.IsMatch(codeText, $@"(?<![\w.$]){Regex.Escape(n)}\b")));
        if (c.UsesDialog) codeLib.Add("useWfDialog");
        if (c.UsesModel || usesFields) codeLib.Add("useWfModel");
        codeLib.AddRange(c.LibTypes.Where(t => Regex.IsMatch(codeText, $@"\b{Regex.Escape(t)}\b")).Select(t => "type " + t));
        if (codeLib.Count > 0) code.AppendLine($"import {{ {string.Join(", ", codeLib)} }} from '@wfm/ui-react';");
        foreach (var (comp, file) in c.PageImports.Where(p => UsesComponent(codeText, p.Key))) code.AppendLine($"import {{ {comp} }} from './{file}';");
        var csharpHelpers = CsharpRuntime.Helpers.Where(h => Regex.IsMatch(codeText, $@"\b{h}\(")).ToList();
        if (csharpHelpers.Count > 0) code.AppendLine($"import {{ {string.Join(", ", csharpHelpers)} }} from '../lib/csharp';");
        // Only code counts for the support classes: a model key or a column field with the same name as a class is not a use.
        var supportCode = SupportUsed(Code(handlerCode
            .Concat(usesFields ? c.Translation!.FieldInitials.Values : Enumerable.Empty<string>())
            .Concat((c.Translation?.CtorProps ?? new List<CtorProp>()).Select(p => p.Type + " " + p.Default)))).ToList();
        if (supportCode.Count > 0) code.AppendLine($"import {{ {string.Join(", ", supportCode)} }} from '../support';");
        if (c.Translation is { FixturesImport.Length: > 0 } fx && usesFields && fx.CtorProps.Any(p => p.Default != null && p.Default.Contains("fixtures.") && fx.FieldInitials.Values.Contains(p.Local)))
            code.AppendLine($"import {{ fixtures }} from '{fx.FixturesImport}';");
        if (serviceImport is not null && _translation is not null && Regex.IsMatch(codeText, $@"\b{Regex.Escape(_translation.Vocabulary.InstanceName)}\."))
            code.AppendLine(serviceImport);
        var typesUsed = typeImports.Where(t => Regex.IsMatch(codeText, $@"\b{Regex.Escape(t)}\b")).ToList();
        if (typesUsed.Count > 0 && _translation is not null)
            code.AppendLine($"import type {{ {string.Join(", ", typesUsed)} }} from '{_translation.Vocabulary.TypesImport}';");
        code.AppendLine();
        foreach (var line in body) code.AppendLine(line);

        var sb = new StringBuilder();
        sb.AppendLine($"// Generated by Migrator.Target.React from {unit.Name} ({unit.Page.RelativePath ?? "n/a"}). Markup only: the code-behind (model, handlers, event wiring) is in {component}.code.tsx. Not final: see MIGRATION_REPORT.md.");
        var viewLib = c.Lib.Where(n => char.IsUpper(n[0]) || Regex.IsMatch(viewText, $@"(?<![\w.$]){Regex.Escape(n)}\b")).ToList();
        if (viewLib.Count > 0) sb.AppendLine($"import {{ {string.Join(", ", viewLib)} }} from '@wfm/ui-react';");
        foreach (var (comp, file) in c.PageImports.Where(p => UsesComponent(viewText, p.Key))) sb.AppendLine($"import {{ {comp} }} from './{file}';");
        var supportView = SupportUsed(viewText).ToList();
        if (supportView.Count > 0) sb.AppendLine($"import {{ {string.Join(", ", supportView)} }} from '../support';");
        var fromHook = new List<string> { hookName };
        fromHook.AddRange(fromCode);
        if (ctorProps.Count > 0) fromHook.Add($"type {component}Props");
        sb.AppendLine($"import {{ {string.Join(", ", fromHook)} }} from '{codeFile}';");
        sb.AppendLine();
        sb.AppendLine(ctorProps.Count > 0 ? $"export function {component}(props: {component}Props) {{" : $"export function {component}() {{");
        sb.AppendLine(needed.Count > 0 ? $"  const {{ {string.Join(", ", needed)} }} = {hookName}({(ctorProps.Count > 0 ? "props" : "")});" : $"  {hookName}({(ctorProps.Count > 0 ? "props" : "")});");
        sb.AppendLine("  return (");
        foreach (var line in jsx) sb.AppendLine("    " + line);
        sb.AppendLine("  );");
        sb.AppendLine("}");

        return FinishPage(c, unit, component, root, sb.ToString(), code.ToString(), "");
    }

    /// <summary>The page object: its files (as the framework wrote them), the counts the report needs and the method ranges the compile gate maps errors through.</summary>
    private GeneratedPage FinishPage(Ctx c, UnitIr unit, string component, ControlNodeIr root, string source, string codeSource, string componentSource)
    {
        var navHandlers = c.Handlers.Values.Count(h => unit.Page.Navigations.Any(n => n.SourceControlId == h.ControlId && n.TriggerEvent == h.Event));
        var page = new GeneratedPage
        {
            ComponentName = component,
            Source = source,
            CodeSource = codeSource,
            ComponentSource = componentSource,
            LegacySource = BuildLegacy(unit),
            Controls = Walk(root).Count(),
            Handlers = c.Handlers.Count,
            NavigationHandlers = navHandlers,
            NonVisual = c.NonVisual,
            Assets = c.Assets,
            IconsExtracted = c.IconsExtracted,
            IconsConverted = c.IconsConverted,
            IconsPlaceholder = c.IconsPlaceholder
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
            page.Bypassed.AddRange(stats.Methods.SelectMany(m => m.Bypasses));
            AddRanges(page, stats);
        }
        return page;
    }

    // ------------------------------------------------------------------------------------------------ elements

    private List<string>? RenderElement(ControlNodeIr node, Ctx c, bool isRoot = false)
    {
        var type = ShortType(node.Effective());
        if (!isRoot && NonVisualTypes.Contains(type))
        {
            c.NonVisual.Add($"{node.Id} ({type})");
            return null;
        }

        // A grid column the designer declares but never attaches to a grid (the grid binds its columns at runtime): inert leftover, not a missing control.
        if (!isRoot && type.StartsWith("DataGridView", StringComparison.Ordinal) && type.EndsWith("Column", StringComparison.Ordinal)
            && node.Attributes.GetValueOrDefault("ParentSlot") != "Columns")
        {
            c.NonVisual.Add($"{node.Id} ({type}, not attached to a grid)");
            return null;
        }

        var spec = isRoot ? RootSpec(node) : _spec.Find(node.Effective());
        if (!isRoot && spec?.Name == "WfListView" && IsVisualOnlyList(node, c) && _spec.Find("System.Windows.Forms.DataGridView") is { } gridSpec)
        {
            spec = gridSpec;
            c.ListGrids.Add(node.Id);
            c.Issue("listview-as-grid", $"{node.Id}: a ListView that no code or event uses is rendered as a grid with its designer columns and sample rows");
        }
        if (spec is null) return RenderFallback(node, c, type);

        c.Lib.Add(spec.Name);
        var attrs = new List<string>();
        if (node.Attributes.TryGetValue("Fallback.From", out var fallbackFrom)) attrs.Add($"/* FALLBACK: {ShortType(fallbackFrom)} */");
        BuildProps(node, spec, c, attrs, isRoot);
        if (!_preview) BuildEvents(node, spec, c, attrs, isRoot);
        else if (_previewNavigation) AddPreviewNavigation(node, spec, c, attrs, isRoot);
        var children = RenderChildren(node, spec, c);
        var lines = Element(spec.Name, attrs, children);

        // ContextMenuStrip="this.contextMenuStrip1" -> wrap the control.
        if (!isRoot && node.Attributes.TryGetValue("ContextMenuStrip", out var cm)
            && c.ById.TryGetValue(LastToken(cm), out var menuNode) && ShortType(menuNode.Effective()) == "ContextMenuStrip")
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
        if (!_preview && !isRoot && spec.Name != "WfTabPage" && c.ModelHas(node.Id, "Visible"))
        {
            c.UsesModel = true;
            lines = new List<string> { $"{{vm.{node.Id}.visible && (" }.Concat(Indent(lines)).Append(")}").ToList();
        }
        return lines;
    }

    /// <summary>
    /// A control with no library component is rendered by the closest available one (ui-spec fallbacks.json; default: a panel when it has children, else a label).
    /// The node is rewritten in memory: its resolved type becomes the replacement's type, aliased properties/events are copied, and `Fallback.From` / `Fallback.Alias.*`
    /// remember what happened (so a regeneration reports it again and the translator reads the right member).
    /// </summary>
    private void ApplyFallbacks(Ctx c, ControlNodeIr root)
    {
        foreach (var node in Walk(root).Skip(1))
        {
            if (node.Attributes.TryGetValue("Fallback.From", out var already))
            {
                c.Issue("fallback-control", $"{node.Id}: {ShortType(already)} rendered as {_spec.Find(node.Effective())?.Name}");
                continue;
            }

            var effective = node.Effective();
            var shortType = ShortType(effective);
            if (_spec.Find(effective) is not null || NonVisualTypes.Contains(shortType) || node.Attributes.GetValueOrDefault("ParentSlot") == "Columns") continue;
            if (shortType.StartsWith("DataGridView", StringComparison.Ordinal) && shortType.EndsWith("Column", StringComparison.Ordinal)) continue;
            if (effective.EndsWith("UserControl", StringComparison.Ordinal) && node.Children.Count > 0) continue; // inlined user controls render as panels (RenderFallback)
            if (effective.StartsWith("System.", StringComparison.Ordinal) && !effective.StartsWith("System.Windows.Forms.", StringComparison.Ordinal)) continue; // non-visual component

            var fallback = _spec.FindFallback(effective);
            var use = fallback?.Use ?? (node.Children.Count > 0 ? _spec.DefaultWithChildren : _spec.DefaultOther);
            if (_spec.ByName(use) is not { WinformsTypes.Length: > 0 } target) continue;

            foreach (var (from, to) in fallback?.PropertyAliases ?? new Dictionary<string, string>())
            {
                if (node.Attributes.TryGetValue(from, out var value) && !node.Attributes.ContainsKey(to)) node.Attributes[to] = value;
                node.Attributes["Fallback.Alias." + from] = to;
            }
            foreach (var (from, to) in fallback?.EventAliases ?? new Dictionary<string, string>())
                if (node.Attributes.TryGetValue("On" + from, out var handler) && !node.Attributes.ContainsKey("On" + to)) node.Attributes["On" + to] = handler;

            node.Attributes["Fallback.From"] = effective;
            node.Attributes[ControlTypes.ResolvedTypeKey] = target.WinformsTypes[0];
            if (use == "WfLabel" && !node.Attributes.ContainsKey("Text")) node.Attributes["Text"] = $"[{shortType}]";
            c.Issue("fallback-control", $"{node.Id}: {shortType} rendered as {use}{(fallback?.Note is { } note ? $" ({note})" : "")}");
        }
    }

    private SpecControl? RootSpec(ControlNodeIr root)
    {
        var spec = _spec.Find(root.Effective());
        if (spec != null) return spec;
        var name = root.Effective().EndsWith("UserControl", StringComparison.Ordinal) ? "WfPanel" : "WfForm"; // custom base classes behave as Form
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
            "WfForm" or "WfPanel" or "WfGroupBox" or "WfTabPage" => WithDynamicHost(node, c, RenderArranged(children, c)),
            _ => RenderSequential(children, c)
        };
    }

    /// <summary>Controls the code adds at run time (`panel.Controls.Add(new X())`) render after the designer children.</summary>
    private List<string> WithDynamicHost(ControlNodeIr node, Ctx c, List<string> lines)
    {
        if (_preview || !c.DynamicIds.Contains(node.Id)) return lines;
        c.Lib.Add("WfDynamicHost");
        c.UsesModel = true;
        lines.Add($"<WfDynamicHost controls={{vm.{node.Id}.controls}} />");
        return lines;
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
            var spec = _spec.Find(node.Effective());
            if (spec is not null && IsStateful(spec.Name))
            {
                c.StatefulIds.Add(node.Id);
                AddToModel(c, node, StatefulProp(spec.Name));
            }
            if (node.Attributes.GetValueOrDefault("Visible") == "false") AddToModel(c, node, "Visible");
            if (spec?.Name == "WfListView" && !IsVisualOnlyList(node, c)) { c.ListViewIds.Add(node.Id); AddListViewModel(c, node); }
            if (spec?.Name == "WfTreeView") { c.TreeViewIds.Add(node.Id); AddTreeViewModel(c, node); }
            // Items/selection behave like a combo box; the checked state of a CheckedListBox is its own model entry.
            if (spec?.Name is "WfCheckedListBox" or "WfDomainUpDown" && c.ComboIds.Add(node.Id)) AddComboModel(c, node);
            if (spec?.Name == "WfCheckedListBox") c.ModelSet(node.Id, "CheckedIndices", "[] as number[]");
        }

        foreach (var host in ContainerUsage.Hosts(c.Unit))
            if (c.ById.TryGetValue(host, out var hostNode) && _spec.Find(hostNode.Effective())?.Name is "WfPanel" or "WfGroupBox" or "WfTabPage")
            {
                c.DynamicIds.Add(host);
                c.LibTypes.Add("WfDynamicControl");
                c.ModelSet(host, "Controls", "[] as WfDynamicControl[]");
            }
        // A tab control the code drives through TabPages / TabCount / SelectedTab: its pages show and hide through the model, and the selected page is an index.
        foreach (var (tab, selectedUsed) in ContainerUsage.TabUsage(c.Unit))
        {
            if (!c.ById.TryGetValue(tab, out var tabNode) || tabNode.Effective() != ContainerUsage.TabControlType) continue;
            c.StatefulIds.Add(tab);
            AddToModel(c, tabNode, "SelectedIndex");
            foreach (var page in tabNode.Children.Where(ch => ch.Effective() == ContainerUsage.TabPageType))
            {
                AddToModel(c, page, "Visible");
                if (selectedUsed) AddToModel(c, page, "Text");
            }
        }

        var code = string.Join("\n", c.Unit.Page.Members.Where(m => m.Kind == MemberKind.Method && m.Body != null).Select(m => m.Body));
        foreach (Match m in ComboUsageRegex().Matches(code))
            if (c.ById.TryGetValue(m.Groups[1].Value, out var comboNode) && comboNode != root && _spec.Find(comboNode.Effective())?.Name is "WfComboBox" or "WfListBox" && c.ComboIds.Add(comboNode.Id))
                AddComboModel(c, comboNode);
        foreach (Match m in UsedPropertyRegex().Matches(code))
        {
            var id = m.Groups[1].Value == "this" ? root.Id : m.Groups[1].Value;
            if (!c.ById.TryGetValue(id, out var node)) continue;
            var prop = node.Attributes.GetValueOrDefault("Fallback.Alias." + m.Groups[2].Value) ?? m.Groups[2].Value;
            if (prop == "SelectedIndex")
            {
                // Only a tab control keeps its selected page in the model (a combo box's SelectedIndex is not supported yet).
                if (_spec.Find(node.Effective())?.Name == "WfTabs") { c.StatefulIds.Add(node.Id); AddToModel(c, node, prop); }
                continue;
            }
            if (node != root || prop is "Text" or "Enabled" or "Visible") AddToModel(c, node, prop);
        }
    }

    /// <summary>
    /// A ListView whose rows/columns the translated code never writes (the original fills it in code that stayed commented) would render empty.
    /// It gets clearly marked sample rows (and sample columns when the designer has none) so the control is visible and testable.
    /// </summary>
    private static void SeedListViewSamples(Ctx c, List<string> handlerCode)
    {
        if (c.ListViewIds.Count == 0) return;
        var code = string.Join("\n", handlerCode.Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        foreach (var id in c.ListViewIds)
        {
            var key = Regex.Escape(id);
            var writesItems = Regex.IsMatch(code, $@"\bvm\.{key}\.items(\.(push|splice)\b|\s*=[^=])");
            var writesColumns = Regex.IsMatch(code, $@"\bvm\.{key}\.columns(\.(push|splice)\b|\s*=[^=])");
            if (writesItems) continue;

            var model = c.Model[id];
            var designerColumns = Regex.Matches(model["columns"], @"header:").Count;
            var count = designerColumns > 0 ? designerColumns : 3;
            if (designerColumns == 0 && !writesColumns)
                model["columns"] = "[" + string.Join(", ", Enumerable.Range(1, count).Select(i => $"{{ header: 'Column {i}' }}")) + "] as WfListColumn[]";
            var rows = Enumerable.Range(1, 3).Select(r => "wfListItemFromArray([" + string.Join(", ", Enumerable.Range(1, count).Select(i => $"'Sample {r}.{i}'")) + "])");
            model["items"] = "[" + string.Join(", ", rows) + "] as WfListItem[]";
            c.Lib.Add("wfListItemFromArray");
            c.ModelComments.Add($"// MOCK — {id} rows are sample data: the original fills it in code that is not translated; replace with real implementation");
        }
    }

    /// <summary>
    /// A combo box whose items or selection code touches keeps `items` and `selectedIndex` in the model; `text` / `selected` are accessors over them, so writing
    /// either one (or the index) keeps the others consistent and the control shows the same state. Clear is a model method.
    /// </summary>
    private static void AddComboModel(Ctx c, ControlNodeIr node)
    {
        var items = TryJsonStringArray(node.Attributes.GetValueOrDefault("Items") ?? "", out var json) ? json : "[]";
        var text = Str(node.Attributes.GetValueOrDefault("Text") ?? "");
        c.Model.TryGetValue(node.Id, out var existing);
        existing?.Remove("text");
        c.ModelSet(node.Id, "Items", $"{items} as string[]");
        c.ModelSet(node.Id, "SelectedIndex", "-1");
        c.ModelSet(node.Id, "_text", text);
        c.ModelSet(node.Id, "text", "@raw:get text(): string { return this.selectedIndex >= 0 ? this.items[this.selectedIndex] : this._text; }, set text(v: string) { this._text = v; this.selectedIndex = this.items.indexOf(v); }");
        c.ModelSet(node.Id, "selected", "@raw:get selected(): string | null { return this.selectedIndex >= 0 ? this.items[this.selectedIndex] : null; }, set selected(v: string | null) { this.text = v ?? ''; }");
        c.ModelSet(node.Id, "clear", "@raw:clear(): void { this.items.splice(0); this.selectedIndex = -1; this._text = ''; }");
    }

    [GeneratedRegex(@"\b(\w+)\s*\.\s*(Items|SelectedIndex|SelectedItem|SelectedValue)\b")]
    private static partial Regex ComboUsageRegex();

    /// <summary>Columns (from the designer's ColumnHeader children) and items live in the model so code can Columns.Add / Items.Add / SelectedItems.</summary>
    private static void AddListViewModel(Ctx c, ControlNodeIr node)
    {
        c.LibTypes.Add("WfListColumn");
        c.LibTypes.Add("WfListItem");
        c.ModelSet(node.Id, "Columns", $"[{string.Join(", ", ListColumnDefs(node))}] as WfListColumn[]");
        c.ModelSet(node.Id, "Items", "[] as WfListItem[]");
    }

    /// <summary>`{ header, width }` literals of a ListView's designer ColumnHeader children.</summary>
    private static List<string> ListColumnDefs(ControlNodeIr node) =>
        node.Children.Where(ch => ch.Attributes.GetValueOrDefault("ParentSlot") == "Columns")
            .Select(ch => $"{{ header: {Str(ch.Attributes.GetValueOrDefault("Text") ?? ch.Id)}{(int.TryParse(ch.Attributes.GetValueOrDefault("Width"), out var w) ? $", width: {w}" : "")} }}").ToList();

    private static void AddToModel(Ctx c, ControlNodeIr node, string winFormsProp)
    {
        var raw = node.Attributes.GetValueOrDefault(winFormsProp);
        var initial = winFormsProp switch
        {
            "Text" => Str(raw ?? ""),
            "Enabled" or "Visible" => raw == "false" ? "false" : "true",
            "Checked" or "ReadOnly" => raw == "true" ? "true" : "false",
            "Value" when node.Effective().EndsWith("DateTimePicker", StringComparison.Ordinal) => "new Date().toISOString()",
            "Value" => double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0",
            "SelectionStart" => "new Date().toISOString().slice(0, 10)",
            "SelectedIndex" => int.TryParse(raw, out var index) ? index.ToString() : "0",
            _ => "null"
        };
        c.ModelSet(node.Id, winFormsProp, initial);
    }

    [GeneratedRegex(@"DateTime\(\s*(\d{1,4})\s*,\s*(\d{1,2})\s*,\s*(\d{1,2})")]
    private static partial Regex DateLiteralRegex();

    [GeneratedRegex(@"\b(\w+)\s*\.\s*(Text|Enabled|Visible|Checked|Value|ReadOnly|SelectedIndex)\b")]
    private static partial Regex UsedPropertyRegex();

    // ------------------------------------------------------------------------------------------------ props

    private void BuildProps(ControlNodeIr node, SpecControl spec, Ctx c, List<string> attrs, bool isRoot)
    {
        if (c.ButtonIds.Contains(node.Id)) attrs.Add($"id={Str(node.Id)}");

        foreach (var prop in spec.Props.Where(p => p.From.Length > 0))
        {
            if (prop.Name == "visible")
            {
                // handled by the Visible wrapper; a tab page keeps its place in the tab strip and only hides its tab
                if (spec.Name == "WfTabPage" && !_preview && c.ModelHas(node.Id, "Visible")) { c.UsesModel = true; attrs.Add($"visible={{vm.{node.Id}.visible}}"); }
                continue;
            }
            if (prop.Name == "icon") { if (IconAttr(node, c) is { } iconAttr) attrs.Add(iconAttr); continue; }

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

        if (spec.Name == "WfDateTimePicker") AddDateKind(node, attrs);
        if (spec.Name == "WfRadioButton") attrs.Add($"group={Str(c.ParentOf.GetValueOrDefault(node.Id) ?? "")}");
        if (_preview) PreviewDefaults(node, spec, attrs);
        if (spec.Name == "WfDataGrid") AddGrid(node, c, attrs);
        if (spec.Name == "WfListView") AddListView(node, c, attrs);
        if (spec.Name == "WfTreeView") AddTreeView(node, c, attrs);
        if (spec.Name == "WfScrollBar" && node.Effective().EndsWith("VScrollBar", StringComparison.Ordinal)) attrs.Add("orientation=\"vertical\"");
        if (spec.Name == "WfDropDownButton" && node.Effective().EndsWith("SplitButton", StringComparison.Ordinal)) attrs.Add("split");
    }

    // ------------------------------------------------------------------------------------------------ image lists

    /// <summary>
    /// An ImageList is not a control: its images become web files `public/images/{unit}/{list}/{key}.png` (converted from the decoded stream) and the list a pair of
    /// `keys` / `urls` arrays. The arrays enter the model when code works with the list or a tree/list view shows images from it; statically referenced images
    /// (tab pages, menu and toolbar items) are resolved to their path right here.
    /// </summary>
    private void PrepareImageLists(Ctx c, ControlNodeIr root)
    {
        foreach (var node in Walk(root).Skip(1).Where(n => n.Effective() == ContainerUsage.ImageListType))
        {
            var keys = new List<string>();
            var urls = new List<string>();
            var a = node.Attributes;
            var names = a.TryGetValue("Images.Keys", out var keyJson) ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(keyJson) ?? new List<string>() : new List<string>();
            var data = a.TryGetValue("Images.Data", out var dataJson) ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(dataJson) ?? new List<string>() : new List<string>();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < Math.Max(names.Count, data.Count); i++)
            {
                var key = i < names.Count ? names[i] : "";
                var web = i < data.Count ? IconConverter.ToWeb(System.Convert.FromBase64String(data[i]), "bmp") : null;
                // An image the stream could not give: a file named after its key in the --images folder.
                if (web is null && key.Length > 0 && _translation?.Resources.TryGet(Path.GetFileNameWithoutExtension(key), out var fileData, out var fileFormat) == true)
                    web = IconConverter.ToWeb(fileData, fileFormat);
                var url = PlaceholderIcon;
                if (web is not null)
                {
                    var path = $"images/{c.Unit.Name}/{node.Id}/{ImageFileName(key, i, taken)}.png";
                    c.Assets.Add(($"public/{path}", web.Data));
                    c.IconsConverted++;
                    url = path;
                }
                else
                {
                    c.IconsPlaceholder++;
                    c.Issue("icon-placeholder", $"{node.Id}: image {i}{(key.Length > 0 ? $" ('{key}')" : "")} could not be read (unsupported colour depth or no stream); a placeholder is shown");
                }
                keys.Add(key);
                urls.Add(url);
            }
            c.ImageLists[node.Id] = (keys, urls);
        }

        if (_preview) return; // a visual-only page has no model: only the static images (tab pages, toolbar and menu items) are used

        var inCode = ContainerUsage.ImageListsInCode(c.Unit);
        foreach (var node in c.ById.Values)
        {
            if (node.Attributes.GetValueOrDefault(node.Effective().EndsWith("TreeView", StringComparison.Ordinal) ? "ImageList" : "SmallImageList") is { } reference
                && ImageListId(reference, c) is { } shown)
                inCode.Add(shown);
        }
        foreach (var id in inCode)
        {
            if (!c.ImageLists.TryGetValue(id, out var list)) continue;
            c.ModelSet(id, "Keys", "[" + string.Join(", ", list.Keys.Select(Str)) + "] as string[]");
            c.ModelSet(id, "Urls", "[" + string.Join(", ", list.Urls.Select(Str)) + "] as string[]");
        }
    }

    /// <summary>A file name (without extension) for an image: its key without a trailing image extension, made safe; the index when the image has no key; unique within the list.</summary>
    private static string ImageFileName(string key, int index, HashSet<string> taken)
    {
        var name = Regex.Replace(Path.GetFileNameWithoutExtension(key.Trim()), @"[^A-Za-z0-9._-]+", "_").Trim('_', '.');
        if (name.Length == 0) name = index.ToString();
        var unique = name;
        for (var n = 2; !taken.Add(unique); n++) unique = $"{name}-{n}";
        return unique;
    }

    /// <summary>The id of the ImageList control a property value (`this.ilTree`) points at, when this page has it.</summary>
    private static string? ImageListId(string reference, Ctx c) =>
        c.ById.TryGetValue(LastToken(reference), out var target) && target.Effective() == ContainerUsage.ImageListType ? target.Id : null;

    /// <summary>The web path of the image a control's ImageKey / ImageIndex names in its image list (the control's own list, or the one of the tab control / tool strip it sits in).</summary>
    private string? ImageListIcon(ControlNodeIr node, Ctx c)
    {
        var key = node.Attributes.GetValueOrDefault("ImageKey");
        var hasIndex = int.TryParse(node.Attributes.GetValueOrDefault("ImageIndex"), out var index) && index >= 0;
        if (string.IsNullOrEmpty(key) && !hasIndex) return null;

        for (ControlNodeIr? at = node; at is not null; at = c.ParentOf.TryGetValue(at.Id, out var parent) && c.ById.TryGetValue(parent, out var up) ? up : null)
        {
            if (at.Attributes.GetValueOrDefault("ImageList") is not { } reference || ImageListId(reference, c) is not { } id || !c.ImageLists.TryGetValue(id, out var list)) continue;
            var at2 = !string.IsNullOrEmpty(key) ? list.Keys.IndexOf(key) : index;
            return at2 >= 0 && at2 < list.Urls.Count ? list.Urls[at2] : null;
        }
        return null;
    }

    /// <summary>A tree view keeps its nodes and selected node in the model so `Nodes.Add`, `new TreeNode`, `SelectedNode` and `node.Nodes` translate onto them.</summary>
    private static void AddTreeViewModel(Ctx c, ControlNodeIr node)
    {
        c.LibTypes.Add("WfTreeNode");
        c.ModelSet(node.Id, "Nodes", "[] as WfTreeNode[]");
        c.ModelSet(node.Id, "SelectedNode", "null as WfTreeNode | null");
    }

    private const string SampleTree = "[wfTreeNode('Node 1', [wfTreeNode('Child 1.1'), wfTreeNode('Child 1.2')]), wfTreeNode('Node 2', [wfTreeNode('Child 2.1')])]";

    /// <summary>A tree the translated code never fills (the original builds it in untranslated code) gets clearly marked sample nodes, so the control is visible and testable.</summary>
    private static void SeedTreeViewSamples(Ctx c, List<string> handlerCode)
    {
        if (c.TreeViewIds.Count == 0) return;
        var code = string.Join("\n", handlerCode.Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        foreach (var id in c.TreeViewIds)
        {
            if (Regex.IsMatch(code, $@"\bvm\.{Regex.Escape(id)}\.nodes(\.(push|splice)\b|\s*=[^=])")) continue;
            // Node names the archetype pass read from the code (`new TreeNode("Reports")`) beat the generic sample.
            var named = c.ById.TryGetValue(id, out var treeNode) && treeNode.Attributes.TryGetValue("Archetype.Nodes", out var json)
                ? JsonSerializer.Deserialize<List<string>>(json) : null;
            c.Model[id]["nodes"] = (named is { Count: > 0 } ? "[" + string.Join(", ", named.Select(t => $"wfTreeNode({Str(t)})")) + "]" : SampleTree) + " as WfTreeNode[]";
            c.Lib.Add("wfTreeNode");
            c.ModelComments.Add($"// MOCK — {id} nodes are sample data{(named is { Count: > 0 } ? " (names read from the original code)" : "")}: the original fills it in code that is not translated; replace with real implementation");
        }
    }

    private void AddTreeView(ControlNodeIr node, Ctx c, List<string> attrs)
    {
        c.LibTypes.Add("WfTreeNode");
        if (_preview)
        {
            c.Lib.Add("wfTreeNode");
            var name = Naming.StateName(node.Id) + "Nodes";
            c.TopLevel.Add($"const {name}: WfTreeNode[] = {SampleTree};");
            attrs.Add($"nodes={{{name}}}");
            return;
        }
        c.UsesModel = true;
        if (node.Attributes.GetValueOrDefault("ImageList") is { } treeImages && ImageListId(treeImages, c) is { } treeImageList && c.ImageLists.ContainsKey(treeImageList))
            attrs.Add($"imageList={{vm.{treeImageList}}}");
        attrs.Add($"nodes={{vm.{node.Id}.nodes}}");
        attrs.Add($"selectedNode={{vm.{node.Id}.selectedNode}}");
    }

    private void AddListView(ControlNodeIr node, Ctx c, List<string> attrs)
    {
        if (_preview) { AddListViewPreview(node, c, attrs); return; }
        c.UsesModel = true;
        if (node.Attributes.GetValueOrDefault("SmallImageList") is { } listImages && ImageListId(listImages, c) is { } listImageList && c.ImageLists.ContainsKey(listImageList))
            attrs.Add($"imageList={{vm.{listImageList}}}");
        attrs.Add($"columns={{vm.{node.Id}.columns}}");
        attrs.Add($"items={{vm.{node.Id}.items}}");
        if (node.Attributes.GetValueOrDefault("MultiSelect") == "false") attrs.Add("multiSelect={false}");
    }

    /// <summary>A neutral square shown where the original had an image that could not be turned into a web file.</summary>
    private const string PlaceholderIcon = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='16' height='16'%3E%3Crect x='2' y='2' width='12' height='12' rx='2' fill='%23d0d0d0' stroke='%23808080'/%3E%3C/svg%3E";

    /// <summary>
    /// The `icon` of a menu item or toolbar button: the image the extractor kept (`Image.Data`) written as a web file under `public/icons` (ICO and BMP converted to PNG),
    /// or a placeholder when the control had an image that could not be read (ImageList entry, unsupported format). No image at all: no icon.
    /// </summary>
    private string? IconAttr(ControlNodeIr node, Ctx c)
    {
        var a = node.Attributes;
        if (a.TryGetValue("Image.Data", out var base64) && a.TryGetValue("Image.Format", out var format))
        {
            var web = IconConverter.ToWeb(System.Convert.FromBase64String(base64), format);
            if (web is not null)
            {
                var path = $"icons/{c.Unit.Name}/{node.Id}.{web.Extension}";
                c.Assets.Add(($"public/{path}", web.Data));
                if (web.Converted) c.IconsConverted++; else c.IconsExtracted++;
                return Attr("icon", path);
            }
        }
        if (ImageListIcon(node, c) is { } listIcon) return Attr("icon", listIcon);
        if (a.ContainsKey("Image") || a.ContainsKey("ImageIndex") || a.ContainsKey("ImageKey"))
        {
            c.IconsPlaceholder++;
            c.Issue("icon-placeholder", $"{node.Id}: the image could not be read (ImageList entry, external file or unsupported format); a placeholder icon is shown");
            return Attr("icon", PlaceholderIcon);
        }
        return null;
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
            case "displayStyle":
                // ToolStripItemDisplayStyle.Image | Text | ImageAndText (None leaves the default)
                return LastToken(raw) switch { "Image" => Attr("displayStyle", "image"), "Text" => Attr("displayStyle", "text"), "ImageAndText" => Attr("displayStyle", "imageAndText"), _ => null };
            case "minDate":
            case "maxDate":
                // new System.DateTime(2000, 1, 1, 0, 0, 0, 0) -> "2000-01-01"
                var dm = DateLiteralRegex().Match(raw);
                return dm.Success ? Attr(prop.Name, $"{int.Parse(dm.Groups[1].Value):0000}-{int.Parse(dm.Groups[2].Value):00}-{int.Parse(dm.Groups[3].Value):00}") : null;
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

    private sealed record GridField(string Field, string Header, bool Checkbox, string? Format);

    /// <summary>Column definitions of a grid from its designer column children; with <paramref name="placeholders"/> and no designer columns, three generic ones.</summary>
    /// <summary>
    /// A ListView that is "missing" in the view: no handler is wired to it and no method of the form mentions it, so translated code can neither fill nor read it.
    /// It is shown as a grid (designer columns, three sample rows) instead of an empty list.
    /// </summary>
    private bool IsVisualOnlyList(ControlNodeIr node, Ctx c)
    {
        if (c.VisualLists.TryGetValue(node.Id, out var known)) return known;
        var wired = _spec.Find(node.Effective())?.Events.Any(ev => node.Attributes.ContainsKey("On" + ev.From)) == true;
        var mention = new Regex($@"(?<![\w.]){Regex.Escape(node.Id)}\b");
        var used = c.Unit.Page.Members.Any(m => m.Body is { } body && mention.IsMatch(body));
        return c.VisualLists[node.Id] = !wired && !used;
    }

    private static (List<string> Defs, List<GridField> Fields, int DesignerColumns) GridColumns(ControlNodeIr node, bool placeholders)
    {
        var cols = node.Children.Where(ch => ch.Attributes.GetValueOrDefault("ParentSlot") == "Columns").ToList();
        var defs = new List<string>();
        var fields = new List<GridField>();
        foreach (var col in cols)
        {
            var field = col.Attributes.GetValueOrDefault("DataPropertyName") is { Length: > 0 } dp ? dp : col.Attributes.GetValueOrDefault("Name") ?? col.Id;
            var header = col.Attributes.GetValueOrDefault("HeaderText") ?? col.Attributes.GetValueOrDefault("Text") ?? col.Id;
            var format = col.Attributes.GetValueOrDefault("DefaultCellStyle.Format");
            var width = int.TryParse(col.Attributes.GetValueOrDefault("Width"), out var w) ? $", width: {w}" : "";
            // Cells are editable in place only when the designer says so explicitly (ReadOnly = false on the grid or the column, not true on the column);
            // the WinForms default (editable unless ReadOnly) would turn every migrated grid into an editor.
            var columnReadOnly = col.Attributes.GetValueOrDefault("ReadOnly");
            var editable = columnReadOnly == "false" || (columnReadOnly != "true" && node.Attributes.GetValueOrDefault("ReadOnly") == "false");
            defs.Add($"  {{ field: {Str(field)}, header: {Str(header)}{width}{(format is null ? "" : $", format: {Str(format)}")}{(editable ? ", editable: true" : "")} }},");
            fields.Add(new GridField(field, header, col.TagName.EndsWith("CheckBoxColumn", StringComparison.Ordinal), format));
        }

        if (cols.Count == 0 && placeholders)
            for (var i = 1; i <= 3; i++)
            {
                defs.Add($"  {{ field: \"column{i}\", header: \"Column {i}\" }},");
                fields.Add(new GridField($"column{i}", $"Column {i}", false, null));
            }
        return (defs, fields, cols.Count);
    }

    /// <summary>Three sample rows (fake values shaped by the column headers and formats).</summary>
    private static List<Dictionary<string, object>> GridSampleRows(List<GridField> fields, int count) =>
        Enumerable.Range(1, count).Select(i =>
            fields.GroupBy(f => f.Field).ToDictionary(g => g.Key, g => (object)(g.First().Checkbox ? i % 2 == 1 : g.First().Format is "d" or "date" ? $"2026-01-{i:00}" : $"{g.First().Header} {i}"))).ToList();

    private void AddGrid(ControlNodeIr node, Ctx c, List<string> attrs)
    {
        c.LibTypes.Add("WfGridColumn");
        if (!_preview) c.LibTypes.Add("WfGridCellEvent"); // imported only where a handler uses it; a visual-only page imports every type listed
        var baseName = Naming.StateName(node.Id);
        var colsName = c.Names.Unique(baseName + "Columns");
        var (defs, fields, designerColumns) = GridColumns(node, placeholders: _preview || c.ListGrids.Contains(node.Id));

        if (designerColumns == 0 && !_preview && !c.ListGrids.Contains(node.Id) && !IsGridUsedInCode(node, c))
        {
            c.TopLevel.Add($"// TODO: columns of {node.Id} are created in code (not in the designer); define them here.");
            c.Issue("grid-no-columns", $"{node.Id} has no designer columns; rows are empty");
        }
        if (defs.Any(d => d.Contains("editable: true", StringComparison.Ordinal))) c.EditableGrids.Add(node.Id);
        c.TopLevel.Add($"const {colsName}: WfGridColumn[] = [");
        c.TopLevel.AddRange(defs);
        c.TopLevel.Add("];");

        var rows = GridSampleRows(fields, fields.Count == 0 ? 0 : 3);
        if (_preview)
        {
            var rowsName = c.Names.Unique(baseName + "Rows");
            c.TopLevel.Add($"const {rowsName}: Record<string, unknown>[] = {JsonSerializer.Serialize(rows, JsonOut)};");
            attrs.Add($"rows={{{rowsName}}}");
            attrs.Add($"columns={{{colsName}}}");
            return;
        }

        c.UsesModel = true;
        if (IsGridUsedInCode(node, c))
        {
            // The code reads or fills this grid (Rows.Add, Cells[k].Value, SelectedRows, Columns.Add): columns, rows and selection are model entries.
            c.GridIds.Add(node.Id);
            c.ModelSet(node.Id, "Columns", $"{colsName}.map((col) => ({{ ...col }})) as WfGridColumn[]");
            c.ModelSet(node.Id, "Rows", "[] as Record<string, unknown>[]");
            c.ModelSet(node.Id, "Selected", "[] as Record<string, unknown>[]");
            attrs.Add($"rows={{vm.{node.Id}.rows}}");
            attrs.Add($"columns={{vm.{node.Id}.columns}}");
            return;
        }

        c.ModelComments.Add($"// MOCK — {node.Id} rows are fake sample data; replace with real implementation (inferred — check against backend contract)");
        c.ModelSet(node.Id, "Rows", $"{JsonSerializer.Serialize(rows, JsonOut)} as Record<string, unknown>[]");
        attrs.Add($"rows={{vm.{node.Id}.rows}}");
        attrs.Add($"columns={{{colsName}}}");
    }

    /// <summary>A grid that some method of the form mentions: translated code can then add rows, read cells or use the selection.</summary>
    private static bool IsGridUsedInCode(ControlNodeIr node, Ctx c)
    {
        var mention = new Regex($@"(?<![\w.]){Regex.Escape(node.Id)}\b");
        return c.Unit.Page.Members.Any(m => m.Body is { } body && mention.IsMatch(body));
    }

    /// <summary>A model-backed grid that translated code never fills gets three marked sample rows (and placeholder columns when it has none and code adds none).</summary>
    private static void SeedGridSamples(Ctx c, List<string> handlerCode)
    {
        if (c.GridIds.Count == 0) return;
        var code = string.Join("\n", handlerCode.Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        foreach (var id in c.GridIds)
        {
            var key = Regex.Escape(id);
            var writesRows = Regex.IsMatch(code, $@"\bwfGridAddRow\(vm\.{key}\b|\bvm\.{key}\.rows(\.(push|splice)\b|\s*=[^=])");
            var writesColumns = Regex.IsMatch(code, $@"\bwfGridAddColumn\(vm\.{key}\b|\bvm\.{key}\.columns(\.(push|splice)\b|\s*=[^=])");
            if (writesRows) continue;

            var model = c.Model[id];
            var fields = Regex.Matches(model["columns"], @"field: ""([^""]+)""").Select(m => m.Groups[1].Value).ToList();
            if (fields.Count == 0 && !writesColumns)
            {
                model["columns"] = "[" + string.Join(", ", Enumerable.Range(1, 3).Select(i => $"{{ field: 'column{i}', header: 'Column {i}' }}")) + "] as WfGridColumn[]";
                fields = Enumerable.Range(1, 3).Select(i => $"column{i}").ToList();
            }
            if (fields.Count == 0) continue;
            var rows = Enumerable.Range(1, 3).Select(r => fields.ToDictionary(f => f, f => (object)$"{f} {r}")).ToList();
            model["rows"] = $"{JsonSerializer.Serialize(rows, JsonOut)} as Record<string, unknown>[]";
            c.ModelComments.Add($"// MOCK — {id} rows are fake sample data: the original fills it in code that is not translated; replace with real implementation");
        }
    }

    // ---------------------------------------------------------------------------------------------- preview (visual-only pages)

    /// <summary>
    /// A form or user control as a standalone, visual-only page: controls, layout, fallbacks and designer text as designed, placeholder data in
    /// grids/lists/combo boxes, and nothing that runs: no handlers, model, dialogs, translated code, support or service imports, props.
    /// </summary>
    private GeneratedPage GeneratePreview(UnitIr unit)
    {
        var root = unit.Page.RootControl ?? throw new InvalidDataException($"{unit.Name} has no control tree.");
        var component = _components[unit.Name];
        var c = new Ctx(unit, component);
        ApplyFallbacks(c, root);
        foreach (var n in Walk(root)) c.ById.TryAdd(n.Id, n);
        FillParents(c, root);
        PrepareImageLists(c, root); // the images of an ImageList show on tab pages, toolbar and menu items even in a visual-only page
        var jsx = RenderElement(root, c, isRoot: true) ?? throw new InvalidDataException($"{unit.Name}: root control produced no output.");

        var sb = new StringBuilder();
        if (c.UsesDialog) c.Lib.Add("useWfDialog");
        sb.AppendLine($"// Visual-only preview of {unit.Name} ({unit.Page.RelativePath ?? "n/a"}): controls and layout as designed, placeholder data, no handlers, no logic.");
        var lib = new List<string>(c.Lib);
        lib.AddRange(c.LibTypes.Select(t => "type " + t));
        sb.AppendLine($"import {{ {string.Join(", ", lib)} }} from '@wfm/ui-react';");
        foreach (var (comp, file) in c.PageImports) sb.AppendLine($"import {{ {comp} }} from './{file}';");
        sb.AppendLine();
        foreach (var t in c.TopLevel) sb.AppendLine(t);
        if (c.TopLevel.Count > 0) sb.AppendLine();
        sb.AppendLine($"export function {component}() {{");
        if (c.UsesDialog) sb.AppendLine("  const dialog = useWfDialog();");
        sb.AppendLine("  return (");
        foreach (var line in jsx) sb.AppendLine("    " + line);
        sb.AppendLine("  );");
        sb.AppendLine("}");

        var page = new GeneratedPage { ComponentName = component, Source = sb.ToString(), LegacySource = "", Controls = Walk(root).Count(), NonVisual = c.NonVisual,
            Assets = c.Assets, IconsExtracted = c.IconsExtracted, IconsConverted = c.IconsConverted, IconsPlaceholder = c.IconsPlaceholder };
        page.Issues.AddRange(c.Issues);
        return page;
    }

    /// <summary>
    /// App shell: a control whose event the IR proves opens a form (`ShowDialog`) or closes this one (`Close`, `Hide`, or a DialogResult button) gets exactly that,
    /// through the dialog service, so a form can be walked through like the original window flow. No other handler exists in a visual-only page.
    /// </summary>
    private void AddPreviewNavigation(ControlNodeIr node, SpecControl spec, Ctx c, List<string> attrs, bool isRoot)
    {
        if (isRoot) return;
        foreach (var ev in spec.Events)
        {
            if (attrs.Any(a => a.StartsWith(ev.React + "=", StringComparison.Ordinal))) continue;
            var sites = c.Unit.Page.Navigations.Where(n => n.SourceControlId == node.Id && n.TriggerEvent == ev.From).ToList();

            var target = sites.Where(s => s.TargetType is "Show" or "ShowDialog").Select(s => s.Target).FirstOrDefault(t => t != c.Unit.Name && _generated.Contains(t) && _components.ContainsKey(t));
            if (target is not null)
            {
                var comp = _components[target];
                c.PageImports[comp] = comp;
                c.UsesDialog = true;
                attrs.Add($"{ev.React}={{() => {{ void dialog.show(() => <{comp} />, {{ title: {Str(_units[target].Title)} }}); }}}}");
            }
            else if (spec.Name == "WfButton" && ev.Name == "click" && ButtonDialogResult(node) is { } result)
            {
                c.UsesDialog = true;
                attrs.Add($"{ev.React}={{() => {{ dialog.close({Str(result)}); }}}}");
            }
            else if (sites.Any(s => s.TargetType is "Close" or "Hide"))
            {
                c.UsesDialog = true;
                attrs.Add($"{ev.React}={{() => {{ dialog.close(); }}}}");
            }
        }
    }

    /// <summary>Placeholders so a control is never an empty or blank box in the viewer: sample combo items, a visible name for unnamed labels and buttons.</summary>
    private static void PreviewDefaults(ControlNodeIr node, SpecControl spec, List<string> attrs)
    {
        if (spec.Name is "WfComboBox" or "WfListBox" or "WfCheckedListBox" or "WfDomainUpDown" && !node.Attributes.ContainsKey("Items")) attrs.Add("items={[\"Item 1\", \"Item 2\"]}");
        if (spec.Name is "WfButton" or "WfLabel" && string.IsNullOrWhiteSpace(node.Attributes.GetValueOrDefault("Text"))) attrs.Add(Attr("text", $"[{node.Id}]"));
    }

    /// <summary>A list view with its designer columns (or three generic ones) and three sample rows, as constants.</summary>
    private void AddListViewPreview(ControlNodeIr node, Ctx c, List<string> attrs)
    {
        c.LibTypes.Add("WfListColumn");
        c.LibTypes.Add("WfListItem");
        c.Lib.Add("wfListItemFromArray");
        var baseName = Naming.StateName(node.Id);
        var colsName = c.Names.Unique(baseName + "Columns");
        var itemsName = c.Names.Unique(baseName + "Items");
        var defs = ListColumnDefs(node);
        var count = defs.Count > 0 ? defs.Count : 3;
        if (defs.Count == 0) defs = Enumerable.Range(1, count).Select(i => $"{{ header: 'Column {i}' }}").ToList();
        var rows = Enumerable.Range(1, 3).Select(r => "wfListItemFromArray([" + string.Join(", ", Enumerable.Range(1, count).Select(i => $"'Sample {r}.{i}'")) + "])");
        c.TopLevel.Add($"const {colsName}: WfListColumn[] = [{string.Join(", ", defs)}];");
        c.TopLevel.Add($"const {itemsName}: WfListItem[] = [{string.Join(", ", rows)}];");
        attrs.Add($"columns={{{colsName}}}");
        attrs.Add($"items={{{itemsName}}}");
        if (node.Attributes.GetValueOrDefault("MultiSelect") == "false") attrs.Add("multiSelect={false}");
    }

    // ---------------------------------------------------------------------------------------------- events

    private void BuildEvents(ControlNodeIr node, SpecControl spec, Ctx c, List<string> attrs, bool isRoot)
    {
        foreach (var ev in spec.Events)
        {
            // Two WinForms events can feed one canonical event (DateChanged / DateSelected): the first one that produced the attribute wins.
            if (attrs.Any(a => a.StartsWith(ev.React + "=", StringComparison.Ordinal))) continue;
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
                c.UsesDialog = true;
                var isAsync = handlerMember is not null && c.Translation?.ByMember.TryGetValue(handlerMember, out var mt) == true && mt.IsAsync;
                var close = $"dialog.close({Str(closeWith)});";
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

            // A grid the code uses keeps its selected rows in the model, so SelectedRows / CurrentRow read the selection that is on screen.
            if (spec.Name == "WfDataGrid" && ev.Name == "rowSelect" && c.GridIds.Contains(node.Id) && !_preview)
            {
                c.UsesModel = true;
                attrs.Add($"{ev.React}={{(rows) => {{ vm.{node.Id}.selected = rows;{(handlerName is null ? "" : $" {handlerName}();")} }}}}");
                continue;
            }

            // DataGridView with editable columns: an edited cell is written to its row in the page model (so reads see it), then the CellEndEdit handler runs.
            if (spec.Name == "WfDataGrid" && ev.Name == "cellEdit" && c.EditableGrids.Contains(node.Id) && !_preview)
            {
                c.UsesModel = true;
                attrs.Add($"{ev.React}={{(e) => {{ e.row[e.field] = e.value;{(handlerName is null ? "" : $" {handlerName}();")} }}}}");
                continue;
            }

            // CheckedListBox: the check state is written to the model (so CheckedItems / GetItemChecked read it), then the handler runs.
            if (spec.Name == "WfCheckedListBox" && ev.Name == "itemCheck" && c.ComboIds.Contains(node.Id) && !_preview)
            {
                c.UsesModel = true;
                c.Lib.Add("wfSetItemChecked");
                attrs.Add($"{ev.React}={{(i, on) => {{ wfSetItemChecked(vm.{node.Id}.checkedIndices, i, on);{(handlerName is null ? "" : $" {handlerName}();")} }}}}");
                continue;
            }

            // TreeView selection is written back to the model so `SelectedNode` reads it immediately.
            if (spec.Name == "WfTreeView" && ev.Name == "selectionChange" && c.TreeViewIds.Contains(node.Id) && !_preview)
            {
                c.UsesModel = true;
                attrs.Add($"{ev.React}={{(n) => {{ vm.{node.Id}.selectedNode = n;{(handlerName is null ? "" : $" {handlerName}();")} }}}}");
                continue;
            }

            var isStateEvent = StateEvents.Contains((spec.Name, ev.Name));
            if (isStateEvent && c.StatefulIds.Contains(node.Id))
            {
                c.UsesModel = true;
                var target = $"vm.{node.Id}.{ModelKey(StatefulProp(spec.Name))}";
                // Radio buttons of one container are mutually exclusive: selecting one clears the others that are in the model.
                var clears = spec.Name == "WfRadioButton" ? string.Concat(RadioSiblings(node, c).Select(s => $" vm.{s}.checked = false;")) : "";
                var after = clears.Length > 0 ? $" if (v) {{{clears} }}" : "";
                attrs.Add(handlerName == null ? $"{ev.React}={{(v) => {{ {target} = v;{after} }}}}" : $"{ev.React}={{(v) => {{ {target} = v;{after} {handlerName}(); }}}}");
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
        var handler = new Handler { Name = name, ControlId = node.Id, Event = eventName, Member = member, IsRoot = isRoot, Source = node.Attributes.GetValueOrDefault("Wired." + eventName) == "code" ? "code" : "designer" };
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
            foreach (var l in EnsureNavigation(c, tr, t))
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

    /// <summary>
    /// A wired handler whose navigation the IR proves (a `ShowDialog` or `Close` on this control event) but whose translated code lost it, because the statement that
    /// opened or closed the form depends on code that could not be translated: the popup is opened (with the sample data every generated page has) or the dialog is closed
    /// at the end of the handler, marked MOCK and listed in the report. Handlers that already open the form, or call a function that does, are left alone.
    /// </summary>
    private List<string> EnsureNavigation(Ctx c, PageTranslation tr, MethodTranslation t)
    {
        var handler = c.Handlers.Values.FirstOrDefault(h => tr.ByMember.TryGetValue(h.Member, out var mt) && ReferenceEquals(mt, t));
        if (handler is null || t.Status == TranslationStatus.NotTranslated) return t.Lines;
        var sites = c.Unit.Page.Navigations.Where(n => n.SourceControlId == handler.ControlId && n.TriggerEvent == handler.Event).ToList();

        // Everything this handler runs: its own code and, transitively, the translated functions it calls.
        var reach = new StringBuilder();
        var seen = new HashSet<MethodTranslation>();
        void Visit(MethodTranslation m)
        {
            if (!seen.Add(m)) return;
            var code = string.Join("\n", m.Lines.Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            reach.AppendLine(code);
            foreach (var other in tr.Methods.Where(o => o.Lines.Count > 0 && o != m && Regex.IsMatch(code, $@"(?<![\w.]){Regex.Escape(o.TsName)}\(")))
                Visit(other);
        }
        Visit(t);
        var text = reach.ToString();

        // Navigation found in a method this handler calls is recorded against the method (`Method:<name>`), not the control event.
        foreach (var m in seen)
            sites.AddRange(c.Unit.Page.Navigations.Where(n => n.TriggerEvent == "Method:" + m.Member.Name && !sites.Contains(n)));
        if (sites.Count == 0) return t.Lines;

        var add = new List<string>();
        var shows = sites.Where(x => x.TargetType is "Show" or "ShowDialog").Select(x => x.Target).Distinct().ToList();
        if (shows.Count > 0 && _generated.Contains(shows[0]) && _components.TryGetValue(shows[0], out var comp) && !Regex.IsMatch(text, $@"dialog\.show<[^>]*>\(\([^)]*\) => <{Regex.Escape(comp)}\b"))
        {
            c.PageImports[comp] = comp;
            add.Add($"// MOCK — {shows[0]} is opened here without the original condition and arguments (the code that decided this was not translated); replace with real implementation");
            add.Add($"await dialog.show<unknown>(() => <{comp} />, {{ title: {Str(_units[shows[0]].Title)} }});");
            c.Issue("nav-mock-open", $"{handler.ControlId}.{handler.Event} opens {shows[0]}: wired without its original condition and arguments");
        }
        if (sites.Any(x => x.TargetType is "Close" or "Hide") && !Regex.IsMatch(text, @"\bdialog\.close\b"))
        {
            add.Add("// MOCK — the original closes the form here, but the code that decided this was not translated; replace with real implementation");
            add.Add("dialog.close();");
            c.Issue("nav-mock-close", $"{handler.ControlId}.{handler.Event} closes the form: wired without its original condition");
        }
        if (add.Count == 0) return t.Lines;

        var lines = new List<string>(t.Lines);
        var close = lines.FindLastIndex(l => l.TrimEnd() == "}");
        if (close < 0) return t.Lines;
        var start = lines.FindIndex(l => Regex.IsMatch(l, @"^(async )?function \w+\("));
        if (start >= 0 && add.Any(l => l.StartsWith("await ", StringComparison.Ordinal)) && !lines[start].StartsWith("async ", StringComparison.Ordinal))
            lines[start] = "async " + lines[start];
        lines.InsertRange(close, add.Select(l => "  " + l));
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
        var lines = page.CodeSource.Split('\n');
        var starts = new List<(string Name, int Line)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var m = System.Text.RegularExpressions.Regex.Match(lines[i], @"^  (?:async )?function (\w+)\(");
            if (m.Success) starts.Add((m.Groups[1].Value, i + 1));
        }
        var ret = Array.FindIndex(lines, l => l.StartsWith("  return ", StringComparison.Ordinal)) + 1;
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
                    body.Add($"await dialog.show<unknown>(() => <{comp} />, {{ title: {Str(_units[target].Title)} }});");
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
                c.UsesDialog = true;
                body.Add("dialog.close();");
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

    private static bool IsStateful(string specName) => specName is "WfTextBox" or "WfComboBox" or "WfCheckBox" or "WfRadioButton" or "WfDateTimePicker" or "WfNumericUpDown"
        or "WfMaskedTextBox" or "WfRichTextBox" or "WfMonthCalendar" or "WfListBox"
        or "WfDomainUpDown" or "WfCheckedListBox" or "WfTrackBar" or "WfScrollBar";

    /// <summary>The WinForms property a stateful control keeps in the page model.</summary>
    private static string StatefulProp(string specName) => specName switch
    {
        "WfCheckBox" or "WfRadioButton" => "Checked",
        "WfDateTimePicker" or "WfNumericUpDown" or "WfTrackBar" or "WfScrollBar" => "Value",
        "WfMonthCalendar" => "SelectionStart",
        "WfTabs" => "SelectedIndex",
        _ => "Text"
    };

    private static void FillParents(Ctx c, ControlNodeIr root)
    {
        void Visit(ControlNodeIr parent)
        {
            foreach (var child in parent.Children)
            {
                c.ParentOf[child.Id] = parent.Id;
                Visit(child);
            }
        }
        Visit(root);
    }

    /// <summary>Other radio buttons in the same container that are part of the page model.</summary>
    private List<string> RadioSiblings(ControlNodeIr node, Ctx c) =>
        c.ParentOf.TryGetValue(node.Id, out var parent)
            ? c.ParentOf.Where(kv => kv.Value == parent && kv.Key != node.Id && c.StatefulIds.Contains(kv.Key) && c.ById.TryGetValue(kv.Key, out var s) && _spec.Find(s.Effective())?.Name == "WfRadioButton")
                .Select(kv => kv.Key).ToList()
            : new List<string>();

    /// <summary>DateTimePicker.Format / CustomFormat -> date, datetime or time (the default Long/Short format is a date).</summary>
    private static void AddDateKind(ControlNodeIr node, List<string> attrs)
    {
        var format = LastToken(node.Attributes.GetValueOrDefault("Format") ?? "");
        var kind = format == "Time" ? "time" : "date";
        if (format == "Custom" && node.Attributes.GetValueOrDefault("CustomFormat") is { } custom)
        {
            var hasTime = custom.Contains('H') || custom.Contains('h') || custom.Contains("tt", StringComparison.Ordinal);
            var hasDate = custom.Contains('d') || custom.Contains('M') || custom.Contains('y');
            kind = hasTime && hasDate ? "datetime" : hasTime ? "time" : "date";
        }
        if (kind != "date") attrs.Add($"kind=\"{kind}\"");
    }

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
    /// <summary>A page component is used as an element (`&lt;Comp /&gt;`) or handed to `wfDynamicControl(Comp, ...)`.</summary>
    private static bool UsesComponent(string text, string comp) => Regex.IsMatch(text, $@"(?:<|\bwfDynamicControl\()\s*{Regex.Escape(comp)}\b");

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
