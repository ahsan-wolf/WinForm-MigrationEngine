import { Component, computed, contentChildren, ElementRef, forwardRef, HostListener, inject, InjectionToken, input, output, signal } from '@angular/core';
import { parseMnemonic } from '@wfm/ui-core';
import { WfMnemonic } from './basic';

const HOST = { style: 'display: contents' } as const;
const cls = (base: string, extra: string | undefined): string => (extra ? `${base} ${extra}` : base);

let nextId = 1;

/** The nesting depth menu items start at: popups outside the menu bar (drop-down buttons, context menus) show their items as sub-menu items. */
export const WF_MENU_DEPTH = new InjectionToken<number>('WF_MENU_DEPTH');

/** The menu strip: one top-level item is open at a time; a click outside closes it. */
@Component({
  selector: 'wf-menu-bar',
  standalone: true,
  host: HOST,
  template: `<nav [attr.id]="controlId()" role="menubar" [class]="cls('wf-menubar', className())"><ng-content /></nav>`,
})
export class WfMenuBar {
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  readonly openId = signal<string | null>(null);
  protected cls = cls;
  private readonly element = inject(ElementRef<HTMLElement>);

  @HostListener('document:mousedown', ['$event'])
  protected onDocumentMouseDown(e: MouseEvent): void {
    if (this.openId() !== null && !this.element.nativeElement.contains(e.target as Node)) this.openId.set(null);
  }
}

/** Top-level items open on click (and on hover once a menu is open); submenus open on hover. */
@Component({
  selector: 'wf-menu-item',
  standalone: true,
  imports: [WfMnemonic],
  host: HOST,
  template: `<div role="none" [class]="'wf-menuitem ' + (depth === 0 ? 'wf-menuitem-top' : 'wf-menuitem-sub')" (mouseenter)="enter()" (mouseleave)="leave()">
      <button type="button" [attr.id]="controlId()" [attr.role]="checked() === undefined ? 'menuitem' : 'menuitemcheckbox'" [attr.aria-checked]="checked() === undefined ? null : checked()"
              [attr.aria-haspopup]="hasSub() ? 'true' : null" [attr.aria-expanded]="hasSub() ? open() : null" [disabled]="!!disabled()" [attr.accesskey]="access()"
              [class.wf-menuitem-checked]="!!icon() && !!checked()" (click)="activate()">
        <span class="wf-menuitem-check" aria-hidden="true">@if (icon() && depth > 0) { <img [src]="icon()" alt="" class="wf-menuitem-icon" /> } @else if (checked()) { ✓ }</span>
        @if (icon() && depth === 0) { <img [src]="icon()" alt="" class="wf-menuitem-icon" /> }
        <span class="wf-menuitem-text"><wf-mnemonic [text]="text()" /></span>
        @if (shortcut()) { <kbd class="wf-menuitem-shortcut">{{ shortcut() }}</kbd> }
        @if (hasSub() && depth > 0) { <span aria-hidden="true">▸</span> }
      </button>
      @if (hasSub() && open()) { <div role="menu" class="wf-menu"><ng-content /></div> }
    </div>`,
})
export class WfMenuItem {
  /** A web path, URL or data URI shown in the menu's icon column (top-level items show it before the text). */
  icon = input<string | undefined>();
  text = input<string | undefined>();
  shortcut = input<string | undefined>();
  checked = input<boolean | undefined>();
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  wfClick = output<void>();

  private readonly autoId = `wf-menu-${nextId++}`;
  private readonly bar = inject(WfMenuBar, { optional: true });
  private readonly parent = inject(forwardRef(() => WfMenuItem), { skipSelf: true, optional: true });
  protected readonly depth: number = this.parent ? this.parent.depth + 1 : (inject(WF_MENU_DEPTH, { optional: true }) ?? 0);
  private readonly subOpen = signal(false);
  private readonly children = contentChildren(forwardRef(() => WfMenuItem));
  protected hasSub = computed(() => this.children().length > 0);
  protected open = computed(() => (this.depth === 0 ? this.bar?.openId() === this.autoId : this.subOpen()));
  protected access = computed(() => parseMnemonic(this.text()).accessKey);

  protected activate(): void {
    if (this.disabled()) return;
    if (this.hasSub()) {
      if (this.depth === 0) this.bar?.openId.set(this.open() ? null : this.autoId);
      else this.subOpen.update((o) => !o);
      return;
    }
    this.wfClick.emit();
    this.closeAll();
  }

  protected enter(): void {
    if (this.depth === 0) { if (this.bar?.openId()) this.bar.openId.set(this.autoId); }
    else if (this.hasSub()) this.subOpen.set(true);
  }

  protected leave(): void {
    if (this.depth > 0) this.subOpen.set(false);
  }

  private closeAll(): void {
    this.bar?.openId.set(null);
  }
}

/** Horizontal rule inside a menu, vertical bar inside a toolbar (styled by the parent). */
@Component({
  selector: 'wf-separator',
  standalone: true,
  host: HOST,
  template: `<div role="separator" class="wf-separator"></div>`,
})
export class WfSeparator {}

@Component({
  selector: 'wf-toolbar',
  standalone: true,
  host: HOST,
  template: `<div [attr.id]="controlId()" role="toolbar" [class]="cls('wf-toolbar', className())"><ng-content /></div>`,
})
export class WfToolbar {
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  protected cls = cls;
}

export type WfDisplayStyle = 'image' | 'text' | 'imageAndText';

/** What a toolbar button shows: the image when it has one (WinForms ToolStripButton default), otherwise its text; `displayStyle` overrides. */
@Component({
  selector: 'wf-toolbar-button',
  standalone: true,
  host: HOST,
  template: `<button type="button" [attr.id]="controlId()" class="wf-toolbar-button" [attr.title]="tooltip() ?? label()" [attr.aria-label]="tooltip() ?? (label() || null)" [disabled]="!!disabled()" (click)="wfClick.emit()">
      @if (showImage()) { @if (icon()) { <img [src]="icon()" alt="" /> } @else { <span aria-hidden="true">{{ (label() || tooltip() || '·').slice(0, 1) }}</span> } }
      @if (showText()) { <span class="wf-toolbar-text">{{ label() }}</span> }
    </button>`,
})
export class WfToolbarButton {
  text = input<string | undefined>();
  tooltip = input<string | undefined>();
  icon = input<string | undefined>();
  displayStyle = input<WfDisplayStyle | undefined>();
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  wfClick = output<void>();
  protected label = computed(() => parseMnemonic(this.text()).label);
  private style = computed(() => this.displayStyle() ?? (this.icon() ? 'image' : 'text'));
  protected showImage = computed(() => this.style() !== 'text' || (!this.label() && !this.icon()));
  protected showText = computed(() => this.style() !== 'image' && this.label().length > 0);
}

/** Toolbar button with a drop-down menu of `wf-menu-item` children (ToolStripDropDownButton / ToolStripSplitButton). */
@Component({
  selector: 'wf-drop-down-button',
  standalone: true,
  host: HOST,
  providers: [{ provide: WF_MENU_DEPTH, useValue: 1 }],
  template: `<div class="wf-dropdown">
      <button type="button" [attr.id]="controlId()" class="wf-toolbar-button" [attr.title]="tooltip() ?? label()" [attr.aria-label]="tooltip() ?? (label() || null)" [disabled]="!!disabled()"
              [attr.aria-haspopup]="split() ? null : 'menu'" [attr.aria-expanded]="split() ? null : open()" (click)="wfClick.emit(); split() || open.set(!open())">
        @if (showImage()) { @if (icon()) { <img [src]="icon()" alt="" /> } @else { <span aria-hidden="true">{{ (label() || tooltip() || '·').slice(0, 1) }}</span> } }
        @if (showText()) { <span class="wf-toolbar-text">{{ label() }}</span> }
        @if (!split()) { <span class="wf-dropdown-arrow" aria-hidden="true">▾</span> }
      </button>
      @if (split()) {
        <button type="button" class="wf-toolbar-button wf-dropdown-toggle" aria-label="Show menu" aria-haspopup="menu" [attr.aria-expanded]="open()" [disabled]="!!disabled()" (click)="open.set(!open())"><span aria-hidden="true">▾</span></button>
      }
      <div role="menu" class="wf-menu" [hidden]="!open()" (click)="open.set(false)"><ng-content /></div>
    </div>`,
})
export class WfDropDownButton {
  text = input<string | undefined>();
  tooltip = input<string | undefined>();
  icon = input<string | undefined>();
  displayStyle = input<WfDisplayStyle | undefined>();
  disabled = input<boolean | undefined>(false);
  /** Split button: the main part clicks, a separate arrow opens the menu. Without it the whole button opens the menu. */
  split = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  wfClick = output<void>();
  protected readonly open = signal(false);
  protected label = computed(() => parseMnemonic(this.text()).label);
  private style = computed(() => this.displayStyle() ?? (this.icon() ? 'image' : 'text'));
  protected showImage = computed(() => this.style() !== 'text' || (!this.label() && !this.icon()));
  protected showText = computed(() => this.style() !== 'image' && this.label().length > 0);
  private readonly element = inject(ElementRef<HTMLElement>);

  @HostListener('document:mousedown', ['$event'])
  protected onDocumentMouseDown(e: MouseEvent): void {
    if (this.open() && !this.element.nativeElement.contains(e.target as Node)) this.open.set(false);
  }

  @HostListener('document:keydown', ['$event'])
  protected onDocumentKey(e: KeyboardEvent): void {
    if (this.open() && e.key === 'Escape') this.open.set(false);
  }
}

/** Wraps its target (the default slot); right-click opens the menu (the `[menu]` slot, `wf-menu-item` children) at the pointer. */
@Component({
  selector: 'wf-context-menu',
  standalone: true,
  host: HOST,
  providers: [{ provide: WF_MENU_DEPTH, useValue: 1 }],
  template: `<div [class]="className()" (contextmenu)="$event.preventDefault(); at.set({ x: $event.clientX, y: $event.clientY })">
      <ng-content />
      <div role="menu" class="wf-menu wf-contextmenu" [hidden]="!at()" [style.left.px]="at()?.x" [style.top.px]="at()?.y" (mousedown)="$event.stopPropagation()" (click)="at.set(null)"><ng-content select="[menu]" /></div>
    </div>`,
})
export class WfContextMenu {
  className = input<string | undefined>();
  protected readonly at = signal<{ x: number; y: number } | null>(null);

  @HostListener('document:mousedown')
  protected onDocumentMouseDown(): void {
    if (this.at()) this.at.set(null);
  }
}
