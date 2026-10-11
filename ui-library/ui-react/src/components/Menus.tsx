import { Children, createContext, useContext, useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { MnemonicText, parseMnemonic } from '../mnemonic';

interface BarState { openId: string | null; setOpenId: (id: string | null) => void }
const BarContext = createContext<BarState | null>(null);
const DepthContext = createContext(0);

export function WfMenuBar({ children, id, className }: { children?: ReactNode; id?: string; className?: string }) {
  const [openId, setOpenId] = useState<string | null>(null);
  const ref = useRef<HTMLElement>(null);

  useEffect(() => {
    if (openId === null) return;
    const close = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpenId(null); };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [openId]);

  return (
    <BarContext.Provider value={{ openId, setOpenId }}>
      <nav ref={ref} id={id} role="menubar" className={`wf-menubar${className ? ' ' + className : ''}`}>{children}</nav>
    </BarContext.Provider>
  );
}

export interface WfMenuItemProps {
  text?: string; shortcut?: string; checked?: boolean; disabled?: boolean; onClick?: () => void; id?: string; children?: ReactNode;
}
/** Top-level items open on click (and on hover once a menu is open); submenus open on hover. */
export function WfMenuItem({ text, shortcut, checked, disabled, onClick, id, children }: WfMenuItemProps) {
  const autoId = useId();
  const bar = useContext(BarContext);
  const depth = useContext(DepthContext);
  const [subOpen, setSubOpen] = useState(false);
  const hasSub = Children.count(children) > 0;
  const open = depth === 0 ? bar?.openId === autoId : subOpen;

  const activate = () => {
    if (disabled) return;
    if (hasSub) {
      if (depth === 0) bar?.setOpenId(open ? null : autoId); else setSubOpen(o => !o);
      return;
    }
    onClick?.();
    bar?.setOpenId(null);
  };

  return (
    <div role="none" className={`wf-menuitem ${depth === 0 ? 'wf-menuitem-top' : 'wf-menuitem-sub'}`}
         onMouseEnter={() => { if (depth === 0) { if (bar?.openId) bar.setOpenId(autoId); } else if (hasSub) setSubOpen(true); }}
         onMouseLeave={() => { if (depth > 0) setSubOpen(false); }}>
      <button type="button" id={id} role={checked === undefined ? 'menuitem' : 'menuitemcheckbox'} aria-checked={checked}
              aria-haspopup={hasSub || undefined} aria-expanded={hasSub ? open : undefined}
              disabled={disabled} accessKey={parseMnemonic(text).accessKey} onClick={activate}>
        <span className="wf-menuitem-check" aria-hidden="true">{checked ? '✓' : ''}</span>
        <span className="wf-menuitem-text"><MnemonicText text={text} /></span>
        {shortcut && <kbd className="wf-menuitem-shortcut">{shortcut}</kbd>}
        {hasSub && depth > 0 && <span aria-hidden="true">▸</span>}
      </button>
      {hasSub && open && (
        <DepthContext.Provider value={depth + 1}>
          <div role="menu" className="wf-menu">{children}</div>
        </DepthContext.Provider>
      )}
    </div>
  );
}

/** Horizontal rule inside a menu, vertical bar inside a toolbar (styled by the parent). */
export function WfSeparator() {
  return <div role="separator" className="wf-separator" />;
}

export function WfToolbar({ children, id, className }: { children?: ReactNode; id?: string; className?: string }) {
  return <div id={id} role="toolbar" className={`wf-toolbar${className ? ' ' + className : ''}`}>{children}</div>;
}

export interface WfToolbarButtonProps { text?: string; tooltip?: string; icon?: string; disabled?: boolean; onClick?: () => void; id?: string }
export function WfToolbarButton({ text, tooltip, icon, disabled, onClick, id }: WfToolbarButtonProps) {
  const label = parseMnemonic(text).label;
  return (
    <button type="button" id={id} className="wf-toolbar-button" title={tooltip ?? label} aria-label={tooltip ?? (label || undefined)}
            disabled={disabled} onClick={onClick}>
      {icon ? <img src={icon} alt="" /> : <span aria-hidden="true">{(label || tooltip || '·').slice(0, 1)}</span>}
      {label && <span className="wf-toolbar-text">{label}</span>}
    </button>
  );
}

export interface WfContextMenuProps { menu: ReactNode; children?: ReactNode; className?: string }
/** Wraps its target; right-click opens `menu` (WfMenuItem children) at the pointer. */
export function WfContextMenu({ menu, children, className }: WfContextMenuProps) {
  const [at, setAt] = useState<{ x: number; y: number } | null>(null);

  useEffect(() => {
    if (!at) return;
    const close = () => setAt(null);
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [at]);

  return (
    <div className={className} onContextMenu={e => { e.preventDefault(); setAt({ x: e.clientX, y: e.clientY }); }}>
      {children}
      {at && (
        <DepthContext.Provider value={1}>
          <div role="menu" className="wf-menu wf-contextmenu" style={{ left: at.x, top: at.y }} onMouseDown={e => e.stopPropagation()} onClick={() => setAt(null)}>{menu}</div>
        </DepthContext.Provider>
      )}
    </div>
  );
}
