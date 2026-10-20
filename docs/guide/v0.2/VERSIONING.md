# Versioning

## What is versioned, and how
| Thing | Version | Rule |
|---|---|---|
| This guide and the engine release | `v0.2` (folder `docs/guide/v0.2/`; `v0.1` stays as the record of the first release) | `0.x` is unstable: a new minor folder per release that changes behaviour, commands or outputs |
| Engines (`WinFormsSourceEngine`, `ReactTargetEngine`) | `0.2.0` (`engines` command prints it) | Bump the minor when behaviour, commands or outputs change; the registry checks only the IR schema |
| IR schema (`IrSchema.Version`) | `0.1.0` | Additive changes only; while major is `0` an engine's minor must equal the host's; otherwise engine minor ≤ host minor. The registry refuses a mismatched engine |
| `ui-spec` | `0.2.0` (`package.json`, `controls.json`, `fallbacks.json`) | Additive; a breaking change needs a major bump and a migration note in `docs/` |
| `@wfm/ui-react` (and later `ui-angular`, `ui-html`) | `0.2.0`, semver | Identical component names, props and events across frameworks; engines declare the compatible `ui-spec` range |
| Rule files (`rules/*.json`, `fallbacks.json`) | by git history; `server-bound.json` carries `"version": 1` | Extend with data; bump the field only when the file's shape changes |
| Generated output | not versioned by the engine | Re-running on unchanged input and tool version gives the same output; compare the IR first, then the translator version |

## Cutting a new guide version
1. Branch `docs/guide-v0.X`.
2. Copy `docs/guide/v0.<previous>/` to `docs/guide/v0.X/`; leave the old folder untouched (it documents that release).
3. Update the new copy: version line in `README.md`, the "what changed" section, `CHANGELOG.md` (append the new slices), numbers in `02-ui-generation.md` and `03-backend.md`.
4. Point `docs/guide/LATEST.md` at the new folder.
5. If `IrSchema.Version`, `ui-spec` or a package version changed, update this table and the migration note.
6. Run the checks in `04-workflow.md` §4.6 (including the grep for application names) before asking for the push.
