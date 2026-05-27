# Roadmap Tooling Guide

**Last Updated:** 2026-05-21

This folder contains scripts that help keep roadmap work scoped, valid, and reproducible.

## What each script does (plain language)

- `validate_roadmap.py`  
  Checks roadmap data rules. By default, it validates canonical enum fixtures.  
  With `--roadmap`, it validates the real roadmap evidence YAML file.

- `render_roadmap_docs.py`  
  Normalizes roadmap YAML into deterministic output (stable ordering/formatting), useful for repeatable automation outputs.

- `enforce_phase_scope.py`  
  Enforces roadmap PR phase boundaries (for example PR0, PR1, ...), based on declared phase and changed file paths.

## Common commands

Run from repository root:

```bash
# Validate fixture expectations (default mode)
python3 tools/roadmap/validate_roadmap.py

# Validate the active roadmap evidence file
python3 tools/roadmap/validate_roadmap.py --roadmap docs/roadmap/data/roadmap-items.yml

# Render deterministic roadmap output
python3 tools/roadmap/render_roadmap_docs.py docs/roadmap/data/roadmap-items.yml /tmp/roadmap.normalized.yml

# Check phase scope locally against current branch diff
python3 tools/roadmap/enforce_phase_scope.py --phase PR1 --base-ref origin/main --head-ref HEAD
```

## When to use these scripts

- You changed roadmap governance docs or workflow policy.
- You edited `docs/roadmap/data/roadmap-items.yml`.
- You need to confirm a PR only touches files allowed for its declared phase.

## Related documentation

- `docs/roadmap/roadmap-governance.md`
- `docs/roadmap/data/roadmap-items.yml`
