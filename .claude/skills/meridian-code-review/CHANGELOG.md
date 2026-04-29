# meridian-code-review — Changelog

## Unreleased

### Fixed

- `scripts/run_eval.py` now accepts the current skill-creator eval manifest shape by reading
  `evals[].prompt` as the trigger query and defaulting omitted `should_trigger` values to `true`.
- `scripts/run_eval.py` can be invoked by path without relying on the caller to preconfigure
  `PYTHONPATH`.
- `scripts/utils.py` reads `SKILL.md` with explicit UTF-8 encoding so PowerShell runs do not fail
  on non-ASCII documentation characters.

## v1.2.0 (2026-03-16)

### Added

- **Lens 7: Storage & Pipeline Integrity** — new review lens with `[S1]`–`[Sn]` finding codes covering `AtomicFileWriter` usage, WAL flush ordering, `IFlushable` contract, `FlushAsync` on shutdown, `[StorageSink]` attribute, and source-generated JSON in storage paths
- **Multi-file review mode** — when 2+ files are shared, the skill now maps cross-file relationships first and adds a `## Cross-File Dependencies` section; includes checks for View/ViewModel contract compliance, provider/test coverage, and sink/consumer patterns
- **Dual output format** — all reviews (refactor and review-only) now produce both forms: refactored code includes inline `// [Mx]` comments; review-only markdown includes diff snippets for every CRITICAL/WARNING finding
- **4 new eval cases** (evals 9–12): `JsonlStorageSink` storage integrity, `SymbolsPage`/`SymbolsViewModel` multi-file MVVM pair, F# `QuoteValidator` + C# consumer interop round-trip, `TiingoHistoricalProvider` full provider compliance
- **`evals/benchmark_baseline.json`** — accepted pass-rate baselines per eval with regression floor; `aggregate_benchmark.py` now warns (and exits non-zero) when any eval drops >10pp below baseline
- **Shared project context** — SKILL.md now references `../_shared/project-context.md` as the authoritative source for project statistics, key abstraction file paths, and provider inventory

### Changed

- Updated `references/architecture.md` stats: 779 source files (was 734), 266 test files (was 85), 4 test projects (was 2); added section 12 "Storage & Pipeline Integrity Rules" with `AtomicFileWriter`, WAL ordering, shutdown flush ordering, and `IFlushable` contract examples; added frontmatter with `last_verified` date and refresh command
- Updated solution layout in `architecture.md`: removed UWP project (fully deleted); corrected test directory structure and Makefile target count (96, was 66)
- Updated ADR quick reference: added ADR-001, ADR-006, ADR-007, ADR-008, ADR-009 entries; clarified ADR-013 to reference `EventPipelinePolicy.*.CreateChannel<T>()`
- Expanded SKILL.md trigger list to include storage/WAL/sink keywords (`WriteAheadLog`, `AtomicFileWriter`, storage sink compliance)
- `aggregate_benchmark.py`: added `load_baseline()`, `check_regressions()`, `--baseline`, `--no-baseline` flags; baseline auto-detection from skill path and benchmark directory

### Fixed

- Stale UWP references in architecture.md (UWP project was removed; references incorrectly said "LEGACY — no new features" instead of "removed")
- Stale "85 test files" count in SKILL.md key facts and architecture.md overview

---

## v1.1.0 (2026-02-28)

### Added

- Added eval-viewer HTML viewer (`eval-viewer/viewer.html`) and `generate_review.py` script
- Added `scripts/quick_validate.py` for local skill structure validation
- Added `references/schemas.md` with JSON schemas for evals.json, grading.json, and benchmark.json

### Changed

- Expanded eval-07 (order book validator) assertions: added integrity event emission check and timestamp validation assertion
- Expanded eval-08 (cross-project dependencies) assertions: added `SolidColorBrush` platform-specific usage check

---

## v1.0.0 (2026-02-01)

### Added

- Initial skill release with 6-lens review framework (MVVM, Performance, Error Handling, Test Quality, Provider Compliance, Cross-Cutting)
- 8 eval cases covering: DashboardPage MVVM, pipeline flush, Alpaca provider, backfill error handling, unit test quality, F# interop, order book validator, cross-project dependencies
- `agents/grader.md` with 9-step grading process
- `scripts/` automation: `run_eval.py`, `aggregate_benchmark.py`, `package_skill.py`, `utils.py`
