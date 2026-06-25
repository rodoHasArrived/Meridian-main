# AGENTS.md

This file is a compatibility shim for agents that look for root `AGENTS.md`.
Keep it short and route detailed work to the canonical Meridian guidance sources.

## Meridian Repository Operating Policy

These instructions apply to all work in this repository.

### Protected Target

The `main` branch is protected by GitHub repository rules. These repository
instructions defer to those rules rather than adding a separate blanket
prohibition on writes to `main`. Never attempt to bypass GitHub repository
rules, required status checks, required reviews, or branch protections.

### Local Main Work

AI developers may inspect and make local changes while checked out on `main`
when the user explicitly requests it or the checkout is intentionally operating
there. This local permission does not allow direct protected-branch pushes,
bypasses, self-approval, self-merge, or skipped status checks.

### Default Branch Workflow

Unless the task explicitly requires a different protected-branch flow that
GitHub repository rules allow, use the pull-request workflow:

1. Begin from the latest `origin/main`.
2. Create or use a branch named `codex/<short-task-name>`.
3. Make only changes that are required for the assigned task.
4. Add or update appropriate automated tests.
5. Run `bash scripts/ci.sh`.
6. Do not represent a change as complete when that command fails.
7. Commit the completed change to the feature branch.
8. Push the feature branch to GitHub.
9. Open or update a pull request targeting `main`.
10. Report the commands run and their results in the pull request.

### GitHub Actions Authority

GitHub Actions is the authoritative integration-test result. A successful test run
inside the Codex environment does not replace the required GitHub Actions check.
When GitHub Actions fails:

1. Inspect the failure.
2. Correct the underlying problem.
3. Run `bash scripts/ci.sh` again.
4. Push a new commit to the same pull-request branch.
5. Do not merge or request bypass of the failed check.

### Prohibited Actions

Do not:

- Force-push any shared branch.
- Use `--no-verify`.
- Add `[skip ci]` or another CI-skip directive.
- Disable, delete, weaken, or conditionally bypass tests.
- Change expected behavior merely to make a failing test pass.
- Change repository rulesets or branch protections.
- Self-approve or self-merge pull requests.
- Merge a pull request.
- Store credentials, tokens, keys, or production secrets in the repository.
- Modify CI governance files unless the task explicitly requests it and the pull
  request is designated for human governance review.

### Protected Governance Files

The following files require explicit human review:

- `.github/workflows/**`
- `.github/CODEOWNERS`
- `.github/pull_request_template.md`
- `AGENTS.md`
- `scripts/ci.sh`

### Definition Of Done

Pull-request work is complete only when:

- The requested implementation is present.
- Relevant tests were added or updated.
- `bash scripts/ci.sh` succeeds.
- The branch is pushed to GitHub.
- A pull request targeting `main` exists.
- The pull request explains the change and test evidence.
- Required GitHub Actions checks pass.

A passing local or Codex-environment test is preliminary evidence only. GitHub
Actions remains the merge authority.

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
- `docs/ai/codex/memory-system.md`, `.codex/memory/index.yml`, `.codex/memory/tasks/*.yml`
  descriptors, and `.codex/memory/goals/*.yml` inventories for repo-local Codex memory routing,
  progress tracking, promotion, and validation rules.
- `docs/ai/codex/self-improving-agents.md` for Codex agent improvement, eval promotion, and graph-memory guardrails.
- `docs/product/meridian-design-document.md` for the canonical stakeholder design framing.
- `docs/architecture/meridian-development-intelligence-framework.md`, `docs/architecture/meridian-vision.md`, `docs/architecture/meridian-domain-model.md`, `docs/domain/README.md`, and `docs/ai/context/README.md` for MDIF architecture, domain, and AI context packs before broad generation or architecture-sensitive work.
- `docs/ai/navigation/README.md` and `docs/ai/generated/repo-navigation.md` for repo routing.
- `docs/architecture/project-structure.md` and `docs/architecture/module-map.md` for structure and boundaries.
- `docs/start/README.md` and `docs/engineering/README.md` for commands and validation.
- `docs/roadmap/README.md` and `docs/roadmap/data/*.yml` for current roadmap direction.
- `docs/reference/README.md` for API/env/CLI/schema lookup.

## Current Direction

- Meridian is a .NET 10 operational-finance and trading-platform codebase; fund management is a first-class specialization, not the root model for every workflow.
- The authoritative local checkout path for this workspace is `D:\Meridian-main`.
- Position roadmap and docs work from current source evidence, the roadmap registry, and the current [Meridian Design Document](docs/product/meridian-design-document.md).
- Treat prior baselines and named productization targets as roadmap/status evidence, not development ceilings. Expansion lanes can proceed when current source, roadmap, or user direction supports them.
- Use the current [Meridian Design Document](docs/product/meridian-design-document.md) as the canonical product scope reference.
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
7. If local machine capacity, restore, or MSBuild locks block validation, run `python build/python/cli/buildctl.py validation-status --summary`, shut down leftover build servers with `dotnet build-server shutdown`, and stop only abandoned repo-owned `dotnet`/`MSBuild`/`testhost`/compiler PIDs whose command lines clearly point at this checkout before retrying; if local proof remains unreliable, push the branch and use the manual GitHub-hosted `Targeted Test` workflow with a repo-relative test project under `tests/` plus `dotnet_filter`.
8. Update docs and AI indexes in the same change when behavior, workflow, prompt, skill, or agent guidance changes.
9. For memory-aware Codex tasks, inspect `.codex/memory/index.yml` before loading durable
   memory. If the work has a named scope, route through the matching
   `.codex/memory/tasks/<task-id>.yml` descriptor; if it is long-running, use the relevant
   `.codex/memory/goals/<goal-id>.yml` inventory for progress, evidence, next actions, blockers,
   and promotion candidates. Load only the selected entries that match the descriptor, current
   intent, selected skill, changed paths, branch, or explicit tags; prefer canonical docs, source,
   tests, scripts, scoped `AGENTS.md`, and selected `SKILL.md` files when memory disagrees.
10. Emit compact memory receipts when memory routing is used: selected IDs, match reasons, stale
   warnings, active-goal progress count, and task or branch entries skipped because scope did not
   match. For Codex memory changes, update `.codex/memory/index.yml` plus the indexed Markdown
   entry, task descriptor, or goal inventory, keep user/global tiers disabled unless explicitly opted
   in, use `--receipt` for task or goal routing, and run
   `python build/scripts/docs/check-codex-memory.py --summary`.
11. For Codex development workflow changes, keep `AGENTS.md`, `CLAUDE.md`, `.github/copilot-instructions.md`, `.github/agents/implementation-assurance-agent.md`, `.github/workflows/README.md`, `docs/engineering/README.md`, `docs/start/README.md`, `.codex/skills/_shared/*`, `.claude/skills/_shared/project-context.md`, and `.agents/skills/_shared/project-context.md` synchronized when they teach the same rule.
12. For documentation rebuild work, update the new canonical audience path first, then archive or redirect older hand-authored material in reviewable batches.

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
