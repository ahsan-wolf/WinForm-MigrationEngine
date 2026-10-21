import { Component, computed, contentChildren, effect, forwardRef, inject, input, output, signal, untracked } from '@angular/core';

const HOST = { style: 'display: contents' } as const;
const cls = (base: string, extra: string | undefined): string => (extra ? `${base} ${extra}` : base);

/** One page of a <wf-tabs>; the tabs read its `title` and show it when the page is selected. A hidden page keeps its index but its tab is not shown. */
@Component({
  selector: 'wf-tab-page',
  standalone: true,
  template: `<div role="tabpanel" class="wf-tabpanel" [attr.id]="'panel-' + index()" [hidden]="!active() || visible() === false"><ng-content /></div>`,
})
export class WfTabPage {
  title = input<string | undefined>();
  disabled = input<boolean | undefined>(false);
  /** `false` hides the page's tab (and its content); the page keeps its index, like a TabPage removed from and later re-added to the TabPages collection. */
  visible = input<boolean | undefined>(true);
  /** Image shown before the title (an image-resource path). */
  icon = input<string | undefined>();

  private readonly tabs = inject(forwardRef(() => WfTabs));
  readonly index = computed(() => this.tabs.pages().indexOf(this));
  readonly active = computed(() => this.tabs.activeIndex() === this.index());
}

/** Tab control (WinForms TabControl). Every page stays rendered (hidden when not selected) so their state survives tab switches. */
@Component({
  selector: 'wf-tabs',
  standalone: true,
  host: HOST,
  template: `<div [attr.id]="controlId()" [class]="cls('wf-tabs', className())">
      <div role="tablist" class="wf-tablist" (keydown)="onKeyDown($event)">
        @for (page of pages(); track $index) {
          @if (page.visible() !== false) {
            <button type="button" role="tab" [attr.aria-selected]="$index === activeIndex()" [tabIndex]="$index === activeIndex() ? 0 : -1" [disabled]="!!page.disabled()"
                    [class]="'wf-tab' + ($index === activeIndex() ? ' wf-tab-active' : '')" (click)="select($index)">
              @if (page.icon()) { <img class="wf-tab-icon" [src]="page.icon()" alt="" /> }{{ page.title() ?? 'Page ' + ($index + 1) }}
            </button>
          }
        }
      </div>
      <ng-content />
    </div>`,
})
export class WfTabs {
  /** The selected page. The page model keeps the index: bind it and listen to `wfSelectionChange`. Without a binding the tabs switch on their own. */
  selectedIndex = input<number | undefined>();
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  wfSelectionChange = output<number>();

  readonly pages = contentChildren(WfTabPage);
  private readonly inner = signal(0);
  readonly activeIndex = computed(() => Math.min(Math.max(this.inner(), 0), Math.max(this.pages().length - 1, 0)));
  protected cls = cls;

  constructor() {
    effect(() => {
      const wanted = this.selectedIndex();
      if (wanted !== undefined) untracked(() => this.inner.set(wanted));
    });
  }

  protected select(index: number): void {
    const page = this.pages()[index];
    if (!page || page.disabled() || page.visible() === false) return;
    this.inner.set(index);
    this.wfSelectionChange.emit(index);
  }

  protected onKeyDown(e: KeyboardEvent): void {
    const shown = this.pages().map((p, i) => (p.visible() === false || p.disabled() ? -1 : i)).filter((i) => i >= 0);
    if (shown.length === 0) return;
    const at = shown.indexOf(this.activeIndex());
    const next = e.key === 'ArrowRight' ? shown[Math.min(at + 1, shown.length - 1)] : e.key === 'ArrowLeft' ? shown[Math.max(at - 1, 0)]
      : e.key === 'Home' ? shown[0] : e.key === 'End' ? shown[shown.length - 1] : -1;
    if (next < 0) return;
    e.preventDefault();
    this.select(next);
  }
}
