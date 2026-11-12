# Value envelope

**One contract for what is known about a figure.** Before this, a single number
could be annotated by `DataCondition`, `SeverityBadge`, `Status`,
`FreshnessIndicator`, `ProvenanceChip`, `ValueAuthority`,
`MaterialityAssessment`, `TrustStrip`, `SlaChip` and `EvidenceLink` — ten
sibling vocabularies answering one operator question, each surface picking its own
subset. The envelope makes that question single:

> How much can I lean on this figure, and why?

## The contract

```ts
{
  value,        // pre-formatted text; null renders — or Missing
  label,        // what the figure is
  condition,    // one of the eleven conditions (guidelines/OPERATING_LANGUAGE.md)
  detail,       // qualifier folded into the notation: age, reason
  authority,    // source | derived | desk | override | estimate | unattributed
  evidence,     // [{ label, ref, href }]
  clocks,       // the time roles this value is stated in
  materiality,  // { material, threshold }
}
```

Five facets, no more, no fewer. **A figure that renders has an envelope**; a
figure without one is a bug, not a shortcut. Anything a surface wants to say
about a number goes into a facet — the answer to "where do I put this badge" is
never "beside the number."

## Three disclosure tiers, always the same three

| Tier | Component | What it shows |
| --- | --- | --- |
| 1 | `ValueEnvelope` | the value plus derived marks — condition glyph, ◈ authority, ⊞ evidence, ◷ clock, ‖ material, ▲ incoherent |
| 2 | its hover envelope | condition, authority, up to four clocks, evidence count, materiality, violations |
| 3 | `EnvelopePanel` | the full envelope in an inspector rail or drawer |

Marks are **derived, never authored**. A confirmed value with no authority and no
evidence renders with no marks at all — quiet is the reward for a clean figure.
Never add a mark by hand, and never stack a second provenance component beside a
`ValueEnvelope`.

## Coherence runs on the data

`ValueEnvelope.check(envelope)` (exported as `envelopeCheck`; consumers reach it off the carrier, since only capitalized exports land on the namespace) returns violations of five rules:

- `settlement-before-trade` — settles before it trades
- `calculated-before-evidence` — calculated before its evidence existed
- `posting-outside-period` — posted outside its reporting period
- `evidence-after-close` — evidence arrived after the close deadline
- `override-unattributed` — overridden with no authority recorded

Time roles therefore attach to the **value**, not the screen. A surface no longer
declares its clocks in parallel with the values it shows: `EnvelopeCoherence`
asks the envelopes present what is impossible. `TimeContextPanel` remains for
naming the period an operator is working in, not for restating each figure's
clocks. Role labels: `guidelines/TIME_SEMANTICS.md`.

## How the older components relate

`DataCondition`, `ProvenanceChip`, `ValueAuthority`, `FreshnessIndicator`,
`MaterialityBadge`, `SlaChip` and `EvidenceLink` are **renderers of one facet**
and stay supported — reach for one when a surface needs that facet alone (a
legend, a filter chip, a column header). When a figure is on screen, use the
envelope: it is the only construct that can state all five facets without
contradiction.

## Money surfaces reach the envelope through `AmountCell`

Accounting figures are formatted (currency, ISO minor units, accounting parens,
Dr/Cr) before they are stated, so `AmountCell` — the atom every accounting table
is built from — is the way in. Pass `envelope`, or carry it on the data:

```jsx
<AmountCell value={1290} currency="USD" envelope={{ condition: "estimated",
  detail: "declared rate", authority: "estimate", clocks: { settlement: "2026-07-10" } }} />

// or on the row data — LedgerTable, TrialBalance and StatementTable pass it through:
{ date: "06-09", ref: "JE-1060", debit: { value: 1290, condition: "estimated", authority: "estimate" } }
```

`LedgerTable` and `TrialBalance` rows also take one row-level `envelope` covering
the row's figures — the usual case, since a posting's provenance belongs to the
entry, not to one leg. Formatting stays in `AmountCell`, provenance stays in the
envelope, and the tables' arithmetic (totals, running balance, the Σdebit = Σcredit
proof, sort) reads the figure and ignores the facets. Never format a currency
string yourself to hand to `ValueEnvelope`.

## Rules

1. Every analytical figure renders through `ValueEnvelope` or carries an
   envelope object a grid cell renders.
2. Never stack facet components beside an envelope.
3. Never author a mark; author a facet.
4. Colour is never the only signal — every mark has a glyph and a title.
5. Numeric columns pass `align="right"`; the marks stay with the number.
6. A violation is shown where the value is, not only in the roll-up.
7. Colours come from real tokens — `--red` for violations, `--link` /
   `--link-hover` for evidence links, `--bg-surface` for the envelope popover.
   Verify names against `guidelines/TOKEN_REFERENCE.md`; an unresolved token
   silently freezes a light-mode hex into dark mode.
