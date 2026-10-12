import type { IrSource } from './ir';

/** IrSource over a map of relative path -> File (from a folder picker or a dropped folder). */
export function fileSource(files: Map<string, File>): IrSource {
  return {
    paths: () => [...files.keys()],
    readText: async path => files.get(path)?.text(),
  };
}

/** Files from <input webkitdirectory>: paths come from webkitRelativePath. */
export function fromFileList(list: FileList): Map<string, File> {
  const map = new Map<string, File>();
  for (const f of Array.from(list)) map.set((f.webkitRelativePath || f.name).replace(/\\/g, '/'), f);
  return map;
}

async function walk(entry: FileSystemEntry, out: Map<string, File>): Promise<void> {
  if (entry.isFile) {
    const file = await new Promise<File>((res, rej) => (entry as FileSystemFileEntry).file(res, rej));
    out.set(entry.fullPath.replace(/^\//, ''), file);
  } else if (entry.isDirectory) {
    const reader = (entry as FileSystemDirectoryEntry).createReader();
    for (;;) {
      const batch = await new Promise<FileSystemEntry[]>((res, rej) => reader.readEntries(res, rej));
      if (batch.length === 0) break;
      for (const e of batch) await walk(e, out);
    }
  }
}

/** Files from a drag-and-drop of a folder (or of project.ir.json plus its siblings). */
export async function fromDrop(dt: DataTransfer): Promise<Map<string, File>> {
  const out = new Map<string, File>();
  const entries = Array.from(dt.items).map(i => i.webkitGetAsEntry()).filter((e): e is FileSystemEntry => e !== null);
  for (const e of entries) await walk(e, out);
  return out;
}
