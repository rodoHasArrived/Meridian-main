# Evidence-Backed Investment Operations Plan

**Date:** 2026-04-29
**Status:** Active product-positioning and roadmap filter
**Audience:** Product, roadmap, architecture, web dashboard, governance, and fund-operations contributors

This plan incorporates the 2026-04-29 differentiation revision. Meridian should not be positioned
as another trading workstation or generic front-to-back suite. The sharper category is
**evidence-backed investment operations**: trusted data, research runs, paper validation, ledger
impact, reconciliation outcomes, approvals, and governed report artifacts tied together in one
explainable chain.

Use this document as the product-category filter for roadmap work. It does not create a new wave
or claim the modules below are already delivered. It sharpens Waves 2-4 and tells maintainers what
to keep active, what to demote, and what to archive.

## Product Claim

Meridian should become the system of record for investment decision evidence.

The core commercial question is:

> Can Meridian prove, book, reconcile, approve, and report this investment decision?

That claim is stronger than a broad workstation claim because it connects upstream research and
paper validation to downstream books, reconciliation, and reporting. The active browser dashboard
is the primary operator UI lane for proving that flow; retained WPF surfaces remain compatibility,
regression, and shared-contract support unless a workflow explicitly needs desktop coverage.

## Golden Path

All near-term product work should strengthen this path or explain why it is intentionally deferred:

```text
Trusted provider/data evidence
-> research run
-> run comparison
-> paper promotion
-> paper session
-> portfolio / ledger review
-> reconciliation
-> governed report pack
```

The visible workspaces remain `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`,
`Data`, and `Settings`. Legacy `Research`, `Data Operations`, and `Governance` names are
compatibility aliases or grouping language, not new root navigation.

## Priority Bets

| Priority | Capability | Roadmap placement | Current posture |
| ---: | --- | --- | --- |
| 1 | Run Evidence Graph and Evidence Vault | Wave 3 backbone into Wave 4 | Planned; provider packets, run continuity, paper replay metadata, reconciliation work items, and report-pack seams are inputs |
| 2 | Governed Report Pack Studio | Wave 4 | Partial support through report-pack schema/version checks and export/reporting seams |
| 3 | Reconciliation Desk / Casework | Wave 4 | Early in progress through file-backed break queues, tolerance/sign-off metadata, audit history, and calibration rollups |
| 4 | Accounting-grade PaperOps | Wave 2 into Wave 3 | Partial support through paper sessions, replay/audit metadata, readiness gates, and ledger count checks |
| 5 | Shadow Books and Shadow NAV | Wave 4 later slice | Planned on top of ledger, reconciliation, external-statement import, and report-pack foundations |
| 6 | Strategy / Promotion Passport | Wave 2 into Wave 3 | Partial support through promotion checklist and review-packet state |
| 7 | Data Trust Passport | Wave 1 maintenance into Wave 3 | Partial support through DK1 trust packets, validation evidence, and data-quality posture |
| 8 | Operator Readiness Console | Wave 2 support surface | Partial browser support through `/trading/readiness` and shared readiness payloads |

Do not expand live-broker breadth, generic analytics, or bank-style governance frameworks ahead of
these evidence-producing seams unless the roadmap explicitly pulls that work forward.

## Commercial Packages

These package names are positioning aids, not implementation status:

| Package | Positioning | Included targets |
| --- | --- | --- |
| Meridian Assurance | Prove every strategy decision before capital is at risk | Data Trust Passport, Run Evidence Graph, Strategy / Promotion Passport, readiness console, promotion and IC packs |
| Meridian FundOps Control | Run shadow books, reconcile faster, and close with confidence | Reconciliation Desk, Shadow Books, accounting-grade PaperOps, close and reconciliation packs |
| Meridian Connect Enterprise | Make evidence, books, and workflows programmable | Evidence APIs, imports/exports, webhooks, governed artifacts, selected Assurance/FundOps modules |

## Sequencing Rule

1. Prove one seeded golden path from trusted data to governed report pack.
2. Productize the visible evidence surfaces: Data Trust Passport, Strategy / Promotion Passport,
   Run Evidence Graph, and governed packs.
3. Deepen daily operations: reconciliation casework, shadow books, close readiness, and statement
   comparison.
4. Only then widen integration breadth, analytics depth, live-readiness claims, or optional
   advanced research tracks.

## Documentation And Archive Rule

Active docs should describe the evidence-backed investment-operations path. Prior files that are
deprecated, superseded, or retained only for historical sequencing must move under `archive/docs/`
instead of remaining in the active docs tree.

Archive placement follows the existing buckets:

- `archive/docs/plans/` for superseded plans, release checklists, sprint backlogs, and old
  roadmap proposals
- `archive/docs/summaries/` for dated snapshots, one-off reports, and historical status notes
- `archive/docs/assessments/` for old UI/product assessments and evaluations
- `archive/docs/migrations/` for legacy platform-transition material

When a document moves, update the nearest README, TOC, or source-of-truth link so active guidance
points to current docs and historical links clearly point into `archive/docs/`.

## Active Implementation Anchors

Keep these active unless they are explicitly replaced and their strong references are updated:

- [`../status/ROADMAP.md`](../status/ROADMAP.md)
- [`../status/ROADMAP_COMBINED.md`](../status/ROADMAP_COMBINED.md)
- [`../status/FEATURE_INVENTORY.md`](../status/FEATURE_INVENTORY.md)
- [`meridian-pilot-workflow.md`](meridian-pilot-workflow.md)
- [`meridian-6-week-roadmap.md`](meridian-6-week-roadmap.md)
- [`waves-2-4-operator-readiness-addendum.md`](waves-2-4-operator-readiness-addendum.md)
- [`web-ui-development-pivot.md`](web-ui-development-pivot.md)
- [`paper-trading-cockpit-reliability-sprint.md`](paper-trading-cockpit-reliability-sprint.md)
- [`governance-fund-ops-blueprint.md`](governance-fund-ops-blueprint.md)

The workstation migration blueprint remains an active implementation reference while code,
automation, and generated navigation still depend on it, but the product category above supersedes
any older framing that treats Meridian primarily as a trading workstation.
