import type { ReactNode } from 'react';
import { parseMnemonic } from '@wfm/ui-core';

export { parseMnemonic };

/** Renders the label with the mnemonic character underlined. */
export function MnemonicText({ text }: { text: string | undefined }): ReactNode {
  const { label, index } = parseMnemonic(text);
  if (index < 0) return label;
  return <>{label.slice(0, index)}<u>{label[index]}</u>{label.slice(index + 1)}</>;
}
