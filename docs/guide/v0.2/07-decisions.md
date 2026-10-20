# 7. Decision records

Each record: the question, the options considered, the choice and why. Decided with the owner of the engine on 2026-10-20 unless noted.

## D1. Events subscribed in code (`ctl.Click += handler`) — implemented (0.3.0 line)
- **Question:** the designer wires most events, but code-behind also subscribes events (about 400 statements in the two pilots outside designers, 200 of them `Click`). Today they stay commented TODOs.
- **Choice:** migrate them. The source engine reads these statements from the code-behind (constructor, `Load`, helper methods) and records them as event wiring on the control, exactly like designer wiring (`Attributes["OnClick"] = "handler"`). Workflow, navigation, generated props and translation then work unchanged. The statement stays visible in the translated code as a comment (`// wired: btn.Click += btn_Click → onClick`). A lambda subscription becomes a synthetic handler member. Controls created at run time keep a TODO until the dynamic host exists.
- **Why:** no new generator concept; the wiring is part of the IR, so the IR remains the single source of truth.

## D2. `ImageList` is not a control
- **Question:** `ImageList` holds the images that `ImageKey` / `ImageIndex` refer to (153 `ImageKey`, 56 `Images.Add` in the pilots). Its designer form is a binary, Windows-only stream.
- **Choice:** map it to an **image resource folder** of web files: `public/images/<imagelist-or-resources>/<key>.png`. Sources, in order: the project's `Resources.resx` and `Properties.Resources.X` references; `Images.Add("key", …)` calls; an optional `--images <folder>` of files named by key. The binary `ImageStream` is not decoded: those keys show the placeholder icon and are listed in the report. `ImageKey` / `ImageIndex` on tree nodes, list items, tabs, toolbar and menu items become `icon` paths. ICO and BMP are converted to PNG as for control images.
- **Why:** web standards (a path, an `<img>`), nothing Windows-specific, and a folder the project can fill by hand where extraction is impossible.

## D3. `Path.*` helpers — implemented (0.3.0 line)
- **Question:** how to translate `Path.Combine` (40 uses), `GetFileName` (37), `GetFileNameWithoutExtension` (20), `GetDirectoryName` (17), `GetExtension` (9).
- **Options:** (a) `path-browserify` npm package: POSIX only, breaks `C:\folder\file` paths; (b) the `URL` API: only valid for URLs; (c) a small in-repo helper.
- **Choice:** (c), `pathCombine`, `getFileName`, … in the generated `src/lib/csharp.ts` beside `formatString`, accepting both separators; strings that are web URLs use `new URL()`. No dependency. Server-only file access (`File.*`, `Directory.*`) stays a TODO for the backend.

## D4. Routes
- **Question:** pages are reachable only through `?page=<Name>` (dev launcher) and `#/<Name>` (app shell).
- **Options:** (a) hash routes; (b) History API with `URL` / `URLSearchParams`; (c) `react-router` (dependency); (d) `URLPattern` (not baseline in all browsers yet).
- **Choice:** (b): a ~60-line generated `src/lib/router.ts` (`navigate`, `usePath`) and one route per page (`/Customers`, `/OrderDetails`). `?page=` stays as an alias. Dialogs stay popups, with an optional `?dialog=<Name>` deep link.
- **Why:** web standards only, no dependency, deep links and back/forward work.

## D5. Popups are native `<dialog>` elements, closed with `dialog.close()` (done in 0.2)
- A dialog page takes no `onClose` prop. `dialog.show(() => <XPage />)` opens it, `dialog.close(result?)` closes the topmost popup and resolves the opener's promise; on a standalone page `dialog.close()` calls the provider's `onRootClose`. Every popup has a title-bar ✕ and closes on Escape.

## D6. Backend gateway is deferred
- Kept on the to-do list ([08-roadmap.md](08-roadmap.md)); the read-only analyzer from 0.1 stays the only backend piece.

## D7. Versions
- 0.2.0 for both engines, `ui-spec` and `@wfm/ui-react` (additive: new components, dialog service additions). The IR schema stays 0.1.0 (only new dotted pseudo-attributes). The slices in [08-roadmap.md](08-roadmap.md) ship as 0.3.0.
