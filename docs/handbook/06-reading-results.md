# 6. Reading the results

## 6.1 What is in the output folder
```
<out>/
├─ ir/                         the IR (identical for every target)
│  ├─ project.ir.json          index, Failures, Shared.Unsupported
│  ├─ support.ir.json          the application's own non-UI classes (when any)
│  ├─ resources.ir.json        referenced Resources.resx images (when any)
│  └─ <Unit>/                  five files per form or user control (complete is the source of truth)
├─ react-app/                  or angular-app/   the working app
├─ react-appshell/             visual-only app shell (mode App shell)
├─ react-viewer/               visual-only component viewer (mode Component viewer)
└─ backend/, contracts/        only if you ran the backend and contract commands there
```

### React app (`react-app/`)
| Path | Content |
|---|---|
| `MIGRATION_REPORT.md` | The report ([6.2](#62-the-report)) |
| `src/pages/<Name>Page.tsx` / `<Name>Control.tsx` | **Markup only** (the "Designer"): `Wf*` components bound by name |
| `src/pages/<Name>….code.tsx` | The **code-behind**: hook `use<Name>Code()` with the model (`vm` for control properties, `f` for form fields), handlers, translated methods and the exported `wiring` table (`control`, `event`, `handler`, `method`, `source`) |
| `src/support/index.ts` | The application's own classes (helpers, data classes, enums), called as in the original |
| `src/services/<assembly>/` | Typed mock services from a contract (when `--contract` was given) |
| `src/lib/router.ts`, `csharp.ts` | Generated routing and runtime helpers (`string.Format`, `Path.*`, `TryParse`) |
| `src/Modules.tsx` | The `/modules` catalog (only with `--catalog` / `--units all` and units no code opens) |
| `src/legacy/*.cs.txt` | The **original code-behind** of every generated form, so nothing is lost |
| `public/icons`, `public/images` | Control images, image-list entries (one PNG per image) and `Properties.Resources` images; `favicon` from the entry form's icon |

### Angular app (`angular-app/`)
| Path | Content |
|---|---|
| `src/pages/<page>.page.html` | The template (the Designer) |
| `src/pages/<page>.page.ts` | The standalone component: creates the code-behind, exposes what the template uses |
| `src/pages/<page>.page.code.ts` | The code-behind factory with model, handlers, translated methods and `wiring` |
| `src/app/` | `app.config.ts`, `app.routes.ts`, `app.component.ts`, `pages.component.ts` (`/pages`), `modules.component.ts` (`/modules`) |
| `src/lib/csharp.ts`, `src/legacy/`, `public/` | As in React |

## 6.2 The report
`MIGRATION_REPORT.md` is the first thing to read. Sections, in order:

| Section | What it tells you | What to do with it |
|---|---|---|
| Header | Source technology, entry form, depth, units generated of units found, units not generated | Not generated units are listed with the reason (out of scope, or a generation failure; a failure never aborts the run) |
| **Generated pages** | Per unit: component, hop distance, controls, handlers, navigation wired, issues | Open units with many issues first |
| **Icons** | Images extracted, converted, placeholders | Placeholders mean an image could not be read: supply it with `--images` |
| **Archetypes** | How units were classified, what was inferred, how many are in the catalog ([6.4](#64-archetypes-and-the-modules-catalog)) | Check inferred columns, nodes, menu items against the original |
| **Translation (rules)** | Methods: *complete* (every statement translated), *partial* (some statements stayed as commented C#), *not translated* (the whole method stays commented; a typed stub keeps callers compiling); how many the compile gate demoted; **most common reasons** | The reasons list is the fastest way to see what blocks more automation |
| **Project classes** | The same for the application's own classes | Same |
| **Manual work by category** | Controls with no library component, navigation that needs a decision, grids whose columns are created in code, non-visual components, server-only code (SERVER-ONLY), contract notes, compile errors outside translated methods | Your to-do list |
| Known limitations of this generator | Standing limits | Read once |

Console summary after a run: units extracted, pages generated, methods complete / partial / not translated, demoted by the gate, **TS errors remaining** (should be 0).

## 6.3 Markers in generated code
Nothing is dropped silently. Search the generated app for these:

| Marker | Meaning | Action |
|---|---|---|
| `// TODO: not translated — <reason>` + commented C# | The rules could not translate this faithfully; the original is kept | Translate by hand or extend a rule |
| `// TODO: wire to real API — X not resolvable from IR` | A call or type the IR or contract does not describe | Provide a contract or wire it to the real service |
| `// MOCK — replace with real implementation` | Sample or fake data (grid rows, list rows, tree nodes, constructor-argument defaults) | Replace with real data |
| `// inferred — check against backend contract` | A type guessed from columns or formats | Confirm |
| `// BYPASSED — Windows-specific API (…)` | Registry, clipboard, `Process.Start`, directory services…: no browser equivalent; the original is kept next to it | Decide a web equivalent or drop |
| `// dropped (no browser equivalent: …)` | Appearance-only or UI-loop code (Cursor, `DoEvents`, Refresh) | Usually nothing |
| `/* FALLBACK: DateTimePicker */` | A control with no component of its own, rendered by the closest one | Check look and behaviour, or add a real component |
| `// wired in the markup (btn.Click): btn.Click += handler;` | A code-behind subscription turned into event wiring; the markup binds the handler | Nothing; the original is kept as this comment |
| `<WfUnsupported …/>` / `<wf-unsupported>` | A control that could not even get a fallback (rare) | Replace manually |
| `// LLM-translated — verify` | Reserved for the LLM tier (not generated yet) | |

SERVER-ONLY: members of a contract that look like secrets, and methods and classes that talk to a database, the file system or configuration, are left out of browser code and listed in the report as backend work.

## 6.4 Archetypes and the `/modules` catalog
Every generated unit is classified by **what its designer contains** first and by **name** second:

| Archetype | Designer content | Page built from |
|---|---|---|
| Grid | a `DataGridView` (with few inputs) | `WfDataGrid` |
| Tree | a `TreeView` | `WfTreeView` |
| Menu | a `MenuStrip` as the only content | `WfMenuBar` |
| Toolbar | a `ToolStrip` as the only content | `WfToolbar` |
| List | a `ListView` / `ListBox` | `WfListView` / `WfListBox` |
| Form | anything else (edit forms, filters, dialogs) | as before |

When the designer is empty or small, name patterns decide (`…Grid…`, `…Tree…`, `…Menu…`, `…ToolStrip…`, `…List…`). Where the designer is silent — a grid built in code, a menu whose items are added at run time — the engine infers columns, nodes, items or buttons from literals in the code (`Columns.Add("x", "Header")`, `new TreeNode("x")`, `new ToolStripMenuItem("x")`) or from the row type of the data source, and otherwise shows clearly marked generic placeholders. Everything inferred is listed in the report.

The **`/modules`** page (with `--catalog` or `--units all`) lists the units no code opens, grouped by archetype, and opens the selected one in a content area. It stands in for the original dynamic loading or permission logic, which is **not translated**. Units that show blank there have an empty designer and no code (typically base classes).

Details and limits: [../guide/v0.5/10-archetypes.md](../guide/v0.5/10-archetypes.md).

## 6.4a Third-party controls
A control from a vendor or a custom library (not `System.*`) is drawn by the closest library component, chosen by **name patterns** in `ui-library/ui-spec/fallbacks.json` (data; extend a copy per project). Typical results: numeric boxes become the text box with a number rule (`allowFloat` / `allowNegative` read from the designer), text-only boxes the text box with a letters rule, date / time / date-time editors the date picker with the matching kind, grids (also hierarchical and flex grids) the data grid, combos and look-ups the drop-down (the library also has a multi-column grid drop-down for hand-written use and for code that supplies rows), colour and font editors the colour and font pickers. Each one is listed in the report as a *fallback* with its pattern note; a control that matches no pattern shows as a label with its type name.

## 6.4b The API report
`<out>/dotnet-api/API_REPORT.md` (target `dotnet-api`): provider, classes ported, operations per class (`POST /api/<Class>/<Method>`), **members not ported** (they did not compile on .NET 10 and throw `NotSupportedException`), **SQL built from strings** (injection risk, one line per method), Windows-only calls, Access-style SQL, and the configuration keys the code reads (`AppSettings__<key>`, secret-looking ones marked SERVER-ONLY). In the web report the section *API calls* lists the classes the app calls (`--api`).

## 6.5 How to judge quality
| Look at | Healthy | Worry when |
|---|---|---|
| `0 TS error(s) remain`, build, lint | all clean | any error remains (the gate could not run, or an error sits outside translated methods) |
| Complete share of methods | grows as rules improve (about half on the two applications measured so far) | most methods *not translated*; read the reasons |
| `Failures` in `project.ir.json` | empty | units that could not be extracted |
| `Unsupported` | short | long lists of the same statement: an extractor gap |
| FALLBACK and `WfUnsupported` counts | few | many controls of one type: add a component |
| Placeholders for images | few | many: supply `--images` |
| Pages that render and navigate | all | blank pages: open `/<page-name>`, check the console |

Remember: *complete* means every statement was translated, not that it is correct. Review the logic with people who know the application.
