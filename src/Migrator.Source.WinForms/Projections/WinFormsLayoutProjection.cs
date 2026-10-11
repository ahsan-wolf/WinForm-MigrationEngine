using System.Text.Json;
using System.Text.RegularExpressions;
using Migrator.Engine.Projections;
using WebFormsMigrator.IR.Models;

namespace Migrator.Source.WinForms.Projections;

/// <summary>
/// Layout facet, the WinForms counterpart of html-css.ir.json. Derived only from the control attributes already in the
/// PageIr (including the pseudo-attributes the designer parser writes: TableLayout.*, ColumnStyles, RowStyles). Facts are recorded, never applied; generators derive flex/grid from them (Skill 4) and must not copy pixels.
/// </summary>
public sealed partial class WinFormsLayoutProjection : IIrProjection
{
    public string FileSuffix => "layout";

    public object Project(PageIr page)
    {
        var list = new List<ControlLayout>();
        if (page.RootControl is not null) Collect(page.RootControl, isRoot: true, list);
        return new LayoutFile(page.Name, list);
    }

    private static void Collect(ControlNodeIr node, bool isRoot, List<ControlLayout> into)
    {
        var a = node.Attributes;
        into.Add(new ControlLayout(
            node.Id,
            Dock: a.TryGetValue("Dock", out var dock) ? LastToken(dock) : null,
            Anchor: a.TryGetValue("Anchor", out var anchor) ? ParseAnchor(anchor) : null,
            Location: a.TryGetValue("Location", out var loc) ? ParsePoint(loc) : null,
            // A Form/UserControl is sized through ClientSize in designer code.
            Size: ParseSize(a.GetValueOrDefault("Size") ?? (isRoot ? a.GetValueOrDefault("ClientSize") : null)),
            TabIndex: int.TryParse(a.GetValueOrDefault("TabIndex"), out var tab) ? tab : null,
            Margin: ParseInsets(a.GetValueOrDefault("Margin")),
            Padding: ParseInsets(a.GetValueOrDefault("Padding")),
            Visible: bool.TryParse(a.GetValueOrDefault("Visible"), out var visible) ? visible : null,
            Enabled: bool.TryParse(a.GetValueOrDefault("Enabled"), out var enabled) ? enabled : null,
            TableLayout: new TableLayoutCell(Int(a, "TableLayout.Row"), Int(a, "TableLayout.Column"), Int(a, "TableLayout.RowSpan"), Int(a, "TableLayout.ColumnSpan")),
            TableDefinition: ParseTableDefinition(a)));

        foreach (var child in node.Children) Collect(child, isRoot: false, into);
    }

    private static int? Int(Dictionary<string, string> attrs, string key) =>
        int.TryParse(attrs.GetValueOrDefault(key), out var n) ? n : null;

    private static TableDefinition? ParseTableDefinition(Dictionary<string, string> attrs)
    {
        var columns = ParseTracks(attrs.GetValueOrDefault("ColumnStyles"));
        var rows = ParseTracks(attrs.GetValueOrDefault("RowStyles"));
        return columns.Count == 0 && rows.Count == 0 ? null : new TableDefinition(columns, rows);
    }

    private static List<TrackSize> ParseTracks(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new List<TrackSize>();
        try
        {
            return JsonSerializer.Deserialize<List<TrackSize>>(json) ?? new List<TrackSize>();
        }
        catch (JsonException)
        {
            return new List<TrackSize>();
        }
    }

    private static string LastToken(string value) => value[(value.LastIndexOf('.') + 1)..].Trim();

    private static List<string>? ParseAnchor(string value)
    {
        var styles = AnchorRegex().Matches(value).Select(m => m.Groups[1].Value).Distinct().ToList();
        return styles.Count == 0 ? null : styles;
    }

    private static LayoutPoint? ParsePoint(string? value) =>
        Numbers(value) is [var x, var y] ? new LayoutPoint(x, y) : null;

    private static LayoutSize? ParseSize(string? value) =>
        Numbers(value) is [var w, var h] ? new LayoutSize(w, h) : null;

    /// <summary>Padding/Margin: one argument = all sides, four = left, top, right, bottom.</summary>
    private static LayoutInsets? ParseInsets(string? value) => Numbers(value) switch
    {
        [var all] => new LayoutInsets(all, all, all, all),
        [var l, var t, var r, var b] => new LayoutInsets(l, t, r, b),
        _ => null
    };

    /// <summary>Integer arguments of the first "(...)" in an expression like new System.Drawing.Point(12, 12); null/empty if absent.</summary>
    private static int[] Numbers(string? expression)
    {
        if (expression is null) return Array.Empty<int>();
        var open = expression.IndexOf('(');
        var close = expression.LastIndexOf(')');
        if (open < 0 || close < open) return Array.Empty<int>();

        var parts = expression[(open + 1)..close].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var numbers = new List<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var n)) return Array.Empty<int>(); // not a plain literal list: record nothing rather than guess
            numbers.Add(n);
        }
        return numbers.ToArray();
    }

    [GeneratedRegex(@"AnchorStyles\.(Top|Bottom|Left|Right|None)")]
    private static partial Regex AnchorRegex();
}
