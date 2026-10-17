import { useState, type KeyboardEvent } from 'react';

interface Common { id?: string; className?: string; tooltip?: string; disabled?: boolean }
const cls = (base: string, extra?: string): string => (extra ? `${base} ${extra}` : base);

// ---------------------------------------------------------------------------------------------------------- CheckedListBox

/** `SetItemChecked(i, checked)` over the page model's `checkedIndices` array. */
export function wfSetItemChecked(indices: number[], index: number, checked: boolean): void {
  const at = indices.indexOf(index);
  if (checked && at < 0) indices.push(index);
  else if (!checked && at >= 0) indices.splice(at, 1);
}

export interface WfCheckedListBoxProps extends Common {
  items?: string[];
  /** Indices of the checked items (WinForms `CheckedIndices`). Controlled when `onItemCheck` is passed, otherwise only the starting state. */
  checkedIndices?: number[];
  /** Selected item text (WinForms `Text` / `SelectedItem`). */
  value?: string;
  onSelectionChange?: (value: string) => void;
  onItemCheck?: (index: number, checked: boolean) => void;
}
/** List of items with a native check box each (CheckedListBox). Clicking an item selects it and toggles its check box. */
export function WfCheckedListBox({ items = [], checkedIndices, value, onSelectionChange, onItemCheck, disabled, id, className, tooltip }: WfCheckedListBoxProps) {
  const [localChecked, setLocalChecked] = useState<number[]>(checkedIndices ?? []);
  const [localSelected, setLocalSelected] = useState<string | undefined>(value);
  const checked = onItemCheck ? checkedIndices ?? [] : localChecked;
  const selected = onSelectionChange ? value : localSelected;

  const toggle = (index: number, on: boolean) => {
    setLocalChecked(prev => (on ? [...prev.filter(i => i !== index), index] : prev.filter(i => i !== index)));
    onItemCheck?.(index, on);
  };
  const pick = (item: string) => { setLocalSelected(item); onSelectionChange?.(item); };

  return (
    <div role="listbox" id={id} title={tooltip} aria-disabled={disabled} aria-multiselectable className={cls('wf-checkedlist', className)}>
      {items.map((item, i) => (
        <label key={`${i}:${item}`} role="option" aria-selected={selected === item} className={cls('wf-checkedlist-row', selected === item ? 'wf-checkedlist-selected' : undefined)}
               onClick={() => !disabled && pick(item)}>
          <input type="checkbox" disabled={disabled} checked={checked.includes(i)} onChange={e => toggle(i, e.target.checked)} />
          <span>{item}</span>
        </label>
      ))}
    </div>
  );
}

// ---------------------------------------------------------------------------------------------------------- DomainUpDown

export interface WfDomainUpDownProps extends Common {
  items?: string[];
  value?: string;
  readOnly?: boolean;
  onSelectionChange?: (value: string) => void;
}
/** Text box with up/down buttons that step through a list of strings (DomainUpDown). Arrow keys step too. */
export function WfDomainUpDown({ items = [], value, readOnly, onSelectionChange, disabled, id, className, tooltip }: WfDomainUpDownProps) {
  const [local, setLocal] = useState(value ?? items[0] ?? '');
  const shown = onSelectionChange ? value ?? '' : local;
  const set = (v: string) => { setLocal(v); onSelectionChange?.(v); };
  const step = (delta: number) => {
    if (items.length === 0) return;
    const at = items.indexOf(shown);
    set(items[Math.min(Math.max(at < 0 ? (delta > 0 ? 0 : items.length - 1) : at + delta, 0), items.length - 1)]);
  };
  const onKey = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowUp') { e.preventDefault(); step(-1); } else if (e.key === 'ArrowDown') { e.preventDefault(); step(1); }
  };
  return (
    <span className={cls('wf-updown', className)} title={tooltip}>
      <input type="text" id={id} role="spinbutton" aria-valuetext={shown} disabled={disabled} readOnly={readOnly} className="wf-textbox" value={shown}
             onKeyDown={onKey} onChange={e => set(e.target.value)} />
      <span className="wf-updown-buttons">
        <button type="button" tabIndex={-1} aria-label="Previous" disabled={disabled} onClick={() => step(-1)}>▴</button>
        <button type="button" tabIndex={-1} aria-label="Next" disabled={disabled} onClick={() => step(1)}>▾</button>
      </span>
    </span>
  );
}

// ---------------------------------------------------------------------------------------------------------- TrackBar / ScrollBar

export interface WfRangeProps extends Common {
  value?: number; min?: number; max?: number; step?: number;
  orientation?: 'horizontal' | 'vertical';
  onValueChange?: (value: number) => void;
}

function Range({ kind, value, min = 0, max = 10, step = 1, orientation = 'horizontal', onValueChange, disabled, id, className, tooltip }: WfRangeProps & { kind: 'trackbar' | 'scrollbar' }) {
  return (
    <input type="range" id={id} title={tooltip} disabled={disabled} min={min} max={max} step={step} aria-orientation={orientation}
           className={cls(`wf-range wf-${kind} wf-range-${orientation}`, className)}
           {...(onValueChange ? { value: value ?? min } : { defaultValue: value ?? min })}
           onChange={e => onValueChange?.(e.target.valueAsNumber)} />
  );
}
/** Slider (TrackBar): a native range input, horizontal or vertical. */
export function WfTrackBar(props: WfRangeProps) { return <Range kind="trackbar" {...props} />; }
/** Scroll bar as a value control (HScrollBar / VScrollBar): a native range input; defaults to 0..100. */
export function WfScrollBar(props: WfRangeProps) { return <Range kind="scrollbar" max={100} {...props} />; }
