# AGENTS.md

This file is a compatibility shim for agents that look for root `AGENTS.md`.
Keep it short and route detailed work to the canonical Meridian guidance sources.

## Read First

- `docs/README.md` for the canonical documentation front door.
- `docs/start/README.md`, `docs/product/README.md`, `docs/engineering/README.md`, and `docs/operators/README.md` for rebuilt audience paths.
- `docs/documentation-ownership.md` for documentation ownership, generated-doc, and archive rules.
- `CLAUDE.md` for the full repository guide.
- `.codex/skills/_shared/project-context.md` for current Codex project context.
- `docs/ai/codex/quickstart.md` for the fastest Codex task startup path.
- `docs/ai/navigation/README.md` and `docs/ai/generated/repo-navigation.md` for repo routing.
- `docs/architecture/project-structure.md` and `docs/architecture/module-map.md` for structure and boundaries.
- `docs/developer/setup.md`, `docs/developer/build-test-run.md`, and `docs/HELP.md` for current commands.
- `docs/development/desktop-testing-guide.md`, `docs/development/wpf-implementation-notes.md`, and `docs/development/desktop-workflow-automation.md` for WPF desktop validation.
- `docs/plans/current-direction-and-status.md`, `docs/status/ROADMAP.md`, and `docs/status/FEATURE_INVENTORY.md` for current delivery interpretation.

## Current Direction

- Meridian is a .NET 10 fund-management and trading-platform codebase.
- The authoritative local checkout path for this workspace is `D:\Meridian-main`.
- Position new roadmap and docs work around evidence-backed investment operations: trusted data, research, paper validation, books, reconciliation, approvals, and governed reports.
- `src/Meridian.Wpf/` and `src/Meridian.Ui/dashboard/` are both active operator UI surfaces.
- `src/Meridian.Ui/wwwroot/workstation/` remains the built browser workstation asset lane served by the local host.
- Keep `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/` as shared API/read-model support surfaces for both the desktop shell and browser workstation.
- No mobile development lane: do not create mobile applications, mobile-specific product surfaces, native iOS/Android clients, MAUI clients, React Native clients, Flutter clients, or mobile-first workflows. Responsive browser validation is allowed only for the browser workstation.
- Keep visible root operator navigation to `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`.

## Agent Workflow

1. Run `git status --short` before editing and treat unrelated changes as user-owned.
2. Route large-repo work through `docs/ai/navigation/README.md` and the generated repo map before broad recursive search.
3. Read the nearest source README and `docs/source/data/source-modules.yml` before source edits under `src/**`.
4. Prefer shared service/read-model seams before UI-specific forks.
5. Use the narrowest validation command that covers the files changed.
6. Update docs and AI indexes in the same change when behavior, workflow, prompt, skill, or agent guidance changes.
7. For documentation rebuild work, update the new canonical audience path first, then archive or redirect older hand-authored material in reviewable batches.

## Command Discovery

Use the maintained command references instead of copying command catalogs into this shim:

- `docs/developer/build-test-run.md`
- `docs/HELP.md`
- `docs/development/desktop-testing-guide.md`
- `docs/development/desktop-workflow-automation.md`
- `docs/operations/msix-packaging.md`
- `docs/operations/provider-degradation-calibration.md`
- `docs/ai/codex/quickstart.md`

For command discovery, start with:

```bash
make help
dotnet run --project src/Meridian/Meridian.csproj -- --help
python3 build/python/cli/buildctl.py --help
```
