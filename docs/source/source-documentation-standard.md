# Source Documentation Standard

## Purpose

This standard defines required documentation artifacts for source modules and the lifecycle transitions that affect module ownership, pathing, and roadmap traceability.

## Core Artifacts

Every source module must be represented in:

- `docs/source/data/source-modules.yml`
- `docs/source/data/source-readme-coverage.yml`
- The owning `src/**/README.md` file(s)

## README Template Contract (Mandatory)

Every README referenced by `docs/source/data/source-readme-coverage.yml` must include the following contract exactly.

### 1) Front matter keys (YAML)

A YAML front matter block must exist at the top of the file, delimited by `---` and containing:

- `module_id`
- `owner`
- `status`
- `last_verified`

### 2) Required section headings (exact `##` headings)

The README body must include all of the following headings exactly:

- `## Module Purpose`
- `## Ownership and Runtime`
- `## Dependencies and Integrations`
- `## Operational Notes`

### 3) Generated block markers (exact strings)

The README body must include the generated overview block markers exactly as shown below and in this order:

- `<!-- GENERATED:MODULE_OVERVIEW BEGIN -->`
- `<!-- GENERATED:MODULE_OVERVIEW END -->`

## Lifecycle Transition Contract

`docs/source/data/source-readme-coverage.yml` must declare transition records using these values:

- `added`
- `moved`
- `split`
- `merged`
- `deprecated`
- `archived`

Each transition record must include roadmap linkage fields:

- `roadmap.id` (stable plan/workstream identifier)
- `roadmap.url` (link to the active plan/status artifact)
- `roadmap.status` (`planned`, `in-progress`, `completed`, `archived`)

### Required updates by transition type

| Transition | `source-modules.yml` | `source-readme-coverage.yml` | `src/**/README.md` | Roadmap linkage |
|---|---|---|---|---|
| `added` | Add new unique module entry with canonical path. | Add new coverage entry with `exists=true` and initial transition. | Add/expand module section and responsibilities. | Add roadmap link for the owning workstream. |
| `moved` | Update canonical path and preserve module ID. | Append transition with `from_path` and `to_path`; mark old path superseded. | Update old and new README references to avoid stale paths. | Keep same roadmap ID, update status/note for move execution. |
| `split` | Keep origin entry and add child module IDs referencing `split_from`. | Record origin `split` transition and child `added` transitions. | Update origin README scope and add child README ownership notes. | Link all child modules to the same or successor roadmap item. |
| `merged` | Mark source module IDs merged into target; keep target active path. | Add `merged` transitions for source IDs with `merged_into`. | Remove duplicated sections from source README and consolidate in target README. | Update roadmap status for merge completion and consolidation scope. |
| `deprecated` | Mark module state as deprecated with successor or sunset date. | Add `deprecated` transition with rationale and retirement milestone. | Add deprecation banner/notes and migration destination. | Set roadmap status to in-progress/completed for retirement plan. |
| `archived` | Move module state to archived and lock canonical path/history. | Add `archived` transition and reference archive location. | Update active README to point to archive; archive README if moved. | Set roadmap status to archived/completed with closure evidence. |

## Validator Expectations

`tools/source_docs/validate_source_readmes.py` enforces:

- canonical module path existence (`source-modules.yml`)
- unique module IDs (`source-modules.yml`)
- stale README path detection after moves/renames (`source-readme-coverage.yml` transitions)

## Schema evolution and compatibility

Source-doc schema validation is intentionally strict for registry and renderer safety.

- **Major version bump** is required for breaking schema changes (required-field removals/renames, enum contractions, or semantic redefinitions).
- **Minor-compatible updates** in the same major schema may add optional fields only when explicitly documented, or via controlled `extensions` keys (`x-*` naming).
- **Migration path trigger**: when stricter validation introduces new "missing required field" or "unexpected field" failures for existing source-doc entries, include a same-PR migration plan and data backfill/update before adopting the validator rule in CI.
- **Behavior alignment**: validator behavior should mirror schema posture—required structural fields must exist, and untracked fields must fail fast.

## Deterministic Rendering Rules

`tools/source_docs/render_source_docs.py` is the canonical renderer for `docs/source/data/*.yml`.

Normalization policy:

- Unicode normalization: **NFC** on all string keys/values.
- Newline policy: **LF (`\n`)** for all generated files.
- Date formatting: strict **`YYYY-MM-DD`** when an ISO date is detected.
- Key ordering: lexicographic order after normalized key comparison.
- Output ordering: source files are rendered in sorted filename order.

Generated outputs are written to:

- `docs/generated/source/*.json`
- `docs/generated/source/*.normalized.yml`
- `docs/generated/source/diagrams/*.mmd`
- `docs/generated/source/render-manifest.json`

Run reproducibility gate locally:

```bash
python3 -m pip install -r tools/source_docs/requirements-render.txt
python3 tools/source_docs/render_source_docs.py
python3 tools/source_docs/check_source_determinism.py
```
