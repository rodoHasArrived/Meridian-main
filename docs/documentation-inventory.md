# Documentation Inventory

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-05-30

This inventory seeds the documentation rebuild. It classifies the existing `docs/` surface by folder so migration can proceed in reviewable batches instead of one destructive rewrite.

## Classification Key

- `canonical`: active source to rewrite or preserve in the new model.
- `source-material`: useful facts should be extracted into canonical docs, then the old doc can be archived.
- `generated`: preserve; update source registry or generator instead of hand-editing output.
- `archive`: move to `archive/docs/` when replacement links are in place.
- `delete-candidate`: only untracked/generated junk or explicitly approved removals.

## Folder-Level Inventory

| Current folder | Initial class | Target owner | Notes |
| --- | --- | --- | --- |
| `docs/ai/` | canonical | `docs/ai/` | Keep as controlled assistant workflow system; update indexes for new docs model. |
| `docs/roadmap/` | canonical/generated | `docs/roadmap/` | Keep registry truth in `data/*.yml` and generated views. |
| `docs/source/` | canonical/generated | `docs/source/` | Keep source-module registry and generated source docs. |
| `docs/generated/` | generated | `docs/generated/` | Preserve; update generator inventory and do-not-edit policy. |
| `docs/start/` | canonical | `docs/start/` | Canonical first-run lane for project orientation, prerequisites, setup, launch paths, first validation, and audience routing. |
| `docs/product/` | canonical | `docs/product/` | Canonical stakeholder lane for product promise, operator lifecycle, capability posture, roadmap interpretation, and conservative completion-claim language. |
| `docs/engineering/` | canonical | `docs/engineering/` | Canonical developer and coding-agent lane for solution map, build/test/run commands, validation lanes, WPF/browser rules, source registry workflow, and known local blockers. |
| `docs/operators/` | canonical | `docs/operators/` | Canonical operator procedure lane for setup, provider workflows, workstation usage, runbooks, deployment, troubleshooting, support evidence, and operational guardrails. |
| `docs/reference/` | canonical | `docs/reference/` | Canonical lookup lane for APIs, env/config, CLI/schema shapes, provider capability matrices, UFL/asset profiles, glossary-style definitions, and stable contract tables. |
| `docs/architecture/` | source-material | `docs/engineering/` | Keep high-value architecture docs; route through engineering index. |
| `docs/developer/` | source-material | `docs/engineering/` | Merge with development guidance over time. |
| `docs/development/` | source-material | `docs/engineering/` | Merge developer guides and validation lanes into engineering. |
| `docs/operations/` | source-material | `docs/operators/` | Operator-facing runbooks, deployment, reliability, packaging, maintenance, and support docs now route through the operator lane. |
| `docs/providers/` | source-material | `docs/operators/` and `docs/reference/` | Provider setup and recovery belong to operators; provider capability and comparison lookup belongs to reference. |
| `docs/plans/` | source-material/archive | `docs/product/`, `docs/engineering/`, `docs/roadmap/`, `archive/docs/plans/` | Index demoted to migration source material; extract current direction and archive superseded plans. |
| `docs/status/` | controlled-migration/generated/archive | `docs/product/`, `docs/roadmap/`, `docs/generated/`, `archive/docs/summaries/` | Index demoted to controlled status/reporting migration; generated reports stay automation-owned. |
| `docs/evaluations/` | source-material/archive | `docs/product/`, `docs/engineering/`, `docs/reference/`, `archive/docs/assessments/` | Index demoted to migration source material; extract verified-current facts before archiving individual evaluations. |
| `docs/audits/` | source-material/archive | `docs/engineering/`, `archive/docs/assessments/` | Index demoted to migration source material; duplicate historical snapshots should become redirects to archive. |
| `docs/security/` | source-material | `docs/operators/`, `docs/engineering/`, `docs/reference/` | Split procedure, engineering, and lookup content later. |
| `docs/design/` | source-material | `docs/engineering/` and `docs/product/` | Preserve design-system facts; remove stale visual planning later. |
| `docs/diagrams/` | source-material/generated | `docs/engineering/`, `docs/source/` | Keep diagrams linked from canonical owners or registries. |
| `docs/integrations/` | source-material | `docs/engineering/` and `docs/reference/` | Keep only verified integration guidance active. |
| `docs/prompts/` | canonical/source-material | `docs/ai/` | Provider-agnostic prompt docs should be discoverable from AI. |
| `docs/api/` | source-material | `docs/reference/` | Merge lookup content into reference. |
| `docs/getting-started/` | source-material | `docs/start/` and `docs/operators/` | First-run and pilot setup content should migrate into start/operator lanes. |
| `docs/examples/` | source-material | `docs/start/` or `docs/reference/` | Keep examples only when current. |
| `docs/testing/` | source-material | `docs/engineering/` | Merge validation guidance into engineering. |
| `docs/ui/` | source-material | `docs/engineering/` and `docs/product/` | Keep UI behavior through shared workstation docs. |
| `docs/screenshots/` | source-material/generated | `docs/operators/` or generated evidence docs | Keep only current evidence workflow links. |
| `docs/docfx/` | source-material | `docs/reference/` | Keep if API-doc generation remains active. |

## Known Drift Flags

- Active docs should use `D:\Meridian-main`, not `C:\Dev\Meridian-main`, as the canonical local path.
- Roadmap truth should not be duplicated outside `docs/roadmap/data/*.yml`, generated roadmap views, and explicitly current stakeholder summaries.
- Source-module ownership should not be duplicated outside `docs/source/data/*.yml`, registered source READMEs, and generated source views.
- Existing dirty worktree changes outside this documentation rebuild are user-owned and must not be folded into archive or rewrite batches by accident.

## Next Migration Batch

1. Continue collapsing `docs/plans/` completed, speculative, or point-in-time plans into redirect stubs after checking whether any durable rule needs extraction.
2. Add targeted archive entries as files move into `archive/docs/assessments/`, `archive/docs/plans/`, `archive/docs/summaries/`, `archive/docs/migrations/`, or `archive/docs/workflows/`.
3. Leave redirect stubs only for high-traffic paths linked from root, AI, roadmap, command docs, or current folder indexes.
4. Do not move `docs/evaluations/` wholesale yet: its README still classifies several files as canonical/current. First extract current product, engineering, or reference facts into the rebuilt docs model, then archive the old evaluation files.
5. Run link repair and structure validation after each batch.

## Completed Migration Notes

| Date | Source path | Archive path | Action |
| --- | --- | --- | --- |
| 2026-05-30 | `build/scripts/docs/validate-docs-structure.py` | n/a | Added migration-aware top-level docs folder enforcement: canonical folders pass, known legacy folders warn, and unexpected new docs folders fail validation. |
| 2026-05-30 | `docs/audits/CODE_REVIEW_2026-03-16.md` | `archive/docs/assessments/CODE_REVIEW_2026-03-16.md` | Active duplicate replaced with redirect stub after the archive copy was confirmed byte-identical. |
| 2026-05-30 | `docs/audits/AUDIT_REPORT.md` | `archive/docs/assessments/AUDIT_REPORT.md` | March 2026 repo-wide audit preserved in the assessment archive and replaced with a high-traffic redirect stub. |
| 2026-05-30 | `docs/audits/BACKTEST_ENGINE_CODE_REVIEW_2026_03_25.md` | `archive/docs/assessments/BACKTEST_ENGINE_CODE_REVIEW_2026_03_25.md` | Backtesting subsystem review preserved in the assessment archive after durable backtesting engineering rules were summarized in `docs/engineering/README.md`. |
| 2026-05-30 | `docs/audits/FURTHER_SIMPLIFICATION_OPPORTUNITIES.md` | `archive/docs/assessments/FURTHER_SIMPLIFICATION_OPPORTUNITIES.md` | March 2026 simplification backlog preserved in the assessment archive after engineering docs were updated to require current-source revalidation before acting on archived cleanup findings. |
| 2026-05-30 | `docs/audits/workspace-visual-audit-checklist-2026-04-22.md` | `archive/docs/assessments/workspace-visual-audit-checklist-2026-04-22.md` | Workstation visual audit preserved in the assessment archive after durable visual consistency rules were summarized in `docs/engineering/README.md`. |
| 2026-05-30 | `docs/audits/audit-architecture-results.txt`, `docs/audits/audit-code-results.json`, `docs/audits/audit-results-full.json`, `docs/audits/prompt-generation-results.json` | `archive/docs/assessments/` | Point-in-time machine-readable audit outputs moved out of active docs; archive index now requires rerunning the owning audit before treating findings as current. |
| 2026-05-30 | `docs/evaluations/2026-03-brainstorm-next-frontier.md` | `archive/docs/assessments/2026-03-brainstorm-next-frontier.md` | March 2026 product/analytics brainstorm archived with a redirect stub; current product direction must come from product docs and roadmap registry. |
| 2026-05-30 | `docs/evaluations/assembly-performance-opportunities.md` | `archive/docs/assessments/assembly-performance-opportunities.md` | March 2026 performance-opportunity assessment archived with a redirect stub after engineering docs were updated to require current source and benchmark evidence before performance work. |
| 2026-05-30 | `docs/evaluations/competitive-analysis-2026-03.md` | `archive/docs/assessments/competitive-analysis-2026-03.md` | March/April 2026 competitive analysis archived with a redirect stub after adding a product rule that historical market comparisons are source material, not current positioning. |
| 2026-05-30 | `docs/evaluations/data-quality-monitoring-evaluation.md` | `archive/docs/assessments/data-quality-monitoring-evaluation.md` | February 2026 data-quality assessment archived with a redirect stub after product docs were updated to require current provider validation evidence and generated status artifacts for quality claims. |
| 2026-05-30 | `docs/evaluations/desktop-platform-improvements-implementation-guide.md` | `archive/docs/assessments/desktop-platform-improvements-implementation-guide.md` | Historical desktop improvement program guide archived with a redirect stub after engineering docs were updated to use current desktop source, WPF tests, shared UI service tests, and active guidance. |
| 2026-05-30 | `docs/evaluations/high-value-low-cost-improvements-brainstorm.md` | `archive/docs/assessments/high-value-low-cost-improvements-brainstorm.md` | February/March 2026 improvement-priority snapshot archived with a redirect stub; current priorities must be revalidated through engineering/product docs and roadmap registry. |
| 2026-05-30 | `docs/evaluations/historical-data-providers-evaluation.md` | `archive/docs/assessments/historical-data-providers-evaluation.md` | February 2026 backfill-provider assessment archived with a redirect stub after reference docs were updated to route provider-selection claims through current matrices and validation evidence. |
| 2026-05-30 | `docs/evaluations/ingestion-orchestration-evaluation.md` | `archive/docs/assessments/ingestion-orchestration-evaluation.md` | February/March 2026 scheduler/backfill-control assessment archived with a redirect stub after engineering/operator docs were updated to preserve job state, checkpoint, idempotency, backpressure, audit, and SLO guidance. |
| 2026-05-30 | `docs/evaluations/nautilus-inspired-restructuring-proposal.md` | `archive/docs/assessments/nautilus-inspired-restructuring-proposal.md` | March 2026 structural proposal archived with a redirect stub after adding an engineering rule that old restructuring advice must be revalidated against current source ownership and registry evidence. |
| 2026-05-30 | `docs/evaluations/operational-readiness-evaluation.md` | `archive/docs/assessments/operational-readiness-evaluation.md` | February 2026 readiness assessment archived with a redirect stub after operator docs were updated to require current SLO, runbook, release-gate, and validation evidence for readiness claims. |
| 2026-05-30 | `docs/evaluations/quant-script-blueprint-brainstorm.md` | `archive/docs/assessments/quant-script-blueprint-brainstorm.md` | Historical QuantScript critique and backlog archived with a redirect stub after the document itself identified newer implementation evidence as authoritative. |
| 2026-05-30 | `docs/evaluations/realtime-streaming-architecture-evaluation.md` | `archive/docs/assessments/realtime-streaming-architecture-evaluation.md` | March 2026 streaming architecture assessment archived with a redirect stub after engineering/operator/reference docs were updated to preserve streaming pipeline, evidence, and lookup rules. |
| 2026-05-30 | `docs/evaluations/storage-architecture-evaluation.md` | `archive/docs/assessments/storage-architecture-evaluation.md` | March 2026 storage architecture assessment archived with a redirect stub after engineering/reference docs were updated to preserve persistence, catalog, retention, and validation rules. |
| 2026-05-30 | `docs/evaluations/windows-desktop-provider-configurability-assessment.md` | `archive/docs/assessments/windows-desktop-provider-configurability-assessment.md` | February/March 2026 desktop provider-config assessment archived with a redirect stub after operator and engineering docs were updated to keep provider settings on shared typed configuration and credential seams. |
| 2026-05-30 | `docs/plans/performance-todo-2026-05-21.md` | `archive/docs/plans/performance-todo-2026-05-21.md` | Environment-specific benchmark blocker/TODO snapshot archived with a redirect stub; active performance work now routes through engineering guidance plus fresh benchmark evidence. |
| 2026-05-30 | `docs/plans/runbook-template-registry-modernization-plan.md` | `archive/docs/plans/runbook-template-registry-modernization-plan.md` | Draft runbook/template registry plan archived with a redirect stub; active platform scope must be proven through operator, engineering, and roadmap evidence. |
| 2026-05-30 | `docs/audits/README.md` | n/a | Reframed as a source-material migration index instead of an active governance front door. |
| 2026-05-30 | `docs/evaluations/README.md` | n/a | Reframed as a source-material migration index with target canonical lanes for extraction. |
| 2026-05-30 | `docs/plans/README.md` | n/a | Reframed as a plan source-material migration index under product, engineering, roadmap, and archive ownership. |
| 2026-05-30 | `docs/status/README.md` | n/a | Reframed as a controlled status/reporting migration index with generated reports kept automation-owned. |
| 2026-05-30 | `docs/reference/README.md` | n/a | Rebuilt as the canonical lookup lane and linked migration sources for provider matrices, schemas, contracts, and UFL material. |
| 2026-05-30 | `docs/operators/README.md` | n/a | Rebuilt as the canonical operator procedure lane and mapped setup, provider, workstation, governance, reconciliation, reliability, packaging, and support source material. |
| 2026-05-30 | `docs/start/README.md` | n/a | Expanded into the canonical first-run lane using verified setup, launch, validation, and audience-routing source material from legacy getting-started and developer guides. |
| 2026-05-30 | `docs/engineering/README.md` | n/a | Expanded into the canonical developer/agent lane with solution ownership, build/test/run commands, WPF/browser rules, source registry workflow, documentation proof lanes, and local blocker guidance. |
| 2026-05-30 | `docs/product/README.md` | n/a | Expanded into the canonical stakeholder lane with product promise, operator lifecycle, roadmap posture, capability map, source-of-truth order, and conservative claim language. |
| 2026-05-30 | `docs/development/documentation-contribution-guide.md` | n/a | Demoted legacy placement taxonomy behind the new documentation ownership contract and inserted the rebuilt target model for future docs. |
| 2026-05-30 | active docs path references | n/a | Replaced active `C:\Dev\Meridian-main` checkout guidance with canonical `D:\Meridian-main`; historical/archive references remain eligible for later archive cleanup. |
