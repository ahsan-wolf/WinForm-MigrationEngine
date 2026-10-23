# 10. Archetype controls and the `/modules` catalog

Some user controls are never created by code the extractor can see: a menu tree, a permission check or a database row decides at run time which screen to load (`Activator.CreateInstance`, reflection, a factory). Before 0.5 such units were generated only with `--units all`, nothing hosted them, and a control whose designer is empty (its grid columns, tree nodes or menu items are built in code) rendered as an empty shell.

0.5 classifies every generated unit by **what it is for** (its archetype), builds the archetype's primary control from the designer and, where the designer is silent, from literals in the code, and writes a **catalog page** that lists and opens the units no code opens.

## 10.1 Archetypes
| Archetype | What the unit is | Primary control | Page built |
|---|---|---|---|
| Grid | A list of records | `DataGridView` | grid with the designer's columns (or inferred ones) and marked sample rows |
| Tree | A hierarchy | `TreeView` | tree with its nodes |
| Menu | A menu bar | `MenuStrip` | menu bar with its items |
| Toolbar | A row of tool buttons | `ToolStrip` | toolbar with its buttons |
| List | Items with columns | `ListView`, `ListBox` | list (a ListView nothing uses shows as a grid) |
| Form | Everything else: edit forms, filters, dialogs | none | as before |

No new library component: an archetype page is composed from the components that already exist (`WfDataGrid`, `WfTreeView`, `WfMenuBar`, `WfToolbar`, `WfListView`), and the code-behind of the unit is translated as for any page.

## 10.2 How a unit is classified
`ControlArchetypes` (in `Migrator.Target.Core`, IR only, no application knowledge) decides in this order:

1. **Designer first.** A unit whose visual content is mainly one data control is that archetype: a `DataGridView` with at most `inputThreshold` input controls (text boxes, combo boxes, check boxes, date pickers, ...) is a Grid; a `TreeView` without a grid, a Tree; a `ListView` or `ListBox`, a List; a `MenuStrip` or `ToolStrip` that is the **only** content apart from containers and non-visual components, a Menu or a Toolbar. More inputs than the threshold make it an edit form: a form that merely has a menu or a toolbar stays a Form.
2. **Name as hint.** Only when the designer cannot decide (it has no data control, at most `inputThreshold` inputs, and is empty or a user control), the name patterns of `rules/control-archetypes.json` apply. They are case-insensitive regular expressions, tried against the class name and against the name without its Hungarian prefix, first match wins: `...MenuStrip`/`...Menu` is a Menu, `...ToolStrip`/`...ToolBar` a Toolbar, `...Tree`/`...TreeView`/`...TreeViewer` a Tree, `...Grid`/`...GridView`/`...GridControl` a Grid, `...List`/`...ListView` a List.
3. **Both agree** is reported as `designer+name`; when they disagree the designer wins.

The result is computed at generation time; the IR files do not change.

## 10.3 What is built where the designer is silent
`ControlArchetypes.Synthesize` changes the unit's IR in memory before any page is generated. Everything it adds carries the pseudo-attributes `Archetype.Synthesized` or `Archetype.Inferred` and is listed in `MIGRATION_REPORT.md` (section "Archetypes").

| Archetype | Missing | Inferred from (first that gives names) | Else |
|---|---|---|---|
| Grid | the grid itself | a `DataGridView` added with `Dock = Fill` | |
| Grid | its columns | `Columns.Add("name", "Header")`, `DataPropertyName = "x"`, `Columns["x"]` / `Cells["x"]` in the code; or the public properties of the row type of a `DataSource = rows` (`List<Row>`, `BindingList<Row>`, `Row[]`, resolved in `support.ir.json`) | three generic columns, marked |
| Tree | nodes | `new TreeNode("text")`, `Nodes.Add("text")` (top level only; no children are invented) | the generic sample tree, marked `// MOCK` |
| Menu | the strip, its items | `new ToolStripMenuItem("text")`, `Items.Add("text")`, `DropDownItems.Add("text")` | `Menu 1`, `Menu 2`, `Menu 3`, marked |
| Toolbar | the strip, its buttons | `new ToolStripButton("text")`, `Items.Add("text")` | `Button 1` ... `Button 3`, marked |

Rules that keep it safe:
- A grid that **is** in the designer gets inferred columns only from its row type, and only when no code touches its `Columns` (code that adds columns at run time is already translated and would otherwise duplicate them). Generic columns exist only for a grid the pass itself added.
- Sample rows, nodes and items are data of the generated app (`// MOCK — replace with real implementation`), never a claim about the original.
- A unit classified as a Form is never touched.

## 10.4 The `/modules` catalog
With `--catalog` (and by default with `--units all`) the pipeline lists every generated unit that no `Show`/`ShowDialog`/`new` reaches from the entry form, with its archetype, and the target writes a page for it:

| Target | Files | Route |
|---|---|---|
| React | `src/Modules.tsx` (lazy page components, a left navigation grouped by archetype, a content area), linked from `/pages` | `/modules` |
| Angular | `src/app/modules.component.ts` (`NgComponentOutlet`, dynamic `import()` per unit), linked from `/pages` | `/modules` |

The page stands in for the original loading logic: selecting a unit opens it in the content area. The permission or loading logic itself (which unit, for which user) is **not translated**; it stays the original C# comment in the host's code-behind. When every unit is opened by code, no catalog page is written.

## 10.5 Options
| Option | Meaning |
|---|---|
| `--catalog` | Generate the units no code opens and write the `/modules` page (implied by `--units all`) |
| `--archetypes <file>` | A copy of `rules/control-archetypes.json` with project-specific name patterns (generic words only, kept with the project, outside this repository) |

`control-archetypes.json`: `inputThreshold` (default 6) and `names` (`{ "archetype": "grid|tree|menu|toolbar|list", "pattern": "<regex>" }`). Without the file the same defaults apply.

## 10.6 Verification (application-neutral)
- A synthetic sample with a host form that loads a module by name and four modules: a grid control with an empty designer and `Columns.Add` in code, a tree control with nodes added in code, a menu control with items in code, and a toolbar control with two designer buttons. Generated for both targets: the four modules are classified (`name`, `designer+name`, `name`, `designer+name`), listed under `/modules` by archetype, and open with their columns, nodes, menu items and buttons. `tsc`/`ngc` report 0 errors; `vite build` and `ng build` succeed.
- Pilot with 159 units (`--units all`): 117 forms, 31 grids, 7 trees, 4 lists; 158 catalog entries; every entry opened in the Angular and React apps in Edge with no console or network errors; 8 are blank because the control has an empty designer and no code (base classes). `tsc`/`ngc` 0 errors.
- Pilot with 30 units: 16 forms, 1 grid, 1 tree, 12 lists; the generated pages are unchanged (only the report gained the archetype section).
- Regression: the React output of all samples and both pilots is byte-identical except `MIGRATION_REPORT.md` and, with `--units all`, `Modules.tsx` and the route in `main.tsx`.

## 10.7 Known limits
- Classification is a heuristic; a control that mixes a grid, a tree and many inputs is a Form. Add a name pattern or fix the page by hand.
- Menu and toolbar items from code are literal strings only; items built from data (a database row, a resource) fall back to the marked placeholders.
- A grid column inferred from `Cells["x"]` is a field name, not a header (the header is the humanised field name).
- Nested menu structure is not inferred from code (a flat list of top-level items).
- The catalog opens a unit with no properties; a user control that needs constructor arguments shows its mock defaults (see [02-ui-generation.md](02-ui-generation.md)).
