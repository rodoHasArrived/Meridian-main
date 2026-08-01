# Accounting-system Tier 1 blueprints

**Status:** active
**Owner:** Ledger / fund-accounting lane
**Reviewed:** 2026-08-01

Code-ready technical designs for the larger accounting-engine gaps identified in the
accounting-system review. Each blueprint is grounded in the current Meridian source (real type
names, signatures, and the projector → governed-draft → approval pattern) and is intended to be
implementable directly: it specifies policy forks with recommended defaults, C# domain sketches,
persistence/migrations, projector and governed-draft integration, endpoints and UI surfaces, a
test plan, and an ordered implementation checklist.

These three blueprints share the ledger migration sequence, the `AutomatedJournalEventKind` enum,
and the fund high-water mark. Read the shared conventions and the recorded cross-blueprint contracts
in [`docs/engineering/blueprints/README.md`](../../engineering/blueprints/README.md) — the canonical
blueprint register — **before** claiming a migration ordinal, route prefix, or enum ordinal here.

| Blueprint | Scope | Delivery state |
|---|---|---|
| [incentive-fee-mechanics.md](incentive-fee-mechanics.md) | Hurdle rates (soft/hard + GP catch-up), first-class crystallization schedules, and durable per-investor high-water-mark / loss-carryforward state — replacing today's pass-in HWM. | **Design** — nothing from this blueprint is in source yet |
| [commitment-and-capital-call-engine.md](commitment-and-capital-call-engine.md) | Investor commitments, drawdown schedules, uncalled-commitment roll-forward (`net-called + uncalled + expired = total`), recallable distributions, and default / late-interest handling. | **Partially implemented** — see below |
| [equalization-and-series-accounting.md](equalization-and-series-accounting.md) | Equalisation credit/debit vs. series-of-shares accounting for open-end funds with mid-period subscriptions, so performance-fee equity is fair across investors who entered at different NAVs. | **Partially implemented** — see below |

Each blueprint still carries real product/policy decisions (the policy forks) that should be
confirmed before its remaining phases are built.

## What has already landed

The delivery-state column is documentation coherence, not roadmap truth — live status stays in the
roadmap registry (`docs/roadmap/README.md`).

**Commitment & capital-call engine** — the domain and posting layers shipped ahead of the rest of
the blueprint:

- `src/Meridian.Ledger/PrivateCapitalCommitments.cs` — `CommitmentStatus`,
  `DrawdownInstallmentStatus`, `DistributionRecallability`, and the commitment/installment records
  (blueprint §4.1–§4.3).
- `src/Meridian.FinancialOperations/PrivateCapital/CommitmentRollForwardCalculator.cs` — the
  `net-called + uncalled + expired = total` invariant carrier (blueprint §4.4).
- `src/Meridian.Ledger/CapitalCallDraftFactory.cs`, `CapitalCallPlanBuilder.cs`, and
  `CapitalCallScheduleDraftBuilder.cs` — governed drafting (blueprint §7.2).
- `AutomatedJournalEventKind.CapitalCallIssued` / `CapitalCallFunded` /
  `CapitalCallDefaultInterestAccrued` are already on the enum.

Still design-only: the migration and stores (§6), the endpoints (§8.3), and the commitment
workbench read model (§8.2/§8.4).

**Equalization / series accounting** — `src/Meridian.Ledger/EqualizationCalculator.cs` ships the
single-NAV equalisation credit / contingent-redemption math against a class high-water mark. The
lot-level Method A projection (§5, §7.2), Method B series accounting (§6, §7.3), persistence (§10),
and endpoints (§12.2) are design-only.

**Incentive-fee mechanics** — nothing yet. The engine still computes a fund-level HWM performance
fee inside `PartnershipInvestorAccountingProjector.Project` with the HWM passed in per period,
exactly as the blueprint describes today's behavior.

## Related — already implemented

The self-contained Tier 1 gaps from the same review are implemented in the ledger accounting
engine (see `src/Meridian.Ledger` and `src/Meridian.Application/Accounting`):

- **Cost-basis completeness** — `AverageCost` relief method and wash-sale loss deferral
  (`WashSalePolicy` / `WashSaleOutcome`) in `LedgerTaxLotReliefProjector`.
- **Valuation-policy rigor** — ASC 820 `FairValueLevel`, `StalePricePolicy`, and
  `WaterfallMarkPriceSource`.
- **Corporate-action reversals** — `LedgerJournalReversal` and the opt-in
  `AutoReverseSupersededPostings` bridge path.
