---
id: meridian-host
title: Meridian Host
layer: host
state: active
canonical_path: src/Meridian
architecture_boundary: composition-root-and-runtime-host
roadmap_anchor: W2-paper-trading-cockpit
---

# Meridian Host

## Purpose
Composition root and runtime host for Meridian service startup and CLI entry points.

## Responsibilities
- Bootstrap dependency injection, configuration, and host lifetime.
- Route CLI command execution and runtime hosting orchestration.

## Key dependencies
- `src/Meridian.Application`
- `src/Meridian.Contracts`

## Notes
Keep startup orchestration and process-host concerns isolated from domain/application logic.
