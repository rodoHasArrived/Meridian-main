# API Contract Coverage Dashboard

_Auto-generated from canonical JSON payload._
_Generated: 2026-06-19T00:53:24.330883+00:00_
Data sources: `src/**/*.cs endpoint mappings`, `src/Meridian.Contracts/Api/UiApiRoutes.cs`, `src/Meridian.Contracts/Workstation/*.cs`, `docs/**/*.md`


Tracks whether mapped API routes and workstation DTO contracts are visible in the Markdown documentation set.

## Summary

| Metric | Value |
|---|---:|
| Weighted score | 30.3% |
| Endpoint coverage | 42.4% |
| Workstation contract coverage | 12.1% |
| Endpoints documented | 223 / 526 |
| Workstation contracts documented | 83 / 688 |

## Endpoint Coverage

| Method | Route | Status | Source |
|---|---|---|---|
| `GET` | `/api/accounting-system/import/latest` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:50` |
| `POST` | `/api/accounting-system/import/preview` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:32` |
| `GET` | `/api/accounting-system/providers` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:17` |
| `GET` | `/api/accounting-system/reconciliation/latest` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:68` |
| `POST` | `/api/admin/cleanup/execute` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:272` |
| `GET` | `/api/admin/cleanup/preview` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:232` |
| `GET` | `/api/admin/error-codes` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:349` |
| `GET` | `/api/admin/maintenance/history` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:83` |
| `POST` | `/api/admin/maintenance/run` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:46` |
| `GET` | `/api/admin/maintenance/run/{runId}` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:70` |
| `GET` | `/api/admin/maintenance/schedule` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:26` |
| `GET` | `/api/admin/quick-check` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:381` |
| `GET` | `/api/admin/retention` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:179` |
| `POST` | `/api/admin/retention/apply` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:216` |
| `GET` | `/api/admin/retention/compliance-report` | Gap | `src/Meridian.Ui.Shared/Endpoints/ResilienceEndpoints.cs:88` |
| `DELETE` | `/api/admin/retention/{policyId}/delete` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:199` |
| `POST` | `/api/admin/selftest` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:323` |
| `GET` | `/api/admin/show-config` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:362` |
| `POST` | `/api/admin/storage/migrate/{targetTier}` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:116` |
| `GET` | `/api/admin/storage/permissions` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:289` |
| `GET` | `/api/admin/storage/tiers` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:101` |
| `GET` | `/api/admin/storage/usage` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:138` |
| `POST` | `/api/alignment/create` | Gap | `src/Meridian.Ui.Shared/Endpoints/HistoricalEndpoints.cs:176` |
| `POST` | `/api/alignment/preview` | Gap | `src/Meridian.Ui.Shared/Endpoints/HistoricalEndpoints.cs:196` |
| `GET` | `/api/analytics/anomalies` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:87` |
| `GET` | `/api/analytics/compare` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:51` |
| `GET` | `/api/analytics/completeness` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:113` |
| `GET` | `/api/analytics/gaps` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:24` |
| `POST` | `/api/analytics/gaps/repair` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:36` |
| `GET` | `/api/analytics/latency` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:63` |
| `GET` | `/api/analytics/latency/stats` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:75` |
| `GET` | `/api/analytics/quality-report` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:101` |
| `GET` | `/api/analytics/rate-limits` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:143` |
| `GET` | `/api/analytics/throughput` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:131` |
| `GET` | `/api/auth/access-assignments` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:465` |
| `POST` | `/api/auth/access-assignments` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:506` |
| `POST` | `/api/auth/access-assignments/{assignmentId}/revoke` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:546` |
| `GET` | `/api/auth/accounts` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:177` |
| `PUT` | `/api/auth/accounts/{username}` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:200` |
| `POST` | `/api/auth/accounts/{username}/disable` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:298` |
| `POST` | `/api/auth/accounts/{username}/password-reset` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:238` |
| `GET` | `/api/auth/audit` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:389` |
| `POST` | `/api/auth/login` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:35` |
| `POST` | `/api/auth/logout` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:130` |
| `GET` | `/api/auth/me` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:150` |
| `POST` | `/api/auth/role-profiles` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:413` |
| `GET` | `/api/auth/roles` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:171` |
| `POST` | `/api/auth/sessions/revoke` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:358` |
| `GET` | `/api/backfill/checkpoints` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:25` |
| `GET` | `/api/backfill/checkpoints/resumable` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:91` |
| `GET` | `/api/backfill/checkpoints/validation` | Gap | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:37` |
| `GET` | `/api/backfill/checkpoints/{jobId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:118` |
| `GET` | `/api/backfill/checkpoints/{jobId}/pending` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:144` |
| `POST` | `/api/backfill/checkpoints/{jobId}/resume` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:204` |
| `GET` | `/api/backfill/completeness` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:217` |
| `POST` | `/api/backfill/cost-estimate` | Gap | `src/Meridian.Ui.Shared/Endpoints/ResilienceEndpoints.cs:48` |
| `GET` | `/api/backfill/executions` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:107` |
| `POST` | `/api/backfill/gap-fill` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:64` |
| `GET` | `/api/backfill/gaps` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:175` |
| `GET` | `/api/backfill/health` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:24` |
| `GET` | `/api/backfill/presets` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:92` |
| `GET` | `/api/backfill/progress` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:111` |
| `GET` | `/api/backfill/providers` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:32` |
| `GET` | `/api/backfill/providers/audit` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:175` |
| `POST` | `/api/backfill/providers/dry-run-plan` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:156` |
| `GET` | `/api/backfill/providers/fallback-chain` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:144` |
| `GET` | `/api/backfill/providers/metadata` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:123` |
| `GET` | `/api/backfill/providers/statuses` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:133` |
| `GET` | `/api/backfill/resolve/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:46` |
| `POST` | `/api/backfill/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:83` |
| `POST` | `/api/backfill/run/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:55` |
| `GET` | `/api/backfill/schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:162` |
| `POST` | `/api/backfill/schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:177` |
| `GET` | `/api/backfill/schedules/templates` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:287` |
| `DELETE` | `/api/backfill/schedules/{id}` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:203` |
| `GET` | `/api/backfill/schedules/{id}` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:192` |
| `POST` | `/api/backfill/schedules/{id}/disable` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:234` |
| `POST` | `/api/backfill/schedules/{id}/enable` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:219` |
| `GET` | `/api/backfill/schedules/{id}/history` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:274` |
| `POST` | `/api/backfill/schedules/{id}/run` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:249` |
| `GET` | `/api/backfill/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:132` |
| `GET` | `/api/backfill/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:42` |
| `GET` | `/api/backfill/validation` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:25` |
| `GET` | `/api/backfill/validation/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:115` |
| `GET` | `/api/backpressure` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:118` |
| `GET` | `/api/calendar/holidays` | Gap | `src/Meridian.Ui.Shared/Endpoints/CalendarEndpoints.cs:64` |
| `GET` | `/api/calendar/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/CalendarEndpoints.cs:21` |
| `GET` | `/api/calendar/trading-days` | Gap | `src/Meridian.Ui.Shared/Endpoints/CalendarEndpoints.cs:89` |
| `GET` | `/api/canonicalization/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:112` |
| `GET` | `/api/canonicalization/parity` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:47` |
| `GET` | `/api/canonicalization/parity/{provider}` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:75` |
| `GET` | `/api/canonicalization/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:20` |
| `GET` | `/api/catalog/coverage` | Gap | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:247` |
| `GET` | `/api/catalog/search` | Gap | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:24` |
| `GET` | `/api/catalog/symbols` | Gap | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:133` |
| `GET` | `/api/catalog/timeline` | Gap | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:170` |
| `GET` | `/api/compliance/access-reviews` | Gap | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:61` |
| `POST` | `/api/compliance/access-reviews/run` | Gap | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:52` |
| `POST` | `/api/compliance/actions/evaluate` | Gap | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:15` |
| `GET` | `/api/compliance/audit/extract` | Gap | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:33` |
| `GET` | `/api/compliance/controls/attestation` | Gap | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:37` |
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
| `GET` | `/api/data/bbo/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:329` |
| `GET` | `/api/data/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:410` |
| `GET` | `/api/data/l3-orderbook/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:259` |
| `GET` | `/api/data/orderbook/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:187` |
| `GET` | `/api/data/orderflow/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:369` |
| `GET` | `/api/data/quotes-snapshot` | Gap | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:109` |
| `GET` | `/api/data/quotes/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:65` |
| `GET` | `/api/data/trades/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:25` |
| `GET` | `/api/demo/historical/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:118` |
| `GET` | `/api/demo/market-data/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:102` |
| `GET` | `/api/demo/mode` | Gap | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:71` |
| `GET` | `/api/demo/symbols` | Gap | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:88` |
| `POST` | `/api/dev/seed/bank-transactions` | Documented | `src/Meridian.Ui.Shared/Endpoints/BankingEndpoints.cs:254` |
| `GET` | `/api/diagnostics/bundle` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:148` |
| `GET` | `/api/diagnostics/config` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:122` |
| `GET` | `/api/diagnostics/coordination` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:458` |
| `POST` | `/api/diagnostics/dry-run` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:35` |
| `GET` | `/api/diagnostics/error-codes` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:356` |
| `GET` | `/api/diagnostics/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:169` |
| `GET` | `/api/diagnostics/providers` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:55` |
| `POST` | `/api/diagnostics/providers/{providerName}/test` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:293` |
| `GET` | `/api/diagnostics/quick-check` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:315` |
| `POST` | `/api/diagnostics/selftest` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:366` |
| `GET` | `/api/diagnostics/show-config` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:332` |
| `GET` | `/api/diagnostics/storage` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:72` |
| `POST` | `/api/diagnostics/test-connectivity` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:405` |
| `POST` | `/api/diagnostics/validate` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:271` |
| `POST` | `/api/diagnostics/validate-config` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:429` |
| `POST` | `/api/diagnostics/validate-credentials` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:387` |
| `GET` | `/api/errors` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:107` |
| `GET` | `/api/events/stream` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:206` |
| `POST` | `/api/export/analysis` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:81` |
| `GET` | `/api/export/formats` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:121` |
| `POST` | `/api/export/integrity` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:255` |
| `POST` | `/api/export/orderflow` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:204` |
| `GET` | `/api/export/preview` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:28` |
| `POST` | `/api/export/quality-report` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:160` |
| `POST` | `/api/export/research-package` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:341` |
| `POST` | `/api/export/strategy-package` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:336` |
| `GET` | `/api/failover/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:34` |
| `POST` | `/api/failover/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:71` |
| `GET` | `/api/failover/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:226` |
| `GET` | `/api/failover/rules` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:94` |
| `POST` | `/api/failover/rules` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:124` |
| `GET` | `/api/funds/{fundId:guid}/accounts` | Documented | `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs:127` |
| `GET` | `/api/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:37` |
| `GET` | `/api/health/detailed` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:182` |
| `GET` | `/api/health/diagnostics/bundle` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:230` |
| `GET` | `/api/health/events` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:172` |
| `GET` | `/api/health/metrics` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:185` |
| `GET` | `/api/health/providers` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:55` |
| `GET` | `/api/health/providers/{provider}/diagnostics` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:95` |
| `POST` | `/api/health/providers/{provider}/test` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:204` |
| `GET` | `/api/health/storage` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:137` |
| `GET` | `/api/health/summary` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:28` |
| `GET` | `/api/indices/{indexName}/constituents` | Gap | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:558` |
| `GET` | `/api/ingestion/jobs` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:29` |
| `POST` | `/api/ingestion/jobs` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:110` |
| `GET` | `/api/ingestion/jobs/resumable` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:149` |
| `DELETE` | `/api/ingestion/jobs/{jobId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:171` |
| `GET` | `/api/ingestion/jobs/{jobId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:51` |
| `POST` | `/api/ingestion/jobs/{jobId}/transition` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:66` |
| `GET` | `/api/ingestion/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:160` |
| `POST` | `/api/journals/{journalEntryId:guid}/post` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:600` |
| `GET` | `/api/lean/algorithms` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:149` |
| `GET` | `/api/lean/auto-export` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:389` |
| `POST` | `/api/lean/auto-export/configure` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:420` |
| `GET` | `/api/lean/backtest/history` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:335` |
| `POST` | `/api/lean/backtest/start` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:250` |
| `DELETE` | `/api/lean/backtest/{backtestId}/delete` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:376` |
| `GET` | `/api/lean/backtest/{backtestId}/results` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:300` |
| `GET` | `/api/lean/backtest/{backtestId}/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:282` |
| `POST` | `/api/lean/backtest/{backtestId}/stop` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:320` |
| `GET` | `/api/lean/config` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:60` |
| `POST` | `/api/lean/results/ingest` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:455` |
| `GET` | `/api/lean/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:32` |
| `GET` | `/api/lean/symbol-map` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:549` |
| `POST` | `/api/lean/sync` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:179` |
| `GET` | `/api/lean/sync/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:230` |
| `POST` | `/api/lean/verify` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:87` |
| `GET` | `/api/ledger/accounting-configuration` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:386` |
| `POST` | `/api/ledger/accounting-configuration/activate` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:521` |
| `GET` | `/api/ledger/accounting-configuration/audit` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:555` |
| `POST` | `/api/ledger/accounting-configuration/chart` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:410` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:470` |
| `POST` | `/api/ledger/accounting-configuration/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:500` |
| `POST` | `/api/ledger/accounting-configuration/templates` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:440` |
| `GET` | `/api/ledger/books` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:20` |
| `POST` | `/api/ledger/books` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:69` |
| `GET` | `/api/ledger/books/{ledgerBookId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:46` |
| `GET` | `/api/ledger/journal-entry-workbench` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:579` |
| `POST` | `/api/ledger/journal-entry-workbench/drafts` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:888` |
| `POST` | `/api/ledger/journal-entry-workbench/submit-approval` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:952` |
| `POST` | `/api/ledger/journal-entry-workbench/validate` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:923` |
| `GET` | `/api/ledger/periods` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:99` |
| `POST` | `/api/ledger/periods` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:136` |
| `POST` | `/api/ledger/periods/{periodId:guid}/close` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:167` |
| `GET` | `/api/ledger/periods/{periodId:guid}/pnl-summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:258` |
| `GET` | `/api/ledger/periods/{periodId:guid}/trial-balance` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:210` |
| `GET` | `/api/ledger/periods/{periodId:guid}/trial-balance-report` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:234` |
| `GET` | `/api/ledger/private-capital/activity` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:603` |
| `GET` | `/api/ledger/private-capital/capital-account-subledger` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:714` |
| `GET` | `/api/ledger/private-capital/capital-account-workbench` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:842` |
| `GET` | `/api/ledger/private-capital/fund-event-command-center` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:674` |
| `GET` | `/api/ledger/private-capital/fund-event-record` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:633` |
| `GET` | `/api/ledger/private-capital/report-output` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:773` |
| `GET` | `/api/ledger/reports/pnl-summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:334` |
| `GET` | `/api/ledger/reports/trial-balance` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:282` |
| `GET` | `/api/loans/portfolio` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:750` |
| `POST` | `/api/loans/rebuild-all` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:761` |
| `GET` | `/api/loans/rebuild-checkpoints` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:741` |
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
| `DELETE` | `/api/maintenance/schedules/{id}/delete` | Gap | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:59` |
| `POST` | `/api/maintenance/schedules/{id}/disable` | Gap | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:87` |
| `POST` | `/api/maintenance/schedules/{id}/enable` | Gap | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:73` |
| `GET` | `/api/maintenance/schedules/{id}/history` | Gap | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:123` |
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
| `GET` | `/api/messaging/activity` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:97` |
| `GET` | `/api/messaging/config` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:30` |
| `GET` | `/api/messaging/consumers` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:126` |
| `GET` | `/api/messaging/endpoints` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:145` |
| `GET` | `/api/messaging/errors` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:306` |
| `POST` | `/api/messaging/errors/{messageId}/retry` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:327` |
| `GET` | `/api/messaging/publishing` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:264` |
| `POST` | `/api/messaging/queues/{queueName}/purge` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:281` |
| `GET` | `/api/messaging/stats` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:69` |
| `GET` | `/api/messaging/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:51` |
| `POST` | `/api/messaging/test` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:172` |
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
| `GET` | `/api/plaid/accounts` | Gap | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:31` |
| `GET` | `/api/plaid/institutions/search` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:44` |
| `GET` | `/api/plaid/items` | Gap | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:18` |
| `POST` | `/api/plaid/items/{itemId}/sync` | Gap | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:124` |
| `POST` | `/api/plaid/link-token` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:74` |
| `POST` | `/api/plaid/public-token/exchange` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:99` |
| `POST` | `/api/plaid/transfers/sandbox` | Gap | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:176` |
| `POST` | `/api/plaid/webhook` | Gap | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:154` |
| `GET` | `/api/portfolio/household` | Gap | `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs:259` |
| `GET` | `/api/projections/{projectionRunId:guid}/flows` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:583` |
| `GET` | `/api/provider-routing/bindings` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:36` |
| `GET` | `/api/provider-routing/connections` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:20` |
| `POST` | `/api/provider-routing/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:68` |
| `GET` | `/api/provider-routing/trust-snapshots` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:52` |
| `GET` | `/api/providers/capabilities` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:145` |
| `GET` | `/api/providers/catalog` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:535` |
| `GET` | `/api/providers/catalog/{providerId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:574` |
| `GET` | `/api/providers/comparison` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:276` |
| `POST` | `/api/providers/configure` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:253` |
| `GET` | `/api/providers/connections` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:19` |
| `GET` | `/api/providers/dashboard` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:278` |
| `GET` | `/api/providers/failover` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:54` |
| `GET` | `/api/providers/failover-thresholds` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:211` |
| `POST` | `/api/providers/failover/reset` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:96` |
| `POST` | `/api/providers/failover/trigger` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:80` |
| `GET` | `/api/providers/health` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:228` |
| `GET` | `/api/providers/ib/error-codes` | Documented | `src/Meridian.Ui.Shared/Endpoints/IBEndpoints.cs:91` |
| `GET` | `/api/providers/ib/limits` | Documented | `src/Meridian.Ui.Shared/Endpoints/IBEndpoints.cs:113` |
| `GET` | `/api/providers/ib/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/IBEndpoints.cs:24` |
| `GET` | `/api/providers/latency` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:129` |
| `GET` | `/api/providers/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:451` |
| `GET` | `/api/providers/rate-limits` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:111` |
| `GET` | `/api/providers/readiness` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:347` |
| `GET` | `/api/providers/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:359` |
| `POST` | `/api/providers/switch` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:164` |
| `DELETE` | `/api/providers/{providerId}/credentials` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:87` |
| `PUT` | `/api/providers/{providerId}/credentials` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:34` |
| `POST` | `/api/providers/{providerId}/verify` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:63` |
| `GET` | `/api/providers/{providerName}` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:24` |
| `GET` | `/api/providers/{providerName}/rate-limit-history` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:130` |
| `POST` | `/api/providers/{providerName}/test` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:191` |
| `POST` | `/api/providers/{provider}/test-connection` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderCredentialEndpoints.cs:69` |
| `POST` | `/api/providers/{provider}/validate-credentials` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderCredentialEndpoints.cs:26` |
| `GET` | `/api/quality/anomalies` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:174` |
| `GET` | `/api/quality/anomalies/stale` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:218` |
| `GET` | `/api/quality/anomalies/statistics` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:215` |
| `GET` | `/api/quality/anomalies/unacknowledged` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:203` |
| `POST` | `/api/quality/anomalies/{anomalyId}/acknowledge` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:206` |
| `GET` | `/api/quality/anomalies/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:200` |
| `GET` | `/api/quality/comparison/discrepancies` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:253` |
| `GET` | `/api/quality/comparison/statistics` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:265` |
| `GET` | `/api/quality/comparison/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:246` |
| `GET` | `/api/quality/completeness` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:77` |
| `GET` | `/api/quality/completeness/low` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:102` |
| `GET` | `/api/quality/completeness/summary` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:99` |
| `GET` | `/api/quality/completeness/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:84` |
| `GET` | `/api/quality/dashboard` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:69` |
| `GET` | `/api/quality/drops` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:329` |
| `GET` | `/api/quality/drops/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:354` |
| `GET` | `/api/quality/errors` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:147` |
| `GET` | `/api/quality/errors/statistics` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:166` |
| `GET` | `/api/quality/errors/top-symbols` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:169` |
| `GET` | `/api/quality/errors/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:159` |
| `GET` | `/api/quality/gaps` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:111` |
| `GET` | `/api/quality/gaps/statistics` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:138` |
| `GET` | `/api/quality/gaps/timeline/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:130` |
| `GET` | `/api/quality/gaps/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:123` |
| `GET` | `/api/quality/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:312` |
| `GET` | `/api/quality/health/unhealthy` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:345` |
| `GET` | `/api/quality/health/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:336` |
| `GET` | `/api/quality/latency` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:223` |
| `GET` | `/api/quality/latency/high` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:241` |
| `GET` | `/api/quality/latency/statistics` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:238` |
| `GET` | `/api/quality/latency/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:226` |
| `GET` | `/api/quality/latency/{symbol}/histogram` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:235` |
| `GET` | `/api/quality/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:72` |
| `GET` | `/api/quality/reports/daily` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:270` |
| `POST` | `/api/quality/reports/export` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:297` |
| `GET` | `/api/quality/reports/weekly` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:278` |
| `POST` | `/api/quant/parameters` | Gap | `src/Meridian.Ui.Shared/Endpoints/QuantLabEndpoints.cs:87` |
| `POST` | `/api/quant/run` | Gap | `src/Meridian.Ui.Shared/Endpoints/QuantLabEndpoints.cs:35` |
| `GET` | `/api/quant/templates` | Gap | `src/Meridian.Ui.Shared/Endpoints/QuantLabEndpoints.cs:123` |
| `GET` | `/api/reconciliation/exceptions` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:659` |
| `POST` | `/api/reconciliation/exceptions/{exceptionId:guid}/resolve` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:668` |
| `GET` | `/api/reconciliation/{runId:guid}/results` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:650` |
| `GET` | `/api/reference-data/bonds/issuer-ladder` | Gap | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:63` |
| `GET` | `/api/reference-data/bonds/maturity-ladder` | Gap | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:87` |
| `GET` | `/api/reference-data/bonds/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:24` |
| `GET` | `/api/reference-data/bonds/{securityId:guid}/accrual-convention` | Gap | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:50` |
| `GET` | `/api/reference-data/bonds/{securityId:guid}/lifecycle` | Gap | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:37` |
| `GET` | `/api/reference-data/certificates-of-deposit/by-issuer` | Gap | `src/Meridian.Ui.Shared/Endpoints/CertificateOfDepositReferenceEndpoints.cs:29` |
| `GET` | `/api/reference-data/certificates-of-deposit/maturing-before` | Gap | `src/Meridian.Ui.Shared/Endpoints/CertificateOfDepositReferenceEndpoints.cs:40` |
| `GET` | `/api/reference-data/certificates-of-deposit/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/CertificateOfDepositReferenceEndpoints.cs:17` |
| `GET` | `/api/reference-data/commodities/by-exchange` | Gap | `src/Meridian.Ui.Shared/Endpoints/CommodityReferenceEndpoints.cs:40` |
| `GET` | `/api/reference-data/commodities/by-type` | Gap | `src/Meridian.Ui.Shared/Endpoints/CommodityReferenceEndpoints.cs:29` |
| `GET` | `/api/reference-data/commodities/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/CommodityReferenceEndpoints.cs:17` |
| `GET` | `/api/reference-data/crypto/by-base-currency` | Gap | `src/Meridian.Ui.Shared/Endpoints/CryptoReferenceEndpoints.cs:48` |
| `GET` | `/api/reference-data/crypto/by-network` | Gap | `src/Meridian.Ui.Shared/Endpoints/CryptoReferenceEndpoints.cs:35` |
| `GET` | `/api/reference-data/crypto/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/CryptoReferenceEndpoints.cs:21` |
| `GET` | `/api/reference-data/deposits/by-institution` | Gap | `src/Meridian.Ui.Shared/Endpoints/DepositReferenceEndpoints.cs:35` |
| `GET` | `/api/reference-data/deposits/maturing-before` | Gap | `src/Meridian.Ui.Shared/Endpoints/DepositReferenceEndpoints.cs:48` |
| `GET` | `/api/reference-data/deposits/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DepositReferenceEndpoints.cs:21` |
| `GET` | `/api/reference-data/edgar/facts/{cik}` | Documented | `src/Meridian.Ui.Shared/Endpoints/EdgarReferenceDataEndpoints.cs:63` |
| `GET` | `/api/reference-data/edgar/filers/{cik}` | Documented | `src/Meridian.Ui.Shared/Endpoints/EdgarReferenceDataEndpoints.cs:49` |
| `GET` | `/api/reference-data/edgar/security-data/{cik}` | Documented | `src/Meridian.Ui.Shared/Endpoints/EdgarReferenceDataEndpoints.cs:77` |
| `GET` | `/api/reference-data/equities/by-exchange` | Gap | `src/Meridian.Ui.Shared/Endpoints/EquityReferenceEndpoints.cs:29` |
| `GET` | `/api/reference-data/equities/by-issuer` | Gap | `src/Meridian.Ui.Shared/Endpoints/EquityReferenceEndpoints.cs:40` |
| `GET` | `/api/reference-data/equities/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/EquityReferenceEndpoints.cs:17` |
| `GET` | `/api/reference-data/futures/by-root` | Gap | `src/Meridian.Ui.Shared/Endpoints/FutureReferenceEndpoints.cs:35` |
| `GET` | `/api/reference-data/futures/expiry-ladder` | Gap | `src/Meridian.Ui.Shared/Endpoints/FutureReferenceEndpoints.cs:48` |
| `GET` | `/api/reference-data/futures/front-month` | Gap | `src/Meridian.Ui.Shared/Endpoints/FutureReferenceEndpoints.cs:61` |
| `GET` | `/api/reference-data/futures/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/FutureReferenceEndpoints.cs:21` |
| `GET` | `/api/reference-data/fxspot/by-currency` | Gap | `src/Meridian.Ui.Shared/Endpoints/FxSpotReferenceEndpoints.cs:41` |
| `GET` | `/api/reference-data/fxspot/pairs/{pairCode}` | Gap | `src/Meridian.Ui.Shared/Endpoints/FxSpotReferenceEndpoints.cs:29` |
| `GET` | `/api/reference-data/fxspot/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/FxSpotReferenceEndpoints.cs:17` |
| `GET` | `/api/reference-data/money-market-funds/by-family` | Gap | `src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundReferenceEndpoints.cs:29` |
| `GET` | `/api/reference-data/money-market-funds/by-sweep-eligibility` | Gap | `src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundReferenceEndpoints.cs:40` |
| `GET` | `/api/reference-data/money-market-funds/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundReferenceEndpoints.cs:17` |
| `POST` | `/api/reference-data/options/chains/import` | Gap | `src/Meridian.Ui.Shared/Endpoints/OptionChainEndpoints.cs:19` |
| `GET` | `/api/reference-data/options/chains/snapshot` | Gap | `src/Meridian.Ui.Shared/Endpoints/OptionChainEndpoints.cs:44` |
| `GET` | `/api/reference-data/options/contracts/{contractSymbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/OptionReferenceEndpoints.cs:17` |
| `GET` | `/api/reference-data/options/contracts/{contractSymbol}/underlying-linkage` | Gap | `src/Meridian.Ui.Shared/Endpoints/OptionReferenceEndpoints.cs:42` |
| `GET` | `/api/reference-data/options/expiry-ladder` | Gap | `src/Meridian.Ui.Shared/Endpoints/OptionReferenceEndpoints.cs:54` |
| `GET` | `/api/reference-data/options/series/{optionChainId}` | Gap | `src/Meridian.Ui.Shared/Endpoints/OptionReferenceEndpoints.cs:29` |
| `GET` | `/api/reference-data/swaps/by-type` | Gap | `src/Meridian.Ui.Shared/Endpoints/SwapReferenceEndpoints.cs:29` |
| `GET` | `/api/reference-data/swaps/maturing-before` | Gap | `src/Meridian.Ui.Shared/Endpoints/SwapReferenceEndpoints.cs:40` |
| `GET` | `/api/reference-data/swaps/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/SwapReferenceEndpoints.cs:17` |
| `GET` | `/api/replay/files` | Gap | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:28` |
| `GET` | `/api/replay/preview` | Gap | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:196` |
| `POST` | `/api/replay/start` | Gap | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:68` |
| `GET` | `/api/replay/stats` | Gap | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:276` |
| `POST` | `/api/replay/{sessionId}/pause` | Gap | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:100` |
| `POST` | `/api/replay/{sessionId}/resume` | Gap | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:114` |
| `POST` | `/api/replay/{sessionId}/seek` | Gap | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:142` |
| `POST` | `/api/replay/{sessionId}/speed` | Gap | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:155` |
| `GET` | `/api/replay/{sessionId}/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:169` |
| `POST` | `/api/replay/{sessionId}/stop` | Gap | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:128` |
| `GET` | `/api/resilience/circuit-breakers` | Gap | `src/Meridian.Ui.Shared/Endpoints/ResilienceEndpoints.cs:25` |
| `POST` | `/api/sampling/create` | Gap | `src/Meridian.Ui.Shared/Endpoints/SamplingEndpoints.cs:24` |
| `GET` | `/api/sampling/estimate` | Gap | `src/Meridian.Ui.Shared/Endpoints/SamplingEndpoints.cs:111` |
| `GET` | `/api/sampling/saved` | Gap | `src/Meridian.Ui.Shared/Endpoints/SamplingEndpoints.cs:153` |
| `GET` | `/api/sampling/{sampleId}` | Gap | `src/Meridian.Ui.Shared/Endpoints/SamplingEndpoints.cs:177` |
| `POST` | `/api/schedules/cron/next-runs` | Gap | `src/Meridian.Ui.Shared/Endpoints/CronEndpoints.cs:42` |
| `POST` | `/api/schedules/cron/validate` | Gap | `src/Meridian.Ui.Shared/Endpoints/CronEndpoints.cs:20` |
| `POST` | `/api/security-master` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:338` |
| `POST` | `/api/security-master/aliases/upsert` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:422` |
| `POST` | `/api/security-master/amend` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:366` |
| `GET` | `/api/security-master/asset-profiles` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:72` |
| `POST` | `/api/security-master/asset-profiles/approve` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:156` |
| `POST` | `/api/security-master/asset-profiles/drafts` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:115` |
| `GET` | `/api/security-master/asset-profiles/promotion-candidates` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:87` |
| `POST` | `/api/security-master/asset-profiles/rollback` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:197` |
| `GET` | `/api/security-master/asset-profiles/{profileId}/lineage` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:99` |
| `GET` | `/api/security-master/conflicts` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:640` |
| `POST` | `/api/security-master/conflicts/{conflictId:guid}/resolve` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:660` |
| `POST` | `/api/security-master/deactivate` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:394` |
| `POST` | `/api/security-master/import` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:707` |
| `POST` | `/api/security-master/ingest/edgar` | Documented | `src/Meridian.Ui.Shared/Endpoints/EdgarReferenceDataEndpoints.cs:24` |
| `GET` | `/api/security-master/ingest/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:738` |
| `POST` | `/api/security-master/resolve` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:243` |
| `POST` | `/api/security-master/search` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:280` |
| `GET` | `/api/security-master/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:40` |
| `GET` | `/api/security-master/{securityId:guid}/convertible-equity-terms` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:530` |
| `PATCH` | `/api/security-master/{securityId:guid}/convertible-equity-terms` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:551` |
| `GET` | `/api/security-master/{securityId:guid}/corporate-actions` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:588` |
| `POST` | `/api/security-master/{securityId:guid}/corporate-actions` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:609` |
| `GET` | `/api/security-master/{securityId:guid}/history` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:311` |
| `GET` | `/api/security-master/{securityId:guid}/operator-overrides` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:759` |
| `PATCH` | `/api/security-master/{securityId:guid}/operator-overrides` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:783` |
| `GET` | `/api/security-master/{securityId:guid}/preferred-equity-terms` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:473` |
| `PATCH` | `/api/security-master/{securityId:guid}/preferred-equity-terms` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:494` |
| `GET` | `/api/security-master/{securityId:guid}/trading-parameters` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:450` |
| `GET` | `/api/security-master/{securityId:guid}/validation` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:58` |
| `POST` | `/api/servicer-reports` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:689` |
| `GET` | `/api/servicer-reports/{batchId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:715` |
| `GET` | `/api/servicer-reports/{batchId:guid}/position-lines` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:723` |
| `GET` | `/api/servicer-reports/{batchId:guid}/transaction-lines` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:732` |
| `GET` | `/api/sla/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:555` |
| `GET` | `/api/sla/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:581` |
| `GET` | `/api/sla/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:527` |
| `GET` | `/api/sla/status/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:530` |
| `GET` | `/api/sla/violations` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:539` |
| `GET` | `/api/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:96` |
| `GET` | `/api/storage/archive/stats` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:319` |
| `GET` | `/api/storage/breakdown` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:79` |
| `GET` | `/api/storage/capacity-forecast` | Gap | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:586` |
| `GET` | `/api/storage/catalog` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:353` |
| `POST` | `/api/storage/cleanup` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:279` |
| `GET` | `/api/storage/cleanup/candidates` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:244` |
| `POST` | `/api/storage/convert-parquet` | Gap | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:557` |
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
| `POST` | `/api/strategies/covered-call/chain-preview` | Gap | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:158` |
| `GET` | `/api/strategies/covered-call/runs` | Gap | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:52` |
| `POST` | `/api/strategies/covered-call/runs` | Gap | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:24` |
| `POST` | `/api/strategies/covered-call/runs/{runId}/cancel` | Gap | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:135` |
| `GET` | `/api/strategies/covered-call/runs/{runId}/result` | Gap | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:93` |
| `GET` | `/api/strategies/covered-call/runs/{runId}/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:70` |
| `GET` | `/api/strategies/runs/compare` | Documented | `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs:2549` |
| `GET` | `/api/strategies/{strategyId}/runs` | Documented | `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs:2428` |
| `GET` | `/api/subscriptions/active` | Gap | `src/Meridian.Ui.Shared/Endpoints/SubscriptionEndpoints.cs:21` |
| `POST` | `/api/subscriptions/subscribe` | Gap | `src/Meridian.Ui.Shared/Endpoints/SubscriptionEndpoints.cs:43` |
| `POST` | `/api/subscriptions/unsubscribe/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/SubscriptionEndpoints.cs:72` |
| `GET` | `/api/symbols` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:31` |
| `POST` | `/api/symbols/add` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:143` |
| `GET` | `/api/symbols/archived` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:79` |
| `POST` | `/api/symbols/batch` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:392` |
| `POST` | `/api/symbols/bulk-add` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:307` |
| `POST` | `/api/symbols/bulk-remove` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:347` |
| `POST` | `/api/symbols/create` | Gap | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:440` |
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
| `POST` | `/api/symbols/{symbol}/update` | Gap | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:480` |
| `GET` | `/api/system/lifecycle` | Documented | `src/Meridian/UiServer.cs:351` |
| `POST` | `/api/system/shutdown` | Documented | `src/Meridian/UiServer.cs:373` |
| `GET` | `/health` | Documented | `src/Meridian.Application/Composition/HostAdapters.cs:60` |
| `GET` | `/live` | Documented | `src/Meridian.Application/Composition/HostAdapters.cs:73` |
| `GET` | `/ready` | Documented | `src/Meridian.Application/Composition/HostAdapters.cs:72` |

## Workstation Contract Coverage

| Contract | Status | Source |
|---|---|---|
| `AccrualCalculationResultDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:190` |
| `AccrualInputSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:172` |
| `AlpacaBrokerageConnectionRequestDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:102` |
| `ApprovalDecision` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:30` |
| `ApprovalPolicy` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:22` |
| `ApprovalStep` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:21` |
| `ApproveSecurityMasterOverrides` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:13` |
| `ApproveWorkflow` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:20` |
| `AuditTrailExplorerQueryDto` | Gap | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:8` |
| `AuditTrailExplorerResultDto` | Gap | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:54` |
| `AuditTrailObjectKindDto` | Gap | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:64` |
| `AuditTrailTimelineEntryDto` | Gap | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:27` |
| `BankAccountSnapshot` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:97` |
| `BankStatementImportResultDto` | Gap | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:62` |
| `BooksBeforeBrokerReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:62` |
| `BrokerageAccountKindDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:17` |
| `BrokerageAccountLinkRequestDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:60` |
| `BrokerageCashFlowEntryDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:129` |
| `BrokerageCashFlowSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:139` |
| `BrokerageConnectionStateDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:26` |
| `BrokerageConnectionStatusDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:85` |
| `BrokerageHouseholdAccountDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:154` |
| `BrokerageHouseholdPortfolioDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:187` |
| `BrokerageHouseholdPositionDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:170` |
| `BrokeragePortfolioPerformanceDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:113` |
| `BrokeragePortfolioPerformancePointDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:107` |
| `BuildLedgerDraft` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:14` |
| `BulkResolveSecurityMasterConflictsRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:394` |
| `BulkResolveSecurityMasterConflictsResult` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:400` |
| `CashFinancingSummary` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:135` |
| `CashFlowEntryDto` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:710` |
| `CashFlowProjectionPoint` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:34` |
| `CashForecastResult` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:36` |
| `CashLadderBucketDto` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:720` |
| `CashSyncSourceAvailability` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:2` |
| `CashSyncWindow` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:5` |
| `CloseWorkflow` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:22` |
| `ClosedLotSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:902` |
| `CollateralCallDto` | Gap | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:26` |
| `CounterpartyExposureDto` | Gap | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:10` |
| `CouponEvent` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:20` |
| `CrossFundReportingConsolidationDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:321` |
| `CrossFundReportingConsolidationScopeDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:311` |
| `DataUploadPreviewResultDto` | Gap | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:41` |
| `DataUploadTemplateCatalogDto` | Gap | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:6` |
| `DataUploadTemplateDto` | Gap | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:15` |
| `DataUploadTemplateFieldDto` | Gap | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:31` |
| `DataUploadValidationIssueDto` | Gap | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:82` |
| `DeltaOutlierResult` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:15` |
| `EquityCurvePoint` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:647` |
| `EquityCurveSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:656` |
| `EvidenceArtifactCaptureDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:39` |
| `EvidenceArtifactExtractionFieldDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:47` |
| `EvidenceArtifactRefDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:28` |
| `EvidenceArtifactRefDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:58` |
| `EvidenceAssuranceComponentDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:123` |
| `EvidenceCompletenessDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:330` |
| `EvidenceCompletenessSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:117` |
| `EvidenceEdgeDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:80` |
| `EvidenceEndpointErrorDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:322` |
| `EvidenceFreshnessDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:23` |
| `EvidenceGraphDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:364` |
| `EvidenceLifecycleMetadataDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:296` |
| `EvidenceNodeDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:64` |
| `EvidenceNodeDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:75` |
| `EvidencePacketDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:352` |
| `EvidencePacketExportRequest` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:386` |
| `EvidencePacketExportResponse` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:395` |
| `EvidenceProofChainDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:164` |
| `EvidenceProofChainLayerDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:151` |
| `EvidenceProofChainLayerKindDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:138` |
| `EvidenceRequestListDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:225` |
| `EvidenceSlaAssessmentDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:112` |
| `EvidenceSlaPolicyDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:103` |
| `EvidenceStatusDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:6` |
| `EvidenceSubjectDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:15` |
| `EvidenceSubjectLinkageDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:302` |
| `EvidenceSupportRequestDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:213` |
| `EvidenceTemplateDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:379` |
| `EvidenceTemplateExportSettingsDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:374` |
| `EvidenceValidationIssueDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:94` |
| `EvidenceValidationSeverityDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:88` |
| `EvidenceVaultArtifactDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:197` |
| `EvidenceVaultIdentityDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:181` |
| `EvidenceVaultIntakeRequestDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:267` |
| `EvidenceVaultIntakeResponseDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:282` |
| `EvidenceVaultLookupRequestDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:312` |
| `EvidenceVaultRequestListEntryDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:247` |
| `EvidenceVaultRequestListQueryDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:238` |
| `ExpectedAccountingEventDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:206` |
| `ExpectedAccountingEventKindDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:151` |
| `ExpectedJournalPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:234` |
| `ExpectedJournalPreviewLineDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:224` |
| `ExposureSnapshotDto` | Gap | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:2` |
| `ExposureTrendPointDto` | Gap | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:43` |
| `FeatureCapabilitySettingsResponse` | Gap | `src/Meridian.Contracts/Workstation/FeatureCapabilityDtos.cs:2` |
| `FeatureCapabilityToggleDto` | Gap | `src/Meridian.Contracts/Workstation/FeatureCapabilityDtos.cs:5` |
| `FeatureCapabilityToggleRequest` | Gap | `src/Meridian.Contracts/Workstation/FeatureCapabilityDtos.cs:16` |
| `FinancialRecordExplorerCellDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:65` |
| `FinancialRecordExplorerColumnDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:58` |
| `FinancialRecordExplorerDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:14` |
| `FinancialRecordExplorerFilterDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:51` |
| `FinancialRecordExplorerGraphEdgeDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:115` |
| `FinancialRecordExplorerGraphNodeDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:108` |
| `FinancialRecordExplorerProofActionDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:95` |
| `FinancialRecordExplorerRecordGraphDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:104` |
| `FinancialRecordExplorerRelationshipDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:121` |
| `FinancialRecordExplorerRowDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:72` |
| `FinancialRecordExplorerSavedViewDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:36` |
| `FinancialRecordExplorerSavedViewSaveRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:128` |
| `FinancialRecordExplorerScopeItemDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:31` |
| `FinancialRecordExplorerSelectedRecordDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:82` |
| `FinancialRecordExplorerSummaryItemDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:45` |
| `FinancialRecordExplorerTone` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:6` |
| `FundAccountBrokerageBalanceSnapshotDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:198` |
| `FundAccountBrokerageCashTransactionDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:244` |
| `FundAccountBrokerageCorporateActionDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:253` |
| `FundAccountBrokerageFillDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:232` |
| `FundAccountBrokerageOrderDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:218` |
| `FundAccountBrokeragePositionDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:205` |
| `FundAccountBrokerageSyncActivityDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:265` |
| `FundAccountCloseReadinessActionDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:560` |
| `FundAccountCloseReadinessBlockerDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:552` |
| `FundAccountCloseReadinessComponentDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:538` |
| `FundAccountCloseReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:566` |
| `FundAccountCloseReadinessStatusDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:532` |
| `FundAccountSummary` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:68` |
| `FundAuditEntry` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:296` |
| `FundAuditEvidenceCategoryKeyDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:59` |
| `FundAuditEvidenceCategorySummaryDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:490` |
| `FundAuditPackReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:499` |
| `FundJournalLine` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:47` |
| `FundLedgerDimensionSnapshot` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:121` |
| `FundLedgerQuery` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:25` |
| `FundLedgerReconciliationSnapshot` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:130` |
| `FundLedgerScope` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:6` |
| `FundLedgerSliceDto` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:71` |
| `FundLedgerSnapshotBalanceLine` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:110` |
| `FundLedgerSummary` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:86` |
| `FundLedgerTotalsDto` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:59` |
| `FundNavAssetClassExposureDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:105` |
| `FundNavAttributionSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:112` |
| `FundOperationsNavigationContext` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:26` |
| `FundOperationsTab` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:8` |
| `FundOperationsWorkspaceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:455` |
| `FundOperationsWorkspaceQuery` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:94` |
| `FundPortfolioPosition` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:113` |
| `FundReconciliationItem` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:160` |
| `FundReportAssetClassSectionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:487` |
| `FundReportPackArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:545` |
| `FundReportPackEvidenceBundleApprovalDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:788` |
| `FundReportPackEvidenceBundleDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:796` |
| `FundReportPackEvidenceBundleSourceLinkDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:779` |
| `FundReportPackGenerateRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:529` |
| `FundReportPackHistoryItemDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:756` |
| `FundReportPackLifecycleEventDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:714` |
| `FundReportPackLineagePointerDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:568` |
| `FundReportPackPreviewDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:512` |
| `FundReportPackPreviewRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:476` |
| `FundReportPackProvenanceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:556` |
| `FundReportPackSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:725` |
| `FundReportPackValidationIssueDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:697` |
| `FundReportingProfileDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:124` |
| `FundReportingSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:429` |
| `FundTrialBalanceLine` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:35` |
| `FundWorkflowCommandMetadata` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:7` |
| `FundWorkflowOverallStatus` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:2` |
| `FundWorkflowRejectionReasonCode` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:6` |
| `FundWorkflowStage` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:4` |
| `FundWorkflowState` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:24` |
| `FundWorkflowSubStatus` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:5` |
| `FundWorkspaceSummary` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:37` |
| `FxConversionReference` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:32` |
| `GovernanceLifecycleProjectionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:196` |
| `GovernanceReportArtifactFormatDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:23` |
| `GovernanceReportKindDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:6` |
| `GovernanceReportPackStatusDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:33` |
| `GovernanceReportValidationSeverityDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:51` |
| `HaircutRuleDto` | Gap | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:24` |
| `ImportBrokerData` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:10` |
| `InsightFeed` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:19` |
| `InsightWidget` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:6` |
| `InstrumentPassportDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:368` |
| `InstrumentPassportPricingDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:384` |
| `InstrumentPassportProviderConfidenceDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:353` |
| `InvestmentAccountingPreviewModeDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:24` |
| `InvestmentAccountingReconciliationExpectationDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:54` |
| `InvestmentAccountingTradeSideDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:17` |
| `InvestmentAccountingTransactionKindDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:6` |
| `InvestmentAccountingTransactionLabPreviewDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:71` |
| `InvestmentAccountingTransactionLabRequestDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:29` |
| `InvestmentAccountingTrialBalanceImpactDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:47` |
| `LedgerAmountApprovalStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:654` |
| `LedgerAmountProvenanceDetailDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:678` |
| `LedgerAmountProvenanceEvidenceDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:581` |
| `LedgerAmountReconciliationCaseDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:616` |
| `LedgerAmountReconciliationStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:647` |
| `LedgerAmountReportUsageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:660` |
| `LedgerAmountSecurityMasterLinkDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:605` |
| `LedgerAmountStrategyRunLinkDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:670` |
| `LedgerImpactPreviewDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:285` |
| `LedgerJournalLine` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:411` |
| `LedgerSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:364` |
| `LedgerTrialBalanceLine` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:391` |
| `MarginRequirementDto` | Gap | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:22` |
| `MeridianAssuranceScoreDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:130` |
| `MetricsDiff` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:624` |
| `MultiAssetClassCoverageDto` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:575` |
| `MultiAssetCoverageSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:591` |
| `MultiAssetDrillThroughTargetDto` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:562` |
| `MultiAssetEvidenceRequirementDto` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:541` |
| `MultiAssetReadinessBlockerDto` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:552` |
| `NormalizeBrokerTransactions` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:11` |
| `OpenLotSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:889` |
| `OperationsAccountingRecordEvidenceCategoryDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1003` |
| `OperationsAccountingRecordSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:993` |
| `OperationsActionOriginDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:460` |
| `OperationsApprovalDecisionRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:667` |
| `OperationsApprovalDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1134` |
| `OperationsApprovalPolicyMatrixDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:720` |
| `OperationsApprovalPolicyMatrixRowDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:726` |
| `OperationsApprovalPolicyRuleAuditEventDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:770` |
| `OperationsApprovalPolicyRuleUpsertRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:744` |
| `OperationsApprovalPolicyRuleUpsertResultDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:765` |
| `OperationsApprovalStateDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:101` |
| `OperationsAssignBreakCaseRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:644` |
| `OperationsBreakCaseDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1098` |
| `OperationsBrokerIntakeStateDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:51` |
| `OperationsChecklistAcknowledgeRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1041` |
| `OperationsChecklistControlApprovalDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:715` |
| `OperationsCloseCalendarDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:785` |
| `OperationsCloseCalendarItemAuditEventDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:825` |
| `OperationsCloseCalendarItemDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:789` |
| `OperationsCloseCalendarItemUpsertRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:811` |
| `OperationsCloseCalendarItemUpsertResultDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:820` |
| `OperationsCloseChecklistTaskDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1012` |
| `OperationsClosePackagePublicationDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1029` |
| `OperationsCloseReadinessBlockerDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1177` |
| `OperationsCloseReadinessComponentDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1166` |
| `OperationsCloseReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1157` |
| `OperationsCloseWorkflowRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:688` |
| `OperationsContinuityCorrelationKeysDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1126` |
| `OperationsContinuityWorkflowDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:898` |
| `OperationsContinuityWorkflowSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:849` |
| `OperationsDashboardMetricDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:951` |
| `OperationsDashboardSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:935` |
| `OperationsEvidenceLinkDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1202` |
| `OperationsEvidencePackageSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:965` |
| `OperationsGateDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1047` |
| `OperationsGateKeyDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:41` |
| `OperationsGatePostureRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:496` |
| `OperationsGateStatusDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:28` |
| `OperationsIssueCodeDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:438` |
| `OperationsJournalEntryMetadataDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:602` |
| `OperationsLedgerDraftRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:527` |
| `OperationsLedgerJournalCandidateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:565` |
| `OperationsLedgerJournalLineDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:587` |
| `OperationsLedgerPostRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:551` |
| `OperationsLedgerPostingStateDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:71` |
| `OperationsLedgerPreviewDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1144` |
| `OperationsLedgerValidationRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:541` |
| `OperationsNextActionDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1193` |
| `OperationsReconciliationLaneStatusDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:92` |
| `OperationsReconciliationLaneSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:982` |
| `OperationsReconciliationRunRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:617` |
| `OperationsReconciliationStateDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:81` |
| `OperationsRejectWorkflowRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:678` |
| `OperationsReopenWorkflowRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:702` |
| `OperationsReportPackReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1151` |
| `OperationsResolveBreakCaseRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:635` |
| `OperationsReviewedAutomationArtifactDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:881` |
| `OperationsReviewedAutomationSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:862` |
| `OperationsSecurityMasterOverrideApprovalRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:485` |
| `OperationsSecurityMasterResolveRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:516` |
| `OperationsSecurityMasterStateDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:61` |
| `OperationsStartWorkflowRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:467` |
| `OperationsSubmitApprovalRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:656` |
| `OperationsTimelineEntryDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1058` |
| `OperationsTransitionRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:477` |
| `OperationsTransitionResultDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:839` |
| `OperationsWorkflowAuditDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1078` |
| `OperationsWorkflowBlockerDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1185` |
| `OperationsWorkflowStatusDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:11` |
| `OperatorInboxDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:93` |
| `OperatorWorkItemDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:58` |
| `OperatorWorkItemKindDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:6` |
| `OperatorWorkItemToneDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:20` |
| `OperatorWorkflowHomeSummary` | Gap | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:6` |
| `ParameterDiff` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:618` |
| `PaymentInstruction` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:17` |
| `PilotAcceptanceEvidenceCategoryDto` | Gap | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:51` |
| `PilotAcceptanceEvidenceDto` | Gap | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:67` |
| `PilotAcceptanceEvidenceRoleDto` | Gap | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:62` |
| `PilotEvidenceGraphEdgeDto` | Gap | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:85` |
| `PilotReadinessArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:90` |
| `PilotReadinessStageDto` | Gap | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:6` |
| `PilotReadinessStageGateDto` | Gap | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:25` |
| `PilotReadinessStageStatusDto` | Gap | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:19` |
| `PilotW4AcceptanceEvaluationDto` | Gap | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:74` |
| `PortfolioLedgerDriftDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:47` |
| `PortfolioLedgerWorkflowStatusDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:39` |
| `PortfolioLedgerWorkflowStatusSnapshotDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:52` |
| `PortfolioPositionSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:344` |
| `PortfolioReportingAnalyticsKindDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:271` |
| `PortfolioReportingAnalyticsRowDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:289` |
| `PortfolioReportingAnalyticsScopeDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:279` |
| `PortfolioReportingCutDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:169` |
| `PortfolioReportingCutKindDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:150` |
| `PortfolioReportingLiveViewDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:208` |
| `PortfolioReportingLiveViewFreshnessPolicyDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:195` |
| `PortfolioReportingLiveViewStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:158` |
| `PortfolioReportingPnlSliceDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:251` |
| `PortfolioReportingPnlSlicePeriodDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:240` |
| `PortfolioSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:308` |
| `PositionDiffEntry` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:609` |
| `PostLedgerEntries` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:16` |
| `PrivateCapitalCloseCockpitApprovalDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1238` |
| `PrivateCapitalCloseCockpitDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1300` |
| `PrivateCapitalCloseCockpitLaneDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1223` |
| `PrivateCapitalCloseCockpitWorkflowDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1209` |
| `PrivateCapitalNavSupportComponentDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1253` |
| `PrivateCapitalNavSupportPackageDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1281` |
| `PrivateCapitalShadowNavTieOutDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1262` |
| `ProductExposureDto` | Gap | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:20` |
| `ProviderCorporateActionEvidenceCandidateDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:429` |
| `ProviderCorporateActionLedgerEffectDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:447` |
| `ProviderCorporateActionReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:488` |
| `ProviderCorporateActionReadinessLineDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:420` |
| `ProviderLedgerReconciliationBreakDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:333` |
| `ProviderLedgerReconciliationBreakSignOffStateDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:297` |
| `ProviderLedgerReconciliationCheckDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:320` |
| `ProviderLedgerReconciliationCheckStatusDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:289` |
| `ProviderLedgerReconciliationDetailDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:520` |
| `ProviderLedgerReconciliationRequestDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:312` |
| `ProviderLedgerReconciliationStatusDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:281` |
| `ProviderLedgerReconciliationSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:357` |
| `ProviderPromotionChecklistDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:300` |
| `ProviderSecurityMasterPassportDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:377` |
| `ProviderSecurityMasterPassportStatusDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:305` |
| `ProviderSecurityMasterScheduleFeedDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:464` |
| `ProviderShadowBookComparisonDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:410` |
| `ProviderShadowBookComparisonLineDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:399` |
| `ReconciliationBreakCategory` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:50` |
| `ReconciliationBreakDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:120` |
| `ReconciliationBreakExplanationDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:434` |
| `ReconciliationBreakQueueItem` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:364` |
| `ReconciliationBreakQueueProjectionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:242` |
| `ReconciliationBreakQueueProjectionItemDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:257` |
| `ReconciliationBreakQueueStatus` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:280` |
| `ReconciliationBreakScore` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:575` |
| `ReconciliationBreakSeverity` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:37` |
| `ReconciliationBreakStatus` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:24` |
| `ReconciliationBulkCaseworkCaseResult` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:550` |
| `ReconciliationBulkCaseworkRequest` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:530` |
| `ReconciliationBulkCaseworkResult` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:557` |
| `ReconciliationCalibrationProfileSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:622` |
| `ReconciliationCalibrationStatusDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:354` |
| `ReconciliationCalibrationSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:639` |
| `ReconciliationCaseComment` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:442` |
| `ReconciliationCaseCommentVisibility` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:326` |
| `ReconciliationCaseLifecycleState` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:290` |
| `ReconciliationCasePriority` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:306` |
| `ReconciliationCaseSignoffRecord` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:566` |
| `ReconciliationCaseSlaState` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:315` |
| `ReconciliationCaseStateTransition` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:588` |
| `ReconciliationCaseTransitionAction` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:599` |
| `ReconciliationCaseTransitionCommand` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:609` |
| `ReconciliationCaseworkAction` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:334` |
| `ReconciliationCaseworkCommand` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:505` |
| `ReconciliationCorrelationContext` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:700` |
| `ReconciliationJobControl` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:723` |
| `ReconciliationMatchDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:106` |
| `ReconciliationPayloadEnvelope` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:710` |
| `ReconciliationProcessingTelemetry` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:738` |
| `ReconciliationRolloutFlags` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:749` |
| `ReconciliationRunDetail` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:261` |
| `ReconciliationRunRequest` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:68` |
| `ReconciliationRunSummary` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:82` |
| `ReconciliationSchemaVersion` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:690` |
| `ReconciliationSecurityCoverageIssueDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:138` |
| `ReconciliationSlaComputationResult` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:483` |
| `ReconciliationSlaPolicy` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:462` |
| `ReconciliationSourceKind` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:10` |
| `ReconciliationSummary` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:182` |
| `ReconciliationTaxonomySnapshot` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:500` |
| `ReconciliationTaxonomyValue` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:492` |
| `RejectWorkflow` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:21` |
| `RenderReportTemplateRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1320` |
| `RenderReportTemplateResponseDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1325` |
| `ReopenWorkflow` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:23` |
| `ReportAccessEvaluationDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1307` |
| `ReportAccessModeDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1281` |
| `ReportAccessPolicyDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1300` |
| `ReportAccessPrincipalDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1295` |
| `ReportAccessPrincipalKindDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1289` |
| `ReportBrandingThemeDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:136` |
| `ReportPackAuditEventDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1386` |
| `ReportPackChangedLineDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1389` |
| `ReportPackCreateRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1435` |
| `ReportPackDeliveryAccessLinkDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:850` |
| `ReportPackDeliveryApprovalStepDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:878` |
| `ReportPackDeliveryArtifactDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:839` |
| `ReportPackDeliveryAttemptDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:958` |
| `ReportPackDeliveryEvidencePacketDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:886` |
| `ReportPackDeliveryFailureRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:985` |
| `ReportPackDeliveryHistoryDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:994` |
| `ReportPackDeliveryModeDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:832` |
| `ReportPackDeliveryNotificationDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:858` |
| `ReportPackDeliveryPackageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:910` |
| `ReportPackDeliveryRecipientDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:872` |
| `ReportPackDeliveryRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:975` |
| `ReportPackDeliveryStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:822` |
| `ReportPackEvidenceLinkDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1388` |
| `ReportPackLineProvenanceDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1390` |
| `ReportPackPublicationManifestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1408` |
| `ReportPackPublishRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1417` |
| `ReportPackRejectRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1430` |
| `ReportPackRejectionMetadataDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1450` |
| `ReportPackRestateRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1456` |
| `ReportPackRestatementMetadataDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1443` |
| `ReportPackWorkflowActionRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1427` |
| `ReportPackWorkflowRecordDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1462` |
| `ReportPackWorkflowStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1139` |
| `ReportTemplateAuditEventDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1341` |
| `ReportTemplateDecisionRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1382` |
| `ReportTemplateDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1312` |
| `ReportTemplateDraftRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1371` |
| `ReportTemplateGovernanceRecordDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1349` |
| `ReportTemplateLifecycleStatusDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1333` |
| `ReportTemplateParameterDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1154` |
| `ReportWriterAggregateFunctionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1166` |
| `ReportWriterFilterDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1201` |
| `ReportWriterFilterLineageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1239` |
| `ReportWriterFilterOperatorDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1187` |
| `ReportWriterFormulaDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1180` |
| `ReportWriterFormulaLineageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1234` |
| `ReportWriterGridColumnDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1220` |
| `ReportWriterGridDataDictionaryFieldDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1245` |
| `ReportWriterGridDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1207` |
| `ReportWriterGridKindDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1157` |
| `ReportWriterGridLineageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1259` |
| `ReportWriterGridRenderDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1268` |
| `ReportWriterGridRowDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1225` |
| `ReportWriterGridValidationCheckDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1254` |
| `ReportWriterMetricDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1174` |
| `ReportWriterMetricLineageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1229` |
| `ReportingDueScheduleRunResultDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1096` |
| `ReportingRunAuditEntryDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1112` |
| `ReportingRunAuditTrailDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1119` |
| `ReportingRunRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1100` |
| `ReportingRunResultDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1109` |
| `ReportingScheduleDeliveryPlanDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1004` |
| `ReportingScheduleDeliveryTargetDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:998` |
| `ReportingScheduleRecordDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1053` |
| `ReportingScheduleRunResultDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1090` |
| `ReportingScheduleStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1047` |
| `ReportingScheduleUpsertRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1074` |
| `ResearchBriefingAlert` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:95` |
| `ResearchBriefingDto` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:132` |
| `ResearchBriefingRun` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:53` |
| `ResearchBriefingWorkspaceSummary` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:118` |
| `ResearchRunDrillInLinks` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:42` |
| `ResearchSavedComparison` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:84` |
| `ResearchSavedComparisonMode` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:73` |
| `ResearchWhatChangedItem` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:106` |
| `ResolveBreakCase` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:18` |
| `ResolveReconciliationBreakRequest` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:679` |
| `ResolveSecurityMasterMappings` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:12` |
| `ReviewReconciliationBreakRequest` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:669` |
| `RunAttributionSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:697` |
| `RunCashFlowSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:744` |
| `RunCashLadder` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:730` |
| `RunComparisonDto` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:532` |
| `RunComparisonRequest` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:576` |
| `RunDiffRequest` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:581` |
| `RunFillEntry` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:668` |
| `RunFillSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:679` |
| `RunLotSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:917` |
| `RunPortfolioDrillInSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:759` |
| `RunReconciliation` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:17` |
| `SecurityClassificationSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:8` |
| `SecurityEconomicDefinitionSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:27` |
| `SecurityIdentityDrillInDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:56` |
| `SecurityMasterAccountingIssueDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:247` |
| `SecurityMasterChangeHistoryItemDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:165` |
| `SecurityMasterConflictAssessmentDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:116` |
| `SecurityMasterConflictRecommendationKind` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:41` |
| `SecurityMasterDownstreamImpactDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:316` |
| `SecurityMasterEconomicDefinitionDrillInDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:73` |
| `SecurityMasterFactorPointDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:226` |
| `SecurityMasterIdentifierSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:131` |
| `SecurityMasterImpactLinkDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:332` |
| `SecurityMasterImpactSeverity` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:16` |
| `SecurityMasterLotModelDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:251` |
| `SecurityMasterOpenLotDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:274` |
| `SecurityMasterOpenLotProvenanceDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:305` |
| `SecurityMasterOpenLotReadModelDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:260` |
| `SecurityMasterProviderSymbolMappingDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:143` |
| `SecurityMasterRecommendedActionDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:339` |
| `SecurityMasterRecommendedActionKind` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:26` |
| `SecurityMasterScheduleBookDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:190` |
| `SecurityMasterScheduleEventDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:204` |
| `SecurityMasterScheduleProvenanceDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:238` |
| `SecurityMasterScheduleSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:180` |
| `SecurityMasterSchemaCompatibilityDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:156` |
| `SecurityMasterSourceCandidateDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:104` |
| `SecurityMasterTrustPostureDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:90` |
| `SecurityMasterTrustSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:48` |
| `SecurityMasterTrustTone` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:7` |
| `SecurityMasterWorkstationDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:43` |
| `SettlementInstruction` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:19` |
| `StartWorkflow` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:9` |
| `StatementBreakDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:175` |
| `StatementBreakType` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:53` |
| `StatementMatchSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:157` |
| `StatementMatchTier` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:39` |
| `StatementNormalizedCashDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:125` |
| `StatementNormalizedPositionDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:106` |
| `StatementNormalizedTransactionDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:138` |
| `StatementReconciliationBreakExplanationDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:248` |
| `StatementReconciliationCaseAttachmentDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:239` |
| `StatementReconciliationCaseAuditEventDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:257` |
| `StatementReconciliationCaseCommentDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:232` |
| `StatementReconciliationCaseCommentThreadDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:227` |
| `StatementReconciliationCaseDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:204` |
| `StatementRunBreakDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:345` |
| `StatementRunCreateDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:297` |
| `StatementRunDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:268` |
| `StatementRunExceptionDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:360` |
| `StatementRunReconcileRequestDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:315` |
| `StatementRunStatus` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:9` |
| `StatementRunSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:323` |
| `StatementRunValidationDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:337` |
| `StatementSourceDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:76` |
| `StatementValidationIssueDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:92` |
| `StatementValidationSeverity` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:27` |
| `StpProcessingState` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:4` |
| `StpStateTransition` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:37` |
| `StrategyBriefingAlert` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:59` |
| `StrategyBriefingDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:96` |
| `StrategyBriefingRun` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:17` |
| `StrategyBriefingWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:82` |
| `StrategyDesignCell` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:22` |
| `StrategyDesignCompiledScript` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:102` |
| `StrategyDesignDocument` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:6` |
| `StrategyDesignDraftSaveRequest` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:144` |
| `StrategyDesignDraftSaveResponse` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:149` |
| `StrategyDesignDraftSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:131` |
| `StrategyDesignFieldCatalogItem` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:47` |
| `StrategyDesignPreviewResult` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:111` |
| `StrategyDesignPreviewRow` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:90` |
| `StrategyDesignRunBacktestRequest` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:158` |
| `StrategyDesignRunBacktestResponse` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:165` |
| `StrategyDesignRunTraceEntry` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:120` |
| `StrategyDesignTemplate` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:61` |
| `StrategyDesignTransition` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:35` |
| `StrategyDesignValidationMessage` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:73` |
| `StrategyDesignValidationResult` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:82` |
| `StrategyRunArtifactCompleteness` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:452` |
| `StrategyRunCashFlowDigest` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:796` |
| `StrategyRunComparison` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:430` |
| `StrategyRunContinuityDetail` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:874` |
| `StrategyRunContinuityDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:864` |
| `StrategyRunContinuityLineage` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:788` |
| `StrategyRunContinuityLink` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:773` |
| `StrategyRunContinuitySeamHealthStatus` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:832` |
| `StrategyRunContinuityStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:842` |
| `StrategyRunContinuityWarning` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:817` |
| `StrategyRunContinuityWarningSeverity` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:824` |
| `StrategyRunCrossModeTransitionMetadata` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:503` |
| `StrategyRunDetail` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:252` |
| `StrategyRunDiff` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:584` |
| `StrategyRunDrillInLinks` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:6` |
| `StrategyRunEngine` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:22` |
| `StrategyRunExecutionSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:135` |
| `StrategyRunGovernanceHook` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:116` |
| `StrategyRunGovernanceSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:169` |
| `StrategyRunHistoryQuery` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:481` |
| `StrategyRunIdentity` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:182` |
| `StrategyRunLineageEventType` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:471` |
| `StrategyRunLineageTimelineEntry` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:514` |
| `StrategyRunLiveStatus` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:66` |
| `StrategyRunMode` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:11` |
| `StrategyRunPaperStatus` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:93` |
| `StrategyRunPromotionState` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:50` |
| `StrategyRunPromotionSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:151` |
| `StrategyRunReviewPacketDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:343` |
| `StrategyRunStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:35` |
| `StrategyRunSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:198` |
| `StrategyRunTimelineEntry` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:488` |
| `StrategyRunTimelineProjection` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:463` |
| `StrategySavedComparison` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:48` |
| `StrategySavedComparisonMode` | Gap | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:37` |
| `StrategySweepObjectiveRanking` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:230` |
| `StrategySweepResultGroup` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:239` |
| `StrategyWhatChangedItem` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:70` |
| `StructuredReportingExportColumnDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:384` |
| `StructuredReportingExportDataDictionaryFieldDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:389` |
| `StructuredReportingExportDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:358` |
| `StructuredReportingExportPayloadDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:406` |
| `StructuredReportingExportPurposeDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:348` |
| `StructuredReportingExportRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:419` |
| `StructuredReportingExportRowLineageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:401` |
| `StructuredReportingExportValidationCheckDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:396` |
| `SubmitForApproval` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:19` |
| `SymbolAttributionEntry` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:687` |
| `SyncCompletenessResult` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:13` |
| `SyncValidationResult` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:16` |
| `ThresholdBreachDto` | Gap | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:35` |
| `TradingAcceptanceGateDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:105` |
| `TradingAcceptanceGateStatusDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:29` |
| `TradingControlEvidenceDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:171` |
| `TradingControlReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:187` |
| `TradingOperatorReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:312` |
| `TradingOperatorSignoffReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:219` |
| `TradingPaperSessionReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:134` |
| `TradingPromotionReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:205` |
| `TradingReplayReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:156` |
| `TradingReportPackReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:289` |
| `TradingTrustGateContractReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:247` |
| `TradingTrustGateEvidenceDocumentDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:239` |
| `TradingTrustGateReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:257` |
| `TradingTrustGateSampleReviewDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:228` |
| `ValidateLedgerDraft` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:15` |
| `VersionedReportTemplateIdDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1152` |
| `WorkflowActionDto` | Documented | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:29` |
| `WorkflowBlockerSummary` | Gap | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:42` |
| `WorkflowDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:14` |
| `WorkflowEvidenceBadge` | Gap | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:52` |
| `WorkflowLibraryDto` | Documented | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:6` |
| `WorkflowNextAction` | Gap | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:33` |
| `WorkflowPresetDto` | Gap | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:43` |
| `WorkflowPresetLibraryDto` | Gap | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:63` |
| `WorkflowPresetPinRequest` | Gap | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:82` |
| `WorkflowPresetSaveRequest` | Gap | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:70` |
| `WorkspaceWorkflowSummary` | Gap | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:20` |
| `WorkstationAccountingPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:473` |
| `WorkstationAccountingWorkspaceSummary` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:233` |
| `WorkstationBrokerageAccountDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:42` |
| `WorkstationBrokerageAccountLinkDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:51` |
| `WorkstationBrokerageSyncHealth` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:7` |
| `WorkstationBrokerageSyncRunRequestDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:35` |
| `WorkstationBrokerageSyncStatusDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:67` |
| `WorkstationDataBackfillRecord` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:650` |
| `WorkstationDataExportRecord` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:661` |
| `WorkstationDataPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:673` |
| `WorkstationDataProviderDiagnostic` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:606` |
| `WorkstationDataProviderRecord` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:628` |
| `WorkstationDataProviderRoutingSummary` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:616` |
| `WorkstationGeneratedReportWriterGridPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:371` |
| `WorkstationMetricCard` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:20` |
| `WorkstationModeComparisonGroup` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:44` |
| `WorkstationPlotToolPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:121` |
| `WorkstationPlotToolTabState` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:108` |
| `WorkstationPortfolioPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:511` |
| `WorkstationPortfolioRunRow` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:492` |
| `WorkstationPortfolioSummaryPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:527` |
| `WorkstationPortfolioSummaryTelemetry` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:518` |
| `WorkstationReportAccessAuditSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:424` |
| `WorkstationReportPackDistributionPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:408` |
| `WorkstationReportWriterDatasetSourcePayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:280` |
| `WorkstationReportWriterFieldPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:272` |
| `WorkstationReportWriterFilterPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:266` |
| `WorkstationReportWriterFormulaPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:261` |
| `WorkstationReportWriterGridPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:301` |
| `WorkstationReportWriterMetricPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:256` |
| `WorkstationReportingPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:443` |
| `WorkstationReportingProfilePayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:244` |
| `WorkstationReportingRunLinkPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:353` |
| `WorkstationReportingRunNextActionPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:361` |
| `WorkstationReportingRunPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:384` |
| `WorkstationReportingTemplatePayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:318` |
| `WorkstationRunDigest` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:31` |
| `WorkstationSecurityCoverageStatus` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:267` |
| `WorkstationSecurityReference` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:284` |
| `WorkstationSessionPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:81` |
| `WorkstationSessionWorkspaceSummary` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:69` |
| `WorkstationStrategyPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:132` |
| `WorkstationStrategyWorkspaceSummary` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:97` |
| `WorkstationTimelineCard` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:52` |
| `WorkstationTradingBrokerageState` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:200` |
| `WorkstationTradingFillRow` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:174` |
| `WorkstationTradingOrderRow` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:161` |
| `WorkstationTradingPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:215` |
| `WorkstationTradingPositionRow` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:147` |
| `WorkstationTradingRiskState` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:187` |
| `WorkstationWatchlist` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:29` |
| `WorkstationWorkspaceDefinition` | Gap | `src/Meridian.Contracts/Workstation/WorkstationWorkspaceCatalog.cs:6` |

## Follow-up Queue

- Document or intentionally suppress 303 mapped endpoint gap(s).
- Document or intentionally suppress 605 workstation contract gap(s).

---

*This dashboard is auto-generated. Do not edit manually.*
