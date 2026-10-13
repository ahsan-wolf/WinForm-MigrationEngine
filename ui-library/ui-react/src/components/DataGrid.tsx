import { useMemo, useState } from 'react';

export interface WfGridColumn { field: string; header: string; width?: number; format?: 'date' | 'number' | string }
export type GridRow = Record<string, unknown>;

export interface WfDataGridProps {
  rows: GridRow[];
  columns: WfGridColumn[];
  selectionMode?: 'none' | 'single' | 'multi';
  sortable?: boolean;
  readOnly?: boolean;
  onRowSelect?: (rows: GridRow[]) => void;
  onCellClick?: (e: { row: GridRow; field: string; rowIndex: number }) => void;
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

export function WfDataGrid({ rows, columns, selectionMode = 'single', sortable = true, onRowSelect, onCellClick, id, className }: WfDataGridProps) {
  const [selected, setSelected] = useState<ReadonlySet<number>>(new Set());
  const [sort, setSort] = useState<{ field: string; dir: 1 | -1 } | null>(null);

  const order = useMemo(() => {
    const idx = rows.map((_, i) => i);
    if (!sort) return idx;
    return idx.sort((a, b) => {
      const x = rows[a][sort.field]; const y = rows[b][sort.field];
      return (x === y ? 0 : String(x ?? '') < String(y ?? '') ? -1 : 1) * sort.dir;
    });
  }, [rows, sort]);

  const pick = (rowIndex: number, additive: boolean) => {
    if (selectionMode === 'none') return;
    const next = new Set(selectionMode === 'multi' && additive ? selected : []);
    if (next.has(rowIndex)) next.delete(rowIndex); else next.add(rowIndex);
    setSelected(next);
    onRowSelect?.([...next].map(i => rows[i]));
  };

  return (
    <div id={id} className={`wf-datagrid${className ? ' ' + className : ''}`}>
      <table role="grid" aria-multiselectable={selectionMode === 'multi'}>
        <thead>
          <tr>
            {columns.map((c, ci) => (
              <th key={`${ci}:${c.field}`} style={c.width ? { width: c.width } : undefined} scope="col"
                  aria-sort={sort?.field === c.field ? (sort.dir === 1 ? 'ascending' : 'descending') : undefined}
                  onClick={sortable ? () => setSort(s => ({ field: c.field, dir: s?.field === c.field && s.dir === 1 ? -1 : 1 })) : undefined}>
                {c.header}{sort?.field === c.field ? (sort.dir === 1 ? ' ▲' : ' ▼') : ''}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {order.map(i => (
            <tr key={i} aria-selected={selected.has(i)} className={selected.has(i) ? 'wf-row-selected' : undefined}
                onClick={e => pick(i, e.ctrlKey || e.metaKey)}>
              {columns.map((c, ci) => (
                <td key={`${ci}:${c.field}`} onClick={() => onCellClick?.({ row: rows[i], field: c.field, rowIndex: i })}>{formatCell(rows[i][c.field], c.format)}</td>
              ))}
            </tr>
          ))}
          {rows.length === 0 && <tr><td className="wf-grid-empty" colSpan={Math.max(columns.length, 1)}>No rows</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
