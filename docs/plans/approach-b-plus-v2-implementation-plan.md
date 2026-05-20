# Approach B+ v2 Implementation Plan

## Objective

Implement **Approach B+ v2** as a governed documentation system where:

- Structured registries are the source of truth.
- Generated docs are deterministic, reproducible views.
- Source READMEs are hybrid human + generated maintenance guides.
- AI workflows and CI enforce synchronization and prevent drift.

This plan is sequenced as PR0–PR9 with strict scope boundaries, gates, and stop conditions.

## Non-Negotiable Operating Rules

1. **Schema/version contracts are mandatory** for all registries.
2. **Generation is deterministic** (no wall-clock/local-machine/random inputs).
3. **Generated docs are never hand-edited** outside approved generated blocks.
4. **Scope is phase-gated**; stop and split PRs when boundaries expand.
5. **Source README coverage is required** for registered priority modules.
6. **TODOs must be structured and ID-linked** to registry records.
7. **AI coding rules must enforce source/doc/registry sync**.
8. **CI must detect drift and fail loudly**.

## Target Architecture

- `docs/roadmap/` for roadmap governance, schemas, data registries, and generated roadmap/status views.
- `docs/source/` for source documentation standards, schemas, module/todo registries, and generated source-doc views.
- `docs/architecture/diagrams` + generated `docs/architecture/assets` for deterministic architecture visuals.
- `docs/status/assets` for roadmap/status visuals generated from structured inputs.
- `src/**/README.md` as local module guides with generated roadmap traceability and TODO blocks.
- `tools/roadmap/*` and `tools/source_docs/*` for validators, renderers, migrations, and TODO scanning.

## Schema + Versioning Contract

All registry files must include:

- `schema.id`
- `schema.version` (semantic version string)
- `schema.minimum_renderer_version`

### Version change policy

- Patch: clarifications only.
- Minor: backward-compatible additions (e.g., optional field, enum expansion).
- Major: breaking schema or semantic changes.

### Renderer compatibility rule

Renderers/validators must declare supported schema majors and fail on unknown majors.

### Migration rule

Major schema bumps require:

- migration script(s) under `tools/.../migrations/`
- migration documentation under `docs/roadmap/schema-migrations/` (and/or corresponding source-docs path)

## Deterministic Generation Contract

Generated outputs must be a pure function of:

- registry inputs,
- renderer code,
- renderer version,
- pinned templates/config.

### Required guarantees

- Stable sort orders per record type.
- Stable formatting (UTF-8 LF, fixed table columns, normalized empty values/dates/paths).
- Deterministic SVG IDs and status colors.
- Generated file headers with generator/schema/input metadata.
- Generated manifests with input/output hashes.
- Render-twice determinism verification in CI.

## Source README Contract

Use hybrid ownership:

- Human-authored sections: purpose, layer responsibility, key files, workflows, related docs.
- Generated/validated sections: roadmap traceability, TODO checklist, diagram index (or validated registry links).

For `src/**` behavior/workflow/boundary/validation changes, nearest module README must be updated or explicitly justified in PR summary.

## TODO Contract

- TODOs must live in structured registry (`docs/source/data/source-todos.yml`).
- Inline code TODOs must reference valid roadmap/TODO IDs.
- Validation must fail TODOs without IDs, with unknown IDs, or closed TODOs still present inline.

## Global PR Gates

Every PR in this initiative must pass:

- Scope gate
- Schema gate
- Determinism gate
- Drift gate (`git diff --exit-code` after generation)
- README coverage gate
- TODO gate
- Evidence gate for accepted/done claims
- AI sync gate
- Narrow validation command reporting gate

## Stop Conditions

Split/stop immediately when:

- schema change is needed mid-PR,
- generator output is nondeterministic,
- implementation + governance changes mix in one PR,
- generated docs were manually edited,
- done-state evidence is missing,
- new WPF product scope appears outside retained support,
- new mobile/client scope appears,
- AI guidance diverges across providers.

## Execution Plan (PR0–PR9)

### PR0 — Governance contract only

Add governance/policy docs only (`schema-versioning`, generated-doc policy, source doc standards/templates).

**DoD:** policies complete, no registries/renderers/generated outputs yet.

### PR1 — Schemas + validator skeleton

Add v1 schema files and validator skeletons with tiny fixtures.

**DoD:** fixtures validate; missing required fields and unsupported majors fail.

### PR2 — Roadmap registry seed

Add initial roadmap data registries with limited high-level items (W1–W6 anchors).

**DoD:** validation passes; exit criteria + evidence posture present; no done without evidence.

### PR3 — Source module registry seed

Add source module/todo/diagram/readme-coverage registries + `src/README.md` rules.

**DoD:** priority module records complete and aligned with architecture boundaries.

### PR4 — Source READMEs (core backend)

Create/upgrade READMEs for host/application/contracts modules.

**DoD:** required front matter + sections + generated block markers present.

### PR5 — Source READMEs (UI + retained WPF)

Create/upgrade READMEs for UI/dashboard/ui-services/ui-shared/WPF.

**DoD:** browser-first dashboard role explicit; WPF retained-support scope explicit.

### PR6 — Deterministic Markdown renderers

Implement roadmap + source-doc markdown renderers and generated manifests.

**DoD:** deterministic headers, stable ordering, render-twice parity, no post-render diff drift.

### PR7 — Diagram/SVG rendering

Implement roadmap/source diagram renderers and deterministic SVG generation.

**DoD:** deterministic IDs/colors; pinned Mermaid only where required; reproducible regeneration.

### PR8 — AI coding instruction sync

Update shared AI workflow contract + mirrors + path-specific instructions for `src/**`.

**DoD:** shared-policy-first synchronization rules; no provider-policy divergence.

### PR9 — CI drift enforcement

Add CI workflow for validators, renderers, determinism checks, and drift checks.

**DoD:** CI fails on schema/readme/todo/generation/diagram drift or nondeterminism.

## Initiative Completion Checklist

Approach B+ v2 is complete only when all are true:

- Roadmap and source registries are schema-versioned and validated.
- Schema-major compatibility and migration paths are enforced.
- Generated docs/diagrams are deterministic and reproducible.
- Priority source modules have README coverage with validated structure.
- TODO workflow is registry-backed and ID-enforced.
- AI workflow contract enforces source/doc/registry synchronization.
- CI enforces drift gates end-to-end.

## Naming

Use this canonical initiative name:

**Approach B+ v2: Structured Roadmap Registry + Source Documentation Mesh + Versioned Schemas + Deterministic Generated Docs + AI Coding Sync + CI Drift Gates**.
