# Code review remediation — 2026-11-12

Mode: **Deep Review**. Coordinator and final validation owner: root integration lane.
Scope: the supplied codebase assessment, including correctness, security, maintainability,
test coverage, documentation, and CI duplication. Use synthetic fixtures. No live activation.

## Parallel task manifest

| Lane | Initial source set and ownership | Validation owner / first proof |
| --- | --- | --- |
| Storage safety | `FileMaintenanceService`, JSONL write policy, `AtomicFileWriter`, WAL recovery | storage_safety / focused maintenance and WAL tests |
| Storage sinks | Composite/catalog/JSONL/Parquet sinks, historical coverage | storage_sinks / focused sink and coverage tests |
| Financial correctness | Simulated portfolio, backtest engine/metrics, paper gateway, commission model, live metrics, journal validation | finance / backtest, execution and ledger tests |
| Providers and infrastructure | Polygon, Robinhood, Alpaca historical, rate limiting, provider health, detailed health, status writer | providers_infra / synthetic HTTP, clock and monitoring tests |
| UI and configuration | Config endpoint, WPF config factories/ticker, browser fetch cleanup, certification test | ui_security / endpoint, desktop and browser tests |
| Structure and repository upkeep | Project references, test project split, responsibility extraction, ADR resource, CI, evidence project isolation | structure_docs_tests / project graph, test discovery and workflow checks |
| Integration and missing tool coverage | MCP tools, QuantScript worker process tests, build integration, assessment evidence | root / targeted .NET tests and `git diff --check` |

Working snapshot: `codex/correct-review-findings`, based on `86a1689f20`; clean before work.
The supplied findings are hypotheses until checked against source and targeted tests.
The root lane installed .NET SDK 10.0.100 locally because the environment initially had no
`dotnet` on PATH. After environment resumption, validation used SDK 10.0.404.

Shared-file coordination: storage safety owns JSONL fencing, storage sinks owns buffer lifecycle;
coordinate before edits. Structure owns project and solution files; leave test sources in place
while changing compile ownership. UI owns the affected screen files. New MCP tests belong to root.

Integration order: infrastructure, finance, storage sinks, storage safety, UI, structure, final
documentation. Shared checkout edits are visible immediately; no intermediate agent commits.
Rerun validation when dependent source, project references, or test ownership changes.
For a failing lane, correct its changes in place or revert only its owned hunks; preserve other lanes.

CI changes are designated for **human governance review** before merge, as required by
`AGENTS.md`. Required check identities and safety tests must be preserved.

## Remediation ledger

| Assessment item | Result |
| --- | --- |
| H1: defrag loses concurrent writes / mixes Flat partitions | One-day default; authoritative parsed partition closure; writer fences held through retirement; partition identity retained in fragment names. Open-writer, cross-process, embedded-date and Flat isolation regressions. |
| H2: backfill credentials exposed | Cloned response projection masks all nine current credential fields, including Nasdaq/OpenFIGI. ViewConfig HTTP regression checks serialization and unchanged stored values. Independent source review completed. |
| Backtest zero/negative rates | Zero postings omitted; negative financing amounts post with the appropriate direction. Engine and portfolio regressions. |
| Calendar interest and annualization | ACT/365 financing and calendar-return annualization applied consistently, including walk-forward metrics. Weekend regression. |
| Double commission cash flows | Trade principal and separate commission flows reconcile to ledger cash. |
| Market fill caller-price override | Market orders use observed feed prices; caller LimitPrice is ignored. No-feed rejection/scaffold policy remains explicit. |
| Partial-fill commissions | Cumulative per-order fees, minimums and maximums; engine accounts only accepted slices. Rejected-slice regression. |
| Live WinRate/ProfitFactor fabricated as zero | Explicit unavailable values serialize as JSON null; retained summary-only coverage warning. No closed-trade outcomes invented from fills. |
| Composite retry duplicates | Per-child in-process acknowledgements skip successful sinks on retry; open circuits do not acknowledge required writes. |
| Catalog counts overwritten | Cumulative ranges/counts, durable directory indexes, flush retry baselines and catalog-save retries. Restart regression. |
| Retained sink buffers | Empty successful batches retire safely under buffer locks; late/concurrent append admissions retry retired buffers. Failed batches remain recoverable. |
| Intraday coverage | Session timezone and bar-start session date determine checkpoints, with an explicit timezone override. After-hours resume regression. |
| Provider partial results/pagination | Polygon/Robinhood propagate failed pages; all Alpaca historical loops share cursor-cycle checks. Robinhood disposes responses, paces requests and validates continuation origin before authentication. |
| WPF synchronous DI disk reads | Async startup primes configuration; six factories and strategy workspace construction consume the cached data root. |
| Monitoring thresholds/downtime/version/status loop | Correct degraded interval, capture downtime before reset, assembly version, retry recoverable status IO failures. |
| Orphan/quarantine cleanup | Only root-relative names inspected; quarantine is retained by orphan and temporary cleanup. |
| Atomic replace | Atomic filesystem replacement preserves the live destination while retaining backup behavior. Concurrent-reader regression. |
| WAL crash suffix | Unterminated torn writes recover without a corruption alert; complete-record corruption checks remain. Valid unterminated records are preserved. |
| Parquet date inconsistency | UTC filename dates match UTC partition directories. |
| Mixed functional journal currencies | C# and F# journal validation reject mixed functional currencies while preserving multi-transaction-currency journals. |
| Rate-limit boundary/config changes | Expired boundary entries evicted; limits update the existing shared limiter. Deterministic clock tests. |
| Async timer overlap | Provider health and desktop ticker reject overlapping ticks and handle cancellation. |
| Unmanaged HTTP fallbacks | Four shared connection services require the injected client factory for network calls. |
| ADR resource pattern injection | Numeric ID validation precedes file enumeration; adversarial resource tests. |
| Browser fetch lifecycle | First-run and covered-call initial requests abort on unmount; component and actual navigation checks. |
| Reversed dependency edges | Storage business adapters separated; Risk depends on Execution.Sdk; reconciliation adapters moved from Infrastructure to DataIntegration; unused Strategies edge removed. |
| UI application-layer coupling | Seven readers (911 lines) compile in Reporting.ReadModels over four lower owners, with legacy public type forwarders. Cross-domain orchestration remains in Ui.Shared; its total direct-reference count is not claimed to be reduced. |
| Large services/view models | Extracted stateless desktop presentation, report artifact rendering, reporting schedule persistence, domain endpoint registration, browser provider components and Security Master projections/state. Public entrypoints retained. |
| Giant test project | Dedicated Storage/Execution compile ownership and CI roster entries; host integration cases remain in the main suite. No duplicate case sources. |
| Thin MCP/worker coverage | Five MCP tool families plus ADR resource tests; actual worker process launch, protocol, request validation and execution tests. |
| Skipped certification test | Re-enabled against the current typed evidence authority; rejects nonempty but unverifiable evidence URIs. |
| Timing hotspots / assertion-free singleton test | Identified TTL, periodic flush, revision ordering and concurrency waits use clocks or acknowledgements; provider-health tests isolate instances and assert outcomes. |
| Documentation bloat | 338 PNGs (101,861,161 bytes) converted to LFS pointers at the current revision; original image hashes verified. Historical blobs retained. |
| Archived Profile projects | Independent build and package props isolate all six projects. |
| Stale status reports | Owning generators refreshed status, source and navigation outputs. No release-readiness claim inferred. |
| CI overlap | Rechecked against current workflows: legacy PR work is secret scanning; its full validation is nightly/manual. Documentation generation inspects repository-wide structure, so a narrow path filter would miss valid changes. Required gates retained. |

## Validation

| Check | Outcome |
| --- | --- |
| Full Storage suite | 930 passed; zero failures/skips |
| Full Execution suite | 992 passed; zero failures/skips |
| Full Backtesting suite | 471 passed; zero failures/skips |
| F# LedgerKernelTests | 55 passed; zero failures/skips |
| Provider-health UI service tests | 21 passed; zero failures/skips |
| QuantScript worker process/protocol/launch slice | 18 passed; zero failures/skips |
| Browser Settings/Accounting and accessibility slice | 185 passed |
| Browser first-run/covered-call slice | 65 passed |
| Browser bundle / TypeScript | Passed; existing bundle-size warnings |
| Chromium workflows | Setup, abort-on-navigation, provider filter/guided setup, AAPL search/identity selection; 1440×900 and 1366×768; no page errors |
| Windows-targeted desktop compilation | Passed; WPF runtime execution requires Windows |
| CI runner Python tests | 53 cases completed; three environment skips |
| Main integration and boundary slice | 1,027 passed; zero failures/skips; actual HTTP credential masking, timing hotspots, reporting readers and assembly boundaries |
| Connection-service and API-client regressions | 40 passed; zero failures/skips |
| Documentation CI lane | `bash scripts/ci.sh --lane verify-docs` passed, including convergence, committed-output drift checks and 83 adapter-readiness automation tests |

Main test evidence is retained in the integration run's TRX output. Focused commands use
`dotnet test <test-project> --no-build --no-restore --filter <owning-test-class>` after compiling
the changed graph. The complete Storage/Execution/Backtesting project suites were also run.
`bash scripts/ci.sh --lane verify-docs` covers documentation generation and drift checks;
the full aggregate `bash scripts/ci.sh` was not run. Hosted required checks remain authoritative.

## Delivery limits

The original remediation validation was local to `codex/correct-review-findings`; it did not
include publication or live activation. The owner subsequently authorized publication and merge
when ready; the synchronization evidence below records that preparation.
Rebuild host and plugin consumers together: Risk constructor parameter contracts changed even
though relocated type identities are forwarded. LFS objects are present locally and must be uploaded
when publishing; no historical Git rewrite was performed. The large orchestration services still
span multiple domains; extracted boundaries improve ownership without claiming a complete
application-layer redesign.

## Synchronization for publication

The publication work merges upstream `63e84bb978b5a8b8cffbce3820b6842aa907423f` into the remediation
branch. The validation table above describes the original local remediation revision; overlapping
source changes require fresh validation after synchronization.

The merged image inventory contains 329 scoped PNGs (90,112,988 bytes), all matching the newest
upstream image bytes and verified local LFS objects. Upstream removed 11 duplicate UI diagrams
and added two screenshots; those changes are retained. The original capture is preserved in
`lfs-migration.json`, with a separate `upstreamSynchronization` inventory. Upload verification
must complete before publication is considered ready; this record does not claim remote upload.

The synchronized local worktree has additional validation evidence below. These results do not
replace hosted checks on the eventual pull-request head, and browser slices may share cases.

| Synchronized check | Outcome |
| --- | --- |
| Full Storage suite | 962 passed |
| Full Execution suite | 1,109 passed |
| Full Backtesting suite | 516 passed |
| F# ledger slice | 57 passed |
| Browser UI regression slices | 260 passed |
| Browser ratchet checks | 21 passed; existing cap budgets retained |
| Strategy-focused browser tests | 115 passed |
| Browser artifacts | Rebuilt after strategy leaf extractions; cap budgets unchanged |
| Windows-targeted desktop compilation | Passed with zero errors; Windows runtime execution remains separate |
| Final main integration slice | 1,187 passed; zero failures/skips |
| Saved-view and status-writer focused regressions | 37 passed; includes foreign-view rejection and column-only personal copies |
| Provider-health UI service tests | 21 passed |
| QuantScript worker boundary slice | 18 passed |
| CI test-roster Python tests | 50 passed; three environment skips |

Local synchronization is validated. At this validation point, publication was blocked by the managed network policy rejecting
the Git LFS verification host `lfs.github.com` (HTTP CONNECT 403). No LFS hook or repository rule
was bypassed; uploading and clean-checkout verification remain prerequisites to publication.

Saved-table-view reads and endpoints now share normalization, including default columns and an
unknown-view-ID guard. Additional negative/default-column cases cover those boundaries. The
status-writer retry test isolates static metrics; its change does not alter production behavior.
The final C# formatting changes affect four test files only. Reviewed source hashes were refreshed
only for `SRC-UI-SHARED`, `SRC-UI-DASHBOARD` and `SRC-UI` after these final changes; the accepted
WPF source baseline remained stable.

### Second synchronization after network access was applied

The active environment applied policy revision 2, and the normal Git LFS client successfully
uploaded and verified all 393 reachable objects (102 MB). No LFS hook or repository protection
was bypassed. Main advanced to `b51cf47ef4` during the network wait, so the publication branch
also incorporates its fixed-income units, exact-decimal transport, retained evidence and generated
Security Master codec changes.

Upstream now contains the explorer fixes and additional nontruncating admission bounds; the
explorer endpoint and reader retain those latest implementations. Browser extraction preserves
all 592 latest declarations without token changes. The upstream equity-path component and
view-model pair replaces the superseded local single-file extraction, avoiding a TypeScript
module-resolution collision. Presentation budgets remain unchanged. Independent review found
no material issue in the combined accounting/storage registration, generated codec ordering or
reporting forwarding boundaries.

The second synchronized backend build completed with zero errors. Its browser slice passed
408 tests across 18 files, and Chromium workflow checks passed at both desktop viewports.
Documentation generation converged with zero failed steps or protection violations; source
hash validation reported zero errors and warnings. Independent remote hydration began with an
empty LFS cache and no local references: all 329 images (90,112,988 bytes) matched their retained
manifest, Git pointers and freshly downloaded objects. `git lfs fsck --objects --pointers` passed.
Hosted checks on the published pull-request head remain authoritative for the merge.

### Hosted validation corrections and final synchronization

The expanded parallel application slice passed 2,074 tests; the updated Storage and Execution
suites passed 961 and 1,110 tests respectively, with zero skips. These results include the
server-options binding, trusted-origin, diagnostic privacy and log-field regressions. Supplemental
backfill logging coverage uses a provider cutoff beyond the production stale tolerance so both
resume and stale diagnostics are exercised without changing the requested symbol.

Hosted CodeQL analysis exposed 21 reported flows. Thirteen path reports shared one source: the
conversion endpoint could infer request-body storage options without service registration. All
storage options now bind explicitly from trusted services, with real HTTP tests for configured
roots and missing registration. Four financial boundaries construct server-owned commands while
preserving every reviewed-evidence field; undefined origins downgrade to nonhuman authority and
cannot append a reviewed journal. Privacy diagnostics omit workflow/fund identities, paths and
exception objects. Remaining diagnostic fields encode line breaks and preserve business identities.
No security alert was dismissed or suppressed; the hosted security gate must rescan the final head.

Windows ran 2,688 tests on the earlier published head: 2,687 passed and one continuous-reader
storage test exposed a destination-name gap during backup replacement. The shared Windows
publication primitive now performs one native rename while retained delete-sharing readers keep
their generation. Backup snapshots receive the original DACL at creation, are fsynced before
publication, and retain durable recovery evidence if backup promotion fails after the live commit.
Independent review checked native layouts, handles, extended path aliases, sharing denial and
recovery sequencing. Actual native execution remains a required hosted Windows result.

Disposable NuGet restore clones retain LFS pointers through a command-scoped checkout setting;
they do not require binary assets. Restore fingerprints, caller configuration and normal publication
hooks remain enforced. The helper regression suite passed 23 tests, including missing-object and
environment-isolation proofs. The roster suite passed 51 tests with three existing optional
PowerShell skips. All 15 catalog facts remain in the main assembly, including its three integration
facts, and discovery confirms the focused Storage assembly no longer owns them.

Publication also incorporates upstream `a2002fcea559217bfd5f8c73d1cbd90de6b174b9`, including the
shared-accrual and feature-composition work. Source merged without conflicts. Independent review
confirmed the same 16 feature modules, activation rules, financial adapter ownership and origin DTO
fields. All 52 refreshed screenshots retain exact upstream bytes. The current scoped inventory is
329 PNGs (90,119,455 bytes), recorded separately as `thirdSynchronization` without altering earlier
captures. Remote upload and hydration verification must cover that final inventory before merge.

The synchronized targeted run passed 903 tests with zero skips, covering changed upstream
accounting and receipt cases, composition order, architectural boundaries and the security
regressions. Its ten backfill coverage/resume cases include the corrected stale-log fixture.
These cases overlap earlier slices and are not an additional independent total.

Upstream later advanced to `a0f1cfffb2d1b2dda4a25c638b1b6540b338b5cb`. Its startup-plan and
reporting-policy ownership changes retain the reviewed initialization sequence and durable-store
scope. The compatibility query-context conversion is public across the read-model assembly seam;
its six fields and policy behavior remain unchanged. No image bytes changed in this synchronization.

The next hosted Windows run passed all atomic reader, backup, alias and long-path cases, but two
protected-DACL assertions failed. Loaded `FileSecurity` descriptors carried no modified-section
flags, so persistence was a no-op. The replacement now copies the access descriptor into a fresh
object and explicitly persists its access rules, preserving the original protection flags without
owner, group or audit changes. The unchanged assertions require another Windows runtime run.
Failure-tail printing also escapes characters unsupported by the destination console encoding;
raw UTF-8 artifacts, original failure status and later-shard execution are preserved. Its Python
regressions passed 61 tests with three existing optional PowerShell skips.

The final integration incorporates `dbc7651a05a6f0b591523e4a93e8b40ab181270b`, including the
governed intercompany agreement registry and the newer assessment repairs. All new persistence
sources and migration 006 retain single ownership through Storage.Accounting; integration-tagged
cases remain in the canonical main suite. The intercompany authority is supplied by the deferred
factory consumed by the journal writer, with validation and transactional interfaces sharing the
same singleton. Resolution-order and absent-authority regressions cover that registration seam.
Posting-origin regressions also retain the nested agreement ID/version and existing evidence.

The browser integration preserves all 615 upstream declarations, including the agreement editor,
versioned journal references and recovery workflow. Its 291 tests, TypeScript build, bundle,
unchanged style/size gates and Chromium workflow checks passed. WPF retains the new agreement
view and asynchronously primed configuration snapshots. A local startup run encountered 26
disk-capacity errors; obsolete ignored build outputs were removed before rerunning those cases.

The next synchronization incorporates `3ada46416ddaea7215a8b806759d9b6450a80b0b`, preserving the
new default-off preparer-attestation lanes and their native producer authority. Both new ledger
interfaces resolve to the existing writer singleton alongside the deferred agreement authority.
All twelve new storage sources and migration 066 retain one adapter owner. The new integration
cases remain in the canonical main suite. No native writer or defragmentation repair changed.
The fifth-sync fund-agreement and UI projection slices passed 14 and 77 tests respectively.
The sixth browser synchronization retained 31 upstream attestation files byte-for-byte and all
615 extracted declarations; its changed attestation, API and workbench slice passed 173 tests.
The larger combined regression slice is being rerun after correcting the composition test's
service-collection interface declaration and a nullable native-posting actor boundary. New
reconciliation support imports use the extracted DataIntegration contract namespace. Earlier
failed builds are not passing test results.

### Final local validation before documentation-only synchronization

The combined application, startup, reporting, intercompany, attestation and security slice passed
1,582 of 1,582 tests with zero skips. Subsequent formatting changed whitespace in three test
files only; their assertions are unchanged. The synchronized inventory contains 329 PNGs
(90,922,143 bytes), exactly matching upstream `4d1059e0fd03dcf0230f49db06d67149000a3447`.
The later `f65ab80d30d0bb7cff6279ad0a2f61fe17ec0871` synchronization changes documentation
and tooling only, with no executable source changes. Required hosted checks on the final
published head remain the merge authority.

### Latest Windows backup-descriptor correction

Diagnostic Windows run `43327730266` passed 2,777 tests and failed one backup DACL assertion.
Destination ACL and publication cases passed; the backup retained the same access rules but its
descriptor was `D:P` instead of the expected `D:PAI`. The repair reapplies the original access
descriptor to the staged backup after creation, before either live or backup publication. Independent
review accepted lifecycle and access safety. Subsequent manual and pull-request Windows runs on
`7798dc2b18` each passed all 2,778 tests with zero failures, including all 60 atomic-writer cases and
both protected-DACL rows. Smoke publication also passed. These later results verify the repair;
the earlier 2,777-pass/one-failure diagnostic remains historical evidence.

### Storage and lifecycle authority boundaries

All ten storage handlers that need `StorageOptions` now resolve trusted options from the request's
server service scope, failing before filesystem access if registration is missing. Tier migration
rejects out-of-root sources, discovers entries beneath the configured root and rejects symbolic links
or reparse points. It guards the compression-adjusted target before access and after copying and
compression finish, before atomic publication, then rechecks both paths before source deletion.

Manual journal lifecycle commands explicitly construct all fifteen contract fields, retaining request
evidence while deriving actor, tenant/company, report principals and origin from server authority.
Only eight defined actions are admitted: Validate requires ledger read permission; Submit, Approve,
Reject, Post, Reverse, Rebook and LockAfterClose require certification permission. Undefined actions
are refused before service invocation. The supplied actor remains a separate audit assertion.

Both changes passed independent source review. The combined storage and posting-authority slice
passed 136 of 136 tests with zero skips, including all 25 new lifecycle cases and all 37 storage
endpoint and migration cases. A fresh CodeQL result remains pending; this record claims no CodeQL
closure.

The configuration-reference producer now emits the notice required by the existing documentation
rules. Its focused regression generates the reference through the real producer and verifies the
actual rule accepts it and rejects an output with the header removed. All five producer tests pass,
and the generated file passes the scoped rules check with zero errors. The rule itself is unchanged.

### Compatibility and exact agreement-filter follow-up

The follow-up to `e65826817330fb3d10beb094b45052744df1856e` routes intercompany optional-text
normalization and canonical JSON fingerprints through shared primitives. Optional blank terms,
meaningful internal whitespace and lowercase retained command/review hashes remain unchanged.
CSV and IB Flex imports reuse digest-byte computation while preserving uppercase row checksums,
UTF-8 and serialized row/element bytes, source-file hash casing, duplicate keys and null rejection.

Financial Record Explorer agreement ID/version filters now match the bound detail fields exactly,
including saved views. Version `1` cannot match `10`; unrelated text cannot admit a row with another
or missing agreement. Both agreement dimensions appear in the shared reader's filter choices.
The dashboard source-contract correction checks the actual Security Master projection owner,
the compatibility entrypoint's function/type re-exports and the screen's schedules-view binding.
Production browser behavior and existing budgets are unchanged.

The frozen local follow-up passed 126 main-suite cases and 17 fund-structure cases, with zero
failures or skips. These scopes include all twelve new cases: seven reader-filter, two persisted
statement-checksum and three agreement-rule cases. The targeted browser slice passed 76 cases,
including all 31 design-system contract cases; TypeScript and ESLint passed. Formatting passed
for all seven changed C# files. The maintained local `verify-workflows` lane completed 2,345 cases
with zero failures/errors and 16 existing skips, using real PowerShell 7.7 and isolated Pillow 13
with the approved actionlint/ShellCheck tools. Independent source reviews passed. Counts describe
their own revisions and overlapping scopes; they are not cumulative totals.

On the published `e658268` head, the CodeQL gate reported **No new alerts in code changed** after
the 36-alert storage/lifecycle repair, and Windows Desktop Build passed. The earlier native
`7798dc2b18` runs retain their separate 2,778/2,778 full-WPF and 60/60 atomic-writer evidence.
These hosted results do not validate the unpublished follow-up. Fresh required hosted checks on
the next published head remain the merge authority; no security alert or required check was
suppressed or bypassed.
