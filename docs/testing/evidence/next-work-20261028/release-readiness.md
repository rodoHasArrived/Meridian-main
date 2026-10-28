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
