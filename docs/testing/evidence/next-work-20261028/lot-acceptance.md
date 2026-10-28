# LOT integration and acceptance refresh — 2026-10-28

**Status:** focused .NET and browser regressions passed; live operator acceptance remains open  
**Source:** `a5b2589f0395d054bec0b59672a990419ef237ed` plus the task working tree  
**Scope:** `W10-LOT-002`; retain implementation order LOT → MARK → SEAM → RECON

The delivered preparation, independent approval and canonical atomic posting path was preserved.
This refresh tests the existing browser integration and records remaining consumer/retirement
boundaries. It does not accept the roadmap item or certify a release.

## Fresh browser evidence

The flow under test was `/workstation/accounting/ledger` → prepare factor paydown → review exact
amounts and per-lot basis → confirm by keyboard → create draft → inspect the scoped journal-review
link. The Browser plugin was unavailable; regular Playwright used system Chromium on Linux at
`http://127.0.0.1:5175`, with Vite explicitly in `fixture-only` mode.

| Check | Observed result |
| --- | --- |
| Component/API/integration suite | 91 passed, zero failed or skipped across five files |
| Page identity | `Accounting Workstation - Meridian`; intended Ledger Explorer route |
| Rendered precision | `0.9876543210987654321098765432` retained exactly in prepare and draft requests |
| Review gate | Draft disabled before description and reviewed confirmation; keyboard Space confirmed review |
| Draft result | Per-lot principal and remaining transaction/functional basis visible; scope retained in journal-review link |
| 1920×1080, 1440×900, 1366×768 | Flow completed at each size; no document horizontal overflow |
| Runtime | Zero page errors, zero console warnings/errors, zero Vite error overlays |
| Accessibility | Axe reported zero violations within the Factor paydown region at each size |

The browser API responses were mocked, including book, period, preparation and draft results.
The example evidence identity and draft are explicitly mock data. This proves rendered interaction
and request handling; it does not prove live evidence authority, independent approval, posting,
Windows behavior or operator acceptance. No acceptance actor or decision was invented.

Without the explicit mocks, the same fixture-only route displayed the missing `/api/ledger/books`
fixture and withheld the preparation form until an authoritative book and period could be selected.
There was no connected production population or live operator desktop in this environment.

Reproduction:

```bash
cd src/Meridian.Ui/dashboard
MERIDIAN_BROWSER_RESULTS_DIR=/tmp/meridian-lot-browser-tests-current npm test -- \
  src/lib/api/factor-paydown.api.test.ts \
  src/screens/accounting-screen.factor-paydown-panel.test.tsx \
  src/screens/accounting-screen.posted-ledger.view-model.test.ts \
  src/screens/accounting-screen.posted-ledger-panel.test.tsx \
  src/screens/finance-standard-pages-screen.test.tsx
```

The stable test runner recorded test-identity digest
`6acb090e7dc5743a7b797e89f12263de8bf420c745f8ba5f4ec29368210e0433` and exit code 0.
The [compact regression receipt](lot-browser-regression.json) retains source hashes, test counts,
rendered checks and the exact mock requests.
Existing Vite native-loader notices and three React `act(...)` warnings in the posted-ledger
scope-reset test were emitted by the component run. The rendered Chromium run emitted none.
Temporary detailed results are `/tmp/meridian-lot-browser-tests-current/summary.json` and
`/tmp/meridian-lot-browser-review.json`; screenshots are
`/tmp/meridian-lot-review-{1920,1440,1366}.png`. These workspace files are not hosted artifacts.

## Fresh .NET evidence

The coordinated `Meridian.Tests` run passed **906/906 tests**, with zero failures or skips.
Of these, the following **230** results matched the LOT filters; counts were read from the TRX,
rather than inferred from source declarations:

| Included `FullyQualifiedName~` filter | Passed |
| --- | ---: |
| `CanonicalOpenLotConsumerTests` | 95 |
| `OpenLotAmortizationContinuationTests` | 18 |
| `OpenLotAmortizationPrecisionTests` | 2 |
| `OpenLotSuccessorTests` | 34 |
| `PaperTradingPortfolioBasisParityTests` | 24 |
| `LedgerReportingAuthoritativeSource` | 51 |
| `SuccessorAmortization_` | 6 |

Filters were joined with `|`; no category or Docker exclusion was used. The six real PostgreSQL
successor-amortization cases passed: premium and discount continuation/replay after period lock,
forged receipt/stale target refusal, missing predecessor receipt refusal, late-failure rollback
and exact retry, and reversal followed by schedule-preserving rebook. Reporting coverage includes
retained successor origins, signed evidence and altered-authority refusals. The consumer tests
also exercise Backtesting's canonical-binding adapter inside `Meridian.Tests`; the separate
`Meridian.Backtesting.Tests/CanonicalLotReliefParityTests` project was not independently run in
this focused packet.

The run used `buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj --queue
--isolation-key next-work-20261028` and the combined credential/MARK/SEAM/LOT filter. The full
command is retained in `/tmp/meridian-next-work-tests.log`; its validation receipt is
`.ai/validation-runs/test-meridian-tests-20261028223901-9139b6076af4480ebe808eeb9653561f.json`
and its TRX is
`.ai/test-results/test-meridian-tests-20261028223901-9139b6076af4480ebe808eeb9653561f/next-work.trx`.
These are local results for the task working tree, not a hosted integration-gate pass.

## Consumer parity and compatibility retirement

| Consumer/boundary | Existing proof surface | Remaining requirement |
| --- | --- | --- |
| Execution | `PaperTradingPortfolioBasisParityTests`; shared exact `LotBasisRelief` allocation | Complete canonical identity/FX and policy cutover; live shadow comparison |
| Backtesting | `CanonicalLotReliefParityTests`; `CanonicalOpenLotConsumerTests.Backtesting` | Functional-FX HIFO ordering, SpecificId fallback/tie policy and factor economics remain distinct |
| Reporting | `LedgerReportingAuthoritativeSourceTests.SuccessorReportPack` and successor integrity tests | Same-population reconciliation to retained posted mutations and operator review |
| Successor amortization | `OpenLotAmortizationContinuationTests`; `AtomicTaxLotJournalStoreTests.SuccessorAmortization` | Broader successors, changed schedules and correction policy need explicit governed cases |
| Legacy model retirement | Standalone production writes already refused; reviewed backfill retained | Blueprint requires zero compatibility telemetry for one release before removing the model; no such observation was supplied |

Backtesting's compatibility paydown path still subtracts projected monetary principal from total
basis. It does not receive canonical original face, retained prior/current factor or lot identity.
It therefore cannot establish pro-rata canonical paydown basis or premium/discount realization.
The blueprint example has 964,250 basis and a factor change from 0.95 to 0.90: principal subtraction
leaves 914,250 while governed pro-rata relief leaves 913,500. Altering the scalar formula cannot
supply the missing authority. A canonical adapter must carry those retained facts explicitly.

`Meridian.Execution.Sdk.TaxLot` still exists, uses decimal quantities and supports existing
consumers. The blueprint's historical reference to a long quantity is not current source truth.
The type still has symbol-based identity; six files in Execution/Backtesting production sources
refer to `TaxLot`. Deleting it now would break active compatibility consumers and would not meet
the release-long telemetry condition.

## Live acceptance still required

Use an actual declared entity/fund, ledger book, account/position, accounting basis and open period
on both workstations. Retain exact source/action identities, prior/current factors, affected lot
versions, preview fingerprint, draft and approval IDs, immutable journal/mutation receipts and the
resulting Reporting evidence. Independent Accounting/Ledger and Storage review must attribute an
accept/reject/defer decision. Exercise stale authority, restart/retry and supported successor/
amortization cases using the delivered services. Record remaining compatibility discrepancies
without changing simulation data into accounting authority.

This session has no Windows interactive runtime, connected operator population or attributed
operator decision. WPF compilation and mocked browser checks cannot close those requirements.
