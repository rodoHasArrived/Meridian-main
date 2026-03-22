# API Documentation Validation Report

> Auto-generated API documentation validation. Do not edit manually.
> Generated: 2026-03-22 03:10:52 UTC

## Summary

| Metric | Value |
|--------|-------|
| Total Endpoints | 40 |
| Documented | 4 |
| Undocumented | 36 |
| Deprecated Docs | 138 |
| **Coverage** | **10.0%** 🔴 Poor |

## Undocumented Endpoints

These endpoints exist in the code but are not documented:

| Method | Path | Location |
|--------|------|----------|
| `GET` | `/api/backfill/checkpoints/{jobId}` | `src\Meridian.Ui.Shared\Endpoints\CheckpointEndpoints.cs:63` |
| `GET` | `/api/backfill/checkpoints/{jobId}/pending` | `src\Meridian.Ui.Shared\Endpoints\CheckpointEndpoints.cs:89` |
| `POST` | `/api/backfill/checkpoints/{jobId}/resume` | `src\Meridian.Ui.Shared\Endpoints\CheckpointEndpoints.cs:117` |
| `GET` | `/api/backfill/progress` | `src\Meridian.Ui.Shared\Endpoints\BackfillEndpoints.cs:118` |
| `GET` | `/api/config/data-sources` | `src\Meridian.Ui.Shared\Endpoints\ProviderEndpoints.cs:394` |
| `POST` | `/api/config/data-sources` | `src\Meridian.Ui.Shared\Endpoints\ProviderEndpoints.cs:410` |
| `GET` | `/api/events/stream` | `src\Meridian.Ui.Shared\Endpoints\StatusEndpoints.cs:206` |
| `GET` | `/api/health` | `src\Meridian.Ui.Shared\Endpoints\StatusEndpoints.cs:37` |
| `GET` | `/api/health/detailed` | `src\Meridian.Ui.Shared\Endpoints\StatusEndpoints.cs:182` |
| `GET` | `/api/ingestion/jobs/resumable` | `src\Meridian.Ui.Shared\Endpoints\IngestionJobEndpoints.cs:149` |
| `GET` | `/api/ingestion/summary` | `src\Meridian.Ui.Shared\Endpoints\IngestionJobEndpoints.cs:160` |
| `POST` | `/api/maintenance/execute` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:243` |
| `GET` | `/api/maintenance/executions` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:286` |
| `POST` | `/api/maintenance/executions/cleanup` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:546` |
| `GET` | `/api/maintenance/executions/failed` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:334` |
| `POST` | `/api/maintenance/executions/{executionId}/cancel` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:267` |
| `GET` | `/api/maintenance/presets` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:465` |
| `GET` | `/api/maintenance/schedules` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:29` |
| `POST` | `/api/maintenance/schedules` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:59` |
| `GET` | `/api/maintenance/schedules/summary` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:351` |
| `PUT` | `/api/maintenance/schedules/{scheduleId}` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:114` |
| `POST` | `/api/maintenance/schedules/{scheduleId}/disable` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:205` |
| `POST` | `/api/maintenance/schedules/{scheduleId}/enable` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:188` |
| `GET` | `/api/maintenance/schedules/{scheduleId}/executions` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:318` |
| `GET` | `/api/maintenance/schedules/{scheduleId}/summary` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:364` |
| `POST` | `/api/maintenance/schedules/{scheduleId}/trigger` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:222` |
| `GET` | `/api/maintenance/statistics` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:380` |
| `GET` | `/api/maintenance/status` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:409` |
| `GET` | `/api/maintenance/task-types` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:523` |
| `POST` | `/api/maintenance/validate-cron` | `src\Meridian.Application\Http\Endpoints\ArchiveMaintenanceEndpoints.cs:424` |
| `GET` | `/api/packaging/contents` | `src\Meridian.Application\Http\Endpoints\PackagingEndpoints.cs:155` |
| `POST` | `/api/packaging/create` | `src\Meridian.Application\Http\Endpoints\PackagingEndpoints.cs:31` |
| `GET` | `/api/packaging/download/{fileName}` | `src\Meridian.Application\Http\Endpoints\PackagingEndpoints.cs:280` |
| `POST` | `/api/packaging/import` | `src\Meridian.Application\Http\Endpoints\PackagingEndpoints.cs:74` |
| `GET` | `/api/packaging/list` | `src\Meridian.Application\Http\Endpoints\PackagingEndpoints.cs:192` |
| `POST` | `/api/packaging/validate` | `src\Meridian.Application\Http\Endpoints\PackagingEndpoints.cs:119` |

## Deprecated Documentation

These endpoints are documented but no longer exist in the code:

| Method | Path | Location |
|--------|------|----------|
| `LIST` | `/api/backfill/checkpoints` | `docs\reference\api-reference.md:331` |
| `LIST` | `/api/backfill/checkpoints/resumable` | `docs\reference\api-reference.md:332` |
| `CHECKPOINT` | `/api/backfill/checkpoints/{jobId}` | `docs\reference\api-reference.md:333` |
| `SYMBOLS` | `/api/backfill/checkpoints/{jobId}/pending` | `docs\reference\api-reference.md:334` |
| `RESUME` | `/api/backfill/checkpoints/{jobId}/resume` | `docs\reference\api-reference.md:335` |
| `CURRENT` | `/api/backfill/progress` | `docs\reference\api-reference.md:323` |
| `LIST` | `/api/backfill/providers` | `docs\reference\api-reference.md:319` |
| `EXECUTE` | `/api/backfill/run` | `docs\reference\api-reference.md:321` |
| `PREVIEW` | `/api/backfill/run/preview` | `docs\reference\api-reference.md:322` |
| `LAST` | `/api/backfill/status` | `docs\reference\api-reference.md:320` |
| `BACKPRESSURE` | `/api/backpressure` | `docs\reference\api-reference.md:471` |
| `FULL` | `/api/config` | `docs\reference\api-reference.md:300` |
| `UPDATE` | `/api/config/alpaca` | `docs\reference\api-reference.md:302` |
| `UPDATE` | `/api/config/datasource` | `docs\reference\api-reference.md:301` |
| `CREATE` | `/api/config/datasources` | `docs\reference\api-reference.md:309` |
| `LIST` | `/api/config/datasources` | `docs\reference\api-reference.md:308` |
| `SET` | `/api/config/datasources/defaults` | `docs\reference\api-reference.md:312` |
| `UPDATE` | `/api/config/datasources/failover` | `docs\reference\api-reference.md:313` |
| `DELETE` | `/api/config/datasources/{id}` | `docs\reference\api-reference.md:310` |
| `TOGGLE` | `/api/config/datasources/{id}/toggle` | `docs\reference\api-reference.md:311` |
| `GET` | `/api/config/derivatives` | `docs\reference\api-reference.md:306` |
| `UPDATE` | `/api/config/derivatives` | `docs\reference\api-reference.md:307` |
| `UPDATE` | `/api/config/storage` | `docs\reference\api-reference.md:303` |
| `ADD` | `/api/config/symbols` | `docs\reference\api-reference.md:304` |
| `REMOVE` | `/api/config/symbols/{symbol}` | `docs\reference\api-reference.md:305` |
| `CONNECTION` | `/api/connections` | `docs\reference\api-reference.md:348` |
| `BEST` | `/api/data/bbo/{symbol}` | `docs\reference\api-reference.md:387` |
| `LIVE` | `/api/data/health` | `docs\reference\api-reference.md:389` |
| `ORDER` | `/api/data/orderbook/{symbol}` | `docs\reference\api-reference.md:386` |
| `ORDER` | `/api/data/orderflow/{symbol}` | `docs\reference\api-reference.md:388` |
| `LATEST` | `/api/data/quotes/{symbol}` | `docs\reference\api-reference.md:385` |
| `RECENT` | `/api/data/trades/{symbol}` | `docs\reference\api-reference.md:384` |
| `ERROR` | `/api/errors` | `docs\reference\api-reference.md:470` |
| `SERVER` | `/api/events/stream` | `docs\reference\api-reference.md:472` |
| `FAILOVER` | `/api/failover/config` | `docs\reference\api-reference.md:354` |
| `UPDATE` | `/api/failover/config` | `docs\reference\api-reference.md:355` |
| `FORCE` | `/api/failover/force/{ruleId}` | `docs\reference\api-reference.md:359` |
| `PROVIDER` | `/api/failover/health` | `docs\reference\api-reference.md:360` |
| `ALL` | `/api/failover/rules` | `docs\reference\api-reference.md:356` |
| `CREATE` | `/api/failover/rules` | `docs\reference\api-reference.md:357` |
| `DELETE` | `/api/failover/rules/{id}` | `docs\reference\api-reference.md:358` |
| `DETAILED` | `/api/health/detailed` | `docs\reference\api-reference.md:469` |
| `QUERY` | `/api/historical` | `docs\reference\api-reference.md:395` |
| `LIST` | `/api/historical/symbols` | `docs\reference\api-reference.md:396` |
| `DATE` | `/api/historical/{symbol}/daterange` | `docs\reference\api-reference.md:397` |
| `CREATE` | `/api/ingestion/jobs` | `docs\reference\api-reference.md:404` |
| `LIST` | `/api/ingestion/jobs` | `docs\reference\api-reference.md:403` |
| `LIST` | `/api/ingestion/jobs/resumable` | `docs\reference\api-reference.md:408` |
| `DELETE` | `/api/ingestion/jobs/{jobId}` | `docs\reference\api-reference.md:406` |
| `GET` | `/api/ingestion/jobs/{jobId}` | `docs\reference\api-reference.md:405` |
| `TRANSITION` | `/api/ingestion/jobs/{jobId}/transition` | `docs\reference\api-reference.md:407` |
| `SUMMARY` | `/api/ingestion/summary` | `docs\reference\api-reference.md:409` |
| `RUN` | `/api/maintenance/execute` | `docs\reference\api-reference.md:438` |
| `LIST` | `/api/maintenance/executions` | `docs\reference\api-reference.md:440` |
| `CLEAN` | `/api/maintenance/executions/cleanup` | `docs\reference\api-reference.md:443` |
| `LIST` | `/api/maintenance/executions/failed` | `docs\reference\api-reference.md:442` |
| `CANCEL` | `/api/maintenance/executions/{executionId}/cancel` | `docs\reference\api-reference.md:439` |
| `LIST` | `/api/maintenance/presets` | `docs\reference\api-reference.md:447` |
| `CREATE` | `/api/maintenance/schedules` | `docs\reference\api-reference.md:428` |
| `LIST` | `/api/maintenance/schedules` | `docs\reference\api-reference.md:427` |
| `SUMMARY` | `/api/maintenance/schedules/summary` | `docs\reference\api-reference.md:437` |
| `UPDATE` | `/api/maintenance/schedules/{scheduleId}` | `docs\reference\api-reference.md:430` |
| `DISABLE` | `/api/maintenance/schedules/{scheduleId}/disable` | `docs\reference\api-reference.md:433` |
| `ENABLE` | `/api/maintenance/schedules/{scheduleId}/enable` | `docs\reference\api-reference.md:432` |
| `EXECUTION` | `/api/maintenance/schedules/{scheduleId}/executions` | `docs\reference\api-reference.md:435` |
| `SUMMARY` | `/api/maintenance/schedules/{scheduleId}/summary` | `docs\reference\api-reference.md:436` |
| `TRIGGER` | `/api/maintenance/schedules/{scheduleId}/trigger` | `docs\reference\api-reference.md:434` |
| `OVERALL` | `/api/maintenance/statistics` | `docs\reference\api-reference.md:444` |
| `CURRENT` | `/api/maintenance/status` | `docs\reference\api-reference.md:445` |
| `LIST` | `/api/maintenance/task-types` | `docs\reference\api-reference.md:448` |
| `VALIDATE` | `/api/maintenance/validate-cron` | `docs\reference\api-reference.md:446` |
| `LIST` | `/api/packaging/contents` | `docs\reference\api-reference.md:419` |
| `CREATE` | `/api/packaging/create` | `docs\reference\api-reference.md:415` |
| `DOWNLOAD` | `/api/packaging/download/{fileName}` | `docs\reference\api-reference.md:420` |
| `IMPORT` | `/api/packaging/import` | `docs\reference\api-reference.md:416` |
| `LIST` | `/api/packaging/list` | `docs\reference\api-reference.md:418` |
| `VALIDATE` | `/api/packaging/validate` | `docs\reference\api-reference.md:417` |
| `PROVIDER` | `/api/providers/catalog` | `docs\reference\api-reference.md:345` |
| `SINGLE` | `/api/providers/catalog/{providerId}` | `docs\reference\api-reference.md:346` |
| `FEATURE` | `/api/providers/comparison` | `docs\reference\api-reference.md:344` |
| `IB` | `/api/providers/ib/error-codes` | `docs\reference\api-reference.md:367` |
| `IB` | `/api/providers/ib/limits` | `docs\reference\api-reference.md:368` |
| `IB` | `/api/providers/ib/status` | `docs\reference\api-reference.md:366` |
| `LATENCY` | `/api/providers/latency` | `docs\reference\api-reference.md:347` |
| `PROVIDER` | `/api/providers/metrics` | `docs\reference\api-reference.md:342` |
| `SINGLE` | `/api/providers/metrics/{providerId}` | `docs\reference\api-reference.md:343` |
| `ALL` | `/api/providers/status` | `docs\reference\api-reference.md:341` |
| `DROPPED` | `/api/quality/drops` | `docs\reference\api-reference.md:454` |
| `DROPS` | `/api/quality/drops/{symbol}` | `docs\reference\api-reference.md:455` |
| `FULL` | `/api/status` | `docs\reference\api-reference.md:468` |
| `ARCHIVE` | `/api/storage/archive/stats` | `docs\reference\api-reference.md:274` |
| `BREAKDOWN` | `/api/storage/breakdown` | `docs\reference\api-reference.md:264` |
| `FULL` | `/api/storage/catalog` | `docs\reference\api-reference.md:266` |
| `RUN` | `/api/storage/cleanup` | `docs\reference\api-reference.md:273` |
| `FILES` | `/api/storage/cleanup/candidates` | `docs\reference\api-reference.md:272` |
| `STORAGE` | `/api/storage/health` | `docs\reference\api-reference.md:265` |
| `DETAILED` | `/api/storage/health/check` | `docs\reference\api-reference.md:275` |
| `FIND` | `/api/storage/health/orphans` | `docs\reference\api-reference.md:276` |
| `RUN` | `/api/storage/maintenance/defrag` | `docs\reference\api-reference.md:280` |
| `AVAILABLE` | `/api/storage/profiles` | `docs\reference\api-reference.md:262` |
| `ACTIVE` | `/api/storage/quality/alerts` | `docs\reference\api-reference.md:289` |
| `ACKNOWLEDGE` | `/api/storage/quality/alerts/{alertId}/acknowledge` | `docs\reference\api-reference.md:290` |
| `DETECTED` | `/api/storage/quality/anomalies` | `docs\reference\api-reference.md:293` |
| `RUN` | `/api/storage/quality/check` | `docs\reference\api-reference.md:294` |
| `SOURCE` | `/api/storage/quality/rankings/{symbol}` | `docs\reference\api-reference.md:291` |
| `QUALITY` | `/api/storage/quality/scores` | `docs\reference\api-reference.md:287` |
| `OVERALL` | `/api/storage/quality/summary` | `docs\reference\api-reference.md:286` |
| `QUALITY` | `/api/storage/quality/symbol/{symbol}` | `docs\reference\api-reference.md:288` |
| `QUALITY` | `/api/storage/quality/trends?days=` | `docs\reference\api-reference.md:292` |
| `SEARCH` | `/api/storage/search/files?symbol=&q=` | `docs\reference\api-reference.md:267` |
| `OVERALL` | `/api/storage/stats` | `docs\reference\api-reference.md:263` |
| `LIST` | `/api/storage/symbol/{symbol}/files` | `docs\reference\api-reference.md:270` |
| `STORAGE` | `/api/storage/symbol/{symbol}/info` | `docs\reference\api-reference.md:268` |
| `STORAGE` | `/api/storage/symbol/{symbol}/path` | `docs\reference\api-reference.md:271` |
| `DETAILED` | `/api/storage/symbol/{symbol}/stats` | `docs\reference\api-reference.md:269` |
| `EXECUTE` | `/api/storage/tiers/migrate` | `docs\reference\api-reference.md:279` |
| `MIGRATION` | `/api/storage/tiers/plan?days=` | `docs\reference\api-reference.md:278` |
| `TIER` | `/api/storage/tiers/statistics` | `docs\reference\api-reference.md:277` |
| `ALL` | `/api/symbols` | `docs\reference\api-reference.md:242` |
| `ADD` | `/api/symbols/add` | `docs\reference\api-reference.md:246` |
| `SYMBOLS` | `/api/symbols/archived` | `docs\reference\api-reference.md:244` |
| `BATCH` | `/api/symbols/batch` | `docs\reference\api-reference.md:256` |
| `ADD` | `/api/symbols/bulk-add` | `docs\reference\api-reference.md:249` |
| `REMOVE` | `/api/symbols/bulk-remove` | `docs\reference\api-reference.md:250` |
| `ALL` | `/api/symbols/mappings` | `docs\reference\api-reference.md:374` |
| `CREATE` | `/api/symbols/mappings` | `docs\reference\api-reference.md:375` |
| `IMPORT` | `/api/symbols/mappings/import` | `docs\reference\api-reference.md:378` |
| `DELETE` | `/api/symbols/mappings/{symbol}` | `docs\reference\api-reference.md:377` |
| `SINGLE` | `/api/symbols/mappings/{symbol}` | `docs\reference\api-reference.md:376` |
| `SYMBOLS` | `/api/symbols/monitored` | `docs\reference\api-reference.md:243` |
| `SEARCH` | `/api/symbols/search?q=` | `docs\reference\api-reference.md:251` |
| `AGGREGATE` | `/api/symbols/statistics` | `docs\reference\api-reference.md:253` |
| `VALIDATE` | `/api/symbols/validate` | `docs\reference\api-reference.md:252` |
| `ARCHIVE` | `/api/symbols/{symbol}/archive` | `docs\reference\api-reference.md:248` |
| `RECENT` | `/api/symbols/{symbol}/depth` | `docs\reference\api-reference.md:255` |
| `REMOVE` | `/api/symbols/{symbol}/remove` | `docs\reference\api-reference.md:247` |
| `DETAILED` | `/api/symbols/{symbol}/status` | `docs\reference\api-reference.md:245` |
| `RECENT` | `/api/symbols/{symbol}/trades` | `docs\reference\api-reference.md:254` |

## Recommendations

1. **Document 36 missing endpoints**: Add entries to `docs/reference/api-reference.md` with descriptions, parameters, and response formats.

2. **Remove 138 deprecated entries**: Clean up documentation for endpoints that no longer exist.

---

*This report is auto-generated. Run `python3 build/scripts/docs/validate-api-docs.py` to regenerate.*
