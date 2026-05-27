# Roadmap And Source Docs Implementation Prompt

Use this prompt when asking an AI assistant to update Meridian source documentation, roadmap registries, TODOs, or generated documentation tooling.

```text
Implement the requested change using the Approach B++ documentation system.

Before editing:
- Read docs/roadmap/README.md and docs/source/README.md.
- Read the nearest registered src/**/README.md for any source files touched.
- Identify relevant module IDs in docs/source/data/source-modules.yml.
- Identify relevant roadmap item IDs in docs/roadmap/data/roadmap-items.yml.

During editing:
- Update source README prose when behavior, validation, module ownership, diagrams, or TODO scope changes.
- Update docs/source/data/source-modules.yml, source-todos.yml, diagram-index.yml, or roadmap data when registry truth changes.
- Do not hand-edit generated docs outside approved generated blocks.
- Avoid cosmetic wording churn unless it fixes broken documentation, canonical naming, accessibility, or contract correctness.

Validate:
- python3 build/scripts/docs/validate-roadmap-registry.py --summary
- python3 build/scripts/docs/validate-source-readmes.py --summary
- python3 build/scripts/docs/scan-source-todos.py --summary
- python3 build/scripts/docs/render-roadmap-docs.py --summary
- python3 build/scripts/docs/render-source-docs.py --summary
- git diff --check
```
