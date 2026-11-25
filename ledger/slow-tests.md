# Slow-test ledger

Scheduled task: find the tests that cost Meridian CI the most time, speed up one group without weakening it, and open a draft `[slow-test]` PR.

## Slowest tests

Source: hosted Meridian CI run 45657856947 (main 858e6eb6, 2026-11-25), artifacts `meridian-ci-dotnet-8423-1` (TRX) and `meridian-ci-browser-8423-1` (vitest batch JSON). verify-dotnet ran its tests in about 1,870 test-seconds over 12m14s wall (2 processes). Lane share = test-seconds / 1,870.

| # | Test or class | Project / shard | Time | Lane share | Notes |
|---|---|---|---|---|---|
| 1 | WorkstationEndpointsTests (399 tests, map split) | Meridian.Tests / core-ui-workstation-endpoints-map | 362 s | 19% | host per test; max 2.0 s |
| 2 | WorkstationEndpointsTests (212 tests, base) | core-ui-workstation-endpoints | 176 s | 9% | same class |
| 3 | WorkstationEndpointsTests (179 tests, A-C) | core-ui-workstation-endpoints-a-c | 148 s | 8% | same class |
| 4 | CollateralExposureServiceTests (3 capacity tests 30-36 s) | core-ui-other | 102 s | 5.5% | attempted slow-tests-001 |
| 5 | ProjectReferenceGraphTests (33) | core-platform-domain-root | 64 s | 3.4% | ExpandingPropertyCycle 25.7 s |
| 6 | DesignerPaperRehearsalTests.ScopedDesigner_ReplaysRealEngine... | core-ui-other | 61 s | 3.3% | single test |
| 7 | QuantScript ScriptRunnerTests (41) | quantscript | 54 s | 2.9% | worker processes; max 4.8 s |
| 8 | LayerBoundaryTests (15) | core-platform-domain-root | 46 s | 2.5% | UiReconciliationApi 26.4 s |
| 9 | ProviderAssetClassCodeRatchetTests (4) | core-platform-domain-root | 27 s | 1.5% | source-tree walk |
| 10 | SecurityAssetClassLiteralRatchetTests (3) | core-platform-domain-root | 22 s | 1.2% | source-tree walk |
| 11 | TradingBrokerageRecoveryEndpointTests (32) | core-ui-other | 20 s | 1.1% | |
| 12 | DedupWalOrderingTests (37) | core-application | 20 s | 1.1% | one test at 10.0 s |
| 13 | SecurityAssetClassComparisonRatchetTests (3) | core-platform-domain-root | 19 s | 1.0% | source-tree walk |
| 14 | PrivateCapitalEndpointsTests (31) | core-ui-other | 19 s | 1.0% | |
| 15 | SecurityMasterWriteRefusalEndpointsTests (61) | core-ui-other | 15 s | 0.8% | |

Browser lane (same run): "Run dashboard tests" step took 688 s for 665 files and 427 file-seconds. The slowest file is accounting-screen.test.tsx at 33 s (93 tests), then operations-continuity-screen at 15 s and trading-screen at 14 s.

## Attempts

| ID | Tests | Cause | Before | After | PR | State |
|---|---|---|---|---|---|---|
| slow-tests-001 | CollateralExposureServiceTests: PastCapacity, AtCapacity (pair), AtCapacityAcrossManyObservations, SnapshotCurrent_ReportsEveryRetainedExposure | Filled the 20,000-row window with one IngestBatch per row; each call copies and re-keys the whole window, so the fill is O(n^2). Fix: batched prefill of distinct observations to 200 rows short of the cap, then single-row deliveries across the cap. | CI 102 s; local class 80 s; local core-ui-other shard 305 s wall | local class 5 s; shard 267 s wall | https://github.com/rodoHasArrived/Meridian-main/pull/3787 | draft, open |

## Not safely fixable

| Tests | Reason |
|---|---|
| (none recorded yet) | Candidates for later runs: DesignerPaperRehearsalTests (61 s single test), the architecture ratchet tests (repeated source-tree walks), WorkstationEndpointsTests (host per test). None has been diagnosed. |

Pre-existing coverage gap found (not changed): AtCapacity_EvictsWholeObservationsRatherThanRows does not catch a row-trimming eviction mutation, because only 2 rows cross the cap. Noted for test-quality.

## Run log

- 2026-11-25 | first recorded run | main be2baba0 | Hosted TRX from run 45657856947 (every later main run was cancelled). Picked CollateralExposureServiceTests (O(n^2) fill). PR #3787 opened as a draft. Equivalence: snapshots identical element by element (temporary test). Stability: 20/20 class runs and 4/4 shard runs passed. Mutation checks: 2 of 3 mutations caught; the row-trim mutation is missed by the original test too. Product note for the owner: IngestBatch does O(n) work per delivery while holding the gate.
