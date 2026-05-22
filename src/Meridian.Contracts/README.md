---
module_id: SRC-CONTRACTS
owner: Meridian
status: active
last_verified: 2026-05-21
---

# Meridian Contracts

## Module Purpose
Shared contracts and DTOs used across host, services, desktop, and dashboard surfaces.

## Ownership and Runtime
- Owner: Meridian
- Runtime lane: Meridian Contracts

## Dependencies and Integrations
- Define stable transport payloads and shared schema objects.
- Provide compatibility-safe contracts for inter-module integration.
- Dependency: `Consumers in host, UI services, shared UI read models, and clients.`
- Dependency: `Serialization/runtime libraries required for DTO transport.`

## Operational Notes
Treat additive and breaking changes as cross-module compatibility work.
Chief of Staff orchestration payloads for workstation surfaces live under `Workstation/ChiefOfStaffDtos.cs`.

<!-- GENERATED:MODULE_OVERVIEW BEGIN -->
Generated overview content is maintained by documentation automation.
<!-- GENERATED:MODULE_OVERVIEW END -->
