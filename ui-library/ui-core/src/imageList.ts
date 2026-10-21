/**
 * The counterpart of a WinForms ImageList: not a control but a list of image resources (web paths, URLs or data URIs) reachable by position or by key.
 * It is plain data, so pages keep it in their model and code adds and removes entries in place (`Images.Add`, `Images.Clear`, `Images.RemoveByKey`).
 * Controls that show images from it (`WfTreeView`, `WfListView`) take it as `imageList` and resolve a node's or item's `imageKey` / `imageIndex`.
 */
export interface WfImageList {
  /** Key of each image ('' when it has none), in the same order as `urls`. */
  keys: string[];
  /** Where each image is served from. */
  urls: string[];
}

/** An image list over entries given as `[key, url]` pairs, or plain urls. */
export function wfImageList(entries: ReadonlyArray<string | readonly [string, string]> = []): WfImageList {
  const list: WfImageList = { keys: [], urls: [] };
  for (const entry of entries) {
    if (typeof entry === 'string') wfImageListAdd(list, '', entry);
    else wfImageListAdd(list, entry[0], entry[1]);
  }
  return list;
}

/** The url of the image at a position or with a key; undefined when there is none (an unknown key or an index out of range). */
export function wfImageListUrl(list: WfImageList | undefined, keyOrIndex: string | number | undefined | null): string | undefined {
  if (!list || keyOrIndex === undefined || keyOrIndex === null || keyOrIndex === '') return undefined;
  const at = typeof keyOrIndex === 'number' ? keyOrIndex : list.keys.indexOf(keyOrIndex);
  return at >= 0 ? list.urls[at] : undefined;
}

/** `Images.Add(key, image)` / `Images.Add(image)` (empty key): returns the new image's index. */
export function wfImageListAdd(list: WfImageList, key: string, url: string): number {
  list.keys.push(key);
  list.urls.push(url);
  return list.urls.length - 1;
}

/** `Images.RemoveAt(i)` */
export function wfImageListRemoveAt(list: WfImageList, index: number): void {
  if (index < 0 || index >= list.urls.length) return;
  list.keys.splice(index, 1);
  list.urls.splice(index, 1);
}

/** `Images.RemoveByKey(key)` */
export function wfImageListRemoveByKey(list: WfImageList, key: string): void {
  wfImageListRemoveAt(list, list.keys.indexOf(key));
}

/** `Images.Clear()` */
export function wfImageListClear(list: WfImageList): void {
  list.keys.splice(0);
  list.urls.splice(0);
}

/** A neutral square for an image that is not available (WinForms code adds images the engine cannot resolve, such as project resources or files). */
export function wfPlaceholderImage(): string {
  return "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='16' height='16'%3E%3Crect x='2' y='2' width='12' height='12' rx='2' fill='%23d0d0d0' stroke='%23808080'/%3E%3C/svg%3E";
}
