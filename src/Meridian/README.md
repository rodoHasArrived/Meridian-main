---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-HOST
path: src/Meridian
status: active
owner_lane: Runtime Host
last_reviewed: 2026-05-20
---

# src/Meridian

## Purpose

The Meridian host starts the local application, exposes CLI entrypoints, wires configuration, and composes runtime services.

## Layer responsibility

This layer owns process startup and composition. It should delegate business workflows to application services and keep transport or UI-specific behavior out of the host.

## Key folders and files

- `Program.cs` - primary host and CLI entrypoint.
- Composition and startup helpers - dependency injection, configuration, and hosted runtime setup.

## Important workflows

Host changes usually affect command discovery, local run behavior, configuration validation, or the web-hosted workstation path.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-HOST -->
| Roadmap item | Title |
| --- | --- |
| `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W6-LIVE-001` | Live-readiness governance |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-HOST -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
python3 build/python/cli/buildctl.py build --project src/Meridian/Meridian.csproj --configuration Debug --isolation-key codex-host
```

## Change rules

Keep host changes narrow and composition-focused. Do not move business logic, UI projection rules, or provider implementation details into this project.

## Related docs

- `docs/developer/build-test-run.md`
- `docs/HELP.md`
- `docs/source/generated/source-module-index.md`
