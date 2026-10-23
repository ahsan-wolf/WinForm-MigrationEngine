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
- **Outcome (0.3.0 line):** the `ImageStream` **is** decoded, in managed code: a BinaryFormatter `ImageListStreamer` is the signature `MSFt` followed by run-length pairs; expanded it is the native image-list stream (28-byte header, one strip DIB of all images, a 1-bit mask DIB for lists without alpha). `ImageListDecoder` (source engine) cuts the strip into one 32-bit BMP per image and the designer's `Images.SetKeyName` calls give the keys; both travel in the IR as `Images.Data` / `Images.Keys` / `Images.Size`. The target converts each image to `public/images/<unit>/<imagelist>/<key>.png` and keeps `keys` / `urls` in the page model for code (`Images.Add`, `Count`, `ContainsKey`, `IndexOfKey`, `RemoveAt`, `Clear`, `il.Images[k]`) and for tree and list views (`imageList` prop; nodes and items carry `imageKey` / `imageIndex`). Tab pages, toolbar and menu items resolve `ImageKey` / `ImageIndex` to the file at generation time. `Properties.Resources.X` resolves through `resources.ir.json` (the images of the project's `Resources.resx` that the code references, embedded or `ResXFileRef`, written as `public/images/resources/<name>.png`), and `--images <folder>` supplies files named by key for names or image-list entries that could not be extracted. An image argument that still cannot be resolved is added as a placeholder with a `// TODO`.

## D3. `Path.*` helpers — implemented (0.3.0 line)
- **Question:** how to translate `Path.Combine` (40 uses), `GetFileName` (37), `GetFileNameWithoutExtension` (20), `GetDirectoryName` (17), `GetExtension` (9).
- **Options:** (a) `path-browserify` npm package: POSIX only, breaks `C:\folder\file` paths; (b) the `URL` API: only valid for URLs; (c) a small in-repo helper.
- **Choice:** (c), `pathCombine`, `getFileName`, … in the generated `src/lib/csharp.ts` beside `formatString`, accepting both separators; strings that are web URLs use `new URL()`. No dependency. Server-only file access (`File.*`, `Directory.*`) stays a TODO for the backend.

## D4. Routes — implemented (0.3.0 line)
- **Question:** pages are reachable only through `?page=<Name>` (dev launcher) and `#/<Name>` (app shell).
- **Options:** (a) hash routes; (b) History API with `URL` / `URLSearchParams`; (c) `react-router` (dependency); (d) `URLPattern` (not baseline in all browsers yet).
- **Choice:** (b): a small generated `src/lib/router.ts` (`currentRoute`, `routeUrl`, `navigate`, `routeFor`, `usePath`) and one standard path per page: `/` is the entry form, `/<page-name>` opens any page on its own (kebab-case of the component name without `Page`: `SearchResultsPage` is `/search-results`), `/pages` lists them all. Old `?page=<Name>` links are rewritten to routes. Dialogs stay popups.
- **Relative paths:** `index.html` carries `<base href="%BASE_URL%" />` and Vite uses `base: './'`, so icons, images and links are relative (`icons/…`, `images/…`) and the app runs at the site root or under a sub-path. The base is read once at start-up; routes are one segment. A production host must serve `index.html` for unknown paths (Vite dev and preview already do). The viewer and the app shell are development tools and keep their `#/<unit>` hash routes.
- **Why:** web standards only, no dependency, deep links and back/forward work.

## D5. Popups are native `<dialog>` elements, closed with `dialog.close()` (done in 0.2)
- A dialog page takes no `onClose` prop. `dialog.show(() => <XPage />)` opens it, `dialog.close(result?)` closes the topmost popup and resolves the opener's promise; on a standalone page `dialog.close()` calls the provider's `onRootClose`. Every popup has a title-bar ✕ and closes on Escape.

## D6. Backend gateway is deferred
- Kept on the to-do list ([08-roadmap.md](08-roadmap.md)); the read-only analyzer from 0.1 stays the only backend piece.

## D8. Archetype controls and a catalog for units code never opens (0.5)
- **Question:** units loaded at run time (a menu tree, a permission check, a database row) are never reached by `new X()` / `ShowDialog()`; they were generated only with `--units all`, hosted nowhere, and empty when their grid, tree or menu is built in code.
- **Options:** (a) new library components per archetype; (b) classify and compose from the existing components; (c) read the loading logic and translate it.
- **Choice:** (b). The designer decides first (what the unit contains), the name second (data file `rules/control-archetypes.json`, extendable per project); missing columns, nodes and items come from literals in the code or the row type of the data source, else marked generic ones. A generated `/modules` page lists the units no code opens by archetype and opens the selected one, standing in for the loading logic, which stays untranslated.
- **Why:** no library change (the contract stays 0.3.0 / 0.4.0), the same result for both targets from the shared core, no application names in code or rules, every guess marked and reported.

## D7. Versions
- 0.3.0 for both engines, `ui-spec` and `@wfm/ui-react` (additive: `WfDynamicHost`, image list helpers, grid cell events, tab page props; 0.2.0 before). The IR schema stays 0.1.0 (only new dotted pseudo-attributes). The slices in [08-roadmap.md](08-roadmap.md) ship as 0.3.0. The archetype slice (D8) ships as 0.5.0 for the React and Angular targets only; nothing in the libraries or the IR changed.
