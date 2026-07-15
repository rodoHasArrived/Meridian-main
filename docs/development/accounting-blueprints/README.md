# Accounting-system Tier 1 blueprints

Code-ready technical designs for the larger accounting-engine gaps identified in the
accounting-system review. Each blueprint is grounded in the current Meridian source (real type
names, signatures, and the projector → governed-draft → approval pattern) and is intended to be
implementable directly: it specifies policy forks with recommended defaults, C# domain sketches,
persistence/migrations, projector and governed-draft integration, endpoints and UI surfaces, a
test plan, and an ordered implementation checklist.

These three are **design-only** (not yet implemented) because each is a new domain with real
product/policy decisions that should be confirmed before build:

| Blueprint | Scope |
|---|---|
| [incentive-fee-mechanics.md](incentive-fee-mechanics.md) | Hurdle rates (soft/hard + GP catch-up), first-class crystallization schedules, and durable per-investor high-water-mark / loss-carryforward state — replacing today's pass-in HWM. |
| [commitment-and-capital-call-engine.md](commitment-and-capital-call-engine.md) | Investor commitments, drawdown schedules, uncalled-commitment roll-forward (`net-called + uncalled + expired = total`), recallable distributions, and default / late-interest handling. |
| [equalization-and-series-accounting.md](equalization-and-series-accounting.md) | Equalisation credit/debit vs. series-of-shares accounting for open-end funds with mid-period subscriptions, so performance-fee equity is fair across investors who entered at different NAVs. |

## Related — already implemented

The self-contained Tier 1 gaps from the same review are implemented in the ledger accounting
engine (see `src/Meridian.Ledger` and `src/Meridian.Application/Accounting`):

- **Cost-basis completeness** — `AverageCost` relief method and wash-sale loss deferral
  (`WashSalePolicy` / `WashSaleOutcome`) in `LedgerTaxLotReliefProjector`.
- **Valuation-policy rigor** — ASC 820 `FairValueLevel`, `StalePricePolicy`, and
  `WaterfallMarkPriceSource`.
- **Corporate-action reversals** — `LedgerJournalReversal` and the opt-in
  `AutoReverseSupersededPostings` bridge path.
