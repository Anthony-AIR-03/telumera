# Contributing to Telumera

Telumera is built module-by-module against the backlog in `planning/Telumera_Asana_Import.csv` and
the roadmap in `planning/Telumera_Modular_Project_Plan.md`. These conventions keep that history
readable as the project grows past a single contributor.

## Issues / tasks

- The backlog lives in `planning/Telumera_Asana_Import.csv` (Epic → Task, grouped by module ID
  `M00`–`M11`). Check there before writing up a new task from scratch.
- Before starting a task, confirm it meets the **Definition of Ready** (plan §13): purpose is
  clear, one service/package owns it, acceptance criteria and input/output contracts are known,
  privacy and auth impact considered, dependencies linked, test approach identified.
- A task isn't done until it meets the **Definition of Done** (plan §14): acceptance criteria
  pass, no cross-service DB reads, contracts versioned, tests exist, logs/metrics/traces added,
  privacy/auth reviewed, retry/failure behavior implemented, docs/runbooks updated, local stack
  still works, CI passes, demoed with real or deterministic data.

## Branches

Short-lived branches off `main`, named `<type>/<module-id>-<short-description>` (module id
lowercase, omit it for changes that aren't tied to one module):

```
feat/m01.2-browser-tracking-sdk
fix/m03-issue-grouping-off-by-one
chore/eslint-upgrade
```

`<type>` matches the commit types below. Delete the branch once it's merged.

## Commits

Imperative mood, module id in parentheses when the change is scoped to one module/epic —
matching the existing history (e.g. `Add shared service-defaults library and .NET service
templates (M00.2)`):

```
<Imperative summary> (<module id>)

<optional body: why, not what — the diff already shows what>
```

Prefix the summary with a type when it clarifies intent and the change isn't a plain feature
addition: `Fix`, `Refactor`, `Docs`, `Chore`, `Test`. Keep one logical change per commit; don't
bundle an unrelated formatting pass into a feature commit.

## Pull requests

- Open a PR for anything beyond a trivial doc fix, even while working solo — it's the paper trail
  for *why*, and the habit is the point once other contributors join.
- Fill in what changed and why, which module/task it closes, and how it was verified (tests run,
  screenshots for UI, manual steps for infra).
- Self-review against the Definition of Done before requesting review.
- Once the M00.5 CI workflow exists, it must pass before merge; until then, run the relevant local
  checks yourself (`dotnet build` / `dotnet format --verify-no-changes` for .NET,
  `npm run lint` / `npm run format` / `npm run type-check` for the affected TypeScript workspace).
- Prefer squash merge so `main` keeps one commit per logical change.

## Releases

Releases follow the module sequence in plan §6 (Release 0 — Platform Foundation, Release 1 —
Product Analytics, and so on), not a calendar — a release ships when its module's acceptance
criteria are met, not on a fixed date. Tag a release as `v<release-number>-<module-codename>`
(e.g. `v0-platform-foundation`) once its module is demoed end-to-end with real or deterministic
data, per the Definition of Done.
