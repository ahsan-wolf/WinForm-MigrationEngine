import { Component, effect, input, output, viewChild, type ElementRef } from '@angular/core';
import type { GridRow, WfGridCellEdit, WfGridCellEvent, WfGridColumn } from '@wfm/ui-core';

const HOST = { style: 'display: contents' } as const;

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

interface Cell { value: unknown; text: string }

/** Table of rows over `GridRow` objects (DataGridView): sorting, paging, column resizing, cell editing, selection by click and arrow keys, cell events. */
@Component({
  selector: 'wf-data-grid',
  standalone: true,
  host: HOST,
  template: `<div [attr.id]="controlId()" [class]="'wf-datagrid' + (className() ? ' ' + className() : '')" tabindex="0" (keydown)="onKeyDown($event)">
      <table role="grid" [attr.aria-multiselectable]="selectionMode() === 'multi'">
        <thead>
          <tr>
            @for (c of columns(); track $index; let ci = $index) {
              <th scope="col" [style.width.px]="widthOf(ci, c)" [style.min-width.px]="widthOf(ci, c)" [attr.aria-sort]="ariaSort(c)" (click)="sortable() && sortBy(c)">
                {{ c.header }}{{ sortMark(c) }}
                @if (resizable()) { <span class="wf-grid-resizer" role="separator" aria-orientation="vertical" (mousedown)="startResize(ci, $event)" (click)="$event.stopPropagation()"></span> }
              </th>
            }
          </tr>
        </thead>
        <tbody>
          @for (i of visible(); track i) {
            <tr [attr.aria-selected]="isSelected(i)" [class.wf-row-selected]="isSelected(i)" (click)="pick(i, $event.ctrlKey || $event.metaKey)">
              @for (c of columns(); track $index; let ci = $index) {
                @let cell = cellOf(i, ci, c);
                <td (click)="cellClick(i, ci, c)" (dblclick)="cellDoubleClick(i, ci, c)">
                  @if (isEditing(i, ci)) {
                    <input #editor class="wf-grid-edit" [value]="editing!.draft" (input)="editing!.draft = $any($event.target).value" (click)="$event.stopPropagation()"
                           (keydown)="onEditKey($event, i, ci, c, cell.value)" (blur)="commit(i, ci, parse(c, editing?.draft ?? '', cell.value))" />
                  } @else if (isBoolean(cell.value) && canEdit(c)) {
                    <input type="checkbox" [checked]="!!cell.value" (click)="$event.stopPropagation()" (change)="apply(i, c, $any($event.target).checked)" [attr.aria-label]="c.header" />
                  } @else { {{ cell.text }} }
                </td>
              }
            </tr>
          }
          @if (rows().length === 0) { <tr><td class="wf-grid-empty" [attr.colspan]="Math.max(columns().length, 1)">No rows</td></tr> }
        </tbody>
      </table>
      @if (paged() && pageCount() > 1) {
        <div class="wf-grid-pager" role="navigation" aria-label="Pages">
          <button type="button" aria-label="Previous page" [disabled]="current() === 0" (click)="page = current() - 1">‹</button>
          <span aria-live="polite">Page {{ current() + 1 }} of {{ pageCount() }}</span>
          <button type="button" aria-label="Next page" [disabled]="current() >= pageCount() - 1" (click)="page = current() + 1">›</button>
        </div>
      }
    </div>`,
})
export class WfDataGrid {
  rows = input<GridRow[]>([]);
  columns = input<WfGridColumn[]>([]);
  selectionMode = input<'none' | 'single' | 'multi'>('single');
  sortable = input(true);
  /**
   * `true`: no cell can be edited. `false` (like a WinForms DataGridView with ReadOnly = false): every column is editable except those with `editable: false`.
   * Left out: only columns with `editable: true` can be edited.
   */
  readOnly = input<boolean | undefined>();
  /** Field that identifies a row. Selection follows the key when rows are reloaded; without it rows are identified by object identity. */
  rowKey = input<string | undefined>();
  /** Rows per page; omit or 0 to show every row. */
  pageSize = input<number | undefined>();
  resizable = input(true);
  controlId = input<string | undefined>();
  className = input<string | undefined>();

  wfRowSelect = output<GridRow[]>();
  wfCellClick = output<WfGridCellEvent>();
  wfCellDoubleClick = output<WfGridCellEvent>();
  /** Emitted for every cell shown, before it is drawn; a subscriber may replace `value` (CellFormatting). Keep it free of side effects: it runs on every change detection. */
  wfCellFormatting = output<WfGridCellEvent>();
  /** An edited cell: the grid never changes `rows`; the page applies the value (the grid shows the edit meanwhile). */
  wfCellEdit = output<WfGridCellEdit>();

  protected readonly Math = Math;
  private selected = new Set<unknown>();
  private sort: { field: string; dir: 1 | -1 } | null = null;
  protected page = 0;
  private widths: Record<number, number> = {};
  protected editing: { key: unknown; col: number; draft: string } | null = null;
  private overrides = new Map<unknown, Record<string, unknown>>();
  private activeField: string | null = null;
  private lastKey: unknown = undefined;
  private readonly editor = viewChild<ElementRef<HTMLInputElement>>('editor');

  constructor() {
    effect(() => this.editor()?.nativeElement.focus());
  }

  private keyOf(row: GridRow): unknown { return this.rowKey() ? row[this.rowKey()!] : row; }

  /** Row indexes in display order, recomputed on every change detection: the page model mutates `rows` in place (push, splice). */
  private order(): number[] {
    const rows = this.rows();
    const order = rows.map((_, i) => i);
    if (this.sort) order.sort((a, b) => compare(rows[a][this.sort!.field], rows[b][this.sort!.field]) * this.sort!.dir);
    return order;
  }

  protected paged = () => !!this.pageSize() && this.pageSize()! > 0;
  protected pageCount = () => (this.paged() ? Math.max(1, Math.ceil(this.rows().length / this.pageSize()!)) : 1);
  protected current = () => Math.min(this.page, this.pageCount() - 1);
  protected visible(): number[] {
    const order = this.order();
    return this.paged() ? order.slice(this.current() * this.pageSize()!, (this.current() + 1) * this.pageSize()!) : order;
  }

  protected isSelected(i: number): boolean { return this.selected.has(this.keyOf(this.rows()[i])); }
  protected isBoolean = (value: unknown) => typeof value === 'boolean';
  protected canEdit(c: WfGridColumn): boolean { return !this.readOnly() && (c.editable ?? this.readOnly() === false); }
  protected isEditing(i: number, ci: number): boolean { return this.editing !== null && this.editing.key === this.keyOf(this.rows()[i]) && this.editing.col === ci; }

  protected widthOf(ci: number, c: WfGridColumn): number | null { return this.widths[ci] ?? c.width ?? null; }
  protected ariaSort(c: WfGridColumn): string | null { return this.sort?.field === c.field ? (this.sort.dir === 1 ? 'ascending' : 'descending') : null; }
  protected sortMark(c: WfGridColumn): string { return this.sort?.field === c.field ? (this.sort.dir === 1 ? ' ▲' : ' ▼') : ''; }
  protected sortBy(c: WfGridColumn): void { this.sort = { field: c.field, dir: this.sort?.field === c.field && this.sort.dir === 1 ? -1 : 1 }; }

  protected cellOf(i: number, ci: number, c: WfGridColumn): Cell {
    const row = this.rows()[i];
    const value = this.overrides.get(this.keyOf(row))?.[c.field] ?? row[c.field];
    const formatting: WfGridCellEvent = { row, field: c.field, rowIndex: i, columnIndex: ci, value };
    this.wfCellFormatting.emit(formatting);
    return { value, text: c.formatter ? c.formatter(formatting.value, row) : formatCell(formatting.value, c.format) };
  }

  protected pick(rowIndex: number, additive: boolean): void {
    if (this.selectionMode() === 'none') return;
    const key = this.keyOf(this.rows()[rowIndex]);
    const next = new Set(this.selectionMode() === 'multi' && additive ? this.selected : []);
    if (next.has(key)) next.delete(key); else next.add(key);
    this.selected = next;
    this.lastKey = key;
    this.wfRowSelect.emit(this.rows().filter((r) => next.has(this.keyOf(r))));
  }

  protected cellClick(i: number, ci: number, c: WfGridColumn): void {
    this.activeField = c.field;
    this.wfCellClick.emit({ row: this.rows()[i], field: c.field, rowIndex: i, columnIndex: ci });
  }

  protected cellDoubleClick(i: number, ci: number, c: WfGridColumn): void {
    this.wfCellDoubleClick.emit({ row: this.rows()[i], field: c.field, rowIndex: i, columnIndex: ci });
    this.beginEdit(i, ci, c);
  }

  protected apply(i: number, c: WfGridColumn, value: unknown): void {
    const row = this.rows()[i];
    // The edit shows right away; the page also receives it and applies it to its row (an output cannot tell whether anyone listens).
    this.overrides.set(this.keyOf(row), { ...this.overrides.get(this.keyOf(row)), [c.field]: value });
    this.wfCellEdit.emit({ row, field: c.field, rowIndex: i, value });
  }

  /** Ends the open editor and applies its value, once: Enter and the blur that follows it must not both commit. */
  protected commit(i: number, ci: number, value: unknown): void {
    const open = this.editing;
    if (!open || open.key !== this.keyOf(this.rows()[i]) || open.col !== ci) return;
    this.editing = null;
    this.apply(i, this.columns()[ci], value);
  }

  protected parse(c: WfGridColumn, draft: string, old: unknown): unknown {
    return c.format === 'number' || typeof old === 'number' ? (draft.trim() !== '' && !Number.isNaN(Number(draft)) ? Number(draft) : old) : draft;
  }

  private beginEdit(i: number, ci: number, c: WfGridColumn): void {
    const row = this.rows()[i];
    const current = this.overrides.get(this.keyOf(row))?.[c.field] ?? row[c.field];
    if (!this.canEdit(c) || typeof current === 'boolean') return;
    this.editing = { key: this.keyOf(row), col: ci, draft: String(current ?? '') };
  }

  protected onEditKey(e: KeyboardEvent, i: number, ci: number, c: WfGridColumn, value: unknown): void {
    if (e.key === 'Enter') this.commit(i, ci, this.parse(c, this.editing?.draft ?? '', value));
    else if (e.key === 'Escape') this.editing = null;
    e.stopPropagation();
  }

  protected startResize(ci: number, e: MouseEvent): void {
    e.preventDefault();
    e.stopPropagation();
    const th = (e.currentTarget as HTMLElement).parentElement!;
    const startX = e.clientX;
    const startW = this.widths[ci] ?? th.getBoundingClientRect().width ?? 80;
    const move = (m: MouseEvent) => { this.widths = { ...this.widths, [ci]: Math.max(30, startW + m.clientX - startX) }; };
    const up = () => { document.removeEventListener('mousemove', move); document.removeEventListener('mouseup', up); };
    document.addEventListener('mousemove', move);
    document.addEventListener('mouseup', up);
  }

  protected onKeyDown(e: KeyboardEvent): void {
    if ((e.target as HTMLElement).tagName === 'INPUT') return;
    const visible = this.visible();
    const rows = this.rows();
    const at = visible.findIndex((i) => this.keyOf(rows[i]) === this.lastKey);
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      const next = visible[Math.min(Math.max(at + (e.key === 'ArrowDown' ? 1 : -1), 0), visible.length - 1)];
      if (next !== undefined) { this.pick(next, false); e.preventDefault(); }
    } else if ((e.key === 'F2' || e.key === 'Enter') && at >= 0) {
      const columns = this.columns();
      const ci = columns.findIndex((x) => x.field === this.activeField && this.canEdit(x));
      const target = ci >= 0 ? ci : columns.findIndex((x) => this.canEdit(x));
      if (target >= 0) { this.beginEdit(visible[at], target, columns[target]); e.preventDefault(); }
    }
  }
}
