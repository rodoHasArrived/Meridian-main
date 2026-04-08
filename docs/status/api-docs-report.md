# API Documentation Validation Report

> Auto-generated API documentation validation. Do not edit manually.
> Generated: 2026-04-08 03:32:19 UTC

## Summary

| Metric | Value |
|--------|-------|
| Total Endpoints | 50 |
| Documented | 3 |
| Undocumented | 47 |
| Deprecated Docs | 140 |
| **Coverage** | **6.0%** 🔴 Poor |

## Undocumented Endpoints

These endpoints exist in the code but are not documented:

| Method | Path | Location |
|--------|------|----------|
| `GET` | `/api/backfill/checkpoints/{jobId}` | `src\Meridian.Ui.Shared\Endpoints\CheckpointEndpoints.cs:118` |
| `GET` | `/api/backfill/checkpoints/{jobId}/pending` | `src\Meridian.Ui.Shared\Endpoints\CheckpointEndpoints.cs:144` |
| `POST` | `/api/backfill/checkpoints/{jobId}/resume` | `src\Meridian.Ui.Shared\Endpoints\CheckpointEndpoints.cs:192` |
| `GET` | `/api/backfill/progress` | `src\Meridian.Ui.Shared\Endpoints\BackfillEndpoints.cs:122` |
| `GET` | `/api/config/data-sources` | `src\Meridian.Ui.Shared\Endpoints\ProviderEndpoints.cs:538` |
| `POST` | `/api/config/data-sources` | `src\Meridian.Ui.Shared\Endpoints\ProviderEndpoints.cs:554` |
| `POST` | `/api/dev/seed/bank-transactions` | `src\Meridian.Ui.Shared\Endpoints\BankingEndpoints.cs:147` |
| `GET` | `/api/events/stream` | `src\Meridian.Ui.Shared\Endpoints\StatusEndpoints.cs:206` |
| `GET` | `/api/health` | `src\Meridian.Ui.Shared\Endpoints\StatusEndpoints.cs:37` |
| `GET` | `/api/health/detailed` | `src\Meridian.Ui.Shared\Endpoints\StatusEndpoints.cs:182` |
| `GET` | `/api/ingestion/jobs/resumable` | `src\Meridian.Ui.Shared\Endpoints\IngestionJobEndpoints.cs:149` |
| `GET` | `/api/ingestion/summary` | `src\Meridian.Ui.Shared\Endpoints\IngestionJobEndpoints.cs:160` |
| `POST` | `/api/journals/{journalEntryId:guid}/post` | `src\Meridian.Ui.Shared\Endpoints\DirectLendingEndpoints.cs:531` |
| `GET` | `/api/loans/portfolio` | `src\Meridian.Ui.Shared\Endpoints\DirectLendingEndpoints.cs:667` |
| `POST` | `/api/loans/rebuild-all` | `src\Meridian.Ui.Shared\Endpoints\DirectLendingEndpoints.cs:677` |
| `GET` | `/api/loans/rebuild-checkpoints` | `src\Meridian.Ui.Shared\Endpoints\DirectLendingEndpoints.cs:659` |
| `POST` | `/api/maintenance/execute` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:133` |
| `GET` | `/api/maintenance/executions` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:176` |
| `POST` | `/api/maintenance/executions/cleanup` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:436` |
| `GET` | `/api/maintenance/executions/failed` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:224` |
| `POST` | `/api/maintenance/executions/{executionId}/cancel` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:157` |
| `GET` | `/api/maintenance/presets` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:355` |
| `GET` | `/api/maintenance/schedules/summary` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:241` |
| `PUT` | `/api/maintenance/schedules/{scheduleId}` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:36` |
| `GET` | `/api/maintenance/schedules/{scheduleId}/executions` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:208` |
| `GET` | `/api/maintenance/schedules/{scheduleId}/summary` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:254` |
| `POST` | `/api/maintenance/schedules/{scheduleId}/trigger` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:112` |
| `GET` | `/api/maintenance/statistics` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:270` |
| `GET` | `/api/maintenance/status` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:299` |
| `GET` | `/api/maintenance/task-types` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:413` |
| `POST` | `/api/maintenance/validate-cron` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:314` |
| `GET` | `/api/packaging/contents` | `src\Meridian.Application\Http\Endpoints\PackagingEndpoints.cs:155` |
| `POST` | `/api/packaging/create` | `src\Meridian.Application\Http\Endpoints\PackagingEndpoints.cs:31` |
| `GET` | `/api/packaging/download/{fileName}` | `src\Meridian.Application\Http\Endpoints\PackagingEndpoints.cs:280` |
| `POST` | `/api/packaging/import` | `src\Meridian.Application\Http\Endpoints\PackagingEndpoints.cs:74` |
| `GET` | `/api/packaging/list` | `src\Meridian.Application\Http\Endpoints\PackagingEndpoints.cs:192` |
| `POST` | `/api/packaging/validate` | `src\Meridian.Application\Http\Endpoints\PackagingEndpoints.cs:119` |
| `GET` | `/api/projections/{projectionRunId:guid}/flows` | `src\Meridian.Ui.Shared\Endpoints\DirectLendingEndpoints.cs:515` |
| `GET` | `/api/reconciliation/exceptions` | `src\Meridian.Ui.Shared\Endpoints\DirectLendingEndpoints.cs:585` |
| `POST` | `/api/reconciliation/exceptions/{exceptionId:guid}/resolve` | `src\Meridian.Ui.Shared\Endpoints\DirectLendingEndpoints.cs:593` |
| `GET` | `/api/reconciliation/{runId:guid}/results` | `src\Meridian.Ui.Shared\Endpoints\DirectLendingEndpoints.cs:577` |
| `POST` | `/api/servicer-reports` | `src\Meridian.Ui.Shared\Endpoints\DirectLendingEndpoints.cs:612` |
| `GET` | `/api/servicer-reports/{batchId:guid}` | `src\Meridian.Ui.Shared\Endpoints\DirectLendingEndpoints.cs:636` |
| `GET` | `/api/servicer-reports/{batchId:guid}/position-lines` | `src\Meridian.Ui.Shared\Endpoints\DirectLendingEndpoints.cs:643` |
| `GET` | `/api/servicer-reports/{batchId:guid}/transaction-lines` | `src\Meridian.Ui.Shared\Endpoints\DirectLendingEndpoints.cs:651` |
| `GET` | `/api/strategies/runs/compare` | `src\Meridian.Ui.Shared\Endpoints\WorkstationEndpoints.cs:574` |
| `GET` | `/api/strategies/{strategyId}/runs` | `src\Meridian.Ui.Shared\Endpoints\WorkstationEndpoints.cs:496` |

## Deprecated Documentation

These endpoints are documented but no longer exist in the code:

| Method | Path | Location |
|--------|------|----------|
| `LIST` | `/api/backfill/checkpoints` | `docs\reference\api-reference.md:335` |
| `LIST` | `/api/backfill/checkpoints/resumable` | `docs\reference\api-reference.md:336` |
| `CHECKPOINT` | `/api/backfill/checkpoints/{jobId}` | `docs\reference\api-reference.md:337` |
| `SYMBOLS` | `/api/backfill/checkpoints/{jobId}/pending` | `docs\reference\api-reference.md:338` |
| `RESUME` | `/api/backfill/checkpoints/{jobId}/resume` | `docs\reference\api-reference.md:339` |
| `CURRENT` | `/api/backfill/progress` | `docs\reference\api-reference.md:327` |
| `LIST` | `/api/backfill/providers` | `docs\reference\api-reference.md:323` |
| `EXECUTE` | `/api/backfill/run` | `docs\reference\api-reference.md:325` |
| `PREVIEW` | `/api/backfill/run/preview` | `docs\reference\api-reference.md:326` |
| `LAST` | `/api/backfill/status` | `docs\reference\api-reference.md:324` |
| `BACKPRESSURE` | `/api/backpressure` | `docs\reference\api-reference.md:475` |
| `FULL` | `/api/config` | `docs\reference\api-reference.md:304` |
| `UPDATE` | `/api/config/alpaca` | `docs\reference\api-reference.md:306` |
| `BACKWARD` | `/api/config/data-sources` | `docs\reference\api-reference.md:505` |
| `UPDATE` | `/api/config/datasource` | `docs\reference\api-reference.md:305` |
| `CREATE` | `/api/config/datasources` | `docs\reference\api-reference.md:313` |
| `LIST` | `/api/config/datasources` | `docs\reference\api-reference.md:312` |
| `SET` | `/api/config/datasources/defaults` | `docs\reference\api-reference.md:316` |
| `UPDATE` | `/api/config/datasources/failover` | `docs\reference\api-reference.md:317` |
| `DELETE` | `/api/config/datasources/{id}` | `docs\reference\api-reference.md:314` |
| `TOGGLE` | `/api/config/datasources/{id}/toggle` | `docs\reference\api-reference.md:315` |
| `GET` | `/api/config/derivatives` | `docs\reference\api-reference.md:310` |
| `UPDATE` | `/api/config/derivatives` | `docs\reference\api-reference.md:311` |
| `UPDATE` | `/api/config/storage` | `docs\reference\api-reference.md:307` |
| `ADD` | `/api/config/symbols` | `docs\reference\api-reference.md:308` |
| `REMOVE` | `/api/config/symbols/{symbol}` | `docs\reference\api-reference.md:309` |
| `CONNECTION` | `/api/connections` | `docs\reference\api-reference.md:352` |
| `BEST` | `/api/data/bbo/{symbol}` | `docs\reference\api-reference.md:391` |
| `LIVE` | `/api/data/health` | `docs\reference\api-reference.md:393` |
| `ORDER` | `/api/data/orderbook/{symbol}` | `docs\reference\api-reference.md:390` |
| `ORDER` | `/api/data/orderflow/{symbol}` | `docs\reference\api-reference.md:392` |
| `LATEST` | `/api/data/quotes/{symbol}` | `docs\reference\api-reference.md:389` |
| `RECENT` | `/api/data/trades/{symbol}` | `docs\reference\api-reference.md:388` |
| `ERROR` | `/api/errors` | `docs\reference\api-reference.md:474` |
| `SERVER` | `/api/events/stream` | `docs\reference\api-reference.md:476` |
| `FAILOVER` | `/api/failover/config` | `docs\reference\api-reference.md:358` |
| `UPDATE` | `/api/failover/config` | `docs\reference\api-reference.md:359` |
| `FORCE` | `/api/failover/force/{ruleId}` | `docs\reference\api-reference.md:363` |
| `PROVIDER` | `/api/failover/health` | `docs\reference\api-reference.md:364` |
| `ALL` | `/api/failover/rules` | `docs\reference\api-reference.md:360` |
| `CREATE` | `/api/failover/rules` | `docs\reference\api-reference.md:361` |
| `DELETE` | `/api/failover/rules/{id}` | `docs\reference\api-reference.md:362` |
| `DETAILED` | `/api/health/detailed` | `docs\reference\api-reference.md:473` |
| `QUERY` | `/api/historical` | `docs\reference\api-reference.md:399` |
| `LIST` | `/api/historical/symbols` | `docs\reference\api-reference.md:400` |
| `DATE` | `/api/historical/{symbol}/daterange` | `docs\reference\api-reference.md:401` |
| `CREATE` | `/api/ingestion/jobs` | `docs\reference\api-reference.md:408` |
| `LIST` | `/api/ingestion/jobs` | `docs\reference\api-reference.md:407` |
| `LIST` | `/api/ingestion/jobs/resumable` | `docs\reference\api-reference.md:412` |
| `DELETE` | `/api/ingestion/jobs/{jobId}` | `docs\reference\api-reference.md:410` |
| `GET` | `/api/ingestion/jobs/{jobId}` | `docs\reference\api-reference.md:409` |
| `TRANSITION` | `/api/ingestion/jobs/{jobId}/transition` | `docs\reference\api-reference.md:411` |
| `SUMMARY` | `/api/ingestion/summary` | `docs\reference\api-reference.md:413` |
| `RUN` | `/api/maintenance/execute` | `docs\reference\api-reference.md:442` |
| `LIST` | `/api/maintenance/executions` | `docs\reference\api-reference.md:444` |
| `CLEAN` | `/api/maintenance/executions/cleanup` | `docs\reference\api-reference.md:447` |
| `LIST` | `/api/maintenance/executions/failed` | `docs\reference\api-reference.md:446` |
| `CANCEL` | `/api/maintenance/executions/{executionId}/cancel` | `docs\reference\api-reference.md:443` |
| `LIST` | `/api/maintenance/presets` | `docs\reference\api-reference.md:451` |
| `CREATE` | `/api/maintenance/schedules` | `docs\reference\api-reference.md:432` |
| `LIST` | `/api/maintenance/schedules` | `docs\reference\api-reference.md:431` |
| `SUMMARY` | `/api/maintenance/schedules/summary` | `docs\reference\api-reference.md:441` |
| `GET` | `/api/maintenance/schedules/{scheduleId}` | `docs\reference\api-reference.md:433` |
| `UPDATE` | `/api/maintenance/schedules/{scheduleId}` | `docs\reference\api-reference.md:434` |
| `DISABLE` | `/api/maintenance/schedules/{scheduleId}/disable` | `docs\reference\api-reference.md:437` |
| `ENABLE` | `/api/maintenance/schedules/{scheduleId}/enable` | `docs\reference\api-reference.md:436` |
| `EXECUTION` | `/api/maintenance/schedules/{scheduleId}/executions` | `docs\reference\api-reference.md:439` |
| `SUMMARY` | `/api/maintenance/schedules/{scheduleId}/summary` | `docs\reference\api-reference.md:440` |
| `TRIGGER` | `/api/maintenance/schedules/{scheduleId}/trigger` | `docs\reference\api-reference.md:438` |
| `OVERALL` | `/api/maintenance/statistics` | `docs\reference\api-reference.md:448` |
| `CURRENT` | `/api/maintenance/status` | `docs\reference\api-reference.md:449` |
| `LIST` | `/api/maintenance/task-types` | `docs\reference\api-reference.md:452` |
| `VALIDATE` | `/api/maintenance/validate-cron` | `docs\reference\api-reference.md:450` |
| `LIST` | `/api/packaging/contents` | `docs\reference\api-reference.md:423` |
| `CREATE` | `/api/packaging/create` | `docs\reference\api-reference.md:419` |
| `DOWNLOAD` | `/api/packaging/download/{fileName}` | `docs\reference\api-reference.md:424` |
| `IMPORT` | `/api/packaging/import` | `docs\reference\api-reference.md:420` |
| `LIST` | `/api/packaging/list` | `docs\reference\api-reference.md:422` |
| `VALIDATE` | `/api/packaging/validate` | `docs\reference\api-reference.md:421` |
| `PROVIDER` | `/api/providers/catalog` | `docs\reference\api-reference.md:349` |
| `SINGLE` | `/api/providers/catalog/{providerId}` | `docs\reference\api-reference.md:350` |
| `FEATURE` | `/api/providers/comparison` | `docs\reference\api-reference.md:348` |
| `IB` | `/api/providers/ib/error-codes` | `docs\reference\api-reference.md:371` |
| `IB` | `/api/providers/ib/limits` | `docs\reference\api-reference.md:372` |
| `IB` | `/api/providers/ib/status` | `docs\reference\api-reference.md:370` |
| `LATENCY` | `/api/providers/latency` | `docs\reference\api-reference.md:351` |
| `PROVIDER` | `/api/providers/metrics` | `docs\reference\api-reference.md:346` |
| `SINGLE` | `/api/providers/metrics/{providerId}` | `docs\reference\api-reference.md:347` |
| `ALL` | `/api/providers/status` | `docs\reference\api-reference.md:345` |
| `DROPPED` | `/api/quality/drops` | `docs\reference\api-reference.md:458` |
| `DROPS` | `/api/quality/drops/{symbol}` | `docs\reference\api-reference.md:459` |
| `FULL` | `/api/status` | `docs\reference\api-reference.md:472` |
| `ARCHIVE` | `/api/storage/archive/stats` | `docs\reference\api-reference.md:278` |
| `BREAKDOWN` | `/api/storage/breakdown` | `docs\reference\api-reference.md:268` |
| `FULL` | `/api/storage/catalog` | `docs\reference\api-reference.md:270` |
| `RUN` | `/api/storage/cleanup` | `docs\reference\api-reference.md:277` |
| `FILES` | `/api/storage/cleanup/candidates` | `docs\reference\api-reference.md:276` |
| `STORAGE` | `/api/storage/health` | `docs\reference\api-reference.md:269` |
| `DETAILED` | `/api/storage/health/check` | `docs\reference\api-reference.md:279` |
| `FIND` | `/api/storage/health/orphans` | `docs\reference\api-reference.md:280` |
| `RUN` | `/api/storage/maintenance/defrag` | `docs\reference\api-reference.md:284` |
| `AVAILABLE` | `/api/storage/profiles` | `docs\reference\api-reference.md:266` |
| `ACTIVE` | `/api/storage/quality/alerts` | `docs\reference\api-reference.md:293` |
| `ACKNOWLEDGE` | `/api/storage/quality/alerts/{alertId}/acknowledge` | `docs\reference\api-reference.md:294` |
| `DETECTED` | `/api/storage/quality/anomalies` | `docs\reference\api-reference.md:297` |
| `RUN` | `/api/storage/quality/check` | `docs\reference\api-reference.md:298` |
| `SOURCE` | `/api/storage/quality/rankings/{symbol}` | `docs\reference\api-reference.md:295` |
| `QUALITY` | `/api/storage/quality/scores` | `docs\reference\api-reference.md:291` |
| `OVERALL` | `/api/storage/quality/summary` | `docs\reference\api-reference.md:290` |
| `QUALITY` | `/api/storage/quality/symbol/{symbol}` | `docs\reference\api-reference.md:292` |
| `QUALITY` | `/api/storage/quality/trends?days=` | `docs\reference\api-reference.md:296` |
| `SEARCH` | `/api/storage/search/files?symbol=&q=` | `docs\reference\api-reference.md:271` |
| `OVERALL` | `/api/storage/stats` | `docs\reference\api-reference.md:267` |
| `LIST` | `/api/storage/symbol/{symbol}/files` | `docs\reference\api-reference.md:274` |
| `STORAGE` | `/api/storage/symbol/{symbol}/info` | `docs\reference\api-reference.md:272` |
| `STORAGE` | `/api/storage/symbol/{symbol}/path` | `docs\reference\api-reference.md:275` |
| `DETAILED` | `/api/storage/symbol/{symbol}/stats` | `docs\reference\api-reference.md:273` |
| `EXECUTE` | `/api/storage/tiers/migrate` | `docs\reference\api-reference.md:283` |
| `MIGRATION` | `/api/storage/tiers/plan?days=` | `docs\reference\api-reference.md:282` |
| `TIER` | `/api/storage/tiers/statistics` | `docs\reference\api-reference.md:281` |
| `ALL` | `/api/symbols` | `docs\reference\api-reference.md:246` |
| `ADD` | `/api/symbols/add` | `docs\reference\api-reference.md:250` |
| `SYMBOLS` | `/api/symbols/archived` | `docs\reference\api-reference.md:248` |
| `BATCH` | `/api/symbols/batch` | `docs\reference\api-reference.md:260` |
| `ADD` | `/api/symbols/bulk-add` | `docs\reference\api-reference.md:253` |
| `REMOVE` | `/api/symbols/bulk-remove` | `docs\reference\api-reference.md:254` |
| `ALL` | `/api/symbols/mappings` | `docs\reference\api-reference.md:378` |
| `CREATE` | `/api/symbols/mappings` | `docs\reference\api-reference.md:379` |
| `IMPORT` | `/api/symbols/mappings/import` | `docs\reference\api-reference.md:382` |
| `DELETE` | `/api/symbols/mappings/{symbol}` | `docs\reference\api-reference.md:381` |
| `SINGLE` | `/api/symbols/mappings/{symbol}` | `docs\reference\api-reference.md:380` |
| `SYMBOLS` | `/api/symbols/monitored` | `docs\reference\api-reference.md:247` |
| `SEARCH` | `/api/symbols/search?q=` | `docs\reference\api-reference.md:255` |
| `AGGREGATE` | `/api/symbols/statistics` | `docs\reference\api-reference.md:257` |
| `VALIDATE` | `/api/symbols/validate` | `docs\reference\api-reference.md:256` |
| `ARCHIVE` | `/api/symbols/{symbol}/archive` | `docs\reference\api-reference.md:252` |
| `RECENT` | `/api/symbols/{symbol}/depth` | `docs\reference\api-reference.md:259` |
| `REMOVE` | `/api/symbols/{symbol}/remove` | `docs\reference\api-reference.md:251` |
| `DETAILED` | `/api/symbols/{symbol}/status` | `docs\reference\api-reference.md:249` |
| `RECENT` | `/api/symbols/{symbol}/trades` | `docs\reference\api-reference.md:258` |

## Recommendations

1. **Document 47 missing endpoints**: Add entries to `docs/reference/api-reference.md` with descriptions, parameters, and response formats.

2. **Remove 140 deprecated entries**: Clean up documentation for endpoints that no longer exist.

---

*This report is auto-generated. Run `python3 build/scripts/docs/validate-api-docs.py` to regenerate.*
