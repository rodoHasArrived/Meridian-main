# Product Documentation

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

Start here for Meridian's product direction, delivery evidence, and release-readiness questions.
This page routes stakeholders to the owning document; it does not maintain a second status tracker.

## What a Stakeholder Should Read Here

| Your question | Start here |
| --- | --- |
| What is Meridian for, and who does it serve? | [Meridian Design Document](meridian-design-document.md) |
| What has been delivered, accepted, or planned? | [Generated Roadmap Summary](../roadmap/generated/ROADMAP_SUMMARY.md) and [Roadmap Register](../roadmap/generated/roadmap-register.md) |
| What blocks a production release? | [Implementation and Readiness Tracker](implementation-todo-list.md) |
| What should we work on next? | [Product Plans — Next-Work Determinations](plans/README.md#next-work-determinations) |
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
P0 release gate. A capability's acceptance is bounded by its recorded evidence; it does not certify
a production release. Release claims require the tracker, roadmap evidence, packaging, operator
preflight, and required GitHub Actions evidence to agree on the same release commit.

## Canonical Product Design Charter

The [Meridian Design Document](meridian-design-document.md) owns product scope, customer needs,
operating principles, and capability intent. Start there before changing stakeholder narrative.
Use the [Core Extensibility Model](../architecture/core-extensibility-model.md) for the engineering
boundary between shared financial-operations objects and governed tenant configuration.

## Canonical Product Truth Order

Use the source that owns the question:

| Question | Authoritative source |
| --- | --- |
| Delivery state, acceptance, and sequencing | [Roadmap Registry](../roadmap/README.md) and its generated views |
| Product scope and design intent | [Meridian Design Document](meridian-design-document.md) |
| Production-readiness execution and P0 evidence | [Implementation and Readiness Tracker](implementation-todo-list.md) |
| Implemented module behavior and ownership | [Source Documentation Mesh](../source/README.md) and the owning source README |
| Prioritization rationale and historical findings | [Product Plans](plans/README.md) and [Review History](review-history.md); recheck their dated evidence before acting |

## Stakeholder Claim Rules

- Describe a capability as closed or done only when its roadmap row and supporting evidence agree.
- Describe work as planned only when the registry records that state.
- Keep implemented behavior, operator acceptance, and production certification distinct.
- Link claims to their roadmap evidence and the relevant [operator procedure](../operators/README.md)
  or [reference contract](../reference/README.md).
- Treat a review, brainstorm, prototype, or delivery plan as a dated input; it does not change status.

The operating proof chain is **Import → Validate → Reconcile → Approve → Report**. Check evidence
through that chain when assessing a new or changed stakeholder claim.

## Product-Owner Validation

When updating this lane, follow the [Documentation Ownership Contract](../documentation-ownership.md).
Update the owning registry or charter before changing a claim; generated views remain generator-owned.
Use the [Engineering documentation checks](../engineering/README.md#documentation-and-registry-ownership)
for the affected surface. Keep dated reviews discoverable through [Review History](review-history.md)
and update the [Product Plans index](plans/README.md) when prioritization changes.
