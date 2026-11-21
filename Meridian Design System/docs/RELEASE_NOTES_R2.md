# Release 2 — Standardize financial decisions

**Purpose: make consequential changes reviewable, explainable, and safe.**

Release 2 rebuilds six components so that every consequential action in Meridian carries the same
structure: what is being changed, what it is being compared against, what evidence supports it, what
it will affect, who is accountable, and what is still unexplained. All prior prop names are still
honored — existing call sites upgrade untouched.

| Component | Role | Location |
| --- | --- | --- |
| `VarianceCell` | Actual-versus-expected comparison with tolerance and result state | `components/accounting/VarianceCell.jsx` |
| `TieOutPanel` | Reconciliation proof between independently sourced values | `components/accounting/TieOutPanel.jsx` |
| `ChangeSetReview` | Reviews staged changes before mutation | `components/operations/ChangeSetReview.jsx` |
| `VersionConflictNotice` | Prevents silent overwrites after intervening updates | `components/operations/VersionConflictNotice.jsx` |
| `ResolutionDrawer` | Canonical evidence, cause, correction, impact, ownership, and resolution surface | `components/operations/ResolutionDrawer.jsx` |
| `AttestationPanel` | Version-specific readiness, certification, rationale, and sign-off control | `components/operations/AttestationPanel.jsx` |

Supporting exports — `ChangeSetProvider`, `ChangeSetDiff`, `ChangeReviewBar`, and
`ChangeSetHooks.useChangeSet` — preserve the distinction between **editing**, **reviewing**, and
**committing**.

---

## VarianceCell

- Seven comparison notations: currency, accounting (parenthesized negatives), percent, basis points,
  quantity, count, duration (early/late).
- Absolute (`tolerance`) or relative (`tolerancePct`) tolerance, plus a **near-tolerance** band
  (`nearFactor`, default 0.8) that resolves to *Review* rather than agreement.
- Seven result states including `pending`, `not-comparable`, `missing-actual`, and
  `missing-reference` — missing data never renders as agreement.
- The ten data conditions are carried through: estimated values suffix `Est.`, overridden values
  carry `*`.
- Optional `explanation`, `comparisonLabel`, and reference display; `onSelect` makes the cell a
  selectable datum; `layout="inline"` for dense rows.
- Every state pairs a mark with a word. Unfavorable variance uses red; terracotta remains brand.

## TieOutPanel

- Gross difference, explained reconciling items, and **unexplained residual** are now separate
  lines. A residual inside tolerance but unexplained reads as *Within tolerance* — a disposition is
  still required.
- Seven reconciliation types: one-to-one, many-to-one, one-to-many, rollforward, quantity-and-value,
  count-and-amount, multi-basis.
- Reconciling items expand to show condition and evidence; `explained: false` marks an item that is
  identified but unsubstantiated.
- `secondary` adds a second measure (units, record counts) so a value tie-out cannot conceal a
  quantity break.
- Actions: investigate, add reconciling item, link evidence, accept within tolerance (reason
  required), escalate, reopen, export proof.

## ChangeSetReview

- Change-set identity: id, scope, affected record count, initiator, base version.
- Changes group by business domain, each with a per-field **reason**, validation issue, and stated
  downstream impact.
- Twelve lifecycle states, derived from validation unless supplied.
- Impact-before-commitment block: affected records, recalculations, reports, controls, locked-period
  and prior-approval invalidation, audit event.
- Discard requires confirmation and lists exactly what would be lost.
- `ChangeSetProvider` now owns validation, per-field reasons, and derived status;
  `ChangeSetHooks.useChangeSet()` reads it.

## VersionConflictNotice

- Three-way presentation: **base · yours · latest**, with per-field conflict detection.
- Non-conflicting fields are counted and can be merged in bulk; conflicting fields require an
  explicit per-field choice.
- Replacing a newer value warns that validation re-runs and that impact previews taken against the
  base version are invalidated; a rationale is required by default.
- Primary action is *Reload latest record*. Save-as-draft and discard are available; nothing
  auto-merges. `role="alert"`, focus moves to the heading.

## ResolutionDrawer

- Severity badge, affected object, case id and version, and a plain-language statement of the break.
- Evidence list marks what is **missing**, not only what is attached.
- Root cause is a controlled classification (so exceptions aggregate) plus optional free text.
- Correction facts, impact facts, and an ownership/SLA grid (assignee, team, due, age, escalation,
  prior handoffs).
- Exactly one primary action; resolution rationale can be required. Narrow viewports get a
  full-width region with an explicit return path.

## AttestationPanel

- Binding block: object, version, period, snapshot, required signatory role.
- Readiness gates distinguish **blocking** from merely incomplete; evidence completeness shown as
  attached-of-required.
- Maker–checker signatories with segregation-of-duties enforcement against `currentUser`.
- Ten states including `superseded` and `expired` — an attestation whose underlying version
  changed is void, not stale.
- Approval history, certification text, required rationale, and an explicit "cannot attest
  because…" statement.

---

## Compatibility

No prop was removed. `adjustments`, `labels`, `mode`, `field`/`proposed`/`latest`,
`originalVersion`, `assignee`, and `sla` all still work. `useChangeSet` remains a named export
and is additionally reachable as `ChangeSetHooks.useChangeSet` for consumers reading the compiled
bundle namespace.
