# 8. Glossary and cheat sheet

## 8.1 Glossary
| Term | Meaning |
|---|---|
| **IR** | Intermediate representation: framework-neutral JSON describing each form and user control (control tree, members, event wiring, navigation, layout facts). Written once by the source engine; every target reads only the IR |
| **Source engine** | Reads a legacy technology and writes the IR (today: WinForms) |
| **Target engine** | Reads the IR and generates an app (today: React, Angular) |
| **Unit** | One form or user control |
| **Entry form** | The form that becomes the app's `/` route; by default the startup form |
| **Depth** | How many `Show` / `ShowDialog` hops from the entry form are generated |
| **Archetype** | What a unit is for: grid, tree, menu, toolbar, list or form |
| **Catalog / `/modules`** | A generated page listing the units no code opens (loaded dynamically in the original) and opening them in a content area |
| **UI library** | `@wfm/ui-react`, `@wfm/ui-angular`, `@wfm/ui-core` and the contract `ui-spec`: the `Wf*` components generated pages use |
| **`ui-spec`** | The framework-neutral control contract (JSON) the generators read |
| **Code-behind** | The generated file with a page's model, handlers and translated methods (`*.code.tsx` / `*.page.code.ts`); the markup file is the "Designer" |
| **`vm` / `f`** | In generated code: control properties (`vm.txtSearch.text`) and form fields |
| **Wiring table** | The exported list of `control / event / handler` of a form, for tracing back to the original |
| **Compile gate** | The `tsc` / `ngc` loop that demotes methods that do not compile back to commented C# |
| **Complete / partial / not translated** | Translation status of a method: all statements, some statements, none |
| **Fallback** | The closest library component used for a control that has none of its own |
| **Contract** | The public API of a backend client DLL, read from metadata, used to generate typed mock services |
| **Support classes** | The application's own non-UI classes, emitted as `src/support` |
| **App shell** | A visual-only WinForms-style host window over all units (React) |
| **Viewer** | A visual-only page per unit with a sidebar (React) |
| **Rules file** | Data (`rules/*.json`) extended per project without rebuilding |
| **Migrator UI** | The local web application that runs the engine from a form |

## 8.2 Command cheat sheet
```bash
migrator engines
migrator ir      --source <src> --out <out>
migrator migrate --source <src> --out <out> --translate rules --compile-gate --depth all [--target angular-app]
migrator migrate --source <src> --out <out> --units all --translate rules --compile-gate      # + /modules catalog
migrator migrate --source <src> --out <out> --appshell                                       # visual-only shell (React)
migrator migrate --source <src> --out <out> --preview                                        # visual-only viewer (React)
migrator contract --assembly <dll> --out <dir>                                               # then: migrate --contract <contract.json>
migrator backend  --source <dir> [--source <dir>] [--wsdl <dir>] --out <dir>
pwsh tools/migrator-ui/start.ps1                                                             # the Migrator UI
```

## 8.3 App cheat sheet
| Do | React | Angular |
|---|---|---|
| Install | `npm install` | `npm install` |
| Run | `npm run dev` → http://localhost:5173 | `npm start` → http://localhost:4200 |
| Build | `npm run build` | `npm run build` |
| Check | `npm run lint` | `npm run lint`, `npm run check` (`ngc`) |

| Route | Shows |
|---|---|
| `/` | The entry form |
| `/pages` | List of all generated pages |
| `/<page-name>` | One page on its own, with mock data |
| `/modules` | The catalog of units no code opens (with `--catalog` / `--units all`) |

## 8.4 Migrator UI cheat sheet
| Do | Where |
|---|---|
| Start | `pwsh tools/migrator-ui/start.ps1` → http://127.0.0.1:5280 |
| Choose input / output | *Source and output* → Browse… |
| Choose what to generate | *What to generate*: IR only / Working app / App shell / Component viewer |
| See the equivalent command | the grey box above **Run** |
| Run / cancel | **Run**, **Cancel** |
| Read output | tabs **Log**, **Report**, **Runs** |
| Run the generated app | tab **Run the app**: **1. Install**, **2. Start**, **Open in browser**, **Stop** |

## 8.5 Where results go
`<out>/ir` · `<out>/react-app` or `<out>/angular-app` (with `MIGRATION_REPORT.md`) · `<out>/react-appshell` · `<out>/react-viewer`. Always outside the repository, never committed.
