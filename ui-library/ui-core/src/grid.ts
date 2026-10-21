export interface WfGridColumn {
  field: string; header: string; width?: number; format?: 'date' | 'number' | string;
  /** Cells of this column can be edited in place (double-click, or F2 / Enter on the selected row). `false` keeps it read-only even when the grid has `readOnly={false}`. */
  editable?: boolean;
  /** Custom cell text (the counterpart of WinForms `CellFormatting`): receives the cell value and its row. */
  formatter?: (value: unknown, row: GridRow) => string;
}
export type GridRow = Record<string, unknown>;
export interface WfGridCellEdit { row: GridRow; field: string; rowIndex: number; value: unknown }
/** A click or double click on a cell: the counterpart of DataGridViewCellEventArgs (`RowIndex`, `ColumnIndex`). */
export interface WfGridCellEvent {
  row: GridRow; field: string; rowIndex: number; columnIndex: number;
  /** Only in `onCellFormatting`: the cell's value, which the handler may replace (DataGridViewCellFormattingEventArgs.Value). */
  value?: unknown;
}

/**
 * The page model of a data grid, as generated pages keep it: the columns, the rows (one object per row, keyed by column field) and the selected rows.
 * The helpers below are the counterparts of the DataGridView members WinForms code uses (`Rows.Add`, `Rows[i].Cells[k].Value`, `Columns.Add`, ...), so translated
 * code reads like the original. Columns may be created at run time, so a column index is always resolved through `columns` (never a fixed field).
 */
export interface WfGridModel {
  columns: WfGridColumn[];
  rows: GridRow[];
  selected: GridRow[];
}

/** `Columns.Add(name, headerText)`: returns the new column's index (`Columns.Count` before the call). */
export function wfGridAddColumn(grid: WfGridModel, name: string, header?: string, width?: number): number {
  grid.columns.push({ field: name, header: header ?? name, ...(width !== undefined ? { width } : {}) });
  return grid.columns.length - 1;
}

/** The column a cell key names: a column index, or the column's field, name or header text. */
function column(grid: WfGridModel, key: number | string): WfGridColumn | undefined {
  if (typeof key === 'number') return grid.columns[key];
  return grid.columns.find(c => c.field === key) ?? grid.columns.find(c => c.header === key);
}

/** `Rows.Add(v0, v1, ...)`: one value per column in order (extra values get generated columns); returns the new row's index. */
export function wfGridAddRow(grid: WfGridModel, values: unknown[] = []): number {
  while (grid.columns.length < values.length) wfGridAddColumn(grid, `column${grid.columns.length + 1}`);
  const row: GridRow = {};
  grid.columns.forEach((c, i) => { row[c.field] = values[i] ?? null; });
  grid.rows.push(row);
  return grid.rows.length - 1;
}

/** `Rows.Clear()` also forgets the selection. */
export function wfGridClearRows(grid: WfGridModel): void {
  grid.rows.splice(0);
  grid.selected.splice(0);
}

/** `Rows.Remove(row)` / `Rows.RemoveAt(i)` by row object. */
export function wfGridRemoveRow(grid: WfGridModel, row: GridRow): void {
  const at = grid.rows.indexOf(row);
  if (at >= 0) grid.rows.splice(at, 1);
  const picked = grid.selected.indexOf(row);
  if (picked >= 0) grid.selected.splice(picked, 1);
}

/** `row.Cells[key].Value` */
export function wfGridCell(grid: WfGridModel, row: GridRow | undefined, key: number | string): unknown {
  const c = column(grid, key);
  return row && c ? row[c.field] : undefined;
}

/** `row.Cells[key].Value = value` */
export function wfGridSetCell(grid: WfGridModel, row: GridRow | undefined, key: number | string, value: unknown): void {
  const c = column(grid, key);
  if (row && c) row[c.field] = value;
}
