---
name: meridian-ci-cd
description: Manage, create, and troubleshoot Meridian GitHub Actions workflows. Use when working on CI/CD pipelines, the Meridian CI quality gate, workflow hygiene, action pins, Dependabot for actions, release or packaging workflows, a red check, or any file under .github/workflows/ or .github/actions/. Triggers include "add workflow", "fix CI", "pipeline failing", "GitHub Actions", "quality-gate", "workflow hygiene", and "clean up the workflows".
license: See repository LICENSE
compatibility: >
  Portable Agent Skill package for Agent Skills-compatible hosts. Reads markdown references
  and YAML templates on demand and edits repository files when the host permits filesystem writes.
metadata:
  owner: meridian-ai
  version: "1.0"
  spec: open-agent-skills-v1
---

# Meridian CI/CD & GitHub Actions

Meridian is a .NET 10 platform with a browser workstation and a WPF desktop shell. Its Actions
surface changes often, so this skill teaches where the truth lives and which rules are enforced.
It deliberately holds no workflow count or full inventory: the repository's hygiene check rejects
hand-written counts, and a copied table goes stale.

## Read these first

| Source | What it gives you |
|---|---|
| `.github/workflows/README.md` | Active workflow table (file, trigger, purpose, artifacts), lane mapping, standards, local equivalents |
| `docs/engineering/ci-cd-optimization.md` | Validation ownership, required checks, administrator rollout, benchmark promotion criteria |
| `docs/status/workflow-manifest.json`, `docs/generated/workflows-overview.md` | Generated inventory |
| `CLAUDE.md` | Quick commands and the rule that CI governance changes need human review |

`references/workflow-inventory.md` in this skill is an orientation map only. When it disagrees
with the repository README, the README wins.

## Architecture in brief

- **Meridian CI** (`meridian-ci.yml`) is the merge gate. It runs four lanes in parallel
  (`verify-dotnet`, `verify-browser`, `verify-docs`, `verify-workflows`; docs and workflows share
  one job) and reports one stable `quality-gate` check, plus an `integration-gate` job that runs
  the PostgreSQL-backed suites itself through `.github/actions/service-backed-integrations`.
- Every lane is `bash scripts/ci.sh --lane <name>`, so hosted and local runs execute the same
  script.
- **CI** (`ci.yml`) owns Secret Scan and nightly/manual coverage. **CodeQL** (`codeql.yml`)
  reports `Analyze csharp` and `Analyze javascript-typescript`.
- Specialist workflows are path-filtered or manual (Windows Desktop Build, Demo Smoke, Golden
  Path, Schema Control, provider smokes, screenshots, release packaging, certification). They are
  not required checks.
- `Targeted Test` (`targeted-test.yml`) is the manual hosted lane for when local validation is
  unreliable.
- There is no shared reusable build workflow. Shared setup is the composite action
  `.github/actions/setup-dotnet-nuget` (SDK from `global.json` plus the NuGet cache). Other
  composite actions: `install-playwright-chromium`, `close-superseded-refresh-prs`.

Required-check names are set in repository settings, not in workflow files. Never rename
`quality-gate`, `integration-gate`, `Secret Scan` or the `Analyze <language>` jobs as a cleanup.

## Enforced rules

`python3 build/scripts/ci/check-workflow-hygiene.py` (part of `verify-workflows`, with
actionlint) fails the lane when any of these is broken:

- Every external action is pinned to a full 40-character commit SHA with a `# vX` comment, in
  workflows and in local composite actions. Never use `@v4`-style tags.
- One action repository carries one pin everywhere. Copy the SHA already used in the repo;
  Dependabot's grouped `github-actions` update moves workflows and `.github/actions/*` together.
- Every job declares `timeout-minutes`, except a job that only calls a reusable workflow.
- Workflow names are unique; local `uses: ./...` targets exist; retired workflow filenames stay
  retired and are not referenced from active docs.
- Docs carry no hand-written workflow counts.

Conventions that are not machine-checked but are expected:

- Top-level `permissions: contents: read`; widen per job only where needed.
- `actions/checkout` with `persist-credentials: false` unless the job must push.
- PR and branch validation uses a concurrency group that cancels superseded runs. Schedule and
  manual runs use a separate group so a push cannot cancel them.
- .NET SDK comes from `global.json`; the browser lane reads Node from `.nvmrc`; Python is 3.12.
- Central package management applies: never put a package version in a project file.
- Restore and build with `/p:EnableWindowsTargeting=true` on Linux runners.
- Diagnostics go under ignored paths such as `artifacts/`; upload with `if: always()` when the
  evidence matters on failure.
- Many workflows have a contract test under `tests/scripts/test_*workflow*.py`. Search there
  before changing a pin, a path filter, or a step that looks redundant; some hardcoded values are
  asserted on purpose.

## Common tasks

### Add or change a workflow

1. Read the README row for the nearest existing workflow and copy its shape. Start from
   `assets/pr-workflow.yml` or `assets/scheduled-workflow.yml`.
2. If the setup is exactly "install SDK, cache NuGet", use
   `./.github/actions/setup-dotnet-nuget` after checkout and add
   `.github/actions/setup-dotnet-nuget/**` to the workflow's path filters.
3. Add or update the workflow's row in `.github/workflows/README.md`.
4. Regenerate the inventory and confirm the diff:
   `python3 build/scripts/docs/generate-workflow-manifest.py` and
   `python3 build/scripts/docs/generate-structure-docs.py --workflows-only --output docs/generated/workflows-overview.md`.
5. Validate (next section), then open a PR from a `claude/<task>` or `codex/<task>` branch.
   Workflow changes are CI governance changes: say so in the PR and leave the merge to the owner
   unless they ask for it.

### Validate locally

```bash
python3 build/scripts/ci/check-workflow-hygiene.py
python3 build/scripts/ci/check-lane-manifest.py --summary
actionlint                      # 1.7.12 is the CI pin
python3 -m unittest discover -s tests/scripts -p "test_*workflow*.py"
bash scripts/ci.sh --lane verify-workflows   # needs pwsh, actionlint and the pinned Python packages
```

Some `tests/scripts` suites need `pwsh`. If they fail in a container without it, compare against
the unchanged tree before blaming your change, and report them as pre-existing.

### Debug a red check

1. Identify the lane or workflow from the check name, then read the failed step's log. Lane
   summaries under `artifacts/ci-summary/` include a reproduction command.
2. Reproduce with the narrowest command: `bash scripts/ci.sh --lane verify-dotnet`,
   `--lane verify-browser`, `--lane verify-docs` or `--lane verify-workflows`.
3. If local machine limits, restore failures or MSBuild locks get in the way, dispatch the hosted
   lane instead of retrying broad local scripts:

   ```bash
   gh workflow run targeted-test.yml --ref <branch> -f mode=dotnet-filtered -f dotnet_project=tests/Meridian.Tests/Meridian.Tests.csproj -f dotnet_filter="FullyQualifiedName~<TestClassOrMethod>"
   ```

4. Fix the root cause. Do not skip, disable or quarantine a test to get green, and do not relax a
   gate's accepted conclusions.

| Symptom | Likely cause | Fix |
|---|---|---|
| NU1008 | Version on a `PackageReference` | Move the version to `Directory.Packages.props` |
| NETSDK1100 | Windows-targeted project on Linux | Pass `/p:EnableWindowsTargeting=true` |
| Hygiene: "pinned to more than one commit" | A new or stale action SHA | Use the SHA the rest of the repo uses |
| Hygiene: "has no timeout-minutes" | New job without a bound | Add a realistic `timeout-minutes` |
| `verify-docs` freshness failure | Generated docs out of date | `python build/python/cli/buildctl.py regen --fix` |
| `quality-gate` red with green lanes | A lane was skipped or cancelled unexpectedly | Read the gate's own log; it names the lane |

## Boundaries

- No Docker publishing, stale-issue, AI-review or mobile workflows: these were removed on purpose.
- Release, signing, certification and generated-repair publisher workflows have protected
  environments and write boundaries. Do not merge them with read-only validation or widen their
  permissions as part of a cleanup.
- Do not change repository rules, required checks or secrets from a workflow change.
- Claims about speed need measured evidence; see the benchmark promotion criteria in
  `docs/engineering/ci-cd-optimization.md`.
