# Generated Documentation

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-05-30

This folder contains documentation generated from code, registries, repository structure, or documentation automation. Do not edit generated outputs by hand unless the file explicitly marks an editable block.

## Generated Families

| Family | Source of truth | Output examples | Maintenance rule |
| --- | --- | --- | --- |
| Repository structure | Repository files and `build/scripts/docs/generate-structure-docs.py` | `repository-structure.md` | Regenerate from script. |
| AI navigation | Repo map inputs and `build/scripts/docs/generate-ai-navigation.py` | `docs/ai/generated/repo-navigation.md`, `docs/ai/generated/repo-navigation.json`, `docs/ai/generated/recent-changes.md` | Regenerate when routing truth changes. |
| Roadmap views | `docs/roadmap/data/*.yml` and roadmap renderers | `docs/roadmap/generated/*` | Edit YAML registry or renderer, then validate/render. |
| Source docs | `docs/source/data/*.yml`, source READMEs, source-doc renderers | `docs/source/generated/*` and generated README blocks | Edit registry/source README, then validate/render. |
| Health and evidence dashboards | Build/test/artifact inputs and dashboard generators | status and generated dashboard markdown/json | Update generator or evidence input. |
| AI inventory and skill reports | `.codex/`, `.claude/`, `.agents/`, `.github/`, scripts, and validators | generated AI reports and inventory outputs | Update watched surface and run inventory checks. |

## Key Scripts

```bash
python build/scripts/docs/generate-structure-docs.py
python build/scripts/docs/generate-ai-navigation.py --summary
python build/scripts/docs/validate-roadmap-registry.py --summary
python build/scripts/docs/render-roadmap-docs.py --summary
python build/scripts/docs/validate-source-readmes.py --summary
python build/scripts/docs/render-source-docs.py --summary
python build/scripts/docs/check-ai-inventory.py --summary
python build/scripts/docs/check-ai-handoff.py --output docs/status/ai-handoff-checklist-report.md
python build/scripts/docs/run-docs-automation.py --profile quick --dry-run
```

## Rebuild Rule

The documentation rebuild keeps generated contracts stable. If a generated page is stale, fix the generator, source registry, or source data. Do not hand-edit emitted files just to make the rebuild look clean.
