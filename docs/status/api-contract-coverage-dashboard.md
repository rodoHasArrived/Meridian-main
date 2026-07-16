# API Contract Coverage Dashboard

_Auto-generated from canonical JSON payload._
_Generated: 1970-01-01T00:00:00+00:00_
Data sources: `src/**/*.cs endpoint mappings`, `src/Meridian.Contracts/Api/UiApiRoutes.cs`, `src/Meridian.Contracts/Workstation/*.cs`, `docs/**/*.md`


Tracks whether mapped API routes and workstation DTO contracts are visible in the Markdown documentation set.

## Summary

| Metric | Value |
|---|---:|
| Weighted score | 100.0% |
| Endpoint coverage | 100.0% |
| Workstation contract coverage | 100.0% |
| Endpoints documented | 610 / 610 |
| Workstation contracts documented | 872 / 872 |

## Endpoint Coverage

| Method | Route | Status | Source |
|---|---|---|---|
| `GET` | `/api/accounting-system/export-packages` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:525` |
| `POST` | `/api/accounting-system/export-packages` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:558` |
| `POST` | `/api/accounting-system/export-packages/certification` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:644` |
| `GET` | `/api/accounting-system/export-packages/{exportPackageId}/manifest` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:606` |
| `GET` | `/api/accounting-system/import/latest` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:390` |
| `POST` | `/api/accounting-system/import/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:365` |
| `GET` | `/api/accounting-system/mapping-profiles` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:448` |
| `POST` | `/api/accounting-system/mapping-profiles` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:477` |
| `GET` | `/api/accounting-system/migration-run-artifacts` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:228` |
| `POST` | `/api/accounting-system/migration-run-artifacts` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:256` |
| `POST` | `/api/accounting-system/migration-runs` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:191` |
| `GET` | `/api/accounting-system/migration-worker-plans` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:297` |
| `POST` | `/api/accounting-system/migration-worker-plans` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:327` |
| `GET` | `/api/accounting-system/production-certification-profile` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:123` |
| `POST` | `/api/accounting-system/production-certification-profile` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:150` |
| `POST` | `/api/accounting-system/production-readiness` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:35` |
| `GET` | `/api/accounting-system/providers` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:20` |
| `GET` | `/api/accounting-system/reconciliation/latest` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:419` |
| `GET` | `/api/accounting-system/tenant-administration-profile` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:58` |
| `POST` | `/api/accounting-system/tenant-administration-profile` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:82` |
| `POST` | `/api/admin/cleanup/execute` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:272` |
| `GET` | `/api/admin/cleanup/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:232` |
| `GET` | `/api/admin/error-codes` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:349` |
| `GET` | `/api/admin/maintenance/history` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:83` |
| `POST` | `/api/admin/maintenance/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:46` |
| `GET` | `/api/admin/maintenance/run/{runId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:70` |
| `GET` | `/api/admin/maintenance/schedule` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:26` |
| `GET` | `/api/admin/quick-check` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:381` |
| `GET` | `/api/admin/retention` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:179` |
| `POST` | `/api/admin/retention/apply` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:216` |
| `GET` | `/api/admin/retention/compliance-report` | Documented | `src/Meridian.Ui.Shared/Endpoints/ResilienceEndpoints.cs:88` |
| `DELETE` | `/api/admin/retention/{policyId}/delete` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:199` |
| `POST` | `/api/admin/selftest` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:323` |
| `GET` | `/api/admin/show-config` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:362` |
| `POST` | `/api/admin/storage/migrate/{targetTier}` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:116` |
| `GET` | `/api/admin/storage/permissions` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:289` |
| `GET` | `/api/admin/storage/tiers` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:101` |
| `GET` | `/api/admin/storage/usage` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:138` |
| `POST` | `/api/alignment/create` | Documented | `src/Meridian.Ui.Shared/Endpoints/HistoricalEndpoints.cs:176` |
| `POST` | `/api/alignment/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/HistoricalEndpoints.cs:196` |
| `GET` | `/api/analytics/anomalies` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:87` |
| `GET` | `/api/analytics/compare` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:51` |
| `GET` | `/api/analytics/completeness` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:113` |
| `GET` | `/api/analytics/gaps` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:24` |
| `POST` | `/api/analytics/gaps/repair` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:36` |
| `GET` | `/api/analytics/latency` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:63` |
| `GET` | `/api/analytics/latency/stats` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:75` |
| `GET` | `/api/analytics/quality-report` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:101` |
| `GET` | `/api/analytics/rate-limits` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:143` |
| `GET` | `/api/analytics/throughput` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:131` |
| `GET` | `/api/auth/access-assignments` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:465` |
| `POST` | `/api/auth/access-assignments` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:506` |
| `POST` | `/api/auth/access-assignments/{assignmentId}/revoke` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:546` |
| `GET` | `/api/auth/accounts` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:177` |
| `PUT` | `/api/auth/accounts/{username}` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:200` |
| `POST` | `/api/auth/accounts/{username}/disable` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:298` |
| `POST` | `/api/auth/accounts/{username}/password-reset` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:238` |
| `GET` | `/api/auth/audit` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:389` |
| `POST` | `/api/auth/bootstrap` | Documented | `src/Meridian.Ui.Shared/Endpoints/InitialAccountBootstrapEndpoints.cs:18` |
| `POST` | `/api/auth/login` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:35` |
| `POST` | `/api/auth/logout` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:130` |
| `GET` | `/api/auth/me` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:150` |
| `POST` | `/api/auth/role-profiles` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:413` |
| `GET` | `/api/auth/roles` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:171` |
| `POST` | `/api/auth/sessions/revoke` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:358` |
| `GET` | `/api/backfill/checkpoints` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:25` |
| `GET` | `/api/backfill/checkpoints/resumable` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:91` |
| `GET` | `/api/backfill/checkpoints/validation` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:37` |
| `GET` | `/api/backfill/checkpoints/{jobId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:118` |
| `GET` | `/api/backfill/checkpoints/{jobId}/pending` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:144` |
| `POST` | `/api/backfill/checkpoints/{jobId}/resume` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:204` |
| `GET` | `/api/backfill/completeness` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:207` |
| `POST` | `/api/backfill/cost-estimate` | Documented | `src/Meridian.Ui.Shared/Endpoints/ResilienceEndpoints.cs:48` |
| `GET` | `/api/backfill/executions` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:111` |
| `POST` | `/api/backfill/gap-fill` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:68` |
| `GET` | `/api/backfill/gaps` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:165` |
| `GET` | `/api/backfill/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:28` |
| `GET` | `/api/backfill/presets` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:96` |
| `GET` | `/api/backfill/progress` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:112` |
| `GET` | `/api/backfill/providers` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:33` |
| `GET` | `/api/backfill/providers/audit` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:186` |
| `POST` | `/api/backfill/providers/dry-run-plan` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:167` |
| `GET` | `/api/backfill/providers/fallback-chain` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:155` |
| `GET` | `/api/backfill/providers/metadata` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:134` |
| `GET` | `/api/backfill/providers/statuses` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:144` |
| `GET` | `/api/backfill/resolve/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:50` |
| `POST` | `/api/backfill/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:84` |
| `POST` | `/api/backfill/run/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:56` |
| `GET` | `/api/backfill/schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:159` |
| `POST` | `/api/backfill/schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:174` |
| `GET` | `/api/backfill/schedules/templates` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:284` |
| `DELETE` | `/api/backfill/schedules/{id}` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:200` |
| `GET` | `/api/backfill/schedules/{id}` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:189` |
| `POST` | `/api/backfill/schedules/{id}/disable` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:231` |
| `POST` | `/api/backfill/schedules/{id}/enable` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:216` |
| `GET` | `/api/backfill/schedules/{id}/history` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:271` |
| `POST` | `/api/backfill/schedules/{id}/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:246` |
| `GET` | `/api/backfill/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:129` |
| `GET` | `/api/backfill/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:43` |
| `GET` | `/api/backfill/validation` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:25` |
| `GET` | `/api/backfill/validation/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:110` |
| `GET` | `/api/backpressure` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:119` |
| `GET` | `/api/calendar/holidays` | Documented | `src/Meridian.Ui.Shared/Endpoints/CalendarEndpoints.cs:64` |
| `GET` | `/api/calendar/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/CalendarEndpoints.cs:21` |
| `GET` | `/api/calendar/trading-days` | Documented | `src/Meridian.Ui.Shared/Endpoints/CalendarEndpoints.cs:89` |
| `GET` | `/api/canonicalization/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:112` |
| `GET` | `/api/canonicalization/parity` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:47` |
| `GET` | `/api/canonicalization/parity/{provider}` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:75` |
| `GET` | `/api/canonicalization/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:20` |
| `GET` | `/api/catalog/coverage` | Documented | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:232` |
| `GET` | `/api/catalog/search` | Documented | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:24` |
| `GET` | `/api/catalog/symbols` | Documented | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:128` |
| `GET` | `/api/catalog/timeline` | Documented | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:160` |
| `GET` | `/api/compliance/access-reviews` | Documented | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:61` |
| `POST` | `/api/compliance/access-reviews/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:52` |
| `POST` | `/api/compliance/actions/evaluate` | Documented | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:15` |
| `GET` | `/api/compliance/audit/extract` | Documented | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:33` |
| `GET` | `/api/compliance/controls/attestation` | Documented | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:37` |
| `GET` | `/api/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:36` |
| `POST` | `/api/config/alpaca` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:138` |
| `GET` | `/api/config/data-sources` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:623` |
| `POST` | `/api/config/data-sources` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:641` |
| `POST` | `/api/config/datasource` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:120` |
| `GET` | `/api/config/datasources` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:53` |
| `POST` | `/api/config/datasources` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:72` |
| `POST` | `/api/config/datasources/defaults` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:192` |
| `POST` | `/api/config/datasources/failover` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:225` |
| `GET` | `/api/config/derivatives` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:220` |
| `POST` | `/api/config/derivatives` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:229` |
| `GET` | `/api/config/effective` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:63` |
| `POST` | `/api/config/storage` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:151` |
| `POST` | `/api/config/symbols` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:180` |
| `GET` | `/api/connections` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:145` |
| `GET` | `/api/data/bbo/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:269` |
| `GET` | `/api/data/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:350` |
| `GET` | `/api/data/l3-orderbook/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:199` |
| `GET` | `/api/data/orderbook/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:127` |
| `GET` | `/api/data/orderflow/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:309` |
| `GET` | `/api/data/quotes-snapshot` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:109` |
| `GET` | `/api/data/quotes/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:65` |
| `GET` | `/api/data/trades/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:25` |
| `GET` | `/api/demo/historical/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:118` |
| `GET` | `/api/demo/market-data/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:102` |
| `GET` | `/api/demo/mode` | Documented | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:71` |
| `GET` | `/api/demo/symbols` | Documented | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:88` |
| `POST` | `/api/dev/seed/bank-transactions` | Documented | `src/Meridian.Ui.Shared/Endpoints/BankingEndpoints.cs:254` |
| `GET` | `/api/diagnostics/bundle` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:148` |
| `GET` | `/api/diagnostics/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:122` |
| `GET` | `/api/diagnostics/coordination` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:494` |
| `POST` | `/api/diagnostics/dry-run` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:35` |
| `GET` | `/api/diagnostics/error-codes` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:377` |
| `GET` | `/api/diagnostics/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:169` |
| `GET` | `/api/diagnostics/providers` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:55` |
| `POST` | `/api/diagnostics/providers/{providerName}/test` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:297` |
| `GET` | `/api/diagnostics/quick-check` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:336` |
| `POST` | `/api/diagnostics/selftest` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:387` |
| `GET` | `/api/diagnostics/show-config` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:353` |
| `GET` | `/api/diagnostics/storage` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:72` |
| `POST` | `/api/diagnostics/test-connectivity` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:426` |
| `POST` | `/api/diagnostics/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:275` |
| `POST` | `/api/diagnostics/validate-config` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:465` |
| `POST` | `/api/diagnostics/validate-credentials` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:408` |
| `GET` | `/api/errors` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:108` |
| `GET` | `/api/events/stream` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:211` |
| `POST` | `/api/export/analysis` | Documented | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:81` |
| `GET` | `/api/export/formats` | Documented | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:121` |
| `POST` | `/api/export/integrity` | Documented | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:255` |
| `POST` | `/api/export/orderflow` | Documented | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:204` |
| `GET` | `/api/export/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:28` |
| `POST` | `/api/export/quality-report` | Documented | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:160` |
| `POST` | `/api/export/research-package` | Documented | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:341` |
| `POST` | `/api/export/strategy-package` | Documented | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:336` |
| `GET` | `/api/failover/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:34` |
| `POST` | `/api/failover/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:71` |
| `GET` | `/api/failover/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:226` |
| `GET` | `/api/failover/rules` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:94` |
| `POST` | `/api/failover/rules` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:124` |
| `GET` | `/api/funds/{fundId:guid}/accounts` | Documented | `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs:127` |
| `GET` | `/api/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:38` |
| `GET` | `/api/health/detailed` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:183` |
| `GET` | `/api/health/diagnostics/bundle` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:258` |
| `GET` | `/api/health/events` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:188` |
| `GET` | `/api/health/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:201` |
| `GET` | `/api/health/providers` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:55` |
| `GET` | `/api/health/providers/{provider}/diagnostics` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:103` |
| `POST` | `/api/health/providers/{provider}/test` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:220` |
| `GET` | `/api/health/storage` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:153` |
| `GET` | `/api/health/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:28` |
| `GET` | `/api/indices/{indexName}/constituents` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:553` |
| `GET` | `/api/ingestion/jobs` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:29` |
| `POST` | `/api/ingestion/jobs` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:110` |
| `GET` | `/api/ingestion/jobs/resumable` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:149` |
| `DELETE` | `/api/ingestion/jobs/{jobId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:171` |
| `GET` | `/api/ingestion/jobs/{jobId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:51` |
| `POST` | `/api/ingestion/jobs/{jobId}/transition` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:66` |
| `GET` | `/api/ingestion/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:160` |
| `POST` | `/api/journals/{journalEntryId:guid}/post` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:940` |
| `GET` | `/api/lean/algorithms` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:149` |
| `GET` | `/api/lean/auto-export` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:389` |
| `POST` | `/api/lean/auto-export/configure` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:420` |
| `GET` | `/api/lean/backtest/history` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:335` |
| `POST` | `/api/lean/backtest/start` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:250` |
| `DELETE` | `/api/lean/backtest/{backtestId}/delete` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:376` |
| `GET` | `/api/lean/backtest/{backtestId}/results` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:300` |
| `GET` | `/api/lean/backtest/{backtestId}/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:282` |
| `POST` | `/api/lean/backtest/{backtestId}/stop` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:320` |
| `GET` | `/api/lean/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:60` |
| `POST` | `/api/lean/results/ingest` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:455` |
| `GET` | `/api/lean/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:32` |
| `GET` | `/api/lean/symbol-map` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:549` |
| `POST` | `/api/lean/sync` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:179` |
| `GET` | `/api/lean/sync/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:230` |
| `POST` | `/api/lean/verify` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:87` |
| `GET` | `/api/ledger/accounting-configuration` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:559` |
| `POST` | `/api/ledger/accounting-configuration/activate` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:994` |
| `GET` | `/api/ledger/accounting-configuration/audit` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1029` |
| `POST` | `/api/ledger/accounting-configuration/chart` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:587` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:649` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/candidates` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:823` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/candidates/post` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:866` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/dry-run` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:782` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/projection-sets` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:910` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/promotion-approvals` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:680` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/test-cases` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:716` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/tests` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:953` |
| `POST` | `/api/ledger/accounting-configuration/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:747` |
| `POST` | `/api/ledger/accounting-configuration/templates` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:618` |
| `GET` | `/api/ledger/aggregates/{aggregateId:guid}/journal-entries` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:317` |
| `GET` | `/api/ledger/books` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:29` |
| `POST` | `/api/ledger/books` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:79` |
| `POST` | `/api/ledger/books/rollout-assessment` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:110` |
| `GET` | `/api/ledger/books/{ledgerBookId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:56` |
| `POST` | `/api/ledger/close-management/evidence-review` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1336` |
| `POST` | `/api/ledger/close-management/late-adjustments` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1150` |
| `POST` | `/api/ledger/close-management/late-adjustments/review` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1212` |
| `POST` | `/api/ledger/close-management/period-lock` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1398` |
| `POST` | `/api/ledger/close-management/period-plan/configuration` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1088` |
| `GET` | `/api/ledger/close-management/period-plan/{workflowId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1057` |
| `POST` | `/api/ledger/close-management/period-reopen` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1464` |
| `POST` | `/api/ledger/close-management/task-signoffs` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1274` |
| `POST` | `/api/ledger/journal-automation/daily-mark-to-market-batch-lifecycle` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:378` |
| `POST` | `/api/ledger/journal-automation/daily-mark-to-market-intake` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:427` |
| `POST` | `/api/ledger/journal-automation/daily-mark-to-market-run-due` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:348` |
| `GET` | `/api/ledger/journal-automation/daily-mark-to-market-schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:226` |
| `POST` | `/api/ledger/journal-automation/daily-mark-to-market-schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:257` |
| `POST` | `/api/ledger/journal-automation/dividend-intake` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:470` |
| `POST` | `/api/ledger/journal-automation/fee-accrual-intake` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:513` |
| `GET` | `/api/ledger/journal-automation/monthly-schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:21` |
| `POST` | `/api/ledger/journal-automation/monthly-schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:57` |
| `POST` | `/api/ledger/journal-automation/monthly-schedules/run-due` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:196` |
| `POST` | `/api/ledger/journal-automation/period-close-intake` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:558` |
| `GET` | `/api/ledger/journal-entry-workbench` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1722` |
| `POST` | `/api/ledger/journal-entry-workbench/drafts` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:2043` |
| `POST` | `/api/ledger/journal-entry-workbench/evidence` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:2193` |
| `POST` | `/api/ledger/journal-entry-workbench/lifecycle-action` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:2236` |
| `POST` | `/api/ledger/journal-entry-workbench/submit-approval` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:2148` |
| `POST` | `/api/ledger/journal-entry-workbench/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:2102` |
| `GET` | `/api/ledger/periods` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:151` |
| `POST` | `/api/ledger/periods` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:189` |
| `POST` | `/api/ledger/periods/{periodId:guid}/close` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:221` |
| `GET` | `/api/ledger/periods/{periodId:guid}/journal-entries` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:273` |
| `GET` | `/api/ledger/periods/{periodId:guid}/pnl-summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:416` |
| `GET` | `/api/ledger/periods/{periodId:guid}/trial-balance` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:366` |
| `GET` | `/api/ledger/periods/{periodId:guid}/trial-balance-report` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:391` |
| `GET` | `/api/ledger/private-capital/activity` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1748` |
| `GET` | `/api/ledger/private-capital/capital-account-subledger` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1864` |
| `GET` | `/api/ledger/private-capital/capital-account-workbench` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1996` |
| `GET` | `/api/ledger/private-capital/fund-event-command-center` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1823` |
| `GET` | `/api/ledger/private-capital/fund-event-record` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1780` |
| `GET` | `/api/ledger/private-capital/report-output` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1925` |
| `POST` | `/api/ledger/reports/accounting-package` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1535` |
| `POST` | `/api/ledger/reports/accounting-package/certification` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1586` |
| `GET` | `/api/ledger/reports/accounting-packages` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1639` |
| `GET` | `/api/ledger/reports/accounting-packages/{packageId}/exports/{artifactId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1676` |
| `GET` | `/api/ledger/reports/pnl-summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:500` |
| `GET` | `/api/ledger/reports/trial-balance` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:441` |
| `GET` | `/api/loans/portfolio` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1094` |
| `POST` | `/api/loans/rebuild-all` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1105` |
| `GET` | `/api/loans/rebuild-checkpoints` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1085` |
| `POST` | `/api/maintenance/execute` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:129` |
| `GET` | `/api/maintenance/executions` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:168` |
| `POST` | `/api/maintenance/executions/cleanup` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:407` |
| `GET` | `/api/maintenance/executions/failed` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:210` |
| `GET` | `/api/maintenance/executions/{executionId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:181` |
| `POST` | `/api/maintenance/executions/{executionId}/cancel` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:151` |
| `GET` | `/api/maintenance/presets` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:330` |
| `GET` | `/api/maintenance/schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:20` |
| `POST` | `/api/maintenance/schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:35` |
| `GET` | `/api/maintenance/schedules/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:225` |
| `GET` | `/api/maintenance/schedules/{id}` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:49` |
| `DELETE` | `/api/maintenance/schedules/{id}/delete` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:59` |
| `POST` | `/api/maintenance/schedules/{id}/disable` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:87` |
| `POST` | `/api/maintenance/schedules/{id}/enable` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:73` |
| `GET` | `/api/maintenance/schedules/{id}/history` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:123` |
| `POST` | `/api/maintenance/schedules/{id}/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:101` |
| `DELETE` | `/api/maintenance/schedules/{scheduleId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:90` |
| `PUT` | `/api/maintenance/schedules/{scheduleId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:36` |
| `GET` | `/api/maintenance/schedules/{scheduleId}/executions` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:196` |
| `GET` | `/api/maintenance/schedules/{scheduleId}/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:236` |
| `POST` | `/api/maintenance/schedules/{scheduleId}/trigger` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:109` |
| `GET` | `/api/maintenance/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:250` |
| `GET` | `/api/maintenance/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:277` |
| `GET` | `/api/maintenance/task-types` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:386` |
| `POST` | `/api/maintenance/validate-cron` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:290` |
| `GET` | `/api/messaging/activity` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:97` |
| `GET` | `/api/messaging/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:30` |
| `GET` | `/api/messaging/consumers` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:126` |
| `GET` | `/api/messaging/endpoints` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:145` |
| `GET` | `/api/messaging/errors` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:306` |
| `POST` | `/api/messaging/errors/{messageId}/retry` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:327` |
| `GET` | `/api/messaging/publishing` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:264` |
| `POST` | `/api/messaging/queues/{queueName}/purge` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:281` |
| `GET` | `/api/messaging/stats` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:69` |
| `GET` | `/api/messaging/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:51` |
| `POST` | `/api/messaging/test` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:172` |
| `GET` | `/api/options/chains/{underlyingSymbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:77` |
| `GET` | `/api/options/expirations/{underlyingSymbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:27` |
| `GET` | `/api/options/quotes/{underlyingSymbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:135` |
| `POST` | `/api/options/refresh` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:201` |
| `GET` | `/api/options/strikes/{underlyingSymbol}/{expiration}` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:50` |
| `GET` | `/api/options/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:155` |
| `GET` | `/api/options/underlyings` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:183` |
| `GET` | `/api/packaging/contents` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:170` |
| `POST` | `/api/packaging/create` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:33` |
| `GET` | `/api/packaging/download/{fileName}` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:284` |
| `POST` | `/api/packaging/import` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:87` |
| `GET` | `/api/packaging/list` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:202` |
| `POST` | `/api/packaging/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:136` |
| `DELETE` | `/api/packaging/{fileName}` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:246` |
| `GET` | `/api/plaid/accounts` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:31` |
| `GET` | `/api/plaid/institutions/search` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:44` |
| `GET` | `/api/plaid/items` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:18` |
| `POST` | `/api/plaid/items/{itemId}/sync` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:124` |
| `POST` | `/api/plaid/link-token` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:74` |
| `POST` | `/api/plaid/public-token/exchange` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:99` |
| `POST` | `/api/plaid/transfers/sandbox` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:176` |
| `POST` | `/api/plaid/webhook` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:154` |
| `GET` | `/api/portfolio/household` | Documented | `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs:259` |
| `GET` | `/api/projections/{projectionRunId:guid}/flows` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:923` |
| `GET` | `/api/provider-routing/bindings` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:36` |
| `GET` | `/api/provider-routing/connections` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:20` |
| `POST` | `/api/provider-routing/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:68` |
| `GET` | `/api/provider-routing/trust-snapshots` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:52` |
| `GET` | `/api/providers/capabilities` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:143` |
| `GET` | `/api/providers/capability-matrix` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:177` |
| `GET` | `/api/providers/catalog` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:560` |
| `GET` | `/api/providers/catalog/{providerId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:606` |
| `GET` | `/api/providers/comparison` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:280` |
| `POST` | `/api/providers/configure` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:257` |
| `GET` | `/api/providers/connections` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:19` |
| `GET` | `/api/providers/dashboard` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:301` |
| `GET` | `/api/providers/failover` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:56` |
| `GET` | `/api/providers/failover-thresholds` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:226` |
| `POST` | `/api/providers/failover/reset` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:98` |
| `POST` | `/api/providers/failover/trigger` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:82` |
| `GET` | `/api/providers/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:243` |
| `GET` | `/api/providers/ib/error-codes` | Documented | `src/Meridian.Ui.Shared/Endpoints/IBEndpoints.cs:91` |
| `GET` | `/api/providers/ib/limits` | Documented | `src/Meridian.Ui.Shared/Endpoints/IBEndpoints.cs:113` |
| `GET` | `/api/providers/ib/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/IBEndpoints.cs:24` |
| `GET` | `/api/providers/latency` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:130` |
| `GET` | `/api/providers/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:476` |
| `GET` | `/api/providers/modules` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:23` |
| `POST` | `/api/providers/modules` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:56` |
| `GET` | `/api/providers/modules/catalogue` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:40` |
| `DELETE` | `/api/providers/modules/{moduleId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:110` |
| `PUT` | `/api/providers/modules/{moduleId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:82` |
| `PUT` | `/api/providers/modules/{moduleId}/enabled` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:135` |
| `POST` | `/api/providers/modules/{moduleId}/test` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:158` |
| `GET` | `/api/providers/rate-limits` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:113` |
| `GET` | `/api/providers/readiness` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:351` |
| `POST` | `/api/providers/restart` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:192` |
| `GET` | `/api/providers/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:363` |
| `POST` | `/api/providers/switch` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:162` |
| `DELETE` | `/api/providers/{providerId}/credentials` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:87` |
| `PUT` | `/api/providers/{providerId}/credentials` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:34` |
| `POST` | `/api/providers/{providerId}/verify` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:63` |
| `GET` | `/api/providers/{providerName}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:26` |
| `GET` | `/api/providers/{providerName}/rate-limit-history` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:126` |
| `POST` | `/api/providers/{providerName}/test` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:189` |
| `POST` | `/api/providers/{provider}/test-connection` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderCredentialEndpoints.cs:69` |
| `POST` | `/api/providers/{provider}/validate-credentials` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderCredentialEndpoints.cs:26` |
| `GET` | `/api/quality/anomalies` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:251` |
| `GET` | `/api/quality/anomalies/stale` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:295` |
| `GET` | `/api/quality/anomalies/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:292` |
| `GET` | `/api/quality/anomalies/unacknowledged` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:280` |
| `POST` | `/api/quality/anomalies/{anomalyId}/acknowledge` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:283` |
| `GET` | `/api/quality/anomalies/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:277` |
| `GET` | `/api/quality/comparison/discrepancies` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:330` |
| `GET` | `/api/quality/comparison/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:342` |
| `GET` | `/api/quality/comparison/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:323` |
| `GET` | `/api/quality/completeness` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:93` |
| `GET` | `/api/quality/completeness/low` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:118` |
| `GET` | `/api/quality/completeness/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:115` |
| `GET` | `/api/quality/completeness/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:100` |
| `GET` | `/api/quality/dashboard` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:77` |
| `GET` | `/api/quality/drops` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:289` |
| `GET` | `/api/quality/drops/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:314` |
| `GET` | `/api/quality/errors` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:224` |
| `GET` | `/api/quality/errors/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:243` |
| `GET` | `/api/quality/errors/top-symbols` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:246` |
| `GET` | `/api/quality/errors/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:236` |
| `GET` | `/api/quality/gaps` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:127` |
| `GET` | `/api/quality/gaps/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:215` |
| `GET` | `/api/quality/gaps/timeline/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:207` |
| `GET` | `/api/quality/gaps/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:139` |
| `POST` | `/api/quality/gaps/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:146` |
| `GET` | `/api/quality/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:389` |
| `GET` | `/api/quality/health/unhealthy` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:422` |
| `GET` | `/api/quality/health/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:413` |
| `GET` | `/api/quality/latency` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:300` |
| `GET` | `/api/quality/latency/high` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:318` |
| `GET` | `/api/quality/latency/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:315` |
| `GET` | `/api/quality/latency/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:303` |
| `GET` | `/api/quality/latency/{symbol}/histogram` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:312` |
| `GET` | `/api/quality/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:88` |
| `GET` | `/api/quality/reports/daily` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:347` |
| `POST` | `/api/quality/reports/export` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:374` |
| `GET` | `/api/quality/reports/weekly` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:355` |
| `POST` | `/api/quant/parameters` | Documented | `src/Meridian.Ui.Shared/Endpoints/QuantLabEndpoints.cs:87` |
| `POST` | `/api/quant/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/QuantLabEndpoints.cs:35` |
| `GET` | `/api/quant/templates` | Documented | `src/Meridian.Ui.Shared/Endpoints/QuantLabEndpoints.cs:123` |
| `GET` | `/api/reconciliation/exceptions` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1001` |
| `POST` | `/api/reconciliation/exceptions/{exceptionId:guid}/resolve` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1010` |
| `GET` | `/api/reconciliation/{runId:guid}/results` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:992` |
| `GET` | `/api/reference-data/bonds/issuer-ladder` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:63` |
| `GET` | `/api/reference-data/bonds/maturity-ladder` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:87` |
| `GET` | `/api/reference-data/bonds/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:24` |
| `GET` | `/api/reference-data/bonds/{securityId:guid}/accrual-convention` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:50` |
| `GET` | `/api/reference-data/bonds/{securityId:guid}/lifecycle` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:37` |
| `GET` | `/api/reference-data/certificates-of-deposit/by-issuer` | Documented | `src/Meridian.Ui.Shared/Endpoints/CertificateOfDepositReferenceEndpoints.cs:29` |
| `GET` | `/api/reference-data/certificates-of-deposit/maturing-before` | Documented | `src/Meridian.Ui.Shared/Endpoints/CertificateOfDepositReferenceEndpoints.cs:40` |
| `GET` | `/api/reference-data/certificates-of-deposit/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/CertificateOfDepositReferenceEndpoints.cs:17` |
| `GET` | `/api/reference-data/commodities/by-exchange` | Documented | `src/Meridian.Ui.Shared/Endpoints/CommodityReferenceEndpoints.cs:40` |
| `GET` | `/api/reference-data/commodities/by-type` | Documented | `src/Meridian.Ui.Shared/Endpoints/CommodityReferenceEndpoints.cs:29` |
| `GET` | `/api/reference-data/commodities/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/CommodityReferenceEndpoints.cs:17` |
| `GET` | `/api/reference-data/crypto/by-base-currency` | Documented | `src/Meridian.Ui.Shared/Endpoints/CryptoReferenceEndpoints.cs:48` |
| `GET` | `/api/reference-data/crypto/by-network` | Documented | `src/Meridian.Ui.Shared/Endpoints/CryptoReferenceEndpoints.cs:35` |
| `GET` | `/api/reference-data/crypto/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/CryptoReferenceEndpoints.cs:21` |
| `GET` | `/api/reference-data/deposits/by-institution` | Documented | `src/Meridian.Ui.Shared/Endpoints/DepositReferenceEndpoints.cs:35` |
| `GET` | `/api/reference-data/deposits/maturing-before` | Documented | `src/Meridian.Ui.Shared/Endpoints/DepositReferenceEndpoints.cs:48` |
| `GET` | `/api/reference-data/deposits/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DepositReferenceEndpoints.cs:21` |
| `GET` | `/api/reference-data/edgar/facts/{cik}` | Documented | `src/Meridian.Ui.Shared/Endpoints/EdgarReferenceDataEndpoints.cs:63` |
| `GET` | `/api/reference-data/edgar/filers/{cik}` | Documented | `src/Meridian.Ui.Shared/Endpoints/EdgarReferenceDataEndpoints.cs:49` |
| `GET` | `/api/reference-data/edgar/security-data/{cik}` | Documented | `src/Meridian.Ui.Shared/Endpoints/EdgarReferenceDataEndpoints.cs:77` |
| `GET` | `/api/reference-data/equities/by-exchange` | Documented | `src/Meridian.Ui.Shared/Endpoints/EquityReferenceEndpoints.cs:29` |
| `GET` | `/api/reference-data/equities/by-issuer` | Documented | `src/Meridian.Ui.Shared/Endpoints/EquityReferenceEndpoints.cs:40` |
| `GET` | `/api/reference-data/equities/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/EquityReferenceEndpoints.cs:17` |
| `GET` | `/api/reference-data/futures/by-root` | Documented | `src/Meridian.Ui.Shared/Endpoints/FutureReferenceEndpoints.cs:35` |
| `GET` | `/api/reference-data/futures/expiry-ladder` | Documented | `src/Meridian.Ui.Shared/Endpoints/FutureReferenceEndpoints.cs:48` |
| `GET` | `/api/reference-data/futures/front-month` | Documented | `src/Meridian.Ui.Shared/Endpoints/FutureReferenceEndpoints.cs:61` |
| `GET` | `/api/reference-data/futures/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/FutureReferenceEndpoints.cs:21` |
| `GET` | `/api/reference-data/fxspot/by-currency` | Documented | `src/Meridian.Ui.Shared/Endpoints/FxSpotReferenceEndpoints.cs:41` |
| `GET` | `/api/reference-data/fxspot/pairs/{pairCode}` | Documented | `src/Meridian.Ui.Shared/Endpoints/FxSpotReferenceEndpoints.cs:29` |
| `GET` | `/api/reference-data/fxspot/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/FxSpotReferenceEndpoints.cs:17` |
| `GET` | `/api/reference-data/money-market-funds/by-family` | Documented | `src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundReferenceEndpoints.cs:29` |
| `GET` | `/api/reference-data/money-market-funds/by-sweep-eligibility` | Documented | `src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundReferenceEndpoints.cs:40` |
| `GET` | `/api/reference-data/money-market-funds/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundReferenceEndpoints.cs:17` |
| `POST` | `/api/reference-data/options/chains/import` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionChainEndpoints.cs:19` |
| `GET` | `/api/reference-data/options/chains/snapshot` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionChainEndpoints.cs:44` |
| `GET` | `/api/reference-data/options/contracts/{contractSymbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionReferenceEndpoints.cs:17` |
| `GET` | `/api/reference-data/options/contracts/{contractSymbol}/underlying-linkage` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionReferenceEndpoints.cs:42` |
| `GET` | `/api/reference-data/options/expiry-ladder` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionReferenceEndpoints.cs:54` |
| `GET` | `/api/reference-data/options/series/{optionChainId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionReferenceEndpoints.cs:29` |
| `GET` | `/api/reference-data/swaps/by-type` | Documented | `src/Meridian.Ui.Shared/Endpoints/SwapReferenceEndpoints.cs:29` |
| `GET` | `/api/reference-data/swaps/maturing-before` | Documented | `src/Meridian.Ui.Shared/Endpoints/SwapReferenceEndpoints.cs:40` |
| `GET` | `/api/reference-data/swaps/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SwapReferenceEndpoints.cs:17` |
| `GET` | `/api/replay/files` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:28` |
| `GET` | `/api/replay/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:196` |
| `POST` | `/api/replay/start` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:68` |
| `GET` | `/api/replay/stats` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:276` |
| `POST` | `/api/replay/{sessionId}/pause` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:100` |
| `POST` | `/api/replay/{sessionId}/resume` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:114` |
| `POST` | `/api/replay/{sessionId}/seek` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:142` |
| `POST` | `/api/replay/{sessionId}/speed` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:155` |
| `GET` | `/api/replay/{sessionId}/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:169` |
| `POST` | `/api/replay/{sessionId}/stop` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:128` |
| `GET` | `/api/resilience/circuit-breakers` | Documented | `src/Meridian.Ui.Shared/Endpoints/ResilienceEndpoints.cs:25` |
| `POST` | `/api/sampling/create` | Documented | `src/Meridian.Ui.Shared/Endpoints/SamplingEndpoints.cs:24` |
| `GET` | `/api/sampling/estimate` | Documented | `src/Meridian.Ui.Shared/Endpoints/SamplingEndpoints.cs:111` |
| `GET` | `/api/sampling/saved` | Documented | `src/Meridian.Ui.Shared/Endpoints/SamplingEndpoints.cs:153` |
| `GET` | `/api/sampling/{sampleId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SamplingEndpoints.cs:177` |
| `POST` | `/api/schedules/cron/next-runs` | Documented | `src/Meridian.Ui.Shared/Endpoints/CronEndpoints.cs:42` |
| `POST` | `/api/schedules/cron/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/CronEndpoints.cs:20` |
| `POST` | `/api/security-master` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:343` |
| `POST` | `/api/security-master/aliases/upsert` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:427` |
| `POST` | `/api/security-master/amend` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:371` |
| `GET` | `/api/security-master/asset-profiles` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:77` |
| `POST` | `/api/security-master/asset-profiles/approve` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:161` |
| `POST` | `/api/security-master/asset-profiles/drafts` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:120` |
| `GET` | `/api/security-master/asset-profiles/promotion-candidates` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:92` |
| `POST` | `/api/security-master/asset-profiles/rollback` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:202` |
| `GET` | `/api/security-master/asset-profiles/{profileId}/lineage` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:104` |
| `GET` | `/api/security-master/conflicts` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:757` |
| `POST` | `/api/security-master/conflicts/{conflictId:guid}/resolve` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:777` |
| `GET` | `/api/security-master/corporate-actions/inbox` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:682` |
| `POST` | `/api/security-master/corporate-actions/inbox/apply` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:692` |
| `POST` | `/api/security-master/corporate-actions/ingest` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:653` |
| `GET` | `/api/security-master/coverage/draft/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:741` |
| `GET` | `/api/security-master/data-entitlements` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1303` |
| `POST` | `/api/security-master/data-entitlements` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1325` |
| `GET` | `/api/security-master/data-entitlements/expiring` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1313` |
| `DELETE` | `/api/security-master/data-entitlements/{entitlementId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1353` |
| `POST` | `/api/security-master/deactivate` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:399` |
| `GET` | `/api/security-master/exceptions/aging` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1419` |
| `POST` | `/api/security-master/import` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:824` |
| `POST` | `/api/security-master/ingest/edgar` | Documented | `src/Meridian.Ui.Shared/Endpoints/EdgarReferenceDataEndpoints.cs:24` |
| `GET` | `/api/security-master/ingest/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:855` |
| `GET` | `/api/security-master/quality-report/latest` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1403` |
| `POST` | `/api/security-master/quality-report/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1385` |
| `POST` | `/api/security-master/resolve` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:248` |
| `POST` | `/api/security-master/search` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:285` |
| `GET` | `/api/security-master/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:42` |
| `GET` | `/api/security-master/{securityId:guid}/cashflow-projections` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1280` |
| `GET` | `/api/security-master/{securityId:guid}/cashflow-source` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1239` |
| `PUT` | `/api/security-master/{securityId:guid}/cashflow-source` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1252` |
| `GET` | `/api/security-master/{securityId:guid}/convertible-equity-terms` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:535` |
| `PATCH` | `/api/security-master/{securityId:guid}/convertible-equity-terms` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:556` |
| `GET` | `/api/security-master/{securityId:guid}/corporate-actions` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:593` |
| `POST` | `/api/security-master/{securityId:guid}/corporate-actions` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:614` |
| `GET` | `/api/security-master/{securityId:guid}/history` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:316` |
| `GET` | `/api/security-master/{securityId:guid}/operator-overrides` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:876` |
| `PATCH` | `/api/security-master/{securityId:guid}/operator-overrides` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:900` |
| `POST` | `/api/security-master/{securityId:guid}/operator-overrides/decision` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:935` |
| `GET` | `/api/security-master/{securityId:guid}/preferred-equity-terms` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:478` |
| `PATCH` | `/api/security-master/{securityId:guid}/preferred-equity-terms` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:499` |
| `GET` | `/api/security-master/{securityId:guid}/price-comparison` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1222` |
| `GET` | `/api/security-master/{securityId:guid}/price-golden-copy` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1208` |
| `GET` | `/api/security-master/{securityId:guid}/pricing-hierarchy` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1134` |
| `PUT` | `/api/security-master/{securityId:guid}/pricing-hierarchy` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1148` |
| `POST` | `/api/security-master/{securityId:guid}/raw-price` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1178` |
| `GET` | `/api/security-master/{securityId:guid}/trading-parameters` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:455` |
| `GET` | `/api/security-master/{securityId:guid}/validation` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:63` |
| `POST` | `/api/servicer-reports` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1032` |
| `GET` | `/api/servicer-reports/{batchId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1059` |
| `GET` | `/api/servicer-reports/{batchId:guid}/position-lines` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1067` |
| `GET` | `/api/servicer-reports/{batchId:guid}/transaction-lines` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1076` |
| `GET` | `/api/sla/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:641` |
| `GET` | `/api/sla/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:667` |
| `GET` | `/api/sla/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:613` |
| `GET` | `/api/sla/status/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:616` |
| `GET` | `/api/sla/violations` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:625` |
| `GET` | `/api/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:97` |
| `GET` | `/api/storage/archive/stats` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:309` |
| `GET` | `/api/storage/breakdown` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:79` |
| `GET` | `/api/storage/capacity-forecast` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:536` |
| `GET` | `/api/storage/catalog` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:343` |
| `POST` | `/api/storage/cleanup` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:274` |
| `GET` | `/api/storage/cleanup/candidates` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:239` |
| `POST` | `/api/storage/convert-parquet` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:512` |
| `GET` | `/api/storage/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:207` |
| `GET` | `/api/storage/health/check` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:401` |
| `GET` | `/api/storage/health/orphans` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:418` |
| `POST` | `/api/storage/maintenance/defrag` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:495` |
| `GET` | `/api/storage/profiles` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:24` |
| `GET` | `/api/storage/quality/alerts` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:103` |
| `POST` | `/api/storage/quality/alerts/{alertId}/acknowledge` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:123` |
| `GET` | `/api/storage/quality/anomalies` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:198` |
| `POST` | `/api/storage/quality/check` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:229` |
| `GET` | `/api/storage/quality/rankings/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:133` |
| `GET` | `/api/storage/quality/scores` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:54` |
| `GET` | `/api/storage/quality/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:25` |
| `GET` | `/api/storage/quality/symbol/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:81` |
| `GET` | `/api/storage/quality/trends` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:160` |
| `GET` | `/api/storage/search/files` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:360` |
| `GET` | `/api/storage/stats` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:36` |
| `GET` | `/api/storage/symbol/{symbol}/files` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:157` |
| `GET` | `/api/storage/symbol/{symbol}/info` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:105` |
| `GET` | `/api/storage/symbol/{symbol}/path` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:184` |
| `GET` | `/api/storage/symbol/{symbol}/stats` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:130` |
| `POST` | `/api/storage/tiers/migrate` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:434` |
| `GET` | `/api/storage/tiers/plan` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:476` |
| `GET` | `/api/storage/tiers/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:460` |
| `POST` | `/api/strategies/covered-call/chain-preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:159` |
| `GET` | `/api/strategies/covered-call/runs` | Documented | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:53` |
| `POST` | `/api/strategies/covered-call/runs` | Documented | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:25` |
| `POST` | `/api/strategies/covered-call/runs/{runId}/cancel` | Documented | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:136` |
| `GET` | `/api/strategies/covered-call/runs/{runId}/result` | Documented | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:94` |
| `GET` | `/api/strategies/covered-call/runs/{runId}/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:71` |
| `GET` | `/api/strategies/runs/compare` | Documented | `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs:2763` |
| `GET` | `/api/strategies/{strategyId}/runs` | Documented | `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs:2642` |
| `GET` | `/api/subscriptions/active` | Documented | `src/Meridian.Ui.Shared/Endpoints/SubscriptionEndpoints.cs:21` |
| `POST` | `/api/subscriptions/subscribe` | Documented | `src/Meridian.Ui.Shared/Endpoints/SubscriptionEndpoints.cs:43` |
| `POST` | `/api/subscriptions/unsubscribe/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SubscriptionEndpoints.cs:72` |
| `GET` | `/api/symbols` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:31` |
| `POST` | `/api/symbols/add` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:138` |
| `GET` | `/api/symbols/archived` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:79` |
| `POST` | `/api/symbols/batch` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:387` |
| `POST` | `/api/symbols/bulk-add` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:302` |
| `POST` | `/api/symbols/bulk-remove` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:342` |
| `POST` | `/api/symbols/create` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:435` |
| `GET` | `/api/symbols/mappings` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolMappingEndpoints.cs:70` |
| `POST` | `/api/symbols/mappings` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolMappingEndpoints.cs:77` |
| `GET` | `/api/symbols/monitored` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:56` |
| `GET` | `/api/symbols/registry` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolMappingEndpoints.cs:29` |
| `GET` | `/api/symbols/search` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:363` |
| `GET` | `/api/symbols/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:232` |
| `POST` | `/api/symbols/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:264` |
| `DELETE` | `/api/symbols/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:525` |
| `POST` | `/api/symbols/{symbol}/archive` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:283` |
| `GET` | `/api/symbols/{symbol}/depth` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:207` |
| `POST` | `/api/symbols/{symbol}/remove` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:162` |
| `GET` | `/api/symbols/{symbol}/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:98` |
| `GET` | `/api/symbols/{symbol}/trades` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:182` |
| `POST` | `/api/symbols/{symbol}/update` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:475` |
| `GET` | `/api/system/lifecycle` | Documented | `src/Meridian/UiServer.cs:381` |
| `POST` | `/api/system/shutdown` | Documented | `src/Meridian/UiServer.cs:403` |
| `POST` | `/api/workstation/desktop/launch` | Documented | `src/Meridian.Ui.Shared/Endpoints/FirstRunEndpoints.cs:27` |
| `GET` | `/health` | Documented | `src/Meridian.Application/Composition/HostAdapters.cs:60` |
| `GET` | `/live` | Documented | `src/Meridian.Application/Composition/HostAdapters.cs:73` |
| `GET` | `/ready` | Documented | `src/Meridian.Application/Composition/HostAdapters.cs:72` |

## Workstation Contract Coverage

| Contract | Status | Source |
|---|---|---|
| `AccrualCalculationResultDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:191` |
| `AccrualInputSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:173` |
| `ActivationOutcomeDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:27` |
| `AlpacaBrokerageConnectionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:102` |
| `ApprovalDecision` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:30` |
| `ApprovalPolicy` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:22` |
| `ApprovalStep` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:21` |
| `ApproveSecurityMasterOverrides` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:13` |
| `ApproveSecurityMasterRevisionRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:79` |
| `ApproveWorkflow` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:20` |
| `AuditTrailExplorerQueryDto` | Documented | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:8` |
| `AuditTrailExplorerResultDto` | Documented | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:54` |
| `AuditTrailObjectKindDto` | Documented | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:64` |
| `AuditTrailTimelineEntryDto` | Documented | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:27` |
| `AutomatedJournalCapitalAccountReconciliationDto` | Documented | `src/Meridian.Contracts/Workstation/AutomatedJournalScheduleDtos.cs:25` |
| `AutomatedJournalScheduleStateDto` | Documented | `src/Meridian.Contracts/Workstation/AutomatedJournalScheduleDtos.cs:8` |
| `AutomatedJournalScheduleStatusDto` | Documented | `src/Meridian.Contracts/Workstation/AutomatedJournalScheduleDtos.cs:51` |
| `BankAccountSnapshot` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:102` |
| `BankStatementImportResultDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:74` |
| `BooksBeforeBrokerReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:62` |
| `BrokerageAccountKindDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:17` |
| `BrokerageAccountLinkRequestDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:60` |
| `BrokerageCashFlowEntryDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:129` |
| `BrokerageCashFlowSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:139` |
| `BrokerageConnectionStateDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:26` |
| `BrokerageConnectionStatusDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:85` |
| `BrokerageHouseholdAccountDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:154` |
| `BrokerageHouseholdPortfolioDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:187` |
| `BrokerageHouseholdPositionDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:170` |
| `BrokeragePortfolioPerformanceDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:113` |
| `BrokeragePortfolioPerformancePointDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:107` |
| `BuildLedgerDraft` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:14` |
| `BulkResolveSecurityMasterConflictsRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:577` |
| `BulkResolveSecurityMasterConflictsResult` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:583` |
| `CanonicalizationAssuranceDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:109` |
| `CanonicalizationProviderSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:119` |
| `CashFinancingSummary` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:140` |
| `CashFlowEntryDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:713` |
| `CashFlowProjectionPoint` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:34` |
| `CashForecastResult` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:36` |
| `CashLadderBucketDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:723` |
| `CashSyncSourceAvailability` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:2` |
| `CashSyncWindow` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:5` |
| `CloseWorkflow` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:22` |
| `ClosedLotSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:905` |
| `CollateralCallDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:26` |
| `CompleteActivationOutcomeRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:43` |
| `CompleteFirstRunRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:37` |
| `CorporateActionDescriptorDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:82` |
| `CorporateActionTimelineEntryDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:96` |
| `CounterpartyExposureDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:10` |
| `CouponEvent` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:20` |
| `CrossFundReportingConsolidationDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:323` |
| `CrossFundReportingConsolidationScopeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:313` |
| `DailyValuationBatchLifecycleRequestDto` | Documented | `src/Meridian.Contracts/Workstation/DailyValuationScheduleDtos.cs:54` |
| `DailyValuationBatchLifecycleResultDto` | Documented | `src/Meridian.Contracts/Workstation/DailyValuationScheduleDtos.cs:67` |
| `DailyValuationScheduleStateDto` | Documented | `src/Meridian.Contracts/Workstation/DailyValuationScheduleDtos.cs:7` |
| `DailyValuationScheduleStatusDto` | Documented | `src/Meridian.Contracts/Workstation/DailyValuationScheduleDtos.cs:22` |
| `DataUploadPreviewResultDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:53` |
| `DataUploadTemplateCatalogDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:12` |
| `DataUploadTemplateDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:24` |
| `DataUploadTemplateFieldDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:43` |
| `DataUploadValidationIssueDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:134` |
| `DataUploadWorkbookPreviewResultDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:111` |
| `DataUploadWorkbookSheetPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:94` |
| `DeltaOutlierResult` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:15` |
| `EquityCurvePoint` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:650` |
| `EquityCurveSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:659` |
| `EvidenceArtifactCaptureDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:40` |
| `EvidenceArtifactExtractionFieldDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:49` |
| `EvidenceArtifactRefDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:29` |
| `EvidenceArtifactRefDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:278` |
| `EvidenceAssuranceComponentDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:343` |
| `EvidenceCompletenessDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:625` |
| `EvidenceCompletenessSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:151` |
| `EvidenceDocumentAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:152` |
| `EvidenceDocumentAuthorityDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:159` |
| `EvidenceDocumentClassificationDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:62` |
| `EvidenceDocumentConfirmedFieldDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:144` |
| `EvidenceDocumentDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:192` |
| `EvidenceDocumentExtractionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:245` |
| `EvidenceDocumentExtractionResultDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:253` |
| `EvidenceDocumentIntakeChannelDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:80` |
| `EvidenceDocumentIntakeSourceDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:271` |
| `EvidenceDocumentIntakeSourceKindDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:261` |
| `EvidenceDocumentLinkDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:128` |
| `EvidenceDocumentLinkKindDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:104` |
| `EvidenceDocumentReviewStateDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:135` |
| `EvidenceDocumentReviewStatusDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:121` |
| `EvidenceDocumentSourceRecordDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:170` |
| `EvidenceEdgeDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:300` |
| `EvidenceEndpointErrorDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:617` |
| `EvidenceExtractionStatusDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:93` |
| `EvidenceFreshnessDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:24` |
| `EvidenceGraphDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:659` |
| `EvidenceLifecycleMetadataDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:591` |
| `EvidenceManifestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:232` |
| `EvidenceManifestPackageKindDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:222` |
| `EvidenceNodeDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:284` |
| `EvidenceNodeDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:295` |
| `EvidencePacketDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:647` |
| `EvidencePacketExportRequest` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:681` |
| `EvidencePacketExportResponse` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:690` |
| `EvidenceProofChainDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:384` |
| `EvidenceProofChainLayerDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:371` |
| `EvidenceProofChainLayerKindDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:358` |
| `EvidenceRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:182` |
| `EvidenceRequestListDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:460` |
| `EvidenceRequestListKindDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:450` |
| `EvidenceSlaAssessmentDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:332` |
| `EvidenceSlaPolicyDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:323` |
| `EvidenceStatusDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:6` |
| `EvidenceSubjectDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:15` |
| `EvidenceSubjectLinkageDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:597` |
| `EvidenceSupportRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:436` |
| `EvidenceTemplateDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:674` |
| `EvidenceTemplateExportSettingsDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:669` |
| `EvidenceValidationIssueDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:314` |
| `EvidenceValidationSeverityDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:308` |
| `EvidenceVaultArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:419` |
| `EvidenceVaultDocumentEntryDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:522` |
| `EvidenceVaultDocumentQueryDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:509` |
| `EvidenceVaultDocumentReviewRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:533` |
| `EvidenceVaultDocumentReviewResponseDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:543` |
| `EvidenceVaultIdentityDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:401` |
| `EvidenceVaultIntakeRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:547` |
| `EvidenceVaultIntakeResponseDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:574` |
| `EvidenceVaultLookupRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:607` |
| `EvidenceVaultRequestListEntryDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:486` |
| `EvidenceVaultRequestListQueryDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:476` |
| `ExpectedAccountingEventDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:207` |
| `ExpectedAccountingEventKindDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:152` |
| `ExpectedJournalPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:242` |
| `ExpectedJournalPreviewLineDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:232` |
| `ExposureSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:2` |
| `ExposureTrendPointDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:43` |
| `FeatureCapabilitySettingsResponse` | Documented | `src/Meridian.Contracts/Workstation/FeatureCapabilityDtos.cs:2` |
| `FeatureCapabilityToggleDto` | Documented | `src/Meridian.Contracts/Workstation/FeatureCapabilityDtos.cs:5` |
| `FeatureCapabilityToggleRequest` | Documented | `src/Meridian.Contracts/Workstation/FeatureCapabilityDtos.cs:16` |
| `FinancialOperationsCloseSupportDecisionDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialOperationsCommandCenterDtos.cs:21` |
| `FinancialOperationsCloseSupportDecisionRowDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialOperationsCommandCenterDtos.cs:34` |
| `FinancialOperationsCommandCenterDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialOperationsCommandCenterDtos.cs:2` |
| `FinancialOperationsCommandCenterMetricDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialOperationsCommandCenterDtos.cs:49` |
| `FinancialOperationsQueueRowDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialOperationsCommandCenterDtos.cs:57` |
| `FinancialRecordExplorerCellDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:71` |
| `FinancialRecordExplorerColumnDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:64` |
| `FinancialRecordExplorerDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:14` |
| `FinancialRecordExplorerFilterDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:57` |
| `FinancialRecordExplorerGraphEdgeDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:121` |
| `FinancialRecordExplorerGraphNodeDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:114` |
| `FinancialRecordExplorerProofActionDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:101` |
| `FinancialRecordExplorerQueryDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:31` |
| `FinancialRecordExplorerRecordGraphDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:110` |
| `FinancialRecordExplorerRelationshipDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:127` |
| `FinancialRecordExplorerRowDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:78` |
| `FinancialRecordExplorerSavedViewDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:41` |
| `FinancialRecordExplorerSavedViewSaveRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:134` |
| `FinancialRecordExplorerScopeItemDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:36` |
| `FinancialRecordExplorerSelectedRecordDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:88` |
| `FinancialRecordExplorerSummaryItemDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:51` |
| `FinancialRecordExplorerTone` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:6` |
| `FirstRunStatusDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:2` |
| `FundAccountBrokerageBalanceSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:198` |
| `FundAccountBrokerageCashTransactionDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:244` |
| `FundAccountBrokerageCorporateActionDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:253` |
| `FundAccountBrokerageFillDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:232` |
| `FundAccountBrokerageOrderDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:218` |
| `FundAccountBrokeragePositionDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:205` |
| `FundAccountBrokerageSyncActivityDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:265` |
| `FundAccountCloseReadinessActionDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:560` |
| `FundAccountCloseReadinessBlockerDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:552` |
| `FundAccountCloseReadinessComponentDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:538` |
| `FundAccountCloseReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:566` |
| `FundAccountCloseReadinessStatusDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:532` |
| `FundAccountSummary` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:73` |
| `FundAuditEntry` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:301` |
| `FundAuditEvidenceCategoryKeyDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:61` |
| `FundAuditEvidenceCategorySummaryDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:496` |
| `FundAuditPackReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:505` |
| `FundJournalLine` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:50` |
| `FundLedgerDimensionSnapshot` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:126` |
| `FundLedgerQuery` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:27` |
| `FundLedgerReconciliationSnapshot` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:135` |
| `FundLedgerScope` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:8` |
| `FundLedgerSliceDto` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:75` |
| `FundLedgerSnapshotBalanceLine` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:114` |
| `FundLedgerSummary` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:90` |
| `FundLedgerTotalsDto` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:63` |
| `FundNavAssetClassExposureDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:107` |
| `FundNavAttributionSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:114` |
| `FundOperationsNavigationContext` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:26` |
| `FundOperationsTab` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:8` |
| `FundOperationsWorkspaceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:460` |
| `FundOperationsWorkspaceQuery` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:96` |
| `FundPortfolioPosition` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:118` |
| `FundReconciliationItem` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:165` |
| `FundReportAssetClassSectionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:493` |
| `FundReportPackArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:551` |
| `FundReportPackEvidenceBundleApprovalDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:794` |
| `FundReportPackEvidenceBundleDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:802` |
| `FundReportPackEvidenceBundleSourceLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:785` |
| `FundReportPackGenerateRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:535` |
| `FundReportPackHistoryItemDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:762` |
| `FundReportPackLifecycleEventDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:720` |
| `FundReportPackLineagePointerDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:574` |
| `FundReportPackPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:518` |
| `FundReportPackPreviewRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:482` |
| `FundReportPackProvenanceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:562` |
| `FundReportPackSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:731` |
| `FundReportPackValidationIssueDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:703` |
| `FundReportingProfileDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:126` |
| `FundReportingSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:431` |
| `FundTrialBalanceLine` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:37` |
| `FundWorkflowCommandMetadata` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:7` |
| `FundWorkflowOverallStatus` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:2` |
| `FundWorkflowRejectionReasonCode` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:6` |
| `FundWorkflowStage` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:4` |
| `FundWorkflowState` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:24` |
| `FundWorkflowSubStatus` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:5` |
| `FundWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:42` |
| `FxConversionReference` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:32` |
| `GovernanceLifecycleProjectionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:201` |
| `GovernanceReportArtifactFormatDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:25` |
| `GovernanceReportKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:8` |
| `GovernanceReportPackStatusDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:35` |
| `GovernanceReportValidationSeverityDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:53` |
| `HaircutRuleDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:24` |
| `ImportBrokerData` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:10` |
| `IngestionCheckpointDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:44` |
| `IngestionOperationActionDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:61` |
| `IngestionOperationActionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:67` |
| `IngestionOperationActionResultDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:71` |
| `IngestionOperationDetailDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:38` |
| `IngestionOperationRowDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:20` |
| `IngestionOperationsSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:4` |
| `IngestionOperationsSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:10` |
| `IngestionSymbolProgressDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:51` |
| `InsightFeed` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:19` |
| `InsightWidget` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:6` |
| `InstrumentPassportClassificationProfileDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:523` |
| `InstrumentPassportDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:547` |
| `InstrumentPassportOperationsHandoffDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:411` |
| `InstrumentPassportOperationsReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:450` |
| `InstrumentPassportOperationsWorkbenchDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:426` |
| `InstrumentPassportOperationsWorkbenchItemDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:440` |
| `InstrumentPassportOperationsWorkbenchPanelDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:433` |
| `InstrumentPassportPricingDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:567` |
| `InstrumentPassportProviderConfidenceDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:382` |
| `InstrumentPassportReferenceDataWorkbenchDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:397` |
| `InstrumentPassportReferenceDataWorkbenchSectionDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:403` |
| `InvestmentAccountingPreviewModeDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:24` |
| `InvestmentAccountingReconciliationExpectationDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:54` |
| `InvestmentAccountingTradeSideDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:17` |
| `InvestmentAccountingTransactionKindDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:6` |
| `InvestmentAccountingTransactionLabPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:71` |
| `InvestmentAccountingTransactionLabRequestDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:29` |
| `InvestmentAccountingTrialBalanceImpactDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:47` |
| `LedgerAmountApprovalStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:660` |
| `LedgerAmountProvenanceDetailDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:684` |
| `LedgerAmountProvenanceEvidenceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:587` |
| `LedgerAmountReconciliationCaseDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:622` |
| `LedgerAmountReconciliationStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:653` |
| `LedgerAmountReportUsageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:666` |
| `LedgerAmountSecurityMasterLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:611` |
| `LedgerAmountStrategyRunLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:676` |
| `LedgerImpactPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:290` |
| `LedgerJournalLine` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:413` |
| `LedgerSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:364` |
| `LedgerTrialBalanceLine` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:392` |
| `MarginRequirementDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:22` |
| `MeridianAssuranceScoreDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:350` |
| `MetricsDiff` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:627` |
| `MultiAssetClassCoverageDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:930` |
| `MultiAssetCoverageSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:969` |
| `MultiAssetDrillThroughTargetDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:917` |
| `MultiAssetEvidenceRequirementDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:896` |
| `MultiAssetPackCoverageDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:945` |
| `MultiAssetReadinessBlockerDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:907` |
| `NormalizeBrokerTransactions` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:11` |
| `NullReportingRunNotifier` | Documented | `src/Meridian.Contracts/Workstation/IReportingRunNotifier.cs:15` |
| `OpenLotSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:892` |
| `OperationsAccountingRecordEvidenceCategoryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1015` |
| `OperationsAccountingRecordSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1005` |
| `OperationsActionOriginDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:461` |
| `OperationsApprovalDecisionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:671` |
| `OperationsApprovalDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1152` |
| `OperationsApprovalPolicyMatrixDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:730` |
| `OperationsApprovalPolicyMatrixRowDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:736` |
| `OperationsApprovalPolicyRuleAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:780` |
| `OperationsApprovalPolicyRuleUpsertRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:754` |
| `OperationsApprovalPolicyRuleUpsertResultDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:775` |
| `OperationsApprovalStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:101` |
| `OperationsAssignBreakCaseRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:648` |
| `OperationsBreakCaseDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1116` |
| `OperationsBrokerIntakeStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:51` |
| `OperationsChecklistAcknowledgeRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1059` |
| `OperationsChecklistControlApprovalDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:725` |
| `OperationsCloseCalendarDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:795` |
| `OperationsCloseCalendarItemAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:835` |
| `OperationsCloseCalendarItemDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:799` |
| `OperationsCloseCalendarItemUpsertRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:821` |
| `OperationsCloseCalendarItemUpsertResultDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:830` |
| `OperationsCloseChecklistTaskDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1024` |
| `OperationsClosePackagePublicationDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1041` |
| `OperationsCloseReadinessBlockerDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1195` |
| `OperationsCloseReadinessComponentDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1184` |
| `OperationsCloseReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1175` |
| `OperationsCloseWorkflowRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:692` |
| `OperationsContinuityCorrelationKeysDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1144` |
| `OperationsContinuityWorkflowDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:909` |
| `OperationsContinuityWorkflowSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:859` |
| `OperationsDashboardMetricDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:963` |
| `OperationsDashboardSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:947` |
| `OperationsEvidenceLinkDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1220` |
| `OperationsEvidencePackageSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:977` |
| `OperationsGateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1065` |
| `OperationsGateKeyDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:41` |
| `OperationsGatePostureRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:498` |
| `OperationsGateStatusDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:28` |
| `OperationsIssueCodeDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:439` |
| `OperationsJournalEntryMetadataDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:606` |
| `OperationsLedgerDraftRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:530` |
| `OperationsLedgerJournalCandidateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:568` |
| `OperationsLedgerJournalLineDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:590` |
| `OperationsLedgerPostRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:554` |
| `OperationsLedgerPostingStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:71` |
| `OperationsLedgerPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1162` |
| `OperationsLedgerValidationRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:544` |
| `OperationsNextActionDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1211` |
| `OperationsReconciliationLaneStatusDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:92` |
| `OperationsReconciliationLaneSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:994` |
| `OperationsReconciliationRunRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:621` |
| `OperationsReconciliationStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:81` |
| `OperationsRejectWorkflowRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:682` |
| `OperationsReopenWorkflowRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:712` |
| `OperationsReportPackReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1169` |
| `OperationsResolveBreakCaseRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:639` |
| `OperationsReviewedAutomationArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:892` |
| `OperationsReviewedAutomationSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:873` |
| `OperationsSecurityMasterOverrideApprovalRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:487` |
| `OperationsSecurityMasterResolveRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:518` |
| `OperationsSecurityMasterStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:61` |
| `OperationsStartWorkflowRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:468` |
| `OperationsSubmitApprovalRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:660` |
| `OperationsTimelineEntryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1076` |
| `OperationsTransitionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:479` |
| `OperationsTransitionResultDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:849` |
| `OperationsWorkflowAuditDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1096` |
| `OperationsWorkflowBlockerDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1203` |
| `OperationsWorkflowStatusDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:11` |
| `OperatorInboxDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:94` |
| `OperatorWorkItemDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:59` |
| `OperatorWorkItemKindDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:6` |
| `OperatorWorkItemToneDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:21` |
| `OperatorWorkflowHomeSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:6` |
| `ParameterDiff` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:621` |
| `PaymentInstruction` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:17` |
| `PilotAcceptanceEvidenceCategoryDto` | Documented | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:51` |
| `PilotAcceptanceEvidenceDto` | Documented | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:67` |
| `PilotAcceptanceEvidenceRoleDto` | Documented | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:62` |
| `PilotEvidenceGraphEdgeDto` | Documented | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:85` |
| `PilotReadinessArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:90` |
| `PilotReadinessStageDto` | Documented | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:6` |
| `PilotReadinessStageGateDto` | Documented | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:25` |
| `PilotReadinessStageStatusDto` | Documented | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:19` |
| `PilotW4AcceptanceEvaluationDto` | Documented | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:74` |
| `PortfolioLedgerDriftDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:48` |
| `PortfolioLedgerWorkflowStatusDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:40` |
| `PortfolioLedgerWorkflowStatusSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:53` |
| `PortfolioPositionSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:344` |
| `PortfolioReportingAnalyticsKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:273` |
| `PortfolioReportingAnalyticsRowDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:291` |
| `PortfolioReportingAnalyticsScopeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:281` |
| `PortfolioReportingCutDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:171` |
| `PortfolioReportingCutKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:152` |
| `PortfolioReportingLiveViewDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:210` |
| `PortfolioReportingLiveViewFreshnessPolicyDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:197` |
| `PortfolioReportingLiveViewStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:160` |
| `PortfolioReportingPnlSliceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:253` |
| `PortfolioReportingPnlSlicePeriodDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:242` |
| `PortfolioSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:308` |
| `PositionDiffEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:612` |
| `PostLedgerEntries` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:16` |
| `PrivateCapitalCloseCockpitApprovalDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1256` |
| `PrivateCapitalCloseCockpitDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1318` |
| `PrivateCapitalCloseCockpitLaneDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1241` |
| `PrivateCapitalCloseCockpitWorkflowDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1227` |
| `PrivateCapitalNavSupportComponentDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1271` |
| `PrivateCapitalNavSupportPackageDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1299` |
| `PrivateCapitalShadowNavTieOutDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1280` |
| `ProductExposureDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:20` |
| `ProviderCorporateActionEvidenceCandidateDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:429` |
| `ProviderCorporateActionLedgerEffectDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:447` |
| `ProviderCorporateActionReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:488` |
| `ProviderCorporateActionReadinessLineDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:420` |
| `ProviderLedgerReconciliationBreakDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:333` |
| `ProviderLedgerReconciliationBreakSignOffStateDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:297` |
| `ProviderLedgerReconciliationCheckDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:320` |
| `ProviderLedgerReconciliationCheckStatusDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:289` |
| `ProviderLedgerReconciliationDetailDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:520` |
| `ProviderLedgerReconciliationRequestDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:312` |
| `ProviderLedgerReconciliationStatusDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:281` |
| `ProviderLedgerReconciliationSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:357` |
| `ProviderPromotionChecklistDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:334` |
| `ProviderSecurityMasterPassportDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:377` |
| `ProviderSecurityMasterPassportStatusDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:305` |
| `ProviderSecurityMasterScheduleFeedDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:464` |
| `ProviderShadowBookComparisonDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:410` |
| `ProviderShadowBookComparisonLineDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:399` |
| `PublishSecurityMasterRevisionRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:97` |
| `RecommendedActionDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:35` |
| `ReconciliationBreakCategory` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:51` |
| `ReconciliationBreakDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:121` |
| `ReconciliationBreakExplanationDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:443` |
| `ReconciliationBreakQueueItem` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:372` |
| `ReconciliationBreakQueueProjectionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:247` |
| `ReconciliationBreakQueueProjectionItemDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:262` |
| `ReconciliationBreakQueueStatus` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:288` |
| `ReconciliationBreakScore` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:584` |
| `ReconciliationBreakSeverity` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:38` |
| `ReconciliationBreakStatus` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:25` |
| `ReconciliationBulkCaseworkCaseResult` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:559` |
| `ReconciliationBulkCaseworkRequest` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:539` |
| `ReconciliationBulkCaseworkResult` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:566` |
| `ReconciliationCalibrationProfileSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:631` |
| `ReconciliationCalibrationStatusDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:362` |
| `ReconciliationCalibrationSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:648` |
| `ReconciliationCaseComment` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:451` |
| `ReconciliationCaseCommentVisibility` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:334` |
| `ReconciliationCaseLifecycleState` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:298` |
| `ReconciliationCasePriority` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:314` |
| `ReconciliationCaseSignoffRecord` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:575` |
| `ReconciliationCaseSlaState` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:323` |
| `ReconciliationCaseStateTransition` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:597` |
| `ReconciliationCaseTransitionAction` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:608` |
| `ReconciliationCaseTransitionCommand` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:618` |
| `ReconciliationCaseworkAction` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:342` |
| `ReconciliationCaseworkCommand` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:514` |
| `ReconciliationCorrelationContext` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:709` |
| `ReconciliationJobControl` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:732` |
| `ReconciliationMatchDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:107` |
| `ReconciliationPayloadEnvelope` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:719` |
| `ReconciliationProcessingTelemetry` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:747` |
| `ReconciliationRolloutFlags` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:758` |
| `ReconciliationRunDetail` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:269` |
| `ReconciliationRunRequest` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:69` |
| `ReconciliationRunSummary` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:83` |
| `ReconciliationSchemaVersion` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:699` |
| `ReconciliationSecurityCoverageIssueDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:139` |
| `ReconciliationSlaComputationResult` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:492` |
| `ReconciliationSlaPolicy` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:471` |
| `ReconciliationSourceKind` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:11` |
| `ReconciliationSummary` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:187` |
| `ReconciliationTaxonomySnapshot` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:509` |
| `ReconciliationTaxonomyValue` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:501` |
| `RejectWorkflow` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:21` |
| `RenderReportTemplateRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1582` |
| `RenderReportTemplateResponseDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1587` |
| `ReopenWorkflow` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:23` |
| `ReportAccessEvaluationDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1569` |
| `ReportAccessModeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1543` |
| `ReportAccessPolicyDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1562` |
| `ReportAccessPrincipalDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1557` |
| `ReportAccessPrincipalKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1551` |
| `ReportBrandingThemeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:138` |
| `ReportPackAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1648` |
| `ReportPackChangedLineDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1651` |
| `ReportPackCreateRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1701` |
| `ReportPackDeliveryAccessLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:856` |
| `ReportPackDeliveryApprovalStepDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:884` |
| `ReportPackDeliveryArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:845` |
| `ReportPackDeliveryAttemptDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:967` |
| `ReportPackDeliveryEvidencePacketDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:892` |
| `ReportPackDeliveryFailureRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:994` |
| `ReportPackDeliveryHistoryDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1003` |
| `ReportPackDeliveryModeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:838` |
| `ReportPackDeliveryNotificationDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:864` |
| `ReportPackDeliveryPackageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:916` |
| `ReportPackDeliveryRecipientDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:878` |
| `ReportPackDeliveryRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:984` |
| `ReportPackDeliveryStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:828` |
| `ReportPackEvidenceLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1650` |
| `ReportPackLineProvenanceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1652` |
| `ReportPackPublicationManifestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1670` |
| `ReportPackPublishRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1683` |
| `ReportPackRejectRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1696` |
| `ReportPackRejectionMetadataDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1716` |
| `ReportPackRestateRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1722` |
| `ReportPackRestatementMetadataDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1709` |
| `ReportPackWorkflowActionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1693` |
| `ReportPackWorkflowRecordDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1728` |
| `ReportPackWorkflowStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1309` |
| `ReportTemplateAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1603` |
| `ReportTemplateDecisionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1644` |
| `ReportTemplateDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1574` |
| `ReportTemplateDraftRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1633` |
| `ReportTemplateGovernanceRecordDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1611` |
| `ReportTemplateLifecycleStatusDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1595` |
| `ReportTemplateParameterDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1324` |
| `ReportWriterAggregateFunctionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1336` |
| `ReportWriterCellStyleDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1394` |
| `ReportWriterChartDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1419` |
| `ReportWriterChartRenderDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1430` |
| `ReportWriterChartSeriesDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1425` |
| `ReportWriterChartTypeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1411` |
| `ReportWriterDiffDirectionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1510` |
| `ReportWriterDiffRowStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1501` |
| `ReportWriterFilterDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1371` |
| `ReportWriterFilterLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1458` |
| `ReportWriterFilterOperatorDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1357` |
| `ReportWriterFormatRuleDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1402` |
| `ReportWriterFormulaDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1350` |
| `ReportWriterFormulaLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1453` |
| `ReportWriterGridColumnDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1438` |
| `ReportWriterGridDataDictionaryFieldDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1464` |
| `ReportWriterGridDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1377` |
| `ReportWriterGridDiffCellDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1516` |
| `ReportWriterGridDiffDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1530` |
| `ReportWriterGridDiffRowDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1523` |
| `ReportWriterGridKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1327` |
| `ReportWriterGridLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1478` |
| `ReportWriterGridRenderDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1487` |
| `ReportWriterGridRowDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1443` |
| `ReportWriterGridValidationCheckDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1473` |
| `ReportWriterMetricDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1344` |
| `ReportWriterMetricLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1448` |
| `ReportingAccountingBasisDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1165` |
| `ReportingConsolidationLevelDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1175` |
| `ReportingDueScheduleRunResultDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1150` |
| `ReportingEntityScopeKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1156` |
| `ReportingFinalityDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1193` |
| `ReportingLedgerBookSelectionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1215` |
| `ReportingOutputFormatDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1184` |
| `ReportingRunAuditEntryDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1279` |
| `ReportingRunAuditTrailDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1286` |
| `ReportingRunParametersDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1219` |
| `ReportingRunReadinessCheckDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1237` |
| `ReportingRunReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1251` |
| `ReportingRunReadinessStatusDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1200` |
| `ReportingRunRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1263` |
| `ReportingRunResultDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1276` |
| `ReportingRunScopeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1207` |
| `ReportingScheduleDeliveryPlanDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1013` |
| `ReportingScheduleDeliveryTargetDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1007` |
| `ReportingScheduleRecordDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1099` |
| `ReportingScheduleRunResultDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1144` |
| `ReportingScheduleStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1056` |
| `ReportingScheduleUpsertRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1126` |
| `ReportingStarterKitDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1073` |
| `ReportingStarterKitProvisionResultDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1094` |
| `ReportingStarterKitStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1083` |
| `ReportingStarterSeedScheduleDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1063` |
| `ResearchBriefingAlert` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:95` |
| `ResearchBriefingDto` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:132` |
| `ResearchBriefingRun` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:53` |
| `ResearchBriefingWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:118` |
| `ResearchRunDrillInLinks` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:42` |
| `ResearchSavedComparison` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:84` |
| `ResearchSavedComparisonMode` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:73` |
| `ResearchWhatChangedItem` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:106` |
| `ResolveBreakCase` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:18` |
| `ResolveReconciliationBreakRequest` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:688` |
| `ResolveSecurityMasterMappings` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:12` |
| `ResolveSourceConflictRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:52` |
| `RestatementCandidateDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:138` |
| `ReviewReconciliationBreakRequest` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:678` |
| `RunAttributionSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:700` |
| `RunCashFlowSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:747` |
| `RunCashLadder` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:733` |
| `RunComparisonDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:535` |
| `RunComparisonRequest` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:579` |
| `RunDiffRequest` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:584` |
| `RunFillEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:671` |
| `RunFillSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:682` |
| `RunLotSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:920` |
| `RunPortfolioDrillInSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:762` |
| `RunReconciliation` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:17` |
| `SecurityClassificationSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:8` |
| `SecurityEconomicDefinitionSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:27` |
| `SecurityIdentityDrillInDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:56` |
| `SecurityMasterAccountingIssueDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:255` |
| `SecurityMasterChangeHistoryItemDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:194` |
| `SecurityMasterConflictAssessmentDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:145` |
| `SecurityMasterConflictAuthorityDecision` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:149` |
| `SecurityMasterConflictRecommendationKind` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:41` |
| `SecurityMasterConflictResolutionDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:113` |
| `SecurityMasterDownstreamImpactDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:345` |
| `SecurityMasterEconomicDefinitionDrillInDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:102` |
| `SecurityMasterEditOrigin` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:10` |
| `SecurityMasterEditResultDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:105` |
| `SecurityMasterEntitlementApplicabilityDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:483` |
| `SecurityMasterFactorPointDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:255` |
| `SecurityMasterIdentifierSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:160` |
| `SecurityMasterImpactLinkDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:361` |
| `SecurityMasterImpactSeverity` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:16` |
| `SecurityMasterLotModelDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:280` |
| `SecurityMasterManualChangeApprovalPostureDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:510` |
| `SecurityMasterOpenLotDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:303` |
| `SecurityMasterOpenLotProvenanceDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:334` |
| `SecurityMasterOpenLotReadModelDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:289` |
| `SecurityMasterOperatingModelDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:461` |
| `SecurityMasterOperatingModelStageDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:475` |
| `SecurityMasterOperatorMetadataDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:499` |
| `SecurityMasterProviderSymbolMappingDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:172` |
| `SecurityMasterPublishResultDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:126` |
| `SecurityMasterRecommendedActionDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:368` |
| `SecurityMasterRecommendedActionKind` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:26` |
| `SecurityMasterRevisionPublishedEvent` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:159` |
| `SecurityMasterRevisionStateDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:21` |
| `SecurityMasterScheduleBookDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:219` |
| `SecurityMasterScheduleEventDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:233` |
| `SecurityMasterScheduleProvenanceDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:267` |
| `SecurityMasterScheduleSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:209` |
| `SecurityMasterSchemaCompatibilityDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:185` |
| `SecurityMasterSourceCandidateDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:133` |
| `SecurityMasterTrustPostureDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:119` |
| `SecurityMasterTrustSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:48` |
| `SecurityMasterTrustTone` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:7` |
| `SecurityMasterWorkstationDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:43` |
| `SettlementInstruction` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:19` |
| `StartWorkflow` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:9` |
| `StarterWorkspaceDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:20` |
| `StatementBreakDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:175` |
| `StatementBreakType` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:53` |
| `StatementColumnConfidenceDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:50` |
| `StatementColumnMappingDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:57` |
| `StatementConnectorDescriptorDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:9` |
| `StatementFetchScheduleDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:154` |
| `StatementFetchScheduleUpsertRequestDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:167` |
| `StatementImportCommitResultDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:116` |
| `StatementImportIssueDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:64` |
| `StatementImportPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:101` |
| `StatementImportReconciliationCaseLinkDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:143` |
| `StatementKindSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:91` |
| `StatementMappingProfileActivityCodeDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:43` |
| `StatementMappingProfileCsvOptionsDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:32` |
| `StatementMappingProfileDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:19` |
| `StatementMappingProfileFieldDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:37` |
| `StatementMatchSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:157` |
| `StatementMatchTier` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:39` |
| `StatementNormalizedCashDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:125` |
| `StatementNormalizedPositionDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:106` |
| `StatementNormalizedTransactionDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:138` |
| `StatementProfileSuggestionDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:97` |
| `StatementReconciliationBreakExplanationDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:248` |
| `StatementReconciliationCaseAttachmentDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:239` |
| `StatementReconciliationCaseAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:257` |
| `StatementReconciliationCaseCommentDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:232` |
| `StatementReconciliationCaseCommentThreadDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:227` |
| `StatementReconciliationCaseDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:204` |
| `StatementRecordPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:73` |
| `StatementRunBreakDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:345` |
| `StatementRunCreateDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:297` |
| `StatementRunDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:268` |
| `StatementRunExceptionDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:360` |
| `StatementRunReconcileRequestDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:315` |
| `StatementRunStatus` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:9` |
| `StatementRunSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:323` |
| `StatementRunValidationDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:337` |
| `StatementSourceDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:76` |
| `StatementValidationIssueDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:92` |
| `StatementValidationSeverity` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:27` |
| `StorageAssurancePermissionsDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:146` |
| `StorageAssuranceSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:80` |
| `StorageCapacitySummaryDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:127` |
| `StorageHealthSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:90` |
| `StorageMaintenanceActionDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:154` |
| `StorageMaintenanceCandidateDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:165` |
| `StorageMaintenanceCommandRequestDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:186` |
| `StorageMaintenanceItemResultDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:192` |
| `StorageMaintenancePreviewDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:173` |
| `StorageMaintenancePreviewRequestDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:160` |
| `StorageMaintenanceResultDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:198` |
| `StorageQualityAlertDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:139` |
| `StorageQualitySummaryDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:101` |
| `StorageTierSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:134` |
| `StpProcessingState` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:4` |
| `StpStateTransition` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:37` |
| `StrategyBriefingAlert` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:59` |
| `StrategyBriefingDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:96` |
| `StrategyBriefingRun` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:17` |
| `StrategyBriefingWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:82` |
| `StrategyDesignCell` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:22` |
| `StrategyDesignCompiledScript` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:102` |
| `StrategyDesignDocument` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:6` |
| `StrategyDesignDraftSaveRequest` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:144` |
| `StrategyDesignDraftSaveResponse` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:149` |
| `StrategyDesignDraftSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:131` |
| `StrategyDesignFieldCatalogItem` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:47` |
| `StrategyDesignPreviewResult` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:111` |
| `StrategyDesignPreviewRow` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:90` |
| `StrategyDesignRunBacktestRequest` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:158` |
| `StrategyDesignRunBacktestResponse` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:165` |
| `StrategyDesignRunTraceEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:120` |
| `StrategyDesignTemplate` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:61` |
| `StrategyDesignTransition` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:35` |
| `StrategyDesignValidationMessage` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:73` |
| `StrategyDesignValidationResult` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:82` |
| `StrategyRunArtifactCompleteness` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:455` |
| `StrategyRunCashFlowDigest` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:799` |
| `StrategyRunComparison` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:433` |
| `StrategyRunContinuityDetail` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:877` |
| `StrategyRunContinuityDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:867` |
| `StrategyRunContinuityLineage` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:791` |
| `StrategyRunContinuityLink` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:776` |
| `StrategyRunContinuitySeamHealthStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:835` |
| `StrategyRunContinuityStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:845` |
| `StrategyRunContinuityWarning` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:820` |
| `StrategyRunContinuityWarningSeverity` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:827` |
| `StrategyRunCrossModeTransitionMetadata` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:506` |
| `StrategyRunDetail` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:252` |
| `StrategyRunDiff` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:587` |
| `StrategyRunDrillInLinks` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:6` |
| `StrategyRunEngine` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:22` |
| `StrategyRunExecutionSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:135` |
| `StrategyRunGovernanceHook` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:116` |
| `StrategyRunGovernanceSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:169` |
| `StrategyRunHistoryQuery` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:484` |
| `StrategyRunIdentity` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:182` |
| `StrategyRunLineageEventType` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:474` |
| `StrategyRunLineageTimelineEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:517` |
| `StrategyRunLiveStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:66` |
| `StrategyRunMode` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:11` |
| `StrategyRunPaperStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:93` |
| `StrategyRunPromotionState` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:50` |
| `StrategyRunPromotionSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:151` |
| `StrategyRunReviewPacketDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:385` |
| `StrategyRunStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:35` |
| `StrategyRunSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:198` |
| `StrategyRunTimelineEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:491` |
| `StrategyRunTimelineProjection` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:466` |
| `StrategySavedComparison` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:48` |
| `StrategySavedComparisonMode` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:37` |
| `StrategySweepObjectiveRanking` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:230` |
| `StrategySweepResultGroup` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:239` |
| `StrategyWhatChangedItem` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:70` |
| `StructuredReportingExportColumnDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:386` |
| `StructuredReportingExportDataDictionaryFieldDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:391` |
| `StructuredReportingExportDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:360` |
| `StructuredReportingExportPayloadDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:408` |
| `StructuredReportingExportPurposeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:350` |
| `StructuredReportingExportRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:421` |
| `StructuredReportingExportRowLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:403` |
| `StructuredReportingExportValidationCheckDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:398` |
| `SubmitForApproval` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:19` |
| `SubmitSecurityMasterRevisionRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:67` |
| `SymbolAttributionEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:690` |
| `SyncCompletenessResult` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:13` |
| `SyncValidationResult` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:16` |
| `ThresholdBreachDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:35` |
| `TradingAcceptanceGateDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:106` |
| `TradingAcceptanceGateStatusDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:30` |
| `TradingControlEvidenceDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:205` |
| `TradingControlReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:221` |
| `TradingExecutionReconciliationBreakDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:118` |
| `TradingExecutionReconciliationReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:128` |
| `TradingLiveOperationRequirementDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:140` |
| `TradingOperatorReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:346` |
| `TradingOperatorSignoffReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:253` |
| `TradingPaperSessionReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:168` |
| `TradingPromotionReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:239` |
| `TradingReplayReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:190` |
| `TradingReportPackReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:323` |
| `TradingTrustGateContractReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:281` |
| `TradingTrustGateEvidenceDocumentDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:273` |
| `TradingTrustGateReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:291` |
| `TradingTrustGateSampleReviewDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:262` |
| `UpdateSecurityFieldRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:35` |
| `ValidateLedgerDraft` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:15` |
| `VersionedReportTemplateIdDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1322` |
| `WorkflowActionDto` | Documented | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:29` |
| `WorkflowBlockerSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:42` |
| `WorkflowDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:14` |
| `WorkflowEvidenceBadge` | Documented | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:52` |
| `WorkflowLibraryDto` | Documented | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:6` |
| `WorkflowNextAction` | Documented | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:33` |
| `WorkflowPresetDto` | Documented | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:43` |
| `WorkflowPresetLibraryDto` | Documented | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:64` |
| `WorkflowPresetPinRequest` | Documented | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:84` |
| `WorkflowPresetSaveRequest` | Documented | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:71` |
| `WorkspaceModeDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:12` |
| `WorkspaceWorkflowSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:20` |
| `WorkstationAccountingAgingBucketPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:745` |
| `WorkstationAccountingAlertPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:762` |
| `WorkstationAccountingCashFlowSummaryPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:713` |
| `WorkstationAccountingControlCenterPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:766` |
| `WorkstationAccountingDrillLinkPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:758` |
| `WorkstationAccountingOwnerWorkloadPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:749` |
| `WorkstationAccountingPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:828` |
| `WorkstationAccountingRunCashFlowPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:702` |
| `WorkstationAccountingRunGovernancePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:685` |
| `WorkstationAccountingRunReconciliationPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:691` |
| `WorkstationAccountingRunRecord` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:723` |
| `WorkstationAccountingSeverityCountPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:741` |
| `WorkstationAccountingTrendSnapshotPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:753` |
| `WorkstationAccountingWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:411` |
| `WorkstationBrokerageAccountDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:42` |
| `WorkstationBrokerageAccountLinkDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:51` |
| `WorkstationBrokerageSyncHealth` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:7` |
| `WorkstationBrokerageSyncRunRequestDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:35` |
| `WorkstationBrokerageSyncStatusDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:67` |
| `WorkstationDataBackfillRecord` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1032` |
| `WorkstationDataExportRecord` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1043` |
| `WorkstationDataPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1055` |
| `WorkstationDataProviderDiagnostic` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:988` |
| `WorkstationDataProviderRecord` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1010` |
| `WorkstationDataProviderRoutingSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:998` |
| `WorkstationGeneratedReportWriterGridPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:550` |
| `WorkstationKernelAlertThresholdsPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:788` |
| `WorkstationKernelCriticalSeverityRatePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:795` |
| `WorkstationKernelDomainPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:804` |
| `WorkstationKernelDriftPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:783` |
| `WorkstationKernelLatencyPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:778` |
| `WorkstationKernelObservabilityPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:815` |
| `WorkstationMetricCard` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:20` |
| `WorkstationModeComparisonGroup` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:118` |
| `WorkstationModeComparisonRun` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:92` |
| `WorkstationPlotToolFocusPointPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:214` |
| `WorkstationPlotToolLegendItemPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:208` |
| `WorkstationPlotToolMomentPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:257` |
| `WorkstationPlotToolPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:299` |
| `WorkstationPlotToolPointPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:198` |
| `WorkstationPlotToolRegressionPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:263` |
| `WorkstationPlotToolSampleRowPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:267` |
| `WorkstationPlotToolSignalCardPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:220` |
| `WorkstationPlotToolStatisticsPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:276` |
| `WorkstationPlotToolStudyPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:288` |
| `WorkstationPlotToolSummaryItemPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:203` |
| `WorkstationPlotToolSummaryTilePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:250` |
| `WorkstationPlotToolTabState` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:182` |
| `WorkstationPlotToolTickPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:195` |
| `WorkstationPlotToolWorkspacePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:227` |
| `WorkstationPortfolioPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:866` |
| `WorkstationPortfolioRunRow` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:847` |
| `WorkstationPortfolioSummaryPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:882` |
| `WorkstationPortfolioSummaryTelemetry` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:873` |
| `WorkstationReportAccessAuditSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:637` |
| `WorkstationReportPackDistributionPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:621` |
| `WorkstationReportWriterDatasetSourcePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:458` |
| `WorkstationReportWriterFieldPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:450` |
| `WorkstationReportWriterFilterPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:444` |
| `WorkstationReportWriterFormulaPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:439` |
| `WorkstationReportWriterGridPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:479` |
| `WorkstationReportWriterMetricPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:434` |
| `WorkstationReportingDailyWorkItemDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:602` |
| `WorkstationReportingPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:656` |
| `WorkstationReportingProfilePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:422` |
| `WorkstationReportingRunLinkPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:532` |
| `WorkstationReportingRunNextActionPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:540` |
| `WorkstationReportingRunPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:563` |
| `WorkstationReportingTemplatePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:496` |
| `WorkstationRunDigest` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:105` |
| `WorkstationRunDrillInLinks` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:29` |
| `WorkstationSecurityCoverageGapPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:54` |
| `WorkstationSecurityCoveragePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:60` |
| `WorkstationSecurityCoverageReferencePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:37` |
| `WorkstationSecurityCoverageStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:267` |
| `WorkstationSecurityReference` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:284` |
| `WorkstationSessionPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:155` |
| `WorkstationSessionWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:143` |
| `WorkstationStrategyPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:310` |
| `WorkstationStrategyRunCard` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:71` |
| `WorkstationStrategyWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:171` |
| `WorkstationTimelineCard` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:126` |
| `WorkstationTradingBrokerageState` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:378` |
| `WorkstationTradingFillRow` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:352` |
| `WorkstationTradingOrderRow` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:339` |
| `WorkstationTradingPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:393` |
| `WorkstationTradingPositionRow` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:325` |
| `WorkstationTradingRiskState` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:365` |
| `WorkstationWatchlist` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:29` |
| `WorkstationWorkspaceDefinition` | Documented | `src/Meridian.Contracts/Workstation/WorkstationWorkspaceCatalog.cs:6` |

## Follow-up Queue

No API contract coverage gaps detected.

---

*This dashboard is auto-generated. Do not edit manually.*
