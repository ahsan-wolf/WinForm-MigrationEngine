import { useEffect, useRef, useState, type ReactNode } from 'react';
import { wfFilterInput, type WfInputRule } from '@wfm/ui-core';
import { MnemonicText, parseMnemonic } from '../mnemonic';

interface Common { id?: string; className?: string; tooltip?: string; disabled?: boolean }
const cls = (base: string, extra?: string): string => (extra ? `${base} ${extra}` : base);

export interface WfLabelProps extends Common { text?: string; htmlFor?: string }
export function WfLabel({ text, htmlFor, id, className, tooltip }: WfLabelProps) {
  return <label id={id} htmlFor={htmlFor} title={tooltip} className={cls('wf-label', className)}><MnemonicText text={text} /></label>;
}

export interface WfButtonProps extends Common { text?: string; variant?: 'default' | 'primary'; onClick?: () => void }
export function WfButton({ text, variant = 'default', onClick, disabled, id, className, tooltip }: WfButtonProps) {
  return (
    <button type="button" id={id} title={tooltip} disabled={disabled} accessKey={parseMnemonic(text).accessKey}
            className={cls(`wf-button wf-button-${variant}`, className)} onClick={onClick}>
      <MnemonicText text={text} />
    </button>
  );
}

export interface WfTextBoxProps extends Common {
  value?: string; placeholder?: string; readOnly?: boolean; password?: boolean; multiline?: boolean; maxLength?: number;
  /** Input rule: `letters` accepts only letters and spaces; `number` accepts digits, plus one decimal point with `allowFloat` and a minus sign with `allowNegative`. Typing and pasting follow it. */
  inputRule?: WfInputRule; allowFloat?: boolean; allowNegative?: boolean;
  onValueChange?: (value: string) => void; onEnter?: () => void;
}
export function WfTextBox({ value, placeholder, readOnly, password, multiline, maxLength, inputRule, allowFloat, allowNegative, onValueChange, onEnter, disabled, id, className, tooltip }: WfTextBoxProps) {
  // Controlled when someone listens for changes; otherwise `value` is only the starting text and the user can still type (a preview or an unwired field).
  const common = {
    id, title: tooltip, placeholder, readOnly, disabled, maxLength,
    ...(onValueChange ? { value: value ?? '' } : { defaultValue: value ?? '' }),
    className: cls('wf-textbox', className),
    onChange: (e: { target: { value: string } }) => {
      if (inputRule) {
        const kept = wfFilterInput(inputRule, e.target.value, { allowFloat, allowNegative });
        if (kept !== e.target.value) e.target.value = kept; // an uncontrolled box shows what the rule keeps
      }
      onValueChange?.(e.target.value);
    },
  };
  if (multiline) return <textarea {...common} rows={4} />;
  return <input {...common} type={password ? 'password' : 'text'} onKeyDown={e => { if (e.key === 'Enter') onEnter?.(); }} />;
}

/** A column of a multi-column drop-down. */
export interface WfComboColumn { field: string; header?: string; width?: number }
export interface WfComboBoxProps extends Common {
  items?: string[]; value?: string; onSelectionChange?: (value: string) => void;
  /** With `columns` the drop-down opens a small grid of `rows` (a multi-column combo): `valueField` is the selected value, `displayField` the text shown. */
  columns?: WfComboColumn[]; rows?: Record<string, unknown>[]; valueField?: string; displayField?: string;
}

/** Multi-column drop-down: a button showing the selected row's display text; the panel is a grid with a header, keyboard (arrows, Enter, Escape) and click selection. */
function MultiColumnCombo({ columns, rows = [], value, valueField, displayField, onSelectionChange, disabled, id, className, tooltip }:
  Required<Pick<WfComboBoxProps, 'columns'>> & Omit<WfComboBoxProps, 'columns' | 'items'>) {
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);
  const box = useRef<HTMLDivElement>(null);
  const valueKey = valueField ?? columns[0].field;
  const shownKey = displayField ?? columns.find(c => c.field !== valueKey)?.field ?? valueKey;
  const selected = rows.find(r => String(r[valueKey]) === value);

  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => { if (!box.current?.contains(e.target as Node)) setOpen(false); };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);

  const choose = (row: Record<string, unknown>) => { onSelectionChange?.(String(row[valueKey])); setOpen(false); };
  const onKeyDown = (e: { key: string; preventDefault: () => void }) => {
    if (e.key === 'Escape') setOpen(false);
    else if (e.key === 'ArrowDown') { e.preventDefault(); setOpen(true); setActive(a => Math.min(a + 1, rows.length - 1)); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(a => Math.max(a - 1, 0)); }
    else if (e.key === 'Enter' && open && rows[active]) { e.preventDefault(); choose(rows[active]); }
  };

  return (
    <div ref={box} className={cls('wf-combo-multi', className)}>
      <button type="button" id={id} title={tooltip} disabled={disabled} role="combobox" aria-haspopup="grid" aria-expanded={open}
              className="wf-combobox wf-combo-multi-button" onClick={() => setOpen(o => !o)} onKeyDown={onKeyDown}>
        {selected ? String(selected[shownKey] ?? '') : ''}
      </button>
      {open && (
        <div role="grid" className="wf-combo-multi-panel">
          <table>
            <thead><tr>{columns.map(c => <th key={c.field} style={c.width ? { width: c.width } : undefined}>{c.header ?? c.field}</th>)}</tr></thead>
            <tbody>
              {rows.map((r, i) => (
                <tr key={i} role="row" aria-selected={r === selected} className={i === active ? 'wf-combo-active' : undefined}
                    onMouseEnter={() => setActive(i)} onMouseDown={e => { e.preventDefault(); choose(r); }}>
                  {columns.map(c => <td key={c.field}>{String(r[c.field] ?? '')}</td>)}
                </tr>
              ))}
              {rows.length === 0 && <tr><td colSpan={columns.length}>No rows</td></tr>}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

export function WfComboBox({ items = [], value, onSelectionChange, columns, rows, valueField, displayField, disabled, id, className, tooltip }: WfComboBoxProps) {
  if (columns && columns.length > 0)
    return <MultiColumnCombo columns={columns} rows={rows} value={value} valueField={valueField} displayField={displayField} onSelectionChange={onSelectionChange} disabled={disabled} id={id} className={className} tooltip={tooltip} />;
  return (
    <select id={id} title={tooltip} disabled={disabled} {...(onSelectionChange ? { value: value ?? '' } : { defaultValue: value ?? '' })} className={cls('wf-combobox', className)}
            onChange={e => onSelectionChange?.(e.target.value)}>
      {value !== undefined && value !== '' && !items.includes(value) && <option value={value}>{value}</option>}
      {items.map(item => <option key={item} value={item}>{item}</option>)}
    </select>
  );
}

export interface WfCheckBoxProps extends Common { text?: string; checked?: boolean; onCheckedChange?: (checked: boolean) => void }
export function WfCheckBox({ text, checked, onCheckedChange, disabled, id, className, tooltip }: WfCheckBoxProps) {
  return (
    <label className={cls('wf-checkbox', className)} title={tooltip}>
      <input type="checkbox" id={id} disabled={disabled} {...(onCheckedChange ? { checked: checked ?? false } : { defaultChecked: checked ?? false })}
             accessKey={parseMnemonic(text).accessKey} onChange={e => onCheckedChange?.(e.target.checked)} />
      <span><MnemonicText text={text} /></span>
    </label>
  );
}

export interface WfProgressProps extends Common { value?: number; max?: number; indeterminate?: boolean }
export function WfProgress({ value = 0, max = 100, indeterminate, id, className, tooltip }: WfProgressProps) {
  return (
    <progress id={id} title={tooltip} className={cls('wf-progress', className)} max={max}
              value={indeterminate ? undefined : value} aria-valuenow={indeterminate ? undefined : value} aria-valuemax={max} />
  );
}

export interface WfPictureBoxProps extends Common { src?: string; alt?: string; fit?: 'contain' | 'fill' | 'none' | 'cover'; onClick?: () => void }
export function WfPictureBox({ src, alt = '', fit = 'contain', onClick, id, className, tooltip }: WfPictureBoxProps) {
  if (!src) return <div id={id} role="img" aria-label={alt || tooltip || 'image'} title={tooltip} className={cls('wf-picturebox wf-picturebox-empty', className)} onClick={onClick} />;
  return <img id={id} src={src} alt={alt} title={tooltip} className={cls('wf-picturebox', className)} style={{ objectFit: fit }} onClick={onClick} />;
}

export interface WfUnsupportedProps { type: string; reason?: string; id?: string; children?: ReactNode }
/** Placeholder for any control without a library component. Never hides the gap: the box is visible and named. */
export function WfUnsupported({ type, reason = 'Not migrated — see MIGRATION_REPORT.md', id }: WfUnsupportedProps) {
  return <div id={id} role="note" className="wf-unsupported">⚠ {type.slice(type.lastIndexOf('.') + 1)} — {reason}</div>;
}
