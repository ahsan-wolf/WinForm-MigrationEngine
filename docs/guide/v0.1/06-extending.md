# 6. Extending

Contracts, IR keys and examples are in `../../ENGINE_AUTHORING.md`; this is the map.

| I want to… | Do this | Touches |
|---|---|---|
| Support another control | Add it to `ui-spec/controls.json` (WinForms types, props with `from`/`transform`, events), implement it in every `ui-*` package, add a row to `../../UI_CONTROL_MAPPING.md` and `SKILLS.md` Skill 0, drop its `fallbacks.json` entry | ui-library + data; no generator code for simple controls |
| Change a stand-in | Edit `ui-spec/fallbacks.json` | data |
| Bypass another Windows API | Add the name to `rules/windows-bypass.json` (or the project's copy) | data |
| Mark more code server-only | Extend `rules/server-bound.json` | data |
| Detect more in the backend survey | Extend `rules/backend-patterns.json`, pass `--rules` | data |
| Translate another C# pattern | Add a rule in `Migrator.Target.React/Translation/` (see authoring guide); check `tsc` and lint stay at 0 | target |
| Read another legacy technology (WPF, VB6) | New `Migrator.Source.X` implementing `ISourceEngine` (+ optional capability interfaces), register in `engines.json`, add a Skill 18 section | new project only |
| Generate another target (Angular, HTML+jQuery, Vue, REST API) | New `Migrator.Target.X` (`IProjectTargetEngine`) + `ui-library/ui-x` implementing `ui-spec` with identical names/props/events, register, Skill 18 section | new projects only |
| Add a plugin engine | Drop the assembly in `plugins/`, **list it** in `engines.json` (unlisted DLLs are never executed) | config |

Rules that always apply: engines hold no global state; one engine's failure on one unit never aborts a run; schema changes are additive and bump `IrSchema.Version`; no test, demo or Storybook projects in this repo.
