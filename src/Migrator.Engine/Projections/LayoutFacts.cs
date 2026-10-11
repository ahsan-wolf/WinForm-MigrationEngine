namespace Migrator.Engine.Projections;

// Shape of {Unit}.layout.ir.json. Shared so a target engine can read the file without referencing the source engine.

public sealed record LayoutPoint(int X, int Y);

public sealed record LayoutSize(int Width, int Height);

public sealed record LayoutInsets(int Left, int Top, int Right, int Bottom);

/// <summary>The control's cell inside a TableLayoutPanel; all null when the control is not in a table.</summary>
public sealed record TableLayoutCell(int? Row, int? Column, int? RowSpan, int? ColumnSpan);

/// <summary>One column/row track of a TableLayoutPanel (SizeType: Absolute | Percent | AutoSize).</summary>
public sealed record TrackSize(string SizeType, double? Value);

/// <summary>Set on the TableLayoutPanel itself (null on every other control).</summary>
public sealed record TableDefinition(List<TrackSize> Columns, List<TrackSize> Rows);

/// <summary>Layout facts for one control. A null means "not set in the designer" (WinForms default applies).</summary>
public sealed record ControlLayout(
    string Id,
    string? Dock,
    List<string>? Anchor,
    LayoutPoint? Location,
    LayoutSize? Size,
    int? TabIndex,
    LayoutInsets? Margin,
    LayoutInsets? Padding,
    bool? Visible,
    bool? Enabled,
    TableLayoutCell TableLayout,
    TableDefinition? TableDefinition);

public sealed record LayoutFile(string Name, List<ControlLayout> Controls);
