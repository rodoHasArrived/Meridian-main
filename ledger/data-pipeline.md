# Data-pipeline audit ledger

Task: audit of market-data ingestion, storage, and durability for integrity and recoverability.
Focus rotates by weekday (America/Phoenix): Mon streaming, Tue backfill, Wed WAL and atomic writes,
Thu dedup/normalization/accounting storage, Fri archival/retention/lineage.

## Open findings

| ID | Title | Files or area | First seen | Last seen | Status | Evidence |
|---|---|---|---|---|---|---|
| data-pipeline-001 | `--wal-repair` could rewrite a live segment | WalRepairCommand.cs; WriteAheadLog.cs RepairAsync | 2026-11-25 | 2026-11-26 | resolved (#3743, da361331) | RepairAsync calls AcquireOwnership (.owner.lock); repair refuses owned dirs. |
| data-pipeline-002 | No single-writer lock on a WAL directory | WriteAheadLog.cs InitializeAsync; PipelineDurabilityPaths | 2026-11-25 | 2026-11-26 | resolved (#3743, da361331) | Exclusive .owner.lock + WalDirectoryInUseException; per-workload _wal/{collector,backfill}. |
| data-pipeline-003 | Failed directory fsync treated as success | AtomicFileWriter.cs; WAL; JsonlStorageSink; PersistentDedupLedger | 2026-11-25 | 2026-11-26 | resolved (#3743, da361331) | SyncDirectoryOrThrowAsync at WAL create/archive/adopt, sink flush, dedup commit, tier migration; counter mdc_storage_directory_fsync_failures_total. Residual: no alert rule shipped. |
| data-pipeline-004 | RepairAsync drops corrupt lines with no quarantine copy; torn tail counted as corruption | WriteAheadLog.cs RepairCoreAsync | 2026-11-25 | 2026-11-26 | open | Not on main 5fd5e185 (no quarantine/IncompleteTailRecords); fix sits in open PR #3771, which overlaps #3743 and needs a rebase. |
| data-pipeline-005 | Sequence recovery ignores corrupt lines, so sequence numbers can be reused | WriteAheadLog.cs GetLastSequenceNumberAsync (~1242) | 2026-11-25 | 2026-11-26 | open | main still takes max over ReadWalFileAsync records only; fix in open PR #3771. |
| data-pipeline-006 | Unsequenced non-trade/quote events collapse to one per (source,symbol,type) per 24h TTL | PersistentDedupLedger.cs ComputeEventKey fallback `seq:{evt.Sequence}`; IBCallbackRouter.cs (SequenceNumber: 0) | 2026-11-26 | 2026-11-26 | open | Probe on 5fd5e185: 2nd OrderFlow and 2nd L2Snapshot with seq 0 -> Duplicate. Suppressed before WAL append, so lost permanently; key reloads after restart. Dedup is always on in PipelineFeatureRegistration. |
| data-pipeline-007 | Dual-path hot path rebuilds live Trade/BboQuote events without provenance | DualPathEventPipeline.cs TryRouteTrade/ReconstituteTrade/Quote; RawTradeEvent | 2026-11-26 | 2026-11-26 | open | Source becomes MarketDataSources.Unknown (since 875dec3f), and Venue, StreamId, RawConditions, CanonicalSymbol/conditions/venue and receive stamps are dropped; JsonlStoragePolicy partitions by Source; dedup prefix becomes UNKNOWN:sym. |
| data-pipeline-008 | Trade dedup identity omits provider sequence/trade id and is not normalized | PersistentDedupLedger.cs WriteTradeIdentity/WriteQuoteIdentity | 2026-11-26 | 2026-11-26 | open | Probe: distinct trades seq 1001/1002 with same ts/px/size -> 2nd Duplicate (data loss). 500.1 vs 500.10 and the same instant at different offsets -> both Reserved (replays not deduped). |
| data-pipeline-009 | No per-type/per-source dedup suppression metric; dedup cache has no hard cap | EventPipeline._deduplicatedCount; PipelineDiagnosticsProjection; PersistentDedupLedger EvictExpiredBackground | 2026-11-26 | 2026-11-26 | open | Only an aggregate Deduplicated count; nothing in PrometheusMetrics; maxInMemoryEntries only triggers TTL eviction. Silent loss like 006 cannot be seen. |
| data-pipeline-010 | ETL partner CSV naive timestamps assumed UTC; record index used as sequence | DataIntegration/Etl/EtlNormalizationService.cs NormalizeAsync | 2026-11-26 | 2026-11-26 | open (inference) | DateTimeStyles.AssumeUniversal with no declared source time zone; an exchange-local file shifts by 4-5h silently. |

## Run log

- 2026-11-25 (Wed) | main 7138e532 | WAL and atomic writes | Examined: repair CLI vs live segment, two writers on one WAL dir, dir-fsync failure, repair evidence loss, sequence reuse after corruption, crash between archive and delete, torn tail under BatchedSync, disk-full flush | First run. Opened 001-005. 197 WAL/AtomicFileWriter tests pass.
- 2026-11-25 (Wed, follow-up) | PR 3771 head 55c14052 | Fix pass for 001-005 | PR #3771 opened (unmerged).
- 2026-11-26 (Thu) | main 5fd5e185 | Dedup, normalization, alignment, accounting storage | Examined: unsequenced event dedup keys (OrderFlow/L2/Integrity/options), restart reload of suppressing keys, hot-path reconstruction metadata loss, identical-print trade collisions, decimal-scale/offset key drift, fill redelivery beyond 4096 remembered ids (safe: deterministic journal id + PostAndConfirm read-back), dedup ledger torn tail, ETL timestamp zone | #3743 resolved 001-003; 004/005 remain (PR #3771 open). Opened 006-010. Verified 006/008 with a scratch console probe against Meridian.Application (no repo changes). Accounting fill posting found sound.
