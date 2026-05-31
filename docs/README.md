# Meridian Documentation

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-05-30

This is the canonical front door for Meridian documentation. The active documentation model is intentionally smaller than the historical tree: start from an audience path, follow the canonical source, and treat older planning, audit, evaluation, or status files as source material unless they are linked from this page.

Current local project path: `D:\Meridian-main`.

## Start By Audience

| Audience | Start here | Use when |
| --- | --- | --- |
| Developers and agents | [Engineering](engineering/README.md) | Build, test, architecture, module ownership, source docs, WPF/browser workstation rules, and validation lanes. |
| Stakeholders | [Product](product/README.md) | Product narrative, capability status, roadmap interpretation, and evidence-backed investment-operations framing. |
| Operators | [Operators](operators/README.md) | Provider setup, workstation launch, runbooks, deployment, troubleshooting, and support artifacts. |
| First-time contributors | [Start](start/README.md) | Fast local orientation, setup, launch, and first safe validation commands. |
| AI assistants | [AI](ai/README.md) | Agent workflow contracts, repo navigation, Codex/Claude/Copilot guidance, prompts, skills, and inventory checks. |

## Canonical Sources

| Topic | Canonical source |
| --- | --- |
| Repository entrypoint | [../README.md](../README.md) |
| Documentation ownership and migration rules | [Documentation Ownership Contract](documentation-ownership.md) |
| Full documentation rebuild inventory | [Documentation Inventory](documentation-inventory.md) |
| Current product direction | [Product](product/README.md) and [Roadmap Registry](roadmap/README.md) |
| Stakeholder design charter (active) | [Meridian Design Document (Draft v1.0)](product/meridian-design-document.md) |
| Roadmap registry | [Roadmap Registry](roadmap/README.md) and `docs/roadmap/data/*.yml` |
| Source module registry | [Source Documentation Mesh](source/README.md) and `docs/source/data/*.yml` |
| Architecture and module boundaries | [Engineering](engineering/README.md), [Project Structure](architecture/project-structure.md), and [Module Map](architecture/module-map.md) |
| Build, test, and run commands | [Engineering](engineering/README.md), [Start](start/README.md), and [HELP](HELP.md) |
| Generated documentation policy | [Generated Documentation](generated/README.md) |
| AI workflow policy | [Provider-Agnostic AI Development Contract](ai/assistant-workflow-contract.md) |

## Active Documentation Model

- `start/` is the first-run and fastest-orientation lane.
- `product/` is the stakeholder narrative and capability-status lane.
- `engineering/` is the developer and coding-agent lane.
- `operators/` is the runbook, setup, troubleshooting, and support lane.
- `reference/` remains the lookup lane for API, environment, schema, and glossary material.
- `ai/`, `roadmap/`, `source/`, and `generated/` remain specialized controlled documentation systems.
- `archive/docs/` is the destination for superseded plans, stale audits, old status snapshots, historical experiments, and one-off brainstorms.

The older folders (`developer/`, `development/`, `plans/`, `status/`, `operations/`, `evaluations/`, `audits/`, and related areas) are active only where linked from the canonical sources above. During the rebuild, they should be treated as migration inputs, not as the desired final information architecture.

## Generated And Registry-Owned Content

Do not hand-edit generated docs or registry views. Update the source registry or generator, then rerun the narrow generation lane:

```bash
python build/scripts/docs/validate-roadmap-registry.py --summary
python build/scripts/docs/render-roadmap-docs.py --summary
python build/scripts/docs/validate-source-readmes.py --summary
python build/scripts/docs/validate-doc-hashes.py --summary
```

## Archive Policy

Archive rather than delete when a document has historical value, stale evidence, superseded roadmap interpretation, or useful implementation context. Each archived batch should include a replacement link or an explicit reason when no active replacement exists.

## Migration Status

- `docs/audits/` and `docs/evaluations/` are now source-material indexes. Use them for historical context and extraction work, not as canonical destinations for new guidance.
- `docs/plans/` and `docs/status/` are now controlled migration indexes. Use `docs/product/` for stakeholder interpretation, `docs/roadmap/data/*.yml` for durable roadmap truth, and generated views for rendered registry output.
- `archive/docs/assessments/`, `archive/docs/plans/`, `archive/docs/summaries/`, `archive/docs/migrations/`, and `archive/docs/workflows/` have bucket indexes for staged archive batches.
- High-traffic archived material should keep a short redirect stub at the old path until all active links move to the replacement.

## Rebuild Acceptance Criteria

- Active docs use `D:\Meridian-main` as the canonical local path.
- Active docs do not claim source-of-truth ownership when a registry or generator owns the truth.
- Every active folder has a clear `README.md`.
- High-traffic retired paths either redirect or are linked from an archive index.
- Root `README.md`, `AGENTS.md`, `CLAUDE.md`, and AI indexes route to this documentation model.
