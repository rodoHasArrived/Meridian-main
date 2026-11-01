# Synthetic Scenario Index

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-31

Index of the deterministic synthetic scenarios that exist in the repository today, built from the
[synthetic scenario contract](README.md#synthetic-scenario-contract). Every row was checked by
searching the tree for the fixture path or loader; nothing here is a claim about provider, bank,
custodian, or administrator behavior.

**Evidence label:** every scenario below is `synthetic-verified` for Meridian's own behavior at
most (see [provider evidence labels](../ai/solo-developer-workflow.md#provider-evidence-labels)).
None is `live-verified`. Where a consumer is "none found", the fixture is only self-checked or
not consumed at all, so it proves less than its name suggests.

"Seed or as-of" records only what the artifact states. "Not recorded" means the fixture or code
states no seed or date.

## Scenarios

| Scenario | Path | Coverage (institutions, accounts, events) | Seed or as-of | Consumers (verified by grep) | Outcomes asserted | Outcomes not asserted |
| --- | --- | --- | --- | --- | --- | --- |
| Corporate-action golden set (12 JSON scenarios plus `KNOWN-DEFECTS.md`) | `tests/fixtures/corporate-actions/golden/` | Single synthetic security per scenario: forward and reverse split, cash dividend (amended, cancelled, special, no prior bar, no position context), cash+stock merger, spin-off, bond call, MBS factor paydown, alias normalization. No institution or account. | Per-action `exDate` values (for example 2020-08-31, 2026-02-05); no seed; prices are described as synthetic approximations. | `tests/Meridian.Backtesting.Tests/CorporateActionGoldenAdjustmentTests.cs`, `CorporateActionKnownDefectTests.cs`; `tests/Meridian.Tests/SecurityMaster/CorporateActionGoldenLedgerTests.cs`, `CorporateActionLedgerKnownDefectTests.cs`; loader in `tests/Shared/CorporateActions/`. | Series continuity, factor monotonicity, notional conservation, a pinned prior-bar close, journal balance and idempotent posting (per `invariantTags`). | Holdings and cost basis across a multi-account book; reconciliation differences; close balances. Known defects are tracked, not fixed. |
| Fund-economics worked examples | `tests/fixtures/fund-economics/golden/fund-economics-worked-examples.json` | One fund model: management fee and expense accruals, performance fee with high-water mark, European waterfalls (100% and 80% catch-up), equalization, unit register for one equalising class. No institution. | Narrative gives period lengths; the only date field is `asOf` `2026-12-31` in the unit register; no seed. Figures are hand-computed. | `tests/Meridian.Tests/Ledger/FundEconomicsGoldenWorkedExampleTests.cs`, `FundEconomicsJournalFactoryTests.cs`; `tests/Meridian.FSharp.Tests/FundEconomicsTests.fs`; plus fixture self-check `tests/scripts/test_fund_economics_golden_consistency.py` (python, runs without dotnet). | Fees, accruals, waterfall tiers and cumulative LP/GP totals, equalization credits, unit-register balances (kernels vs fixture, dotnet). Python check: closed-form fees, tier conservation, cumulative totals, equalization. | Journals posted from these figures beyond the factory tests; administrator/NAV statement tie-out; bank cash. |
| Face-value-lot constant-yield grid | `tests/fixtures/schedules/golden/face-value-lot-constant-yield.json` (576 cases; `KNOWN-DEFECTS.md` beside it) | One lot (face 1,000,000, 5% coupon, par basis 100) across payment frequencies, day-count conventions, prices, and as-of positions. No institution. | Per-case `acquired`, `maturity`, `asOf` (for example 2026-06-15 to 2031-06-15); captured at base `7d072638`; no seed. | `tests/Meridian.Tests/SecurityMaster/FaceValueLotGoldenTests.cs` (writes a `.received.json` on drift); also `ScheduleParityGrids.cs`, `SchedulePeriodRollingRatchetTests.cs`. | Amortized basis per case (characterization of current behavior). | Independent expected values: this is a regression capture, not a hand-derived oracle. Journals, holdings, recon. |
| Security Master terms: version history | `tests/fixtures/security-master/terms-versions/` (32 asset classes), `terms-element-shapes/versioned-containers.json` | Terms payload shapes per asset class and version. No accounts. | Not recorded. | `tests/Meridian.Tests/SecurityMaster/SecurityAssetClassTermsHistoryTests.cs`; terms-versions also referenced in `tests/Meridian.Tests/Ui/DirectLendingEndpointsTests.cs`. | Schema/upgrade round trip of terms. | Holdings, basis, journals, recon, close balances (out of scope for the fixture). |
| Security Master terms: golden decode and write | `tests/fixtures/security-master/terms-golden/` | Schedule-convention decode and security-terms write shapes. | Not recorded. | `ScheduleConventionsDecodeGoldenTests.cs`, `SecurityTermsWriteGoldenTests.cs`, `SecurityMasterServiceSnapshotTests.AheadOfWrite.cs` (all under `tests/Meridian.Tests/SecurityMaster/`). | Decoded and written terms equal the golden. | Anything downstream of terms. |
| Security Master schedule validation | `tests/fixtures/security-master/schedule-validation/` (accepted, rejected, duplicate-properties, numeric-exactness) | Economic-schedule inputs that must be accepted or rejected. | Not recorded. | `tests/Meridian.Tests/SecurityMaster/SecurityEconomicScheduleFixtureCorpus.cs`. | Accept/reject outcome and numeric exactness. | Posting or valuation from accepted schedules. |
| Explorer parity fixture | `tests/fixtures/security-instrument-explorer-parity.json` | Security and instrument records for browser/WPF explorer parity. | Not recorded. | `tests/Meridian.Wpf.Tests/ViewModels/FinancialRecordExplorerViewModelTests.cs`; `src/Meridian.Ui/dashboard/src/components/meridian/financial-record-explorer.test.tsx`. | Explorer rows agree across clients. | Financial outcomes. |
| Mixed-credit portfolio status set | `tests/fixtures/portfolio/mixed-credit-status-set.json` (generator `generate_mixed_credit_status_set.py`, `source-payloads/`) | Synthetic funds, 50 direct loans, MBS packs, treasuries, corporate bonds, one equity, funded loan, undrawn credit line; 4 accounts, 52 borrower parties; payloads: servicer loan tape, custodian holdings, broker positions, pricing snapshot, bank statement (3 rows), lifecycle event feed. | as-of `2026-06-04`; schema 1.5.0; no seed recorded (generator is deterministic by construction but states no seed). | Only `tests/scripts/test_mixed_credit_status_set.py` (python). No C# test loads this set (searched `tests/**/*.cs`). | Fixture shape: composition counts, entity cross-references, payload files exist and record counts match the manifest, servicer tape has 50 loans and 3 late. | Meridian behavior on it: no ingestion, reconciliation, journal, or close-balance test runs against these payloads. Expected recon differences and close balances are not defined. |
| Statement reconciliation CSVs | `tests/Meridian.Tests/Reconciliation/Fixtures/` (`statement-clean-reconciles.csv`, `statement-unresolved-breaks.csv`, `statement-invalid-blockers.csv`) | Account `A1`, symbols SPY/QQQ, positions, cash, and dividends. | Trade dates 2026-05-28 and 2026-05-29; no seed. | `tests/Meridian.Tests/Reconciliation/StatementFixtureScenarioTests.cs`. | Match requires internal evidence, duplicate-header rejection, duplicate-statement blocker, broker mapping profile parse, invalid-row and run blockers. | Cost basis, journals, close balances; a custodian/bank end-to-end ending balance. |
| Provider golden path: ledger-backed recon break | `tests/Meridian.Tests/Infrastructure/Providers/Fixtures/Alpaca/alpaca-aapl-partial-fill-ledger-break.json`, `.../InteractiveBrokers/ibkr-aapl-partial-fill-ledger-break.json` | Alpaca and IB provider-shaped messages for an AAPL partial fill; expected canonical events, ledger lines, one break, evidence ids. Provider shapes follow public docs; no live network. | `generatedAtUtc` 2026-05-28 (Alpaca); message timestamps 2026-04-11; no seed. | `tests/Meridian.Tests/Integration/ProviderGoldenPathTransactionLedgerReconciliationTests.cs`. | Schema sections present, canonical events, replayed ledger lines, exactly one break, evidence ids. | Holdings and basis after settlement; close balances. Not live provider behavior (`live-untested`). |
| IB order samples | `tests/Meridian.Tests/Infrastructure/Providers/Fixtures/InteractiveBrokers/ib_order_*.json` (10) | Order types and time-in-force combinations. | Not recorded. | `tests/Meridian.Tests/Infrastructure/Providers/IBOrderSampleTests.cs`. | Order translation per sample. | Fills, positions, ledger. |
| Polygon recorded sessions | `tests/Meridian.Tests/Infrastructure/Providers/Fixtures/Polygon/polygon-recorded-session-*.json` (7) | AAPL, NVDA, SPY, GLD, TSLA, MSFT sessions plus an auth-failure/rate-limit session. Recorded-style, not live captures. | Timestamps inside frames (for example `timestampMs` 1742481000123); no seed. | `tests/Meridian.Tests/Infrastructure/Providers/PolygonRecordedSessionReplayTests.cs`. | Decoded trades, quotes, aggregates, ignored frames, failure handling. | Anything past the market-data decode boundary. |
| Canonicalization fixtures | `tests/Meridian.Tests/DataIntegration/Canonicalization/Fixtures/` (Polygon and Alpaca trade samples) | Regular, odd-lot, extended-hours, and XNAS identity trades. | Not recorded. | `CanonicalizationGoldenFixtureTests.cs`, `CanonicalizationFixtureDriftTests.cs`. | Canonical event shape and drift. | Downstream storage or ledger. |
| Synthetic market data providers | `src/Meridian.Infrastructure/Adapters/Synthetic/` (`SyntheticMarketDataClient`, `SyntheticHistoricalDataProvider`, `SyntheticOptionsChainProvider`) | Seeded deterministic walk of bars/ticks; not observed market data. | Seeded in code (source comment: "deterministic seeded walk"); seed value to be read from source, not repeated here. | `tests/Meridian.Tests/Infrastructure/Providers/SyntheticHistoricalProviderContractTests.cs`, `SyntheticProviderTestHarness.cs`, `tests/Meridian.Tests/Providers/ProviderCapabilityDescriptorCatalogTests.cs`. | Provider contract (determinism, shape). | Specific price outcomes tied to a business scenario. |
| Demo workspace seeding | `src/Meridian.Ui.Shared/Services/DemoWorkspaceSeeder.cs`, `DemoTenantBlueprint.cs`, `DemoTenantProvisioner.cs` (CLI `--seed-demo`) | Isolated demo root; seeded strategy run, reconciliation, casework; provenance label `DemoTenantBlueprint.SeededProvenanceLabel`; idempotent. | Not recorded as a fixed as-of in the fixture; seeding is idempotent. | `tests/Meridian.Tests/Demo/DemoWorkspaceSeederTests.cs`, `DemoWorkspaceSmokeTests.cs`, `DemoWorkspaceGuardTests.cs`; `tests/Meridian.Tests/Ui/DemoTenantProvisionerTests.cs`. | Seeded provenance, reconciliation and strategy-run loaded, idempotence, guards. | Exact balances or holdings in the demo book as an acceptance oracle. |
| Banking seed | `src/Meridian.FinancialOperations/Banking/InMemoryBankingService.cs` (`SeedBankTransactionsAsync`) | Synthetic bank transactions by type. | Not recorded. | `tests/Meridian.Tests/FinancialOperations/Banking/BankTransactionSeedTests.cs`. | Seeding behavior of the in-memory service. | End-to-end bank-to-ledger tie-out. |
| W10 population (historical) | `docs/testing/evidence/w10-615abde9/population.json` | Retained evidence population, commit-bound. | Bound to commit `615abde9`. | Evidence packet only; see its README. | Not a test input. | Not applicable. |

## Gaps

These scenarios do not exist yet. Each is a follow-up and needs dotnet to implement and run,
because the assertions belong in C# or F# test code.

1. **Mixed-credit end-to-end test.** No C# test loads `mixed-credit-status-set.json` or its
   `source-payloads/`. Needed: ingest the servicer, custodian, broker, bank, and pricing payloads;
   assert holdings, cost basis, balanced journals, expected reconciliation differences, and close
   balances as of 2026-06-04, with those expected values written into the fixture.
2. **Bank, custodian, broker, and administrator end-to-end scenario.** No scenario ties a bank
   statement, custodian holdings, broker positions, and an administrator NAV statement to one
   ledger with expected close balances. The statement CSVs under `Reconciliation/Fixtures` cover
   account `A1` only and stop at match/break/blocker outcomes.
3. **Expected journals and close balances for corporate actions across accounts.** The golden set
   asserts invariants for a single security; no multi-account scenario asserts resulting holdings,
   basis, and balances together.
4. **Fund economics to journals to NAV close.** Worked examples are asserted per kernel; no
   scenario threads them through posting to a fund close balance.
5. **Uniform seed and as-of recording.** Many rows above are "not recorded". A scenario
   manifest convention (seed, as-of, generator, owner test) is not defined or enforced.
6. **Demo workspace oracle.** The seeded demo book has no fixed expected holdings or balances.
7. **Python-checkable scaffolding.** Only the mixed-credit set and the fund-economics examples
   have python self-checks. Other fixtures (statement CSVs, provider golden-path ledgers) could
   gain balance/consistency checks without dotnet; this was not done here.

Items 1 to 4 and 6 require adding and running C# tests, which cannot be done without dotnet.
