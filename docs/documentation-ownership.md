# Documentation Ownership Contract

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-07-19

This contract defines where Meridian documentation belongs in the current model and during the
remaining compatibility-lane cleanup.

## Ownership Model

| Documentation type | Owner location | Rule |
| --- | --- | --- |
| First-run orientation | `docs/start/` | Keep short, current, and command-oriented. |
| Stakeholder/product narrative | `docs/product/` | Explain product direction, capability posture, and roadmap interpretation without duplicating registry truth. |
| Developer and agent workflow | `docs/engineering/` and `docs/ai/` | Route coding work through architecture, module maps, source registry, and AI contracts. |
| Architecture and domain guidance | `docs/architecture/`, `docs/domain/`, and `docs/adr/` | Keep current system design, MDIF context, business vocabulary, and active decisions explicit and linked from Engineering. |
| Operator procedures | `docs/operators/` | Link to setup, provider, runbook, deployment, troubleshooting, and support procedures. |
| Lookup material | `docs/reference/` | Keep APIs, environment variables, CLI flags, schemas, glossary, and matrices discoverable. |
| Roadmap truth | `docs/roadmap/data/*.yml` | Validate and render; do not hand-author competing durable roadmap truth. |
| Source-module truth | `docs/source/data/*.yml` and registered `src/**/README.md` | Update with source behavior, ownership, validation, TODO, and diagram changes. |
| Generated output | `docs/generated/`, `docs/roadmap/generated/`, `docs/source/generated/`, `docs/ai/generated/` | Update generators or inputs, not emitted files by hand. |
| Automation-owned status output | `docs/status/` | Keep generated reports and compatibility artifacts at the paths consumed by tooling; do not create competing roadmap truth. |
| Detailed supporting guidance | `docs/development/`, `docs/security/`, `docs/testing/`, `docs/integrations/`, and related specialist folders | Link from the canonical owner and avoid duplicating current commands or product status. |
| Transitional compatibility paths | `docs/operations/` and `docs/plans/` | Retain only when active code, tests, tooling, or strong links require the path; do not add new durable guidance. |
| Historical or superseded material | `archive/docs/` | Preserve useful history with replacement links or explicit archive rationale. |

## Placement Classes

- `canonical`: active audience, architecture, domain, AI, roadmap, source, or generated-system owner.
- `supporting`: active specialist material linked from a canonical owner.
- `transitional`: compatibility content retained because active tools, tests, or strong links still
  require its path; do not add new durable guidance.
- `source-material`: historical facts awaiting extraction into a canonical or supporting owner.
- `generated`: preserve the output and update its registry input or generator only.
- `archived`: retain under `archive/docs/` with replacement links where useful.
- `delete-candidate`: use only for untracked generated junk or explicitly approved removals.

## Source-Of-Truth Rules

- Do not keep two active docs claiming the same durable truth.
- Prefer registry data and generated views for roadmap and source-module status.
- Prefer root shims for `README.md`, `AGENTS.md`, and `CLAUDE.md`; avoid long duplicated command catalogs.
- Keep `docs/README.md` as the canonical documentation front door.
- Keep AI workflow rules synchronized through `docs/ai/assistant-workflow-contract.md` and assistant-specific indexes.
- Use repository-relative paths in committed docs. Machine-specific checkout paths belong in local
  environment instructions, not portable project guidance.

## Review Rules

Before adding or moving docs:

1. Choose the owning lane from the table above.
2. Check whether a registry or generator already owns the truth.
3. Add or update the nearest `README.md` only when discoverability changes.
4. Archive superseded material instead of deleting it when it has historical or evidence value.
5. Run the narrowest docs validation command for the touched surface.

## Structure Enforcement

`build/scripts/docs/validate-docs-structure.py` enforces the current documentation model:

- Canonical top-level docs folders are `ai`, `architecture`, `domain`, `engineering`, `generated`,
  `operators`, `product`, `reference`, `roadmap`, `source`, and `start`.
- Supporting folders are allowed without migration warnings when their README routes readers to the
  canonical owner and explains any automation contract.
- `operations` and `plans` are transitional and continue to warn until their strong references and
  tooling contracts are retired or moved.
- Removed or unexpected top-level folders are errors. Add active documentation to a canonical or
  supporting lane, or archive historical material under `archive/docs/`.
- The validator still warns on missing lifecycle fields so legacy files can be cleaned up in batches instead of blocking unrelated slices.
