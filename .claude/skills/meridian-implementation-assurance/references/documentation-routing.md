# Documentation Routing Guide

Use this map to decide where updates belong when implementing code changes.

## Core Rule

Prefer updating an existing document in the most specific section first. Create a new document only if no existing document fits.

## Routing Matrix

- `docs/architecture/`
  - Use for architecture boundaries, component responsibilities, and system design decisions that are not ADR-level.
- `docs/adr/`
  - Use for durable architecture decisions and trade-offs that should be tracked as ADRs.
- `docs/reference/`
  - Use for operator/developer reference material (APIs, data dictionaries, configuration behavior).
- `docs/generated/`
  - Use for generated outputs only. Do not hand-author unless the repo's workflow explicitly requires it.
- `docs/evaluations/`
  - Use for analysis documents, assessments, and option comparisons.
- `docs/ai/`
  - Use for AI-agent operating instructions, prompt conventions, and AI workflow documentation.

## If No Documentation Exists

1. Choose the nearest folder from the routing matrix.
2. Add a focused markdown file with a clear title and scope.
3. Link the new file from the nearest `README.md` or index document in that subtree.
4. In the PR summary, mention the new doc path and why a new file was needed.

## Quality Bar for Doc Updates

- State **what changed** and **why**.
- Include usage/operational impact when relevant.
- Keep examples aligned with current code names and paths.
- Remove or revise stale statements in nearby docs when discovered.
