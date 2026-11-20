# Bond settlement and LOT handoff golden case

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-11-20
**Evidence scope:** synthetic-verified for the selected local runtime checks; live-untested

This packet specifies and records bounded synthetic bond settlement and governed acquisition intake for
`W10-LOT-002` and `W10-BOND-001`. The implementation candidate is
`67b81bbfe838434bd5b3b3f39893787d971bca5c` on `codex/bond-lot-evidence`, based on
`dfdbb0ce7751d3bf2e01ae670389dbf2027dd229`; see [PR #3685](https://github.com/rodoHasArrived/Meridian-main/pull/3685).
Execution, ordinary endpoint/LOT-consumer and disposable PostgreSQL checks passed within the
scopes below: 464 selected tests, zero failures or skips. The ordinary rerun used test-only follow-up
`3759142b20b1cc128802abbb411b0aaa21c70fb6`, which adds required reviewer notes to the intake fixture.
Production code is unchanged from the implementation candidate.
It supplies no roadmap acceptance, live-provider claim, or release certification.

## Implementation boundary

`PaperTrading:Gateway:RequireBondAccruedInterest=true` opts the hosted paper gateway into
required settlement economics. An immediate or resting face-value fill is rejected when
analytics are missing, blocked, fail, or return inconsistent security, dates, clean/dirty prices
or accrued interest. Zero accrued interest is valid. Default configuration keeps the existing
clean-only fallback, and persisted fills retain their original replay economics. See the
[configuration contract](../../../reference/appsettings-schema.md#required-paper-bond-settlement).

Separately, the existing asset-accounting candidate endpoint now places a server-verified Long
bond acquisition into the immutable journal review queue. This slice requires face-value
per-100 terms, an explicit amortization method, matching acquisition/functional currency and
reconciled settlement. The spine verifies retained analytics and book/position authority;
retries reuse the source draft. Submit, independent approval and typed atomic posting remain
separate governed actions. Manual saves and generic posting cannot replace that authority.

There is **no automatic paper-fill-to-canonical-lot conversion**. Canonical paper-bond posting
still refuses the unsupported path. The paper gateway and acquisition intake are distinct
capabilities sharing the arithmetic below; this packet does not claim a connected end-to-end flow.

## Independent expected amounts

Use the existing synthetic case in
[`PaperBondAccruedInterestTests`](../../../../tests/Meridian.Tests/Execution/PaperBondAccruedInterestTests.cs):
USD 200,000 opening cash; a long purchase of 100,000 face at 101.25 clean per 100 face,
1.50 purchased accrued interest per 100 face, and no fees. These amounts are fixed inputs;
they do not establish that 1.50 is the correct accrued interest for undeclared bond terms.

| Item | Independent calculation | Expected USD |
| --- | --- | ---: |
| Clean consideration | 100,000 × 101.25 / 100 | 101,250.00 |
| Purchased accrued interest | 100,000 × 1.50 / 100 | 1,500.00 |
| Settlement cash outflow | 101,250 + 1,500 | 102,750.00 |
| Cash after settlement | 200,000 − 102,750 | 97,250.00 |
| Clean lot basis | Clean consideration only | 101,250.00 |
| Interest receivable | Purchased interest, outside lot basis | 1,500.00 |
| Assets after purchase | 97,250 + 101,250 + 1,500 | 200,000.00 |
| Income from purchasing accrued interest | No income is earned by the purchase | 0.00 |

The combined accounting effect debits securities 101,250 and accrued interest receivable
1,500, and credits cash 102,750. The existing execution consumer may retain these as separate
balanced clean-trade and `trade-accrued-interest` journals; their aggregate must reproduce the
same effect without capitalizing purchased interest or counting it as income. A canonical lot
requires its retained security, book-position, quantity-basis and acquisition-currency facts.
Absence of those facts cannot be repaired with a ticker-derived identity or synthetic defaults.

The neighboring sale case sells the same face and clean price with 2.00 accrued per 100:
cash received is 103,250, the purchased 1,500 receivable clears, and earned coupon income is
500. The two clean legs net to zero, and combined cash rises 500. This is sale recognition;
it does not prove coupon cash receipt or period-end premium amortization.

## Acceptance matrix

| Check | Required result | Current candidate evidence |
| --- | --- | --- |
| Dirty settlement and clean basis | Cash 97,250; clean basis 101,250; receivable 1,500; purchase income zero | Passed in the 146-test execution selection |
| Required paper settlement | With the opt-in enabled, immediate and resting fills reject missing/inconsistent economics without fill quantity, price or fees; zero accrual remains valid | Passed in the 146-test execution selection |
| Default and replay compatibility | Legacy fallback and persisted-fill economics remain unchanged | Passed in the 146-test execution selection |
| Governed acquisition intake | A server-verified Long, same-currency, face-value candidate enters journal review with balanced 102,750 debits/credits and clean 101,250 acquisition basis | Passed in the 310-test ordinary selection |
| Intake retry and review authority | Exact retry retains one immutable source draft; self-approval, forged manual saves and generic posting refuse | PostgreSQL review/post/reopen replay and ordinary endpoint cases passed |
| Missing or inconsistent authority | Missing review services refuse before drafting; unsupported identity, scope or accounting facts supply no guessed amount or identity | Execution refusal and ordinary endpoint cases passed |
| Canonical lot persistence | Required identity, Face basis, original/open quantities, acquisition currency/FX and clean basis survive a disposable PostgreSQL round trip | Passed in the 8-test PostgreSQL selection |
| Existing LOT boundaries | Selected consumer, acquisition and amortization regressions retain their supported behavior | Selected ordinary consumer and PostgreSQL acquisition cases passed |

Keep an unresolved handoff visible and retryable. A pending accounting consequence must never
be represented as a posted lot. Retain original execution evidence and acquisition authority
on retry rather than recalculating purchased interest from current terms or a current mark.

## Candidate verification receipt

Commands run from the repository root in Bash with .NET SDK 10.0.404, selected by
`PATH=/tmp/meridian-dotnet:$PATH DOTNET_ROOT=/tmp/meridian-dotnet`. The completed execution
selection restored and built candidate `67b81bbfe8`. The PostgreSQL selection used `--no-build`
and the same `bond-lot-evidence` isolation key to reuse those verified candidate assemblies.
It was not a fresh build. Neither run inherits counts from an earlier packet.

```bash
python3 build/python/cli/buildctl.py test --project tests/Meridian.Execution.Tests/Meridian.Execution.Tests.csproj \
  --filter 'FullyQualifiedName~PaperBondAccruedInterestTests|FullyQualifiedName~EventDrivenDecouplingTests|FullyQualifiedName~PaperTradingGatewayConcurrencyTests' \
  --queue --queue-timeout-seconds 1800 --verbosity quiet --logger trx \
  --isolation-key bond-lot-evidence --results-directory artifacts/test-results/bond-lot-evidence

python3 build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj \
  --filter 'Category!=Integration&(FullyQualifiedName~BondAcquisitionIntake|FullyQualifiedName~CanonicalOpenLotConsumerTests|FullyQualifiedName~BondSettlementConsiderationTests|FullyQualifiedName~AssetAccountingEventSpineServiceTests)' \
  --queue --queue-timeout-seconds 1800 --property BuildProjectReferences=false --verbosity quiet --logger trx \
  --isolation-key bond-lot-evidence --results-directory artifacts/test-results/bond-lot-evidence

python3 build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj \
  --filter 'FullyQualifiedName~AssetAcquisitionLotPostgresRoundTripTests|FullyQualifiedName~OpenLotPostgresTests' \
  --no-build --queue --queue-timeout-seconds 1800 --verbosity quiet --logger trx \
  --isolation-key bond-lot-evidence --results-directory artifacts/test-results/bond-lot-postgres
```

The middle command is the completed ordinary rerun selection on `3759142b20`. It rebuilt the
test project with `BuildProjectReferences=false`, reusing unchanged production outputs built
and verified at `67b81bbfe8`; it does not rebuild every project at the test-only follow-up.

Execution tests remain under `tests/Meridian.Tests/Execution/` in source but compile in the
split `Meridian.Execution.Tests` project. The new required-settlement cases are in
[`PaperBondAccruedInterestTests.RequiredSettlement.cs`](../../../../tests/Meridian.Tests/Execution/PaperBondAccruedInterestTests.RequiredSettlement.cs).
The intake cases are in
[`WorkstationEndpointsTests.BondAcquisitionIntake.cs`](../../../../tests/Meridian.Tests/Ui/WorkstationEndpointsTests.BondAcquisitionIntake.cs)
and compile in `Meridian.Tests`.

The PostgreSQL golden case is
[`GoldenBondSettlement_ReviewApprovePostAndReopen_PreservesCleanLotAndDirtyJournalExactlyOnce`](../../../../tests/Meridian.Tests/AssetOperations/AssetAcquisitionLotPostgresRoundTripTests.BondSettlement.cs)
in `AssetAcquisitionLotPostgresRoundTripTests`. It supplies fixed analytical prices through a
stub `IBondAnalyticsService`; real settlement-receipt verification, source spine, posting rules,
journal review, independent controller approval and typed atomic journal/lot posting consume
them. The test uses PostgreSQL source, journal, lot, draft and recovery stores plus a file-backed
Evidence Vault, then reopens services and retries the same posting. Its expected result is one
clean-cost lot and one dirty-consideration journal. It does not validate coupon-schedule analytics.
The narrower filter `FullyQualifiedName~GoldenBondSettlement` selects that case alone.

The PostgreSQL selection must use only a disposable synthetic test database. Use the
[shared test-fixture configuration](../../README.md#shared-postgresql-fixtures); keep production
connection variables unset. Record schema cleanup explicitly and count skipped tests rather
than treating an unavailable database as a successful round trip.

| Verification | Result | Receipt |
| --- | --- | --- |
| Execution .NET checks | 146 passed, 0 failed, 0 skipped; exit 0 | Candidate `67b81bbfe8`; local log `/tmp/meridian-bond-execution-verified.log`; TRX run `test-meridian-execution-tests-20261120191450-dbf14a8e1de34071927d8a7907adecb6` under `artifacts/test-results/bond-lot-evidence/` |
| Ordinary endpoint and LOT-consumer checks | 310 passed, 0 failed, 0 skipped; exit 0 | Test assembly rebuilt at `3759142b20` using unchanged verified `67b81bbfe8` production references; local log `/tmp/meridian-bond-ledger-final.log`; TRX run `test-meridian-tests-20261120192901-ae240dd35f1f4edcab10d506d37521ca` under `artifacts/test-results/bond-lot-evidence/` |
| Disposable PostgreSQL checks | 8 passed, 0 failed, 0 skipped; exit 0 | Same verified `67b81bbfe8` assemblies, `--no-build`; local log `/tmp/meridian-bond-postgres-verified.log`; TRX run `test-meridian-tests-20261120192207-42a1a5d1267e40d686702db51608f246` under `artifacts/test-results/bond-lot-postgres/` |
| PostgreSQL cleanup | Passed: zero owned/non-system schemas remained; disposable container removed | Shared local PostgreSQL selected through the test alias; `docker stop` removed the `--rm` container; subsequent `docker ps -a --filter name=meridian-bond-lot-evidence` returned no container |
| Local documentation lane | Passed, exit 0 on `67b81bbfe8` | `PATH=/tmp/meridian-dotnet:$PATH DOTNET_ROOT=/tmp/meridian-dotnet bash scripts/ci.sh --lane verify-docs`; .NET SDK 10.0.404; local log `/tmp/meridian-bond-docs-verified.log` |
| Full local `bash scripts/ci.sh` | Not run for this packet | Record any later attempt and limitations |
| Required hosted checks | Pending | Run URLs and checked commit pending |
| Separate AI review of financial changes | Completed: no consequential defects found | Separate-agent financial review; this is not independent human review or owner acceptance |
| Browser / Windows WPF / operator acceptance | Not exercised by this packet | Separate workflow and runtime evidence required |

Keep raw logs and TRX files in CI artifacts rather than adding raw payloads to this directory.
The selected local runtime scope is `synthetic-verified`; provider behavior remains
`live-untested` without a separately authorized live run. Hosted checks remain pending and these
local results do not replace their conclusions.

Initial development runs exposed test-fixture defects: missing entity dimensions, a chart
display-name/path assertion mismatch and an interface-only position assertion. Those test
corrections are included in the implementation candidate. The final intake-fixture correction adds
required reviewer notes in test-only `3759142b20`; its ordinary rerun passed as recorded above.

## Remaining boundaries

This covers required paper settlement and separate governed acquisition intake, not the full
decision-to-proof reference slice. It does not connect paper execution to canonical acquisition.
It does not establish a complete terms-derived bond schedule, coupon receipt, effective-interest
amortization, custodian reconciliation, tied financial statements, or proof links from every
reported amount. Existing unsupported treatments and parent-row acceptance requirements remain
in force. Browser and WPF parity are not established by backend checks.

The user queue remains LOT → MARK → SEAM → RECON. This packet adopts no charter candidate and
changes no roadmap status. Synthetic values stay outside the Verified Coverage numerator under
the [charter measurement contract](../../../product/meridian-design-document.md#15-north-star-metrics).
