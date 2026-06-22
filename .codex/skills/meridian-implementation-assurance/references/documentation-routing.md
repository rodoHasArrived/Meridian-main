# Documentation Routing Guide

Use this guide when implementation assurance needs a documentation update. Prefer the most
specific existing document. Create a new document only when no existing home covers the behavior.

## Current Audience Paths

- `docs/start/`
  - Use for first-run setup, quickstart flows, and entrypoint instructions.
- `docs/product/`
  - Use for product scope, stakeholder framing, and user-facing capability context.
- `docs/engineering/`
  - Use for developer workflows, validation commands, build/test guidance, and implementation
    conventions.
- `docs/operators/`
  - Use for operator workflows, runbooks, provider setup, close support, recovery, and day-to-day
    system operation.
- `docs/ai/`
  - Use for AI-agent operating instructions, Codex/Claude/Copilot workflow guidance, prompt
    conventions, skill catalogs, and eval process docs.
- `docs/roadmap/`
  - Use for roadmap direction only through the registry or established generated-doc workflow.
- `docs/source/`
  - Use for source-module READMEs, source registry ownership, stale-doc tracking, and source/docs
    hash alignment.
- `docs/reference/`
  - Use for stable lookup material such as APIs, configuration, provider matrices, schemas, and
    command reference.

## Specialist Paths

- `docs/architecture/`
  - Use for architecture boundaries, component responsibilities, and system design decisions that
    are not ADR-level.
- `docs/adr/`
  - Use for durable architecture decisions and trade-offs that should be tracked as ADRs.
- `docs/generated/`
  - Use for generated outputs only. Do not hand-author unless the repo workflow explicitly says to.
- `docs/evaluations/`
  - Use for analysis documents, assessments, test/eval reports, and option comparisons.
- Legacy linked areas such as `docs/operations/`, `docs/developer/`, `docs/development/`, and
  `docs/status/` may still contain active linked material. Update an existing legacy doc in place
  when it is the canonical linked home for the topic, but route new operator-facing docs to
  `docs/operators/`.

## If No Documentation Exists

1. Choose the nearest current audience path or specialist path.
2. Add a focused Markdown file with a clear title and scope.
3. Link the new file from the nearest `README.md` or index in that subtree.
4. Mention the new doc path, cross-link path, and why a new file was needed in the final evidence.

## Quality Bar

- State what changed and why.
- Include usage, operational impact, validation, or migration notes when relevant.
- Keep examples aligned with current code names, routes, and paths.
- Remove or revise stale statements in nearby docs when discovered.
- Do not hand-edit generated docs; update the source registry or generator and rerun the narrow
  generator.
