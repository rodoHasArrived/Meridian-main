# ADR-017: Modular Operational Monolith

**Status:** accepted
**Owner:** core-team
**Reviewed:** 2026-06-16
**Date:** 2026-06-16

## Problem

Meridian is expanding across portfolio management, fund accounting, reconciliation, reporting, audit evidence, data operations, and workstation UI surfaces. Splitting too early into independently deployed services would increase operational complexity before module boundaries, domain invariants, and accounting/audit workflows are stable.

## Decision

Keep Meridian as a modular operational monolith for the current growth stage. Preserve strong project, module, service, contract, and read-model boundaries inside the solution while optimizing for local consistency, testability, auditability, and shared operational-record workflows.

## Alternatives Considered

- **Microservices first:** rejected because deployment, schema, eventing, and observability overhead would outpace current product maturity.
- **Single undifferentiated application:** rejected because it would blur fund accounting, portfolio, provider, storage, UI, and audit boundaries.
- **Plugin-only architecture:** deferred because Meridian first needs stable core domain contracts and operational-record invariants.

## Consequences

- Modules must remain independently understandable and testable inside the solution.
- Cross-module dependencies should flow through contracts, services, read models, and documented boundaries.
- Domain dictionary and AI context packs become required drift-control tools as the monolith grows.
- Future service extraction remains possible where operational, deployment, or scaling evidence justifies it.
