# Engineering Blueprints

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-08-01

Code-ready technical design documents produced by Blueprint Mode. Each blueprint translates a
prioritized idea into named interfaces, component designs, data flows, a test plan, and an
implementation checklist grounded in Meridian's actual stack.

This README is the **canonical register for every active blueprint in the repository**, wherever it
is filed. Blueprints live next to their owning lane, so there is more than one folder; there is only
one register. Add new blueprints here as well as to their lane index.

## Register

| Blueprint | Home | Lane | Delivery state |
|---|---|---|---|
| [Repo Engine, Depreciation Schedule, and Borrower-Side Debt](financing-liabilities-depreciation-blueprint.md) | `docs/engineering/blueprints/` | Ledger / financing | **Partially implemented** — depreciation shipped (`FixedAssetDepreciationProjector`, `DepreciationScheduleCalculator`, `AutomatedJournalEventKind.DepreciationPosted`); repo and borrower-side debt remain design-only |
| [Full Incentive-Fee Mechanics](../../development/accounting-blueprints/incentive-fee-mechanics.md) | `docs/development/accounting-blueprints/` | Ledger / fund accounting | **Design** — no incentive-fee policy, hurdle calculator, or durable HWM/LCF state in source |
| [Commitment & Capital-Call Engine](../../development/accounting-blueprints/commitment-and-capital-call-engine.md) | `docs/development/accounting-blueprints/` | Ledger / private capital | **Partially implemented** — domain and posting layers shipped (`PrivateCapitalCommitments`, `CommitmentRollForwardCalculator`, `CapitalCallDraftFactory`, `CapitalCallPlanBuilder`); persistence, endpoints, and workbench remain design-only |
| [Equalization / Series Accounting](../../development/accounting-blueprints/equalization-and-series-accounting.md) | `docs/development/accounting-blueprints/` | Ledger / fund accounting | **Partially implemented** — single-NAV `EqualizationCalculator` shipped; lot-level Method A, Method B series accounting, persistence, and endpoints remain design-only |
| [Portfolio Cash Ladder](../../product/portfolio-cash-ladder-blueprint-2026-07.md) | `docs/product/` | Portfolio forecasting | **Partially implemented** — compute-on-request vertical slice shipped; persisted runs, per-currency views, and structured sourcing remain design-only |
| [Quote-stream fan-out](../../product/web-ui-stream-fan-out-blueprint-2026-07.md) | `docs/product/` | Workstation shell | **Implemented** — PRs A–C shipped; PR D was rescoped into the report-run stream blueprint |
| [Report-run status stream](../../product/web-ui-report-run-stream-blueprint-2026-07.md) | `docs/product/` | Workstation shell / reporting | **Implemented** — D1–D3 shipped |
| [Report Writer Debounced Live Auto-Preview](../../plans/report-writer-auto-preview-blueprint.md) | `docs/plans/` | Browser workstation | **Design** — `reporting-screen.report-writer-auto-preview.ts` does not exist in source |

Delivery state is a documentation-coherence marker, not roadmap truth. Live status stays in the
roadmap registry (`docs/roadmap/README.md`, `docs/roadmap/data/*.yml`).

The design-system Workstation Template Blueprint
(`Meridian Design System/guidelines/WORKSTATION_BLUEPRINT.md`) is deliberately **not** in this
register: it is a scaffolding guide for design-system prototype templates under
`Meridian Design System/templates/`, not a Blueprint Mode feature design, and it does not govern the
production browser workstation in `src/Meridian.Ui/dashboard/`.

## Shared conventions

Blueprints are written independently but land in one repository. These conventions exist so two
blueprints cannot both be "correct" and still collide at implementation time. A blueprint that
needs to deviate must say so explicitly and name the blueprint it is deviating from.

### Ledger migration ordinals

`src/Meridian.Storage/Ledger/Migrations/` uses ordered `V_ledger_###__snake_name.sql` scripts
auto-discovered by `LedgerMigrationRunner`. Ordinals are a **global, shared resource** — a blueprint
that hard-codes one without reserving it will collide with both its siblings and with whatever has
shipped since it was written.

**Highest ordinal on disk: `V_ledger_028__wash_sale_activation.sql`** (note `008` is used twice;
keep new ordinals unique).

Reservations for the in-flight blueprints:

| Range | Reserved by |
|---|---|
| 029–030 | [Incentive-fee mechanics](../../development/accounting-blueprints/incentive-fee-mechanics.md) — policy, state |
| 031–032 | [Commitment & capital-call engine](../../development/accounting-blueprints/commitment-and-capital-call-engine.md) — commitments, expiry events |
| 033–035 | [Equalization / series accounting](../../development/accounting-blueprints/equalization-and-series-accounting.md) — policy, subscription lots, fund series |

Re-derive the next free ordinal from disk at implementation time and update this table if an
unrelated lane lands first. Do not renumber a migration that has already shipped.

### DDL precision

Ledger migrations use exactly two numeric precisions. New subsidiary-ledger tables must use them:

- `numeric(38, 12)` — money, rates, per-share values, quantities, and unit costs in new blueprint
  tables (precedent: `V_ledger_009__tax_lot_persistence.sql`).
- `numeric(38, 10)` — reserved for journal-leg columns (`debit`, `credit`,
  `fx_rate_to_functional`) that must match the shape of `ledger_entries`.

Do not introduce a third precision. The C# layer keeps `decimal` and rounds for presentation and
posting; storage precision is not the rounding policy.

### API route prefixes

`UiApiRoutes` has no `/api/accounting/` or `/api/financing/` prefix. Ledger, fund-accounting, and
financing surfaces belong under the existing **`/api/ledger/...`** prefix (62 routes today; e.g.
`/api/ledger/private-capital/...`, `/api/ledger/close-management`). Workstation read models belong
under `/api/workstation/...`; reporting and fund-structure surfaces under `/api/fund-structure/...`.
Introducing a new top-level prefix is a decision to make once, in this register, not per blueprint.

### Enum extension

`AutomatedJournalEventKind` and `ManualJournalEntryTypeDto` are append-only shared enums. Check the
live enum before claiming a member is new, and check sibling blueprints before claiming an explicit
ordinal. Current tail of `ManualJournalEntryTypeDto` is `ClosingEntry = 15`; the equalization
blueprint reserves `16–18`.

### Terminology

Use **US spelling in code identifiers, routes, and wire contracts** (`Crystallize`,
`Equalization`) — this is what the shipped `EqualizationCalculator` already uses. UK spelling in
narrative prose is tolerated, but a blueprint must not let the two spellings reach an interface, a
route segment, or a column name, because the incentive-fee and equalization engines share one
crystallization boundary.

### Cross-blueprint contracts

Where two blueprints touch the same state, the contract is recorded in **both** documents, not
inferred. Current recorded contract:

- **High-water-mark ownership** — incentive-fee `Fork G` and equalization `§9`. There is exactly one
  HWM store per fund. `Fork G = FundLevel` pairs with equalization Method A (single fund HWM, fee
  *reallocated* across subscription lots, no per-investor HWM rows). `Fork G = InvestorSeries` is
  realized by equalization Method B (per-series HWM in `fund_series.high_water_mark_per_share`).
  Incentive-fee `§5.3` durable state carries loss-carryforward and accrual history against whichever
  scope that choice selects; it does not introduce a competing per-investor HWM.

## Adding a blueprint

1. File it next to its owning lane (`docs/engineering/blueprints/`,
   `docs/development/accounting-blueprints/`, or `docs/product/` for product-direction designs).
2. Add a lifecycle header (`Status`, `Owner`, `Reviewed`) — `validate-docs-structure.py` warns
   without one.
3. Add a row to the register above and to the lane's own index.
4. Check the shared conventions before claiming a migration ordinal, route prefix, or enum ordinal.
5. Re-check delivery state when the lane ships; a blueprint that still says "design-only" after its
   code has landed is a defect in this register.
