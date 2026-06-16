# AGENTS.md

This file is a compatibility shim for agents that look for root `AGENTS.md`.
Keep it short and route detailed work to the canonical Meridian guidance sources.

## Read First

- `docs/README.md` for the canonical documentation front door.
- `docs/product/meridian-design-document.md` for the current design charter.
- `docs/start/README.md`, `docs/product/README.md`, `docs/engineering/README.md`, and `docs/operators/README.md` for rebuilt audience paths.
- `docs/documentation-ownership.md` for documentation ownership, generated-doc, and archive rules.
- `CLAUDE.md` for the full repository guide.
- `.github/copilot-instructions.md` and `.github/agents/implementation-assurance-agent.md` for GitHub-hosted assistant mirrors that must stay aligned with shared development and validation rules.
- `.github/workflows/README.md` for GitHub-hosted validation workflows, including manual targeted testing.
- `.codex/skills/_shared/project-context.md` for current Codex project context.
- `docs/ai/codex/quickstart.md` for the fastest Codex task startup path.
- `docs/ai/codex/self-improving-agents.md` for Codex agent improvement, eval promotion, and graph-memory guardrails.
- `docs/product/meridian-design-document.md` for the canonical stakeholder design framing.
- `docs/architecture/meridian-development-intelligence-framework.md`, `docs/architecture/meridian-vision.md`, `docs/architecture/meridian-domain-model.md`, `docs/domain/README.md`, and `docs/ai/context/README.md` for MDIF architecture, domain, and AI context packs before broad generation or architecture-sensitive work.
- `docs/ai/navigation/README.md` and `docs/ai/generated/repo-navigation.md` for repo routing.
- `docs/architecture/project-structure.md` and `docs/architecture/module-map.md` for structure and boundaries.
- `docs/start/README.md` and `docs/engineering/README.md` for commands and validation.
- `docs/roadmap/README.md` and `docs/roadmap/data/*.yml` for current roadmap direction.
- `docs/reference/README.md` for API/env/CLI/schema lookup.

## Current Direction

- Meridian is a .NET 10 fund-management and trading-platform codebase.
- The authoritative local checkout path for this workspace is `D:\Meridian-main`.
- Position new roadmap and docs work around the W1-W5 operational record baseline: data confidence, retained source evidence, reconciliation, approvals, accounting records, multi-asset operational coverage, and governed reports.
- Defer Backtesting Studio, live-readiness beyond paper-first governance, full payments, forecasting, enterprise risk, client portal, no-code workflow design, mobile, and other expansion lanes unless the work directly strengthens that operational record workflow.
- Use the current [Meridian Design Document (Version 0.15)](docs/product/meridian-design-document.md) as the canonical product scope reference.
- `src/Meridian.Wpf/` and `src/Meridian.Ui/dashboard/` are both active operator UI surfaces.
- `src/Meridian.Ui/wwwroot/workstation/` remains the built browser workstation asset lane served by the local host.
- Keep `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/` as shared API/read-model support surfaces for both the desktop shell and browser workstation.
- No mobile development lane: do not create mobile applications, mobile-specific product surfaces, native iOS/Android clients, MAUI clients, React Native clients, Flutter clients, or mobile-first workflows. Responsive browser validation is allowed only for the browser workstation.
- Keep visible root operator navigation to `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`.

## Agent Workflow

1. Run `git status --short` before editing and treat unrelated changes as user-owned.
2. Route large-repo work through `docs/ai/navigation/README.md` and the generated repo map before broad recursive search.
3. Load the relevant MDIF constitution, domain dictionary, and AI context pack before broad code generation, domain modeling, workflow design, or architecture-sensitive refactors.
4. Read the nearest source README and `docs/source/data/source-modules.yml` before source edits under `src/**`.
5. Prefer shared service/read-model seams before UI-specific forks.
6. Use the narrowest validation command that covers the files changed.
7. If local machine capacity, restore, or MSBuild locks block validation, push the branch and use the manual GitHub-hosted `Targeted Test` workflow before retrying broad local scripts.
8. Update docs and AI indexes in the same change when behavior, workflow, prompt, skill, or agent guidance changes.
9. For Codex development workflow changes, keep `AGENTS.md`, `CLAUDE.md`, `.github/copilot-instructions.md`, `.github/agents/implementation-assurance-agent.md`, `.github/workflows/README.md`, `docs/engineering/README.md`, `docs/start/README.md`, `.codex/skills/_shared/*`, `.claude/skills/_shared/project-context.md`, and `.agents/skills/_shared/project-context.md` synchronized when they teach the same rule.
10. For documentation rebuild work, update the new canonical audience path first, then archive or redirect older hand-authored material in reviewable batches.

## Command Discovery

Use maintained command references instead of copying command catalogs into this shim:

- `docs/start/README.md`
- `docs/engineering/README.md`
- `docs/HELP.md`
- `docs/ai/codex/quickstart.md`
- `docs/documentation-ownership.md`
- `docs/ai/assistant-workflow-contract.md`

For command discovery in Windows/PowerShell, first use the direct commands below. GNU Make is an
optional convenience wrapper in this repo; only run `make help` when `where.exe make` finds an
installed `make`.

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --help
python build/python/cli/buildctl.py --help
```
