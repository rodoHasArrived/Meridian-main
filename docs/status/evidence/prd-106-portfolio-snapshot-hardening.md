# PRD-106 portfolio and corporate-action snapshot hardening review

**Status:** Review required

**Owner:** Backtesting and Accounting reviewers

**Reviewed:** 2026-09-02

**Prepared:** 2026-09-02
**Production claim:** None

## Decision requested

This re-cut requires independent human approval from both the Backtesting and Accounting owners.
The approvals are separate: neither review substitutes for the other, and a green automated gate
does not supply either signoff.

| Review owner | Required focus | Exit evidence |
| --- | --- | --- |
| Backtesting | Exact filtered JSONL capture, one-time adjustment preparation, deterministic replay, cancellation/cleanup, effective-date and authority filtering, and directed short lots in snapshots | Named human approval on the re-cut head with no unresolved correctness finding |
| Accounting | Aggregate/lot quantity agreement, reverse-split retained-basis conservation, fractional cash-in-lieu behavior, multi-account transformation and cash evidence, and profitable/loss-making short economics | Named human approval on the re-cut head with no unresolved accounting finding |

## Scope and PRD linkage

This change is one focused hardening slice under
[`PRD-106`](../../product/implementation-todo-list.md): it makes replay preparation and execution use
one captured market-data window and makes portfolio snapshots preserve the accounting facts needed
by downstream Backtesting and Security Master readers. It does not complete PRD-106's broader host
composition, browser/WPF parity, bounded batch concurrency, cancellation, recovery, or durable
terminal-state retry requirements.

Included:

- immutable per-symbol corporate-action adjustment plans prepared from the complete filtered bar
  window and one captured Security Master corporate-action query result;
- execution replay from the exact captured JSONL event snapshot used for preparation;
- rejection of cancelled or merely announced actions and exclusion of actions whose effective date
  is after the request's `To` boundary;
- additive `OpenLot.IsShort` direction through portfolio, Security Master workstation DTO, and
  browser type mirrors, with short unrealized P&L calculated as entry price minus mark;
- FIFO whole-unit reverse-split lot apportionment that carries each source entitlement's basis
  across lot boundaries, creates deterministic composite lot/fill identities when fractions
  combine, and retains each component's source lot, fill, acquisition date, exact entitlement, and
  basis through chained actions;
- account-scoped cash-flow and ledger evidence for fractional basis relief and cash-in-lieu realized
  gain/loss, including the all-fraction case where no successor position remains;
- asset-event transformation and cash evidence for every brokerage account holding the source
  symbol, including successor lot symbols.

Excluded:

- order lifecycle, fill-model, commission, performance-metric, QuantScript, and broader browser
  behavior;
- changes to the corporate-action command, approval, canonical-chain, or posting contracts landed
  by the existing corporate-action work;
- transaction-time reconstruction of Security Master corporate-action revisions.

## Snapshot boundary and known limitation

For an adjusted run, the engine first writes the date- and symbol-filtered event stream to a unique
temporary JSONL snapshot. It prepares one adjustment plan from all historical bars in that file and
one Security Master query result, then executes from the snapshot rather than reopening the source
partition. The plan's content version hashes the captured bars, economic cutoff, and effective
action terms.

The request `To` timestamp is an **economic effective-through cutoff**, not a storage transaction-
time revision boundary. The current Security Master query contract does not expose revision
`recorded_at` metadata or an as-recorded-at query. Consequently, the plan consistently freezes the
effective corporate-action chain returned when preparation begins, but it cannot reconstruct which
amendment rows were known historically at the request's `To` timestamp. Adding that capability
requires a separately reviewed, first-class bitemporal query contract; this slice intentionally does
not attach hidden metadata to DTO instances or replace the current corporate-action contracts.
Adjustment-service implementations that expose only the legacy unbounded `AdjustAsync` seam now
fail closed when a bounded plan is requested; they must implement `PrepareAsync` before serving an
adjusted engine run so the logged cutoff cannot overstate what the implementation enforced.

The engine logs the prepared plan content version, bar count, symbol, and cutoff, but the canonical
`BacktestResult` does not yet retain that plan version as durable run lineage. A completed result
therefore cannot independently prove its adjustment plan after logs expire. Closing that gap needs a
separately reviewed additive result-lineage contract and remains an open PRD-106 hardening action.

Canonical `BacktestMetrics.SymbolAttribution` remains fill-derived. It does not replay split or
other corporate-action quantity/basis transformations before later fills, and it does not attribute
cash-in-lieu realized P&L. The event-day `AssetEventCashFlow`, portfolio realized balance when a
successor exists, and account ledger retain the cash-in-lieu accounting result, but a consumer must
not use symbol attribution to reconcile any transformed lot sequence. Adding corporate-action-aware
metric attribution is intentionally excluded from this slice and remains an open PRD-106
reconciliation action.

The temporary snapshot is deleted in the iterator's `finally` path. Review must confirm cancellation
and exceptional disposal exercise that path on hosted runners.

## Required fresh gates

Every re-cut head must produce fresh results. Evidence inherited from PR #2789 or from an earlier
head is not acceptable.

| Gate | Requirement |
| --- | --- |
| Meridian CI (.NET/browser/docs) | Required, green on the exact reviewed head |
| Windows Desktop | Required, green on the exact reviewed head |
| WPF Dev Loop | Required, green on the exact reviewed head |
| WPF Route Validation | Required, green on the exact reviewed head |
| CodeQL C#/JS | Required, green on the exact reviewed head |
| Documentation Automation | Required, green on the exact reviewed head |
| Roadmap Source Docs | Required, green on the exact reviewed head |

Focused evidence must include the following suites or equivalent narrower filters:

```bash
dotnet test tests/Meridian.Backtesting.Tests/Meridian.Backtesting.Tests.csproj --filter "FullyQualifiedName~PortfolioCorporateActionSnapshotTests|FullyQualifiedName~CorporateActionAdjustmentServiceTests|FullyQualifiedName~BacktestEngineIntegrationTests"
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~WorkstationEndpointsTests"
cd src/Meridian.Ui/dashboard && npm run test:vitest -- src/screens/accounting-screen.view-model.test.ts src/screens/accounting-screen.test.tsx
```

Reviewers should retain the exact commit SHA, test summaries, and both named human decisions with
the pull request evidence.

## W6 boundary

`W6-BTSTUDIO-001` remains unchanged: it is complete only for the bounded host-composed browser
Covered Call Evidence Vault and governed Paper-promotion loop already recorded in the roadmap. This
hardening does not broaden that evidence, reopen W6, certify broader Studio UX, or satisfy PRD-106's
remaining orchestration exit criteria. No roadmap registry or W6 status change is requested.
