import { useState, type KeyboardEvent, type MouseEvent } from 'react';

/** One row. `text` is the first column, `subItems[k]` is column k + 1 (WinForms ListViewItem / SubItems). */
export interface WfListItem {
  text: string;
  subItems: string[];
  checked: boolean;
  selected: boolean;
  tag?: unknown;
  imageKey?: string;
  name?: string;
}

export interface WfListColumn { header: string; width?: number }

/** `new ListViewItem(text)` */
export function wfListItem(text = '', subItems: string[] = [], imageKey?: string): WfListItem {
  return { text, subItems, checked: false, selected: false, imageKey };
}

/** `new ListViewItem(string[] values)`: the first value is the item text, the rest are sub items. */
export function wfListItemFromArray(values: string[]): WfListItem {
  return wfListItem(values[0] ?? '', values.slice(1));
}

export interface WfListViewProps {
  columns: WfListColumn[];
  items: WfListItem[];
  checkBoxes?: boolean;
  multiSelect?: boolean;
  disabled?: boolean;
  /** Called with the selected row indices after a click or arrow key. The caller applies them to `items[i].selected`. */
  onSelectionChange?: (indices: number[]) => void;
  onItemCheckedChange?: (index: number, checked: boolean) => void;
  /** Double-click or Enter on a row. */
  onItemActivate?: (index: number) => void;
  onClick?: () => void;
  onDoubleClick?: () => void;
  onKeyUp?: (key: string) => void;
  onColumnClick?: (column: number) => void;
  id?: string;
  className?: string;
}

const selectedIndices = (items: WfListItem[]): number[] => items.flatMap((item, i) => (item.selected ? [i] : []));

/** Details-view list with row selection (click, Ctrl/Shift for several), optional check boxes and keyboard navigation. Controlled: it renders `items` as given. */
export function WfListView({
  columns, items, checkBoxes, multiSelect = true, disabled, onSelectionChange, onItemCheckedChange, onItemActivate,
  onClick, onDoubleClick, onKeyUp, onColumnClick, id, className,
}: WfListViewProps) {
  const [anchor, setAnchor] = useState(0);
  const columnCount = Math.max(columns.length, 1, ...items.map(i => 1 + i.subItems.length));

  const select = (indices: number[]) => onSelectionChange?.(indices);

  const rowClick = (index: number, e: MouseEvent) => {
    if (disabled) return;
    if (multiSelect && e.shiftKey) {
      const [from, to] = [Math.min(anchor, index), Math.max(anchor, index)];
      select(Array.from({ length: to - from + 1 }, (_, k) => from + k));
    } else if (multiSelect && (e.ctrlKey || e.metaKey)) {
      const current = selectedIndices(items);
      select(current.includes(index) ? current.filter(i => i !== index) : [...current, index]);
      setAnchor(index);
    } else {
      select([index]);
      setAnchor(index);
    }
  };

  const keyDown = (e: KeyboardEvent) => {
    if (disabled || items.length === 0) return;
    const current = selectedIndices(items);
    const last = current.length > 0 ? current[current.length - 1] : -1;
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      const next = Math.min(Math.max(last + (e.key === 'ArrowDown' ? 1 : -1), 0), items.length - 1);
      select([next]);
      setAnchor(next);
    } else if (e.key === 'Enter' && last >= 0) {
      onItemActivate?.(last);
    } else if (e.key === 'a' && (e.ctrlKey || e.metaKey) && multiSelect) {
      e.preventDefault();
      select(items.map((_, i) => i));
    }
  };

  return (
    <div id={id} className={`wf-listview${className ? ' ' + className : ''}`} tabIndex={0} role="grid" aria-multiselectable={multiSelect}
         aria-disabled={disabled} onKeyDown={keyDown} onKeyUp={e => onKeyUp?.(e.key)} onClick={onClick} onDoubleClick={onDoubleClick}>
      <table>
        <thead>
          <tr>
            {checkBoxes && <th className="wf-listview-check" scope="col" />}
            {Array.from({ length: columnCount }, (_, c) => (
              <th key={c} scope="col" style={columns[c]?.width ? { width: columns[c].width } : undefined} onClick={() => onColumnClick?.(c)}>
                {columns[c]?.header ?? ''}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {items.map((item, i) => (
            <tr key={i} aria-selected={item.selected} className={item.selected ? 'wf-row-selected' : undefined}
                onClick={e => rowClick(i, e)} onDoubleClick={() => onItemActivate?.(i)}>
              {checkBoxes && (
                <td className="wf-listview-check">
                  <input type="checkbox" checked={item.checked} disabled={disabled} aria-label="Select row"
                         onClick={e => e.stopPropagation()} onChange={e => onItemCheckedChange?.(i, e.target.checked)} />
                </td>
              )}
              {Array.from({ length: columnCount }, (_, c) => <td key={c}>{c === 0 ? item.text : item.subItems[c - 1] ?? ''}</td>)}
            </tr>
          ))}
          {items.length === 0 && <tr><td className="wf-grid-empty" colSpan={columnCount + (checkBoxes ? 1 : 0)}>No items</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
