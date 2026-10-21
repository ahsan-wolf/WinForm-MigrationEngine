import { Component, computed, input, output, signal } from '@angular/core';
import { applyMask } from '@wfm/ui-core';

const HOST = { style: 'display: contents' } as const;
const cls = (base: string, extra: string | undefined): string => (extra ? `${base} ${extra}` : base);

/** List of items with a native check box each (CheckedListBox). Clicking an item selects it and toggles its check box. */
@Component({
  selector: 'wf-checked-list-box',
  standalone: true,
  host: HOST,
  template: `<div role="listbox" [attr.id]="controlId()" [attr.title]="tooltip()" [attr.aria-disabled]="disabled() ? 'true' : null" aria-multiselectable="true" [class]="cls('wf-checkedlist', className())">
      @for (item of items(); track $index; let i = $index) {
        <label role="option" [attr.aria-selected]="selected() === item" [class]="'wf-checkedlist-row' + (selected() === item ? ' wf-checkedlist-selected' : '')" (click)="!disabled() && pick(item)">
          <input type="checkbox" [disabled]="!!disabled()" [checked]="isChecked(i)" (change)="toggle(i, $any($event.target).checked)" />
          <span>{{ item }}</span>
        </label>
      }
    </div>`,
})
export class WfCheckedListBox {
  items = input<string[]>([]);
  /** Indices of the checked items (WinForms `CheckedIndices`). */
  checkedIndices = input<number[] | undefined>();
  /** Selected item text (WinForms `Text` / `SelectedItem`). */
  value = input<string | undefined>();
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  wfSelectionChange = output<string>();
  /** `[index, checked]` */
  wfItemCheck = output<[number, boolean]>();
  protected cls = cls;
  private readonly localChecked = signal<number[]>([]);
  private readonly localSelected = signal<string | undefined>(undefined);
  protected selected = () => this.value() ?? this.localSelected();
  protected isChecked = (i: number) => (this.checkedIndices() ?? this.localChecked()).includes(i);

  protected toggle(index: number, on: boolean): void {
    this.localChecked.update((prev) => (on ? [...prev.filter((i) => i !== index), index] : prev.filter((i) => i !== index)));
    this.wfItemCheck.emit([index, on]);
  }

  protected pick(item: string): void {
    this.localSelected.set(item);
    this.wfSelectionChange.emit(item);
  }
}

/** Text box with up/down buttons that step through a list of strings (DomainUpDown). Arrow keys step too. */
@Component({
  selector: 'wf-domain-up-down',
  standalone: true,
  host: HOST,
  template: `<span [class]="cls('wf-updown', className())" [attr.title]="tooltip()">
      <input type="text" [attr.id]="controlId()" role="spinbutton" [attr.aria-valuetext]="shown()" [disabled]="!!disabled()" [readOnly]="!!readOnly()" class="wf-textbox" [value]="shown()"
             (keydown)="onKey($event)" (input)="set($any($event.target).value)" />
      <span class="wf-updown-buttons">
        <button type="button" tabindex="-1" aria-label="Previous" [disabled]="!!disabled()" (click)="step(-1)">▴</button>
        <button type="button" tabindex="-1" aria-label="Next" [disabled]="!!disabled()" (click)="step(1)">▾</button>
      </span>
    </span>`,
})
export class WfDomainUpDown {
  items = input<string[]>([]);
  value = input<string | undefined>();
  readOnly = input<boolean | undefined>(false);
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  wfSelectionChange = output<string>();
  protected cls = cls;
  private readonly local = signal<string | undefined>(undefined);
  protected shown = () => this.value() ?? this.local() ?? this.items()[0] ?? '';

  protected set(v: string): void {
    this.local.set(v);
    this.wfSelectionChange.emit(v);
  }

  protected step(delta: number): void {
    const items = this.items();
    if (items.length === 0) return;
    const at = items.indexOf(this.shown());
    this.set(items[Math.min(Math.max(at < 0 ? (delta > 0 ? 0 : items.length - 1) : at + delta, 0), items.length - 1)]);
  }

  protected onKey(e: KeyboardEvent): void {
    if (e.key === 'ArrowUp') { e.preventDefault(); this.step(-1); } else if (e.key === 'ArrowDown') { e.preventDefault(); this.step(1); }
  }
}

/** Slider (TrackBar): a native range input, horizontal or vertical. */
@Component({
  selector: 'wf-track-bar',
  standalone: true,
  host: HOST,
  template: `<input type="range" [attr.id]="controlId()" [attr.title]="tooltip()" [disabled]="!!disabled()" [attr.min]="min()" [attr.max]="max()" [attr.step]="step()" [attr.aria-orientation]="orientation()"
                    [class]="cls('wf-range wf-trackbar wf-range-' + orientation(), className())" [value]="value() ?? min()" (input)="wfValueChange.emit($any($event.target).valueAsNumber)" />`,
})
export class WfTrackBar {
  value = input<number | undefined>();
  min = input(0);
  max = input(10);
  step = input(1);
  orientation = input<'horizontal' | 'vertical'>('horizontal');
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  wfValueChange = output<number>();
  protected cls = cls;
}

/** Scroll bar as a value control (HScrollBar / VScrollBar): a native range input; defaults to 0..100. */
@Component({
  selector: 'wf-scroll-bar',
  standalone: true,
  host: HOST,
  template: `<input type="range" [attr.id]="controlId()" [attr.title]="tooltip()" [disabled]="!!disabled()" [attr.min]="min()" [attr.max]="max()" [attr.step]="step()" [attr.aria-orientation]="orientation()"
                    [class]="cls('wf-range wf-scrollbar wf-range-' + orientation(), className())" [value]="value() ?? min()" (input)="wfValueChange.emit($any($event.target).valueAsNumber)" />`,
})
export class WfScrollBar {
  value = input<number | undefined>();
  min = input(0);
  max = input(100);
  step = input(1);
  orientation = input<'horizontal' | 'vertical'>('horizontal');
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  wfValueChange = output<number>();
  protected cls = cls;
}

/** Text box that formats what is typed through a WinForms mask (MaskedTextBox). The value is the formatted text. */
@Component({
  selector: 'wf-masked-text-box',
  standalone: true,
  host: HOST,
  template: `<input type="text" [attr.id]="controlId()" [attr.title]="tooltip()" [disabled]="!!disabled()" [readOnly]="!!readOnly()" [attr.placeholder]="placeholder()" [class]="cls('wf-textbox wf-masked', className())"
                    [value]="shown()" (input)="onInput($any($event.target).value)" />`,
})
export class WfMaskedTextBox {
  value = input<string | undefined>();
  /** WinForms `Mask` string, for example `(000) 000-0000`. Without a mask the box behaves as a plain text box. */
  mask = input<string | undefined>();
  readOnly = input<boolean | undefined>(false);
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  wfValueChange = output<string>();
  protected cls = cls;
  private readonly local = signal<string | undefined>(undefined);
  protected shown = computed(() => applyMask(this.mask(), this.value() ?? this.local() ?? ''));
  protected placeholder = computed(() => (this.mask() ? this.mask()!.replace(/[0-9#]/g, '_').replace(/[9?aAC&L]/g, '_') : null));

  protected onInput(text: string): void {
    const v = applyMask(this.mask(), text);
    this.local.set(v);
    this.wfValueChange.emit(v);
  }
}

/** Multi-line text area (RichTextBox). Plain text only: character formatting and embedded objects of the original are not reproduced. */
@Component({
  selector: 'wf-rich-text-box',
  standalone: true,
  host: HOST,
  template: `<textarea [attr.id]="controlId()" [attr.title]="tooltip()" [disabled]="!!disabled()" [readOnly]="!!readOnly()" [class]="cls('wf-textbox wf-richtext', className())" [value]="value() ?? ''"
                       (input)="wfValueChange.emit($any($event.target).value)"></textarea>`,
})
export class WfRichTextBox {
  value = input<string | undefined>();
  readOnly = input<boolean | undefined>(false);
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  wfValueChange = output<string>();
  protected cls = cls;
}

const pad = (n: number) => String(n).padStart(2, '0');
const isoDate = (y: number, m: number, d: number) => `${y}-${pad(m + 1)}-${pad(d)}`;
const parseDate = (value?: string): Date | null => {
  const m = value ? /^(\d{4})-(\d{2})-(\d{2})/.exec(value) : null;
  return m ? new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3])) : null;
};

/** One-month calendar with previous/next month buttons and a selected day (MonthCalendar). */
@Component({
  selector: 'wf-month-calendar',
  standalone: true,
  host: HOST,
  template: `<div [attr.id]="controlId()" [attr.title]="tooltip()" role="group" aria-label="Calendar" [attr.aria-disabled]="disabled() ? 'true' : null" [class]="cls('wf-calendar', className())">
      <div class="wf-calendar-head">
        <button type="button" [disabled]="!!disabled()" aria-label="Previous month" (click)="stepMonth(-1)">‹</button>
        <span aria-live="polite">{{ title() }}</span>
        <button type="button" [disabled]="!!disabled()" aria-label="Next month" (click)="stepMonth(1)">›</button>
      </div>
      <div class="wf-calendar-grid" role="grid">
        @for (w of weekdays; track w) { <span class="wf-calendar-dow" role="columnheader">{{ w }}</span> }
        @for (day of cells(); track $index) {
          @if (day === null) { <span></span> } @else {
            <button type="button" [disabled]="!!disabled()" role="gridcell" (click)="pick(day)" [attr.aria-selected]="isSelected(day)"
                    [class]="'wf-calendar-day' + (isToday(day) ? ' wf-calendar-today' : '')">{{ day }}</button>
          }
        }
      </div>
    </div>`,
})
export class WfMonthCalendar {
  /** Selected date, ISO text (`2026-01-05`). */
  value = input<string | undefined>();
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  wfValueChange = output<string>();
  protected cls = cls;
  protected readonly weekdays = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'];
  private readonly local = signal<string | undefined>(undefined);
  private readonly view = signal<{ y: number; m: number } | null>(null);
  private selected = () => parseDate(this.value() ?? this.local());
  private shown = () => this.view() ?? (() => { const d = this.selected() ?? new Date(); return { y: d.getFullYear(), m: d.getMonth() }; })();
  protected title = () => new Date(this.shown().y, this.shown().m, 1).toLocaleString(undefined, { month: 'long', year: 'numeric' });
  protected cells = (): (number | null)[] => {
    const { y, m } = this.shown();
    const first = new Date(y, m, 1).getDay();
    const days = new Date(y, m + 1, 0).getDate();
    return [...Array<null>(first).fill(null), ...Array.from({ length: days }, (_, i) => i + 1)];
  };

  protected stepMonth(delta: number): void {
    const { y, m } = this.shown();
    const d = new Date(y, m + delta, 1);
    this.view.set({ y: d.getFullYear(), m: d.getMonth() });
  }

  protected pick(day: number): void {
    const { y, m } = this.shown();
    const v = isoDate(y, m, day);
    this.local.set(v);
    this.wfValueChange.emit(v);
  }

  protected isSelected(day: number): boolean {
    const s = this.selected();
    const { y, m } = this.shown();
    return !!s && s.getFullYear() === y && s.getMonth() === m && s.getDate() === day;
  }

  protected isToday(day: number): boolean {
    const t = new Date();
    const { y, m } = this.shown();
    return t.getFullYear() === y && t.getMonth() === m && t.getDate() === day;
  }
}

/** Always-open single-selection list of strings (ListBox). The value is the selected text; empty when nothing is selected. */
@Component({
  selector: 'wf-list-box',
  standalone: true,
  host: HOST,
  template: `<select [attr.id]="controlId()" [attr.title]="tooltip()" [disabled]="!!disabled()" [attr.size]="size()" [class]="cls('wf-listbox', className())" (change)="wfSelectionChange.emit($any($event.target).value)">
      @for (item of items(); track $index) { <option [value]="item" [selected]="item === value()">{{ item }}</option> }
    </select>`,
})
export class WfListBox {
  items = input<string[]>([]);
  value = input<string | undefined>();
  disabled = input<boolean | undefined>(false);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
  tooltip = input<string | undefined>();
  wfSelectionChange = output<string>();
  protected cls = cls;
  protected size = computed(() => Math.max(4, Math.min(this.items().length, 12)));
}
