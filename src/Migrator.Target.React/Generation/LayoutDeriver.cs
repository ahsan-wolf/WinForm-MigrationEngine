using Migrator.Engine.Projections;
using WebFormsMigrator.IR.Models;

namespace Migrator.Target.React.Generation;

/// <summary>Controls of one container, grouped the way CSS can express them. Never carries pixel positions (Skill 4).</summary>
public sealed class Arrangement
{
    public List<ControlNodeIr> Top { get; } = new();
    public List<ControlNodeIr> Left { get; } = new();
    public List<ControlNodeIr> Fill { get; } = new();
    public List<ControlNodeIr> Right { get; } = new();
    public List<ControlNodeIr> Bottom { get; } = new();

    /// <summary>Free-form controls clustered into visual rows (by Y), each ordered left to right (by X).</summary>
    public List<List<ControlNodeIr>> Rows { get; } = new();
}

public static class LayoutDeriver
{
    private const int RowTolerancePx = 8;

    /// <summary>WinForms defaults when the designer sets no Dock: MenuStrip/ToolStrip dock to the top, StatusStrip to the bottom.</summary>
    private static string? DefaultDock(string tagName) => tagName[(tagName.LastIndexOf('.') + 1)..] switch
    {
        "MenuStrip" or "ToolStrip" => "Top",
        "StatusStrip" => "Bottom",
        _ => null
    };

    public static Arrangement Arrange(IEnumerable<ControlNodeIr> children, IReadOnlyDictionary<string, ControlLayout> layout)
    {
        var arrangement = new Arrangement();
        var ordered = children.Select((node, index) => (node, index, l: layout.GetValueOrDefault(node.Id))).ToList();

        var free = new List<(ControlNodeIr node, int index, ControlLayout? l)>();
        foreach (var item in ordered)
        {
            var dock = item.l?.Dock ?? DefaultDock(item.node.TagName);
            switch (dock)
            {
                case "Top": arrangement.Top.Add(item.node); break;
                case "Bottom": arrangement.Bottom.Add(item.node); break;
                case "Left": arrangement.Left.Add(item.node); break;
                case "Right": arrangement.Right.Add(item.node); break;
                case "Fill": arrangement.Fill.Add(item.node); break;
                default: free.Add(item); break;
            }
        }

        SortBy(arrangement.Top, layout, byY: true);
        SortBy(arrangement.Bottom, layout, byY: true);
        SortBy(arrangement.Left, layout, byY: false);
        SortBy(arrangement.Right, layout, byY: false);

        // Free controls: those with a Location cluster into rows by Y; ones without keep declaration order, each on its own row.
        var rows = new List<List<(ControlNodeIr node, ControlLayout l)>>();
        var rowY = 0;
        foreach (var item in free.Where(f => f.l?.Location != null).OrderBy(f => f.l!.Location!.Y).ThenBy(f => f.l!.Location!.X))
        {
            var y = item.l!.Location!.Y;
            if (rows.Count == 0 || y - rowY > RowTolerancePx)
            {
                rows.Add(new());
                rowY = y;
            }
            rows[^1].Add((item.node, item.l));
        }
        foreach (var row in rows)
            arrangement.Rows.Add(row.OrderBy(r => r.l.Location!.X).ThenBy(r => r.l.TabIndex ?? int.MaxValue).Select(r => r.node).ToList());

        foreach (var item in free.Where(f => f.l?.Location == null).OrderBy(f => f.index))
            arrangement.Rows.Add(new List<ControlNodeIr> { item.node });

        return arrangement;
    }

    private static void SortBy(List<ControlNodeIr> nodes, IReadOnlyDictionary<string, ControlLayout> layout, bool byY)
    {
        var keyed = nodes.Select((n, i) => (n, i, p: layout.GetValueOrDefault(n.Id)?.Location)).ToList();
        keyed.Sort((a, b) =>
        {
            var c = (byY ? a.p?.Y ?? 0 : a.p?.X ?? 0).CompareTo(byY ? b.p?.Y ?? 0 : b.p?.X ?? 0);
            return c != 0 ? c : a.i.CompareTo(b.i);
        });
        nodes.Clear();
        nodes.AddRange(keyed.Select(k => k.n));
    }
}
