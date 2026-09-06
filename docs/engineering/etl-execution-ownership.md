# ETL execution ownership

This change addresses the single-publisher requirement in `PRD-008`. An instance-wide lease
alone is insufficient: the shared store permits a same-instance reacquisition, and a separate
ownership check cannot exclude transfer between checking and publishing.

## Execution boundary

`ILeaseManager.TryAcquireExecutionAsync` creates a unique owner for one run. The returned
`IExecutionLease` owns renewal and release; a rejected duplicate has no lease to release.
Execution ownership is required even when general cluster coordination is configured for
single-instance operation. Production composition supplies the shared coordination store.

`IExecutionLease.ExecuteAsync` validates the retained owner, version, and expiry while holding
the store's resource lock. Acquisition and transfer cannot run until the callback completes.
Callbacks must not invoke coordination operations for that same resource. The lease manager
serializes its own renewal with callbacks to avoid competing with an action's lock.
Diagnostics include the retained execution lease alongside ordinary instance leases. Renewal
continues while parsing is idle. Release failures after the run stops are logged; renewal ends
and the lease expires normally, preserving an already-produced committed outcome.

The orchestrator protects admission, staging, audit/reject writes, and publication with this
boundary. Parsing runs outside the lock. A runner suspended in parsing can therefore lose its
lease, but its next publish is rejected. The final flush, catalog rebuild, required export,
checkpoint, source cleanup, and terminal receipt execute in one protected stage. A long action
can delay takeover; contenders remain cancellable and use the store's existing lock timeout.

Failure terminalization is also protected. If ownership has transferred, the stale caller does
not write a Failed transition, checkpoint, or shared terminal receipt. It returns a failure
receipt that explicitly states it was not retained and directs the operator to verify the current
owner and retained source before retrying.

## Regression evidence

The baseline tests ran against unchanged production code and failed in both required ways:

- A duplicate start removed the lease held by the first run.
- A runner resumed after transfer published one record, where zero were permitted.

`EtlJobOrchestratorOwnershipTests.cs` uses the real shared store, lease manager, orchestrator,
normalization, and event pipeline. Parser gates control the race without timing sleeps.
`ExecutionLeaseTests` covers duplicate acquisition in both coordination modes, exclusion during
an action, cancellation of a contender, stale disposal, and manager shutdown.

Real-file failure tests configure delete and archive actions and verify that catalog/export
`Success=false` leaves the original CSV and staged bytes unchanged with no advanced checkpoint.
`EtlCrashRetentionTests` uses a child process with real staging, normalization, JSONL storage,
and catalog services. It terminates the child after staging, after flush, after catalog commit,
or after an export file is written but before export returns. Assertions distinguish those
boundaries and do not rely on graceful disposal. The helper reference change also requires the
existing process-runner regressions.

The first expanded process run exposed a Windows integration defect: `File.OpenRead` in the
catalog conflicted with the JSONL sink's live append handle. Catalog reads now permit that handle
while bounding scans to captured byte counts, checking size and modification time before commit,
and rejecting metadata-read failures. A mutation during scanning retains the previous manifest.

The expanded Windows suite passed 72 selected ETL, execution-lease, lease-manager,
ingestion-coordination, process-runner, and storage-catalog tests with zero failures or skips.
This includes all four forced process-termination scenarios and the catalog sharing repair.
Full repository CI and hosted validation remain required for the candidate. These tests do not
by themselves establish release certification, installed-host recovery, restart deduplication,
or crash-between-every-stage proof.

The additional `durable-flushed` process case initializes the production pipeline's WAL and
persistent deduplication ledger, kills the writer after flush, observes lease expiry, reloads
the interrupted job, and transitions it to Paused before retry. It checks one suppressed
duplicate, exactly one stored trade, a retained checkpoint, successful source cleanup, and
exactly one catalog data file. Its first run passed the retry and deduplication assertions but
failed the catalog count: the internal `_dedup/dedup_ledger.jsonl` was indexed as a second data
file. Default catalog rebuild exclusions now include `_dedup`. The corrected Windows run passed
all 27 selected crash-retention, catalog, and process-runner tests with zero failures or skips
(`artifacts/p0-etl-durable-catalog-tests.log`). This scenario covers restart after the flush
boundary, not every crash window; hosted validation remains required.
