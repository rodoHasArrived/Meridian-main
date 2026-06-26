# Engineering Documentation

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-05-31

This is the canonical developer and agent entrypoint for Meridian engineering and contribution work.
It replaces hand-built planning and historical engineering prose with active operating guidance.

## Audience Paths

- **Start first:** [Start](../start/README.md)
- **Product context:** [Product](../product/README.md), including the [Meridian Design Document (Draft v1.0)](../product/meridian-design-document.md)
- **AI policy:** [AI assistant contract](../ai/assistant-workflow-contract.md)
- **Source ownership:** [Source registry](../source/README.md)
- **Roadmap truth:** [Roadmap registry](../roadmap/README.md)
- **Generated output rules:** [Documentation ownership](../documentation-ownership.md)
- **Dead-code cleanup inventory:** [Dead-Code Inventory](dead-code-inventory.md)
- **Free development tools:** [Free Development Tools](free-development-tools.md)
- **C#/WPF market study companion:** [Practical C# and WPF for Financial Markets](practical-csharp-wpf-financial-markets.md)

## Architecture and Module Boundaries

Use the source-module registry and source README workflow before changing code:

- [Source module registry](../source/data/source-modules.yml) *(canonical for engineering ownership)*
- [Source ownership and validation](../source/README.md) *(canonical ownership workflow)*
- [Module map](../architecture/module-map.md) *(legacy source material; verify against `source/data/source-modules.yml`)*
- [Project structure](../architecture/project-structure.md) *(legacy source material)*

Canonical ownership rule:

- Keep business logic in application/domain/shared-service layers.
- Keep UI behavior thin in `src/Meridian.Wpf/` and `src/Meridian.Ui/dashboard/`.
- Keep shared UI read-model/service contracts in `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/`.
- Never create duplicate business behavior per surface unless a surface-specific constraint exists.

## Build/Test/Run

Prefer the narrowest proof lane for the files you change.

For completed PR-ready work, use the canonical repository gate:

```powershell
bash scripts/ci.sh
```

GitHub Actions `Meridian CI / quality-gate` is the authoritative merge result. Local work may
happen on `main` when the user explicitly requests it or the checkout is intentionally operating
there. Do not bypass GitHub branch protections; for PR-ready publishing, use a
`codex/<short-task-name>` branch and a pull request targeting `main`.

For local .NET tests, prefer the contention-aware runner over raw `dotnet test`:

```powershell
python build/python/cli/buildctl.py validation-status --summary
python build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~<TestClassOrMethod>" --queue
```

The runner serializes local validation, detects active repo-owned build/test/compiler processes,
builds before testing to avoid stale `--no-build` assemblies, uses isolated `artifacts/bin` and
`artifacts/obj` roots by default, and writes run evidence under `.ai/validation-runs/`.
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

### Most common default lanes

```powershell
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
python build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --queue
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
```

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
dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080
npm --prefix src/Meridian.Ui/dashboard run dev
pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Development
```

Use `pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Production -BuildOnly` for a Release
host/desktop build that does not require database connectivity. Use `-LaunchMode Production`
without `-BuildOnly` only when the production governance persistence variables are configured:
`MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING` and `MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING`.
Development launch mode sets `DOTNET_ENVIRONMENT=Development`,
`ASPNETCORE_ENVIRONMENT=Development`, and `MERIDIAN_USE_INMEMORY_GOVERNANCE=true` for the
launched processes, then restores the caller's environment.

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
- Legacy hand-authored engineering guides remain source material only unless explicitly linked from canonical lanes.
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

## Legacy Source-Material Index

- [Developer Quick Guides](../../archive/docs/developer/README.md) *(source material for migration only)*
- [Development Guides](../development/README.md) *(source material for migration only)*
- [Desktop Testing Guide](../development/desktop-testing-guide.md) *(source material for migration only)*
- [Architecture Documentation](../architecture/README.md) *(source material; canonicalized through source registry)*
- [Old WPF workflow notes](../development/wpf-implementation-notes.md) *(source material)*

## Canonical Ownership Summary

- Canonical engineering start: this file + nearest module `README.md`.
- Canonical ownership and module map: `docs/source/data/source-modules.yml`, `docs/source/README.md`.
- Canonical roadmap and acceptance: `docs/roadmap/data/*.yml` + generated roadmap outputs.
- Canonical generated engineering docs: outputs under `docs/source/generated/` (do not hand-edit).
