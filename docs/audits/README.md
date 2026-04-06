# Audits Directory

**Owner:** Core Team
**Scope:** Governance — Code Quality
**Review Cadence:** As hygiene audits are completed
**Last Reviewed:** 2026-04-05

This directory contains active audit reports and machine-readable outputs for Meridian code-quality, simplification, and architecture-hygiene review work.

## Governance Position

Audits belong to the governance zone alongside `evaluations/`.

- `audits/` focuses on targeted code-quality audits, cleanup analyses, and hygiene assessments.
- `evaluations/` focuses on technology tradeoffs, architecture evaluations, and proposal-style reviews.

For a consolidated cross-folder summary, see [../status/EVALUATIONS_AND_AUDITS.md](../status/EVALUATIONS_AND_AUDITS.md).

## Current Audit Surfaces

| Document | Purpose |
|----------|---------|
| [AUDIT_REPORT.md](AUDIT_REPORT.md) | Consolidated human-readable audit report |
| [BACKTEST_ENGINE_CODE_REVIEW_2026_03_25.md](BACKTEST_ENGINE_CODE_REVIEW_2026_03_25.md) | Backtest-engine-focused review findings |
| [CODE_REVIEW_2026-03-16.md](CODE_REVIEW_2026-03-16.md) | Repo review snapshot from the March 2026 audit pass |
| [FURTHER_SIMPLIFICATION_OPPORTUNITIES.md](FURTHER_SIMPLIFICATION_OPPORTUNITIES.md) | Simplification backlog and code-reduction opportunities |
| [audit-results-full.json](audit-results-full.json) | Full machine-readable audit output |
| [audit-code-results.json](audit-code-results.json) | Code-focused machine-readable audit output |
| [audit-architecture-results.txt](audit-architecture-results.txt) | Architecture audit text output |
| [prompt-generation-results.json](prompt-generation-results.json) | Prompt-generation artifact retained for traceability |

## Archived Audit Snapshots

Historical point-in-time audit documents live in `archive/docs/assessments/`.

| Document | Reason Archived |
|----------|-----------------|
| [AUDIT_REPORT_2026_03_20.md](https://github.com/rodoHasArrived/Meridian/blob/main/archive/docs/assessments/AUDIT_REPORT_2026_03_20.md) | Dated snapshot retained after `AUDIT_REPORT.md` became the active audit surface |
| [CLEANUP_SUMMARY.md](https://github.com/rodoHasArrived/Meridian/blob/main/archive/docs/assessments/CLEANUP_SUMMARY.md) | Hygiene phases complete |
| [H3_DEBUG_CODE_ANALYSIS.md](https://github.com/rodoHasArrived/Meridian/blob/main/archive/docs/assessments/H3_DEBUG_CODE_ANALYSIS.md) | Historical debug analysis retained for reference |
| [CLEANUP_OPPORTUNITIES.md](https://github.com/rodoHasArrived/Meridian/blob/main/archive/docs/assessments/CLEANUP_OPPORTUNITIES.md) | Superseded by later cleanup and simplification planning |
| [UWP_COMPREHENSIVE_AUDIT.md](https://github.com/rodoHasArrived/Meridian/blob/main/archive/docs/assessments/UWP_COMPREHENSIVE_AUDIT.md) | Historical UWP audit preserved after the platform removal |

## Audit Standards

When creating or updating audits:

1. Put the executive summary first.
2. Ground each finding in specific files, commands, or test evidence.
3. Separate intentional code from problematic code.
4. Include validation steps so another maintainer can reproduce the result.

## Related Documentation

- [../status/EVALUATIONS_AND_AUDITS.md](../status/EVALUATIONS_AND_AUDITS.md)
- [../evaluations/README.md](../evaluations/README.md)
- [../status/IMPROVEMENTS.md](../status/IMPROVEMENTS.md)
- [../status/ROADMAP.md](../status/ROADMAP.md)
- [../development/README.md](../development/README.md)
- [../architecture/README.md](../architecture/README.md)
