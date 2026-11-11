# The object model — what Meridian is about

**Normative.** Meridian's components describe *how* a surface looks and *how* work moves. This
document names *what* the work is about. Sixteen financial objects run through every workstation in
the platform; a screen is always about one of them. Naming that object — and stating its identity
the same way everywhere — is what lets an operator move from Security Master to Accounting to Close
Management without relearning where anything lives.

Contract: `components/core/ObjectModel.jsx` · card: **Core › Object model** ·
API: `ObjectHeader`, `describeObject`, `objectMeta`, `OBJECT_TYPES`, `ObjectHooks`.

---

## The object ledger

| Object | Identifier a human quotes | "How much" means | Lifecycle |
| --- | --- | --- | --- |
| Security | CUSIP | Market value | active · matured · called · delisted |
| Position | Position | Market value | open · closed |
| Tax lot | Lot | Cost basis | open · partially-relieved · relieved |
| Account | Account | Balance | open · restricted · closed |
| Journal entry | Entry | Debit total | draft · pending · posted · reversed |
| Transaction | Transaction | Net amount | pending · settled · cancelled |
| Portfolio | Portfolio | Net asset value | active · closing · closed |
| Legal entity | Entity | Total assets | active · dormant · dissolved |
| Accounting period | Period | Entries posted | open · soft-closed · hard-closed · adjustment |
| Reconciliation | Reconciliation | Residual difference | open · in-review · accepted · signed |
| Exception | Exception | Residual | open · classified · resolved · escalated · waived |
| Control | Control | Dependent controls | not-started · in-progress · blocked · complete · reopened |
| Evidence item | Document | Extraction completeness | received · extracted · verified · superseded · missing |
| Calculation | Calculation | Result | current · stale · superseded · failed |
| Attestation | Attestation | Gates satisfied | draft · ready · signed · rejected · reopened |
| Scenario | Scenario | Change versus base | draft · published · superseded |

Two properties of this table matter more than its contents.

**"How much" is never assumed to be a currency amount.** A control's magnitude is the number of
things it blocks. An evidence item's magnitude is how complete its extraction is. A screen that
prints a dollar sign next to every object is describing a spreadsheet, not a platform.

**Lifecycle is the object's own vocabulary, not a status color.** `superseded` and `reversed` are
different facts and must not both flatten into "inactive". Read lifecycle from `objectMeta(type)`
rather than hardcoding a per-screen enum.

---

## The identity contract

Every object answers the same ten questions, in this fixed order:

| # | Facet | Answer |
| --- | --- | --- |
| 1 | Object type | Which of the sixteen |
| 2 | Primary identifier | The value a human quotes on a call |
| 3 | Name | The human-readable description |
| 4 | Context | Entity, portfolio, book, basis — the scope that qualifies it |
| 5 | Condition | One of the eleven data conditions |
| 6 | As-of | The `TimeContext` role that makes the stated values true |
| 7 | Material value | What "how much" means for this type |
| 8 | Version | Which revision the screen is acting on |
| 9 | Evidence coverage | How much of the required evidence is attached |
| 10 | Open exceptions | What is unresolved, and whether it blocks |

`ObjectHeader` renders exactly that order. Facets the object does not carry are **absent, never
blank** — an empty "Version —" is a claim that the object is unversioned, which is usually a lie.

```jsx
<ObjectHeader
  type="position"
  id="91282CKQ3" identifierLabel="CUSIP"
  name="US Treasury 4.375% 05/15/34"
  context={{ portfolio: "General Account", book: "STAT" }}
  value={{ label: "Market value", amount: "$46.9M", secondary: "Par $48.2M" }}
  condition="confirmed" asOf="2026-07-31" version={4}
  coverage={{ attached: 99, required: 100, label: "sources" }}
  exceptions={{ count: 1, blocking: true }}
  onOpenEvidence={openEvidence} onOpenExceptions={openExceptions}
/>
```

### It is a pattern, not a card

One rule beneath it. No fill, no shadow, no radius, no icon. It sits directly under
`WorkspaceContextBar` and above the dominant work plane, and it is the only place a screen states
identity. A second identity block anywhere below it is a defect.

### Evidence and exceptions are entry points

`onOpenEvidence` and `onOpenExceptions` turn those two facets into buttons. They are the header's
only interactive elements, because they are the only facets that lead somewhere: the evidence rail
and the exception queue. Coverage renders in the negative color when incomplete; exceptions render
in the negative color when blocking. Neither ever animates.

### One description, four surfaces

`describeObject` normalizes any record into the contract, so the header, a chip, a breadcrumb, and a
search result describe the same object with the same words. When a list row and a detail header
disagree about an object's name, that is a normalization failure, not a copy decision.

---

## Rules of use

**Declare the type, don't restate the ledger.** `type="journal"` already means the identifier is
labelled *Entry* and the material value is *Debit total*. Override `identifierLabel` only where the
institution genuinely uses another key (ISIN over CUSIP).

**Context on the header is object context, not screen scope.** Entity, period, basis, and currency
belong to `WorkspaceContextBar` — the screen declares those once. The header carries only the facets
that qualify *this object* within that scope: its portfolio, its book, its custodian.

**As-of comes from `TimeContext`.** The header's as-of is the `asOf` role, not a locally formatted
date string. If the object's material value is a valuation, the screen also owes the `valuation`
role somewhere visible (`guidelines/TIME_SEMANTICS.md`).

**Version is required wherever a decision is possible.** Any screen that can post, resolve, approve,
or attest is acting on a specific revision. A decision taken against an unnamed version cannot be
audited.

---

## Anti-patterns

| Don't | Why |
| --- | --- |
| A bespoke identity block per workstation | The operator relearns where the identifier lives on every screen |
| Identity rendered as a card with fill and shadow | Identity is not a module; it frames the plane, it does not compete with it |
| Coverage as a bare percentage | "97%" of what population, weighted how? Use `SourceCoverageSummary` semantics |
| `condition="ok"` | The eleven data conditions are the vocabulary; "ok" is not one of them |
| Blank facets to keep the row even | An empty facet asserts a fact about the object |
| Terracotta on any header facet | Brand signal is identity and scenario only; flags use the semantic negatives |
