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
`dotnet` on PATH. Exact validation outcomes are recorded below after integration.

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
| CI runner Python tests | 53 passed; three environment skips |
| Main integration and boundary slice | 1,027 passed; zero failures/skips; actual HTTP credential masking, timing hotspots, reporting readers and assembly boundaries |
| Documentation CI lane | Generators converged with zero failures; final committed-output check pending local commit |

Main test evidence is retained in the integration run's TRX output. Focused commands use
`dotnet test <test-project> --no-build --no-restore --filter <owning-test-class>` after compiling
the changed graph. The complete Storage/Execution/Backtesting project suites were also run.
`bash scripts/ci.sh --lane verify-docs` covers documentation generation and drift checks;
the full aggregate `bash scripts/ci.sh` was not run. Hosted required checks remain authoritative.

## Delivery limits

Changes are local to `codex/correct-review-findings`; no push, pull request, merge or live activation.
Rebuild host and plugin consumers together: Risk constructor parameter contracts changed even
though relocated type identities are forwarded. LFS objects are present locally and must be uploaded
when publishing; no historical Git rewrite was performed. The large orchestration services still
span multiple domains; extracted boundaries improve ownership without claiming a complete
application-layer redesign.
