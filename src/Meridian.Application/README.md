---
module_id: SRC-APP
owner: Meridian
status: active
last_verified: 2026-05-21
---

# Meridian Application

## Module Purpose
Application-layer use cases, orchestration services, and command handlers.

## Ownership and Runtime
- Owner: Meridian
- Runtime lane: Meridian Application

## Dependencies and Integrations
- Coordinate workflows across providers, storage, and execution subsystems.
- Expose command and use-case entry points used by host and APIs.
- Dependency: `src/Meridian.Contracts`
- Dependency: `Infrastructure/provider abstractions consumed via interfaces.`

## Operational Notes
Keep orchestration here; avoid leaking transport/UI concerns into this layer.

<!-- GENERATED:MODULE_OVERVIEW BEGIN -->
Generated overview content is maintained by documentation automation.
<!-- GENERATED:MODULE_OVERVIEW END -->
