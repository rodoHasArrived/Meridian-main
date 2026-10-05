# W10 desktop supporting evidence — candidate 615abde9

**Status:** static support captured; Windows execution and operator decisions pending
**Owner:** Workstation Shell and UX
**Reviewed:** 2026-10-02

Candidate: `615abde90001ab33bd6e58e545edc7fce635e254` (PR #3048).
Inspection completed: `2026-10-02T22:10:31Z`. Execution environment: Linux,
Python 3.12.14. This record establishes static source/supporting-check evidence only.
No Windows workstation, installed WPF acceptance session, live desktop screenshots,
provisioned accounting population, or human operator decisions were available.
Every desktop acceptance gate remains **pending**.

Implementation order remains **LOT → MARK → SEAM → RECON**. Execute the joint
operator walkthrough in the separate order **SEAM before MARK**.

## Executed checks

From the repository root, both commands returned exit code 0:

```bash
python3 scripts/wpf_finance_ux_checks.py --root . --paths src/Meridian.Wpf --output artifacts/w10-acceptance/615abde9/desktop/wpf-finance-ux-static.md
python3 artifacts/w10-acceptance/615abde9/desktop/inspect_desktop_support.py > artifacts/w10-acceptance/615abde9/desktop/source-inventory.json
```

The maintained structural checker reported **8 passed, 0 failed** in
[wpf-finance-ux-static.md](wpf-finance-ux-static.md). Its checks cover shell styles,
registration, navigation, context, command bars and signifiers; they do not test W10
runtime decisions or rendering. The inventory records SHA-256 hashes, line numbers
and platform conditions for 15 inspected files in
[source-inventory.json](source-inventory.json). Re-run the inventory at the candidate
SHA; it refuses a different `HEAD` or modified source/tests. The retained copy adds
`--root <candidate-checkout>` so this packet's script can inspect an isolated candidate
checkout after documentation commits are appended. No .NET desktop test was executed.

`tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj:27` selects a `net10.0` stub on
non-Windows and disables all default compile items at line 35. Its WPF project
references exist only under the Windows condition at line 70. The application also
excludes all source files on non-Windows (`src/Meridian.Wpf/Meridian.Wpf.csproj:23`).
A successful Linux `dotnet test` of this project would therefore provide no desktop
scenario evidence. `EnableWindowsTargeting=true` alone does not change these guards.

## Scenario support and findings

| Acceptance scope | Existing source/test evidence at this SHA | Result and remaining gate |
| --- | --- | --- |
| S2/S3 complete scope and foreign scope | `OperationsContinuityClosePresentation.cs:13` checks all five dimensions, exact returned scope and active workflow ID/version. `AccountingCloseViewModelTests.cs:1474` declares the scope and exercises missing/invalid/account/book/period recovery. | Static support located; Windows execution and live ownership refusals pending. |
| S4/S5 missing/stale contributor and repair | `OperationsContinuityViewModelTests.cs:133`, theory values `missing`, `stale`, `scope`, `version`, blocks shared readiness despite locally clear gates and then recovers after replacing the shared decision. The `missing` branch removes the entire projection; `stale` supplies a shared stale-report blocker. | The packet now includes this class in its desktop filter and Windows SEAM support run below. A genuinely missing individual contribution, live contributor data, evidence IDs and repaired inputs remain pending. |
| S6 refusal and repair | `AccountingCloseHttpRecoveryTests.cs:36`, theory values `missing`, `foreign`, `stale`, `unavailable`, `wrong-workflow`, `evidence-blocker`, uses the real module/client registration with a stub HTTP responder; refusals stop posts and a refreshed version permits the selected workflow. | Existing stub-HTTP regression located; no live service failure, durable close or human authorization established. |
| S7/S8 selection and delayed responses | `OperationsContinuityViewModelTests.cs:188` drops a delayed ready response after entity editing. `AccountingCloseViewModelTests.cs:1086`, `:1124`, `:1164` cover workflow changes, delayed plan and closing-entry preparation. `AccountingCloseHttpRecoveryTests.ResponseIsolation.cs:27` covers eight combinations of prepare/lock, scope/workflow change and response failure. | The response-isolation partial class is included by the existing `AccountingCloseHttpRecoveryTests` filter; Operations Continuity is included by the added filter. Windows execution/live delay/reload pending. |
| S4/S5 blocker semantics | `OperationsContinuityClosePresentation.cs:29` formats each shared blocker as contributor ID + message. `OperationsContinuityViewModel.cs:529` fills `BlockerRows` from workflow detail; `OperationsContinuityPresentationModels.cs:154` maps legacy workflow blockers. `OperationsContinuityPage.xaml:60`, `:74` bind only shared label/detail. | **Finding D1:** the shared close projection binding does not expose its blocker type, count, severity, owner or causing record IDs, nor a structured repair action. Legacy workflow rows do not establish visibility of these shared contributor fields. Live presentation criterion remains pending; retain this as a source-supported gap. |
| M1–M4 date/age/status and repair | `MarkFreshnessPresentationTests.cs:13` covers stale, missing-date and future-date account/aggregate inspectors and recovery; `:35` fails closed when assessment is absent; `:46` confirms server status is not recomputed using a desktop threshold. `AccountPortfolioViewModel.cs:478` and `AggregatePortfolioViewModel.cs:390` expose observation, age, valuation date and policy. | Static binding/support located. Windows execution, actual affected positions and retained repair inputs pending. |
| M5/M6 completeness/confidence | Desktop presentation takes shared `Status`/`BlockReason` (`MarkFreshnessPresentation.cs:10`, `:18`) and does not implement a local policy. | No desktop confidence/coverage-specific scenario is present in the focused presentation tests. Server tests may provide bounded support; both rendered clients must still display the population's actual reasons. |
| M7/M8 schedule preview and changed policy | The account/aggregate inspectors consume portfolio assessments. No `ValuationFreshnessPreviewDto` or daily-mark-preview use appears in the WPF sources inspected. | **Finding D2:** WPF inspector parity does not establish a desktop schedule-population preview or changed-policy delayed-preview exercise. Population and live desktop gate pending. |
| M9 retained-evidence refusal/repair; M10 override expiry | `MarkFreshnessAssessmentDto` has no override ID, state or expiry. No `MarkOverride` source/test files exist at this SHA. `ValuationFreshnessAcceptanceTests.cs:106` rejects tampered retained evidence even with arbitrary override actor/expired tags and recovers after restoring the retained payload. | **Finding D3:** the tag-refusal regression is not a scoped override lifecycle/expiry test. Implemented override request/approval/expiry/audit and a live desktop expiry path cannot be claimed; gate pending. The blueprint describes future work only. |

The named `.cs` files above resolve through `source-inventory.json`, which supplies
their complete repository-relative paths and candidate file hashes. Their tests have
been inspected, **not passed by this desktop investigation**.

## Reproducible Windows support run and operator handoff

Use an isolated Windows checkout at the candidate SHA, its configured authenticated
non-production persistent host and the same enumerated population as the browser.
Record Windows/build/browser versions, host identity, policy, actor and tenant/company
before executing. Obtain credentials from the recorded secure source; do not retain
them in logs. Require the candidate SHA check to pass before building:

```powershell
$candidate = '615abde90001ab33bd6e58e545edc7fce635e254'
if ((git rev-parse HEAD).Trim() -ne $candidate) { throw 'Wrong acceptance candidate' }
$acceptanceOutput = Join-Path (Get-Location) 'artifacts/w10-acceptance/615abde9/desktop/windows'
New-Item -ItemType Directory -Force -Path $acceptanceOutput | Out-Null
git status --short | Out-File (Join-Path $acceptanceOutput 'working-tree.txt')
dotnet --info | Out-File (Join-Path $acceptanceOutput 'dotnet-info.txt')
dotnet restore tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release --no-restore /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --filter 'FullyQualifiedName~OperationsContinuityViewModelTests|FullyQualifiedName~AccountingCloseViewModelTests|FullyQualifiedName~AccountingCloseHttpRecoveryTests' --logger 'trx;LogFileName=seam-desktop.trx' --results-directory $acceptanceOutput
if ($LASTEXITCODE -ne 0) { throw 'SEAM desktop support failed' }
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release --no-restore /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --filter 'FullyQualifiedName~MarkFreshnessPresentationTests' --logger 'trx;LogFileName=mark-desktop.trx' --results-directory $acceptanceOutput
if ($LASTEXITCODE -ne 0) { throw 'MARK desktop support failed' }
```

These tests remain supporting evidence. Confirm nonzero expected test discovery in
both TRX files; a stub/empty result cannot satisfy this gate. For a separate candidate
desktop app build, use the maintained Windows command:

```powershell
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release --no-restore /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:WindowsPackageType=None
```

The existing `scripts/dev/run-desktop.ps1 -LaunchMode Production` requires
persistence-backed governance configuration and can launch the configured host/client.
Its Development launch switches to in-memory governance. The maintained screenshot
catalog profile requires fixture mode; those catalog images would not prove this
packet's provisioned population or retained state. Capture the actual authenticated
session's windows at each case instead, using the Windows screen-capture facility.

Run **S1–S10 (including S4b) first, then M1–M11**. Retain the same scope selections, full window
screenshots named `<case>-desktop-before.png` and `<case>-desktop-after.png`, shared
request/response IDs, fixture revisions, source changes, repair evidence and UTC times.
For delayed requests retain the old request identity, selected subject change and
completion ordering; demonstrate that the old result never enables the new subject.
For governed commands use the designated human maker/checker controls and retain
command/refusal/approval IDs. Reload both clients and compare retained state.

For D1–D3, preserve the observed limitation and its evidence; do not infer success
from the browser or server tests. Record the actual operator's per-case accept,
reject or defer decision with rationale. If no operator has decided, write
**pending**. No acceptance decision has been made in this desktop evidence record.
