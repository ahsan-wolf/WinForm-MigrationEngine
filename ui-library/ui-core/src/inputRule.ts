/** What a text box accepts besides any text: letters and spaces only, or a number (digits, optionally one minus sign kept in front and one decimal point). */
export type WfInputRule = 'letters' | 'number';

export interface WfInputRuleOptions {
  /** `number` only: a decimal point is accepted (once). */
  allowFloat?: boolean;
  /** `number` only: a leading minus sign is accepted (once, at the start). */
  allowNegative?: boolean;
}

/**
 * The text a box keeps after typing or pasting: every character the rule does not accept is dropped.
 * `letters`: A-Z, a-z and space. `number`: digits, a leading `-` when negative values are allowed, one `.` when decimals are allowed.
 * Applied to the whole new value, so paste and drop follow the same rule as key presses.
 */
export function wfFilterInput(rule: WfInputRule | undefined, value: string, options: WfInputRuleOptions = {}): string {
  if (rule === 'letters') return value.replace(/[^A-Za-z ]/g, '');
  if (rule !== 'number') return value;
  let out = '';
  let dot = false;
  for (const ch of value) {
    if (ch >= '0' && ch <= '9') out += ch;
    else if (ch === '.' && options.allowFloat && !dot) { out += ch; dot = true; }
  }
  // A minus sign typed anywhere becomes the leading sign (once), as the original box prefixes it.
  return options.allowNegative && value.includes('-') ? '-' + out : out;
}
