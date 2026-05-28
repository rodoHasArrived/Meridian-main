# Source Documentation Tooling Guide

**Last Updated:** 2026-05-21

This folder contains scripts that keep source-module documentation complete, consistent, and deterministic.

## What each script does (plain language)

- `validate_source_readmes.py`  
  Validates source module metadata and README coverage contracts (required front matter, required headings, and generated-block markers).

- `render_source_docs.py`  
  Converts `docs/source/data/*.yml` into deterministic generated artifacts under `docs/generated/source/` (JSON, normalized YAML, and Mermaid diagrams).

- `check_source_determinism.py`  
  Runs the renderer multiple times and verifies hashes to ensure generation is reproducible.

## Common commands

Run from repository root:

```bash
# Validate source-module and README coverage rules
python3 tools/source_docs/validate_source_readmes.py

# Validate enum fixtures used by this validator
python3 tools/source_docs/validate_source_readmes.py --fixtures-dir tools/source_docs/fixtures

# Regenerate source-doc outputs
python3 tools/source_docs/render_source_docs.py

# Verify deterministic generation behavior + manifest hashes
python3 tools/source_docs/check_source_determinism.py
```

## When to use these scripts

- You changed files under `docs/source/data/`.
- You changed source-module README coverage metadata.
- You changed generated source docs and need to confirm deterministic rendering.

## Related documentation

- `docs/source/source-documentation-standard.md`
- `docs/generated/README.md`
