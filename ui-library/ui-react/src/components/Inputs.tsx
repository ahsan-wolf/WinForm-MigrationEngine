import { MnemonicText, parseMnemonic } from '../mnemonic';

interface Common { id?: string; className?: string; tooltip?: string; disabled?: boolean }
const cls = (base: string, extra?: string): string => (extra ? `${base} ${extra}` : base);

export type WfDateKind = 'date' | 'datetime' | 'time';

/** ISO text -> the string a native date/time input accepts (`2026-01-05`, `2026-01-05T09:30`, `09:30`); anything else shows empty. */
function inputValue(value: string | undefined, kind: WfDateKind): string {
  if (!value) return '';
  const iso = value.replace(' ', 'T');
  if (kind === 'date') return /^\d{4}-\d{2}-\d{2}/.test(iso) ? iso.slice(0, 10) : '';
  if (kind === 'datetime') return /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/.test(iso) ? iso.slice(0, 16) : '';
  const time = iso.includes('T') ? iso.slice(iso.indexOf('T') + 1) : iso;
  return /^\d{2}:\d{2}/.test(time) ? time.slice(0, 5) : '';
}

/** A min/max ISO date as the string the input expects for the kind (time-only pickers have no date limit). */
function limit(date: string | undefined, kind: WfDateKind, time: string): string | undefined {
  if (!date || kind === 'time' || !/^\d{4}-\d{2}-\d{2}/.test(date)) return undefined;
  return kind === 'datetime' ? `${date.slice(0, 10)}T${time}` : date.slice(0, 10);
}

export interface WfDateTimePickerProps extends Common {
  /** ISO text: `2026-01-05`, `2026-01-05T09:30:00` or `09:30`. */
  value?: string;
  kind?: WfDateKind;
  /** Earliest / latest selectable date (ISO `2026-01-05`; for date-time kinds the time part is ignored). */
  minDate?: string;
  maxDate?: string;
  readOnly?: boolean;
  onValueChange?: (value: string) => void;
}
/** Date, date-time or time field (WinForms DateTimePicker): a native picker; the value is ISO text. */
export function WfDateTimePicker({ value, kind = 'date', minDate, maxDate, readOnly, onValueChange, disabled, id, className, tooltip }: WfDateTimePickerProps) {
  return (
    <input type={kind === 'datetime' ? 'datetime-local' : kind} id={id} title={tooltip} disabled={disabled} readOnly={readOnly}
           min={limit(minDate, kind, '00:00')} max={limit(maxDate, kind, '23:59')} className={cls('wf-textbox wf-datetime', className)} {...(onValueChange ? { value: inputValue(value, kind) } : { defaultValue: inputValue(value, kind) })}
           onChange={e => onValueChange?.(e.target.value)} />
  );
}

export interface WfRadioButtonProps extends Common {
  text?: string; checked?: boolean;
  /** Radio buttons with the same group are mutually exclusive (WinForms: the radio buttons of one container). */
  group?: string;
  onCheckedChange?: (checked: boolean) => void;
}
export function WfRadioButton({ text, checked, group, onCheckedChange, disabled, id, className, tooltip }: WfRadioButtonProps) {
  return (
    <label className={cls('wf-checkbox wf-radio', className)} title={tooltip}>
      <input type="radio" id={id} name={group} disabled={disabled} {...(onCheckedChange ? { checked: checked ?? false } : { defaultChecked: checked ?? false })}
             accessKey={parseMnemonic(text).accessKey} onChange={e => onCheckedChange?.(e.target.checked)} />
      <span><MnemonicText text={text} /></span>
    </label>
  );
}

export interface WfNumericUpDownProps extends Common {
  value?: number; min?: number; max?: number; step?: number; decimals?: number; readOnly?: boolean;
  onValueChange?: (value: number) => void;
}
export function WfNumericUpDown({ value, min, max, step, decimals = 0, readOnly, onValueChange, disabled, id, className, tooltip }: WfNumericUpDownProps) {
  return (
    <input type="number" id={id} title={tooltip} disabled={disabled} readOnly={readOnly} min={min} max={max}
           step={step ?? (decimals > 0 ? 1 / 10 ** decimals : 1)} className={cls('wf-textbox wf-numeric', className)}
           {...(onValueChange ? { value: value ?? '' } : { defaultValue: value ?? '' })}
           onChange={e => { const n = e.target.valueAsNumber; if (!Number.isNaN(n)) onValueChange?.(n); }} />
  );
}

export interface WfLinkLabelProps extends Common { text?: string; onClick?: () => void }
/** Text that acts as a link (WinForms LinkLabel): keyboard-reachable, link colour, underline on hover. */
export function WfLinkLabel({ text, onClick, disabled, id, className, tooltip }: WfLinkLabelProps) {
  return (
    <a href="#" role="link" id={id} title={tooltip} aria-disabled={disabled} className={cls('wf-link', className)}
       onClick={e => { e.preventDefault(); if (!disabled) onClick?.(); }}>
      <MnemonicText text={text} />
    </a>
  );
}
