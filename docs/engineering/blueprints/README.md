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
| [Repo Engine, Depreciation Schedule, and Borrower-Side Debt](financing-liabilities-depreciation-blueprint.md) | `docs/engineering/blueprints/` | Ledger / financing | **Partially implemented** — depreciation *calculation core and governed-draft seam* shipped (`DepreciationScheduleCalculator`, `FixedAssetDepreciationProjector`, `FixedAssetDepreciationDraftBuilder` with a submittable-approval test, `AutomatedJournalEventKind.DepreciationPosted`). No fixed-asset store, migration, orchestrating service, endpoints, or read model yet; repo and borrower-side debt remain design-only |
| [Full Incentive-Fee Mechanics](../../development/accounting-blueprints/incentive-fee-mechanics.md) | `docs/development/accounting-blueprints/` | Ledger / fund accounting | **Design** — no incentive-fee policy, hurdle calculator, or durable HWM/LCF state in source |
| [Commitment & Capital-Call Engine](../../development/accounting-blueprints/commitment-and-capital-call-engine.md) | `docs/development/accounting-blueprints/` | Ledger / private capital | **Partially implemented** — domain and posting layers shipped (`PrivateCapitalCommitments`, `CommitmentRollForwardCalculator`, `CapitalCallDraftFactory`, `CapitalCallPlanBuilder`); persistence, endpoints, and workbench remain design-only |
| [Equalization / Series Accounting](../../development/accounting-blueprints/equalization-and-series-accounting.md) | `docs/development/accounting-blueprints/` | Ledger / fund accounting | **Partially implemented** — `EqualizationCalculator` shipped as an *entry-exposure helper only* (no `GAV_cryst`, so it is not the §5.1/§5.2 crystallization math); lot-level Method A, Method B series accounting, persistence, and endpoints remain design-only |
| [Portfolio Cash Ladder](../../product/portfolio-cash-ladder-blueprint-2026-07.md) | `docs/product/` | Portfolio forecasting | **Partially implemented** — compute-on-request vertical slice shipped; persisted runs, per-currency views, and structured sourcing remain design-only |
| [Quote-stream fan-out](../../product/web-ui-stream-fan-out-blueprint-2026-07.md) | `docs/product/` | Workstation shell | **Implemented** — PRs A–C shipped; PR D was rescoped into the report-run stream blueprint |
| [Report-run status stream](../../product/web-ui-report-run-stream-blueprint-2026-07.md) | `docs/product/` | Workstation shell / reporting | **Implemented, one open divergence** — D1/D3 as designed; D2's shared SSE helper landed additively, so `WorkstationEndpoints.Stream.cs` still has a duplicate loop and `ResolveStreamSessionId` |
| [Report Writer Debounced Live Auto-Preview](../../plans/report-writer-auto-preview-blueprint.md) | `docs/plans/` | Browser workstation | **Design** — `reporting-screen.report-writer-auto-preview.ts` does not exist in source |
| [Security Master Passport Workbench](../../plans/security-master-passport-workbench.md) | `docs/plans/` | Data confidence / accounting | **Largely implemented** — Phases 1–4 shipped (governed-write DTOs, `ISecurityMasterConflictAuthorityPolicy`, `ISecurityMasterWorkbenchCommandService`, `WorkstationEndpoints.SecurityMasterWorkbench.cs`, `SecurityMasterWorkbenchOptions`, WPF `SecurityPassportEditorViewModel`). Open (`[~]`): browser `security-passport-editor.tsx`, `IRestatementCandidateResolver` follow-ons (repeated restatement, `IGovernedLedgerAdjustmentPoster`, durable security→report-line index), full lifecycle integration tests, ADR record |

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
under `/api/workstation/...`; reporting and fund-structure surfaces under `/api/fund-structure/...`;
instrument reference-data surfaces under `/api/security-master/...`.
Introducing a new top-level prefix is a decision to make once, in this register, not per blueprint.

### Enum extension

`AutomatedJournalEventKind` and `ManualJournalEntryTypeDto` are append-only shared enums. Check the
live enum before claiming a member is new, and check sibling blueprints before claiming an explicit
ordinal. Current tail of `ManualJournalEntryTypeDto` is `ClosingEntry = 15`; the equalization
blueprint reserves `16–18`.

The same applies to enums a blueprint might mistake for its own. `Meridian.Ledger.EqualizationMethod`
already ships in `ShareClass.cs` (`None = 0`, `Equalisation = 1`) and gates the Method A path in
`ShareClassUnitRegisterProjector`; the equalization blueprint appends `SeriesOfShares = 2` rather
than redeclaring it. **Before writing `public enum X` in a blueprint, grep `src/` for `enum X`.**

### DTO layering

`Meridian.Contracts` has **no `ProjectReference` at all** — it is a leaf, and the graph runs
`Meridian.Ledger` → `Meridian.Core` → `Meridian.Contracts`. A DTO placed in
`Meridian.Contracts.*` therefore cannot be typed with a domain enum or record from
`Meridian.Ledger`; doing so needs a Contracts→Ledger reference and inverts the graph.

Contracts owns its own wire types and the application service maps at the boundary. This is also
what keeps a grandfathered domain spelling (below) off the public contract. Before writing a DTO
signature in a blueprint, check which project each named type actually lives in.

### Terminology

Use **US spelling in code identifiers, routes, and wire contracts** (`Crystallize`,
`Equalization`). UK spelling in narrative prose is tolerated, but a blueprint must not let the two
spellings reach an interface, a route segment, or a column name, because the incentive-fee and
equalization engines share one crystallization boundary.

**Check for a collision before normalising a name to US spelling.** A UK-spelled proposed identifier
can become a duplicate of a *shipped* US-spelled type in the same namespace. This already bit the
equalization blueprint once: its proposed lot-level `EqualisationAdjustment` normalised to
`EqualizationAdjustment`, which `Meridian.Ledger` already defines in `EqualizationCalculator.cs`
with an incompatible shape. It is now `EqualizationLotAdjustment`.

This convention is **not yet uniformly true of source**, and a blueprint must not claim otherwise.
The shipped type is `EqualizationCalculator` / `EqualizationAdjustment` (US), but its members are
`EqualisationCredit` and `HasEqualisationCredit` (UK), as is the `EqualisationCredit` field on
`ShareClassUnitRegisterProjector`. Those three are **grandfathered** — they keep UK spelling until a
deliberate rename lands with its own migration and PR. New identifiers use US spelling and must not
copy them; the equalization blueprint carries the exception table.

### Cross-blueprint contracts

Where two blueprints touch the same state, the contract is recorded in **both** documents, not
inferred. Current recorded contract:

- **High-water-mark ownership** — incentive-fee `Fork G` and equalization `§9`.
  **`incentive_fee_state` is the single durable owner of the HWM under both equalization methods**
  (incentive-fee §7.2). Fork G selects the *scope* of a row, not a different store:
  `FundLevel` + Method A ⇒ one row per book, `series_id is null`; `InvestorSeries` + Method B ⇒ one
  row per series, `series_id` set. The scope is a **series, never an investor**; per-investor HWM
  rows are not permitted under either method.

  **The HWM is stored per share in both methods** (`high_water_mark_per_share`). A total-NAV HWM is
  not invariant to capital flows — a subscription raises ending NAV without being gain, so the
  projector would charge fee on contributed capital, and Method A's equalisation step cannot correct
  it because that step preserves the projector's total and only redistributes it.
  `PartnershipInvestorAccountingProjector` works in total-NAV terms only, so **every** call scales in
  (`× unitsOutstanding`) and scales the candidate back out (`÷ unitsOutstanding`) before persisting
  (equalization §6.1, incentive-fee §5.4). At zero units — a scope fully redeemed on a
  crystallization date — do not divide: price on pre-redemption units and close the scope.

  **Scope lifecycle.** `incentive_fee_state` carries a `status` (`Live` / `Closed` /
  `Consolidated`); only `Live` scopes are hydrated, and the unique key is partial on it. Opening,
  consolidating, and closing a scope are **single transactional adapter operations**
  (`CreateSeriesWithStateAsync`, `ConsolidateSeriesStateAsync`, `CloseIncentiveFeeStateAsync`), not
  sequences a caller composes — two store calls cannot give all-or-nothing, and a crash between them
  leaves a series with no protected level.

  Two things are explicitly *not* HWM owners, both of which an earlier draft of this contract got
  wrong. `PartnershipInvestorAllocationInput.HighWaterMark` is a `sealed record` constructor
  parameter — a transient per-period projector input hydrated from `incentive_fee_state`, not a
  store. And equalization's `fund_series` table carries **no** HWM column: the previously proposed
  `fund_series.high_water_mark_per_share` is removed, because two stores let crystallization advance
  one HWM while the next fee calculation reads the other.

## Adding a blueprint

1. File it next to its owning lane (`docs/engineering/blueprints/`,
   `docs/development/accounting-blueprints/`, or `docs/product/` for product-direction designs).
2. Add a lifecycle header (`Status`, `Owner`, `Reviewed`) — `validate-docs-structure.py` warns
   without one.
3. Add a row to the register above and to the lane's own index.
4. Check the shared conventions before claiming a migration ordinal, route prefix, or enum ordinal.
5. Re-check delivery state when the lane ships; a blueprint that still says "design-only" after its
   code has landed is a defect in this register.
