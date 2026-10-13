import type { ReactNode } from 'react';
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
  onValueChange?: (value: string) => void; onEnter?: () => void;
}
export function WfTextBox({ value, placeholder, readOnly, password, multiline, maxLength, onValueChange, onEnter, disabled, id, className, tooltip }: WfTextBoxProps) {
  // Controlled when someone listens for changes; otherwise `value` is only the starting text and the user can still type (a preview or an unwired field).
  const common = {
    id, title: tooltip, placeholder, readOnly, disabled, maxLength,
    ...(onValueChange ? { value: value ?? '' } : { defaultValue: value ?? '' }),
    className: cls('wf-textbox', className),
    onChange: (e: { target: { value: string } }) => onValueChange?.(e.target.value),
  };
  if (multiline) return <textarea {...common} rows={4} />;
  return <input {...common} type={password ? 'password' : 'text'} onKeyDown={e => { if (e.key === 'Enter') onEnter?.(); }} />;
}

export interface WfComboBoxProps extends Common { items?: string[]; value?: string; onSelectionChange?: (value: string) => void }
export function WfComboBox({ items = [], value, onSelectionChange, disabled, id, className, tooltip }: WfComboBoxProps) {
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
