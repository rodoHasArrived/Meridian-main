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
- [data/adapter-readiness.yml](data/adapter-readiness.yml) owns the implementation readiness inventory
  for every direct `src/Meridian.Infrastructure/Adapters/` family, including canonical IDs and aliases,
  capability claims, dependencies, risks, degradation behavior, registration, evidence, owner, and next action.

The adapter registry's `registration` list identifies runtime registration paths. Use
`registration: []` for an explicitly excluded family with no runtime registration, retaining its
exclusion source and targeted tests in `evidence`. Catalogued runtime providers require a non-empty
registration list.

## Generated outputs

`build/scripts/docs/render-source-docs.py` writes deterministic views under
`docs/source/generated/` and updates only marked generated blocks in source READMEs.

The [adapter readiness matrix](generated/adapter-readiness-matrix.md) is generated from
`data/adapter-readiness.yml`. Regenerate and validate it from the repository root:

```sh
python build/scripts/docs/render-adapter-readiness.py
python build/scripts/docs/render-source-docs.py --summary
python build/scripts/docs/validate-adapter-readiness.py --summary
```

Run the source-docs renderer after the adapter renderer because
`docs/source/generated/MANIFEST.json` hashes every source registry, including
`data/adapter-readiness.yml`. A registry edit must refresh both the matrix and that shared manifest.

The validator checks the registry against `ProviderCapabilityDescriptorCatalog`, known adapter
types, direct adapter folders, readiness states, and targeted test references. Edit registry inputs
and regenerate the matrix; do not hand-edit its output. This inventory describes source readiness
and does not confer live-provider certification or replace the
[operator validation gates](../reference/provider-validation-matrix.md).

## AI workflow

When editing `src/**`, assistants must read the nearest source README, identify
the module ID, update registry records when ownership or behavior changes, and
report the narrow validation command used.

Source and README hashes use LF-normalized UTF-8 content and ordinal repository-relative POSIX
path ordering, so Windows and Linux checkouts produce the same reviewed baseline. Non-UTF-8 files
retain byte-for-byte hashing. A changed hash after that normalization still requires review;
refresh only the reviewed module entries with `validate-doc-hashes.py --write-module <MODULE_ID>`.
