# Meridian Documentation

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-31

This is the canonical front door for Meridian documentation. Start from an audience or task below,
then follow the named source of truth. Folder names alone do not establish current status: roadmap,
source, and generated content remain registry-owned.

## Current Project Snapshot

For current delivery and acceptance status, use the
[generated roadmap summary](roadmap/generated/ROADMAP_SUMMARY.md) and
[roadmap register](roadmap/generated/roadmap-register.md). The
[program-state registry](roadmap/data/program-state.yml) owns the program snapshot.

Development status (what is built and verified on synthetic data) and release posture are separate
questions. For production-release blockers and required evidence, use the
[Implementation and Readiness Tracker](product/implementation-todo-list.md); it is a production
checklist, not a gate for ordinary development. Capability acceptance and release certification have
separate evidence requirements; this front door does not duplicate their changing status.

## Start By Audience

| Audience | Start here | Use when |
| --- | --- | --- |
| Developers and agents | [Engineering](engineering/README.md) and [Solo-developer workflow](ai/solo-developer-workflow.md) | Build, test, architecture, module ownership, source docs, WPF/browser workstation rules, validation lanes, and risk-based verification tiers. |
| Stakeholders | [Product](product/README.md) | Product narrative, capability status, roadmap interpretation, and evidence-backed investment-operations framing. |
| Operators | [Operators](operators/README.md) | Provider setup, workstation launch, runbooks, deployment, troubleshooting, and support artifacts. |
| First-time contributors | [Start](start/README.md) | Fast local orientation, setup, launch, and first safe validation commands. |
| API and configuration readers | [Reference](reference/README.md) | API contracts, configuration, schemas, provider matrices, and stable lookup tables. |
| AI assistants | [AI](ai/README.md) | Agent workflow contracts, repo navigation, Codex/Claude/Copilot guidance, prompts, skills, and inventory checks. |
| Domain modelers | [Domain Dictionary](domain/README.md) | Business nouns, relationships, rules, examples, and expansion notes for AI-assisted development. |
| Architects and reviewers | [Architecture](architecture/README.md) | MDIF, system boundaries, architectural decisions, runtime design, and rationale. |

## Canonical Sources

| Topic | Canonical source |
| --- | --- |
| Repository entrypoint | [../README.md](../README.md) |
| Documentation ownership and migration rules | [Documentation Ownership Contract](documentation-ownership.md) |
| Current documentation folder inventory | [Documentation Inventory](documentation-inventory.md) |
| Current development status | [Program State](roadmap/data/program-state.yml) and [Roadmap Summary](roadmap/generated/ROADMAP_SUMMARY.md) |
| Release posture and production-readiness checklist | [Implementation and Readiness Tracker](product/implementation-todo-list.md) |
| Development verification tiers and completion vocabulary | [Solo-developer workflow](ai/solo-developer-workflow.md) |
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
| Historical material | Git history ([pinned `archive/` snapshot](https://github.com/rodoHasArrived/Meridian-main/tree/9c0c9de471fb1adbff73321c7d1ccfa871eb669a/archive)) | The repository no longer carries an `archive/` folder; recover superseded material with `git show 9c0c9de4:archive/<path>`. Historical ADRs still cited by source live in `adr/historical/`. |

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

## Retired Material Policy

The repository no longer keeps an `archive/` folder. Delete superseded material only with explicit approval (git history retains it; recover with `git show <commit>:<path>`), or keep it in place with a status note and a replacement link. Do not create a new `archive/` folder. Historical ADRs still cited by source live in `adr/historical/`.

## Migration Status

- The 2026-09-11 archive cleanup (`982eea2d`) removed every `archive/docs/` bucket except
  `summaries/`, and removed `archive/code/`. Active docs that linked into the removed buckets now
  point at GitHub permalinks pinned to the last commit that still carried them (`8a420730`). Use
  that material for context and extraction work only, not as canonical guidance; recover a file
  locally with `git show 8a420730:<path>`.
- The 2026-11-25 cleanup removed the remaining `archive/` folder. The eleven historical ADRs still
  named by `ImplementsAdr` attributes moved to `docs/adr/historical/`; every other former archive
  link now points at a GitHub permalink pinned to `9c0c9de4`, the last commit that carried it.
- `docs/operations/` and `docs/plans/` remain compatibility lanes because active tools, tests, or
  links still consume specific paths. Their indexes identify why each retained file remains.
- `docs/status/` is a supporting automation-owned lane, not a hand-authored roadmap source. Use
  `docs/product/` for stakeholder interpretation and `docs/roadmap/data/*.yml` for durable status.
- The [last archive index](https://github.com/rodoHasArrived/Meridian-main/blob/9c0c9de471fb1adbff73321c7d1ccfa871eb669a/archive/docs/README.md) is a pinned
  snapshot of the former `archive/docs/` buckets. Only the still-cited ADRs remain in the tree, under
  [adr/historical](adr/historical/README.md).
- High-traffic retired material should keep a short redirect stub at the old path until all active links move to the replacement.

## Documentation Acceptance Criteria

- Committed docs use repository-relative paths and commands that work from the repository root.
- Active docs do not claim source-of-truth ownership when a registry or generator owns the truth.
- Every active folder has a clear `README.md`.
- High-traffic retired paths either redirect or are linked from an archive index.
- Root `README.md`, `AGENTS.md`, `CLAUDE.md`, and AI indexes route to this documentation model.
