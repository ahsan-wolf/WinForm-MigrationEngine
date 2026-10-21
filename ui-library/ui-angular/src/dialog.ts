import { ApplicationRef, Component, createComponent, EnvironmentInjector, inject, Injectable, InjectionToken, Injector, input, type Type } from '@angular/core';
import { WfButton } from './basic';

export type WfMessageIcon = 'info' | 'warning' | 'error' | 'question';
export type WfMessageButtons = 'ok' | 'okCancel' | 'yesNo' | 'yesNoCancel' | 'retryCancel' | 'abortRetryIgnore';
/** The DialogResult names MessageBox.Show returns. */
export type WfDialogResult = 'OK' | 'Cancel' | 'Yes' | 'No' | 'Retry' | 'Abort' | 'Ignore';

export interface WfMessageOptions { title?: string; buttons?: WfMessageButtons; icon?: WfMessageIcon; defaultButton?: WfDialogResult }

/** What a dialog's content component can inject to close its own dialog with a result (the page itself uses `dialog.close(result)`). */
export const WF_DIALOG_CLOSE = new InjectionToken<(result?: unknown) => void>('WF_DIALOG_CLOSE');

const BUTTONS: Record<WfMessageButtons, { text: string; result: WfDialogResult }[]> = {
  ok: [{ text: 'OK', result: 'OK' }],
  okCancel: [{ text: 'OK', result: 'OK' }, { text: 'Cancel', result: 'Cancel' }],
  yesNo: [{ text: '&Yes', result: 'Yes' }, { text: '&No', result: 'No' }],
  yesNoCancel: [{ text: '&Yes', result: 'Yes' }, { text: '&No', result: 'No' }, { text: 'Cancel', result: 'Cancel' }],
  retryCancel: [{ text: '&Retry', result: 'Retry' }, { text: 'Cancel', result: 'Cancel' }],
  abortRetryIgnore: [{ text: '&Abort', result: 'Abort' }, { text: '&Retry', result: 'Retry' }, { text: '&Ignore', result: 'Ignore' }],
};
/** What Escape (or the title bar close) answers: the cancelling button of the set. */
const DISMISS: Record<WfMessageButtons, WfDialogResult> = { ok: 'OK', okCancel: 'Cancel', yesNo: 'No', yesNoCancel: 'Cancel', retryCancel: 'Cancel', abortRetryIgnore: 'Ignore' };
const GLYPH: Record<WfMessageIcon, string> = { info: 'i', warning: '!', error: '×', question: '?' };

/** The content of alert / confirm / message boxes. */
@Component({
  selector: 'wf-message-body',
  standalone: true,
  imports: [WfButton],
  template: `<div class="wf-dialog-body wf-message">
      @if (icon()) { <span [class]="'wf-message-icon wf-message-' + icon()" aria-hidden="true">{{ glyph() }}</span> }
      <p>{{ text() }}</p>
      <div class="wf-dialog-actions">
        @for (b of buttons(); track b.result; let i = $index) {
          <wf-button [text]="b.text" [variant]="isDefault(b.result, i) ? 'primary' : 'default'" (wfClick)="close(b.result)" />
        }
      </div>
    </div>`,
})
export class WfMessageBody {
  text = input('');
  icon = input<WfMessageIcon | undefined>();
  set = input<WfMessageButtons>('ok');
  defaultButton = input<WfDialogResult | undefined>();
  protected close = inject(WF_DIALOG_CLOSE);
  protected buttons = () => BUTTONS[this.set()];
  protected glyph = () => (this.icon() ? GLYPH[this.icon()!] : '');
  protected isDefault(result: WfDialogResult, index: number): boolean {
    return (this.defaultButton() ?? BUTTONS[this.set()][0].result) === result || (!this.defaultButton() && index === 0);
  }
}

interface Entry { key: number; dialog: HTMLDialogElement; destroy: () => void; resolve: (value: unknown) => void }

/**
 * Modal popups on the native `<dialog>` element (top layer, focus containment, Escape): the counterpart of MessageBox.Show and Form.ShowDialog.
 * `show()` renders a component into a dialog and resolves with whatever the page passes to `dialog.close(result)`.
 */
@Injectable({ providedIn: 'root' })
export class WfDialogService {
  private readonly appRef = inject(ApplicationRef);
  private readonly environment = inject(EnvironmentInjector);
  private readonly stack: Entry[] = [];
  private counter = 0;

  /** Called by `close()` when no dialog is open: the page is the app's own page (a standalone form), so closing it means leaving it. */
  onRootClose?: () => void;

  /** Form.ShowDialog(): renders the component in a dialog; resolves with the result passed to `close`, or undefined when dismissed. */
  show<T = void>(component: Type<unknown>, options?: { title?: string; inputs?: Record<string, unknown> }): Promise<T | undefined> {
    return new Promise<T | undefined>((resolve) => {
      const key = ++this.counter;
      const dialog = document.createElement('dialog');
      dialog.className = 'wf-dialog';
      dialog.setAttribute('role', 'dialog');
      dialog.setAttribute('aria-modal', 'true');
      dialog.setAttribute('aria-label', options?.title ?? 'Dialog');

      const header = document.createElement('header');
      header.className = 'wf-dialog-title';
      const title = document.createElement('span');
      title.textContent = options?.title ?? '';
      const x = document.createElement('button');
      x.type = 'button';
      x.className = 'wf-dialog-x';
      x.setAttribute('aria-label', 'Close');
      x.title = 'Close';
      x.textContent = '×';
      header.append(title, x);
      const host = document.createElement('div');
      dialog.append(header, host);
      document.body.appendChild(dialog);

      const close = (value?: unknown) => this.finish(key, value);
      const injector = Injector.create({ providers: [{ provide: WF_DIALOG_CLOSE, useValue: close }], parent: this.environment });
      const ref = createComponent(component, { environmentInjector: this.environment, elementInjector: injector, hostElement: host });
      for (const [name, value] of Object.entries(options?.inputs ?? {})) ref.setInput(name, value);
      this.appRef.attachView(ref.hostView);

      x.addEventListener('click', () => close());
      dialog.addEventListener('cancel', (e) => { e.preventDefault(); close(); });
      if (typeof dialog.showModal === 'function') dialog.showModal(); else dialog.setAttribute('open', '');

      this.stack.push({
        key,
        dialog,
        resolve: resolve as (value: unknown) => void,
        destroy: () => {
          this.appRef.detachView(ref.hostView);
          ref.destroy();
          if (dialog.open && typeof dialog.close === 'function') dialog.close();
          dialog.remove();
        },
      });
    });
  }

  /** MessageBox.Show with any button set -> the DialogResult name; closing with Escape gives the Cancel/No/Ignore/OK result the button set allows. */
  message(text: string, options: WfMessageOptions = {}): Promise<WfDialogResult> {
    const set = options.buttons ?? 'ok';
    return this.show<WfDialogResult>(WfMessageBody, { title: options.title, inputs: { text, icon: options.icon, set, defaultButton: options.defaultButton } })
      .then((r) => r ?? DISMISS[set]);
  }

  /** MessageBox.Show(text) */
  alert(text: string, title?: string, icon?: WfMessageIcon): Promise<void> {
    return this.message(text, { title, icon }).then(() => undefined);
  }

  /** MessageBox.Show(text, ..., YesNo | OKCancel) -> true for Yes/OK */
  confirm(text: string, title?: string, icon?: WfMessageIcon): Promise<boolean> {
    return this.message(text, { title, icon, buttons: 'yesNo' }).then((r) => r === 'Yes');
  }

  /** this.Close() / DialogResult: closes the topmost dialog and resolves its `show` promise with `result`. Without an open dialog it calls `onRootClose` (if any). */
  close(result?: unknown): void {
    const top = this.stack.at(-1);
    if (!top) { this.onRootClose?.(); return; }
    this.finish(top.key, result);
  }

  private finish(key: number, value?: unknown): void {
    const at = this.stack.findIndex((e) => e.key === key);
    if (at < 0) return;
    const [entry] = this.stack.splice(at, 1);
    entry.destroy();
    entry.resolve(value);
  }
}
