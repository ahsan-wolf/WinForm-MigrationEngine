import { Children, useEffect, type ReactNode } from 'react';

const cls = (base: string, extra?: string): string => (extra ? `${base} ${extra}` : base);

export interface WfFormProps {
  title?: string; acceptButtonId?: string; cancelButtonId?: string; onLoad?: () => void;
  id?: string; className?: string; children?: ReactNode;
}
/** Screen root. Enter / Escape click the accept / cancel button by id (WinForms AcceptButton / CancelButton). */
export function WfForm({ title, acceptButtonId, cancelButtonId, onLoad, id, className, children }: WfFormProps) {
  useEffect(() => { onLoad?.(); // eslint-disable-next-line react-hooks/exhaustive-deps -- Form.Load runs once on mount
  }, []);

  return (
    <section id={id} aria-label={title} className={cls('wf-form', className)}
             onKeyDown={e => {
               const target = e.key === 'Enter' ? acceptButtonId : e.key === 'Escape' ? cancelButtonId : undefined;
               const tag = (e.target as HTMLElement).tagName;
               if (target && tag !== 'TEXTAREA' && tag !== 'BUTTON') document.getElementById(target)?.click();
             }}>
      {title && <h1 className="wf-form-title">{title}</h1>}
      {children}
    </section>
  );
}

export interface WfPanelProps { visible?: boolean; scroll?: boolean; id?: string; className?: string; children?: ReactNode }
export function WfPanel({ visible = true, scroll, id, className, children }: WfPanelProps) {
  if (!visible) return null;
  return <div id={id} className={cls(`wf-panel${scroll ? ' wf-panel-scroll' : ''}`, className)}>{children}</div>;
}

export interface WfGroupBoxProps { title?: string; visible?: boolean; disabled?: boolean; id?: string; className?: string; children?: ReactNode }
/** Titled group of controls (WinForms GroupBox): a fieldset with a legend; `disabled` disables everything inside. */
export function WfGroupBox({ title, visible = true, disabled, id, className, children }: WfGroupBoxProps) {
  if (!visible) return null;
  return (
    <fieldset id={id} className={cls('wf-groupbox', className)} disabled={disabled}>
      {title && <legend>{title}</legend>}
      {children}
    </fieldset>
  );
}

export interface WfSplitProps {
  orientation?: 'horizontal' | 'vertical'; splitterDistance?: number; id?: string; className?: string; children?: ReactNode;
}
/** Two panes. `splitterDistance` (px) sizes the first pane; the second takes the rest. */
export function WfSplit({ orientation = 'vertical', splitterDistance, id, className, children }: WfSplitProps) {
  const panes = Children.toArray(children);
  return (
    <div id={id} role="group" className={cls(`wf-split wf-split-${orientation}`, className)}>
      <div className="wf-split-first" style={splitterDistance ? { flex: `0 0 ${splitterDistance}px` } : undefined}>{panes[0]}</div>
      <div className="wf-splitter" aria-hidden="true" />
      <div className="wf-split-second">{panes.slice(1)}</div>
    </div>
  );
}

export function WfSplitPane({ children, className }: { children?: ReactNode; className?: string }) {
  return <div className={cls('wf-split-pane', className)}>{children}</div>;
}

export function WfStatusBar({ children, id, className }: { children?: ReactNode; id?: string; className?: string }) {
  return <footer id={id} role="status" className={cls('wf-statusbar', className)}>{children}</footer>;
}
