import { Component, ElementRef, HostListener, computed, inject, input, output, signal } from '@angular/core';
import { parseMnemonic, wfFilterInput, type WfInputRule } from '@wfm/ui-core';

/** Host elements add no box of their own: the inner element takes part in the parent's layout as if it were a direct child. */
const HOST = { style: 'display: contents' } as const;

/** Renders a WinForms mnemonic label ("E&xit"): the access key letter is underlined. */
@Component({
  selector: 'wf-mnemonic',
  standalone: true,
  host: HOST,
  template: `@if (m().index < 0) { <ng-container>{{ m().label }}</ng-container> } @else { <ng-container>{{ m().label.slice(0, m().index) }}<u>{{ m().label[m().index] }}</u>{{ m().label.slice(m().index + 1) }}</ng-container> }`,
})
export class WfMnemonic {
  text = input<string | undefined>();
  m = computed(() => parseMnemonic(this.text()));
}

const cls = (base: string, extra: string | undefined): string => (extra ? `${base} ${extra}` : base);

@Component({
  selector: 'wf-label',
  standalone: true,
  imports: [WfMnemonic],
  host: HOST,
  template: `<label [attr.id]="controlId()" [attr.for]="htmlFor()" [attr.title]="tooltip()" [class]="cls('wf-label', className())"><wf-mnemonic [text]="text()" /></label>`,
})
export class WfLabel {
  text = input<string | undefined>();
  htmlFor = input<string | undefined>();
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  protected cls = cls;
}

@Component({
  selector: 'wf-button',
  standalone: true,
  imports: [WfMnemonic],
  host: HOST,
  template: `<button type="button" [attr.id]="controlId()" [attr.title]="tooltip()" [disabled]="disabled()" [attr.accesskey]="access()"
                     [class]="cls('wf-button wf-button-' + variant(), className())" (click)="wfClick.emit()"><wf-mnemonic [text]="text()" /></button>`,
})
export class WfButton {
  text = input<string | undefined>();
  variant = input<'default' | 'primary'>('default');
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  wfClick = output<void>();
  protected cls = cls;
  protected access = computed(() => parseMnemonic(this.text()).accessKey);
}

@Component({
  selector: 'wf-link-label',
  standalone: true,
  imports: [WfMnemonic],
  host: HOST,
  template: `<a href="#" role="link" [attr.id]="controlId()" [attr.title]="tooltip()" [attr.aria-disabled]="disabled() ? 'true' : null" [class]="cls('wf-link', className())"
                (click)="$event.preventDefault(); disabled() || wfClick.emit()"><wf-mnemonic [text]="text()" /></a>`,
})
export class WfLinkLabel {
  text = input<string | undefined>();
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  wfClick = output<void>();
  protected cls = cls;
}

@Component({
  selector: 'wf-text-box',
  standalone: true,
  host: HOST,
  template: `@if (multiline()) {
      <textarea rows="4" [attr.id]="controlId()" [attr.title]="tooltip()" [attr.placeholder]="placeholder()" [readOnly]="!!readOnly()" [disabled]="!!disabled()"
                [attr.maxlength]="maxLength()" [value]="value() ?? ''" [class]="cls('wf-textbox', className())" (input)="wfValueChange.emit($any($event.target).value)"></textarea>
    } @else {
      <input [type]="password() ? 'password' : 'text'" [attr.id]="controlId()" [attr.title]="tooltip()" [attr.placeholder]="placeholder()" [readOnly]="!!readOnly()"
             [disabled]="!!disabled()" [attr.maxlength]="maxLength()" [value]="value() ?? ''" [class]="cls('wf-textbox', className())"
             (input)="onInput($event)" (keydown)="onKeyDown($event)" />
    }`,
})
export class WfTextBox {
  value = input<string | undefined>();
  placeholder = input<string | undefined>();
  readOnly = input<boolean | undefined>(false);
  password = input<boolean | undefined>(false);
  multiline = input<boolean | undefined>(false);
  maxLength = input<number | undefined>();
  /** `letters` accepts only letters and spaces; `number` accepts digits, plus one decimal point with `allowFloat` and a minus sign with `allowNegative`. */
  inputRule = input<WfInputRule | undefined>();
  allowFloat = input<boolean | undefined>(false);
  allowNegative = input<boolean | undefined>(false);
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  wfValueChange = output<string>();
  wfEnter = output<void>();
  protected cls = cls;

  /** A statement, not an expression: an expression template handler that evaluates to `false` makes Angular cancel the key press. */
  protected onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Enter') this.wfEnter.emit();
  }

  protected onInput(event: Event): void {
    const box = event.target as HTMLInputElement;
    if (this.inputRule()) {
      const kept = wfFilterInput(this.inputRule(), box.value, { allowFloat: !!this.allowFloat(), allowNegative: !!this.allowNegative() });
      if (kept !== box.value) box.value = kept;
    }
    this.wfValueChange.emit(box.value);
  }
}

/** A column of a multi-column drop-down. */
export interface WfComboColumn { field: string; header?: string; width?: number }

@Component({
  selector: 'wf-combo-box',
  standalone: true,
  host: HOST,
  template: `@if (columns() && columns()!.length > 0) {
      <div [class]="cls('wf-combo-multi', className())">
        <button type="button" [attr.id]="controlId()" [attr.title]="tooltip()" [disabled]="!!disabled()" role="combobox" aria-haspopup="grid" [attr.aria-expanded]="open()"
                class="wf-combobox wf-combo-multi-button" (click)="open.set(!open())" (keydown)="onKey($event)">{{ shownText() }}</button>
        @if (open()) {
          <div role="grid" class="wf-combo-multi-panel">
            <table>
              <thead><tr>@for (c of columns()!; track c.field) { <th [style.width.px]="c.width">{{ c.header ?? c.field }}</th> }</tr></thead>
              <tbody>
                @for (r of rows(); track $index) {
                  <tr role="row" [attr.aria-selected]="isSelected(r)" [class.wf-combo-active]="$index === active()" (mouseenter)="active.set($index)" (mousedown)="$event.preventDefault(); choose(r)">
                    @for (c of columns()!; track c.field) { <td>{{ cell(r, c.field) }}</td> }
                  </tr>
                } @empty { <tr><td [attr.colspan]="columns()!.length">No rows</td></tr> }
              </tbody>
            </table>
          </div>
        }
      </div>
    } @else {
      <select [attr.id]="controlId()" [attr.title]="tooltip()" [disabled]="!!disabled()" [class]="cls('wf-combobox', className())"
                     (change)="wfSelectionChange.emit($any($event.target).value)">
        @if (value() !== undefined && value() !== '' && !items().includes(value()!)) { <option [value]="value()!" selected>{{ value() }}</option> }
        @for (item of items(); track $index) { <option [value]="item" [selected]="item === value()">{{ item }}</option> }
      </select>
    }`,
})
export class WfComboBox {
  items = input<string[]>([]);
  value = input<string | undefined>();
  /** With `columns` the drop-down opens a small grid of `rows` (a multi-column combo): `valueField` is the selected value, `displayField` the text shown. */
  columns = input<WfComboColumn[] | undefined>();
  rows = input<Record<string, unknown>[]>([]);
  valueField = input<string | undefined>();
  displayField = input<string | undefined>();
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  wfSelectionChange = output<string>();
  protected cls = cls;

  protected readonly open = signal(false);
  protected readonly active = signal(-1);
  private readonly host = inject(ElementRef<HTMLElement>);

  private valueKey = (): string => this.valueField() ?? this.columns()?.[0]?.field ?? '';
  private shownKey = (): string => this.displayField() ?? this.columns()?.find(c => c.field !== this.valueKey())?.field ?? this.valueKey();
  protected cell = (row: Record<string, unknown>, field: string): string => String(row[field] ?? '');
  protected isSelected = (row: Record<string, unknown>): boolean => String(row[this.valueKey()]) === this.value();
  protected shownText = computed(() => {
    const selected = this.rows().find(r => String(r[this.valueKey()]) === this.value());
    return selected ? String(selected[this.shownKey()] ?? '') : '';
  });

  protected choose(row: Record<string, unknown>): void {
    this.wfSelectionChange.emit(String(row[this.valueKey()]));
    this.open.set(false);
  }

  protected onKey(e: KeyboardEvent): void {
    if (e.key === 'Escape') this.open.set(false);
    else if (e.key === 'ArrowDown') { e.preventDefault(); this.open.set(true); this.active.update(a => Math.min(a + 1, this.rows().length - 1)); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); this.active.update(a => Math.max(a - 1, 0)); }
    else if (e.key === 'Enter' && this.open() && this.rows()[this.active()]) { e.preventDefault(); this.choose(this.rows()[this.active()]); }
  }

  @HostListener('document:mousedown', ['$event'])
  protected closeOnOutside(e: MouseEvent): void {
    if (this.open() && !(this.host.nativeElement as HTMLElement).contains(e.target as Node)) this.open.set(false);
  }
}

@Component({
  selector: 'wf-check-box',
  standalone: true,
  imports: [WfMnemonic],
  host: HOST,
  template: `<label [class]="cls('wf-checkbox', className())" [attr.title]="tooltip()">
      <input type="checkbox" [attr.id]="controlId()" [disabled]="!!disabled()" [checked]="!!checked()" [attr.accesskey]="access()" (change)="wfCheckedChange.emit($any($event.target).checked)" />
      <span><wf-mnemonic [text]="text()" /></span>
    </label>`,
})
export class WfCheckBox {
  text = input<string | undefined>();
  checked = input<boolean | undefined>(false);
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  wfCheckedChange = output<boolean>();
  protected cls = cls;
  protected access = computed(() => parseMnemonic(this.text()).accessKey);
}

@Component({
  selector: 'wf-radio-button',
  standalone: true,
  imports: [WfMnemonic],
  host: HOST,
  template: `<label [class]="cls('wf-checkbox wf-radio', className())" [attr.title]="tooltip()">
      <input type="radio" [attr.id]="controlId()" [attr.name]="group()" [disabled]="!!disabled()" [checked]="!!checked()" [attr.accesskey]="access()" (change)="wfCheckedChange.emit($any($event.target).checked)" />
      <span><wf-mnemonic [text]="text()" /></span>
    </label>`,
})
export class WfRadioButton {
  text = input<string | undefined>();
  checked = input<boolean | undefined>(false);
  /** Radio buttons with the same group are mutually exclusive (WinForms: the radio buttons of one container). */
  group = input<string | undefined>();
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  wfCheckedChange = output<boolean>();
  protected cls = cls;
  protected access = computed(() => parseMnemonic(this.text()).accessKey);
}

@Component({
  selector: 'wf-numeric-up-down',
  standalone: true,
  host: HOST,
  template: `<input type="number" [attr.id]="controlId()" [attr.title]="tooltip()" [disabled]="!!disabled()" [readOnly]="!!readOnly()" [attr.min]="min()" [attr.max]="max()"
                    [attr.step]="step() ?? (decimals() > 0 ? 1 / 10 ** decimals() : 1)" [class]="cls('wf-textbox wf-numeric', className())" [value]="value() ?? ''"
                    (input)="onInput($any($event.target).valueAsNumber)" />`,
})
export class WfNumericUpDown {
  value = input<number | undefined>();
  min = input<number | undefined>();
  max = input<number | undefined>();
  step = input<number | undefined>();
  decimals = input(0);
  readOnly = input<boolean | undefined>(false);
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  wfValueChange = output<number>();
  protected cls = cls;
  protected onInput(n: number): void {
    if (!Number.isNaN(n)) this.wfValueChange.emit(n);
  }
}

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
function limit(date: string | undefined, kind: WfDateKind, time: string): string | null {
  if (!date || kind === 'time' || !/^\d{4}-\d{2}-\d{2}/.test(date)) return null;
  return kind === 'datetime' ? `${date.slice(0, 10)}T${time}` : date.slice(0, 10);
}

/** Date, date-time or time field (WinForms DateTimePicker): a native picker; the value is ISO text. */
@Component({
  selector: 'wf-date-time-picker',
  standalone: true,
  host: HOST,
  template: `<input [type]="kind() === 'datetime' ? 'datetime-local' : kind()" [attr.id]="controlId()" [attr.title]="tooltip()" [disabled]="!!disabled()" [readOnly]="!!readOnly()"
                    [attr.min]="min()" [attr.max]="max()" [class]="cls('wf-textbox wf-datetime', className())" [value]="shown()" (input)="wfValueChange.emit($any($event.target).value)" />`,
})
export class WfDateTimePicker {
  /** ISO text: `2026-01-05`, `2026-01-05T09:30:00` or `09:30`. */
  value = input<string | undefined>();
  kind = input<WfDateKind>('date');
  minDate = input<string | undefined>();
  maxDate = input<string | undefined>();
  readOnly = input<boolean | undefined>(false);
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  wfValueChange = output<string>();
  protected cls = cls;
  protected shown = computed(() => inputValue(this.value(), this.kind()));
  protected min = computed(() => limit(this.minDate(), this.kind(), '00:00'));
  protected max = computed(() => limit(this.maxDate(), this.kind(), '23:59'));
}

@Component({
  selector: 'wf-progress',
  standalone: true,
  host: HOST,
  template: `<progress [attr.id]="controlId()" [attr.title]="tooltip()" [class]="cls('wf-progress', className())" [attr.max]="max()" [attr.value]="indeterminate() ? null : value()"
                      [attr.aria-valuenow]="indeterminate() ? null : value()" [attr.aria-valuemax]="max()"></progress>`,
})
export class WfProgress {
  value = input(0);
  max = input(100);
  indeterminate = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  protected cls = cls;
}

@Component({
  selector: 'wf-picture-box',
  standalone: true,
  host: HOST,
  template: `@if (src()) {
      <img [attr.id]="controlId()" [src]="src()" [alt]="alt()" [attr.title]="tooltip()" [class]="cls('wf-picturebox', className())" [style.object-fit]="fit()" (click)="wfClick.emit()" />
    } @else {
      <div [attr.id]="controlId()" role="img" [attr.aria-label]="alt() || tooltip() || 'image'" [attr.title]="tooltip()" [class]="cls('wf-picturebox wf-picturebox-empty', className())" (click)="wfClick.emit()"></div>
    }`,
})
export class WfPictureBox {
  src = input<string | undefined>();
  alt = input('');
  fit = input<'contain' | 'fill' | 'none' | 'cover'>('contain');
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  wfClick = output<void>();
  protected cls = cls;
}

/** Placeholder for any control without a library component. Never hides the gap: the box is visible and named. */
@Component({
  selector: 'wf-unsupported',
  standalone: true,
  host: HOST,
  template: `<div [attr.id]="controlId()" role="note" class="wf-unsupported">⚠ {{ name() }} — {{ reason() }}</div>`,
})
export class WfUnsupported {
  type = input('');
  reason = input('Not migrated — see MIGRATION_REPORT.md');
  controlId = input<string | undefined>();
  protected name = computed(() => this.type().slice(this.type().lastIndexOf('.') + 1));
}

/** Colour chooser (WinForms ColorDialog / colour editors): the browser's native colour input; the value is `#rrggbb`. */
@Component({
  selector: 'wf-color-picker',
  standalone: true,
  host: HOST,
  template: `<input type="color" [attr.id]="controlId()" [attr.title]="tooltip()" [disabled]="!!disabled()" [class]="cls('wf-colorpicker', className())" [attr.aria-label]="tooltip() ?? 'Colour'"
                    [value]="value()" (input)="wfValueChange.emit($any($event.target).value)" />`,
})
export class WfColorPicker {
  value = input('#000000');
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  wfValueChange = output<string>();
  protected cls = cls;
}

/** Font families offered by default: the web-safe set every browser has. */
export const WF_FONT_FAMILIES = ['Arial', 'Calibri', 'Cambria', 'Courier New', 'Georgia', 'Segoe UI', 'Tahoma', 'Times New Roman', 'Trebuchet MS', 'Verdana'];

/** Font chooser (WinForms FontDialog / font editors): a family drop-down with a live preview line; the value is the family name. */
@Component({
  selector: 'wf-font-picker',
  standalone: true,
  host: HOST,
  template: `<span [class]="cls('wf-fontpicker', className())">
      <select [attr.id]="controlId()" [attr.title]="tooltip()" [disabled]="!!disabled()" class="wf-combobox" [attr.aria-label]="tooltip() ?? 'Font'" (change)="wfValueChange.emit($any($event.target).value)">
        @for (f of list(); track f) { <option [value]="f" [selected]="f === value()">{{ f }}</option> }
      </select>
      <span class="wf-fontpicker-preview" [style.font-family]="value()" [style.font-size.pt]="size()" [style.font-weight]="bold() ? 700 : 400" [style.font-style]="italic() ? 'italic' : 'normal'">AaBb 123</span>
    </span>`,
})
export class WfFontPicker {
  value = input('Segoe UI');
  size = input(10);
  bold = input<boolean | undefined>(false);
  italic = input<boolean | undefined>(false);
  families = input<string[]>(WF_FONT_FAMILIES);
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  wfValueChange = output<string>();
  protected cls = cls;
  protected list = computed(() => (this.families().includes(this.value()) ? this.families() : [this.value(), ...this.families()]));
}
