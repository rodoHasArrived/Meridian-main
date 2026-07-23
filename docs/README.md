# Meridian Documentation

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-07-19

This is the canonical front door for Meridian documentation. Start from an audience or task below,
then follow the named source of truth. Folder names alone do not establish current status: roadmap,
source, and generated content remain registry-owned.

## Current Project Snapshot

The current program-state snapshot is dated **2026-07-18**:

- the W1-W5 baseline, shared Financial Record Explorers, Financial Operations control center,
  statement connector library, and bounded W7 live-readiness governance are complete;
- Evidence Vault productization, statement reconciliation onboarding, and WPF workstation parity
  are in progress;
- production readiness remains blocked until every P0 item in the
  [Implementation and Readiness Tracker](product/implementation-todo-list.md) is complete on the
  same release commit with required release evidence.

Use the [program-state registry](roadmap/data/program-state.yml) and
[generated roadmap summary](roadmap/generated/ROADMAP_SUMMARY.md) for exact status. The summary
above is orientation, not a competing tracker.

## Start By Audience

| Audience | Start here | Use when |
| --- | --- | --- |
| Developers and agents | [Engineering](engineering/README.md) | Build, test, architecture, module ownership, source docs, WPF/browser workstation rules, and validation lanes. |
| Stakeholders | [Product](product/README.md) | Product narrative, capability status, roadmap interpretation, and evidence-backed investment-operations framing. |
| Operators | [Operators](operators/README.md) | Provider setup, workstation launch, runbooks, deployment, troubleshooting, and support artifacts. |
| First-time contributors | [Start](start/README.md) | Fast local orientation, setup, launch, and first safe validation commands. |
| AI assistants | [AI](ai/README.md) | Agent workflow contracts, repo navigation, Codex/Claude/Copilot guidance, prompts, skills, and inventory checks. |
| Domain modelers | [Domain Dictionary](domain/README.md) | Business nouns, relationships, rules, examples, and expansion notes for AI-assisted development. |
| Architects and reviewers | [Architecture](architecture/README.md) | MDIF, system boundaries, architectural decisions, runtime design, and rationale. |

## Canonical Sources

| Topic | Canonical source |
| --- | --- |
| Repository entrypoint | [../README.md](../README.md) |
| Documentation ownership and migration rules | [Documentation Ownership Contract](documentation-ownership.md) |
| Current documentation folder inventory | [Documentation Inventory](documentation-inventory.md) |
| Current delivery and release posture | [Program State](roadmap/data/program-state.yml), [Roadmap Summary](roadmap/generated/ROADMAP_SUMMARY.md), and [Implementation and Readiness Tracker](product/implementation-todo-list.md) |
| Current product direction | [Product](product/README.md) and [Roadmap Registry](roadmap/README.md) |
| Stakeholder design charter (active) | [Meridian Design Document](product/meridian-design-document.md) |
| Roadmap registry | [Roadmap Registry](roadmap/README.md) and `docs/roadmap/data/*.yml` |
| Source module registry | [Source Documentation Mesh](source/README.md) and `docs/source/data/*.yml` |
| Architecture and module boundaries | [Engineering](engineering/README.md), [Project Structure](architecture/project-structure.md), and [Module Map](architecture/module-map.md) |
| Domain dictionary | [Domain](domain/README.md) and [Meridian Domain Model](architecture/meridian-domain-model.md) |
| Build, test, and run commands | [Engineering](engineering/README.md), [Start](start/README.md), and [HELP](HELP.md) |
| Generated documentation policy | [Generated Documentation](generated/README.md) |
| AI workflow policy | [Provider-Agnostic AI Development Contract](ai/assistant-workflow-contract.md) |

## Documentation Model

| Class | Folders | Rule |
| --- | --- | --- |
| Primary audience lanes | `start/`, `product/`, `engineering/`, `operators/`, `reference/` | Begin here for portable, current guidance. |
| Canonical knowledge systems | `architecture/`, `domain/`, `ai/`, `roadmap/`, `source/`, `generated/` | Follow their registry, generation, and ownership rules. |
| Supporting lanes | `adr/`, `development/`, `diagrams/`, `docfx/`, `examples/`, `integrations/`, `prompts/`, `screenshots/`, `security/`, `status/`, `testing/` | Keep discoverable from a canonical index; do not duplicate higher-level truth. |
| Transitional compatibility lanes | `operations/`, `plans/` | Retain only while active tools, tests, or links require the paths; do not add new durable guidance here. |
| Historical material | `archive/docs/` | Preserve superseded plans, audits, status snapshots, migrations, and one-off reports. |

See the [Documentation Inventory](documentation-inventory.md) for the folder-by-folder map. Removed
lanes such as `developer/`, `providers/`, `api/`, `design/`, `ui/`, `evaluations/`, and `audits/`
must not be recreated; use their canonical owner or the archive.

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

- Historical audit and evaluation material now lives in `archive/docs/assessments/`. Use it for context and extraction work, not as canonical guidance.
- `docs/operations/` and `docs/plans/` remain compatibility lanes because active tools, tests, or
  links still consume specific paths. Their indexes identify why each retained file remains.
- `docs/status/` is a supporting automation-owned lane, not a hand-authored roadmap source. Use
  `docs/product/` for stakeholder interpretation and `docs/roadmap/data/*.yml` for durable status.
- `archive/docs/assessments/`, `archive/docs/plans/`, `archive/docs/status/`, `archive/docs/summaries/`, `archive/docs/migrations/`, and `archive/docs/workflows/` have bucket indexes for staged archive batches.
- High-traffic archived material should keep a short redirect stub at the old path until all active links move to the replacement.

## Documentation Acceptance Criteria

- Committed docs use repository-relative paths and commands that work from the repository root.
- Active docs do not claim source-of-truth ownership when a registry or generator owns the truth.
- Every active folder has a clear `README.md`.
- High-traffic retired paths either redirect or are linked from an archive index.
- Root `README.md`, `AGENTS.md`, `CLAUDE.md`, and AI indexes route to this documentation model.
