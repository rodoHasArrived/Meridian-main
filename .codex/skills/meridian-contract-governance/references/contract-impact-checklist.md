# Contract Impact Checklist

Use this checklist when a change touches shared DTOs, routes, read models, provider interfaces, or
workstation payloads.

## Required Evidence

- Contract path and owning project.
- Consumer evidence across services, browser workstation, WPF, tests, and docs.
- Serialization impact: source-generated JSON context, route payloads, compatibility shims, or
  persisted snapshots.
- Whether the change is additive, compatibility-preserving, breaking, or removal.

## Compatibility Choices

- Prefer additive fields and tolerant readers for browser consumers and retained WPF compatibility.
- Use adapter shims when a UI surface cannot move in the same change.
- Version route or payload shape only when simultaneous consumer updates are risky.
- Require migration notes when persisted records or retained evidence are affected.

## Output Checks

- Link changed contract to every known consumer category.
- Name tests for happy path, compatibility, and missing/legacy payloads.
- Update docs when route shape, workflow behavior, public DTOs, or provider contracts change.
- Keep residual risk explicit when static search cannot prove runtime coverage.
