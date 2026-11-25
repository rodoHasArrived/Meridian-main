# Data-pipeline audit ledger

Task: audit of market-data ingestion, storage, and durability for integrity and recoverability.
Focus rotates by weekday (America/Phoenix): Mon streaming, Tue backfill, Wed WAL and atomic writes,
Thu dedup/normalization/accounting storage, Fri archival/retention/lineage.

## Open findings

| ID | Title | Files or area | First seen | Last seen | Status | Evidence |
|---|---|---|---|---|---|---|
| data-pipeline-001 | `--wal-repair` never initializes the WAL, so it can rewrite (rename over) the segment a running collector still has open | src/Meridian.Application/Commands/WalRepairCommand.cs:76-83; src/Meridian.Storage/Archival/WriteAheadLog.cs:1281-1291,1393 | 2026-11-25 | 2026-11-25 | open | No InitializeAsync call, so `_currentWalPath` and `_lastRecoveredWalPath` are null and every segment can be rewritten. On Linux, `File.Move(overwrite)` leaves the live writer on an unlinked inode. No test covers it. |
| data-pipeline-002 | No single-writer lock on a WAL directory; another process (or a second `WriteAheadLog`) can commit, truncate, or repair the same segments | WriteAheadLog.cs (no lock file); PipelineFeatureRegistration.cs:164; ArchivalStorageService.cs:43 (currently unwired) | 2026-11-25 | 2026-11-25 | open | COMMIT is a global horizon (`max` over all segments), so one writer's COMMIT(N) hides another writer's records at or below N. Truncate's scan fallback deletes them. |
| data-pipeline-003 | A failed directory fsync (open or fsync error) is logged as a warning and treated as success | src/Meridian.Storage/Archival/AtomicFileWriter.cs:961-1000 | 2026-11-25 | 2026-11-25 | open | Barriers that depend on it: WAL segment creation, the archive before WAL delete, JSONL new-file entries, and snapshot publication before WAL truncate (PaperFinancing, TradeFill). No metric; no test. |
| data-pipeline-004 | RepairAsync deletes corrupt WAL lines for good, keeps no quarantine copy, and counts a torn tail as corruption | WriteAheadLog.cs:1300-1400; WalRepairCommand.cs | 2026-11-25 | 2026-11-25 | open | Recovery and truncate keep corrupt files "for inspection", but repair rewrites them without a backup. The CLI then exits DataCorruption even for a harmless torn tail. |
| data-pipeline-005 | Sequence recovery ignores corrupt lines and the segment-name base, so new records can reuse sequence numbers after Alert/Skip-mode corruption | WriteAheadLog.cs:1214-1228 (GetLastSequenceNumberAsync), 800-832 | 2026-11-25 | 2026-11-25 | open | max() covers checksum-valid records only. After a reused sequence, the name-derived truncation bound sits below the damaged segment's true maximum. Low severity (hardening). |

## Run log

- 2026-11-25 (Wed) | main 7138e532 | WAL and atomic writes | Examined: repair CLI against a live segment, two writers on one WAL dir, directory-fsync failure, repair evidence loss, sequence reuse after corruption, crash between archive and delete, torn tail under BatchedSync, disk-full flush fault | First recorded run. Core append, flush, commit, truncate, and EventPipeline ordering (WAL flush, then sink append, sink fsync, dedup commit, WAL commit) look sound. The JSONL sink fsyncs before the WAL commit. Opened 001-005. Ran 197 WriteAheadLog and AtomicFileWriter tests in Meridian.Storage.Tests: all pass.
