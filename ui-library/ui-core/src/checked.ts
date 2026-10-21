/** `SetItemChecked(i, checked)` over the page model's `checkedIndices` array. */
export function wfSetItemChecked(indices: number[], index: number, checked: boolean): void {
  const at = indices.indexOf(index);
  if (checked && at < 0) indices.push(index);
  else if (!checked && at >= 0) indices.splice(at, 1);
}
