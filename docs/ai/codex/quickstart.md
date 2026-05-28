# Codex Quickstart

Use this page for the first 10 minutes of a Codex task in Meridian. It compresses the shared
workflow into a task routing checklist, proof matrix, and dirty-worktree protocol. Shared policy
still lives in `../assistant-workflow-contract.md`.

## Startup Checklist

1. Run `git status --short` and separate existing user-owned changes from the task.
2. Classify the request as orient, review, docs, browser, WPF, provider, storage, execution, roadmap,
   cleanup, or test work.
3. Read `../navigation/README.md` and `../generated/repo-navigation.md` for large-repo routing.
4. Read the narrowest relevant Codex skill in `.codex/skills/`.
5. For source edits under `src/**`, read the nearest `README.md` and identify the module in
   `docs/source/data/source-modules.yml`.
6. Choose the smallest validation lane from the task-to-proof matrix before editing.
7. Update the nearest docs or AI index when behavior, workflow, prompt, skill, or agent guidance
   changes.

## Read Budget

Load only enough context to route and validate the task.

| Task state | Read first | Avoid until needed |
| --- | --- | --- |
| Unknown subsystem | `../navigation/README.md`, `../generated/repo-navigation.md` | Full repo scans and broad plan families |
| Source change | Nearest `src/**/README.md`, `docs/source/data/source-modules.yml` | Generated source docs unless a generator is in scope |
| AI docs change | `../assistant-workflow-contract.md`, this page, `README.md` | Host-specific mirrors unless shared policy changes |
| WPF task | `.codex/AGENTS.md`, relevant WPF skill, nearest view model/tests | Broad WPF suites before focused filters |
| Browser task | `.codex/skills/meridian-browser-workstation/SKILL.md`, package tests | WPF validation unless shared contracts changed |

## Dirty Worktree Protocol

- Treat pre-existing changes as user-owned.
- Do not revert, format, or rewrite files outside the requested scope.
- If committed and uncommitted changes coexist, report them separately.
- If a user-owned change touches the same file, read the local diff before editing and preserve
  unrelated hunks.
- Use focused `git diff -- <path>` checks before finalizing so the final summary only claims files
  changed for this task.

## Task-To-Proof Matrix

| Change type | Default docs | Narrow validation |
| --- | --- | --- |
| Codex skill, prompt, checklist, or AI index | `docs/ai/codex/README.md`, `.codex/skills/README.md`, nearest skill or prompt index | `python build/scripts/docs/check-codex-skills.py --summary`; `python build/scripts/docs/check-ai-inventory.py --summary`; `git diff --check -- <paths>` |
| Shared AI policy | `docs/ai/assistant-workflow-contract.md`, `docs/ai/README.md`, affected host index | `python build/scripts/docs/check-ai-inventory.py --summary`; contract-drift check when policy JSON changes |
| Repo navigation or MCP routing | `docs/ai/navigation/README.md`, generated navigation inputs | `python build/scripts/docs/generate-ai-navigation.py --json-output docs/ai/generated/repo-navigation.json --markdown-output docs/ai/generated/repo-navigation.md --recent-changes-output docs/ai/generated/recent-changes.md --summary`; `python build/scripts/docs/check-ai-navigation-freshness.py --max-age-days 14` |
| Browser workstation | `src/Meridian.Ui/dashboard/README.md` when present, related screen docs | `npm --prefix src/Meridian.Ui/dashboard run test`; targeted Vitest file when the change is narrow; `npm --prefix src/Meridian.Ui/dashboard run build` for build-facing changes |
| Shared workstation endpoint or DTO | Nearest `src/**/README.md`, `docs/architecture/module-map.md` | Focused `tests/Meridian.Tests` endpoint filter plus UI consumer tests when DTOs are consumed by WPF or browser |
| WPF view model, shell route, or desktop workflow | `.codex/AGENTS.md`, relevant WPF skill/checklist, desktop testing docs | Focused `tests/Meridian.Wpf.Tests` filter for the touched view model, shell route, or workflow script |
| Provider adapter or provider workflow | Provider docs, `src/Meridian.ProviderSdk`, nearest adapter README | Focused provider tests or provider-validation script that covers the adapter and credential path |
| Storage, WAL, or archival behavior | Storage docs and nearest `src/Meridian.Storage` README | Focused storage/WAL tests; avoid direct file writes outside WAL or `AtomicFileWriter` patterns |
| Docs-only change | Nearest docs index | `git diff --check -- <paths>` plus inventory checks when AI catalogs are affected |

## High-Traffic Skill Defaults

| Skill | Default validation snippet |
| --- | --- |
| `meridian-repo-navigation` | `python build/scripts/docs/check-ai-navigation-freshness.py --max-age-days 14` |
| `meridian-docs` | `git diff --check -- <paths>` and `python build/scripts/docs/check-ai-inventory.py --summary` for AI docs |
| `meridian-browser-workstation` | `npm --prefix src/Meridian.Ui/dashboard run test` or targeted Vitest files |
| `modular-desktop-mvvm` | Focused `dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~<TouchedViewModelOrRoute>" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true` |
| `meridian-implementation-assurance` | Touched-project build/test plus docs sync; AI/tooling changes also run the AI inventory and skill checks |

## Final Response Checklist

- Files changed and why.
- Validation commands with pass/fail results.
- Unrelated dirty-worktree changes explicitly excluded.
- Any docs, tests, or generated artifacts intentionally not updated.
- Residual risk or blocked validation, if any.
