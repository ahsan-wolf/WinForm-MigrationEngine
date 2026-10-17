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
  /** A web path, URL or data URI shown in the menu's icon column (top-level items show it before the text). */
  icon?: string;
  text?: string; shortcut?: string; checked?: boolean; disabled?: boolean; onClick?: () => void; id?: string; children?: ReactNode;
}
/** Top-level items open on click (and on hover once a menu is open); submenus open on hover. */
export function WfMenuItem({ text, icon, shortcut, checked, disabled, onClick, id, children }: WfMenuItemProps) {
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
              disabled={disabled} accessKey={parseMnemonic(text).accessKey} onClick={activate} className={icon && checked ? 'wf-menuitem-checked' : undefined}>
        <span className="wf-menuitem-check" aria-hidden="true">{icon && depth > 0 ? <img src={icon} alt="" className="wf-menuitem-icon" /> : checked ? '✓' : ''}</span>
        {icon && depth === 0 && <img src={icon} alt="" className="wf-menuitem-icon" />}
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

export type WfDisplayStyle = 'image' | 'text' | 'imageAndText';

/** What a toolbar button shows: the image when it has one (WinForms ToolStripButton default), otherwise its text; `displayStyle` overrides. */
function face(label: string, tooltip: string | undefined, icon: string | undefined, displayStyle: WfDisplayStyle | undefined) {
  const style = displayStyle ?? (icon ? 'image' : 'text');
  const showImage = style !== 'text' || (!label && !icon);
  const showText = style !== 'image' && label.length > 0;
  return (
    <>
      {showImage && (icon ? <img src={icon} alt="" /> : <span aria-hidden="true">{(label || tooltip || '·').slice(0, 1)}</span>)}
      {showText && <span className="wf-toolbar-text">{label}</span>}
    </>
  );
}

export interface WfToolbarButtonProps { text?: string; tooltip?: string; icon?: string; displayStyle?: WfDisplayStyle; disabled?: boolean; onClick?: () => void; id?: string }
/** `icon` is a web path, URL or data URI; the text becomes the tooltip when only the image is shown. */
export function WfToolbarButton({ text, tooltip, icon, displayStyle, disabled, onClick, id }: WfToolbarButtonProps) {
  const label = parseMnemonic(text).label;
  return (
    <button type="button" id={id} className="wf-toolbar-button" title={tooltip ?? label} aria-label={tooltip ?? (label || undefined)}
            disabled={disabled} onClick={onClick}>
      {face(label, tooltip, icon, displayStyle)}
    </button>
  );
}

export interface WfDropDownButtonProps {
  text?: string; tooltip?: string; icon?: string; displayStyle?: WfDisplayStyle; disabled?: boolean; id?: string;
  /** Split button: the main part clicks, a separate arrow opens the menu. Without it the whole button opens the menu. */
  split?: boolean;
  onClick?: () => void; children?: ReactNode;
}
/** Toolbar button with a drop-down menu of WfMenuItem children (ToolStripDropDownButton / ToolStripSplitButton). */
export function WfDropDownButton({ text, tooltip, icon, displayStyle, disabled, id, split, onClick, children }: WfDropDownButtonProps) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const label = parseMnemonic(text).label;

  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false); };
    const esc = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false); };
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', esc);
    return () => { document.removeEventListener('mousedown', close); document.removeEventListener('keydown', esc); };
  }, [open]);

  return (
    <div ref={ref} className="wf-dropdown">
      <button type="button" id={id} className="wf-toolbar-button" title={tooltip ?? label} aria-label={tooltip ?? (label || undefined)} disabled={disabled}
              aria-haspopup={split ? undefined : 'menu'} aria-expanded={split ? undefined : open}
              onClick={() => { onClick?.(); if (!split) setOpen(o => !o); }}>
        {face(label, tooltip, icon, displayStyle)}{!split && <span className="wf-dropdown-arrow" aria-hidden="true">▾</span>}
      </button>
      {split && (
        <button type="button" className="wf-toolbar-button wf-dropdown-toggle" aria-label="Show menu" aria-haspopup="menu" aria-expanded={open} disabled={disabled}
                onClick={() => setOpen(o => !o)}><span aria-hidden="true">▾</span></button>
      )}
      {open && (
        <DepthContext.Provider value={1}>
          <div role="menu" className="wf-menu" onClick={() => setOpen(false)}>{children}</div>
        </DepthContext.Provider>
      )}
    </div>
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
