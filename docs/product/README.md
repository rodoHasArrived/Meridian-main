# Product Documentation

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-31

Start here for Meridian's product direction, delivery evidence, and release-readiness questions.
This page routes stakeholders to the owning document; it does not maintain a second status tracker.

Meridian's objectives include financial operations, strategy formulation and validation, order
management, and governed strategy execution. These outcomes share evidence, scoped authority, and
financial integrity. The existing close package is a bounded financial workflow, not a prerequisite
for every strategy or order-management capability.

## What a Stakeholder Should Read Here

| Your question | Start here |
| --- | --- |
| What is Meridian for, and who does it serve? | [Meridian Design Document](meridian-design-document.md) |
| What has been delivered, accepted, or planned? | [Generated Roadmap Summary](../roadmap/generated/ROADMAP_SUMMARY.md) and [Roadmap Register](../roadmap/generated/roadmap-register.md) |
| What blocks a production release? | [Implementation and Readiness Tracker](implementation-todo-list.md) |
| What should we work on next? | [Product Plans — Next-Work Determinations](plans/README.md#next-work-determinations) |
| What work follows from the strategy and OMS scope expansion? | [Expanded strategy and order-management objectives](implementation-todo-list.md#expanded-strategy-and-order-management-objectives) |
| Why was a direction chosen, or what did a review find? | [Product Review and Decision History](review-history.md) |
| Where is a technical plan or blueprint? | [Plans and Blueprints Register](../engineering/blueprints/README.md) |
| What evidence is needed before expanding scope? | [Deferred Expansion Boundaries](deferred-expansion-boundaries.md) |
| How do I operate or deploy the product? | [Operators](../operators/README.md) |

## Current Project Snapshot

Read the [Generated Roadmap Summary](../roadmap/generated/ROADMAP_SUMMARY.md) for the current
program snapshot and the [Roadmap Register](../roadmap/generated/roadmap-register.md) for individual
rows. Their inputs are the [program-state registry](../roadmap/data/program-state.yml) and
[roadmap-item registry](../roadmap/data/roadmap-items.yml).

Check the [Implementation and Readiness Tracker](implementation-todo-list.md) separately for the
P0 production release gate. A capability's acceptance is bounded by its recorded evidence; it does not certify
a production release. For a production release candidate only, release claims require the tracker,
roadmap evidence, packaging, operator preflight, and required GitHub Actions evidence to agree on the
same release commit. Development verification on synthetic data does not wait on operator acceptance or
production certification; see the [Solo-developer workflow](../ai/solo-developer-workflow.md) for tiers and vocabulary.

## Canonical Product Design Charter

The [Meridian Design Document](meridian-design-document.md) owns product scope, customer needs,
operating principles, and capability intent. Start there before changing stakeholder narrative.
Its [strategy and OMS direction](meridian-design-document.md#227-strategy-and-order-management-direction)
records `DEC-TRADING-STRATEGY-DIRECTION-001` and the W13 objectives. Scope registration does not
establish implemented coverage, operator acceptance, a delivery rank, or permission to trade live.
The [strategy, order, and execution dictionary](../domain/strategy-order-execution.md) defines the
shared concepts and authority boundaries.
Use the [Core Extensibility Model](../architecture/core-extensibility-model.md) for the engineering
boundary between shared financial-operations objects and governed tenant configuration.

## Canonical Product Truth Order

Use the source that owns the question:

| Question | Authoritative source |
| --- | --- |
| Delivery state, acceptance, and sequencing | [Roadmap Registry](../roadmap/README.md); canonical data lives in `docs/roadmap/data/*.yml`, and generated views summarize it |
| Product scope and design intent | [Meridian Design Document](meridian-design-document.md) |
| Production-readiness execution and P0 evidence | [Implementation and Readiness Tracker](implementation-todo-list.md) |
| Implemented module behavior and ownership | [Source Documentation Mesh](../source/README.md) and the owning source README |
| Prioritization rationale and historical findings | [Product Plans](plans/README.md) and [Review History](review-history.md); recheck their dated evidence before acting |

## Stakeholder Claim Rules

- Describe a capability as closed or done only when its roadmap row and supporting evidence agree.
- Describe work as planned only when the registry records that state.
- Keep four states distinct: implemented behavior, verification on synthetic data (development
  verification), operator acceptance, and production certification. Development verification does not
  wait on operator acceptance or production certification, and does not imply either.
- In prose, the [solo-developer completion vocabulary](../ai/solo-developer-workflow.md#completion-vocabulary)
  (Planned, In progress, Verified for a stated scope, Released) is communication guidance only; roadmap
  statuses are unchanged. For providers, say `synthetic-verified`, `live-verified`, `live-untested`, or
  `unsupported`.
- Link claims to their roadmap evidence and the relevant [operator procedure](../operators/README.md)
  or [reference contract](../reference/README.md).
- Treat a review, brainstorm, prototype, or delivery plan as a dated input; it does not change status.

Check claims against the evidence path for the declared workflow:

| Outcome | Connected evidence |
| --- | --- |
| Financial operations | Source intake, validation, reconciliation, reviewed accounting, approval, close, and reporting |
| Strategy formulation and validation | Strategy version, parameters, data and runtime assumptions, run results, paper evidence, and promotion decision |
| Order management and execution | Authorized operator or strategy intent, account and pre-trade checks, broker observations, recovery, and portfolio/accounting reconciliation |

A completed stage does not approve its successor. Order intent is distinct from broker-confirmed
state, and execution evidence is distinct from journal-posting authority. Model suggestions cannot
grant their own approval or live authority; approved deterministic strategies remain subject to
the supported runtime, promotion, account, risk, and operator-control gates.

## Product-Owner Validation

When updating this lane, follow the [Documentation Ownership Contract](../documentation-ownership.md).
Update the owning registry or charter before changing a claim; generated views remain generator-owned.
Use the [Engineering documentation checks](../engineering/README.md#documentation-and-registry-ownership)
for the affected surface. Keep dated reviews discoverable through [Review History](review-history.md)
and update the [Product Plans index](plans/README.md) when prioritization changes.
