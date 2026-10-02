# Shared service acceptance support — candidate 615abde9

**Status:** bounded automated support passed; live acceptance pending
**Owner:** Accounting and Ledger / Workstation Shell and UX
**Reviewed:** 2026-10-02

Candidate `615abde90001ab33bd6e58e545edc7fce635e254` was initially checked out clean.
The run used Debian 13 and .NET SDK 10.0.100, installed in task-local storage. No
PostgreSQL acceptance population, authenticated retained host or human operator was
configured. The [TRX](acceptance-support.trx), [summary](summary.json) and
[raw build/test log](focused-tests.log) retain actual results and UTC timings.

**570 passed, zero failed, zero skipped, exit code 0.**

| Test class | Executed cases |
| --- | --- |
| ValuationFreshnessAcceptanceTests | 15 |
| FinancialOperationsCommandCenterReadServiceTests | 58 |
| CloseReadinessSubjectSourceTests | 12 |
| DailyValuationPositionServiceTests | 13 |
| WorkstationEndpointsTests | 457 |
| StrategyDesignerWorkstationEndpointsTests (also matched the endpoint filter) | 15 |

Reproduce from a clean candidate checkout after the maintained dependency restore:

```text
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --filter "FullyQualifiedName~ValuationFreshnessAcceptanceTests|FullyQualifiedName~FinancialOperationsCommandCenterReadService|FullyQualifiedName~CloseReadinessSubjectSourceTests|FullyQualifiedName~WorkstationEndpointsTests|FullyQualifiedName~DailyValuationPositionServiceTests" --logger "trx;LogFileName=acceptance-support.trx" --results-directory artifacts/w10-acceptance/615abde9/server /p:EnableWindowsTargeting=true -m:2
```

Source methods/test names and theory inputs are retained in the TRX. The
[criterion review](../criteria-review.md) links the candidate sources and exact
refusal/repair cases. Coverage includes incomplete/mismatched scope, missing/stale
support, snapshot/version changes, denied source ownership, missing carrying values,
age/date/confidence/coverage admission, retained evidence tampering, policy changes
and restoration after supported repair. The tests use their own isolated fixtures
and doubles; they do not provision [population.json](../population.json).

`RetainedMarkTampering_BlocksApprovalAndPosting_OverrideTagsDoNotAuthorizeIt`
rejects altered mark evidence despite inert actor/expired metadata tags. Its success
establishes no governed override approval, lifetime or expiry mechanism. M10 and
MARK-C4 remain pending because that lifecycle is absent at this candidate.

Build/analyzer warnings are retained in the log. This run does not establish live
client rendering, persistent PostgreSQL certification, installed Windows execution,
maker/checker actions or operator acceptance. Those decisions remain pending.
