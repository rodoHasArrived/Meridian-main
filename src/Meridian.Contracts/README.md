---
id: meridian-contracts
title: Meridian Contracts
layer: contracts
state: active
canonical_path: src/Meridian.Contracts
architecture_boundary: shared-dtos-and-transport-contracts
roadmap_anchor: W3-research-to-paper-continuity
---

# Meridian Contracts

## Purpose
Shared contracts and DTOs used across host, services, desktop, and dashboard surfaces.

## Responsibilities
- Define stable transport payloads and shared schema objects.
- Provide compatibility-safe contracts for inter-module integration.

## Key dependencies
- Consumers in host, UI services, shared UI read models, and clients.
- Serialization/runtime libraries required for DTO transport.

## Notes
Treat additive and breaking changes as cross-module compatibility work.
