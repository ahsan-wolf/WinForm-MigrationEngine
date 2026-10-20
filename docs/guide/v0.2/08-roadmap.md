# 8. Roadmap

Sizes are rough: lines of engine and library code, in the files named. Today's reference: `MethodTranslator` 1,485 lines, `PageGenerator` 1,381, `PageTranslator` 496. The structure of the engine does not change: every slice adds model entries, translator rules and library components behind the existing contracts.

## 8.1 Next slices (target 0.3.0)
| # | Slice | Counts in the two pilots (code-behind) | Touches | Est. lines | Size / risk |
|---|---|---|---|---|---|
| 1 | **Event wiring from code-behind** (decision D1) — **done** on the 0.3.0 line | ~400 `+=` outside designers (200 `Click`, 19 `SelectedIndexChanged`, 13 `KeyDown`, 13 `CheckedChanged`, 11 `FormClosing`) | Source `WinFormsMemberExtractor`, `WinFormsControlTreeExtractor`; target `MethodTranslator.Assign` | ~200 | S–M, low (lambdas are the risky part) |
| 2 | **`dgv.Rows` model**: `Rows.Add`, `Rows.Count`, `Rows[i].Cells[…].Value`, `SelectedRows`, `CurrentRow`, `Rows.Clear`, `RemoveAt` — **done** on the 0.3.0 line (column indexes are resolved through the grid model, so run-time `Columns.Add` works) | `SelectedRows` 115, `Cells[].Value` 99, `Rows.Count` 70, `Rows.Add` 46 | `MethodTranslator`, `PageGenerator`, `TranslationModels`; grid gets controlled selected rows | ~310 | M–L, medium (column index → field mapping, index constants such as `mGridColumnIndex_*` resolved from field initialisers) |
| 3 | **`panel.Controls` dynamic host**: `Controls.Add(new UserControl(args))`, `Clear`, `foreach`, `[i]`, `Count` | `Controls.Add` 272, `foreach` 45, `[i]` 22, `Clear` 14 | `MethodTranslator`, `PageGenerator`, new `WfDynamicHost` | ~290 | M–L, medium (calls **on** created controls need a handle API and stay TODO) |
| 4 | **Tab pages**: `TabPages.Add/Remove/Insert/Clear/Count/[i]`, `SelectedTab` ↔ index, `TabCount`, page `Text/Enabled/Visible`, `Selecting/Selected/Deselected`, page icons, controls inside pages | `SelectedIndex` 531 (all kinds of control), `TabPages` few | `PageGenerator`, `MethodTranslator`, `WfTabs`/`WfTabPage`, `ui-spec` | ~260 | M, low–medium |
| 5 | **Image resources** (decision D2) | `ImageKey` 153, `Images.Add` 56, `ImageList` 70 | Source resx reader, target `PageGenerator`/`AppWriter`/`IconConverter`, CLI `--images` | ~275 | M, medium (binary `ImageStream` stays a placeholder) |
| 6 | **`Path.*` helper** (D3) — **done** on the 0.3.0 line | Combine 40, GetFileName 37, WithoutExtension 20, GetDirectoryName 17, GetExtension 9 | `MethodTranslator` static-call map, generated `lib/csharp.ts` | ~110 | S, low |
| 7 | **Routes** (D4) | — | `AppWriter` templates, generated `lib/router.ts`, shell links | ~140 | S–M, low |

Total about **1,350 lines** in about 14 existing files plus four small new ones; roughly **6–8 focused working days**, as five small branches: (1 + 6), 2, (3 + 4), (5 + 7), each verified (build, lint, jsdom suites, both pilots, a visible browser run) before the next.

Expected effect in pilot A: the reasons "`dgvFile.Rows`", "`pnlCustomField.Controls`" and "event subscription in code" (9 + 16 + 16 statements today) shrink, and the handlers behind Select All, Add Files and Clear Metadata start to act.

## 8.2 To-do list (not scheduled)
- **Backend gateway:** a .NET 10 REST gateway generated from the backend analysis, in front of the running ASMX services (variant A: services stay, library facades are ported only where needed), plus a TypeScript client replacing the mock service. **Open questions:** keep the services running (A) or port them (B); the service URLs; how the third-party SOAP endpoints authenticate (Windows, basic, token).
- Angular library and target (needs a shared generation core first); HTML + jQuery only if the plain-HTML library is not enough.
- Form icon as favicon; `CellFormatting` support; LLM tier for the residue the rules cannot translate; third partial-class files; multi-level workflow steps.
