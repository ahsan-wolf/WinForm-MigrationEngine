import { afterNextRender, Component, input, output } from '@angular/core';

const HOST = { style: 'display: contents' } as const;
const cls = (base: string, extra: string | undefined): string => (extra ? `${base} ${extra}` : base);

/** Screen root. Enter / Escape click the accept / cancel button by id (WinForms AcceptButton / CancelButton). */
@Component({
  selector: 'wf-form',
  standalone: true,
  host: HOST,
  template: `<section [attr.id]="controlId()" [attr.aria-label]="title()" [class]="cls('wf-form', className())" (keydown)="onKey($event)">
      @if (title()) { <h1 class="wf-form-title">{{ title() }}</h1> }
      <ng-content />
    </section>`,
})
export class WfForm {
  title = input<string | undefined>();
  acceptButtonId = input<string | undefined>();
  cancelButtonId = input<string | undefined>();
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  wfLoad = output<void>();
  protected cls = cls;

  constructor() {
    // Form.Load runs once, after the first render.
    afterNextRender(() => this.wfLoad.emit());
  }

  protected onKey(e: KeyboardEvent): void {
    const target = e.key === 'Enter' ? this.acceptButtonId() : e.key === 'Escape' ? this.cancelButtonId() : undefined;
    const tag = (e.target as HTMLElement).tagName;
    if (target && tag !== 'TEXTAREA' && tag !== 'BUTTON') document.getElementById(target)?.click();
  }
}

@Component({
  selector: 'wf-panel',
  standalone: true,
  host: HOST,
  template: `@if (visible()) { <div [attr.id]="controlId()" [class]="cls('wf-panel' + (scroll() ? ' wf-panel-scroll' : ''), className())"><ng-content /></div> }`,
})
export class WfPanel {
  visible = input<boolean | undefined>(true);
  scroll = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  /** A user control's Load event: runs once, after the first render. */
  wfLoad = output<void>();
  protected cls = cls;

  constructor() {
    afterNextRender(() => this.wfLoad.emit());
  }
}

/** Titled group of controls (WinForms GroupBox): a fieldset with a legend; `disabled` disables everything inside. */
@Component({
  selector: 'wf-group-box',
  standalone: true,
  host: HOST,
  template: `@if (visible()) {
      <fieldset [attr.id]="controlId()" [class]="cls('wf-groupbox', className())" [disabled]="!!disabled()">
        @if (title()) { <legend>{{ title() }}</legend> }
        <ng-content />
      </fieldset>
    }`,
})
export class WfGroupBox {
  title = input<string | undefined>();
  visible = input<boolean | undefined>(true);
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  protected cls = cls;
}

/** Two panes: mark the children `first` and `second`. `splitterDistance` (px) sizes the first pane; the second takes the rest. */
@Component({
  selector: 'wf-split',
  standalone: true,
  host: HOST,
  template: `<div [attr.id]="controlId()" role="group" [class]="cls('wf-split wf-split-' + orientation(), className())">
      <div class="wf-split-first" [style.flex]="splitterDistance() ? '0 0 ' + splitterDistance() + 'px' : null"><ng-content select="[first]" /></div>
      <div class="wf-splitter" aria-hidden="true"></div>
      <div class="wf-split-second"><ng-content select="[second]" /></div>
    </div>`,
})
export class WfSplit {
  orientation = input<'horizontal' | 'vertical'>('vertical');
  splitterDistance = input<number | undefined>();
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  protected cls = cls;
}

@Component({
  selector: 'wf-split-pane',
  standalone: true,
  host: HOST,
  template: `<div [class]="cls('wf-split-pane', className())"><ng-content /></div>`,
})
export class WfSplitPane {
  className = input<string | undefined>();
  protected cls = cls;
}

@Component({
  selector: 'wf-status-bar',
  standalone: true,
  host: HOST,
  template: `<footer [attr.id]="controlId()" role="status" [class]="cls('wf-statusbar', className())"><ng-content /></footer>`,
})
export class WfStatusBar {
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  protected cls = cls;
}
