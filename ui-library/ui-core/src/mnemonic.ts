export interface Mnemonic { label: string; accessKey?: string; index: number }

/** WinForms mnemonic: "E&xit" -> label "Exit", accessKey "x", underline index 1. "&&" is a literal ampersand. */
export function parseMnemonic(text: string | undefined): Mnemonic {
  const source = text ?? '';
  let label = '';
  let accessKey: string | undefined;
  let index = -1;
  for (let i = 0; i < source.length; i++) {
    const c = source[i];
    if (c === '&' && source[i + 1] === '&') { label += '&'; i++; continue; }
    if (c === '&' && i + 1 < source.length && accessKey === undefined) { accessKey = source[i + 1].toLowerCase(); index = label.length; continue; }
    label += c;
  }
  return { label, accessKey, index };
}
