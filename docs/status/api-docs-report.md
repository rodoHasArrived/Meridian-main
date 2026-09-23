# API Documentation Validation Report

> Auto-generated API documentation validation. Do not edit manually.
> Generated: 2026-09-23 20:13:02 UTC

## Summary

| Metric | Value |
|--------|-------|
| Total Endpoints | 63 |
| Documented | 14 |
| Undocumented | 49 |
| Deprecated Docs | 218 |
| **Coverage** | **22.2%** 🔴 Poor |

## Undocumented Endpoints

These endpoints exist in the code but are not documented:

| Method | Path | Location |
|--------|------|----------|
| `POST` | `/api/auth/bootstrap` | `src\Meridian.Ui.Shared\Endpoints\InitialAccountBootstrapEndpoints.cs:19` |
| `GET` | `/api/compliance/access-reviews` | `src\Meridian.Ui.Shared\Endpoints\Compliance\ComplianceEndpoints.cs:123` |
| `POST` | `/api/compliance/access-reviews/assess` | `src\Meridian.Ui.Shared\Endpoints\Compliance\ComplianceEndpoints.cs:85` |
| `POST` | `/api/compliance/access-reviews/run` | `src\Meridian.Ui.Shared\Endpoints\Compliance\ComplianceEndpoints.cs:101` |
| `POST` | `/api/compliance/actions/evaluate` | `src\Meridian.Ui.Shared\Endpoints\Compliance\ComplianceEndpoints.cs:48` |
| `POST` | `/api/compliance/approval-requests` | `src\Meridian.Ui.Shared\Endpoints\Compliance\ComplianceEndpoints.cs:15` |
| `POST` | `/api/compliance/approval-requests/{approvalRequestId}/decisions` | `src\Meridian.Ui.Shared\Endpoints\Compliance\ComplianceEndpoints.cs:26` |
| `GET` | `/api/compliance/audit/extract` | `src\Meridian.Ui.Shared\Endpoints\Compliance\ComplianceEndpoints.cs:66` |
| `GET` | `/api/compliance/controls/attestation` | `src\Meridian.Ui.Shared\Endpoints\Compliance\ComplianceEndpoints.cs:70` |
| `GET` | `/api/config/data-sources` | `src\Meridian.Ui.Shared\Endpoints\ProviderEndpoints.cs:635` |
| `POST` | `/api/config/data-sources` | `src\Meridian.Ui.Shared\Endpoints\ProviderEndpoints.cs:653` |
| `POST` | `/api/dev/seed/bank-transactions` | `src\Meridian.Ui.Shared\Endpoints\BankingEndpoints.cs:308` |
| `GET` | `/api/events/stream` | `src\Meridian.Ui.Shared\Endpoints\StatusEndpoints.cs:240` |
| `GET` | `/api/fund-accounts/brokerage-sync/accounts` | `src\Meridian.Ui.Shared\Endpoints\FundAccountEndpoints.cs:250` |
| `GET` | `/api/fund-accounts/{accountId:guid}/brokerage-sync/activity` | `src\Meridian.Ui.Shared\Endpoints\FundAccountEndpoints.cs:369` |
| `GET` | `/api/fund-accounts/{accountId:guid}/brokerage-sync/positions` | `src\Meridian.Ui.Shared\Endpoints\FundAccountEndpoints.cs:353` |
| `GET` | `/api/fund-accounts/{accountId:guid}/brokerage-sync/reconciliation/latest` | `src\Meridian.Ui.Shared\Endpoints\FundAccountEndpoints.cs:433` |
| `GET` | `/api/fund-accounts/{accountId:guid}/brokerage-sync/status` | `src\Meridian.Ui.Shared\Endpoints\FundAccountEndpoints.cs:294` |
| `GET` | `/api/funds/{fundId:guid}/accounts` | `src\Meridian.Ui.Shared\Endpoints\FundAccountEndpoints.cs:134` |
| `GET` | `/api/health` | `src\Meridian.Ui.Shared\Endpoints\StatusEndpoints.cs:42` |
| `GET` | `/api/health/detailed` | `src\Meridian.Ui.Shared\Endpoints\StatusEndpoints.cs:212` |
| `GET` | `/api/ingestion/jobs/resumable` | `src\Meridian.Ui.Shared\Endpoints\IngestionJobEndpoints.cs:160` |
| `GET` | `/api/ingestion/summary` | `src\Meridian.Ui.Shared\Endpoints\IngestionJobEndpoints.cs:171` |
| `POST` | `/api/maintenance/execute` | `src\Meridian.Ui.Shared\Endpoints\ArchiveMaintenanceEndpoints.cs:142` |
| `GET` | `/api/maintenance/executions` | `src\Meridian.Ui.Shared\Endpoints\ArchiveMaintenanceEndpoints.cs:183` |
| `POST` | `/api/maintenance/executions/cleanup` | `src\Meridian.Ui.Shared\Endpoints\ArchiveMaintenanceEndpoints.cs:432` |
| `GET` | `/api/maintenance/executions/failed` | `src\Meridian.Ui.Shared\Endpoints\ArchiveMaintenanceEndpoints.cs:228` |
| `POST` | `/api/maintenance/executions/{executionId}/cancel` | `src\Meridian.Ui.Shared\Endpoints\ArchiveMaintenanceEndpoints.cs:166` |
| `GET` | `/api/maintenance/presets` | `src\Meridian.Ui.Shared\Endpoints\ArchiveMaintenanceEndpoints.cs:353` |
| `GET` | `/api/maintenance/schedules/summary` | `src\Meridian.Ui.Shared\Endpoints\ArchiveMaintenanceEndpoints.cs:244` |
| `PUT` | `/api/maintenance/schedules/{scheduleId}` | `src\Meridian.Ui.Shared\Endpoints\ArchiveMaintenanceEndpoints.cs:37` |
| `GET` | `/api/maintenance/schedules/{scheduleId}/executions` | `src\Meridian.Ui.Shared\Endpoints\ArchiveMaintenanceEndpoints.cs:213` |
| `GET` | `/api/maintenance/schedules/{scheduleId}/summary` | `src\Meridian.Ui.Shared\Endpoints\ArchiveMaintenanceEndpoints.cs:256` |
| `POST` | `/api/maintenance/schedules/{scheduleId}/trigger` | `src\Meridian.Ui.Shared\Endpoints\ArchiveMaintenanceEndpoints.cs:119` |
| `GET` | `/api/maintenance/statistics` | `src\Meridian.Ui.Shared\Endpoints\ArchiveMaintenanceEndpoints.cs:271` |
| `GET` | `/api/maintenance/status` | `src\Meridian.Ui.Shared\Endpoints\ArchiveMaintenanceEndpoints.cs:299` |
| `GET` | `/api/maintenance/task-types` | `src\Meridian.Ui.Shared\Endpoints\ArchiveMaintenanceEndpoints.cs:410` |
| `POST` | `/api/maintenance/validate-cron` | `src\Meridian.Ui.Shared\Endpoints\ArchiveMaintenanceEndpoints.cs:313` |
| `GET` | `/api/packaging/contents` | `src\Meridian.Ui.Shared\Endpoints\PackagingEndpoints.cs:171` |
| `POST` | `/api/packaging/create` | `src\Meridian.Ui.Shared\Endpoints\PackagingEndpoints.cs:34` |
| `GET` | `/api/packaging/download/{fileName}` | `src\Meridian.Ui.Shared\Endpoints\PackagingEndpoints.cs:287` |
| `POST` | `/api/packaging/import` | `src\Meridian.Ui.Shared\Endpoints\PackagingEndpoints.cs:88` |
| `GET` | `/api/packaging/list` | `src\Meridian.Ui.Shared\Endpoints\PackagingEndpoints.cs:204` |
| `POST` | `/api/packaging/validate` | `src\Meridian.Ui.Shared\Endpoints\PackagingEndpoints.cs:137` |
| `GET` | `/api/portfolio/household` | `src\Meridian.Ui.Shared\Endpoints\FundAccountEndpoints.cs:273` |
| `GET` | `/api/system/lifecycle` | `src\Meridian\UiServer.cs:671` |
| `POST` | `/api/system/shutdown` | `src\Meridian\UiServer.cs:705` |
| `GET` | `/api/system/shutdown/receipts/latest` | `src\Meridian\UiServer.cs:780` |
| `GET` | `/api/system/shutdown/{operationId}` | `src\Meridian\UiServer.cs:759` |

## Deprecated Documentation

These endpoints are documented but no longer exist in the code:

| Method | Path | Location |
|--------|------|----------|
| `COMPUTES` | `/api/alignment/preview` | `docs\reference\api-reference.md:159` |
| `LIST` | `/api/backfill/checkpoints` | `docs\reference\api-reference.md:470` |
| `LIST` | `/api/backfill/checkpoints/resumable` | `docs\reference\api-reference.md:471` |
| `CHECKPOINT` | `/api/backfill/checkpoints/{jobId}` | `docs\reference\api-reference.md:472` |
| `SYMBOLS` | `/api/backfill/checkpoints/{jobId}/pending` | `docs\reference\api-reference.md:473` |
| `RESUME` | `/api/backfill/checkpoints/{jobId}/resume` | `docs\reference\api-reference.md:474` |
| `ESTIMATE` | `/api/backfill/cost-estimate` | `docs\reference\api-reference.md:455` |
| `CURRENT` | `/api/backfill/progress` | `docs\reference\api-reference.md:458` |
| `LIST` | `/api/backfill/providers` | `docs\reference\api-reference.md:453` |
| `EXECUTE` | `/api/backfill/run` | `docs\reference\api-reference.md:456` |
| `PREVIEW` | `/api/backfill/run/preview` | `docs\reference\api-reference.md:457` |
| `LAST` | `/api/backfill/status` | `docs\reference\api-reference.md:454` |
| `BACKPRESSURE` | `/api/backpressure` | `docs\reference\api-reference.md:755` |
| `FULL` | `/api/config` | `docs\reference\api-reference.md:434` |
| `UPDATE` | `/api/config/alpaca` | `docs\reference\api-reference.md:436` |
| `BACKWARD` | `/api/config/data-sources` | `docs\reference\api-reference.md:785` |
| `UPDATE` | `/api/config/datasource` | `docs\reference\api-reference.md:435` |
| `CREATE` | `/api/config/datasources` | `docs\reference\api-reference.md:443` |
| `LIST` | `/api/config/datasources` | `docs\reference\api-reference.md:442` |
| `SET` | `/api/config/datasources/defaults` | `docs\reference\api-reference.md:446` |
| `UPDATE` | `/api/config/datasources/failover` | `docs\reference\api-reference.md:447` |
| `DELETE` | `/api/config/datasources/{id}` | `docs\reference\api-reference.md:444` |
| `TOGGLE` | `/api/config/datasources/{id}/toggle` | `docs\reference\api-reference.md:445` |
| `GET` | `/api/config/derivatives` | `docs\reference\api-reference.md:440` |
| `UPDATE` | `/api/config/derivatives` | `docs\reference\api-reference.md:441` |
| `UPDATE` | `/api/config/storage` | `docs\reference\api-reference.md:437` |
| `ADD` | `/api/config/symbols` | `docs\reference\api-reference.md:438` |
| `REMOVE` | `/api/config/symbols/{symbol}` | `docs\reference\api-reference.md:439` |
| `CONNECTION` | `/api/connections` | `docs\reference\api-reference.md:488` |
| `BEST` | `/api/data/bbo/{symbol}` | `docs\reference\api-reference.md:612` |
| `LIVE` | `/api/data/health` | `docs\reference\api-reference.md:614` |
| `ORDER` | `/api/data/orderbook/{symbol}` | `docs\reference\api-reference.md:611` |
| `ORDER` | `/api/data/orderflow/{symbol}` | `docs\reference\api-reference.md:613` |
| `LATEST` | `/api/data/quotes/{symbol}` | `docs\reference\api-reference.md:610` |
| `RECENT` | `/api/data/trades/{symbol}` | `docs\reference\api-reference.md:609` |
| `CREATE` | `/api/environment-designer/drafts` | `docs\reference\api-reference.md:701` |
| `LIST` | `/api/environment-designer/drafts` | `docs\reference\api-reference.md:699` |
| `DELETE` | `/api/environment-designer/drafts/{draftId}` | `docs\reference\api-reference.md:703` |
| `LOAD` | `/api/environment-designer/drafts/{draftId}` | `docs\reference\api-reference.md:700` |
| `SAVE` | `/api/environment-designer/drafts/{draftId}` | `docs\reference\api-reference.md:702` |
| `PUBLISH` | `/api/environment-designer/publish` | `docs\reference\api-reference.md:706` |
| `PREVIEW` | `/api/environment-designer/publish/preview` | `docs\reference\api-reference.md:705` |
| `FETCH` | `/api/environment-designer/runtime/current` | `docs\reference\api-reference.md:711` |
| `FETCH` | `/api/environment-designer/runtime/versions/{versionId}` | `docs\reference\api-reference.md:712` |
| `VALIDATE` | `/api/environment-designer/validate` | `docs\reference\api-reference.md:704` |
| `LIST` | `/api/environment-designer/versions` | `docs\reference\api-reference.md:707` |
| `GET` | `/api/environment-designer/versions/current` | `docs\reference\api-reference.md:708` |
| `LOAD` | `/api/environment-designer/versions/{versionId}` | `docs\reference\api-reference.md:709` |
| `ROLL` | `/api/environment-designer/versions/{versionId}/rollback` | `docs\reference\api-reference.md:710` |
| `ERROR` | `/api/errors` | `docs\reference\api-reference.md:754` |
| `SERVER` | `/api/events/stream` | `docs\reference\api-reference.md:756` |
| `ACCOUNT` | `/api/execution/account` | `docs\reference\api-reference.md:515` |
| `OPERATOR` | `/api/execution/audit` | `docs\reference\api-reference.md:528` |
| `ORDER` | `/api/execution/capabilities` | `docs\reference\api-reference.md:527` |
| `EXECUTION` | `/api/execution/controls` | `docs\reference\api-reference.md:529` |
| `OPEN` | `/api/execution/controls/circuit-breaker` | `docs\reference\api-reference.md:530` |
| `GATEWAY` | `/api/execution/health` | `docs\reference\api-reference.md:526` |
| `OPEN` | `/api/execution/orders` | `docs\reference\api-reference.md:521` |
| `CANCEL` | `/api/execution/orders/cancel-all` | `docs\reference\api-reference.md:524` |
| `SUBMIT` | `/api/execution/orders/submit` | `docs\reference\api-reference.md:522` |
| `CANCEL` | `/api/execution/orders/{orderId}/cancel` | `docs\reference\api-reference.md:523` |
| `PORTFOLIO` | `/api/execution/portfolio` | `docs\reference\api-reference.md:525` |
| `LEGACY` | `/api/execution/positions` | `docs\reference\api-reference.md:516` |
| `SUBMIT` | `/api/execution/positions/actions/close` | `docs\reference\api-reference.md:518` |
| `SUBMIT` | `/api/execution/positions/actions/upsize` | `docs\reference\api-reference.md:519` |
| `BROKER` | `/api/execution/positions/blotter` | `docs\reference\api-reference.md:517` |
| `LEGACY` | `/api/execution/positions/{symbol}/close` | `docs\reference\api-reference.md:520` |
| `PAPER` | `/api/execution/sessions` | `docs\reference\api-reference.md:531` |
| `CREATE` | `/api/execution/sessions/create` | `docs\reference\api-reference.md:533` |
| `PAPER` | `/api/execution/sessions/{sessionId}` | `docs\reference\api-reference.md:532` |
| `CLOSE` | `/api/execution/sessions/{sessionId}/close` | `docs\reference\api-reference.md:534` |
| `REPLAY` | `/api/execution/sessions/{sessionId}/replay` | `docs\reference\api-reference.md:734` |
| `FAILOVER` | `/api/failover/config` | `docs\reference\api-reference.md:579` |
| `UPDATE` | `/api/failover/config` | `docs\reference\api-reference.md:580` |
| `FORCE` | `/api/failover/force/{ruleId}` | `docs\reference\api-reference.md:584` |
| `PROVIDER` | `/api/failover/health` | `docs\reference\api-reference.md:585` |
| `ALL` | `/api/failover/rules` | `docs\reference\api-reference.md:581` |
| `CREATE` | `/api/failover/rules` | `docs\reference\api-reference.md:582` |
| `DELETE` | `/api/failover/rules/{id}` | `docs\reference\api-reference.md:583` |
| `RESOLVES` | `/api/fund-structure/reporting/templates/render` | `docs\reference\api-reference.md:168` |
| `DETAILED` | `/api/health/detailed` | `docs\reference\api-reference.md:753` |
| `QUERY` | `/api/historical` | `docs\reference\api-reference.md:620` |
| `LIST` | `/api/historical/symbols` | `docs\reference\api-reference.md:621` |
| `DATE` | `/api/historical/{symbol}/daterange` | `docs\reference\api-reference.md:622` |
| `CREATE` | `/api/ingestion/jobs` | `docs\reference\api-reference.md:629` |
| `LIST` | `/api/ingestion/jobs` | `docs\reference\api-reference.md:628` |
| `LIST` | `/api/ingestion/jobs/resumable` | `docs\reference\api-reference.md:633` |
| `DELETE` | `/api/ingestion/jobs/{jobId}` | `docs\reference\api-reference.md:631` |
| `GET` | `/api/ingestion/jobs/{jobId}` | `docs\reference\api-reference.md:630` |
| `TRANSITION` | `/api/ingestion/jobs/{jobId}/transition` | `docs\reference\api-reference.md:632` |
| `SUMMARY` | `/api/ingestion/summary` | `docs\reference\api-reference.md:634` |
| `POST` | `/api/ledger/journal-automation/dividend-intake` | `docs\reference\api-reference.md:356` |
| `POST` | `/api/ledger/journal-automation/fee-accrual-intake` | `docs\reference\api-reference.md:357` |
| `GET` | `/api/loans/portfolio` | `docs\reference\api-reference.md:369` |
| `RUN` | `/api/maintenance/execute` | `docs\reference\api-reference.md:664` |
| `LIST` | `/api/maintenance/executions` | `docs\reference\api-reference.md:666` |
| `CLEAN` | `/api/maintenance/executions/cleanup` | `docs\reference\api-reference.md:669` |
| `LIST` | `/api/maintenance/executions/failed` | `docs\reference\api-reference.md:668` |
| `CANCEL` | `/api/maintenance/executions/{executionId}/cancel` | `docs\reference\api-reference.md:665` |
| `LIST` | `/api/maintenance/presets` | `docs\reference\api-reference.md:673` |
| `CREATE` | `/api/maintenance/schedules` | `docs\reference\api-reference.md:653` |
| `LIST` | `/api/maintenance/schedules` | `docs\reference\api-reference.md:652` |
| `SUMMARY` | `/api/maintenance/schedules/summary` | `docs\reference\api-reference.md:663` |
| `GET` | `/api/maintenance/schedules/{scheduleId}` | `docs\reference\api-reference.md:654` |
| `UPDATE` | `/api/maintenance/schedules/{scheduleId}` | `docs\reference\api-reference.md:655` |
| `DISABLE` | `/api/maintenance/schedules/{scheduleId}/disable` | `docs\reference\api-reference.md:658` |
| `ENABLE` | `/api/maintenance/schedules/{scheduleId}/enable` | `docs\reference\api-reference.md:657` |
| `EXECUTION` | `/api/maintenance/schedules/{scheduleId}/executions` | `docs\reference\api-reference.md:661` |
| `COMPATIBILITY` | `/api/maintenance/schedules/{scheduleId}/run` | `docs\reference\api-reference.md:660` |
| `SUMMARY` | `/api/maintenance/schedules/{scheduleId}/summary` | `docs\reference\api-reference.md:662` |
| `TRIGGER` | `/api/maintenance/schedules/{scheduleId}/trigger` | `docs\reference\api-reference.md:659` |
| `OVERALL` | `/api/maintenance/statistics` | `docs\reference\api-reference.md:670` |
| `CURRENT` | `/api/maintenance/status` | `docs\reference\api-reference.md:671` |
| `LIST` | `/api/maintenance/task-types` | `docs\reference\api-reference.md:674` |
| `VALIDATE` | `/api/maintenance/validate-cron` | `docs\reference\api-reference.md:672` |
| `CACHED` | `/api/options/chains/{underlyingSymbol}` | `docs\reference\api-reference.md:498` |
| `AVAILABLE` | `/api/options/expirations/{underlyingSymbol}` | `docs\reference\api-reference.md:496` |
| `CACHED` | `/api/options/quotes/{underlyingSymbol}` | `docs\reference\api-reference.md:499` |
| `REFRESH` | `/api/options/refresh` | `docs\reference\api-reference.md:502` |
| `AVAILABLE` | `/api/options/strikes/{underlyingSymbol}/{expiration}` | `docs\reference\api-reference.md:497` |
| `OPTIONS` | `/api/options/summary` | `docs\reference\api-reference.md:500` |
| `TRACKED` | `/api/options/underlyings` | `docs\reference\api-reference.md:501` |
| `LIST` | `/api/packaging/contents` | `docs\reference\api-reference.md:644` |
| `CREATE` | `/api/packaging/create` | `docs\reference\api-reference.md:640` |
| `DOWNLOAD` | `/api/packaging/download/{fileName}` | `docs\reference\api-reference.md:645` |
| `IMPORT` | `/api/packaging/import` | `docs\reference\api-reference.md:641` |
| `LIST` | `/api/packaging/list` | `docs\reference\api-reference.md:643` |
| `VALIDATE` | `/api/packaging/validate` | `docs\reference\api-reference.md:642` |
| `PROVIDER` | `/api/providers/capability-matrix` | `docs\reference\api-reference.md:484` |
| `PROVIDER` | `/api/providers/catalog` | `docs\reference\api-reference.md:485` |
| `SINGLE` | `/api/providers/catalog/{providerId}` | `docs\reference\api-reference.md:486` |
| `FEATURE` | `/api/providers/comparison` | `docs\reference\api-reference.md:483` |
| `IB` | `/api/providers/ib/error-codes` | `docs\reference\api-reference.md:592` |
| `IB` | `/api/providers/ib/limits` | `docs\reference\api-reference.md:593` |
| `IB` | `/api/providers/ib/status` | `docs\reference\api-reference.md:591` |
| `LATENCY` | `/api/providers/latency` | `docs\reference\api-reference.md:487` |
| `PROVIDER` | `/api/providers/metrics` | `docs\reference\api-reference.md:481` |
| `SINGLE` | `/api/providers/metrics/{providerId}` | `docs\reference\api-reference.md:482` |
| `ALL` | `/api/providers/status` | `docs\reference\api-reference.md:480` |
| `DROPPED` | `/api/quality/drops` | `docs\reference\api-reference.md:684` |
| `DROPS` | `/api/quality/drops/{symbol}` | `docs\reference\api-reference.md:685` |
| `GOVERNED` | `/api/risk/escalations` | `docs\reference\api-reference.md:555` |
| `APPROVE` | `/api/risk/escalations/{escalationId}/approve` | `docs\reference\api-reference.md:556` |
| `DENY` | `/api/risk/escalations/{escalationId}/deny` | `docs\reference\api-reference.md:557` |
| `LIVE` | `/api/risk/rules` | `docs\reference\api-reference.md:551` |
| `OPERATOR` | `/api/risk/rules/{ruleName}/config` | `docs\reference\api-reference.md:553` |
| `UPDATE` | `/api/risk/rules/{ruleName}/config` | `docs\reference\api-reference.md:554` |
| `LIVE` | `/api/risk/rules/{ruleName}/status` | `docs\reference\api-reference.md:552` |
| `POST` | `/api/security-master/corporate-actions/ingest` | `docs\reference\api-reference.md:370` |
| `RESOLVES` | `/api/security-master/resolve` | `docs\reference\api-reference.md:162` |
| `SEARCH` | `/api/security-master/search` | `docs\reference\api-reference.md:163` |
| `FULL` | `/api/status` | `docs\reference\api-reference.md:752` |
| `ARCHIVE` | `/api/storage/archive/stats` | `docs\reference\api-reference.md:408` |
| `BREAKDOWN` | `/api/storage/breakdown` | `docs\reference\api-reference.md:398` |
| `FULL` | `/api/storage/catalog` | `docs\reference\api-reference.md:400` |
| `RUN` | `/api/storage/cleanup` | `docs\reference\api-reference.md:407` |
| `FILES` | `/api/storage/cleanup/candidates` | `docs\reference\api-reference.md:406` |
| `STORAGE` | `/api/storage/health` | `docs\reference\api-reference.md:399` |
| `DETAILED` | `/api/storage/health/check` | `docs\reference\api-reference.md:409` |
| `FIND` | `/api/storage/health/orphans` | `docs\reference\api-reference.md:410` |
| `RUN` | `/api/storage/maintenance/defrag` | `docs\reference\api-reference.md:414` |
| `AVAILABLE` | `/api/storage/profiles` | `docs\reference\api-reference.md:396` |
| `ACTIVE` | `/api/storage/quality/alerts` | `docs\reference\api-reference.md:423` |
| `ACKNOWLEDGE` | `/api/storage/quality/alerts/{alertId}/acknowledge` | `docs\reference\api-reference.md:424` |
| `DETECTED` | `/api/storage/quality/anomalies` | `docs\reference\api-reference.md:427` |
| `RUN` | `/api/storage/quality/check` | `docs\reference\api-reference.md:428` |
| `SOURCE` | `/api/storage/quality/rankings/{symbol}` | `docs\reference\api-reference.md:425` |
| `QUALITY` | `/api/storage/quality/scores` | `docs\reference\api-reference.md:421` |
| `OVERALL` | `/api/storage/quality/summary` | `docs\reference\api-reference.md:420` |
| `QUALITY` | `/api/storage/quality/symbol/{symbol}` | `docs\reference\api-reference.md:422` |
| `QUALITY` | `/api/storage/quality/trends?days=` | `docs\reference\api-reference.md:426` |
| `SEARCH` | `/api/storage/search/files?symbol=&q=` | `docs\reference\api-reference.md:401` |
| `OVERALL` | `/api/storage/stats` | `docs\reference\api-reference.md:397` |
| `LIST` | `/api/storage/symbol/{symbol}/files` | `docs\reference\api-reference.md:404` |
| `STORAGE` | `/api/storage/symbol/{symbol}/info` | `docs\reference\api-reference.md:402` |
| `STORAGE` | `/api/storage/symbol/{symbol}/path` | `docs\reference\api-reference.md:405` |
| `DETAILED` | `/api/storage/symbol/{symbol}/stats` | `docs\reference\api-reference.md:403` |
| `EXECUTE` | `/api/storage/tiers/migrate` | `docs\reference\api-reference.md:413` |
| `MIGRATION` | `/api/storage/tiers/plan?days=` | `docs\reference\api-reference.md:412` |
| `TIER` | `/api/storage/tiers/statistics` | `docs\reference\api-reference.md:411` |
| `FETCHES` | `/api/strategies/covered-call/chain-preview` | `docs\reference\api-reference.md:167` |
| `ALL` | `/api/symbols` | `docs\reference\api-reference.md:376` |
| `ADD` | `/api/symbols/add` | `docs\reference\api-reference.md:380` |
| `SYMBOLS` | `/api/symbols/archived` | `docs\reference\api-reference.md:378` |
| `BATCH` | `/api/symbols/batch` | `docs\reference\api-reference.md:390` |
| `ADD` | `/api/symbols/bulk-add` | `docs\reference\api-reference.md:383` |
| `REMOVE` | `/api/symbols/bulk-remove` | `docs\reference\api-reference.md:384` |
| `ALL` | `/api/symbols/mappings` | `docs\reference\api-reference.md:599` |
| `CREATE` | `/api/symbols/mappings` | `docs\reference\api-reference.md:600` |
| `IMPORT` | `/api/symbols/mappings/import` | `docs\reference\api-reference.md:603` |
| `DELETE` | `/api/symbols/mappings/{symbol}` | `docs\reference\api-reference.md:602` |
| `SINGLE` | `/api/symbols/mappings/{symbol}` | `docs\reference\api-reference.md:601` |
| `SYMBOLS` | `/api/symbols/monitored` | `docs\reference\api-reference.md:377` |
| `SEARCH` | `/api/symbols/search?q=` | `docs\reference\api-reference.md:385` |
| `AGGREGATE` | `/api/symbols/statistics` | `docs\reference\api-reference.md:387` |
| `VALIDATE` | `/api/symbols/validate` | `docs\reference\api-reference.md:386` |
| `ARCHIVE` | `/api/symbols/{symbol}/archive` | `docs\reference\api-reference.md:382` |
| `RECENT` | `/api/symbols/{symbol}/depth` | `docs\reference\api-reference.md:389` |
| `REMOVE` | `/api/symbols/{symbol}/remove` | `docs\reference\api-reference.md:381` |
| `DETAILED` | `/api/symbols/{symbol}/status` | `docs\reference\api-reference.md:379` |
| `RECENT` | `/api/symbols/{symbol}/trades` | `docs\reference\api-reference.md:388` |
| `ACCOUNTING` | `/api/workstation/accounting` | `docs\reference\api-reference.md:723` |
| `DATA` | `/api/workstation/data` | `docs\reference\api-reference.md:724` |
| `FILTERABLE` | `/api/workstation/data/ingestion-operations` | `docs\reference\api-reference.md:725` |
| `INGESTION` | `/api/workstation/data/ingestion-operations/{jobId}` | `docs\reference\api-reference.md:726` |
| `APPLY` | `/api/workstation/data/ingestion-operations/{jobId}/actions/{action}` | `docs\reference\api-reference.md:727` |
| `CONSOLIDATED` | `/api/workstation/data/storage-assurance` | `docs\reference\api-reference.md:728` |
| `EXECUTE` | `/api/workstation/data/storage-assurance/actions/execute` | `docs\reference\api-reference.md:730` |
| `PREVIEW` | `/api/workstation/data/storage-assurance/actions/preview` | `docs\reference\api-reference.md:729` |
| `EVIDENCE` | `/api/workstation/evidence/vault/search` | `docs\reference\api-reference.md:158` |
| `ACCOUNT` | `/api/workstation/operator/inbox` | `docs\reference\api-reference.md:732` |
| `COMPARES` | `/api/workstation/runs/compare` | `docs\reference\api-reference.md:160` |
| `DIFFS` | `/api/workstation/runs/diff` | `docs\reference\api-reference.md:161` |
| `PREVIEWS` | `/api/workstation/strategy/designer/preview` | `docs\reference\api-reference.md:165` |
| `NORMALIZES` | `/api/workstation/strategy/designer/validate` | `docs\reference\api-reference.md:164` |
| `VALIDATES` | `/api/workstation/strategy/engine/validate-run` | `docs\reference\api-reference.md:166` |
| `TRADING` | `/api/workstation/trading` | `docs\reference\api-reference.md:733` |
| `TRADING` | `/api/workstation/trading/readiness` | `docs\reference\api-reference.md:731` |

## Recommendations

1. **Document 49 missing endpoints**: Add entries to `docs/reference/api-reference.md` with descriptions, parameters, and response formats.

2. **Remove 218 deprecated entries**: Clean up documentation for endpoints that no longer exist.

---

*This report is auto-generated. Run `python3 build/scripts/docs/validate-api-docs.py` to regenerate.*
