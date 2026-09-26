# Source Documentation Mesh

**Status:** canonical-registry
**Owner:** core-team
**Reviewed:** 2026-07-19

This directory maps Meridian source code to plain-English module ownership,
roadmap traceability, TODOs, diagrams, and validation commands.

## Source of truth

- `data/source-modules.yml` lists registered code modules.
- `data/source-todos.yml` lists registry-backed implementation follow-ups.
- `data/diagram-index.yml` links diagrams to modules and roadmap items.
- `data/source-readme-coverage.yml` tracks README coverage.

## Generated outputs

`build/scripts/docs/render-source-docs.py` writes deterministic views under
`docs/source/generated/` and updates only marked generated blocks in source READMEs.

## AI workflow

When editing `src/**`, assistants must read the nearest source README, identify
the module ID, update registry records when ownership or behavior changes, and
report the narrow validation command used.

Source and README hashes use LF-normalized UTF-8 content and ordinal repository-relative POSIX
path ordering, so Windows and Linux checkouts produce the same reviewed baseline. Non-UTF-8 files
retain byte-for-byte hashing. A changed hash after that normalization still requires review;
refresh only the reviewed module entries with `validate-doc-hashes.py --write-module <MODULE_ID>`.
