# Bond settlement and LOT handoff golden case

**Status:** draft
**Owner:** core-team
**Reviewed:** 2026-11-20

This packet specifies and records bounded synthetic bond settlement and governed acquisition intake for
`W10-LOT-002` and `W10-BOND-001`. Its base is `dfdbb0ce7751d3bf2e01ae670389dbf2027dd229`
on `codex/bond-lot-evidence`, plus the task changes. Runtime checks are pending below; the
independent arithmetic is a specification, not evidence that the application produced it.
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
| Dirty settlement and clean basis | Cash 97,250; clean basis 101,250; receivable 1,500; purchase income zero | Pending execution |
| Required paper settlement | With the opt-in enabled, immediate and resting fills reject missing/inconsistent economics without fill quantity, price or fees; zero accrual remains valid | Pending execution |
| Default and replay compatibility | Legacy fallback and persisted-fill economics remain unchanged | Pending execution |
| Governed acquisition intake | A server-verified Long, same-currency, face-value candidate enters journal review with balanced 102,750 debits/credits and clean 101,250 acquisition basis | Pending execution |
| Intake retry and review authority | Exact retry retains one immutable source draft; self-approval, forged manual saves and generic posting refuse | Pending execution |
| Missing or inconsistent authority | Missing review services refuse before drafting; unsupported identity, scope or accounting facts supply no guessed amount or identity | Pending execution |
| Canonical lot persistence | Required identity, Face basis, original/open quantities, acquisition currency/FX and clean basis survive a disposable PostgreSQL round trip | Pending PostgreSQL execution |
| Existing LOT boundaries | Selected consumer, acquisition and amortization regressions retain their supported behavior | Pending execution; record selected tests separately |

Keep an unresolved handoff visible and retryable. A pending accounting consequence must never
be represented as a posted lot. Retain original execution evidence and acquisition authority
on retry rather than recalculating purchased interest from current terms or a current mark.

## Candidate verification receipt

Run from the repository root in Bash with the SDK selected by `global.json`. The following
commands are the initial selection; replace this pending receipt with the exact commands,
candidate identity and observed results after execution. Do not inherit counts from earlier packets.

```bash
python build/python/cli/buildctl.py test --project tests/Meridian.Execution.Tests/Meridian.Execution.Tests.csproj \
  --filter 'Category!=Integration&(FullyQualifiedName~PaperBondAccruedInterestTests|FullyQualifiedName~EventDrivenDecouplingTests)' \
  --queue --isolation-key bond-settlement-evidence

python build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj \
  --filter 'Category!=Integration&(FullyQualifiedName~BondAcquisitionIntake|FullyQualifiedName~CanonicalOpenLotConsumerTests)' \
  --queue --isolation-key bond-lot-evidence

python build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj \
  --filter 'FullyQualifiedName~AssetAcquisitionLotPostgresRoundTripTests|FullyQualifiedName~OpenLotPostgresTests' \
  --queue --isolation-key bond-lot-postgres
```

Execution tests remain under `tests/Meridian.Tests/Execution/` in source but compile in the
split `Meridian.Execution.Tests` project. The new required-settlement cases are in
[`PaperBondAccruedInterestTests.RequiredSettlement.cs`](../../../../tests/Meridian.Tests/Execution/PaperBondAccruedInterestTests.RequiredSettlement.cs).
The intake cases are in
[`WorkstationEndpointsTests.BondAcquisitionIntake.cs`](../../../../tests/Meridian.Tests/Ui/WorkstationEndpointsTests.BondAcquisitionIntake.cs)
and compile in `Meridian.Tests`.

The PostgreSQL selection must use only a disposable synthetic test database. Use the
[shared test-fixture configuration](../../README.md#shared-postgresql-fixtures); keep production
connection variables unset. Record schema cleanup explicitly and count skipped tests rather
than treating an unavailable database as a successful round trip.

| Verification | Result | Receipt |
| --- | --- | --- |
| Focused ordinary .NET checks | Pending | Exact filter, pass/fail/skip counts and TRX location pending |
| Disposable PostgreSQL checks | Pending | Database availability, pass/fail/skip counts and schema cleanup pending |
| Full local `bash scripts/ci.sh` | Not run for this packet | Record any later attempt and limitations |
| Required hosted checks | Pending | Run URLs and checked commit pending |
| Separate AI review of financial changes | Pending | Material findings and dispositions pending |
| Browser / Windows WPF / operator acceptance | Not exercised by this packet | Separate workflow and runtime evidence required |

Keep raw logs and TRX files in CI artifacts rather than adding raw payloads to this directory.
Only a completed check may be labeled `synthetic-verified`; provider behavior remains
`live-untested` without a separately authorized live run.

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
