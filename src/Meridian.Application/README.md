---
id: meridian-application
title: Meridian Application
layer: application
state: active
canonical_path: src/Meridian.Application
architecture_boundary: orchestrators-commands-and-use-cases
roadmap_anchor: W3-research-to-paper-continuity
---

# Meridian Application

## Purpose
Application-layer use cases, orchestration services, and command handlers.

## Responsibilities
- Coordinate workflows across providers, storage, and execution subsystems.
- Expose command and use-case entry points used by host and APIs.

## Key dependencies
- `src/Meridian.Contracts`
- Infrastructure/provider abstractions consumed via interfaces.

## Notes
Keep orchestration here; avoid leaking transport/UI concerns into this layer.
