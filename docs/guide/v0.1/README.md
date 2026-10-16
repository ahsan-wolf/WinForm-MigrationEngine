# WinForms Migration Engine — guide v0.1

Complete working guide for engine release **0.1** (IR schema `0.1.0`, `ui-spec` `0.1.0`, `@wfm/ui-react` `0.1.0`). Written for the person who runs, reviews or extends a migration.

| Part | Read it for |
|---|---|
| [01-overview.md](01-overview.md) | What the engine is, the modules, how data flows |
| [02-ui-generation.md](02-ui-generation.md) | Everything the UI side does (IR, controls, translation, viewer/app shell), with measured results and gaps |
| [03-backend.md](03-backend.md) | The backend track: what exists (analyzer), what is planned (.NET 10 REST API), and why |
| [04-workflow.md](04-workflow.md) | Step-by-step runbook from a new application to a checked app |
| [05-project-independence.md](05-project-independence.md) | How the engine stays free of any one application; onboarding a new one |
| [06-extending.md](06-extending.md) | Add a control, a rule, a target, a source technology |
| [CHANGELOG.md](CHANGELOG.md) | What was built, in order |
| [VERSIONING.md](VERSIONING.md) | Version policy and how to cut `v0.2` |

Detail documents that this guide summarises (not duplicates): `../../USER_GUIDE.md` (every option), `../../PROJECT_DOCUMENTATION.md` (as-built architecture), `../../ENGINE_AUTHORING.md` (contracts, IR keys), `../../UI_CONTROL_MAPPING.md` (control table), `../../PROJECT_PLAN.md` (roadmap), `../../../CLAUDE.md` (rules), `../../../SKILLS.md` (mappings).

## State in one paragraph
A WinForms project goes in; out come an IR folder, a React + TypeScript app that uses only `@wfm/ui-react`, a visual-only component viewer and a WinForms-style app shell, and a report of what still needs a person. Business code is translated by deterministic rules and checked by `tsc`; what does not compile is demoted statement by statement to commented C# with a TODO. Custom and third-party controls resolve to the nearest standard control or fall back to the closest library component; Windows-only API calls are bypassed with a marker. A read-only backend analyzer describes the legacy service layer; the REST API generator is the next track.

## What changed since the previous version
First versioned guide. Covers all work to date (see [CHANGELOG.md](CHANGELOG.md)); the backend analyzer is the newest piece.
