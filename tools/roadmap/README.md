# Roadmap Tooling Guide

**Last Updated:** 2026-05-21

This folder contains scripts that help keep roadmap work scoped, valid, and reproducible.

## What each script does (plain language)

- `validate_roadmap.py`  
  Checks roadmap data rules. By default, it validates canonical enum fixtures.  
  With `--roadmap`, it validates the real roadmap evidence YAML file.

- `render_roadmap_docs.py`  
  Normalizes roadmap YAML into deterministic output (stable ordering/formatting), useful for repeatable automation outputs.


## Common commands

Run from repository root:

```bash
# Install the standard parser used by validation and normalization
python3 -m pip install --requirement build/scripts/docs/requirements.txt

# Validate fixture expectations (default mode)
python3 tools/roadmap/validate_roadmap.py

# Validate the active roadmap evidence file
python3 tools/roadmap/validate_roadmap.py --roadmap docs/roadmap/data/roadmap-items.yml

# Render deterministic roadmap output
python3 tools/roadmap/render_roadmap_docs.py docs/roadmap/data/roadmap-items.yml /tmp/roadmap.normalized.yml
```

## When to use these scripts

YAML validation and normalization use the shared strict PyYAML loader. Missing dependencies or
malformed YAML fail with the source path and available line/column information. Normalization
preserves YAML values, quoted colon/hash text, and standard folded or block scalars; it does not
attempt to repair malformed input.

- You changed roadmap governance docs or workflow policy.
- You edited `docs/roadmap/data/roadmap-items.yml`.

## Related documentation

- `docs/roadmap/roadmap-governance.md`
- `docs/roadmap/data/roadmap-items.yml`
