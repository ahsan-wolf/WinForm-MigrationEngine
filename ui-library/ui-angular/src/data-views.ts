import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, ElementRef, inject, input, output, signal } from '@angular/core';
import { wfImageListUrl, type WfImageList, type WfListColumn, type WfListItem, type WfTreeNode } from '@wfm/ui-core';

const HOST = { style: 'display: contents' } as const;

// ------------------------------------------------------------------------------------------------------------- ListView

/** Details-view list with row selection (click, Ctrl/Shift for several), optional check boxes and keyboard navigation. Renders `items` as given. */
@Component({
  selector: 'wf-list-view',
  standalone: true,
  host: HOST,
  template: `<div [attr.id]="controlId()" [class]="'wf-listview' + (className() ? ' ' + className() : '')" tabindex="0" role="grid" [attr.aria-multiselectable]="multiSelect()"
                  [attr.aria-disabled]="disabled() ? 'true' : null" (keydown)="keyDown($event)" (keyup)="wfKeyUp.emit($event.key)" (click)="wfClick.emit()" (dblclick)="wfDoubleClick.emit()">
      <table>
        <thead>
          <tr>
            @if (checkBoxes()) { <th class="wf-listview-check" scope="col"></th> }
            @for (c of columnIndexes(); track c) {
              <th scope="col" [style.width.px]="columns()[c]?.width ?? null" (click)="wfColumnClick.emit(c)">{{ columns()[c]?.header ?? '' }}</th>
            }
          </tr>
        </thead>
        <tbody>
          @for (item of items(); track $index; let i = $index) {
            <tr [attr.aria-selected]="item.selected" [class.wf-row-selected]="item.selected" (click)="rowClick(i, $event)" (dblclick)="wfItemActivate.emit(i)">
              @if (checkBoxes()) {
                <td class="wf-listview-check">
                  <input type="checkbox" [checked]="item.checked" [disabled]="!!disabled()" aria-label="Select row" (click)="$event.stopPropagation()"
                         (change)="wfItemCheckedChange.emit([i, $any($event.target).checked])" />
                </td>
              }
              @for (c of columnIndexes(); track c) {
                <td>@if (c === 0 && iconOf(item)) { <img class="wf-listview-icon" [src]="iconOf(item)" alt="" /> }{{ c === 0 ? item.text : item.subItems[c - 1] }}</td>
              }
            </tr>
          }
          @if (items().length === 0) { <tr><td class="wf-grid-empty" [attr.colspan]="columnCount() + (checkBoxes() ? 1 : 0)">No items</td></tr> }
        </tbody>
      </table>
    </div>`,
})
export class WfListView {
  columns = input<WfListColumn[]>([]);
  items = input<WfListItem[]>([]);
  /** Where `imageKey` / `imageIndex` of the items are looked up (WinForms ListView.SmallImageList). */
  imageList = input<WfImageList | undefined>();
  checkBoxes = input<boolean | undefined>(false);
  multiSelect = input(true);
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();

  /** The selected row indices after a click or arrow key; the page applies them to `items[i].selected`. */
  wfSelectionChange = output<number[]>();
  /** `[index, checked]` */
  wfItemCheckedChange = output<[number, boolean]>();
  /** Double-click or Enter on a row. */
  wfItemActivate = output<number>();
  wfClick = output<void>();
  wfDoubleClick = output<void>();
  wfKeyUp = output<string>();
  wfColumnClick = output<number>();

  private anchor = 0;
  protected columnCount = computed(() => Math.max(this.columns().length, 1, ...this.items().map((i) => 1 + i.subItems.length)));
  protected columnIndexes = computed(() => Array.from({ length: this.columnCount() }, (_, c) => c));

  protected iconOf(item: WfListItem): string | undefined { return wfImageListUrl(this.imageList(), item.imageKey ?? item.imageIndex); }
  private selectedIndices = () => this.items().flatMap((item, i) => (item.selected ? [i] : []));

  protected rowClick(index: number, e: MouseEvent): void {
    if (this.disabled()) return;
    if (this.multiSelect() && e.shiftKey) {
      const [from, to] = [Math.min(this.anchor, index), Math.max(this.anchor, index)];
      this.wfSelectionChange.emit(Array.from({ length: to - from + 1 }, (_, k) => from + k));
    } else if (this.multiSelect() && (e.ctrlKey || e.metaKey)) {
      const current = this.selectedIndices();
      this.wfSelectionChange.emit(current.includes(index) ? current.filter((i) => i !== index) : [...current, index]);
      this.anchor = index;
    } else {
      this.wfSelectionChange.emit([index]);
      this.anchor = index;
    }
  }

  protected keyDown(e: KeyboardEvent): void {
    const items = this.items();
    if (this.disabled() || items.length === 0) return;
    const current = this.selectedIndices();
    const last = current.length > 0 ? current[current.length - 1] : -1;
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      const next = Math.min(Math.max(last + (e.key === 'ArrowDown' ? 1 : -1), 0), items.length - 1);
      this.wfSelectionChange.emit([next]);
      this.anchor = next;
    } else if (e.key === 'Enter' && last >= 0) {
      this.wfItemActivate.emit(last);
    } else if (e.key === 'a' && (e.ctrlKey || e.metaKey) && this.multiSelect()) {
      e.preventDefault();
      this.wfSelectionChange.emit(items.map((_, i) => i));
    }
  }
}

// ------------------------------------------------------------------------------------------------------------- TreeView

/** Hierarchical tree (TreeView): expand/collapse, single selection, optional check boxes, arrow-key navigation. The nodes are plain data owned by the page. */
@Component({
  selector: 'wf-tree-view',
  standalone: true,
  imports: [NgTemplateOutlet],
  host: HOST,
  template: `<ul role="tree" [attr.id]="controlId()" [attr.title]="tooltip()" [attr.aria-disabled]="disabled() ? 'true' : null" [class]="'wf-tree' + (className() ? ' ' + className() : '')">
      <ng-container *ngTemplateOutlet="level; context: { $implicit: nodes() }"></ng-container>
    </ul>
    <ng-template #level let-list>
      @for (node of list; track $index) {
        <li role="treeitem" [attr.aria-expanded]="hasChildren(node) ? !!node.expanded : null" [attr.aria-selected]="selectedNow() === node">
          <div [class]="'wf-tree-row' + (selectedNow() === node ? ' wf-tree-selected' : '')" [attr.tabindex]="disabled() ? -1 : 0" (click)="!disabled() && select(node)"
               (dblclick)="!disabled() && wfNodeDoubleClick.emit(node)" (keydown)="!disabled() && onKeyDown($event, node)">
            <span class="wf-tree-toggle" aria-hidden="true" (click)="$event.stopPropagation(); hasChildren(node) && !disabled() && toggle(node)">{{ hasChildren(node) ? (node.expanded ? '▾' : '▸') : '' }}</span>
            @if (checkBoxes()) {
              <input type="checkbox" tabindex="-1" [disabled]="!!disabled()" [checked]="!!node.checked" (click)="$event.stopPropagation()" (change)="check(node, $any($event.target).checked)" />
            }
            @if (iconOf(node)) { <img [src]="iconOf(node)" alt="" class="wf-tree-icon" /> }
            <span class="wf-tree-text">{{ node.text }}</span>
          </div>
          @if (hasChildren(node) && node.expanded) { <ul role="group"><ng-container *ngTemplateOutlet="level; context: { $implicit: node.nodes }"></ng-container></ul> }
        </li>
      }
    </ng-template>`,
})
export class WfTreeView {
  nodes = input<WfTreeNode[]>([]);
  /** Controlled selection (WinForms `SelectedNode`); omit for an uncontrolled tree. */
  selectedNode = input<WfTreeNode | null | undefined>();
  /** Where `imageKey` / `imageIndex` of the nodes are looked up (WinForms TreeView.ImageList). */
  imageList = input<WfImageList | undefined>();
  checkBoxes = input<boolean | undefined>(false);
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();

  wfSelectionChange = output<WfTreeNode>();
  /** `[node, checked]` */
  wfNodeCheckedChange = output<[WfTreeNode, boolean]>();
  wfNodeClick = output<WfTreeNode>();
  wfNodeDoubleClick = output<WfTreeNode>();
  /** A node with `hasChildren` and no loaded children opens (BeforeExpand): the page may fill `nodes`, also later (an async load shows the children when they arrive). */
  wfNodeExpand = output<WfTreeNode>();

  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly local = signal<WfTreeNode | null>(null);
  protected selectedNow = () => (this.selectedNode() !== undefined ? this.selectedNode() : this.local());

  protected hasChildren = (node: WfTreeNode) => node.nodes.length > 0 || !!node.hasChildren;
  protected iconOf(node: WfTreeNode): string | undefined { return node.icon ?? wfImageListUrl(this.imageList(), node.imageKey ?? node.imageIndex); }

  protected select(node: WfTreeNode): void {
    this.local.set(node);
    this.wfSelectionChange.emit(node);
    this.wfNodeClick.emit(node);
  }

  protected toggle(node: WfTreeNode): void {
    if (!node.expanded && node.hasChildren && node.nodes.length === 0) this.wfNodeExpand.emit(node);
    node.expanded = !node.expanded;
  }

  protected check(node: WfTreeNode, on: boolean): void {
    node.checked = on;
    this.wfNodeCheckedChange.emit([node, on]);
  }

  protected onKeyDown(e: KeyboardEvent, node: WfTreeNode): void {
    const rows = Array.from((this.host.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.wf-tree-row'));
    const at = rows.indexOf(e.currentTarget as HTMLElement);
    if (e.key === 'ArrowDown') rows[at + 1]?.focus();
    else if (e.key === 'ArrowUp') rows[at - 1]?.focus();
    else if (e.key === 'ArrowRight' && node.nodes.length && !node.expanded) this.toggle(node);
    else if (e.key === 'ArrowLeft' && node.nodes.length && node.expanded) this.toggle(node);
    else if (e.key === 'Enter' || e.key === ' ') this.select(node);
    else return;
    e.preventDefault();
  }
}
