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

### Most common default lanes

```powershell
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
```

### Browser-workstation slices

```powershell
dotnet build Meridian.WebWorkstation.slnf -c Debug --no-restore /p:EnableWindowsTargeting=true /p:UseAppHost=false
```

### Desktop slices

```powershell
dotnet restore tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release --no-restore /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:WindowsPackageType=None
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release --no-restore --filter "Category!=Integration" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
```

## Local Run

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080
npm --prefix src/Meridian.Ui/dashboard run dev
pwsh ./scripts/dev/run-desktop.ps1
```

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
