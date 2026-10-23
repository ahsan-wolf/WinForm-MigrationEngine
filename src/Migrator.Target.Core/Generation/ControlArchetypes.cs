using System.Text.Json;
using System.Text.RegularExpressions;
using Migrator.Engine.Projections;
using Migrator.Target.Core.Ir;
using WebFormsMigrator.IR.Models;

namespace Migrator.Target.Core.Generation;

/// <summary>What a unit is for: a grid, tree, menu, toolbar or list control, or an ordinary form / edit control.</summary>
public enum ControlArchetype { Form, Grid, Tree, Menu, Toolbar, List }

/// <summary>How a unit was classified. <see cref="Source"/> is "designer", "name" or "designer+name"; <see cref="Inferred"/> lists what was built from code when the designer was silent.</summary>
public sealed record ArchetypeInfo(string Unit, ControlArchetype Archetype, string Source, string? Primary, List<string> Inferred);

/// <summary>
/// The data behind <see cref="ControlArchetypes"/> (`rules/control-archetypes.json`): name patterns that decide the archetype when the designer cannot, and how
/// many input controls make a unit an edit form. Defaults work without a file.
/// </summary>
public sealed class ArchetypeRules
{
    public int InputThreshold { get; private set; } = 6;
    public List<(ControlArchetype Archetype, Regex Pattern)> Names { get; } = new();

    public static ArchetypeRules Load(string? path)
    {
        var rules = new ArchetypeRules();
        if (path is not null && File.Exists(path))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("inputThreshold", out var t) && t.TryGetInt32(out var threshold)) rules.InputThreshold = threshold;
            if (doc.RootElement.TryGetProperty("names", out var names))
                foreach (var entry in names.EnumerateArray())
                    if (Enum.TryParse<ControlArchetype>(entry.GetProperty("archetype").GetString(), true, out var archetype) && entry.TryGetProperty("pattern", out var pattern))
                        rules.Names.Add((archetype, new Regex(pattern.GetString()!, RegexOptions.IgnoreCase | RegexOptions.Compiled)));
        }
        if (rules.Names.Count == 0)
        {
            rules.Names.Add((ControlArchetype.Menu, new Regex("Menu(Strip|Bar)?(Control)?$", RegexOptions.IgnoreCase)));
            rules.Names.Add((ControlArchetype.Toolbar, new Regex("Tool(Strip|Bar)(Control)?$", RegexOptions.IgnoreCase)));
            rules.Names.Add((ControlArchetype.Tree, new Regex("Tree(View|Viewer|Control)?$", RegexOptions.IgnoreCase)));
            rules.Names.Add((ControlArchetype.Grid, new Regex("Grid(View|Viewer|Control)?s?$|DataGrid", RegexOptions.IgnoreCase)));
            rules.Names.Add((ControlArchetype.List, new Regex("List(View|Viewer|Control)?$", RegexOptions.IgnoreCase)));
        }
        return rules;
    }
}

/// <summary>
/// Classifies units by what their designer holds (a grid, a tree, a menu strip, a tool strip, a list) and, when the designer cannot decide, by name patterns.
/// A unit classified as an archetype whose designer under-specifies it (no grid, no columns, no menu items, ...) gets the missing control or its parts
/// inferred from literals in its code (`Columns.Add("Name", "Header")`, `new TreeNode("Node")`, `new ToolStripMenuItem("File")`) or from the row type
/// of its data source, else clearly marked generic ones. Everything inferred carries `Archetype.Inferred` so it is listed in the report.
/// </summary>
public static partial class ControlArchetypes
{
    private static string Short(ControlNodeIr n) => n.Effective().Split('.').Last();

    private static IEnumerable<ControlNodeIr> Walk(ControlNodeIr node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var d in Walk(child)) yield return d;
        }
    }

    private static readonly HashSet<string> InputTypes = new(StringComparer.Ordinal)
    {
        "TextBox", "MaskedTextBox", "RichTextBox", "ComboBox", "CheckBox", "RadioButton", "DateTimePicker", "NumericUpDown", "DomainUpDown", "TrackBar", "MonthCalendar"
    };

    /// <summary>The archetype of every unit.</summary>
    public static Dictionary<string, ArchetypeInfo> Classify(IReadOnlyDictionary<string, UnitIr> units, ArchetypeRules rules) =>
        units.ToDictionary(u => u.Key, u => ClassifyUnit(u.Value, rules), StringComparer.Ordinal);

    public static ArchetypeInfo ClassifyUnit(UnitIr unit, ArchetypeRules rules)
    {
        var all = unit.Page.RootControl is { } root ? Walk(root).Where(n => n.Attributes.GetValueOrDefault("ParentSlot") is not ("Columns" or "Items" or "DropDownItems" or "TabPages")).ToList() : new List<ControlNodeIr>();
        ControlNodeIr? First(string type) => all.FirstOrDefault(n => Short(n) == type);
        var grid = all.FirstOrDefault(n => Short(n).EndsWith("DataGridView", StringComparison.Ordinal));
        var tree = First("TreeView");
        var menu = First("MenuStrip");
        var bar = First("ToolStrip");
        var list = all.FirstOrDefault(n => Short(n) is "ListView" or "ListBox" or "CheckedListBox");
        var inputs = all.Count(n => InputTypes.Contains(Short(n)));
        var hasData = grid is not null || tree is not null || menu is not null || bar is not null || list is not null;
        // Content other than containers, strips and non-visual components: a menu or toolbar unit is only that strip, not a form that has one.
        var content = all.Count(n => Short(n) is not ("ImageList" or "Timer" or "ToolTip" or "BindingSource" or "ContextMenuStrip" or "StatusStrip" or "MenuStrip" or "ToolStrip" or "Panel" or "GroupBox" or "TableLayoutPanel" or "FlowLayoutPanel" or "SplitContainer" or "SplitterPanel"));
        var visual = content + (menu is null ? 0 : 1) + (bar is null ? 0 : 1);

        ControlArchetype? designer = null;
        ControlNodeIr? primary = null;
        if (grid is not null && inputs <= rules.InputThreshold) (designer, primary) = (ControlArchetype.Grid, grid);
        else if (tree is not null && grid is null && inputs <= rules.InputThreshold) (designer, primary) = (ControlArchetype.Tree, tree);
        else if (menu is not null && grid is null && tree is null && list is null && content == 0 && bar is null) (designer, primary) = (ControlArchetype.Menu, menu);
        else if (bar is not null && grid is null && tree is null && list is null && menu is null && content == 0) (designer, primary) = (ControlArchetype.Toolbar, bar);
        else if (list is not null && grid is null && tree is null && inputs <= rules.InputThreshold) (designer, primary) = (ControlArchetype.List, list);

        var byName = ByName(unit.Name, rules);
        if (designer is { } decided)
            return new ArchetypeInfo(unit.Name, decided, byName == decided ? "designer+name" : "designer", primary?.Id, new List<string>());

        // The designer cannot decide: nothing visual, or no data control and only a few inputs (a grid built in code sits beside a filter row or a button).
        var ambiguous = !hasData && inputs <= rules.InputThreshold && (visual == 0 || unit.IsUserControl);
        if (ambiguous && byName is { } named && named != ControlArchetype.Form)
            return new ArchetypeInfo(unit.Name, named, "name", null, new List<string>());
        return new ArchetypeInfo(unit.Name, ControlArchetype.Form, "designer", null, new List<string>());
    }

    private static ControlArchetype? ByName(string name, ArchetypeRules rules)
    {
        var stripped = Naming.StripPrefix(name);
        foreach (var (archetype, pattern) in rules.Names)
            if (pattern.IsMatch(name) || pattern.IsMatch(stripped)) return archetype;
        return null;
    }

    // ------------------------------------------------------------------------------------------------ synthesis

    /// <summary>
    /// Adds what the designer of an archetype unit does not say: the primary control itself, or its columns / nodes / items. Changes the unit's IR in memory
    /// only (the IR files are untouched). <paramref name="rowProperties"/> gives the public property names of a project class (the row type of a data source).
    /// </summary>
    public static void Synthesize(UnitIr unit, ArchetypeInfo info, Func<string, IReadOnlyList<string>?> rowProperties)
    {
        if (info.Archetype == ControlArchetype.Form || unit.Page.RootControl is not { } root) return;
        var code = string.Join("\n", unit.Page.Members.Where(m => m.Body is not null).Select(m => string.Join("\n", m.Body!.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)))));
        var all = Walk(root).ToList();
        var layout = unit.Layout as IDictionary<string, ControlLayout>;

        ControlNodeIr Make(string id, string type, string dock, bool atStart = false)
        {
            var node = new ControlNodeIr { Id = id, TagName = "System.Windows.Forms." + type };
            node.Attributes["Name"] = id;
            node.Attributes["Archetype.Synthesized"] = info.Archetype.ToString();
            if (atStart) root.Children.Insert(0, node); else root.Children.Add(node);
            layout?.TryAdd(id, new ControlLayout(id, dock, null, null, null, null, null, null, null, null, new TableLayoutCell(null, null, null, null), null));
            info.Inferred.Add($"{id}: a {type} added (the unit is a {info.Archetype.ToString().ToLowerInvariant()} control by its {(info.Source.Contains("name") ? "name" : "design")} but its designer has none)");
            return node;
        }

        switch (info.Archetype)
        {
            case ControlArchetype.Grid:
            {
                var existing = all.FirstOrDefault(n => Short(n).EndsWith("DataGridView", StringComparison.Ordinal));
                if (existing is not null && (existing.Children.Any(ch => ch.Attributes.GetValueOrDefault("ParentSlot") == "Columns")
                    || Regex.IsMatch(code, $@"\b{Regex.Escape(existing.Id)}\s*\.\s*Columns\b"))) return; // columns by design, or created at run time by code the translator handles
                var grid = existing ?? Make("dgvData", "DataGridView", "Fill");
                var (columns, how) = GridColumnsFromCode(code, unit, rowProperties, existing is null);
                if (columns.Count == 0) return;
                foreach (var (field, header) in columns)
                {
                    var col = new ControlNodeIr { Id = "col" + Naming.Pascal(field), TagName = "System.Windows.Forms.DataGridViewTextBoxColumn" };
                    col.Attributes["ParentSlot"] = "Columns";
                    col.Attributes["Name"] = col.Id;
                    col.Attributes["HeaderText"] = header;
                    col.Attributes["DataPropertyName"] = field;
                    col.Attributes["Archetype.Inferred"] = how;
                    grid.Children.Add(col);
                }
                info.Inferred.Add($"{grid.Id}: {columns.Count} column(s) {(how == "generic" ? "are generic placeholders (no column name in the designer or the code)" : how == "type" ? "taken from the properties of the data source's row type" : "named in the code")}");
                break;
            }
            case ControlArchetype.Tree:
            {
                var tree = all.FirstOrDefault(n => Short(n) == "TreeView") ?? Make("tvwNodes", "TreeView", "Fill");
                var texts = Literals(code, TreeNodeRegex()).ToList();
                if (texts.Count > 0)
                {
                    tree.Attributes["Archetype.Nodes"] = JsonSerializer.Serialize(texts);
                    info.Inferred.Add($"{tree.Id}: {texts.Count} node(s) named in the code");
                }
                break;
            }
            case ControlArchetype.Menu:
            {
                var menu = all.FirstOrDefault(n => Short(n) == "MenuStrip") ?? Make("menuMain", "MenuStrip", "Top", atStart: true);
                if (menu.Children.Any(ch => ch.Attributes.GetValueOrDefault("ParentSlot") == "Items")) return;
                var texts = Literals(code, MenuItemRegex()).ToList();
                AddItems(menu, "ToolStripMenuItem", texts, "Menu", info, "item(s)");
                break;
            }
            case ControlArchetype.Toolbar:
            {
                var bar = all.FirstOrDefault(n => Short(n) == "ToolStrip") ?? Make("toolStripMain", "ToolStrip", "Top", atStart: true);
                if (bar.Children.Any(ch => ch.Attributes.GetValueOrDefault("ParentSlot") == "Items")) return;
                var texts = Literals(code, ToolItemRegex()).ToList();
                AddItems(bar, "ToolStripButton", texts, "Button", info, "button(s)");
                break;
            }
            case ControlArchetype.List:
            {
                if (all.All(n => Short(n) is not ("ListView" or "ListBox" or "CheckedListBox"))) Make("lstItems", "ListView", "Fill");
                break;
            }
        }
    }

    private static void AddItems(ControlNodeIr strip, string type, List<string> texts, string placeholder, ArchetypeInfo info, string noun)
    {
        var generic = texts.Count == 0;
        if (generic) texts = Enumerable.Range(1, 3).Select(i => $"{placeholder} {i}").ToList();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in texts)
        {
            var id = "item" + Naming.Pascal(text);
            for (var n = 2; !taken.Add(id); n++) id = "item" + Naming.Pascal(text) + n;
            var item = new ControlNodeIr { Id = id, TagName = "System.Windows.Forms." + type };
            item.Attributes["ParentSlot"] = "Items";
            item.Attributes["Name"] = id;
            item.Attributes["Text"] = text;
            item.Attributes["Archetype.Inferred"] = generic ? "generic" : "code";
            strip.Children.Add(item);
        }
        info.Inferred.Add($"{strip.Id}: {texts.Count} {noun} {(generic ? "are generic placeholders (none named in the designer or the code)" : "named in the code")}");
    }

    private static IEnumerable<string> Literals(string code, Regex pattern) =>
        pattern.Matches(code).Select(m => m.Groups[1].Value).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal);

    private static (List<(string Field, string Header)> Columns, string How) GridColumnsFromCode(string code, UnitIr unit, Func<string, IReadOnlyList<string>?> rowProperties, bool synthesized)
    {
        var found = new List<(string Field, string Header)>();
        void Add(string field, string? header = null)
        {
            if (field.Length > 0 && found.All(f => f.Field != field)) found.Add((field, string.IsNullOrEmpty(header) ? Humanize(field) : header));
        }
        foreach (Match m in ColumnsAddRegex().Matches(code)) Add(m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value : null);
        foreach (Match m in DataPropertyRegex().Matches(code)) Add(m.Groups[1].Value);
        foreach (Match m in ColumnIndexerRegex().Matches(code)) Add(m.Groups[1].Value);
        if (found.Count > 0 && synthesized) return (found, "code");
        found.Clear();

        // The row type of the data source: `grid.DataSource = rows;` where `rows` is a List<Row> / BindingList<Row> / Row[] of a project class.
        var types = new List<string>();
        foreach (Match m in DataSourceRegex().Matches(code))
        {
            if (m.Groups[1].Success) { types.Add(m.Groups[1].Value); continue; }
            var target = m.Groups[2].Value;
            var memberType = unit.Page.Members.FirstOrDefault(x => x.Name == target)?.Type;
            types.Add(!string.IsNullOrEmpty(memberType) ? memberType : Regex.Match(code, $@"\b([\w<>\[\]]+)\s+{Regex.Escape(target)}\s*(=|;)").Groups[1].Value);
        }
        foreach (var type in types)
        {
            var row = RowTypeRegex().Match(type) is { Success: true } g ? g.Groups[1].Success ? g.Groups[1].Value : g.Groups[2].Value : type;
            if (rowProperties(row) is { Count: > 0 } props)
            {
                foreach (var p in props) Add(p);
                return (found, "type");
            }
        }
        return (synthesized ? Enumerable.Range(1, 3).Select(i => ($"column{i}", $"Column {i}")).ToList() : new List<(string, string)>(), "generic");
    }

    /// <summary>`CustomerName` / `customer_name` -> `Customer Name`.</summary>
    private static string Humanize(string field) =>
        string.Join(" ", Regex.Replace(field.Replace('_', ' '), "([a-z0-9])([A-Z])", "$1 $2").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    [GeneratedRegex(@"\bColumns\s*\.\s*Add\s*\(\s*""([^""]+)""(?:\s*,\s*""([^""]*)"")?")]
    private static partial Regex ColumnsAddRegex();

    [GeneratedRegex(@"\bDataPropertyName\s*=\s*""([^""]+)""")]
    private static partial Regex DataPropertyRegex();

    [GeneratedRegex(@"\b(?:Columns|Cells)\s*\[\s*""([^""]+)""\s*\]")]
    private static partial Regex ColumnIndexerRegex();

    [GeneratedRegex(@"\bDataSource\s*=\s*(?:new\s+(?:\w+\.)*(?:BindingList|List)\s*<\s*(\w+)\s*>|(\w+)\s*[;\.\)])")]
    private static partial Regex DataSourceRegex();

    [GeneratedRegex(@"^(?:\w+\.)*(?:BindingList|List|IList|IEnumerable|ICollection)\s*<\s*(\w+)\s*>$|^(\w+)\[\]$")]
    private static partial Regex RowTypeRegex();

    [GeneratedRegex(@"(?:new\s+(?:\w+\.)*TreeNode\s*\(|\bNodes\s*\.\s*Add\s*\()\s*""([^""]+)""")]
    private static partial Regex TreeNodeRegex();

    [GeneratedRegex(@"(?:new\s+(?:\w+\.)*ToolStripMenuItem\s*\(|\b(?:DropDownItems|Items)\s*\.\s*Add\s*\()\s*""([^""]+)""")]
    private static partial Regex MenuItemRegex();

    [GeneratedRegex(@"(?:new\s+(?:\w+\.)*ToolStrip(?:Button|DropDownButton|SplitButton)\s*\(|\bItems\s*\.\s*Add\s*\()\s*""([^""]+)""")]
    private static partial Regex ToolItemRegex();
}
