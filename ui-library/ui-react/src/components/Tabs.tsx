import { Children, isValidElement, useId, useState, type KeyboardEvent, type ReactElement, type ReactNode } from 'react';

const cls = (base: string, extra?: string): string => (extra ? `${base} ${extra}` : base);

export interface WfTabPageProps { title?: string; disabled?: boolean; children?: ReactNode }
/** One page of a <WfTabs>; WfTabs reads its `title` and shows its children when the page is selected. */
export function WfTabPage({ children }: WfTabPageProps) {
  return <>{children}</>;
}

export interface WfTabsProps {
  /**
   * The selected page. Controlled when `onSelectionChange` is also passed (the parent keeps the index); without a change handler it is only the starting page and
   * the tabs switch on their own (a designer-set SelectedIndex must not freeze the control). Omit for an uncontrolled control that starts on the first page.
   */
  selectedIndex?: number;
  onSelectionChange?: (index: number) => void;
  id?: string; className?: string; children?: ReactNode;
}
/** Tab control (WinForms TabControl). Every page stays mounted (hidden when not selected) so their state survives tab switches. */
export function WfTabs({ selectedIndex, onSelectionChange, id, className, children }: WfTabsProps) {
  const base = useId();
  const pages = Children.toArray(children).filter(isValidElement) as ReactElement<WfTabPageProps>[];
  const [inner, setInner] = useState(selectedIndex ?? 0);
  const controlled = selectedIndex !== undefined && onSelectionChange !== undefined;
  const active = Math.min(Math.max(controlled ? selectedIndex : inner, 0), Math.max(pages.length - 1, 0));

  const select = (index: number) => {
    if (pages[index]?.props.disabled) return;
    setInner(index);
    onSelectionChange?.(index);
  };

  const onKeyDown = (e: KeyboardEvent) => {
    const last = pages.length - 1;
    const next = e.key === 'ArrowRight' ? Math.min(active + 1, last) : e.key === 'ArrowLeft' ? Math.max(active - 1, 0)
      : e.key === 'Home' ? 0 : e.key === 'End' ? last : -1;
    if (next < 0) return;
    e.preventDefault();
    select(next);
  };

  return (
    <div id={id} className={cls('wf-tabs', className)}>
      <div role="tablist" className="wf-tablist" onKeyDown={onKeyDown}>
        {pages.map((page, i) => (
          <button key={i} type="button" role="tab" id={`${base}-tab-${i}`} aria-controls={`${base}-panel-${i}`} aria-selected={i === active}
                  tabIndex={i === active ? 0 : -1} disabled={page.props.disabled} className={cls('wf-tab', i === active ? 'wf-tab-active' : undefined)}
                  onClick={() => select(i)}>
            {page.props.title ?? `Page ${i + 1}`}
          </button>
        ))}
      </div>
      {pages.map((page, i) => (
        <div key={i} role="tabpanel" id={`${base}-panel-${i}`} aria-labelledby={`${base}-tab-${i}`} hidden={i !== active} className="wf-tabpanel">
          {page}
        </div>
      ))}
    </div>
  );
}
