---
module_id: SRC-HOST
owner: Meridian
status: active
last_verified: 2026-05-21
---

# Meridian Host

## Module Purpose
Composition root and runtime host for Meridian service startup and CLI entry points.

## Ownership and Runtime
- Owner: Meridian
- Runtime lane: Meridian Host

## Dependencies and Integrations
- Bootstrap dependency injection, configuration, and host lifetime.
- Route CLI command execution and runtime hosting orchestration.
- Dependency: `src/Meridian.Application`
- Dependency: `src/Meridian.Contracts`

## Operational Notes
Keep startup orchestration and process-host concerns isolated from domain/application logic.

<!-- GENERATED:MODULE_OVERVIEW BEGIN -->
Generated overview content is maintained by documentation automation.
<!-- GENERATED:MODULE_OVERVIEW END -->
