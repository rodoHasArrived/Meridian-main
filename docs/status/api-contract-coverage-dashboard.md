# API Contract Coverage Dashboard

_Auto-generated from canonical JSON payload._
_Generated: 1970-01-01T00:00:00+00:00_
Data sources: `src/**/*.cs endpoint mappings`, `src/Meridian.Contracts/Api/UiApiRoutes.cs`, `src/Meridian.Contracts/Workstation/*.cs`, `docs/**/*.md`


Tracks whether mapped API routes and workstation DTO contracts are visible in the Markdown documentation set.

## Summary

| Metric | Value |
|---|---:|
| Weighted score | 99.2% |
| Endpoint coverage | 100.0% |
| Workstation contract coverage | 98.1% |
| Endpoints documented | 588 / 588 |
| Workstation contracts documented | 793 / 808 |

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
| `GET` | `/api/backfill/completeness` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:217` |
| `POST` | `/api/backfill/cost-estimate` | Documented | `src/Meridian.Ui.Shared/Endpoints/ResilienceEndpoints.cs:48` |
| `GET` | `/api/backfill/executions` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:107` |
| `POST` | `/api/backfill/gap-fill` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:64` |
| `GET` | `/api/backfill/gaps` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:175` |
| `GET` | `/api/backfill/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:24` |
| `GET` | `/api/backfill/presets` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:92` |
| `GET` | `/api/backfill/progress` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:112` |
| `GET` | `/api/backfill/providers` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:33` |
| `GET` | `/api/backfill/providers/audit` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:176` |
| `POST` | `/api/backfill/providers/dry-run-plan` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:157` |
| `GET` | `/api/backfill/providers/fallback-chain` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:145` |
| `GET` | `/api/backfill/providers/metadata` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:124` |
| `GET` | `/api/backfill/providers/statuses` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:134` |
| `GET` | `/api/backfill/resolve/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:46` |
| `POST` | `/api/backfill/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:84` |
| `POST` | `/api/backfill/run/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:56` |
| `GET` | `/api/backfill/schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:162` |
| `POST` | `/api/backfill/schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:177` |
| `GET` | `/api/backfill/schedules/templates` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:287` |
| `DELETE` | `/api/backfill/schedules/{id}` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:203` |
| `GET` | `/api/backfill/schedules/{id}` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:192` |
| `POST` | `/api/backfill/schedules/{id}/disable` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:234` |
| `POST` | `/api/backfill/schedules/{id}/enable` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:219` |
| `GET` | `/api/backfill/schedules/{id}/history` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:274` |
| `POST` | `/api/backfill/schedules/{id}/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:249` |
| `GET` | `/api/backfill/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:132` |
| `GET` | `/api/backfill/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:43` |
| `GET` | `/api/backfill/validation` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:25` |
| `GET` | `/api/backfill/validation/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:115` |
| `GET` | `/api/backpressure` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:118` |
| `GET` | `/api/calendar/holidays` | Documented | `src/Meridian.Ui.Shared/Endpoints/CalendarEndpoints.cs:64` |
| `GET` | `/api/calendar/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/CalendarEndpoints.cs:21` |
| `GET` | `/api/calendar/trading-days` | Documented | `src/Meridian.Ui.Shared/Endpoints/CalendarEndpoints.cs:89` |
| `GET` | `/api/canonicalization/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:112` |
| `GET` | `/api/canonicalization/parity` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:47` |
| `GET` | `/api/canonicalization/parity/{provider}` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:75` |
| `GET` | `/api/canonicalization/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:20` |
| `GET` | `/api/catalog/coverage` | Documented | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:247` |
| `GET` | `/api/catalog/search` | Documented | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:24` |
| `GET` | `/api/catalog/symbols` | Documented | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:133` |
| `GET` | `/api/catalog/timeline` | Documented | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:170` |
| `GET` | `/api/compliance/access-reviews` | Documented | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:61` |
| `POST` | `/api/compliance/access-reviews/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:52` |
| `POST` | `/api/compliance/actions/evaluate` | Documented | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:15` |
| `GET` | `/api/compliance/audit/extract` | Documented | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:33` |
| `GET` | `/api/compliance/controls/attestation` | Documented | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:37` |
| `GET` | `/api/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:36` |
| `POST` | `/api/config/alpaca` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:138` |
| `GET` | `/api/config/data-sources` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:591` |
| `POST` | `/api/config/data-sources` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:609` |
| `POST` | `/api/config/datasource` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:120` |
| `GET` | `/api/config/datasources` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:51` |
| `POST` | `/api/config/datasources` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:70` |
| `POST` | `/api/config/datasources/defaults` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:188` |
| `POST` | `/api/config/datasources/failover` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:221` |
| `GET` | `/api/config/derivatives` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:220` |
| `POST` | `/api/config/derivatives` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:229` |
| `GET` | `/api/config/effective` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:63` |
| `POST` | `/api/config/storage` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:151` |
| `POST` | `/api/config/symbols` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:180` |
| `GET` | `/api/connections` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:144` |
| `GET` | `/api/data/bbo/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:330` |
| `GET` | `/api/data/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:411` |
| `GET` | `/api/data/l3-orderbook/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:260` |
| `GET` | `/api/data/orderbook/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:188` |
| `GET` | `/api/data/orderflow/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:370` |
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
| `GET` | `/api/diagnostics/coordination` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:462` |
| `POST` | `/api/diagnostics/dry-run` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:35` |
| `GET` | `/api/diagnostics/error-codes` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:360` |
| `GET` | `/api/diagnostics/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:169` |
| `GET` | `/api/diagnostics/providers` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:55` |
| `POST` | `/api/diagnostics/providers/{providerName}/test` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:297` |
| `GET` | `/api/diagnostics/quick-check` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:319` |
| `POST` | `/api/diagnostics/selftest` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:370` |
| `GET` | `/api/diagnostics/show-config` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:336` |
| `GET` | `/api/diagnostics/storage` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:72` |
| `POST` | `/api/diagnostics/test-connectivity` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:409` |
| `POST` | `/api/diagnostics/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:275` |
| `POST` | `/api/diagnostics/validate-config` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:433` |
| `POST` | `/api/diagnostics/validate-credentials` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:391` |
| `GET` | `/api/errors` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:107` |
| `GET` | `/api/events/stream` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:206` |
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
| `GET` | `/api/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:37` |
| `GET` | `/api/health/detailed` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:182` |
| `GET` | `/api/health/diagnostics/bundle` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:230` |
| `GET` | `/api/health/events` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:172` |
| `GET` | `/api/health/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:185` |
| `GET` | `/api/health/providers` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:55` |
| `GET` | `/api/health/providers/{provider}/diagnostics` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:95` |
| `POST` | `/api/health/providers/{provider}/test` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:204` |
| `GET` | `/api/health/storage` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:137` |
| `GET` | `/api/health/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:28` |
| `GET` | `/api/indices/{indexName}/constituents` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:558` |
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
| `GET` | `/api/ledger/accounting-configuration` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:547` |
| `POST` | `/api/ledger/accounting-configuration/activate` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:982` |
| `GET` | `/api/ledger/accounting-configuration/audit` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1017` |
| `POST` | `/api/ledger/accounting-configuration/chart` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:575` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:637` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/candidates` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:811` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/candidates/post` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:854` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/dry-run` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:770` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/projection-sets` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:898` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/promotion-approvals` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:668` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/test-cases` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:704` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/tests` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:941` |
| `POST` | `/api/ledger/accounting-configuration/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:735` |
| `POST` | `/api/ledger/accounting-configuration/templates` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:606` |
| `GET` | `/api/ledger/aggregates/{aggregateId:guid}/journal-entries` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:305` |
| `GET` | `/api/ledger/books` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:25` |
| `POST` | `/api/ledger/books` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:75` |
| `POST` | `/api/ledger/books/rollout-assessment` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:106` |
| `GET` | `/api/ledger/books/{ledgerBookId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:52` |
| `POST` | `/api/ledger/close-management/evidence-review` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1271` |
| `POST` | `/api/ledger/close-management/late-adjustments` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1121` |
| `POST` | `/api/ledger/close-management/late-adjustments/review` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1171` |
| `POST` | `/api/ledger/close-management/period-lock` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1321` |
| `POST` | `/api/ledger/close-management/period-plan/configuration` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1071` |
| `GET` | `/api/ledger/close-management/period-plan/{workflowId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1045` |
| `POST` | `/api/ledger/close-management/task-signoffs` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1221` |
| `GET` | `/api/ledger/journal-entry-workbench` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1562` |
| `POST` | `/api/ledger/journal-entry-workbench/drafts` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1883` |
| `POST` | `/api/ledger/journal-entry-workbench/evidence` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:2018` |
| `POST` | `/api/ledger/journal-entry-workbench/lifecycle-action` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:2061` |
| `POST` | `/api/ledger/journal-entry-workbench/submit-approval` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1975` |
| `POST` | `/api/ledger/journal-entry-workbench/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1934` |
| `GET` | `/api/ledger/periods` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:147` |
| `POST` | `/api/ledger/periods` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:185` |
| `POST` | `/api/ledger/periods/{periodId:guid}/close` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:217` |
| `GET` | `/api/ledger/periods/{periodId:guid}/journal-entries` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:261` |
| `GET` | `/api/ledger/periods/{periodId:guid}/pnl-summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:404` |
| `GET` | `/api/ledger/periods/{periodId:guid}/trial-balance` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:354` |
| `GET` | `/api/ledger/periods/{periodId:guid}/trial-balance-report` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:379` |
| `GET` | `/api/ledger/private-capital/activity` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1588` |
| `GET` | `/api/ledger/private-capital/capital-account-subledger` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1704` |
| `GET` | `/api/ledger/private-capital/capital-account-workbench` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1836` |
| `GET` | `/api/ledger/private-capital/fund-event-command-center` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1663` |
| `GET` | `/api/ledger/private-capital/fund-event-record` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1620` |
| `GET` | `/api/ledger/private-capital/report-output` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1765` |
| `POST` | `/api/ledger/reports/accounting-package` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1375` |
| `POST` | `/api/ledger/reports/accounting-package/certification` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1426` |
| `GET` | `/api/ledger/reports/accounting-packages` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1479` |
| `GET` | `/api/ledger/reports/accounting-packages/{packageId}/exports/{artifactId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1516` |
| `GET` | `/api/ledger/reports/pnl-summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:488` |
| `GET` | `/api/ledger/reports/trial-balance` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:429` |
| `GET` | `/api/loans/portfolio` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1094` |
| `POST` | `/api/loans/rebuild-all` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1105` |
| `GET` | `/api/loans/rebuild-checkpoints` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1085` |
| `POST` | `/api/maintenance/execute` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:145` |
| `GET` | `/api/maintenance/executions` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:196` |
| `POST` | `/api/maintenance/executions/cleanup` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:500` |
| `GET` | `/api/maintenance/executions/failed` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:256` |
| `GET` | `/api/maintenance/executions/{executionId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:215` |
| `POST` | `/api/maintenance/executions/{executionId}/cancel` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:173` |
| `GET` | `/api/maintenance/presets` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:411` |
| `GET` | `/api/maintenance/schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:20` |
| `POST` | `/api/maintenance/schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:35` |
| `GET` | `/api/maintenance/schedules/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:277` |
| `GET` | `/api/maintenance/schedules/{id}` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:49` |
| `DELETE` | `/api/maintenance/schedules/{id}/delete` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:59` |
| `POST` | `/api/maintenance/schedules/{id}/disable` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:87` |
| `POST` | `/api/maintenance/schedules/{id}/enable` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:73` |
| `GET` | `/api/maintenance/schedules/{id}/history` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:123` |
| `POST` | `/api/maintenance/schedules/{id}/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:101` |
| `DELETE` | `/api/maintenance/schedules/{scheduleId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:95` |
| `PUT` | `/api/maintenance/schedules/{scheduleId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:36` |
| `GET` | `/api/maintenance/schedules/{scheduleId}/executions` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:236` |
| `GET` | `/api/maintenance/schedules/{scheduleId}/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:294` |
| `POST` | `/api/maintenance/schedules/{scheduleId}/trigger` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:120` |
| `GET` | `/api/maintenance/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:314` |
| `GET` | `/api/maintenance/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:347` |
| `GET` | `/api/maintenance/task-types` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:473` |
| `POST` | `/api/maintenance/validate-cron` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:366` |
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
| `GET` | `/api/packaging/download/{fileName}` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:292` |
| `POST` | `/api/packaging/import` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:86` |
| `GET` | `/api/packaging/list` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:206` |
| `POST` | `/api/packaging/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:134` |
| `DELETE` | `/api/packaging/{fileName}` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:252` |
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
| `GET` | `/api/providers/capabilities` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:145` |
| `GET` | `/api/providers/catalog` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:535` |
| `GET` | `/api/providers/catalog/{providerId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:574` |
| `GET` | `/api/providers/comparison` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:276` |
| `POST` | `/api/providers/configure` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:253` |
| `GET` | `/api/providers/connections` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:19` |
| `GET` | `/api/providers/dashboard` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:278` |
| `GET` | `/api/providers/failover` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:54` |
| `GET` | `/api/providers/failover-thresholds` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:211` |
| `POST` | `/api/providers/failover/reset` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:96` |
| `POST` | `/api/providers/failover/trigger` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:80` |
| `GET` | `/api/providers/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:228` |
| `GET` | `/api/providers/ib/error-codes` | Documented | `src/Meridian.Ui.Shared/Endpoints/IBEndpoints.cs:91` |
| `GET` | `/api/providers/ib/limits` | Documented | `src/Meridian.Ui.Shared/Endpoints/IBEndpoints.cs:113` |
| `GET` | `/api/providers/ib/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/IBEndpoints.cs:24` |
| `GET` | `/api/providers/latency` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:129` |
| `GET` | `/api/providers/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:451` |
| `GET` | `/api/providers/modules` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:23` |
| `POST` | `/api/providers/modules` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:56` |
| `GET` | `/api/providers/modules/catalogue` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:40` |
| `DELETE` | `/api/providers/modules/{moduleId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:110` |
| `PUT` | `/api/providers/modules/{moduleId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:82` |
| `PUT` | `/api/providers/modules/{moduleId}/enabled` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:135` |
| `POST` | `/api/providers/modules/{moduleId}/test` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:158` |
| `GET` | `/api/providers/rate-limits` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:111` |
| `GET` | `/api/providers/readiness` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:347` |
| `POST` | `/api/providers/restart` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:177` |
| `GET` | `/api/providers/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:359` |
| `POST` | `/api/providers/switch` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:164` |
| `DELETE` | `/api/providers/{providerId}/credentials` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:87` |
| `PUT` | `/api/providers/{providerId}/credentials` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:34` |
| `POST` | `/api/providers/{providerId}/verify` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:63` |
| `GET` | `/api/providers/{providerName}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:24` |
| `GET` | `/api/providers/{providerName}/rate-limit-history` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:130` |
| `POST` | `/api/providers/{providerName}/test` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:191` |
| `POST` | `/api/providers/{provider}/test-connection` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderCredentialEndpoints.cs:69` |
| `POST` | `/api/providers/{provider}/validate-credentials` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderCredentialEndpoints.cs:26` |
| `GET` | `/api/quality/anomalies` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:174` |
| `GET` | `/api/quality/anomalies/stale` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:218` |
| `GET` | `/api/quality/anomalies/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:215` |
| `GET` | `/api/quality/anomalies/unacknowledged` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:203` |
| `POST` | `/api/quality/anomalies/{anomalyId}/acknowledge` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:206` |
| `GET` | `/api/quality/anomalies/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:200` |
| `GET` | `/api/quality/comparison/discrepancies` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:253` |
| `GET` | `/api/quality/comparison/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:265` |
| `GET` | `/api/quality/comparison/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:246` |
| `GET` | `/api/quality/completeness` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:77` |
| `GET` | `/api/quality/completeness/low` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:102` |
| `GET` | `/api/quality/completeness/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:99` |
| `GET` | `/api/quality/completeness/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:84` |
| `GET` | `/api/quality/dashboard` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:69` |
| `GET` | `/api/quality/drops` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:329` |
| `GET` | `/api/quality/drops/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:354` |
| `GET` | `/api/quality/errors` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:147` |
| `GET` | `/api/quality/errors/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:166` |
| `GET` | `/api/quality/errors/top-symbols` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:169` |
| `GET` | `/api/quality/errors/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:159` |
| `GET` | `/api/quality/gaps` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:111` |
| `GET` | `/api/quality/gaps/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:138` |
| `GET` | `/api/quality/gaps/timeline/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:130` |
| `GET` | `/api/quality/gaps/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:123` |
| `GET` | `/api/quality/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:312` |
| `GET` | `/api/quality/health/unhealthy` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:345` |
| `GET` | `/api/quality/health/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:336` |
| `GET` | `/api/quality/latency` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:223` |
| `GET` | `/api/quality/latency/high` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:241` |
| `GET` | `/api/quality/latency/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:238` |
| `GET` | `/api/quality/latency/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:226` |
| `GET` | `/api/quality/latency/{symbol}/histogram` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:235` |
| `GET` | `/api/quality/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:72` |
| `GET` | `/api/quality/reports/daily` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:270` |
| `POST` | `/api/quality/reports/export` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:297` |
| `GET` | `/api/quality/reports/weekly` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:278` |
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
| `POST` | `/api/security-master` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:340` |
| `POST` | `/api/security-master/aliases/upsert` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:424` |
| `POST` | `/api/security-master/amend` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:368` |
| `GET` | `/api/security-master/asset-profiles` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:74` |
| `POST` | `/api/security-master/asset-profiles/approve` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:158` |
| `POST` | `/api/security-master/asset-profiles/drafts` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:117` |
| `GET` | `/api/security-master/asset-profiles/promotion-candidates` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:89` |
| `POST` | `/api/security-master/asset-profiles/rollback` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:199` |
| `GET` | `/api/security-master/asset-profiles/{profileId}/lineage` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:101` |
| `GET` | `/api/security-master/conflicts` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:648` |
| `POST` | `/api/security-master/conflicts/{conflictId:guid}/resolve` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:668` |
| `GET` | `/api/security-master/data-entitlements` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1138` |
| `POST` | `/api/security-master/data-entitlements` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1160` |
| `GET` | `/api/security-master/data-entitlements/expiring` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1148` |
| `DELETE` | `/api/security-master/data-entitlements/{entitlementId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1188` |
| `POST` | `/api/security-master/deactivate` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:396` |
| `GET` | `/api/security-master/exceptions/aging` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1251` |
| `POST` | `/api/security-master/import` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:715` |
| `POST` | `/api/security-master/ingest/edgar` | Documented | `src/Meridian.Ui.Shared/Endpoints/EdgarReferenceDataEndpoints.cs:24` |
| `GET` | `/api/security-master/ingest/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:746` |
| `GET` | `/api/security-master/quality-report/latest` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1235` |
| `POST` | `/api/security-master/quality-report/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1220` |
| `POST` | `/api/security-master/resolve` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:245` |
| `POST` | `/api/security-master/search` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:282` |
| `GET` | `/api/security-master/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:42` |
| `GET` | `/api/security-master/{securityId:guid}/cashflow-projections` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1115` |
| `GET` | `/api/security-master/{securityId:guid}/cashflow-source` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1074` |
| `PUT` | `/api/security-master/{securityId:guid}/cashflow-source` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1087` |
| `GET` | `/api/security-master/{securityId:guid}/convertible-equity-terms` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:532` |
| `PATCH` | `/api/security-master/{securityId:guid}/convertible-equity-terms` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:553` |
| `GET` | `/api/security-master/{securityId:guid}/corporate-actions` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:590` |
| `POST` | `/api/security-master/{securityId:guid}/corporate-actions` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:611` |
| `GET` | `/api/security-master/{securityId:guid}/history` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:313` |
| `GET` | `/api/security-master/{securityId:guid}/operator-overrides` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:767` |
| `PATCH` | `/api/security-master/{securityId:guid}/operator-overrides` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:791` |
| `GET` | `/api/security-master/{securityId:guid}/preferred-equity-terms` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:475` |
| `PATCH` | `/api/security-master/{securityId:guid}/preferred-equity-terms` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:496` |
| `GET` | `/api/security-master/{securityId:guid}/price-comparison` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1057` |
| `GET` | `/api/security-master/{securityId:guid}/price-golden-copy` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1043` |
| `GET` | `/api/security-master/{securityId:guid}/pricing-hierarchy` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:969` |
| `PUT` | `/api/security-master/{securityId:guid}/pricing-hierarchy` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:983` |
| `POST` | `/api/security-master/{securityId:guid}/raw-price` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1013` |
| `GET` | `/api/security-master/{securityId:guid}/trading-parameters` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:452` |
| `GET` | `/api/security-master/{securityId:guid}/validation` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:60` |
| `POST` | `/api/servicer-reports` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1032` |
| `GET` | `/api/servicer-reports/{batchId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1059` |
| `GET` | `/api/servicer-reports/{batchId:guid}/position-lines` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1067` |
| `GET` | `/api/servicer-reports/{batchId:guid}/transaction-lines` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1076` |
| `GET` | `/api/sla/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:555` |
| `GET` | `/api/sla/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:581` |
| `GET` | `/api/sla/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:527` |
| `GET` | `/api/sla/status/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:530` |
| `GET` | `/api/sla/violations` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:539` |
| `GET` | `/api/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:96` |
| `GET` | `/api/storage/archive/stats` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:319` |
| `GET` | `/api/storage/breakdown` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:79` |
| `GET` | `/api/storage/capacity-forecast` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:586` |
| `GET` | `/api/storage/catalog` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:353` |
| `POST` | `/api/storage/cleanup` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:279` |
| `GET` | `/api/storage/cleanup/candidates` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:244` |
| `POST` | `/api/storage/convert-parquet` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:557` |
| `GET` | `/api/storage/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:212` |
| `GET` | `/api/storage/health/check` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:416` |
| `GET` | `/api/storage/health/orphans` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:438` |
| `POST` | `/api/storage/maintenance/defrag` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:535` |
| `GET` | `/api/storage/profiles` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:24` |
| `GET` | `/api/storage/quality/alerts` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:118` |
| `POST` | `/api/storage/quality/alerts/{alertId}/acknowledge` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:143` |
| `GET` | `/api/storage/quality/anomalies` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:228` |
| `POST` | `/api/storage/quality/check` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:264` |
| `GET` | `/api/storage/quality/rankings/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:153` |
| `GET` | `/api/storage/quality/scores` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:59` |
| `GET` | `/api/storage/quality/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:25` |
| `GET` | `/api/storage/quality/symbol/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:91` |
| `GET` | `/api/storage/quality/trends` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:185` |
| `GET` | `/api/storage/search/files` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:375` |
| `GET` | `/api/storage/stats` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:36` |
| `GET` | `/api/storage/symbol/{symbol}/files` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:162` |
| `GET` | `/api/storage/symbol/{symbol}/info` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:110` |
| `GET` | `/api/storage/symbol/{symbol}/path` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:189` |
| `GET` | `/api/storage/symbol/{symbol}/stats` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:135` |
| `POST` | `/api/storage/tiers/migrate` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:459` |
| `GET` | `/api/storage/tiers/plan` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:511` |
| `GET` | `/api/storage/tiers/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:490` |
| `POST` | `/api/strategies/covered-call/chain-preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:158` |
| `GET` | `/api/strategies/covered-call/runs` | Documented | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:52` |
| `POST` | `/api/strategies/covered-call/runs` | Documented | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:24` |
| `POST` | `/api/strategies/covered-call/runs/{runId}/cancel` | Documented | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:135` |
| `GET` | `/api/strategies/covered-call/runs/{runId}/result` | Documented | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:93` |
| `GET` | `/api/strategies/covered-call/runs/{runId}/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:70` |
| `GET` | `/api/strategies/runs/compare` | Documented | `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs:2698` |
| `GET` | `/api/strategies/{strategyId}/runs` | Documented | `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs:2577` |
| `GET` | `/api/subscriptions/active` | Documented | `src/Meridian.Ui.Shared/Endpoints/SubscriptionEndpoints.cs:21` |
| `POST` | `/api/subscriptions/subscribe` | Documented | `src/Meridian.Ui.Shared/Endpoints/SubscriptionEndpoints.cs:43` |
| `POST` | `/api/subscriptions/unsubscribe/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SubscriptionEndpoints.cs:72` |
| `GET` | `/api/symbols` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:31` |
| `POST` | `/api/symbols/add` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:143` |
| `GET` | `/api/symbols/archived` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:79` |
| `POST` | `/api/symbols/batch` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:392` |
| `POST` | `/api/symbols/bulk-add` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:307` |
| `POST` | `/api/symbols/bulk-remove` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:347` |
| `POST` | `/api/symbols/create` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:440` |
| `GET` | `/api/symbols/mappings` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolMappingEndpoints.cs:29` |
| `POST` | `/api/symbols/mappings` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolMappingEndpoints.cs:48` |
| `GET` | `/api/symbols/monitored` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:56` |
| `GET` | `/api/symbols/search` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:368` |
| `GET` | `/api/symbols/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:237` |
| `POST` | `/api/symbols/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:269` |
| `DELETE` | `/api/symbols/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:530` |
| `POST` | `/api/symbols/{symbol}/archive` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:288` |
| `GET` | `/api/symbols/{symbol}/depth` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:212` |
| `POST` | `/api/symbols/{symbol}/remove` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:167` |
| `GET` | `/api/symbols/{symbol}/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:103` |
| `GET` | `/api/symbols/{symbol}/trades` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:187` |
| `POST` | `/api/symbols/{symbol}/update` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:480` |
| `GET` | `/api/system/lifecycle` | Documented | `src/Meridian/UiServer.cs:349` |
| `POST` | `/api/system/shutdown` | Documented | `src/Meridian/UiServer.cs:371` |
| `GET` | `/health` | Documented | `src/Meridian.Application/Composition/HostAdapters.cs:60` |
| `GET` | `/live` | Documented | `src/Meridian.Application/Composition/HostAdapters.cs:73` |
| `GET` | `/ready` | Documented | `src/Meridian.Application/Composition/HostAdapters.cs:72` |

## Workstation Contract Coverage

| Contract | Status | Source |
|---|---|---|
| `AccrualCalculationResultDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:190` |
| `AccrualInputSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:172` |
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
| `BankAccountSnapshot` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:97` |
| `BankStatementImportResultDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:65` |
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
| `BulkResolveSecurityMasterConflictsRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:523` |
| `BulkResolveSecurityMasterConflictsResult` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:529` |
| `CashFinancingSummary` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:135` |
| `CashFlowEntryDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:713` |
| `CashFlowProjectionPoint` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:34` |
| `CashForecastResult` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:36` |
| `CashLadderBucketDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:723` |
| `CashSyncSourceAvailability` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:2` |
| `CashSyncWindow` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:5` |
| `CloseWorkflow` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:22` |
| `ClosedLotSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:905` |
| `CollateralCallDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:26` |
| `CounterpartyExposureDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:10` |
| `CouponEvent` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:20` |
| `CrossFundReportingConsolidationDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:322` |
| `CrossFundReportingConsolidationScopeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:312` |
| `DataUploadPreviewResultDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:44` |
| `DataUploadTemplateCatalogDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:6` |
| `DataUploadTemplateDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:15` |
| `DataUploadTemplateFieldDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:34` |
| `DataUploadValidationIssueDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:85` |
| `DeltaOutlierResult` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:15` |
| `EquityCurvePoint` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:650` |
| `EquityCurveSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:659` |
| `EvidenceArtifactCaptureDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:40` |
| `EvidenceArtifactExtractionFieldDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:49` |
| `EvidenceArtifactRefDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:29` |
| `EvidenceArtifactRefDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:276` |
| `EvidenceAssuranceComponentDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:341` |
| `EvidenceCompletenessDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:623` |
| `EvidenceCompletenessSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:117` |
| `EvidenceDocumentAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:150` |
| `EvidenceDocumentAuthorityDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:157` |
| `EvidenceDocumentClassificationDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:62` |
| `EvidenceDocumentConfirmedFieldDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:142` |
| `EvidenceDocumentDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:190` |
| `EvidenceDocumentExtractionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:243` |
| `EvidenceDocumentExtractionResultDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:251` |
| `EvidenceDocumentIntakeChannelDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:80` |
| `EvidenceDocumentIntakeSourceDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:269` |
| `EvidenceDocumentIntakeSourceKindDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:259` |
| `EvidenceDocumentLinkDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:126` |
| `EvidenceDocumentLinkKindDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:104` |
| `EvidenceDocumentReviewStateDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:133` |
| `EvidenceDocumentReviewStatusDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:119` |
| `EvidenceDocumentSourceRecordDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:168` |
| `EvidenceEdgeDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:298` |
| `EvidenceEndpointErrorDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:615` |
| `EvidenceExtractionStatusDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:93` |
| `EvidenceFreshnessDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:24` |
| `EvidenceGraphDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:657` |
| `EvidenceLifecycleMetadataDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:589` |
| `EvidenceManifestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:230` |
| `EvidenceManifestPackageKindDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:220` |
| `EvidenceNodeDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:282` |
| `EvidenceNodeDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:293` |
| `EvidencePacketDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:645` |
| `EvidencePacketExportRequest` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:679` |
| `EvidencePacketExportResponse` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:688` |
| `EvidenceProofChainDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:382` |
| `EvidenceProofChainLayerDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:369` |
| `EvidenceProofChainLayerKindDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:356` |
| `EvidenceRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:180` |
| `EvidenceRequestListDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:458` |
| `EvidenceRequestListKindDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:448` |
| `EvidenceSlaAssessmentDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:330` |
| `EvidenceSlaPolicyDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:321` |
| `EvidenceStatusDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:6` |
| `EvidenceSubjectDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:15` |
| `EvidenceSubjectLinkageDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:595` |
| `EvidenceSupportRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:434` |
| `EvidenceTemplateDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:672` |
| `EvidenceTemplateExportSettingsDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:667` |
| `EvidenceValidationIssueDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:312` |
| `EvidenceValidationSeverityDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:306` |
| `EvidenceVaultArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:417` |
| `EvidenceVaultDocumentEntryDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:520` |
| `EvidenceVaultDocumentQueryDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:507` |
| `EvidenceVaultDocumentReviewRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:531` |
| `EvidenceVaultDocumentReviewResponseDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:541` |
| `EvidenceVaultIdentityDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:399` |
| `EvidenceVaultIntakeRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:545` |
| `EvidenceVaultIntakeResponseDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:572` |
| `EvidenceVaultLookupRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:605` |
| `EvidenceVaultRequestListEntryDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:484` |
| `EvidenceVaultRequestListQueryDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:474` |
| `ExpectedAccountingEventDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:206` |
| `ExpectedAccountingEventKindDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:151` |
| `ExpectedJournalPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:234` |
| `ExpectedJournalPreviewLineDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:224` |
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
| `FundAccountSummary` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:68` |
| `FundAuditEntry` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:296` |
| `FundAuditEvidenceCategoryKeyDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:60` |
| `FundAuditEvidenceCategorySummaryDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:493` |
| `FundAuditPackReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:502` |
| `FundJournalLine` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:50` |
| `FundLedgerDimensionSnapshot` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:126` |
| `FundLedgerQuery` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:27` |
| `FundLedgerReconciliationSnapshot` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:135` |
| `FundLedgerScope` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:8` |
| `FundLedgerSliceDto` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:75` |
| `FundLedgerSnapshotBalanceLine` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:114` |
| `FundLedgerSummary` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:90` |
| `FundLedgerTotalsDto` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:63` |
| `FundNavAssetClassExposureDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:106` |
| `FundNavAttributionSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:113` |
| `FundOperationsNavigationContext` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:26` |
| `FundOperationsTab` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:8` |
| `FundOperationsWorkspaceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:457` |
| `FundOperationsWorkspaceQuery` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:95` |
| `FundPortfolioPosition` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:113` |
| `FundReconciliationItem` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:160` |
| `FundReportAssetClassSectionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:490` |
| `FundReportPackArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:548` |
| `FundReportPackEvidenceBundleApprovalDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:791` |
| `FundReportPackEvidenceBundleDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:799` |
| `FundReportPackEvidenceBundleSourceLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:782` |
| `FundReportPackGenerateRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:532` |
| `FundReportPackHistoryItemDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:759` |
| `FundReportPackLifecycleEventDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:717` |
| `FundReportPackLineagePointerDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:571` |
| `FundReportPackPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:515` |
| `FundReportPackPreviewRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:479` |
| `FundReportPackProvenanceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:559` |
| `FundReportPackSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:728` |
| `FundReportPackValidationIssueDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:700` |
| `FundReportingProfileDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:125` |
| `FundReportingSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:430` |
| `FundTrialBalanceLine` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:37` |
| `FundWorkflowCommandMetadata` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:7` |
| `FundWorkflowOverallStatus` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:2` |
| `FundWorkflowRejectionReasonCode` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:6` |
| `FundWorkflowStage` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:4` |
| `FundWorkflowState` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:24` |
| `FundWorkflowSubStatus` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:5` |
| `FundWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:37` |
| `FxConversionReference` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:32` |
| `GovernanceLifecycleProjectionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:196` |
| `GovernanceReportArtifactFormatDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:24` |
| `GovernanceReportKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:7` |
| `GovernanceReportPackStatusDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:34` |
| `GovernanceReportValidationSeverityDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:52` |
| `HaircutRuleDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:24` |
| `ImportBrokerData` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:10` |
| `InsightFeed` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:19` |
| `InsightWidget` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:6` |
| `InstrumentPassportDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:494` |
| `InstrumentPassportOperationsHandoffDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:382` |
| `InstrumentPassportOperationsReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:421` |
| `InstrumentPassportOperationsWorkbenchDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:397` |
| `InstrumentPassportOperationsWorkbenchItemDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:411` |
| `InstrumentPassportOperationsWorkbenchPanelDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:404` |
| `InstrumentPassportPricingDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:513` |
| `InstrumentPassportProviderConfidenceDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:353` |
| `InstrumentPassportReferenceDataWorkbenchDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:368` |
| `InstrumentPassportReferenceDataWorkbenchSectionDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:374` |
| `InvestmentAccountingPreviewModeDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:24` |
| `InvestmentAccountingReconciliationExpectationDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:54` |
| `InvestmentAccountingTradeSideDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:17` |
| `InvestmentAccountingTransactionKindDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:6` |
| `InvestmentAccountingTransactionLabPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:71` |
| `InvestmentAccountingTransactionLabRequestDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:29` |
| `InvestmentAccountingTrialBalanceImpactDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:47` |
| `LedgerAmountApprovalStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:657` |
| `LedgerAmountProvenanceDetailDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:681` |
| `LedgerAmountProvenanceEvidenceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:584` |
| `LedgerAmountReconciliationCaseDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:619` |
| `LedgerAmountReconciliationStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:650` |
| `LedgerAmountReportUsageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:663` |
| `LedgerAmountSecurityMasterLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:608` |
| `LedgerAmountStrategyRunLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:673` |
| `LedgerImpactPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:285` |
| `LedgerJournalLine` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:413` |
| `LedgerSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:364` |
| `LedgerTrialBalanceLine` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:392` |
| `MarginRequirementDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:22` |
| `MeridianAssuranceScoreDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:348` |
| `MetricsDiff` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:627` |
| `MultiAssetClassCoverageDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:924` |
| `MultiAssetCoverageSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:963` |
| `MultiAssetDrillThroughTargetDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:911` |
| `MultiAssetEvidenceRequirementDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:890` |
| `MultiAssetPackCoverageDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:939` |
| `MultiAssetReadinessBlockerDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:901` |
| `NormalizeBrokerTransactions` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:11` |
| `OpenLotSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:892` |
| `OperationsAccountingRecordEvidenceCategoryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1014` |
| `OperationsAccountingRecordSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1004` |
| `OperationsActionOriginDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:461` |
| `OperationsApprovalDecisionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:670` |
| `OperationsApprovalDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1151` |
| `OperationsApprovalPolicyMatrixDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:729` |
| `OperationsApprovalPolicyMatrixRowDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:735` |
| `OperationsApprovalPolicyRuleAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:779` |
| `OperationsApprovalPolicyRuleUpsertRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:753` |
| `OperationsApprovalPolicyRuleUpsertResultDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:774` |
| `OperationsApprovalStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:101` |
| `OperationsAssignBreakCaseRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:647` |
| `OperationsBreakCaseDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1115` |
| `OperationsBrokerIntakeStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:51` |
| `OperationsChecklistAcknowledgeRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1058` |
| `OperationsChecklistControlApprovalDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:724` |
| `OperationsCloseCalendarDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:794` |
| `OperationsCloseCalendarItemAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:834` |
| `OperationsCloseCalendarItemDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:798` |
| `OperationsCloseCalendarItemUpsertRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:820` |
| `OperationsCloseCalendarItemUpsertResultDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:829` |
| `OperationsCloseChecklistTaskDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1023` |
| `OperationsClosePackagePublicationDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1040` |
| `OperationsCloseReadinessBlockerDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1194` |
| `OperationsCloseReadinessComponentDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1183` |
| `OperationsCloseReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1174` |
| `OperationsCloseWorkflowRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:691` |
| `OperationsContinuityCorrelationKeysDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1143` |
| `OperationsContinuityWorkflowDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:908` |
| `OperationsContinuityWorkflowSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:858` |
| `OperationsDashboardMetricDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:962` |
| `OperationsDashboardSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:946` |
| `OperationsEvidenceLinkDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1219` |
| `OperationsEvidencePackageSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:976` |
| `OperationsGateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1064` |
| `OperationsGateKeyDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:41` |
| `OperationsGatePostureRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:498` |
| `OperationsGateStatusDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:28` |
| `OperationsIssueCodeDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:439` |
| `OperationsJournalEntryMetadataDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:605` |
| `OperationsLedgerDraftRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:529` |
| `OperationsLedgerJournalCandidateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:567` |
| `OperationsLedgerJournalLineDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:589` |
| `OperationsLedgerPostRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:553` |
| `OperationsLedgerPostingStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:71` |
| `OperationsLedgerPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1161` |
| `OperationsLedgerValidationRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:543` |
| `OperationsNextActionDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1210` |
| `OperationsReconciliationLaneStatusDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:92` |
| `OperationsReconciliationLaneSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:993` |
| `OperationsReconciliationRunRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:620` |
| `OperationsReconciliationStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:81` |
| `OperationsRejectWorkflowRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:681` |
| `OperationsReopenWorkflowRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:711` |
| `OperationsReportPackReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1168` |
| `OperationsResolveBreakCaseRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:638` |
| `OperationsReviewedAutomationArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:891` |
| `OperationsReviewedAutomationSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:872` |
| `OperationsSecurityMasterOverrideApprovalRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:487` |
| `OperationsSecurityMasterResolveRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:518` |
| `OperationsSecurityMasterStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:61` |
| `OperationsStartWorkflowRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:468` |
| `OperationsSubmitApprovalRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:659` |
| `OperationsTimelineEntryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1075` |
| `OperationsTransitionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:479` |
| `OperationsTransitionResultDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:848` |
| `OperationsWorkflowAuditDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1095` |
| `OperationsWorkflowBlockerDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1202` |
| `OperationsWorkflowStatusDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:11` |
| `OperatorInboxDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:93` |
| `OperatorWorkItemDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:58` |
| `OperatorWorkItemKindDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:6` |
| `OperatorWorkItemToneDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:20` |
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
| `PortfolioLedgerDriftDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:47` |
| `PortfolioLedgerWorkflowStatusDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:39` |
| `PortfolioLedgerWorkflowStatusSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:52` |
| `PortfolioPositionSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:344` |
| `PortfolioReportingAnalyticsKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:272` |
| `PortfolioReportingAnalyticsRowDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:290` |
| `PortfolioReportingAnalyticsScopeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:280` |
| `PortfolioReportingCutDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:170` |
| `PortfolioReportingCutKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:151` |
| `PortfolioReportingLiveViewDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:209` |
| `PortfolioReportingLiveViewFreshnessPolicyDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:196` |
| `PortfolioReportingLiveViewStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:159` |
| `PortfolioReportingPnlSliceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:252` |
| `PortfolioReportingPnlSlicePeriodDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:241` |
| `PortfolioSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:308` |
| `PositionDiffEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:612` |
| `PostLedgerEntries` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:16` |
| `PrivateCapitalCloseCockpitApprovalDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1255` |
| `PrivateCapitalCloseCockpitDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1317` |
| `PrivateCapitalCloseCockpitLaneDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1240` |
| `PrivateCapitalCloseCockpitWorkflowDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1226` |
| `PrivateCapitalNavSupportComponentDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1270` |
| `PrivateCapitalNavSupportPackageDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1298` |
| `PrivateCapitalShadowNavTieOutDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1279` |
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
| `ProviderPromotionChecklistDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:300` |
| `ProviderSecurityMasterPassportDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:377` |
| `ProviderSecurityMasterPassportStatusDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:305` |
| `ProviderSecurityMasterScheduleFeedDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:464` |
| `ProviderShadowBookComparisonDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:410` |
| `ProviderShadowBookComparisonLineDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:399` |
| `PublishSecurityMasterRevisionRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:97` |
| `ReconciliationBreakCategory` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:50` |
| `ReconciliationBreakDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:120` |
| `ReconciliationBreakExplanationDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:435` |
| `ReconciliationBreakQueueItem` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:364` |
| `ReconciliationBreakQueueProjectionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:242` |
| `ReconciliationBreakQueueProjectionItemDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:257` |
| `ReconciliationBreakQueueStatus` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:280` |
| `ReconciliationBreakScore` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:576` |
| `ReconciliationBreakSeverity` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:37` |
| `ReconciliationBreakStatus` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:24` |
| `ReconciliationBulkCaseworkCaseResult` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:551` |
| `ReconciliationBulkCaseworkRequest` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:531` |
| `ReconciliationBulkCaseworkResult` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:558` |
| `ReconciliationCalibrationProfileSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:623` |
| `ReconciliationCalibrationStatusDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:354` |
| `ReconciliationCalibrationSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:640` |
| `ReconciliationCaseComment` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:443` |
| `ReconciliationCaseCommentVisibility` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:326` |
| `ReconciliationCaseLifecycleState` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:290` |
| `ReconciliationCasePriority` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:306` |
| `ReconciliationCaseSignoffRecord` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:567` |
| `ReconciliationCaseSlaState` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:315` |
| `ReconciliationCaseStateTransition` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:589` |
| `ReconciliationCaseTransitionAction` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:600` |
| `ReconciliationCaseTransitionCommand` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:610` |
| `ReconciliationCaseworkAction` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:334` |
| `ReconciliationCaseworkCommand` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:506` |
| `ReconciliationCorrelationContext` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:701` |
| `ReconciliationJobControl` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:724` |
| `ReconciliationMatchDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:106` |
| `ReconciliationPayloadEnvelope` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:711` |
| `ReconciliationProcessingTelemetry` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:739` |
| `ReconciliationRolloutFlags` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:750` |
| `ReconciliationRunDetail` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:261` |
| `ReconciliationRunRequest` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:68` |
| `ReconciliationRunSummary` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:82` |
| `ReconciliationSchemaVersion` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:691` |
| `ReconciliationSecurityCoverageIssueDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:138` |
| `ReconciliationSlaComputationResult` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:484` |
| `ReconciliationSlaPolicy` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:463` |
| `ReconciliationSourceKind` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:10` |
| `ReconciliationSummary` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:182` |
| `ReconciliationTaxonomySnapshot` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:501` |
| `ReconciliationTaxonomyValue` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:493` |
| `RejectWorkflow` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:21` |
| `RenderReportTemplateRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1419` |
| `RenderReportTemplateResponseDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1424` |
| `ReopenWorkflow` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:23` |
| `ReportAccessEvaluationDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1406` |
| `ReportAccessModeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1380` |
| `ReportAccessPolicyDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1399` |
| `ReportAccessPrincipalDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1394` |
| `ReportAccessPrincipalKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1388` |
| `ReportBrandingThemeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:137` |
| `ReportPackAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1485` |
| `ReportPackChangedLineDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1488` |
| `ReportPackCreateRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1538` |
| `ReportPackDeliveryAccessLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:853` |
| `ReportPackDeliveryApprovalStepDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:881` |
| `ReportPackDeliveryArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:842` |
| `ReportPackDeliveryAttemptDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:964` |
| `ReportPackDeliveryEvidencePacketDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:889` |
| `ReportPackDeliveryFailureRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:991` |
| `ReportPackDeliveryHistoryDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1000` |
| `ReportPackDeliveryModeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:835` |
| `ReportPackDeliveryNotificationDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:861` |
| `ReportPackDeliveryPackageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:913` |
| `ReportPackDeliveryRecipientDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:875` |
| `ReportPackDeliveryRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:981` |
| `ReportPackDeliveryStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:825` |
| `ReportPackEvidenceLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1487` |
| `ReportPackLineProvenanceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1489` |
| `ReportPackPublicationManifestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1507` |
| `ReportPackPublishRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1520` |
| `ReportPackRejectRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1533` |
| `ReportPackRejectionMetadataDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1553` |
| `ReportPackRestateRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1559` |
| `ReportPackRestatementMetadataDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1546` |
| `ReportPackWorkflowActionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1530` |
| `ReportPackWorkflowRecordDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1565` |
| `ReportPackWorkflowStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1146` |
| `ReportTemplateAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1440` |
| `ReportTemplateDecisionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1481` |
| `ReportTemplateDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1411` |
| `ReportTemplateDraftRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1470` |
| `ReportTemplateGovernanceRecordDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1448` |
| `ReportTemplateLifecycleStatusDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1432` |
| `ReportTemplateParameterDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1161` |
| `ReportWriterAggregateFunctionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1173` |
| `ReportWriterCellStyleDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1231` |
| `ReportWriterChartDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1256` |
| `ReportWriterChartRenderDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1267` |
| `ReportWriterChartSeriesDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1262` |
| `ReportWriterChartTypeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1248` |
| `ReportWriterDiffDirectionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1347` |
| `ReportWriterDiffRowStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1338` |
| `ReportWriterFilterDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1208` |
| `ReportWriterFilterLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1295` |
| `ReportWriterFilterOperatorDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1194` |
| `ReportWriterFormatRuleDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1239` |
| `ReportWriterFormulaDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1187` |
| `ReportWriterFormulaLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1290` |
| `ReportWriterGridColumnDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1275` |
| `ReportWriterGridDataDictionaryFieldDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1301` |
| `ReportWriterGridDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1214` |
| `ReportWriterGridDiffCellDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1353` |
| `ReportWriterGridDiffDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1367` |
| `ReportWriterGridDiffRowDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1360` |
| `ReportWriterGridKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1164` |
| `ReportWriterGridLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1315` |
| `ReportWriterGridRenderDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1324` |
| `ReportWriterGridRowDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1280` |
| `ReportWriterGridValidationCheckDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1310` |
| `ReportWriterMetricDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1181` |
| `ReportWriterMetricLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1285` |
| `ReportingDueScheduleRunResultDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1102` |
| `ReportingRunAuditEntryDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1119` |
| `ReportingRunAuditTrailDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1126` |
| `ReportingRunRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1106` |
| `ReportingRunResultDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1116` |
| `ReportingScheduleDeliveryPlanDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1010` |
| `ReportingScheduleDeliveryTargetDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1004` |
| `ReportingScheduleRecordDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1059` |
| `ReportingScheduleRunResultDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1096` |
| `ReportingScheduleStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1053` |
| `ReportingScheduleUpsertRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1080` |
| `ResearchBriefingAlert` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:95` |
| `ResearchBriefingDto` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:132` |
| `ResearchBriefingRun` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:53` |
| `ResearchBriefingWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:118` |
| `ResearchRunDrillInLinks` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:42` |
| `ResearchSavedComparison` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:84` |
| `ResearchSavedComparisonMode` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:73` |
| `ResearchWhatChangedItem` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:106` |
| `ResolveBreakCase` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:18` |
| `ResolveReconciliationBreakRequest` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:680` |
| `ResolveSecurityMasterMappings` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:12` |
| `ResolveSourceConflictRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:52` |
| `RestatementCandidateDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:138` |
| `ReviewReconciliationBreakRequest` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:670` |
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
| `SecurityMasterAccountingIssueDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:247` |
| `SecurityMasterChangeHistoryItemDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:165` |
| `SecurityMasterConflictAssessmentDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:116` |
| `SecurityMasterConflictAuthorityDecision` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:149` |
| `SecurityMasterConflictRecommendationKind` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:41` |
| `SecurityMasterConflictResolutionDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:113` |
| `SecurityMasterDownstreamImpactDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:316` |
| `SecurityMasterEconomicDefinitionDrillInDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:73` |
| `SecurityMasterEditOrigin` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:10` |
| `SecurityMasterEditResultDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:105` |
| `SecurityMasterEntitlementApplicabilityDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:454` |
| `SecurityMasterFactorPointDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:226` |
| `SecurityMasterIdentifierSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:131` |
| `SecurityMasterImpactLinkDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:332` |
| `SecurityMasterImpactSeverity` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:16` |
| `SecurityMasterLotModelDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:251` |
| `SecurityMasterManualChangeApprovalPostureDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:481` |
| `SecurityMasterOpenLotDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:274` |
| `SecurityMasterOpenLotProvenanceDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:305` |
| `SecurityMasterOpenLotReadModelDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:260` |
| `SecurityMasterOperatingModelDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:432` |
| `SecurityMasterOperatingModelStageDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:446` |
| `SecurityMasterOperatorMetadataDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:470` |
| `SecurityMasterProviderSymbolMappingDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:143` |
| `SecurityMasterPublishResultDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:126` |
| `SecurityMasterRecommendedActionDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:339` |
| `SecurityMasterRecommendedActionKind` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:26` |
| `SecurityMasterRevisionPublishedEvent` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:159` |
| `SecurityMasterRevisionStateDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:21` |
| `SecurityMasterScheduleBookDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:190` |
| `SecurityMasterScheduleEventDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:204` |
| `SecurityMasterScheduleProvenanceDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:238` |
| `SecurityMasterScheduleSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:180` |
| `SecurityMasterSchemaCompatibilityDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:156` |
| `SecurityMasterSourceCandidateDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:104` |
| `SecurityMasterTrustPostureDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:90` |
| `SecurityMasterTrustSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:48` |
| `SecurityMasterTrustTone` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:7` |
| `SecurityMasterWorkstationDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:43` |
| `SettlementInstruction` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:19` |
| `StartWorkflow` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:9` |
| `StatementBreakDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:175` |
| `StatementBreakType` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:53` |
| `StatementColumnConfidenceDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:50` |
| `StatementColumnMappingDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:57` |
| `StatementConnectorDescriptorDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:9` |
| `StatementFetchScheduleDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:130` |
| `StatementFetchScheduleUpsertRequestDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:143` |
| `StatementImportCommitResultDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:116` |
| `StatementImportIssueDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:64` |
| `StatementImportPreviewDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:101` |
| `StatementKindSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:91` |
| `StatementMappingProfileActivityCodeDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:43` |
| `StatementMappingProfileCsvOptionsDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:32` |
| `StatementMappingProfileDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:19` |
| `StatementMappingProfileFieldDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:37` |
| `StatementMatchSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:157` |
| `StatementMatchTier` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:39` |
| `StatementNormalizedCashDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:125` |
| `StatementNormalizedPositionDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:106` |
| `StatementNormalizedTransactionDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:138` |
| `StatementProfileSuggestionDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:97` |
| `StatementReconciliationBreakExplanationDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:248` |
| `StatementReconciliationCaseAttachmentDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:239` |
| `StatementReconciliationCaseAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:257` |
| `StatementReconciliationCaseCommentDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:232` |
| `StatementReconciliationCaseCommentThreadDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:227` |
| `StatementReconciliationCaseDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:204` |
| `StatementRecordPreviewDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:73` |
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
| `StrategyRunReviewPacketDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:343` |
| `StrategyRunStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:35` |
| `StrategyRunSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:198` |
| `StrategyRunTimelineEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:491` |
| `StrategyRunTimelineProjection` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:466` |
| `StrategySavedComparison` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:48` |
| `StrategySavedComparisonMode` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:37` |
| `StrategySweepObjectiveRanking` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:230` |
| `StrategySweepResultGroup` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:239` |
| `StrategyWhatChangedItem` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:70` |
| `StructuredReportingExportColumnDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:385` |
| `StructuredReportingExportDataDictionaryFieldDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:390` |
| `StructuredReportingExportDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:359` |
| `StructuredReportingExportPayloadDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:407` |
| `StructuredReportingExportPurposeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:349` |
| `StructuredReportingExportRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:420` |
| `StructuredReportingExportRowLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:402` |
| `StructuredReportingExportValidationCheckDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:397` |
| `SubmitForApproval` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:19` |
| `SubmitSecurityMasterRevisionRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:67` |
| `SymbolAttributionEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:690` |
| `SyncCompletenessResult` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:13` |
| `SyncValidationResult` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:16` |
| `ThresholdBreachDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:35` |
| `TradingAcceptanceGateDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:105` |
| `TradingAcceptanceGateStatusDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:29` |
| `TradingControlEvidenceDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:171` |
| `TradingControlReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:187` |
| `TradingOperatorReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:312` |
| `TradingOperatorSignoffReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:219` |
| `TradingPaperSessionReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:134` |
| `TradingPromotionReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:205` |
| `TradingReplayReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:156` |
| `TradingReportPackReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:289` |
| `TradingTrustGateContractReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:247` |
| `TradingTrustGateEvidenceDocumentDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:239` |
| `TradingTrustGateReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:257` |
| `TradingTrustGateSampleReviewDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:228` |
| `UpdateSecurityFieldRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:35` |
| `ValidateLedgerDraft` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:15` |
| `VersionedReportTemplateIdDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1159` |
| `WorkflowActionDto` | Documented | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:29` |
| `WorkflowBlockerSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:42` |
| `WorkflowDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:14` |
| `WorkflowEvidenceBadge` | Documented | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:52` |
| `WorkflowLibraryDto` | Documented | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:6` |
| `WorkflowNextAction` | Documented | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:33` |
| `WorkflowPresetDto` | Documented | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:43` |
| `WorkflowPresetLibraryDto` | Documented | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:63` |
| `WorkflowPresetPinRequest` | Documented | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:82` |
| `WorkflowPresetSaveRequest` | Documented | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:70` |
| `WorkspaceWorkflowSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:20` |
| `WorkstationAccountingAgingBucketPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:739` |
| `WorkstationAccountingAlertPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:756` |
| `WorkstationAccountingCashFlowSummaryPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:707` |
| `WorkstationAccountingControlCenterPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:760` |
| `WorkstationAccountingDrillLinkPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:752` |
| `WorkstationAccountingOwnerWorkloadPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:743` |
| `WorkstationAccountingPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:822` |
| `WorkstationAccountingRunCashFlowPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:696` |
| `WorkstationAccountingRunGovernancePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:679` |
| `WorkstationAccountingRunReconciliationPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:685` |
| `WorkstationAccountingRunRecord` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:717` |
| `WorkstationAccountingSeverityCountPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:735` |
| `WorkstationAccountingTrendSnapshotPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:747` |
| `WorkstationAccountingWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:411` |
| `WorkstationBrokerageAccountDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:42` |
| `WorkstationBrokerageAccountLinkDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:51` |
| `WorkstationBrokerageSyncHealth` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:7` |
| `WorkstationBrokerageSyncRunRequestDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:35` |
| `WorkstationBrokerageSyncStatusDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:67` |
| `WorkstationDataBackfillRecord` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1026` |
| `WorkstationDataExportRecord` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1037` |
| `WorkstationDataPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1049` |
| `WorkstationDataProviderDiagnostic` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:982` |
| `WorkstationDataProviderRecord` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1004` |
| `WorkstationDataProviderRoutingSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:992` |
| `WorkstationGeneratedReportWriterGridPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:549` |
| `WorkstationKernelAlertThresholdsPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:782` |
| `WorkstationKernelCriticalSeverityRatePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:789` |
| `WorkstationKernelDomainPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:798` |
| `WorkstationKernelDriftPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:777` |
| `WorkstationKernelLatencyPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:772` |
| `WorkstationKernelObservabilityPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:809` |
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
| `WorkstationPortfolioPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:860` |
| `WorkstationPortfolioRunRow` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:841` |
| `WorkstationPortfolioSummaryPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:876` |
| `WorkstationPortfolioSummaryTelemetry` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:867` |
| `WorkstationReportAccessAuditSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:633` |
| `WorkstationReportPackDistributionPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:617` |
| `WorkstationReportWriterDatasetSourcePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:458` |
| `WorkstationReportWriterFieldPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:450` |
| `WorkstationReportWriterFilterPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:444` |
| `WorkstationReportWriterFormulaPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:439` |
| `WorkstationReportWriterGridPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:479` |
| `WorkstationReportWriterMetricPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:434` |
| `WorkstationReportingDailyWorkItemDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:598` |
| `WorkstationReportingPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:652` |
| `WorkstationReportingProfilePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:422` |
| `WorkstationReportingRunLinkPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:531` |
| `WorkstationReportingRunNextActionPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:539` |
| `WorkstationReportingRunPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:562` |
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

- Document or intentionally suppress 15 workstation contract gap(s).

---

*This dashboard is auto-generated. Do not edit manually.*
