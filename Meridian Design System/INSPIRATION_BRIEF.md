# Inspiration Brief

This brief records how attached and bundled visual references should influence
Meridian design-system work. The imported June 2026 package shifts the durable
direction to a light WPF-first institutional workstation: paper canvas, white
cards, near-black chrome bars, muted teal-blue accent, desaturated status
washes, hairline borders, and compact operator density.

## Bundled References

| Reference | What it contributes |
| --- | --- |
| `uploads/ChatGPT Image Apr 24, 2026, 03_58_29 PM.png` | Workstation mood reference: persistent chrome, KPI-to-evidence flow, chart/table pairing, and compact operational framing. |
| `scraps/acct-ledger.png` | Accounting density reference for ledger rows, balances, and inspection surfaces. |
| `scraps/acct-template-check.png` | Accounting template check for reconciliation and journal-entry composition. |
| `components/` | Reusable React primitives, data tables, charts, shell chrome, and accounting widgets for package consumers. |
| `templates/` | Copyable workstation starting points for dashboard, charting, accounting, and Security Master registry views. |
| `guidelines/*.card.html` | Foundation cards for brand marks, icons, colors, spacing, typography, and chart encodings. |

## Design Deductions

1. Use workstation chrome for operator context. The package favors a persistent
   brand bar, left rail, and status bar over isolated card-only pages.

2. Pair every summary with evidence. KPI cards need chart, table, ledger,
   delivery, or audit records nearby so the operator can explain the number.

3. Keep data dense and inspectable. Dense tables, expandable rows, split
   workbenches, selected-record details, and tabular numeric alignment are part
   of the brand.

4. Prefer light institutional surfaces. Use the paper canvas and white panels
   from `tokens/colors.css`; reserve near-black for chrome bars, not full-page
   ambience.

5. Keep accounting arithmetic visible. Ledger, reconciliation, statement, tax
   lot, and journal-entry examples should show balance, imbalance, totals,
   source records, and posting readiness without decorative noise.

6. Treat uploaded images as reference material only. Do not copy third-party
   labels, navigation, marks, or proprietary layouts. Convert useful structure
   into Meridian tokens, copy rules, and components.

## Components This Brief Should Influence

- `WorkstationTopbar`, `NavRail`, and `StatusBar` for application chrome.
- `MetricCard`, `DenseDataTable`, `ExpandableDataTable`, `FilteredDataTable`,
  and `EntitySummary` for KPI-to-evidence workflows.
- `AmountCell`, `LedgerTable`, `AccountTree`, `StatementTable`,
  `ReconciliationPanel`, `JournalEntryForm`, and `TaxLotTable` for accounting
  flows.
- `CandleChart`, `EquityCurve`, `ChartCard`, and `Sparkline` for chart-heavy
  workstations.
- `templates/*-workstation/` and `ui_kits/*/` for end-to-end page composition.

## Copy Guidance

- State the fact, then the evidence: `Backfill complete - 412,008 bars - 0 gaps`.
- Name workflow state precisely: `Balanced`, `Out by 1,204.33`,
  `Ready for review`, `Published`, `Fixture`, `Paper`, `Live`.
- Use mono formatting for identifiers, prices, row counts, and UTC timestamps.
- Avoid marketing copy, exclamation marks, emoji, apologies, and decorative
  instruction text.
