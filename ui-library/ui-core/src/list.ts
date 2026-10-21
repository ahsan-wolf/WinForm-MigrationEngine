/** One row. `text` is the first column, `subItems[k]` is column k + 1 (WinForms ListViewItem / SubItems). */
export interface WfListItem {
  text: string;
  subItems: string[];
  checked: boolean;
  selected: boolean;
  tag?: unknown;
  /** ListViewItem.ImageKey / ImageIndex: the image in the list's `imageList` shown before the first column's text. */
  imageKey?: string;
  imageIndex?: number;
  name?: string;
}

export interface WfListColumn { header: string; width?: number }

/** `new ListViewItem(text)` */
export function wfListItem(text = '', subItems: string[] = [], imageKey?: string): WfListItem {
  return { text, subItems, checked: false, selected: false, imageKey };
}

/** `new ListViewItem(string[] values)`: the first value is the item text, the rest are sub items. */
export function wfListItemFromArray(values: string[]): WfListItem {
  return wfListItem(values[0] ?? '', values.slice(1));
}
