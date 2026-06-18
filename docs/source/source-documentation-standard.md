# Source Documentation Standard

Source documentation explains code ownership in plain English for maintainers, operators, and AI agents.

## Core artifacts

Every registered source module is represented in:

- `docs/source/data/source-modules.yml`
- `docs/source/data/source-readme-coverage.yml`
- The owning `src/**/README.md`
- `docs/source/data/source-readme-ignore.yml` when a subtree should be skipped by README discovery

## Required source README front matter

```markdown
---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-UI-DASHBOARD
path: src/Meridian.Ui/dashboard
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-05-20
---
```

## Required sections

Each registered source README must include these headings:

- `## Purpose`
- `## Layer responsibility`
- `## Key folders and files`
- `## Important workflows`
- `## Diagrams`
- `## Roadmap traceability`
- `## TODO checklist`
- `## Validation`
- `## Change rules`
- `## Related docs`

Humans own the prose. Generators own only marked blocks under roadmap traceability and TODO checklist.

## Optional conditional sections

Add these sections only when they provide real module-specific value. They are not required by the
validator and should not be added as empty boilerplate.

| Section | Use when |
| --- | --- |
| `## Plans and roadmap` | The module is tied to active plans, waves, or migration work beyond generated traceability. |
| `## End-user value` | The module has user-visible operator, fund-accounting, research, or governance value. |
| `## Benchmarks and performance` | The module owns hot paths, benchmark commands, throughput claims, or latency-sensitive behavior. |
| `## Operational evidence` | The module emits audit artifacts, dashboards, packets, runbooks, or sign-off evidence. |
| `## Security and credentials` | The module handles secrets, credentials, auth boundaries, or secret-safe validation. |
| `## API and contract notes` | The module owns endpoint, DTO, protocol, MCP, provider, or SDK contracts. |
| `## Migration and archive notes` | The module has retained-support, deprecation, migration, or archive context. |

## Generated blocks

```markdown
<!-- source-roadmap-traceability:begin module=SRC-UI-DASHBOARD -->
Generated content.
<!-- source-roadmap-traceability:end -->

<!-- source-todos:begin module=SRC-UI-DASHBOARD -->
Generated content.
<!-- source-todos:end -->
```

Run validation locally:

```bash
python3 build/scripts/docs/mark-stale-docs.py --write --summary
python3 build/scripts/docs/validate-source-readmes.py --summary
python3 build/scripts/docs/render-source-docs.py --summary
python3 build/scripts/docs/validate-doc-hashes.py --summary
```

Run `python3 build/scripts/docs/validate-doc-hashes.py --write-module <MODULE_ID> --summary` after
confirming a specific stale module's source changes and README/registry updates are intentionally
aligned. Use broad `--write --summary` only after reviewing the full source hash baseline.

## Stale-doc workflow

Use the stale-doc marker before updating source documentation after code changes:

```bash
python3 build/scripts/docs/mark-stale-docs.py --write --summary
python3 build/scripts/docs/sync-source-readmes.py --create-missing --stale-only --summary
python3 build/scripts/docs/render-source-docs.py --stale-only --summary
```

`docs/source/generated/stale-docs.json` is the machine-readable work queue. It records the module
ID, path, README, hash-drift reason, and recommended action. Refresh selected
`docs/source/generated/source-hash-manifest.json` entries with `--write-module` only after those
stale module docs have been reviewed or updated.

## Folder-tree README sync

Use `sync-source-readmes.py --tree` when a module needs README coverage below the module root.

```bash
python3 build/scripts/docs/sync-source-readmes.py --tree --max-depth 2 --summary
python3 build/scripts/docs/sync-source-readmes.py --tree --create-missing --max-depth 2 --summary
```

The tree mode is intentionally opt-in. It scans only configured `tree_roots`, skips build outputs,
dependencies, generated assets, and any path matched by `docs/source/data/source-readme-ignore.yml`.
