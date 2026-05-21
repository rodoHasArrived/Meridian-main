# Approach B+ v2: Structured Roadmap Registry + Source Documentation Mesh + Versioned Schemas + Deterministic Generated Docs + AI Coding Sync + CI Drift Gates

## Purpose

This document defines the governed operating contract for Approach B+ v2. It is the phase-sequencing and gate policy for the PR0–PR9 rollout.

## Non-Negotiable Operating Rules

1. Schema/version contracts are mandatory for all registries.
2. Generation is deterministic (no wall-clock, local-machine, locale, or random inputs).
3. Generated docs are never hand-edited outside approved generated blocks.
4. Scope is phase-gated; stop and split PRs when boundaries expand.
5. Source README coverage is required for registered priority modules.
6. TODOs must be structured and ID-linked to registry records.
7. AI coding rules must enforce source/doc/registry synchronization.
8. CI must detect drift and fail loudly.

## Target Architecture

- `docs/roadmap/` for roadmap governance, schemas, data registries, and generated roadmap/status views.
- `docs/source/` for source documentation standards, schemas, module/todo registries, and generated source-doc views.
- `docs/architecture/diagrams/` plus generated `docs/architecture/assets/` for deterministic architecture visuals.
- `docs/status/assets/` for roadmap/status visuals generated from structured inputs.
- `src/**/README.md` as local module guides with generated roadmap traceability and TODO blocks.
- `tools/roadmap/*` and `tools/source_docs/*` for validators, renderers, migrations, and TODO scanning.

## Schema + Versioning Contract

All registry files must include:

- `schema.id`
- `schema.version` (semantic version string)
- `schema.minimum_renderer_version`

Version policy:

- Patch: clarifications only.
- Minor: backward-compatible additions (optional fields, enum expansions).
- Major: breaking schema or semantic changes.

Renderer compatibility rule:

- Renderers and validators must declare supported schema majors and fail on unknown majors.

Migration rule:

- Major schema bumps require migration scripts in `tools/.../migrations/`.
- Major schema bumps require migration docs in `docs/roadmap/schema-migrations/` and/or corresponding `docs/source` migration paths.

## Deterministic Generation Contract

Generated outputs must be a pure function of:

- registry inputs,
- renderer code,
- renderer version,
- pinned templates/config.

Required guarantees:

- Stable sort orders per record type.
- Stable formatting (UTF-8 LF, fixed table columns, normalized empty values/dates/paths).
- Deterministic SVG IDs and status colors.
- Generated file headers with generator/schema/input metadata.
- Generated manifests with input/output hashes.
- Render-twice determinism verification in CI.

## Source README Contract

Hybrid ownership model:

- Human-authored sections: purpose, layer responsibility, key files, workflows, related docs.
- Generated/validated sections: roadmap traceability, TODO checklist, diagram index (or validated registry links).

For `src/**` behavior/workflow/boundary/validation changes, the nearest module README must be updated or explicitly justified in the PR summary.

## TODO Contract

- Structured TODOs live in `docs/source/data/source-todos.yml`.
- Inline code TODOs must reference valid roadmap/TODO IDs.
- Validation fails TODOs without IDs, unknown IDs, or closed TODOs still present inline.

## Global PR Gates

Every PR in this initiative must pass:

1. Scope gate
2. Schema gate
3. Determinism gate
4. Drift gate (`git diff --exit-code` after generation)
5. README coverage gate
6. TODO gate
7. Evidence gate for accepted/done claims
8. AI sync gate
9. Narrow validation command reporting gate

## Stop Conditions

Split/stop immediately when:

- a schema change is needed mid-PR,
- generator output is nondeterministic,
- implementation and governance changes are mixed in one PR,
- generated docs were manually edited,
- done-state evidence is missing,
- new WPF product scope appears outside retained support,
- new mobile/client scope appears,
- AI guidance diverges across providers.

## Execution Plan (PR0–PR9)

- **PR0 — Governance contract only**  
  Add governance and policy docs only (schema-versioning, generated-doc policy, source doc standards/templates).  
  DoD: policies complete; no registries/renderers/generated outputs yet.

- **PR1 — Schemas + validator skeleton**  
  Add v1 schema files and validator skeletons with tiny fixtures.  
  DoD: fixtures validate; missing required fields and unsupported majors fail.

- **PR2 — Roadmap registry seed**  
  Add initial roadmap registries with limited high-level items (W1–W6 anchors).  
  DoD: validation passes; exit criteria + evidence posture present; no done without evidence.

- **PR3 — Source module registry seed**  
  Add source module/TODO/diagram/README-coverage registries and `src/README.md` rules.  
  DoD: priority module records complete and aligned with architecture boundaries.

- **PR4 — Source READMEs (core backend)**  
  Create/upgrade READMEs for host/application/contracts modules.  
  DoD: required front matter + sections + generated block markers present.

- **PR5 — Source READMEs (UI + retained WPF)**  
  Create/upgrade READMEs for `Ui/dashboard/Ui.Services/Ui.Shared/Wpf`.  
  DoD: browser-first dashboard role explicit; WPF retained-support scope explicit.

- **PR6 — Deterministic Markdown renderers**  
  Implement roadmap + source-doc markdown renderers and generated manifests.  
  DoD: deterministic headers, stable ordering, render-twice parity, no post-render drift.

- **PR7 — Diagram/SVG rendering**  
  Implement roadmap/source diagram renderers and deterministic SVG generation.  
  DoD: deterministic IDs/colors; pinned Mermaid only where required; reproducible regeneration.

- **PR8 — AI coding instruction sync**  
  Update shared AI workflow contract + mirrors + path-specific instructions for `src/**`.  
  DoD: shared-policy-first synchronization rules; no provider-policy divergence.

- **PR9 — CI drift enforcement**  
  Add CI workflow for validators, renderers, determinism checks, and drift checks.  
  DoD: CI fails on schema/readme/todo/generation/diagram drift or nondeterminism.

## Initiative Completion Checklist

Approach B+ v2 is complete only when all are true:

- Roadmap and source registries are schema-versioned and validated.
- Schema-major compatibility and migration paths are enforced.
- Generated docs and diagrams are deterministic and reproducible.
- Priority source modules have README coverage with validated structure.
- TODO workflow is registry-backed and ID-enforced.
- AI workflow contract enforces source/doc/registry synchronization.
- CI enforces drift gates end-to-end.
