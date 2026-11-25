# Data-pipeline audit ledger

Task: audit of market-data ingestion, storage, and durability for integrity and recoverability.
Focus rotates by weekday (America/Phoenix): Mon streaming, Tue backfill, Wed WAL and atomic writes,
Thu dedup/normalization/accounting storage, Fri archival/retention/lineage.

## Open findings

None.

## Fixed findings

All five were fixed in https://github.com/rodoHasArrived/Meridian-main/pull/3771 (branch
`claude/dreamy-euler-804792`, head `55c14052`). That PR was open and unmerged when this was
written, with CI still running on its head. A finding counts as closed on `main` only once the PR
merges; the next Wednesday run should confirm the merge and re-check each fix on `main`, and
reopen any finding whose fix did not land.

| ID | Title | Files or area | First seen | Last seen | Status | Fix |
|---|---|---|---|---|---|---|
| data-pipeline-001 | `--wal-repair` never initializes the WAL, so it can rewrite (rename over) the segment a running collector still has open | src/Meridian.Application/Commands/WalRepairCommand.cs; src/Meridian.Storage/Archival/WriteAheadLog.cs (RepairAsync) | 2026-11-25 | 2026-11-25 | fixed in PR 3771 (unmerged) | `RepairAsync` on an uninitialized instance takes the WAL directory's single-writer lock; `--wal-repair` refuses to run while a writer is live and now targets the resolved data root. Tests: WalRepairCommandTests, WriteAheadLogDirectoryLockTests, cross-process case in WriteAheadLogProcessTests. |
| data-pipeline-002 | No single-writer lock on a WAL directory; another process (or a second `WriteAheadLog`) can commit, truncate, or repair the same segments | src/Meridian.Storage/Archival/WalDirectoryLock.cs; WriteAheadLog.cs (InitializeAsync, DisposeAsync) | 2026-11-25 | 2026-11-25 | fixed in PR 3771 (unmerged) | `InitializeAsync` holds an exclusive `writer.lock` until disposal; a second writer fails with `WalDirectoryInUseException` after `WalOptions.WriterLockTimeout`. `ArchivalStorageService` still points at `<dataRoot>/_wal` but is unwired and would now fail fast. Tests: WriteAheadLogDirectoryLockTests, WriteAheadLogProcessTests. |
| data-pipeline-003 | A failed directory fsync (open or fsync error) is logged as a warning and treated as success | src/Meridian.Storage/Archival/AtomicFileWriter.cs (SyncDirectoryStrictAsync); WriteAheadLog.cs; src/Meridian.Storage/Sinks/JsonlStorageSink.cs; src/Meridian.Execution/Events/PaperFinancingPostingService.cs, TradeFillPostingStore.cs | 2026-11-25 | 2026-11-25 | fixed in PR 3771 (unmerged) | Strict directory sync throws at the barriers named in the finding: WAL segment creation, archive before WAL delete, repair quarantine, JSONL new-file entries, snapshot publication before WAL truncate. Counter `mdc_storage_dir_fsync_failures_total` added. Residual, by design: filesystems that cannot fsync directories (EINVAL/ENOTSUP) are tolerated; the best-effort sync inside the atomic write methods and the non-WAL archive-then-delete paths (JsonlArchiveFiles, TierMigrationService, compression) are unchanged; no alert rule for the counter; no fault-injection test for the JSONL flush or trade-fill snapshot call sites. Tests: AtomicFileWriterDirectorySyncTests, WriteAheadLogDirectorySyncTests, PaperFinancingPostingTests.SnapshotEntryCannotBeMadeDurable_KeepsWalRecordsAndReplaysAfterRestart. |
| data-pipeline-004 | RepairAsync deletes corrupt WAL lines for good, keeps no quarantine copy, and counts a torn tail as corruption | WriteAheadLog.cs (RepairCoreAsync, QuarantineOriginalAsync); WalRepairCommand.cs | 2026-11-25 | 2026-11-25 | fixed in PR 3771 (unmerged) | Each rewritten segment is first copied to `<wal>/quarantine/`; a torn tail is reported as `IncompleteTailRecords`, never rewritten, and no longer makes the CLI exit DataCorruption. Also fixed: dry-run only accepted the MDCWAL01 header; a cancelled scan could rewrite a partial record list. Tests: WriteAheadLogRepairQuarantineTests, WalRepairCommandTests. |
| data-pipeline-005 | Sequence recovery ignores corrupt lines and the segment-name base, so new records can reuse sequence numbers after Alert/Skip-mode corruption | WriteAheadLog.cs (GetLastSequenceNumberAsync, CountRecordLinesAsync) | 2026-11-25 | 2026-11-25 | fixed in PR 3771 (unmerged) | A segment with corrupt lines is bounded by its name's base sequence plus its record line count (line count added on top for foreign-named segments); healthy and torn-tail-only segments stay contiguous. Tests: WriteAheadLogSequenceRecoveryTests. |

## Run log

- 2026-11-25 (Wed) | main 7138e532 | WAL and atomic writes | Examined: repair CLI against a live segment, two writers on one WAL dir, directory-fsync failure, repair evidence loss, sequence reuse after corruption, crash between archive and delete, torn tail under BatchedSync, disk-full flush fault | First recorded run. Core append, flush, commit, truncate, and EventPipeline ordering (WAL flush, then sink append, sink fsync, dedup commit, WAL commit) look sound. The JSONL sink fsyncs before the WAL commit. Opened 001-005. Ran 197 WriteAheadLog and AtomicFileWriter tests in Meridian.Storage.Tests: all pass.
- 2026-11-25 (Wed, follow-up) | PR 3771 head 55c14052 | Fix pass for 001-005 | All five fixed in one PR, not yet merged. Local results on that head: 224 WriteAheadLog/AtomicFileWriter tests in Meridian.Storage.Tests pass; 376 WAL-consumer tests in Meridian.Tests pass (13 skipped); all 1567 Meridian.Execution.Tests pass. Full local ci.sh not run; GitHub CI on the head had not reported. Three existing tests that kept two live writers on one WAL directory were changed to dispose the first.
