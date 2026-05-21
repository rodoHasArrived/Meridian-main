# Tools Directory Guide

**Last Updated:** 2026-05-21

This folder contains small utility scripts used to keep Meridian documentation consistent and machine-checkable.

If you are new to the project, start here instead of opening individual Python files first.

## Quick map

- `tools/roadmap/`  
  Scripts for roadmap governance checks and roadmap-data validation/rendering.
- `tools/source_docs/`  
  Scripts for source-module README coverage checks and deterministic generated source-doc outputs.

## Which folder should I use?

- Use **`tools/roadmap/`** if you are working on roadmap phases, roadmap evidence files, or roadmap governance checks.
- Use **`tools/source_docs/`** if you are working on source-module docs under `docs/source/` and generated source docs under `docs/generated/source/`.

## Fast “just run it” commands

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
```

## Learn more

- `tools/roadmap/README.md`
- `tools/source_docs/README.md`
- `docs/roadmap/roadmap-governance.md`
- `docs/source/source-documentation-standard.md`
