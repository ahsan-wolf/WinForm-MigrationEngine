type Slot = { literal: string } | { accepts: (ch: string) => boolean; optional: boolean };

const letter = (ch: string) => /\p{L}/u.test(ch);
const digit = (ch: string) => /[0-9]/.test(ch);

/** WinForms mask characters -> slots. `0` digit, `9` digit or space (optional), `#` digit/sign (optional), `L`/`?` letter, `A`/`a` letter or digit, `&`/`C` any; `\` escapes; `< > |` are ignored; everything else is a literal. */
function parseMask(mask: string): Slot[] {
  const slots: Slot[] = [];
  for (let i = 0; i < mask.length; i++) {
    const m = mask[i];
    switch (m) {
      case '0': slots.push({ accepts: digit, optional: false }); break;
      case '9': slots.push({ accepts: ch => digit(ch) || ch === ' ', optional: true }); break;
      case '#': slots.push({ accepts: ch => digit(ch) || ch === '+' || ch === '-' || ch === ' ', optional: true }); break;
      case 'L': slots.push({ accepts: letter, optional: false }); break;
      case '?': slots.push({ accepts: letter, optional: true }); break;
      case 'A': slots.push({ accepts: ch => letter(ch) || digit(ch), optional: false }); break;
      case 'a': slots.push({ accepts: ch => letter(ch) || digit(ch), optional: true }); break;
      case '&': slots.push({ accepts: () => true, optional: false }); break;
      case 'C': slots.push({ accepts: () => true, optional: true }); break;
      case '<': case '>': case '|': break;
      case '\\': if (i + 1 < mask.length) slots.push({ literal: mask[++i] }); break;
      default: slots.push({ literal: m });
    }
  }
  return slots;
}

/** Formats typed text through a mask: literals are inserted as the user types, characters that do not fit a slot are skipped, trailing literals are not kept (so Backspace works). */
export function applyMask(mask: string | undefined, text: string): string {
  if (!mask) return text;
  const slots = parseMask(mask);
  const chars = [...text];
  let next = 0;
  let out = '';
  let keep = 0;
  for (const slot of slots) {
    if ('literal' in slot) {
      out += slot.literal;
      if (chars[next] === slot.literal) next++;
      continue;
    }
    while (next < chars.length && !slot.accepts(chars[next])) next++;
    if (next >= chars.length) break;
    out += chars[next++];
    keep = out.length;
  }
  return out.slice(0, keep);
}
