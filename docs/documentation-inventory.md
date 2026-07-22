# Documentation Inventory

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-07-19

This inventory describes the current `docs/` tree. It is deliberately compact: use it to decide
which folder owns a document, whether the folder is canonical or supporting, and which remaining
lanes are transitional. Detailed rebuild history is preserved in the
[2026-07-19 inventory snapshot](../archive/docs/summaries/documentation-inventory-2026-07-19.md).

## Classification Key

- `canonical`: an active source-of-truth or primary audience lane.
- `supporting`: an active specialist or automation-owned lane reached through a canonical index.
- `transitional`: a compatibility or migration lane retained because active tools, tests, or links
  still depend on it.
- `generated`: content owned by registry data or automation; edit the input or generator.
- `archived`: historical or superseded material under `archive/docs/`.

## Current Folder Map

| Folder | Class | Owner / entrypoint | Current role |
| --- | --- | --- | --- |
| `docs/start/` | canonical | [Start](start/README.md) | Fast setup, launch, and first validation path. |
| `docs/product/` | canonical | [Product](product/README.md) | Product framing, supported posture, and bounded roadmap interpretation. |
| `docs/engineering/` | canonical | [Engineering](engineering/README.md) | Build, test, contribution, and source-change workflow. |
| `docs/operators/` | canonical | [Operators](operators/README.md) | Active setup, deployment, recovery, reconciliation, and support procedures. |
| `docs/reference/` | canonical | [Reference](reference/README.md) | APIs, configuration, schemas, matrices, and stable lookup material. |
| `docs/architecture/` | canonical | [Architecture](architecture/README.md) | MDIF, architecture boundaries, current system design, and design rationale. |
| `docs/domain/` | canonical | [Domain Dictionary](domain/README.md) | Durable business nouns, relationships, rules, and examples. |
| `docs/ai/` | canonical | [AI](ai/README.md) | Assistant contracts, navigation, context packs, prompts, and skills. |
| `docs/roadmap/` | canonical / generated | [Roadmap](roadmap/README.md) | Durable roadmap data in `data/*.yml` and generated views. |
| `docs/source/` | canonical / generated | [Source](source/README.md) | Module registry, source README ownership, hashes, and generated views. |
| `docs/generated/` | generated | [Generated docs](generated/README.md) | Repository-, database-, and automation-generated documentation. |
| `docs/adr/` | supporting | [ADRs](adr/README.md) | Current architecture decisions; older decisions live in the archive. |
| `docs/development/` | supporting | [Development guides](development/README.md) | Detailed implementation guides linked from Engineering. |
| `docs/diagrams/` | supporting / generated | [Diagrams](diagrams/README.md) | Maintained visual assets and generated diagram outputs. |
| `docs/docfx/` | supporting / generated | [DocFX](docfx/README.md) | API-documentation configuration and generated-site guidance. |
| `docs/examples/` | supporting | [Examples](examples/README.md) | Current templates and scaffolds. |
| `docs/integrations/` | supporting | [Integrations](integrations/README.md) | Verified interoperability and third-party integration guidance. |
| `docs/prompts/` | supporting | [Prompts](prompts/README.md) | Maintained prompt catalogs routed through the AI lane. |
| `docs/screenshots/` | supporting / generated | [Screenshots](screenshots/README.md) | Maintained visual evidence and capture manifests. |
| `docs/security/` | supporting | [Security](security/README.md) | Threat model, vulnerability posture, and compliance material. |
| `docs/status/` | supporting / generated | [Status](status/README.md) | Automation-owned reports plus compatibility artifacts still consumed by tooling. |
| `docs/testing/` | supporting | [Testing](testing/README.md) | Scenario-specific acceptance and release-gate references. |
| `docs/operations/` | transitional | [Compatibility index](operations/README.md) | Legacy paths retained only where tests, monitoring, or active links still consume them. |
| `docs/plans/` | transitional | [Plans index](plans/README.md) | Active/tool-consumed planning inputs that have not yet moved to an owning canonical lane. |

## Current Project-State Sources

Do not infer current delivery status from folder names or dated plans. Use this order:

1. `docs/roadmap/data/program-state.yml` and `docs/roadmap/data/roadmap-items.yml`.
2. Generated roadmap views under `docs/roadmap/generated/`.
3. `docs/product/meridian-design-document.md` for the product charter.
4. `docs/product/implementation-todo-list.md` for production-readiness execution.
5. Source READMEs and `docs/source/data/source-modules.yml` for implemented module behavior.

As of the 2026-07-18 program-state snapshot, Evidence Vault productization, statement
reconciliation onboarding, and WPF parity are in progress. Production readiness remains blocked
until the canonical tracker and release evidence close on the same release commit.

## Placement Rules

- Add new audience-facing material to `start`, `product`, `engineering`, `operators`, or
  `reference`.
- Add system design to `architecture`, business vocabulary to `domain`, and assistant guidance to
  `ai`.
- Do not add new durable guidance to `operations` or `plans`; first identify the canonical owner.
- Do not hand-edit generated roadmap, source, status, screenshot, or repository-structure outputs.
- Archive superseded material under the matching `archive/docs/<bucket>/` index and update links.
- Use repository-relative paths in committed documentation; machine-specific checkout paths belong
  in local instructions, not portable docs.

## Remaining Organization Work

1. Retire `docs/operations/` only after monitoring registries, route-consistency tests, and active
   links no longer require its compatibility paths.
2. Move each remaining `docs/plans/` item when its owning canonical lane or tooling contract is
   ready; do not bulk-move tool inputs.
3. Continue adding lifecycle metadata to touched hand-authored documents rather than formatting the
   entire tree in one churn-heavy pass.
4. Run the structure validator and link checker after every folder move.

## Historical Rebuild Evidence

- [Detailed inventory snapshot — 2026-07-19](../archive/docs/summaries/documentation-inventory-2026-07-19.md)
- [Documentation consolidation inventory — 2026-05-17](../archive/docs/summaries/documentation-consolidation-inventory-2026-05-17.md)
- [Documentation archive](../archive/docs/README.md)
