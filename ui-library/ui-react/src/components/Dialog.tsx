import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { WfButton } from './Controls';

type Render<T> = (close: (result?: T) => void) => ReactNode;

export type WfMessageIcon = 'info' | 'warning' | 'error' | 'question';
export type WfMessageButtons = 'ok' | 'okCancel' | 'yesNo' | 'yesNoCancel' | 'retryCancel' | 'abortRetryIgnore';
/** The DialogResult names MessageBox.Show returns. */
export type WfDialogResult = 'OK' | 'Cancel' | 'Yes' | 'No' | 'Retry' | 'Abort' | 'Ignore';

export interface WfMessageOptions { title?: string; buttons?: WfMessageButtons; icon?: WfMessageIcon; defaultButton?: WfDialogResult }

export interface WfDialogService {
  /** MessageBox.Show(text) */
  alert(message: string, title?: string, icon?: WfMessageIcon): Promise<void>;
  /** MessageBox.Show(text, ..., YesNo | OKCancel) -> true for Yes/OK */
  confirm(message: string, title?: string, icon?: WfMessageIcon): Promise<boolean>;
  /** MessageBox.Show with any button set -> the DialogResult name; closing with Escape gives the Cancel/No/Ignore/OK result the button set allows. */
  message(message: string, options?: WfMessageOptions): Promise<WfDialogResult>;
  /** Form.ShowDialog(): resolves with whatever the content passes to `close` (DialogResult), or undefined when dismissed. */
  show<T = void>(render: Render<T>, options?: { title?: string }): Promise<T | undefined>;
  /** this.Close() / DialogResult: closes the topmost dialog and resolves its `show` promise with `result`. Without an open dialog it calls the provider's `onRootClose` (if any). */
  close(result?: unknown): void;
}

interface Entry { key: number; title?: string; render: Render<unknown>; resolve: (value: unknown) => void }

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

const DialogContext = createContext<WfDialogService | null>(null);

export interface WfDialogProviderProps {
  children?: ReactNode;
  /** Called by `dialog.close()` when no dialog is open: the page is the app's own page (a standalone form), so closing it means leaving it. */
  onRootClose?: () => void;
}

export function WfDialogProvider({ children, onRootClose }: WfDialogProviderProps) {
  const [stack, setStack] = useState<Entry[]>([]);
  const counter = useRef(0);
  const stackRef = useRef<Entry[]>([]);
  const rootClose = useRef(onRootClose);
  useEffect(() => { stackRef.current = stack; rootClose.current = onRootClose; });

  const close = useCallback((key: number, value?: unknown) => {
    setStack(s => {
      s.find(e => e.key === key)?.resolve(value);
      return s.filter(e => e.key !== key);
    });
  }, []);

  const service = useMemo<WfDialogService>(() => {
    const show = <T,>(render: Render<T>, options?: { title?: string }) =>
      new Promise<T | undefined>(resolve => {
        const key = ++counter.current;
        setStack(s => [...s, { key, title: options?.title, render: render as Render<unknown>, resolve: resolve as (v: unknown) => void }]);
      });

    const message = (text: string, options: WfMessageOptions = {}) => {
      const set = options.buttons ?? 'ok';
      return show<WfDialogResult>(done => (
        <div className="wf-dialog-body wf-message">
          {options.icon && <span className={`wf-message-icon wf-message-${options.icon}`} aria-hidden="true">{GLYPH[options.icon]}</span>}
          <p>{text}</p>
          <div className="wf-dialog-actions">
            {BUTTONS[set].map((b, i) => <WfButton key={b.result} text={b.text} variant={(options.defaultButton ?? BUTTONS[set][0].result) === b.result || (!options.defaultButton && i === 0) ? 'primary' : 'default'} onClick={() => done(b.result)} />)}
          </div>
        </div>
      ), { title: options.title }).then(r => r ?? DISMISS[set]);
    };

    const closeTop = (result?: unknown) => {
      const top = stackRef.current.at(-1);
      if (!top) { rootClose.current?.(); return; }
      close(top.key, result);
    };

    return {
      show,
      message,
      close: closeTop,
      alert: (text, title, icon) => message(text, { title, icon }).then(() => undefined),
      confirm: (text, title, icon) => message(text, { title, icon, buttons: 'yesNo' }).then(r => r === 'Yes'),
    };
  }, [close]);

  return (
    <DialogContext.Provider value={service}>
      {children}
      {stack.map(e => <DialogFrame key={e.key} entry={e} onClose={v => close(e.key, v)} />)}
    </DialogContext.Provider>
  );
}

/** A modal native <dialog>: the browser provides the top layer, focus containment and the backdrop; Escape asks the content to close with its dismiss result. */
function DialogFrame({ entry, onClose }: { entry: Entry; onClose: (value?: unknown) => void }) {
  const ref = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    if (typeof dialog.showModal === 'function') { if (!dialog.open) dialog.showModal(); } else dialog.setAttribute('open', '');
    const onCancel = (e: Event) => { e.preventDefault(); onClose(); };
    dialog.addEventListener('cancel', onCancel);
    // Environments without native <dialog> cancel handling still get Escape.
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    return () => { dialog.removeEventListener('cancel', onCancel); document.removeEventListener('keydown', onKey); if (dialog.open && typeof dialog.close === 'function') dialog.close(); };
  }, [onClose]);

  return (
    <dialog ref={ref} role="dialog" aria-modal="true" aria-label={entry.title ?? 'Dialog'} className="wf-dialog">
      <header className="wf-dialog-title">
        <span>{entry.title ?? ''}</span>
        <button type="button" className="wf-dialog-x" aria-label="Close" title="Close" onClick={() => onClose()}>×</button>
      </header>
      {entry.render(onClose)}
    </dialog>
  );
}

export function useWfDialog(): WfDialogService {
  const service = useContext(DialogContext);
  if (!service) throw new Error('useWfDialog must be used inside <WfDialogProvider>.');
  return service;
}
