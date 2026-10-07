# Engineering Documentation

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

This is the canonical developer and agent entrypoint for Meridian engineering and contribution work.
It replaces hand-built planning and historical engineering prose with active operating guidance.

## Audience Paths

- **Start first:** [Start](../start/README.md)
- **Product context:** [Product](../product/README.md), including the [Meridian Design Document](../product/meridian-design-document.md)
- **AI policy:** [AI assistant contract](../ai/assistant-workflow-contract.md)
- **Source ownership:** [Source registry](../source/README.md)
- **Roadmap truth:** [Roadmap registry](../roadmap/README.md)
- **Generated output rules:** [Documentation ownership](../documentation-ownership.md)
- **Detailed implementation and tooling guides:** [Development](../development/README.md)
- **Writing or moving docs:** [Documentation Contribution Guide](../development/documentation-contribution-guide.md)
- **Dead-code cleanup inventory:** [Dead-Code Inventory](dead-code-inventory.md)
- **Production readiness and test debt:** [Production Readiness Audit 2026-07-27](production-readiness-audit-2026-07-27.md)
- **Security Master architecture audit:** [Security Master Architecture Audit 2026-08-13](security-master-architecture-audit-2026-08-13.md) *(dated source-evidence review; verify findings against current source)*
- **WPF performance and UI/UX audit:** [WPF Performance & UI/UX Refinement Audit 2026-06-14](wpf-perf-uiux-audit-2026-06-14.md) *(dated; its changes were not compiled or run when written)*
- **Release-evidence working ledger:** [Production-Certification Evidence Chain](production-certification-evidence-chain.md)
- **CI/CD ownership and rollout:** [CI/CD validation ownership](ci-cd-optimization.md)
- **Pipeline budget measurements (PRD-112):** [Bounded benchmark lane and retained evidence](pipeline-benchmark.md)
- **Docs regeneration automation constraints:** [Docs Regeneration Automation — Design Constraints](docs-regeneration-automation-design.md)
- **Browser/docs regeneration and generated merge conflicts:** [Run the complete maintained generation sequence](generated-merge-recovery.md)
- **Free development tools:** [Free Development Tools](free-development-tools.md)
- **C#/WPF market study companion:** [Practical C# and WPF for Financial Markets](practical-csharp-wpf-financial-markets.md)

## Architecture and Module Boundaries

Use the source-module registry and source README workflow before changing code:

- [Source module registry](../source/data/source-modules.yml) *(canonical for engineering ownership)*
- [Source ownership and validation](../source/README.md) *(canonical ownership workflow)*
- [Architecture](../architecture/README.md) *(canonical system design and rationale)*
- [Module map](../architecture/module-map.md) *(maintained boundary reference; module ownership is registered in `source/data/source-modules.yml`)*
- [Project structure](../architecture/project-structure.md) *(maintained repository overview)*
- [Live trading engine](live-trading-engine.md) *(promotion → execution loop: feed tap, strategy sessions, OMS routing)*
- [ETL execution ownership](etl-execution-ownership.md) *(single-run admission, guarded publication, takeover, and commit ordering)*

Canonical ownership rule:

- Keep business logic in application/domain/shared-service layers.
- Keep UI behavior thin in `src/Meridian.Wpf/` and `src/Meridian.Ui/dashboard/`.
- Keep shared UI read-model/service contracts in `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/`.
- Never create duplicate business behavior per surface unless a surface-specific constraint exists.

## Plans and Blueprints

Every planning document has one home and one register entry. The
[Plans and Blueprints Register](blueprints/README.md) is the **single index of every plan and
blueprint in the repository**. It names the home folder for each kind:
code-ready designs in [`blueprints/`](blueprints/README.md), engineering working plans in
[`plans/`](plans/README.md), and product delivery plans in
[`../product/plans/`](../product/plans/README.md). It also records the shared conventions (ledger
migration ordinals, DDL precision, API route prefixes, enum extension, terminology) and the
cross-blueprint contracts that stop two independently written designs from colliding.

## Build/Test/Run

Prefer the narrowest proof lane for the files you change.

Enable the [staged-source Git hook](../development/git-hooks.md) with
`./scripts/dev/install-git-hooks.sh` or `make install-hooks`, including in linked worktrees.
The hook checks an isolated snapshot of staged C#/VB whitespace and skips .NET for commits
without supported source files. CI continues to check whitespace across `Meridian.sln`.

For hosted CI cache behavior, artifact locations, and timing comparisons, see
[dependency caches and artifacts](../../.github/workflows/README.md#dependency-caches-and-artifacts).

For completed PR-ready work, use the canonical repository gate:

```powershell
bash scripts/ci.sh
```

GitHub Actions `Meridian CI / quality-gate` is the authoritative merge result. Local work may
happen on `main` when the user explicitly requests it or the checkout is intentionally operating
there. Do not bypass GitHub branch protections; for PR-ready publishing, use a
`codex/<short-task-name>` branch and a pull request targeting `main`.

The four-lane `Meridian CI / quality-gate` keeps its identity. The always-reported
`Meridian CI / integration-gate` companion reports service-backed PostgreSQL tests shared with
Production Certification, including pull requests and merge groups.

The .NET lane builds the web host, then all unique default test projects in one generated
solution-filter build for the standard `Release` configuration. The filter follows shared
dependencies and verifies that the solution enables each selected test project in `Release`.
A grouped build failure remains fatal and triggers serial builds for project-specific diagnostics
before stopping. Explicit `--project` overrides and other configurations retain separate builds.
The hosted lane then runs at most two test shards concurrently. Local `scripts/ci.sh` runs retain
the sequential test default; set
`MERIDIAN_CI_TEST_MAX_PARALLEL=2` only when local capacity permits. Each shard uses isolated
temporary files and retains its logs, TRX results, and duration under
`artifacts/test-results/dotnet/`; temporary fixture files are removed after the shard exits.
The project roster, catch-all shard, filters, and failure aggregation remain the same.
Golden Path runs browser, WPF, and pilot harness validation concurrently, then requires
all three successes in the stable `Pilot Acceptance Evidence` gate. See the
[workflow guide](../../.github/workflows/README.md) for the execution and evidence details.

For local .NET tests, prefer the contention-aware runner over raw `dotnet test`:

```powershell
python build/python/cli/buildctl.py validation-status --summary
python build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~<TestClassOrMethod>" --queue
```

The runner serializes local validation, detects active repo-owned build/test/compiler processes,
builds before testing to avoid stale `--no-build` assemblies, uses isolated `artifacts/bin` and
`artifacts/obj` roots by default, and writes run evidence under `.ai/validation-runs/`.
For repeated local edits, add `--profile worktree` or `--profile session:<name>` to a `build` or
`test` command to reuse compatible restore/build outputs. Keep the same project, SDK, framework,
configuration, runtime, and build properties for that profile; incompatible reuse fails before
building. Every test invocation still gets a separate report directory. Omit the profile (the test
default) or use `--fresh` for fresh isolated validation before handing off a change. Both build and
test commands acquire the validation lock; the test command's `--allow-concurrent` flag only
skips detection of external build processes and keeps the lock. See
[persistent build profiles](../development/build-observability.md#persistent-build-profiles)
for compatibility, retention, and reset details.

Each normal run restores, builds once, then invokes `dotnet test --no-build --no-restore`.
To explicitly reuse outputs, pass `--no-build --isolation-key <existing-key>` (the prior run's
`isolationKey` in its evidence JSON), `--no-build --no-isolation` for shared outputs, or
`--no-build --profile <profile>` after a successful compatible profile build.
Reuse skips both restore and compilation and requires the same project, configuration, framework,
runtime, and MSBuild properties used to produce the outputs. It does not check source freshness;
rerun without `--no-build` after source changes or when outputs are missing or incompatible.
The default `auto` isolation key is rejected with `--no-build` because it selects a new output
location. Missing outputs fail the test step without falling back to a build.
An explicit isolation key selects outputs independently of the unique `--run-id` used for evidence;
the two names may match when that run ID has not been used before. `VSTestNoBuild` is managed by
the runner and cannot be overridden through `--property`, so the test phase cannot compile again.

After a timed-out generation, build, or test attempt, run `python build/python/cli/buildctl.py
validation-status --summary`, then `dotnet build-server shutdown`. Stop only abandoned repo-owned
`dotnet`, `MSBuild`, `testhost`, `csc`, or `VBCSCompiler` PIDs after confirming their command lines
point at this checkout; do not kill unrelated local .NET services.

For local pre-PR proof across the highest-value free tools, use:

```powershell
pwsh ./scripts/dev/run-local-quality.ps1
pwsh ./scripts/dev/run-local-quality.ps1 -IncludePlaywrightSmoke
```

When local CPU, memory, disk, package restore, or MSBuild lock contention makes validation
unreliable, push the branch and run the manual GitHub-hosted
`Targeted Test` workflow before retrying broad local scripts. Select a whitelisted `mode`; the
`dotnet-filtered` mode accepts a repo-relative .NET test project under `tests/` plus a required
positive class, method, trait, or fully qualified name filter.

```powershell
gh workflow run targeted-test.yml --ref <branch> `
  -f mode=dotnet-filtered `
  -f dotnet_project=tests/Meridian.Tests/Meridian.Tests.csproj `
  -f dotnet_filter="FullyQualifiedName~<TestClassOrMethod>"
```

Prefer the validated dispatcher when using the CLI from a local branch:

```powershell
python build/scripts/ci/dispatch-targeted-test.py `
  --ref <branch> `
  --mode dotnet-filtered `
  --dotnet-project tests/Meridian.Tests/Meridian.Tests.csproj `
  --dotnet-filter "FullyQualifiedName~<TestClassOrMethod>" `
  --wait
```

### Most common default lanes

```powershell
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
python build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --queue
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
```

### PostgreSQL schema control

SQL migrations remain the authoritative physical schema. The schema-control workflow applies every
registered migration module to disposable PostgreSQL 16, extracts PostgreSQL-specific catalog
metadata, inventories public C# DTOs and related data objects, evaluates database policies, and
checks the generated manifests and Mermaid diagrams for drift.

```powershell
# Resolve and retain the intended comparison baseline.
$baselineSha = git rev-parse --verify 'origin/main^{commit}'

# Local, database-free migration inventory and safety checks
python build/scripts/schema-control.py inventory --base-ref $baselineSha

# Rebuild and verify with an owned disposable PostgreSQL container (requires Docker)
python -m pip install --requirement tools/schema_control/requirements.txt
python build/scripts/schema-control.py local `
  --base-ref $baselineSha

# Generate a hosted snapshot artifact for review
gh workflow run schema-control.yml --ref <branch> -f mode=snapshot -f baseline_ref=$baselineSha
```

The local command allocates a fresh container, host port, and retained candidate directory for
each run, so consecutive and concurrent worktree checks need no manual database reset or port
selection. It retains verification and PostgreSQL logs and cleans up its owned resources after
completion or cancellation. Use `local --mode snapshot` to prepare a reviewed candidate;
`promote --candidate-root <printed-candidate-directory>` remains an explicit separate command.

PR checks compare against the pull-request event's base SHA. Both manual modes require an explicit
`baseline_ref`, resolved once to a commit. Run evidence records the baseline and the actual
checked-out candidate SHA (normally the GitHub merge commit for PRs). Use that same pair to
reproduce a comparison independently of later `origin/main` advances; see the schema-control guide
for evidence paths and local working-tree details.

Never point `snapshot` or `verify` at a shared or production database. The workflow's check mode is
read-only with respect to the repository and fails when `database/manifest/**` or
`docs/generated/database/**` is stale. Review a hosted snapshot before using `promote`; the tool
does not infer DDL from DTOs or mutate production state. See the
[schema-control guide](../../tools/schema_control/README.md) and the
[generated database catalog](../generated/database/README.md).

### Browser-workstation slices

```powershell
dotnet build Meridian.WebWorkstation.slnf -c Debug --no-restore /p:EnableWindowsTargeting=true /p:UseAppHost=false
```

### Desktop slices

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1 -Restore
pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Development
pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Production -BuildOnly
dotnet restore tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release --no-restore /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:WindowsPackageType=None
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release --no-restore --filter "Category!=Integration" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
```

If GNU Make is installed, `make desktop-test-dev` is a convenience wrapper for the WPF development
validation script. In Windows shells where `where.exe make` finds nothing, use the `pwsh` command
above directly.

Use `-AllowConcurrentDotnet` only when the active repo-owned build/test processes have been
inspected and intentional overlap is acceptable. The validation script serializes WPF builds by
default using:

```powershell
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release --no-restore --no-dependencies /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:WindowsPackageType=None -v:minimal
```

## Local Run

```powershell
npm run dev
npm run dev:fixtures
pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Development
```

For browser development, `npm run dev` coordinates the seeded host, backend watch mode, and
Vite; `npm run dev:fixtures` selects fixture-only API responses. See the
[browser development launcher](web-development.md) for prerequisites, port/data options,
readiness, process ownership, and hot-reload/restart/shutdown acceptance checks. To serve the
tracked browser bundle directly with a configured host, use
`dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080`.

Use `pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Production -BuildOnly` for a Release
host/desktop build that does not require database connectivity. Use `-LaunchMode Production`
without `-BuildOnly` only when the production governance persistence variables are configured:
`MERIDIAN_DATABASE_URL`, or `MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING` and
`MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING`.
Development launch mode sets `DOTNET_ENVIRONMENT=Development`,
`ASPNETCORE_ENVIRONMENT=Development`, and `MERIDIAN_USE_INMEMORY_GOVERNANCE=true` for the
launched processes, then restores the caller's environment.

### Persistence

Non-demo host launches need a persistence decision and **fail closed at startup**
without one: `StorageFeatureRegistration` throws, naming the missing variable, rather than silently
running in-memory. Set `MERIDIAN_DATABASE_URL` to persist every store domain to one PostgreSQL
database; per-domain `MERIDIAN_*_CONNECTION_STRING` variables take precedence over it, so
split-database deployments keep working. `MERIDIAN_USE_INMEMORY_GOVERNANCE=true` is an explicit
local/dev fixture opt-in and is refused when the environment is `Production`.

The canonical description of these defaults, including simulated market data and authentication,
is [Start → Persistence and simulation defaults](../start/README.md#persistence-and-simulation-defaults).

## Workstation Architecture Rules

### WPF Workstation

- Keep views focused on rendering.
- Route commands, state, and labels through view models, services, and shared read models.
- Preserve shared session/workflow semantics with browser.

### Browser Workstation

- Keep React screens presentation-focused.
- Reuse shared service/read-model/contracts from `src/Meridian.Ui.Services` and `src/Meridian.Ui.Shared`.
- Keep fixture/demo states explicitly labeled; do not imply production readiness without evidence.

### Shared Pipeline Rule

Any behavior changed at one workstation must be represented once in shared
contracts/read models/endpoints, then surfaced by each UI.

## Documentation and Registry Ownership

- Source-module truth: `docs/source/data/*.yml` and generated source docs.
- Roadmap truth: `docs/roadmap/data/*.yml` and generated roadmap views.
- Generated docs are not hand-edited; update inputs/generators and regenerate.
- Detailed hand-authored guides under `docs/development/` are supporting material reached through
  this canonical lane; archived guides are historical context only.
- Canonical ownership rules are in `../documentation-ownership.md`; this page is only the active
  engineering start lane.

### Required Engineered Canonicality

- This page is the canonical engineering start for WPF/browser/operator-facing implementation work.
- Architecture and build/test claims must be validated through:
  - owning module README
  - `docs/source/data/source-modules.yml`
  - owning roadmap row in `docs/roadmap/data/*.yml`
- If ownership, validation scope, or boundaries change in `src/**`, update:
  - `docs/source/data/source-modules.yml`
  - `docs/source/data/source-todos.yml` (when TODOs change)
  - The nearest source `README.md`
  - `docs/source/data/diagram-index.yml` (when diagrams change)
- If registry ownership or module boundaries change, regenerate affected source docs after stale-doc sync:

```powershell
python build/scripts/docs/mark-stale-docs.py --write --summary
python build/scripts/docs/validate-source-readmes.py --summary
```

Canonical commands:

```powershell
python build/scripts/docs/validate-docs-structure.py --summary
python build/scripts/docs/validate-source-readmes.py --summary
python build/scripts/docs/validate-roadmap-registry.py --summary
python build/scripts/docs/validate-doc-hashes.py --summary
python build/scripts/docs/check-ai-inventory.py --summary
python build/scripts/docs/check-ai-handoff.py --strict
python build/scripts/docs/check-ai-contract-drift.py --canonical docs/ai/contract-policy.json --mirror docs/ai/copilot/contract-policy.mirror.json --mirror docs/ai/claude/contract-policy.mirror.json
python build/scripts/docs/check-codex-skills.py --summary
```

## Source Change Procedure (Required)

Before editing `src/**`:

1. Read the nearest `src/**/README.md`.
2. Verify module in `docs/source/data/source-modules.yml`.
3. Update affected source README/registry items for ownership, validation, boundary, or TODO changes.
4. Run stale-doc hash flow when module docs are changed:

```powershell
python3 build/scripts/docs/mark-stale-docs.py --write --summary
```

## Supporting Detail and Historical Sources

- [Development Guides](../development/README.md) *(active supporting implementation detail)*
- [Desktop Testing Guide](../development/desktop-testing-guide.md) *(active supporting WPF detail)*
- [Architecture Documentation](../architecture/README.md) *(canonical system design and rationale)*
- [Developer Quick Guides](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/developer/README.md) *(historical source material)*
- [Older WPF workflow notes](../development/wpf-implementation-notes.md) *(supporting context; verify against current source and parity plan)*

## Canonical Ownership Summary

- Canonical engineering start: this file + nearest module `README.md`.
- Canonical ownership and module map: `docs/source/data/source-modules.yml`, `docs/source/README.md`.
- Canonical roadmap and acceptance: `docs/roadmap/data/*.yml` + generated roadmap outputs.
- Canonical generated engineering docs: outputs under `docs/source/generated/` (do not hand-edit).
