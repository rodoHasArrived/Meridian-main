# Mark and close-readiness verification — 2026-10-28

**Status:** automated supporting evidence; live population, Windows workstation and operator acceptance remain open.

**Source baseline:** `a5b2589f0395d054bec0b59672a990419ef237ed`.
Browser application sources exercised below match this baseline. The new population regression
is a working-tree change and requires the implementation branch's own hosted checks.
No roadmap acceptance, close authorization or operator verdict is recorded here.

Implementation order remains **LOT → MARK → SEAM → RECON**. The separately recorded joint
live session remains **SEAM → MARK**; this evidence does not change either ordering.

## Population refusal and repair

`ValuationFreshnessAcceptanceTests.DeclaredTwoBatchPopulation_IdentifiesEveryBlockedPosition_AndMarkRepairClearsBothBatches`
adds a full declared-population regression to the existing individual defect tests. It enumerates
eight account/security positions across two batches, sharing one security between accounts:

| Batch | Positions | Expected blocked positions |
| --- | --- | --- |
| account-a | SHARED age 0 High, BOUNDARY age 3 Medium, STALE age 4 High, UNDATED High | STALE, UNDATED |
| account-b | FUTURE age -1 High, LOW age 0 Low, MISSING quote, SHARED age 0 High | FUTURE, LOW, MISSING |

The test checks eight distinct account/security identities, seven securities, five blocked
positions and two affected batches; absent dates remain unknown. Preparing either unrepaired
batch yields no approval or projection. Replacing all test-source marks with dated
High-confidence observations yields eight assessed, zero blocked, zero affected batches and
the same exact population identities. This is an in-memory service regression: it does not
provision schedules, prove source ownership, persist evidence or establish live population acceptance.

The existing close-readiness suite already exercises complete scope and current contributors,
missing scope, unregistered/failing/stale contributors, all five mismatched scope dimensions,
concurrent evidence changes and missing/stale/mismatched close-plan evidence followed by repair.
Its fixtures consume the shared authority; they do not constitute two installed workstations.
The coordinating implementation run owns the new .NET test result; until its receipt is appended,
the new test is **not claimed passed**.

## Browser supporting evidence

Browser plugin unavailable; the installed Playwright 1.64.1 and system Chromium
151.0.7922.173 were used on Debian 13. Vite served the current application at
`http://127.0.0.1:4173/workstation`, with explicitly simulated API responses.
Viewports were 1440 × 900 and 1366 × 768; no phone/tablet surface was exercised.

- Four focused component/view-model files passed **80 tests**: mark preview, portfolio
  freshness, accounting close cockpit and Operations Continuity. Exact command/result is
  retained in [component-tests.log](mark-close-browser/component-tests.log).
- The maintained reviewed-source close capture passed **24 asserted observations**,
  including changed evidence refusal, same-workflow-version repair, publication-evidence
  mismatch, delayed old responses, keyboard basics and both desktop sizes. This is a
  rendered response simulation, including simulated close responses; no real close was posted.
- A copy of the older combined capture, bound to the current source SHA, recorded
  **37 observations**, with all existing harness decisions passing. This includes explicit
  missing/stale shared contributors, omitted scope dimensions, foreign scope, mark
  stale/future/missing-date/coverage/confidence reasons, failure and repair. Some cases
  are capture-only observations rather than complete assertions; the retained case list
  preserves that distinction. The override wording case simulates text only and proves
  no override lifecycle or expiry behavior.
- Page identity and meaningful content were present, with no framework overlay or page
  errors. Console errors were the expected injected 409/503 refusals. Two selected captures
  were visually inspected: missing dates display `Observed Unknown · age Unknown`; the
  missing contributor displays type, count, severity, owner, evidence ID and repair action.

Retained compact packets include source/dependency versions, case records, selected request/
response bodies and hashes of the complete raw packets:
[close.json](mark-close-browser/close.json),
[mark-and-close.json](mark-close-browser/mark-and-close.json).
Screenshots: [missing date](mark-close-browser/M4-missing-date.png) and
[missing close contributor](mark-close-browser/S4-accounting-details.png).
Complete raw packets remain under `/workspace/scratch/mark-close-20261028/`.

The browser scope is explicitly a maintained **fixture**:
`fundProfileId=fund-alpha`, `ledgerBookId=book-alpha`,
`fundAccountId=53bf0251-17f6-4fb7-8dbe-6fb4966e2749`,
`entityId=entity-alpha`, `periodId=2026-05`. The old mark capture uses a single
September 4 assessment. It is not a same-period integrated valuation population or a
population-wide browser proof. Neither those logical IDs nor screenshot text are retained
accounting identities. The new server population regression is a separate July fixture.

To reproduce the focused browser run from the dashboard directory:

```sh
node ./node_modules/vitest/vitest.mjs run \
  src/screens/accounting-screen.mark-preview.test.tsx \
  src/screens/portfolio-screen.mark-freshness.test.ts \
  src/screens/accounting-screen.close-cockpit.view-model.test.ts \
  src/screens/operations-continuity-screen.view-model.test.ts --maxWorkers=1
```

For rendered checks, start Vite on port 4173 in `fixture-only` mode. Run the maintained
`docs/testing/evidence/w10-seam-reviewed-20261007/browser/capture.mjs` with
`W10_EXPECTED_SOURCE_HEAD` set to the baseline and an external `W10_QA_OUTPUT` directory.
The combined capture is retained as
[capture-mark-and-close.mjs](mark-close-browser/capture-mark-and-close.mjs);
copy it to an external output directory before running it with `W10_REPO_ROOT` pointing
at the candidate checkout. Both scripts refuse a different candidate until deliberately
rebound and rerun; old artifacts cannot certify a later commit.

## Remaining live acceptance

The environment is Linux with no installed WPF runtime, Windows session, designated
human operator, authenticated retained accounting host, or returned acceptance-population
identities. Live browser/WPF acceptance was therefore not executable. Existing canonical
[operator acceptance](../../w10-mark-seam-operator-acceptance.md) records the provisioning
requirements and all pending decisions; its historical records are unchanged.

Current desktop source does preserve structured shared close blockers in
`FundLedgerViewModel.CloseReadiness.cs` (type/count/severity/owner/source IDs), improving
the older D1 source finding. However, no WPF/Ui.Services schedule-preview consumer of
`ValuationFreshnessPreviewDto` or `daily-mark-to-market-preview` was found: the shared
endpoint and runner exist, while desktop schedule/population preview integration remains
open (the older D2 finding). Inspector freshness presentation alone does not prove it.

Completion still requires the same candidate and declared entity/book/account/period in
both clients, an enumerated retained population across every included schedule, default
blocking preview and refused governed valuations with no mutation, owned repairs and
reassessment, contributor faults/repairs reflected in the shared close result, Windows
rendering and explicit operator decisions. No default policy activation, acceptance status,
standing override or compatibility retirement is inferred from these automated checks.
