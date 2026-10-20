import { Children, isValidElement, useId, useState, type KeyboardEvent, type ReactElement, type ReactNode } from 'react';

const cls = (base: string, extra?: string): string => (extra ? `${base} ${extra}` : base);

export interface WfTabPageProps {
  title?: string; disabled?: boolean;
  /** `false` hides the page's tab (and its content); the page keeps its index, like a TabPage removed from and later re-added to the TabPages collection. */
  visible?: boolean;
  /** Image shown before the title (an image-resource path). */
  icon?: string;
  children?: ReactNode;
}
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
    if (pages[index]?.props.disabled || pages[index]?.props.visible === false) return;
    setInner(index);
    onSelectionChange?.(index);
  };

  const onKeyDown = (e: KeyboardEvent) => {
    const shown = pages.map((p, i) => (p.props.visible === false || p.props.disabled ? -1 : i)).filter((i) => i >= 0);
    if (shown.length === 0) return;
    const at = shown.indexOf(active);
    const next = e.key === 'ArrowRight' ? shown[Math.min(at + 1, shown.length - 1)] : e.key === 'ArrowLeft' ? shown[Math.max(at - 1, 0)]
      : e.key === 'Home' ? shown[0] : e.key === 'End' ? shown[shown.length - 1] : -1;
    if (next < 0) return;
    e.preventDefault();
    select(next);
  };

  return (
    <div id={id} className={cls('wf-tabs', className)}>
      <div role="tablist" className="wf-tablist" onKeyDown={onKeyDown}>
        {pages.map((page, i) => page.props.visible === false ? null : (
          <button key={i} type="button" role="tab" id={`${base}-tab-${i}`} aria-controls={`${base}-panel-${i}`} aria-selected={i === active}
                  tabIndex={i === active ? 0 : -1} disabled={page.props.disabled} className={cls('wf-tab', i === active ? 'wf-tab-active' : undefined)}
                  onClick={() => select(i)}>
            {page.props.icon ? <img className="wf-tab-icon" src={page.props.icon} alt="" /> : null}
            {page.props.title ?? `Page ${i + 1}`}
          </button>
        ))}
      </div>
      {pages.map((page, i) => (
        <div key={i} role="tabpanel" id={`${base}-panel-${i}`} aria-labelledby={`${base}-tab-${i}`} hidden={i !== active || page.props.visible === false} className="wf-tabpanel">
          {page}
        </div>
      ))}
    </div>
  );
}
