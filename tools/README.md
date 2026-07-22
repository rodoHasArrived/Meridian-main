# Tools Directory Guide

**Last Updated:** 2026-05-23

This folder contains small utility scripts used to keep Meridian documentation consistent and machine-checkable.

If you are new to the project, start here instead of opening individual Python files first.

## Quick map

- `tools/roadmap/`  
  Scripts for roadmap governance checks and roadmap-data validation/rendering.
- `tools/source_docs/`  
  Scripts for source-module README coverage checks and deterministic generated source-doc outputs.
- `tools/schema_control/`
  PostgreSQL migration auditing, `pg_catalog` extraction, data-object cataloguing, policy checks,
  and deterministic schema documentation.

## Which folder should I use?

- Use **`tools/roadmap/`** if you are working on roadmap phases, roadmap evidence files, or roadmap governance checks.
- Use **`tools/source_docs/`** if you are working on source-module docs under `docs/source/` and generated source docs under `docs/generated/source/`.
- Use **`tools/schema_control/`** when adding or reviewing PostgreSQL migrations, database policies,
  public DTOs/data contracts, or generated database diagrams.

## Fast "just run it" commands

From the repository root:

```bash
# Roadmap: validate canonical fixture enums
python3 tools/roadmap/validate_roadmap.py

# Roadmap: validate the real roadmap data file
python3 tools/roadmap/validate_roadmap.py --roadmap docs/roadmap/data/roadmap-items.yml

# Source docs: validate source module + README coverage metadata
python3 tools/source_docs/validate_source_readmes.py

# Source docs: regenerate deterministic outputs
python3 tools/source_docs/render_source_docs.py

# Source docs: verify deterministic output hashes and repeatability
python3 tools/source_docs/check_source_determinism.py

# PostgreSQL schema control: run migration safety checks without a database
python3 build/scripts/schema-control.py inventory --base-ref origin/main
```

## Learn more

- `tools/roadmap/README.md`
- `tools/source_docs/README.md`
- `tools/schema_control/README.md`
- `docs/roadmap/roadmap-governance.md`
- `docs/source/source-documentation-standard.md`
