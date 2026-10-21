import { useState } from 'react';
import { applyMask } from '@wfm/ui-core';

interface Common { id?: string; className?: string; tooltip?: string; disabled?: boolean }
const cls = (base: string, extra?: string): string => (extra ? `${base} ${extra}` : base);

// ---------------------------------------------------------------------------------------------------------- MaskedTextBox

export interface WfMaskedTextBoxProps extends Common {
  value?: string;
  /** WinForms `Mask` string, for example `(000) 000-0000`. Without a mask the box behaves as a plain text box. */
  mask?: string;
  readOnly?: boolean;
  onValueChange?: (value: string) => void;
}
/** Text box that formats what is typed through a WinForms mask (MaskedTextBox). The value is the formatted text. */
export function WfMaskedTextBox({ value, mask, readOnly, onValueChange, disabled, id, className, tooltip }: WfMaskedTextBoxProps) {
  const [local, setLocal] = useState(() => applyMask(mask, value ?? ''));
  const shown = onValueChange ? applyMask(mask, value ?? '') : local;
  return (
    <input type="text" id={id} title={tooltip} disabled={disabled} readOnly={readOnly} placeholder={mask ? mask.replace(/[0-9#]/g, '_').replace(/[9?aAC&L]/g, '_') : undefined}
           className={cls('wf-textbox wf-masked', className)} value={shown}
           onChange={e => { const v = applyMask(mask, e.target.value); setLocal(v); onValueChange?.(v); }} />
  );
}

// ---------------------------------------------------------------------------------------------------------- RichTextBox

export interface WfRichTextBoxProps extends Common { value?: string; readOnly?: boolean; onValueChange?: (value: string) => void }
/** Multi-line text area (RichTextBox). Plain text only: character formatting and embedded objects of the original are not reproduced. */
export function WfRichTextBox({ value, readOnly, onValueChange, disabled, id, className, tooltip }: WfRichTextBoxProps) {
  return (
    <textarea id={id} title={tooltip} disabled={disabled} readOnly={readOnly} className={cls('wf-textbox wf-richtext', className)}
              {...(onValueChange ? { value: value ?? '' } : { defaultValue: value ?? '' })} onChange={e => onValueChange?.(e.target.value)} />
  );
}

// ---------------------------------------------------------------------------------------------------------- MonthCalendar

const WEEKDAYS = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'];
const pad = (n: number) => String(n).padStart(2, '0');
const iso = (y: number, m: number, d: number) => `${y}-${pad(m + 1)}-${pad(d)}`;
const parse = (value?: string): Date | null => {
  const m = value ? /^(\d{4})-(\d{2})-(\d{2})/.exec(value) : null;
  return m ? new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3])) : null;
};

export interface WfMonthCalendarProps extends Common {
  /** Selected date, ISO text (`2026-01-05`). */
  value?: string;
  onValueChange?: (value: string) => void;
}
/** One-month calendar with previous/next month buttons and a selected day (MonthCalendar). */
export function WfMonthCalendar({ value, onValueChange, disabled, id, className, tooltip }: WfMonthCalendarProps) {
  const [local, setLocal] = useState(value ?? '');
  const selected = parse(onValueChange ? value : local);
  const [view, setView] = useState(() => { const d = selected ?? new Date(); return { y: d.getFullYear(), m: d.getMonth() }; });
  const today = new Date();
  const first = new Date(view.y, view.m, 1).getDay();
  const days = new Date(view.y, view.m + 1, 0).getDate();
  const cells: (number | null)[] = [...Array<null>(first).fill(null), ...Array.from({ length: days }, (_, i) => i + 1)];
  const step = (delta: number) => setView(v => { const d = new Date(v.y, v.m + delta, 1); return { y: d.getFullYear(), m: d.getMonth() }; });
  const pick = (day: number) => { const v = iso(view.y, view.m, day); setLocal(v); onValueChange?.(v); };
  const title = new Date(view.y, view.m, 1).toLocaleString(undefined, { month: 'long', year: 'numeric' });

  return (
    <div id={id} title={tooltip} role="group" aria-label="Calendar" aria-disabled={disabled} className={cls('wf-calendar', className)}>
      <div className="wf-calendar-head">
        <button type="button" disabled={disabled} aria-label="Previous month" onClick={() => step(-1)}>‹</button>
        <span aria-live="polite">{title}</span>
        <button type="button" disabled={disabled} aria-label="Next month" onClick={() => step(1)}>›</button>
      </div>
      <div className="wf-calendar-grid" role="grid">
        {WEEKDAYS.map(w => <span key={w} className="wf-calendar-dow" role="columnheader">{w}</span>)}
        {cells.map((day, i) => day === null
          ? <span key={`e${i}`} />
          : <button type="button" key={day} disabled={disabled} role="gridcell" onClick={() => pick(day)}
                    aria-selected={selected?.getFullYear() === view.y && selected.getMonth() === view.m && selected.getDate() === day}
                    className={cls('wf-calendar-day', today.getFullYear() === view.y && today.getMonth() === view.m && today.getDate() === day ? 'wf-calendar-today' : undefined)}>{day}</button>)}
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------------------------------------- ListBox

export interface WfListBoxProps extends Common { items?: string[]; value?: string; onSelectionChange?: (value: string) => void }
/** Always-open single-selection list of strings (ListBox). The value is the selected text; empty when nothing is selected. */
export function WfListBox({ items = [], value, onSelectionChange, disabled, id, className, tooltip }: WfListBoxProps) {
  return (
    <select id={id} title={tooltip} disabled={disabled} size={Math.max(4, Math.min(items.length, 12))} className={cls('wf-listbox', className)}
            {...(onSelectionChange ? { value: value ?? '' } : { defaultValue: value ?? '' })} onChange={e => onSelectionChange?.(e.target.value)}>
      {items.map((item, i) => <option key={`${i}:${item}`} value={item}>{item}</option>)}
    </select>
  );
}
