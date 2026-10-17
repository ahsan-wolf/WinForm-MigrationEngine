import { useMemo, useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type MouseEvent as ReactMouseEvent } from 'react';

export interface WfGridColumn {
  field: string; header: string; width?: number; format?: 'date' | 'number' | string;
  /** Cells of this column can be edited in place (double-click, or F2 / Enter on the selected row). `false` keeps it read-only even when the grid has `readOnly={false}`. */
  editable?: boolean;
  /** Custom cell text (the counterpart of WinForms `CellFormatting`): receives the cell value and its row. */
  formatter?: (value: unknown, row: GridRow) => string;
}
export type GridRow = Record<string, unknown>;
export interface WfGridCellEdit { row: GridRow; field: string; rowIndex: number; value: unknown }

export interface WfDataGridProps {
  rows: GridRow[];
  columns: WfGridColumn[];
  selectionMode?: 'none' | 'single' | 'multi';
  sortable?: boolean;
  /**
   * `true`: no cell can be edited. `false` (like a WinForms DataGridView with ReadOnly = false): every column is editable except those with `editable: false`.
   * Left out: only columns with `editable: true` can be edited.
   */
  readOnly?: boolean;
  /** Field that identifies a row. Selection follows the key when rows are reloaded; without it rows are identified by object identity. */
  rowKey?: string;
  /** Rows per page; omit or 0 to show every row. */
  pageSize?: number;
  /** Column widths can be dragged (default true). */
  resizable?: boolean;
  onRowSelect?: (rows: GridRow[]) => void;
  onCellClick?: (e: { row: GridRow; field: string; rowIndex: number }) => void;
  /** An edited cell: the grid never changes `rows`; the parent applies the value. Without this callback the edit is kept inside the grid. */
  onCellEdit?: (e: WfGridCellEdit) => void;
  id?: string;
  className?: string;
}

function formatCell(value: unknown, format: string | undefined): string {
  if (value === null || value === undefined) return '';
  if (format === 'd' || format === 'date') {
    const d = new Date(String(value));
    return Number.isNaN(d.getTime()) ? String(value) : d.toLocaleDateString();
  }
  if (format === 'number' && typeof value === 'number') return value.toLocaleString();
  return String(value);
}

const compare = (x: unknown, y: unknown): number =>
  x === y ? 0 : typeof x === 'number' && typeof y === 'number' ? x - y : String(x ?? '') < String(y ?? '') ? -1 : 1;

export function WfDataGrid({
  rows, columns, selectionMode = 'single', sortable = true, readOnly, rowKey, pageSize, resizable = true, onRowSelect, onCellClick, onCellEdit, id, className,
}: WfDataGridProps) {
  const keyOf = (row: GridRow): unknown => (rowKey ? row[rowKey] : row);
  const [selected, setSelected] = useState<ReadonlySet<unknown>>(new Set());
  const [sort, setSort] = useState<{ field: string; dir: 1 | -1 } | null>(null);
  const [page, setPage] = useState(0);
  const [widths, setWidths] = useState<Record<number, number>>({});
  const [editing, setEditing] = useState<{ key: unknown; col: number; draft: string } | null>(null);
  const [overrides, setOverrides] = useState<ReadonlyMap<unknown, Record<string, unknown>>>(new Map());
  const [activeField, setActiveField] = useState<string | null>(null);
  const [lastKey, setLastKey] = useState<unknown>(undefined);
  // The cell being edited, readable from event handlers that run after it was closed (a removed input may still fire blur).
  const activeEdit = useRef<{ key: unknown; col: number } | null>(null);

  const order = useMemo(() => {
    const idx = rows.map((_, i) => i);
    if (!sort) return idx;
    return idx.sort((a, b) => compare(rows[a][sort.field], rows[b][sort.field]) * sort.dir);
  }, [rows, sort]);

  const paged = !!pageSize && pageSize > 0;
  const pageCount = paged ? Math.max(1, Math.ceil(order.length / pageSize)) : 1;
  const current = Math.min(page, pageCount - 1);
  const visible = paged ? order.slice(current * pageSize, (current + 1) * pageSize) : order;

  const emitSelection = (keys: ReadonlySet<unknown>) => onRowSelect?.(rows.filter(r => keys.has(keyOf(r))));
  const pick = (rowIndex: number, additive: boolean) => {
    if (selectionMode === 'none') return;
    const key = keyOf(rows[rowIndex]);
    const next = new Set(selectionMode === 'multi' && additive ? selected : []);
    if (next.has(key)) next.delete(key); else next.add(key);
    setSelected(next);
    setLastKey(key);
    emitSelection(next);
  };

  const cellValue = (row: GridRow, field: string): unknown => overrides.get(keyOf(row))?.[field] ?? row[field];
  const canEdit = (c: WfGridColumn) => !readOnly && (c.editable ?? readOnly === false);

  const apply = (row: GridRow, rowIndex: number, c: WfGridColumn, value: unknown) => {
    if (onCellEdit) onCellEdit({ row, field: c.field, rowIndex, value });
    else setOverrides(prev => new Map(prev).set(keyOf(row), { ...prev.get(keyOf(row)), [c.field]: value }));
  };
  /** Ends the open editor and applies its value, once: Enter and the blur that follows it must not both commit. */
  const commit = (row: GridRow, rowIndex: number, ci: number, value: unknown) => {
    const open = activeEdit.current;
    if (!open || open.key !== keyOf(row) || open.col !== ci) return;
    activeEdit.current = null;
    setEditing(null);
    apply(row, rowIndex, columns[ci], value);
  };
  const cancel = () => { activeEdit.current = null; setEditing(null); };
  const parse = (c: WfGridColumn, draft: string, old: unknown): unknown =>
    c.format === 'number' || typeof old === 'number' ? (draft.trim() !== '' && !Number.isNaN(Number(draft)) ? Number(draft) : old) : draft;
  const beginEdit = (row: GridRow, c: WfGridColumn, ci: number) => {
    if (!canEdit(c) || typeof cellValue(row, c.field) === 'boolean') return;
    activeEdit.current = { key: keyOf(row), col: ci };
    setEditing({ key: keyOf(row), col: ci, draft: String(cellValue(row, c.field) ?? '') });
  };

  const startResize = (ci: number, e: ReactMouseEvent<HTMLElement>) => {
    e.preventDefault(); e.stopPropagation();
    const th = (e.currentTarget as HTMLElement).parentElement!;
    const startX = e.clientX; const startW = widths[ci] ?? th.getBoundingClientRect().width ?? 80;
    const move = (m: MouseEvent) => setWidths(w => ({ ...w, [ci]: Math.max(30, startW + m.clientX - startX) }));
    const up = () => { document.removeEventListener('mousemove', move); document.removeEventListener('mouseup', up); };
    document.addEventListener('mousemove', move); document.addEventListener('mouseup', up);
  };

  const onKeyDown = (e: ReactKeyboardEvent<HTMLDivElement>) => {
    if ((e.target as HTMLElement).tagName === 'INPUT') return;
    const at = visible.findIndex(i => keyOf(rows[i]) === lastKey);
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      const next = visible[Math.min(Math.max(at + (e.key === 'ArrowDown' ? 1 : -1), 0), visible.length - 1)];
      if (next !== undefined) { pick(next, false); e.preventDefault(); }
    } else if ((e.key === 'F2' || e.key === 'Enter') && at >= 0) {
      const ci = columns.findIndex(x => x.field === activeField && canEdit(x));
      const target = ci >= 0 ? ci : columns.findIndex(canEdit);
      if (target >= 0) { beginEdit(rows[visible[at]], columns[target], target); e.preventDefault(); }
    }
  };

  return (
    <div id={id} className={`wf-datagrid${className ? ' ' + className : ''}`} tabIndex={0} onKeyDown={onKeyDown}>
      <table role="grid" aria-multiselectable={selectionMode === 'multi'}>
        <thead>
          <tr>
            {columns.map((c, ci) => {
              const width = widths[ci] ?? c.width;
              return (
                <th key={`${ci}:${c.field}`} style={width ? { width, minWidth: width } : undefined} scope="col"
                    aria-sort={sort?.field === c.field ? (sort.dir === 1 ? 'ascending' : 'descending') : undefined}
                    onClick={sortable ? () => setSort(s => ({ field: c.field, dir: s?.field === c.field && s.dir === 1 ? -1 : 1 })) : undefined}>
                  {c.header}{sort?.field === c.field ? (sort.dir === 1 ? ' ▲' : ' ▼') : ''}
                  {resizable && <span className="wf-grid-resizer" role="separator" aria-orientation="vertical" onMouseDown={e => startResize(ci, e)} onClick={e => e.stopPropagation()} />}
                </th>
              );
            })}
          </tr>
        </thead>
        <tbody>
          {visible.map(i => {
            const row = rows[i];
            const isSelected = selected.has(keyOf(row));
            return (
              <tr key={rowKey ? String(row[rowKey]) : i} aria-selected={isSelected} className={isSelected ? 'wf-row-selected' : undefined} onClick={e => pick(i, e.ctrlKey || e.metaKey)}>
                {columns.map((c, ci) => {
                  const value = cellValue(row, c.field);
                  const isEditing = editing !== null && editing.key === keyOf(row) && editing.col === ci;
                  return (
                    <td key={`${ci}:${c.field}`} onClick={() => { setActiveField(c.field); onCellClick?.({ row, field: c.field, rowIndex: i }); }} onDoubleClick={() => beginEdit(row, c, ci)}>
                      {isEditing ? (
                        <input className="wf-grid-edit" autoFocus value={editing.draft} onChange={e => setEditing({ ...editing, draft: e.target.value })}
                               onClick={e => e.stopPropagation()}
                               onKeyDown={e => { if (e.key === 'Enter') commit(row, i, ci, parse(c, editing.draft, value)); else if (e.key === 'Escape') cancel(); e.stopPropagation(); }}
                               onBlur={() => commit(row, i, ci, parse(c, editing.draft, value))} />
                      ) : typeof value === 'boolean' && canEdit(c) ? (
                        <input type="checkbox" checked={value} onClick={e => e.stopPropagation()} onChange={e => apply(row, i, c, e.target.checked)} aria-label={c.header} />
                      ) : (c.formatter ? c.formatter(value, row) : formatCell(value, c.format))}
                    </td>
                  );
                })}
              </tr>
            );
          })}
          {rows.length === 0 && <tr><td className="wf-grid-empty" colSpan={Math.max(columns.length, 1)}>No rows</td></tr>}
        </tbody>
      </table>
      {paged && pageCount > 1 && (
        <div className="wf-grid-pager" role="navigation" aria-label="Pages">
          <button type="button" aria-label="Previous page" disabled={current === 0} onClick={() => setPage(current - 1)}>‹</button>
          <span aria-live="polite">Page {current + 1} of {pageCount}</span>
          <button type="button" aria-label="Next page" disabled={current >= pageCount - 1} onClick={() => setPage(current + 1)}>›</button>
        </div>
      )}
    </div>
  );
}
