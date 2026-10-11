import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { WfButton } from './Controls';

type Render<T> = (close: (result?: T) => void) => ReactNode;

export interface WfDialogService {
  /** MessageBox.Show(text) */
  alert(message: string, title?: string): Promise<void>;
  /** MessageBox.Show(text, ..., YesNo) -> true for Yes/OK */
  confirm(message: string, title?: string): Promise<boolean>;
  /** Form.ShowDialog(): resolves with whatever the content passes to `close` (DialogResult), or undefined when dismissed. */
  show<T = void>(render: Render<T>, options?: { title?: string }): Promise<T | undefined>;
}

interface Entry { key: number; title?: string; render: Render<unknown>; resolve: (value: unknown) => void }

const DialogContext = createContext<WfDialogService | null>(null);

export function WfDialogProvider({ children }: { children?: ReactNode }) {
  const [stack, setStack] = useState<Entry[]>([]);
  const counter = useRef(0);

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
    return {
      show,
      alert: (message, title) => show<void>(done => (
        <div className="wf-dialog-body"><p>{message}</p><div className="wf-dialog-actions"><WfButton text="OK" variant="primary" onClick={() => done()} /></div></div>
      ), { title }).then(() => undefined),
      confirm: (message, title) => show<boolean>(done => (
        <div className="wf-dialog-body"><p>{message}</p>
          <div className="wf-dialog-actions"><WfButton text="&Yes" variant="primary" onClick={() => done(true)} /><WfButton text="&No" onClick={() => done(false)} /></div></div>
      ), { title }).then(r => r === true),
    };
  }, []);

  return (
    <DialogContext.Provider value={service}>
      {children}
      {stack.map(e => <DialogFrame key={e.key} entry={e} onClose={v => close(e.key, v)} />)}
    </DialogContext.Provider>
  );
}

function DialogFrame({ entry, onClose }: { entry: Entry; onClose: (value?: unknown) => void }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div className="wf-dialog-backdrop">
      <div role="dialog" aria-modal="true" aria-label={entry.title ?? 'Dialog'} className="wf-dialog">
        {entry.title && <header className="wf-dialog-title">{entry.title}</header>}
        {entry.render(onClose)}
      </div>
    </div>
  );
}

export function useWfDialog(): WfDialogService {
  const service = useContext(DialogContext);
  if (!service) throw new Error('useWfDialog must be used inside <WfDialogProvider>.');
  return service;
}
