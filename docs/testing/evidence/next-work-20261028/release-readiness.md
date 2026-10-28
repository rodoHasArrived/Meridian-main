# Next-work release evidence — 2026-10-28

**Status:** observed prerequisites; not release certification
**Owner:** core-team
**Observed:** 2026-10-28 08:40 UTC
**Hosted conclusions refreshed:** 2026-10-28 08:54 UTC, same source baseline only
**Source baseline:** `af7f0c6a91ea3c3bea25befbd8d1a98ac0fbe76f`

This receipt records read-only GitHub API and source inspection during implementation of the
next-work plan. No workflow was dispatched, tag created, secret inspected, or governance changed.
The canonical production gate remains `docs/product/implementation-todo-list.md`.

## Hosted observations

| Evidence | Observed result | Boundary |
| --- | --- | --- |
| [Meridian CI 40657772785](https://github.com/rodoHasArrived/Meridian-main/actions/runs/40657772785) | Completed successfully, attempt 1; updated 2026-10-28 08:51:18 UTC, on the source baseline | Does not certify the implementation branch or later commits |
| [Production Certification 40657772734](https://github.com/rodoHasArrived/Meridian-main/actions/runs/40657772734) | Completed successfully, attempt 1; updated 2026-10-28 08:42:39 UTC, on the same source baseline | Workflow conclusion observed; artifacts not independently revalidated; no signed release or operator acceptance claim |
| [Release Readiness 40294900031, attempt 2](https://github.com/rodoHasArrived/Meridian-main/actions/runs/40294900031/attempts/2) | Failed; updated 2026-10-28 05:35:48 UTC; baseline `65e65ed317d086ec68a39e7df6105ffd9d8e8aa3` | Historical commit; this is an actual newer rerun of the October 26 run |
| Signing job 122651867864 | At 05:35:16 UTC: `Release signing prerequisite failed (signing-secrets-unavailable)` | Establishes unavailable prerequisites for that job, not the current contents of the protected environment |
| Signing artifact | `release-signing-readiness-40294900031-2`, artifact ID `12794867040` uploaded | Artifact identity observed in job log; archive not downloaded or independently verified |
| Tag references | Only `eval-v0.1.0-eval.1` and `v0.1.0-rc.1` returned | `v0.1.0-rc.2` did not exist at inspection |
| Repository ruleset 20768244 | Active, applies to `~ALL`, contains deletion and non-fast-forward protection | This ruleset contains no required-status-check rule; legacy branch protection was not inspected |

The signing script and workflow are present. `release-readiness.yml` provides a manual,
non-packaging prerequisite check in `desktop-release-signing`;
`check-release-signing.ps1` verifies certificate private-key presence, validity dates and exact
MSIX publisher identity. It uses ephemeral key storage and emits a non-secret receipt.
Unavailable prerequisites must continue to fail closed.

## Remaining release actions

1. Complete `PRD-002` credential ownership across browser, WPF/setup and default runtime, with
   legacy ownership/verification migration, rollback and rotation/isolation proof. Bounded
   provider-routing changes alone do not close the whole row.
2. The protected-environment owner provisions `MDC_SIGNING_CERT_PFX_BASE64` and
   `MDC_SIGNING_CERT_PASSWORD`, then runs Release Readiness and reviews its non-secret receipt.
   Do this before creating a release tag; the failed `rc.1` tag is retained.
3. Select a frozen candidate only after its required hosted lanes pass. Bind all resulting
   evidence to that exact SHA, including dependency scans and documentation checks.
4. Obtain signed installer and clean installed startup, upgrade, repair, rollback and uninstall
   evidence for the supported Windows x64/ARM64 envelope (`PRD-000`, `PRD-013`, `PRD-014`).
5. Retain the complete recovery proof: verified recoverable point, simulated and declared loss,
   completed reconciliation, named operator acceptance, bound supporting evidence and a separate
   evaluated receipt with `objectiveStatus: proven` within RPO 3600s/RTO 7200s (`PRD-015`).
   A green archive round trip does not supply this proof.
6. Obtain repository-administrator confirmation of required-check activation (`PRD-016`) and
   complete same-commit documentation evidence (`PRD-017`). Do not infer activation from a green
   workflow or from the observed deletion/non-fast-forward rules.

## LOT consumer follow-up

The factor-paydown preparation service had no operator route at the inspected source baseline
`af7f0c6a91`, making governed activation the first bounded implementation task. This is a
historical baseline observation, not a statement about the implementation branch's current code.

A separate consumer-parity candidate remains in
`src/Meridian.Backtesting/CorporateActionAdjustmentService.cs`: its compatibility principal-paydown
path subtracts projected principal from total basis. The governed lot implementation instead
relieves basis pro rata with the factor change and recognizes the premium/discount difference in
realized gain/loss. For the blueprint's 964,250 basis and 0.95 → 0.90 example, these are 914,250
versus 913,500 of remaining basis. Investigate the simulation contract and retained exact-basis
semantics before changing this path; this receipt does not classify that compatibility behavior
as a newly validated defect or authorize accounting writes from the simulation.

The compatibility adjuster receives ticker, current quantity, unit basis, optional exact total
basis and opening time. It does not receive original face, acquisition factor, current factor or
canonical lot identity. Its projection sets `PriorFactor = 1` and `CurrentFactor = 1 -
DistributionRatio` for each action. Existing `PaperTradingPortfolioBasisParityTests` deliberately
retain principal-subtraction behavior, including short compatibility positions. `LotBasisRelief`
can allocate an established exact basis across known consumption slices, but cannot recover the
missing factor authority or determine premium/discount economics. Therefore a scalar allocator
replacement is not a safe governed-parity fix. A future canonical adapter needs explicit retained
face/factor inputs and source identity; preserve the compatibility contract meanwhile.

No LOT, MARK, SEAM or release acceptance status changes follow from this inspection. Independent
Accounting/Ledger and Storage review, live shadow operations and live Windows operator evidence
remain separate gates.

## Amount-provenance reconciliation

The earlier read-only provenance inspection at `af7f0c6a91` found that the compatibility
`FundOperationsWorkspaceReadService.GenerateReportPackAsync` retained consolidated labels without
an authoritative per-amount journal mapping. That finding describes the historical baseline;
it is not the state of the governed reporting path after integration of incoming main `a6ad3fe157`.

The incoming `LedgerReportingAuthoritativeSource` now retains trial-balance amount bindings with
exact contributing journal/line identities in its content-hashed `ReportingLedgerPopulationSnapshot`.
`ReportAmountBindingBuilder` reproduces those bindings from Ledger's retained financial report pack;
`ReportLedgerAmountProvenanceService` resolves `report:{runId}:{amountId}` through the existing
subject-addressed ledger-amount evidence surface. Historical contributors keep their original
posting-period scope. A missing, altered, foreign or unverified contributor blocks the complete
amount and removes its inspectable source routes. The browser reporting detail uses the shared
proof drawer; it does not reconstruct evidence from live balances.

`docs/testing/w10-amount-provenance.md` and `ReportLedgerAmountProvenanceTests` record this bounded
slice and its historical validation. Those results were not rerun by this inspection and do not
certify the integrated candidate. `W10-PROV-001` remains in progress: generated-report WPF
activation, other report families, NAV and portfolio amount surfaces, current-candidate validation
and live operator acceptance remain open. Compatibility typed pointers remain review-required;
labels and an invented single-book scope cannot establish proof.

## MARK and SEAM acceptance matrix

This is a current-source/document inspection matrix, not execution evidence. The maintained
walkthrough is `docs/testing/w10-mark-seam-operator-acceptance.md`; its recorded representative
population and operator decisions remain unprovisioned/pending. Preserve implementation order
LOT → MARK → SEAM → RECON while executing SEAM before MARK in the joint live session.

| Lane | Required exercise | Linux support available | Remaining acceptance proof |
| --- | --- | --- | --- |
| SEAM scope (S1–S3) | Same complete fund/book/account/entity/period in both clients; clear each dimension and substitute foreign membership | Shared scoped service and endpoint regression suites | Authenticated retained population, live browser/WPF refusal and repaired decisions |
| SEAM contributors (S4/S4b–S6) | Missing, unregistered, stale and unavailable contributors remain blocking with type/count/severity/owner/records; repair each | Service and subject-source tests; browser component tests | Controlled live host-profile registration removal, service fault/recovery and retained before/after projections |
| SEAM commands (S7–S10) | Change evidence after review; delayed old response after selection change; authorized close/reload; coverage remains diagnostic | Concurrency, scope, private-capital evidence and browser view-model tests | Actual denied-role/stale-version refusals, maker/checker commands, persisted close evidence and both installed clients |
| MARK admission (M1–M6/M11) | Fresh and boundary marks pass; stale/future/unknown-date/missing/low-confidence support blocks; append correct owned evidence and repeat | `ValuationFreshnessAcceptanceTests`, policy and position-service tests; browser freshness tests | Same-candidate installed policy capture and live refusal/repair/reload chain with no partial journals |
| MARK population preview (M7/M8) | Enumerate all schedules and account/security positions; preview exact denominator; change scope/policy during delayed request | Preview service and browser selection tests | Retained two-account/two-schedule/eight-position population, carrying values, actual preview denominator, exclusions and zero-write proof; actual WPF rendering |
| MARK retained authority (M9/M10) | Recheck evidence at submit/approve/post; any supported override binds position/observation/date/policy and expires | `ValuationMarkEvidenceGuard` and tampered-evidence/arbitrary-override-tag refusal tests | Live retained lineage and explicit acceptance-scope decision for override criterion; see qualification below |

Current source safely admits **no override bypass**: `ValuationMarkEvidenceGuard` requires a
valid digest and reassesses dated marks; arbitrary override tags do not authorize stale data.
There is no retained governed override/expiry contract in this inspected slice. The acceptance
walkthrough explicitly leaves M10 and MARK criterion 4 pending rather than treating this as a
passing override test. This is not a claim that production must add an override: retain the
fail-closed default and obtain an explicit criterion/scope decision before claiming full MARK
acceptance. Do not silently reinterpret the absence of override support as completed lifecycle
proof.

### Reproducible support commands (not run by this lane)

The following select existing supporting tests on Linux; they cannot establish operator
acceptance. The coordinator owns serialized builds and final CI.

```bash
python build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj --filter 'FullyQualifiedName~ValuationFreshnessAcceptanceTests|FullyQualifiedName~FinancialOperationsCommandCenterReadService|FullyQualifiedName~CloseReadinessSubjectSourceTests|FullyQualifiedName~DailyValuationPositionServiceTests|FullyQualifiedName~PrivateCapitalCloseCockpitServiceTests' --queue
npm --prefix src/Meridian.Ui/dashboard run test:vitest -- src/screens/accounting-screen.mark-preview.test.tsx src/screens/portfolio-screen.mark-freshness.test.ts src/screens/accounting-screen.close-cockpit.view-model.test.ts src/screens/operations-continuity-screen.test.tsx
```

`tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj` excludes test sources and creates a stub on
non-Windows. A successful Linux stub build/run is zero WPF runtime proof. Run the following on
Windows with the installed candidate, and retain runtime results separately from compilation:

```powershell
python build/python/cli/buildctl.py test --project tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~OperationsContinuityViewModelTests|FullyQualifiedName~AccountingCloseViewModelTests|FullyQualifiedName~AccountingCloseHttpRecoveryTests|FullyQualifiedName~MarkFreshnessPresentationTests" --queue
```

Live acceptance additionally needs returned retained IDs (not logical fixture GUIDs), full
candidate/host/client identities, Windows/browser versions, operator roles, policy configuration,
evidence hashes, screenshots, actual command/approval IDs and an attributed accept/reject/defer
decision per criterion. At the documented default (maximum age 3 days, minimum Medium,
complete coverage and observed date required), the planned unrepaired population expects eight
assessed positions, five blocked and two affected valuation batches; after owned repairs it
expects eight assessed and zero blocked. These remain expected shapes, not observed results.

## Local implementation validation

The implementation adds factor-paydown preparation and governed draft routes plus browser/WPF
review controls and the bounded legacy credential-ownership guard. These results describe local
working-tree proof, not hosted certification or operator acceptance:

- The selected .NET run passed **312 tests**, with no failures or skips, covering factor paydowns,
  PostgreSQL atomic posting/replay, provider setup ownership/routing, endpoint authorization and
  read declarations, valuation freshness and close-readiness contributors. PostgreSQL ran in an
  owned disposable PostgreSQL 16 container with isolated test schemas.
- Full WPF application/XAML and complete WPF test-project sources cross-compiled with zero
  errors and 49 warnings. The build explicitly selected `IsWindows=true`,
  `EnableWindowsTargeting=true` and `--full-wpf-build`; no Windows runtime tests ran on Linux.
- Browser API, panel, Ledger Explorer integration and posted-ledger hook checks passed **89 tests**.
  Strict type checking and production bundling passed. Full lint reported zero errors and 105
  existing warnings; new files passed lint without warnings.
- Playwright Chromium exercised preparation, keyboard review confirmation, governed draft and
  journal-approval navigation at 1920×1080, 1440×900 and 1366×768. API responses were mocked;
  no page errors or horizontal overflow were observed and axe reported no violations.
- The complete `bash scripts/ci.sh` gate remains separately recorded by its run result. The first
  attempt stopped at formatting in the new endpoint; scoped formatting corrected those errors.

The selected .NET filter was `FactorPaydown`, `ProviderSetupOwnershipTests`,
`ProviderRoutingEndpointsTests`, `ValuationFreshnessAcceptanceTests`,
`CloseReadinessSubjectSourceTests`, `DailyValuationPositionServiceTests`,
`PrivateCapitalCloseCockpitServiceTests`, `EndpointAuthorization` and `EndpointReadDeclaration`
joined with `FullyQualifiedName~` alternatives. No category exclusion was used, so the paydown
PostgreSQL tests were executed rather than silently skipped.

## Corrected durable review intake

Review found that retaining an asset-spine candidate did not create a manual journal queue draft.
The corrected adapter now persists an immutable source-linked mirror using the candidate journal
ID. A real file-store/workbench test proves discovery after restart, one-ID retry, server-retained
validation and source-preserving submission. Generic edits, approval, posting and corrections are
explicitly refused; no approval evidence is fabricated. The typed retained-approval bridge is
remaining implementation work, not an acceptance checkbox.

The corrected working-tree candidate passed **370 focused .NET tests** and **115 browser tests**.
The updated full Windows WPF application/XAML and test-project sources cross-compiled with zero
errors and 18 warnings. Earlier hosted Windows proof on head `427ba30701` passed all 15 original
factor-paydown tests (full suite 2,456 passed, zero failed, one registered skip); that historical
run does not certify these later corrective edits. The factor panel alone uses shared server
review reads and submission; existing desktop-local journal records keep their original lane.

Dependencies were reinstalled from the dashboard lockfile before bundling. Two consecutive locked
builds had identical paths and SHA-256 digests for all 128 outputs. The factor types are exported
through the shared barrel. Draft deep links select only the exact returned fund/book/period/ID,
preserve unrelated unsaved recovery and reject late or foreign responses.

The broad clean local .NET run before this correction passed 24,173 tests with five registered
skips and failed four unrelated MCP cleanup tests because the container's PID 1 did not reap
orphan processes. All seven MCP runner tests then passed unchanged under `docker-init -s`.
The complete local quality gate on `5aa74aaab0` then passed: 24,178 .NET tests with zero
failures and five registered skips, the complete browser lane, and 1,958 repository script tests
with zero failures and 16 registered skips. The command cleared the three demo environment
flags and ran `bash scripts/ci.sh` under `docker-init -s`; no test or timeout was changed.
This result predates integration of `a6ad3fe157` and does not certify that combined tree.
Hosted checks and live acceptance remain separate. Andrew Rowden was
identified by the user as signing owner; an accessible Windows live-acceptance target remains
unspecified.

The final mocked Chromium review flow selected the second returned journal draft by exact scope
and ID, validated and submitted version 7, and rendered submitted version 8 with approval still
blocked. Both read-only table scroll regions were keyboard reachable and scrollable. Factor-panel
axe found no violations; the manual review view retained 17 contrast findings also present in the
normal-draft baseline and introduced no additional findings. These are mocked rendering results,
not live Windows operator acceptance. The separate unchanged recovery test that timed out under
hosted load passed locally without widening its 30-second subprocess timeout.

The mirror retains the full generated dimension lineage and typed source candidate. Structured
posting-account identities not exposed by `GeneratedPostingLineDto` are not reconstructed from
labels; this review surface is not a complete posting-account projection.

## Validated review milestone — cef39213

Hosted validation completed on feature head
`cef392130bc6e88d0ef11227bccb9a705cadb78c`, tested as PR merge
`d01a64bdfba47613c7f5d88ff38d284746d98db5` against main
`a6ad3fe1575530dc740167923ffd3bafeb27e7dd`. All hosted workflows on that head completed
successfully, including Meridian CI quality/integration gates, CodeQL, Windows Desktop Build,
Golden Path, documentation generation/validation, PostgreSQL schema control, pipeline budget,
demo smoke and screenshot validation. Expected conditional jobs remained skipped.

The following downloaded artifacts were checked against their published SHA-256 digests.
Counts come from their retained TRX or test-summary JSON, not compilation alone.

| Run / artifact | Verified results | Artifact ID / SHA-256 |
| --- | --- | --- |
| [Meridian CI 40683943291](https://github.com/rodoHasArrived/Meridian-main/actions/runs/40683943291), `meridian-ci-dotnet-6004-1` | 24,965 passed, zero failed, five registered skips across 19 TRX files | `12815457208` / `8aca091fe35f61edbcd6243bc7ee699f4aee4baa427196cfc30595f4ba16a7c0` |
| Same run, `meridian-ci-browser-6004-1` | 4,057 passed, zero failed or skipped | `12815490714` / `559045b671cd4a39238b9ca4b80a9773ea69ae02b65b7317bd973878521875fb` |
| Same run, `meridian-ci-integrations-6004-1` | 1,380 PostgreSQL tests passed, zero failed or skipped: 1,367 Meridian and 13 Direct Lending | `12815586206` / `c765e6a93c3f3044e7aebaaf02b20c496d31f60a09521b7e0780e15f8eff6dab` |
| [Windows Desktop Build 40683943060](https://github.com/rodoHasArrived/Meridian-main/actions/runs/40683943060), `windows-desktop-validation-5521` | Native full WPF suite: 2,510 passed, zero failed, one registered skip. All 18 factor-paydown cases passed: six API-client and 12 view-model cases. The unchanged compact-action-chrome assertion passed. All six Windows validation groups passed. | `12815420258` / `2a8c0d77a653ab2766eac4e3a46e829d6dfc79e28e2ffab42f1e7fe3a9555ba3` |

This milestone validates the immutable factor-paydown **review/submission** implementation only.
Its generic approval, posting and correction paths remain blocked. It does not validate the new
isolated typed-approval adapter, subsequent WPF report-provenance work or later credential changes
in the approval worktree. Those changes require their own candidate-bound evidence.

The full local `bash scripts/ci.sh` run on this same head subsequently passed: 24,965 .NET tests
with zero failures and five registered skips, 4,057 browser tests with zero failures or skips,
and 2,184 repository script tests with zero failures and 16 registered skips. It used the pinned
Python dependencies, cleared demo-only environment flags and ran under `docker-init -s`; no
test, timeout or governance rule was changed. The earlier `5aa74aaab0` pass remains historical.

No Production Certification run, live Windows operator acceptance, signing or supported release
is claimed here.

## Receipt-backed posting and client parity — implementation validation

The subsequent implementation extends the validated review milestone with actual independent
approval and canonical posting. The authenticated decision bytes bind source/candidate identity,
tenant/company, fund/book/period/basis, actor and time. A pending post retains the exact original
request before financial publication. Vault content is reopened and verified, and only the canonical
candidate/atomic lot service can publish the journal. Generic source edits and corrections remain
blocked. Capability responses carry the actual authenticated actor; a session change between review
and command submission refuses the stale intention rather than approving under another actor.

The real PostgreSQL path exposed three issues not established by stub tests. Canonical approval
now attests at `max(actual review, actual retention)` without changing either fact or backdating the
Vault. Factor replay requires the immutable original journal, Drafted authority and atomic receipt
before consulting the storage exact-replay rail, rather than rebuilding economics from mutated
lots. Existing schema-owned timestamp comparison handles PostgreSQL microsecond representation;
the exact original timestamp and decimal representation remain authoritative for fingerprints.
Missing or changed authority still refuses; future retention and new-write staleness checks remain.

The expanded local packet passed **645 tests with zero failures or skips**, including the real
PostgreSQL approval → post → restart/replay with deliberately nonzero submicrosecond time. The
round trip retained one canonical journal/workbench ID and mutation batch, changed factor
`0.95 → 0.90`, and booked cash `50,000`, basis relief `50,750` and realized loss `750`.
Altered candidate, instruction, evidence, actor and tenant retries made no additional writes.
Preparation still requires a qualified retained factor observation and prospective economic model.
The fixture seeds them through the supported governed projection pipeline and uses a recorded-as-of
reference adapter for its controlled population; it does not certify historical ingestion.

The new browser lifecycle checks passed **135 tests**; scoped credential API/editor and existing
Settings/accessibility checks passed **71 tests**. Strict types, lint, route/barrel/parity and file-size
checks passed; lint retained 104 existing warnings. Two canonical production builds produced
identical paths and SHA-256 digests for all **129 files**. Mocked Chromium checks at 1920×1080,
1440×900 and 1366×768 exercised approval, retained receipt inspection, interrupted-post reload,
exact prior-version resume, canonical journal navigation, and owned credential save/verify/removal.
The new workflow/editor checks found no overflow, unexpected page errors or axe violations; the
post-recovery exercise explicitly injected one 503. These are rendering tests with mocked APIs.

Full WPF application/XAML and complete Windows test sources cross-compiled with **zero errors and
18 warnings**, including factor approval/posting, explicit owned credential selection, and generated
report amount proof. Report proof requires complete declared journal/line coverage, preserves exact
amounts and historical source manifest identity, and uses invocation-local response guards under
the existing single-operation policy. This compilation is not native Windows runtime evidence.

Schema-control applied all **152 migrations** with zero migration or policy errors. Physical schema,
migration and policy manifests were unchanged; public contracts/dependencies were regenerated.
The three new public contracts are the approval receipt, per-draft capabilities and pending post
intent. Temporary schema reports were retained outside the checkout before documentation refresh.

This packet describes the working implementation after `cef39213`; it is not covered by that
milestone's hosted results. The final integrated commit still requires its own full local gate and
GitHub Actions results. LOT/MARK/SEAM live acceptance, independent Accounting/Ledger and Storage
review, broader LOT/correction/consumer parity, other report amount families, host credential
binding, ownership creation and explicit legacy migration remain open. Andrew Rowden is the stated
release-signing owner; an accessible Windows acceptance target and protected signing readiness
remain unprovided. No release certification, tag or PR merge is claimed.

## Integration follow-ups after 2fda0662

The expanded candidate's hosted ordinary .NET, browser, documentation, schema and CodeQL checks
passed. Integration identified one permanent composition failure, and native Windows found three
credential-editor input-preservation failures; those results were not classified as a green candidate.

The reporting test's original 15-second worker-readiness timeout masked eager activation of the
new optional asset posting graph: the read-only reporting composition intentionally has no
`IAccountingJournalDraftService`. A stronger pre-start component assertion reproduced that exact
missing dependency. Shared manual-workbench construction and endpoint fallback now defer posting
and spine dependencies until a typed asset capability/action actually needs them. Ordinary reads
activate neither factory; activated configuration errors still surface and fail closed. No readiness
rule, worker option, deadline or unrelated feature registration was changed to make the test pass.
The repaired packet passed **187 tests**, including both real PostgreSQL flows and all 38 bridge
cases. A separate database-selector-free composition packet passed **16 tests**.

Native WPF found deferred notifications for the same owned connection could reset editors after
new input was entered during save or verification. The event handler now compares the actual
selected item's complete identity with the accepted or pending retained scope. Duplicate same-scope
notifications preserve newer input; real different or cleared selections still invalidate requests
and clear secrets. Four new cases cover these events, and the original three failing regressions
remain unchanged. Full cross-compilation and native runtime revalidation belong to the repaired
candidate's recorded results.

The interrupted local full run on `2fda0662` passed eight complete shards (16,208 tests, five
registered skips and zero failures) before being superseded for these repairs. It is partial
historical evidence, not a full quality-gate pass. Current main's reviewed dependency-action updates
were integrated without source conflicts; this task authors no governance changes.

The repaired MAIN source subsequently passed full Release WPF application/XAML and Windows
test-source cross-compilation: zero errors and 275 warnings from the fresh dependency rebuild.
It includes the original input-preservation tests and four added redundant-selection cases.
This remains compilation proof; the repaired commit still needs native Windows execution and
its complete candidate-bound local/hosted gates.

## Accepted-order audit cancellation follow-up

The repaired `a12c99a2` candidate passed all 1,381 hosted PostgreSQL tests and the complete native
Windows suite (2,567 passed, zero failed, one registered skip), including all original credential
input regressions. Its ordinary hosted .NET lane exposed one existing intermittent execution issue:
`ResumePendingRunsAsync_RetainsAccountThroughRealOmsReadinessAndBrokerDispatch(true)` found no
`OrderSubmitted` audit after stopping a run whose gateway had accepted its order. All other hosted
workflows passed. The local ordinary .NET shards passed 25,010 tests with five registered skips;
that incomplete full run was superseded to correct the actual cancellation boundary.

The OMS previously passed the strategy caller token to the audit append after broker acceptance.
Stopping the strategy could cancel the WAL append while OMS still returned the accepted outcome.
Only that authoritative post-acknowledgement audit now ignores caller cancellation, consistent
with the existing fill handoff. Pre-dispatch cancellation, auxiliary session-update behavior and
actual I/O failure handling are unchanged.

A deterministic real-engine/gateway barrier waits for the stop token to cancel, then returns the
accepted acknowledgement and checks the actual reopened audit WAL after awaited stop. With the
original token the new regression failed with the same empty audit; its pre-dispatch companion
passed. Restoring the correction passed both cases and the full **184-test** live-engine/OMS/audit
packet, including both original hosted account-scope variants. No test deadline was widened and
no sleep was added. Final candidate-bound full local and hosted checks remain required.

## Unposted preparation recovery review

The integrated `8bf093eb8c` candidate passed its hosted quality gate: 25,275 ordinary .NET
tests (zero failed, five registered skips), 4,103 browser tests and all 1,381 PostgreSQL
tests. Its tested PR merge was `10c04bd8f3adde0281f65eacabf31632a0f9bb09`.
Both original live-trading account-scope variants and the deterministic accepted-order audit
regression passed. Verified ordinary .NET artifact `12830741730` has SHA-256
`be1080df7c22965ee7b0239af77a422956873515a1e07b4d11deba83a36c6ac9`;
PostgreSQL artifact `12829729459` has SHA-256
`2fd1508a8690dddfdf581b5b276b2ff49d51a5dec4f54bd9217570ab8243bcdb`.
Native Windows also passed: 2,569 WPF tests, zero failed and one registered skip,
including all 34 factor-paydown, 20 retained-report-amount and 23 provider-wizard cases.
Artifact `12830722634` has SHA-256
`1e794da93fa2ce30e43b8b1ca455e06d6f3ed7e82078b630edfa9fd0826c6023`.
All hosted checks for that head finished green; its local full gate was not completed.

That candidate still contained the PR review's P1 recovery defect: a candidate-build refusal
after retaining Expected/Projected state could permanently block a corrected preparation for
the same economic observation. Two independent source reviews confirmed the account-change
and after-retention lot-version cases were not covered by unchanged-input retry tests.
The local full run was intentionally stopped after 3,012 passing application and endpoint
tests so the defect could be repaired. Those results do not certify the recovery follow-up.

The recovery follow-up appends a freshly reviewed factor preparation under the same economic
event and source version. Its optional `PreparationRevision` records the exact predecessor spine
version and canonical fingerprint, preparing actor, time and reason. Recovery requires the same
authenticated preparing actor, unchanged source/factors/evidence/owner identity, current complete
lots and authoritative security/book/open-period checks. The trusted preparation time supplies the
recorded-security cutoff; the original economic observation and lineage remain unchanged.
Drafted, approved or posted authority cannot enter this transition. The same locked append version
arbitrates recovery against an old candidate build; all earlier snapshots remain immutable.

A controlled baseline restored only the original `8bf093eb8c` endpoint while retaining the new
regression fixtures. Both after-retention lot drift and corrected-account retry reached the intended
failure: expected HTTP 200, actual HTTP 409 with “The retained factor event differs from the
reviewed preparation.” An exit trap restored the exact fixed source before validation continued.
The baseline log is `/workspace/scratch/meridian-factor-recovery-original-endpoint.log`.

The restored implementation passed the full **601-test** focused packet with zero failures or
skips, recorded in `/workspace/scratch/meridian-factor-recovery-fixed-final.log`. All three HTTP
regressions passed: lot drift after actual projection retention, corrected-account recovery and a
held old candidate build losing the real append CAS to fresh preparation/drafting. The tests retain
byte-exact original snapshots, reject old previews, verify stable recovered retries and retain one
workbench draft without a ledger write during preparation. Recorded-security v1/v2, contract,
strict store, candidate, manual-journal and approval guards also passed. Authorized preview tests
now assert the required read-only head lookup and explicitly forbid all spine mutations; scope
denial checks are unchanged.

Real PostgreSQL proof recovered a failed candidate into the same event's draft, independently
approved and posted it, then restarted and replayed one canonical journal and mutation batch.
The disposable test database was removed after this packet. Schema control again applied all
152 migrations with zero migration/policy errors: 10 schemas, 233 relations and 3,080 public
contract objects. Only contract/dependency inventories changed; physical schema, migration and
policy manifests remain unchanged. Full candidate-bound local and hosted results are recorded
in the PR separately from this focused proof.
