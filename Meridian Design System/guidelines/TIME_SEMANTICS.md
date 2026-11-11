# Time semantics — the temporal contract

**Normative.** A number in Meridian is a claim about a moment. Twelve different moments appear in
the platform, and a figure stated in the wrong one is not imprecise — it is wrong. This document is
the law for which moment a surface means, how it says so, and what makes a date set impossible.

Contract: `components/core/TimeContext.jsx` · card: **Core › Temporal semantics** ·
API: `TimeContextProvider`, `TimeContextPanel`, `TimeRole`, `TIME_ROLES`, `TimeHooks`.

## The three clocks

Every role belongs to one of three clocks. Naming the clock is what stops "the date" from meaning
four things in one screen.

| Clock | Question it answers | Roles |
| --- | --- | --- |
| **In the world** | When did this happen? | `trade` · `settlement` · `effective` · `received` |
| **In the books** | When do we recognize it? | `posting` · `valuation` · `reportingPeriod` · `fiscalPeriod` · `closeDeadline` |
| **In this figure** | When is this number true? | `asOf` · `evidenceAt` · `calculatedAt` |

## The role ledger

| Role | Precision | Authority | Means |
| --- | --- | --- | --- |
| `trade` | date | Order management | The transaction was agreed. |
| `settlement` | date | Custodian | Cash and security actually change hands. |
| `effective` | date | Contract | The term, rate, or amendment starts to apply. |
| `received` | instant | Ingestion | This system first held the evidence. |
| `posting` | date | Ledger | The entry is recognized in the books. |
| `valuation` | date | Pricing | The prices and rates are drawn from this date. |
| `reportingPeriod` | period | Ledger | The period the figure is reported in. |
| `fiscalPeriod` | period | Entity calendar | The entity's own year and period — need not match the calendar. |
| `closeDeadline` | instant | Close calendar | The period stops accepting entries. |
| `asOf` | instant | Reporting | The instant the stated position is true for. |
| `evidenceAt` | instant | Source system | The source document or extract was produced. |
| `calculatedAt` | instant | Calculation engine | This figure was last computed. |

**Formatting is fixed.** Dates render `2026-07-31`. Instants render `2026-07-31 23:59:00Z` — UTC,
mono, explicit marker, never an implied local zone. Periods render verbatim (`2026-07`, `FY2026 · P07`).
`Timestamp` remains the primitive for a bare instant; `TimeRole` is the primitive for an instant
*that has a job*.

## Which roles a surface owes

| Surface | Required | Also expected |
| --- | --- | --- |
| Ledger, journal, posting preview | `posting` · `reportingPeriod` | `effective` · `closeDeadline` |
| Trade, blotter, fills, positions | `trade` · `settlement` | `asOf` |
| Valuation, analytics, risk figure | `valuation` · `asOf` · `calculatedAt` | `reportingPeriod` |
| Evidence, extract, statement | `evidenceAt` · `received` | `asOf` |
| Reconciliation, exception | `asOf` · `reportingPeriod` · `evidenceAt` | `settlement` · `closeDeadline` |
| Close, attestation | `reportingPeriod` · `fiscalPeriod` · `closeDeadline` | `calculatedAt` |

A surface declares what it knows and nothing else: `TimeContextPanel` renders only the roles the
context carries, so an absent role reads as absent rather than as zero.

## Coherence rules

`checkTimeCoherence(time, { now, closed })` is pure and cheap. Run it in the panel **and** in the
gate — an attestation must not be signable over an impossible date set.

| Rule | Severity | Why |
| --- | --- | --- |
| Settlement precedes trade | error | The record cannot be true |
| Figure calculated before its evidence | error | The number does not reflect the source it cites |
| Posting date outside its reporting period | error | The entry lands in a period the report will not show |
| Evidence arrived after the close deadline | warning | The period was measured without it |
| Position settles after the close deadline | warning | The period carries an unsettled position |
| Close deadline passed, period still open | warning | Late entries are accumulating |
| As-of precedes the trade it reports | warning | The report predates its own subject |
| Valuation date differs from the as-of date | info | Prices and position are from different days |
| Effective date after posting date | info | Expect a restatement |

`error` blocks. `warning` requires acknowledgement. `info` must be **visible** and must never be
resolved by rounding two dates together.

## Anti-patterns

- **One `date` prop.** A component that takes `date` forces the reader to guess the clock. Take the
  role by name, or take the context.
- **Local time, or an instant with no zone.** Every instant carries `Z`.
- **As-of standing in for calculated-at.** "As of Jul 31" says when the position is true; it says
  nothing about whether the number was recomputed after the evidence changed. Stale figures hide
  exactly here.
- **Period inferred from a date.** `2026-07-31` is not "July" — an adjustment period, a fiscal
  calendar, or a late entry breaks the inference. Carry `reportingPeriod` explicitly.
- **A blank cell for an unknown date.** Absent is a value; render `—` with the role still named.
- **Re-declaring time per panel.** Declare once with `TimeContextProvider`; `AccountingPeriodSelector`
  still owns *choosing* the period and `WorkspaceContextBar` still displays period and as-of in the
  chrome — all three read the same roles.
