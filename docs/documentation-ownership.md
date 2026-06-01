# Documentation Ownership Contract

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-05-30

This contract defines where Meridian documentation belongs during the full rebuild.

## Ownership Model

| Documentation type | Owner location | Rule |
| --- | --- | --- |
| First-run orientation | `docs/start/` | Keep short, current, and command-oriented. |
| Stakeholder/product narrative | `docs/product/` | Explain product direction, capability posture, and roadmap interpretation without duplicating registry truth. |
| Developer and agent workflow | `docs/engineering/` and `docs/ai/` | Route coding work through architecture, module maps, source registry, and AI contracts. |
| Operator procedures | `docs/operators/` | Link to setup, provider, runbook, deployment, troubleshooting, and support procedures. |
| Lookup material | `docs/reference/` | Keep APIs, environment variables, CLI flags, schemas, glossary, and matrices discoverable. |
| Roadmap truth | `docs/roadmap/data/*.yml` | Validate and render; do not hand-author competing durable roadmap truth. |
| Source-module truth | `docs/source/data/*.yml` and registered `src/**/README.md` | Update with source behavior, ownership, validation, TODO, and diagram changes. |
| Generated output | `docs/generated/`, `docs/roadmap/generated/`, `docs/source/generated/`, `docs/ai/generated/` | Update generators or inputs, not emitted files by hand. |
| Historical or superseded material | `archive/docs/` | Preserve useful history with replacement links or explicit archive rationale. |

## Migration Classes

- `canonical`: rewrite or keep active under the new documentation model.
- `source-material`: extract verified facts, then archive.
- `generated`: preserve and update generator/index only.
- `archive`: move to `archive/docs/` with replacement links where useful.
- `delete-candidate`: use only for untracked generated junk or explicitly approved removals.

## Source-Of-Truth Rules

- Do not keep two active docs claiming the same durable truth.
- Prefer registry data and generated views for roadmap and source-module status.
- Prefer root shims for `README.md`, `AGENTS.md`, and `CLAUDE.md`; avoid long duplicated command catalogs.
- Keep `docs/README.md` as the canonical documentation front door.
- Keep AI workflow rules synchronized through `docs/ai/assistant-workflow-contract.md` and assistant-specific indexes.

## Review Rules

Before adding or moving docs:

1. Choose the owning lane from the table above.
2. Check whether a registry or generator already owns the truth.
3. Add or update the nearest `README.md` only when discoverability changes.
4. Archive superseded material instead of deleting it when it has historical or evidence value.
5. Run the narrowest docs validation command for the touched surface.

## Structure Enforcement

`build/scripts/docs/validate-docs-structure.py` enforces the staged rebuild model:

- Canonical top-level docs folders are `ai`, `engineering`, `generated`, `operators`, `product`, `reference`, `roadmap`, `source`, and `start`.
- Known legacy folders are allowed during migration, but the validator reports them as migration warnings.
- Unexpected new top-level folders are errors. Add new active documentation to a canonical lane or archive historical material under `archive/docs/`.
- The validator still warns on missing lifecycle fields so legacy files can be cleaned up in batches instead of blocking unrelated slices.
