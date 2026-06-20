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
| Endpoints documented | 546 / 546 |
| Workstation contracts documented | 688 / 688 |

## Endpoint Coverage

| Method | Route | Status | Source |
|---|---|---|---|
| `POST` | `/api/accounting-system/export-packages` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:122` |
| `POST` | `/api/accounting-system/export-packages/certification` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:172` |
| `GET` | `/api/accounting-system/export-packages/{exportPackageId}/manifest` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:139` |
| `GET` | `/api/accounting-system/import/latest` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:51` |
| `POST` | `/api/accounting-system/import/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:33` |
| `GET` | `/api/accounting-system/mapping-profiles` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:87` |
| `POST` | `/api/accounting-system/mapping-profiles` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:105` |
| `GET` | `/api/accounting-system/providers` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:18` |
| `GET` | `/api/accounting-system/reconciliation/latest` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:69` |
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
| `GET` | `/api/backfill/progress` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:111` |
| `GET` | `/api/backfill/providers` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:32` |
| `GET` | `/api/backfill/providers/audit` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:175` |
| `POST` | `/api/backfill/providers/dry-run-plan` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:156` |
| `GET` | `/api/backfill/providers/fallback-chain` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:144` |
| `GET` | `/api/backfill/providers/metadata` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:123` |
| `GET` | `/api/backfill/providers/statuses` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:133` |
| `GET` | `/api/backfill/resolve/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:46` |
| `POST` | `/api/backfill/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:83` |
| `POST` | `/api/backfill/run/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:55` |
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
| `GET` | `/api/backfill/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:42` |
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
| `GET` | `/api/data/bbo/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:329` |
| `GET` | `/api/data/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:410` |
| `GET` | `/api/data/l3-orderbook/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:259` |
| `GET` | `/api/data/orderbook/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:187` |
| `GET` | `/api/data/orderflow/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:369` |
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
| `GET` | `/api/diagnostics/coordination` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:458` |
| `POST` | `/api/diagnostics/dry-run` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:35` |
| `GET` | `/api/diagnostics/error-codes` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:356` |
| `GET` | `/api/diagnostics/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:169` |
| `GET` | `/api/diagnostics/providers` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:55` |
| `POST` | `/api/diagnostics/providers/{providerName}/test` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:293` |
| `GET` | `/api/diagnostics/quick-check` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:315` |
| `POST` | `/api/diagnostics/selftest` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:366` |
| `GET` | `/api/diagnostics/show-config` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:332` |
| `GET` | `/api/diagnostics/storage` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:72` |
| `POST` | `/api/diagnostics/test-connectivity` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:405` |
| `POST` | `/api/diagnostics/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:271` |
| `POST` | `/api/diagnostics/validate-config` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:429` |
| `POST` | `/api/diagnostics/validate-credentials` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:387` |
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
| `POST` | `/api/journals/{journalEntryId:guid}/post` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:600` |
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
| `GET` | `/api/ledger/accounting-configuration` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:393` |
| `POST` | `/api/ledger/accounting-configuration/activate` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:682` |
| `GET` | `/api/ledger/accounting-configuration/audit` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:716` |
| `POST` | `/api/ledger/accounting-configuration/chart` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:417` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:477` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/candidates` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:622` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/dry-run` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:593` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/promotion-approvals` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:507` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/test-cases` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:542` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/tests` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:653` |
| `POST` | `/api/ledger/accounting-configuration/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:572` |
| `POST` | `/api/ledger/accounting-configuration/templates` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:447` |
| `GET` | `/api/ledger/books` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:23` |
| `POST` | `/api/ledger/books` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:72` |
| `GET` | `/api/ledger/books/{ledgerBookId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:49` |
| `POST` | `/api/ledger/close-management/late-adjustments` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:766` |
| `POST` | `/api/ledger/close-management/late-adjustments/review` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:815` |
| `GET` | `/api/ledger/close-management/period-plan/{workflowId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:740` |
| `POST` | `/api/ledger/close-management/task-signoffs` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:864` |
| `GET` | `/api/ledger/journal-entry-workbench` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1060` |
| `POST` | `/api/ledger/journal-entry-workbench/drafts` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1369` |
| `POST` | `/api/ledger/journal-entry-workbench/evidence` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1468` |
| `POST` | `/api/ledger/journal-entry-workbench/lifecycle-action` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1503` |
| `POST` | `/api/ledger/journal-entry-workbench/submit-approval` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1433` |
| `POST` | `/api/ledger/journal-entry-workbench/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1404` |
| `GET` | `/api/ledger/periods` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:102` |
| `POST` | `/api/ledger/periods` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:139` |
| `POST` | `/api/ledger/periods/{periodId:guid}/close` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:170` |
| `GET` | `/api/ledger/periods/{periodId:guid}/pnl-summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:261` |
| `GET` | `/api/ledger/periods/{periodId:guid}/trial-balance` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:213` |
| `GET` | `/api/ledger/periods/{periodId:guid}/trial-balance-report` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:237` |
| `GET` | `/api/ledger/private-capital/activity` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1084` |
| `GET` | `/api/ledger/private-capital/capital-account-subledger` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1195` |
| `GET` | `/api/ledger/private-capital/capital-account-workbench` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1323` |
| `GET` | `/api/ledger/private-capital/fund-event-command-center` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1155` |
| `GET` | `/api/ledger/private-capital/fund-event-record` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1114` |
| `GET` | `/api/ledger/private-capital/report-output` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1254` |
| `POST` | `/api/ledger/reports/accounting-package` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:913` |
| `POST` | `/api/ledger/reports/accounting-package/certification` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:950` |
| `GET` | `/api/ledger/reports/accounting-packages` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:996` |
| `GET` | `/api/ledger/reports/accounting-packages/{packageId}/exports/{artifactId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1020` |
| `GET` | `/api/ledger/reports/pnl-summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:339` |
| `GET` | `/api/ledger/reports/trial-balance` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:285` |
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
| `GET` | `/api/projections/{projectionRunId:guid}/flows` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:583` |
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
| `GET` | `/api/providers/rate-limits` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:111` |
| `GET` | `/api/providers/readiness` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:347` |
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
| `GET` | `/api/reconciliation/exceptions` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:659` |
| `POST` | `/api/reconciliation/exceptions/{exceptionId:guid}/resolve` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:668` |
| `GET` | `/api/reconciliation/{runId:guid}/results` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:650` |
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
| `POST` | `/api/security-master` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:338` |
| `POST` | `/api/security-master/aliases/upsert` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:422` |
| `POST` | `/api/security-master/amend` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:366` |
| `GET` | `/api/security-master/asset-profiles` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:72` |
| `POST` | `/api/security-master/asset-profiles/approve` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:156` |
| `POST` | `/api/security-master/asset-profiles/drafts` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:115` |
| `GET` | `/api/security-master/asset-profiles/promotion-candidates` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:87` |
| `POST` | `/api/security-master/asset-profiles/rollback` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:197` |
| `GET` | `/api/security-master/asset-profiles/{profileId}/lineage` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:99` |
| `GET` | `/api/security-master/conflicts` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:640` |
| `POST` | `/api/security-master/conflicts/{conflictId:guid}/resolve` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:660` |
| `POST` | `/api/security-master/deactivate` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:394` |
| `POST` | `/api/security-master/import` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:707` |
| `POST` | `/api/security-master/ingest/edgar` | Documented | `src/Meridian.Ui.Shared/Endpoints/EdgarReferenceDataEndpoints.cs:24` |
| `GET` | `/api/security-master/ingest/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:738` |
| `POST` | `/api/security-master/resolve` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:243` |
| `POST` | `/api/security-master/search` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:280` |
| `GET` | `/api/security-master/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:40` |
| `GET` | `/api/security-master/{securityId:guid}/convertible-equity-terms` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:530` |
| `PATCH` | `/api/security-master/{securityId:guid}/convertible-equity-terms` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:551` |
| `GET` | `/api/security-master/{securityId:guid}/corporate-actions` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:588` |
| `POST` | `/api/security-master/{securityId:guid}/corporate-actions` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:609` |
| `GET` | `/api/security-master/{securityId:guid}/history` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:311` |
| `GET` | `/api/security-master/{securityId:guid}/operator-overrides` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:759` |
| `PATCH` | `/api/security-master/{securityId:guid}/operator-overrides` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:783` |
| `GET` | `/api/security-master/{securityId:guid}/preferred-equity-terms` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:473` |
| `PATCH` | `/api/security-master/{securityId:guid}/preferred-equity-terms` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:494` |
| `GET` | `/api/security-master/{securityId:guid}/trading-parameters` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:450` |
| `GET` | `/api/security-master/{securityId:guid}/validation` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:58` |
| `POST` | `/api/servicer-reports` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:689` |
| `GET` | `/api/servicer-reports/{batchId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:715` |
| `GET` | `/api/servicer-reports/{batchId:guid}/position-lines` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:723` |
| `GET` | `/api/servicer-reports/{batchId:guid}/transaction-lines` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:732` |
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
| `GET` | `/api/strategies/runs/compare` | Documented | `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs:2549` |
| `GET` | `/api/strategies/{strategyId}/runs` | Documented | `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs:2428` |
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
| `GET` | `/api/system/lifecycle` | Documented | `src/Meridian/UiServer.cs:351` |
| `POST` | `/api/system/shutdown` | Documented | `src/Meridian/UiServer.cs:373` |
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
| `ApproveWorkflow` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:20` |
| `AuditTrailExplorerQueryDto` | Documented | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:8` |
| `AuditTrailExplorerResultDto` | Documented | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:54` |
| `AuditTrailObjectKindDto` | Documented | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:64` |
| `AuditTrailTimelineEntryDto` | Documented | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:27` |
| `BankAccountSnapshot` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:97` |
| `BankStatementImportResultDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:62` |
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
| `BulkResolveSecurityMasterConflictsRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:394` |
| `BulkResolveSecurityMasterConflictsResult` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:400` |
| `CashFinancingSummary` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:135` |
| `CashFlowEntryDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:710` |
| `CashFlowProjectionPoint` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:34` |
| `CashForecastResult` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:36` |
| `CashLadderBucketDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:720` |
| `CashSyncSourceAvailability` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:2` |
| `CashSyncWindow` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:5` |
| `CloseWorkflow` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:22` |
| `ClosedLotSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:902` |
| `CollateralCallDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:26` |
| `CounterpartyExposureDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:10` |
| `CouponEvent` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:20` |
| `CrossFundReportingConsolidationDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:321` |
| `CrossFundReportingConsolidationScopeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:311` |
| `DataUploadPreviewResultDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:41` |
| `DataUploadTemplateCatalogDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:6` |
| `DataUploadTemplateDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:15` |
| `DataUploadTemplateFieldDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:31` |
| `DataUploadValidationIssueDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:82` |
| `DeltaOutlierResult` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:15` |
| `EquityCurvePoint` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:647` |
| `EquityCurveSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:656` |
| `EvidenceArtifactCaptureDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:39` |
| `EvidenceArtifactExtractionFieldDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:47` |
| `EvidenceArtifactRefDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:28` |
| `EvidenceArtifactRefDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:58` |
| `EvidenceAssuranceComponentDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:123` |
| `EvidenceCompletenessDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:330` |
| `EvidenceCompletenessSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:117` |
| `EvidenceEdgeDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:80` |
| `EvidenceEndpointErrorDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:322` |
| `EvidenceFreshnessDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:23` |
| `EvidenceGraphDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:364` |
| `EvidenceLifecycleMetadataDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:296` |
| `EvidenceNodeDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:64` |
| `EvidenceNodeDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:75` |
| `EvidencePacketDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:352` |
| `EvidencePacketExportRequest` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:386` |
| `EvidencePacketExportResponse` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:395` |
| `EvidenceProofChainDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:164` |
| `EvidenceProofChainLayerDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:151` |
| `EvidenceProofChainLayerKindDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:138` |
| `EvidenceRequestListDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:225` |
| `EvidenceSlaAssessmentDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:112` |
| `EvidenceSlaPolicyDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:103` |
| `EvidenceStatusDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:6` |
| `EvidenceSubjectDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:15` |
| `EvidenceSubjectLinkageDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:302` |
| `EvidenceSupportRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:213` |
| `EvidenceTemplateDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:379` |
| `EvidenceTemplateExportSettingsDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:374` |
| `EvidenceValidationIssueDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:94` |
| `EvidenceValidationSeverityDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:88` |
| `EvidenceVaultArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:197` |
| `EvidenceVaultIdentityDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:181` |
| `EvidenceVaultIntakeRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:267` |
| `EvidenceVaultIntakeResponseDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:282` |
| `EvidenceVaultLookupRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:312` |
| `EvidenceVaultRequestListEntryDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:247` |
| `EvidenceVaultRequestListQueryDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:238` |
| `ExpectedAccountingEventDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:206` |
| `ExpectedAccountingEventKindDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:151` |
| `ExpectedJournalPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:234` |
| `ExpectedJournalPreviewLineDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:224` |
| `ExposureSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:2` |
| `ExposureTrendPointDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:43` |
| `FeatureCapabilitySettingsResponse` | Documented | `src/Meridian.Contracts/Workstation/FeatureCapabilityDtos.cs:2` |
| `FeatureCapabilityToggleDto` | Documented | `src/Meridian.Contracts/Workstation/FeatureCapabilityDtos.cs:5` |
| `FeatureCapabilityToggleRequest` | Documented | `src/Meridian.Contracts/Workstation/FeatureCapabilityDtos.cs:16` |
| `FinancialRecordExplorerCellDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:66` |
| `FinancialRecordExplorerColumnDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:59` |
| `FinancialRecordExplorerDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:14` |
| `FinancialRecordExplorerFilterDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:52` |
| `FinancialRecordExplorerGraphEdgeDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:116` |
| `FinancialRecordExplorerGraphNodeDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:109` |
| `FinancialRecordExplorerProofActionDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:96` |
| `FinancialRecordExplorerRecordGraphDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:105` |
| `FinancialRecordExplorerRelationshipDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:122` |
| `FinancialRecordExplorerRowDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:73` |
| `FinancialRecordExplorerSavedViewDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:36` |
| `FinancialRecordExplorerSavedViewSaveRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:129` |
| `FinancialRecordExplorerScopeItemDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:31` |
| `FinancialRecordExplorerSelectedRecordDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:83` |
| `FinancialRecordExplorerSummaryItemDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:46` |
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
| `FundAuditEvidenceCategoryKeyDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:59` |
| `FundAuditEvidenceCategorySummaryDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:490` |
| `FundAuditPackReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:499` |
| `FundJournalLine` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:47` |
| `FundLedgerDimensionSnapshot` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:121` |
| `FundLedgerQuery` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:25` |
| `FundLedgerReconciliationSnapshot` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:130` |
| `FundLedgerScope` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:6` |
| `FundLedgerSliceDto` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:71` |
| `FundLedgerSnapshotBalanceLine` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:110` |
| `FundLedgerSummary` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:86` |
| `FundLedgerTotalsDto` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:59` |
| `FundNavAssetClassExposureDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:105` |
| `FundNavAttributionSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:112` |
| `FundOperationsNavigationContext` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:26` |
| `FundOperationsTab` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:8` |
| `FundOperationsWorkspaceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:455` |
| `FundOperationsWorkspaceQuery` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:94` |
| `FundPortfolioPosition` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:113` |
| `FundReconciliationItem` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:160` |
| `FundReportAssetClassSectionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:487` |
| `FundReportPackArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:545` |
| `FundReportPackEvidenceBundleApprovalDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:788` |
| `FundReportPackEvidenceBundleDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:796` |
| `FundReportPackEvidenceBundleSourceLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:779` |
| `FundReportPackGenerateRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:529` |
| `FundReportPackHistoryItemDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:756` |
| `FundReportPackLifecycleEventDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:714` |
| `FundReportPackLineagePointerDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:568` |
| `FundReportPackPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:512` |
| `FundReportPackPreviewRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:476` |
| `FundReportPackProvenanceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:556` |
| `FundReportPackSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:725` |
| `FundReportPackValidationIssueDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:697` |
| `FundReportingProfileDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:124` |
| `FundReportingSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:429` |
| `FundTrialBalanceLine` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:35` |
| `FundWorkflowCommandMetadata` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:7` |
| `FundWorkflowOverallStatus` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:2` |
| `FundWorkflowRejectionReasonCode` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:6` |
| `FundWorkflowStage` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:4` |
| `FundWorkflowState` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:24` |
| `FundWorkflowSubStatus` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:5` |
| `FundWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:37` |
| `FxConversionReference` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:32` |
| `GovernanceLifecycleProjectionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:196` |
| `GovernanceReportArtifactFormatDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:23` |
| `GovernanceReportKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:6` |
| `GovernanceReportPackStatusDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:33` |
| `GovernanceReportValidationSeverityDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:51` |
| `HaircutRuleDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:24` |
| `ImportBrokerData` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:10` |
| `InsightFeed` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:19` |
| `InsightWidget` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:6` |
| `InstrumentPassportDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:368` |
| `InstrumentPassportPricingDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:384` |
| `InstrumentPassportProviderConfidenceDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:353` |
| `InvestmentAccountingPreviewModeDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:24` |
| `InvestmentAccountingReconciliationExpectationDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:54` |
| `InvestmentAccountingTradeSideDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:17` |
| `InvestmentAccountingTransactionKindDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:6` |
| `InvestmentAccountingTransactionLabPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:71` |
| `InvestmentAccountingTransactionLabRequestDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:29` |
| `InvestmentAccountingTrialBalanceImpactDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:47` |
| `LedgerAmountApprovalStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:654` |
| `LedgerAmountProvenanceDetailDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:678` |
| `LedgerAmountProvenanceEvidenceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:581` |
| `LedgerAmountReconciliationCaseDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:616` |
| `LedgerAmountReconciliationStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:647` |
| `LedgerAmountReportUsageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:660` |
| `LedgerAmountSecurityMasterLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:605` |
| `LedgerAmountStrategyRunLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:670` |
| `LedgerImpactPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:285` |
| `LedgerJournalLine` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:411` |
| `LedgerSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:364` |
| `LedgerTrialBalanceLine` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:391` |
| `MarginRequirementDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:22` |
| `MeridianAssuranceScoreDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:130` |
| `MetricsDiff` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:624` |
| `MultiAssetClassCoverageDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:575` |
| `MultiAssetCoverageSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:591` |
| `MultiAssetDrillThroughTargetDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:562` |
| `MultiAssetEvidenceRequirementDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:541` |
| `MultiAssetReadinessBlockerDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:552` |
| `NormalizeBrokerTransactions` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:11` |
| `OpenLotSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:889` |
| `OperationsAccountingRecordEvidenceCategoryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1003` |
| `OperationsAccountingRecordSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:993` |
| `OperationsActionOriginDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:460` |
| `OperationsApprovalDecisionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:667` |
| `OperationsApprovalDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1134` |
| `OperationsApprovalPolicyMatrixDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:720` |
| `OperationsApprovalPolicyMatrixRowDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:726` |
| `OperationsApprovalPolicyRuleAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:770` |
| `OperationsApprovalPolicyRuleUpsertRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:744` |
| `OperationsApprovalPolicyRuleUpsertResultDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:765` |
| `OperationsApprovalStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:101` |
| `OperationsAssignBreakCaseRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:644` |
| `OperationsBreakCaseDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1098` |
| `OperationsBrokerIntakeStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:51` |
| `OperationsChecklistAcknowledgeRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1041` |
| `OperationsChecklistControlApprovalDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:715` |
| `OperationsCloseCalendarDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:785` |
| `OperationsCloseCalendarItemAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:825` |
| `OperationsCloseCalendarItemDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:789` |
| `OperationsCloseCalendarItemUpsertRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:811` |
| `OperationsCloseCalendarItemUpsertResultDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:820` |
| `OperationsCloseChecklistTaskDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1012` |
| `OperationsClosePackagePublicationDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1029` |
| `OperationsCloseReadinessBlockerDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1177` |
| `OperationsCloseReadinessComponentDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1166` |
| `OperationsCloseReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1157` |
| `OperationsCloseWorkflowRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:688` |
| `OperationsContinuityCorrelationKeysDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1126` |
| `OperationsContinuityWorkflowDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:898` |
| `OperationsContinuityWorkflowSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:849` |
| `OperationsDashboardMetricDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:951` |
| `OperationsDashboardSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:935` |
| `OperationsEvidenceLinkDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1202` |
| `OperationsEvidencePackageSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:965` |
| `OperationsGateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1047` |
| `OperationsGateKeyDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:41` |
| `OperationsGatePostureRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:496` |
| `OperationsGateStatusDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:28` |
| `OperationsIssueCodeDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:438` |
| `OperationsJournalEntryMetadataDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:602` |
| `OperationsLedgerDraftRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:527` |
| `OperationsLedgerJournalCandidateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:565` |
| `OperationsLedgerJournalLineDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:587` |
| `OperationsLedgerPostRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:551` |
| `OperationsLedgerPostingStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:71` |
| `OperationsLedgerPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1144` |
| `OperationsLedgerValidationRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:541` |
| `OperationsNextActionDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1193` |
| `OperationsReconciliationLaneStatusDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:92` |
| `OperationsReconciliationLaneSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:982` |
| `OperationsReconciliationRunRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:617` |
| `OperationsReconciliationStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:81` |
| `OperationsRejectWorkflowRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:678` |
| `OperationsReopenWorkflowRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:702` |
| `OperationsReportPackReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1151` |
| `OperationsResolveBreakCaseRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:635` |
| `OperationsReviewedAutomationArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:881` |
| `OperationsReviewedAutomationSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:862` |
| `OperationsSecurityMasterOverrideApprovalRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:485` |
| `OperationsSecurityMasterResolveRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:516` |
| `OperationsSecurityMasterStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:61` |
| `OperationsStartWorkflowRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:467` |
| `OperationsSubmitApprovalRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:656` |
| `OperationsTimelineEntryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1058` |
| `OperationsTransitionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:477` |
| `OperationsTransitionResultDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:839` |
| `OperationsWorkflowAuditDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1078` |
| `OperationsWorkflowBlockerDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1185` |
| `OperationsWorkflowStatusDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:11` |
| `OperatorInboxDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:93` |
| `OperatorWorkItemDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:58` |
| `OperatorWorkItemKindDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:6` |
| `OperatorWorkItemToneDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:20` |
| `OperatorWorkflowHomeSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:6` |
| `ParameterDiff` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:618` |
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
| `PortfolioReportingAnalyticsKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:271` |
| `PortfolioReportingAnalyticsRowDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:289` |
| `PortfolioReportingAnalyticsScopeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:279` |
| `PortfolioReportingCutDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:169` |
| `PortfolioReportingCutKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:150` |
| `PortfolioReportingLiveViewDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:208` |
| `PortfolioReportingLiveViewFreshnessPolicyDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:195` |
| `PortfolioReportingLiveViewStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:158` |
| `PortfolioReportingPnlSliceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:251` |
| `PortfolioReportingPnlSlicePeriodDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:240` |
| `PortfolioSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:308` |
| `PositionDiffEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:609` |
| `PostLedgerEntries` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:16` |
| `PrivateCapitalCloseCockpitApprovalDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1238` |
| `PrivateCapitalCloseCockpitDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1300` |
| `PrivateCapitalCloseCockpitLaneDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1223` |
| `PrivateCapitalCloseCockpitWorkflowDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1209` |
| `PrivateCapitalNavSupportComponentDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1253` |
| `PrivateCapitalNavSupportPackageDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1281` |
| `PrivateCapitalShadowNavTieOutDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1262` |
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
| `ReconciliationBreakCategory` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:50` |
| `ReconciliationBreakDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:120` |
| `ReconciliationBreakExplanationDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:434` |
| `ReconciliationBreakQueueItem` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:364` |
| `ReconciliationBreakQueueProjectionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:242` |
| `ReconciliationBreakQueueProjectionItemDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:257` |
| `ReconciliationBreakQueueStatus` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:280` |
| `ReconciliationBreakScore` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:575` |
| `ReconciliationBreakSeverity` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:37` |
| `ReconciliationBreakStatus` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:24` |
| `ReconciliationBulkCaseworkCaseResult` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:550` |
| `ReconciliationBulkCaseworkRequest` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:530` |
| `ReconciliationBulkCaseworkResult` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:557` |
| `ReconciliationCalibrationProfileSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:622` |
| `ReconciliationCalibrationStatusDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:354` |
| `ReconciliationCalibrationSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:639` |
| `ReconciliationCaseComment` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:442` |
| `ReconciliationCaseCommentVisibility` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:326` |
| `ReconciliationCaseLifecycleState` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:290` |
| `ReconciliationCasePriority` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:306` |
| `ReconciliationCaseSignoffRecord` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:566` |
| `ReconciliationCaseSlaState` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:315` |
| `ReconciliationCaseStateTransition` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:588` |
| `ReconciliationCaseTransitionAction` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:599` |
| `ReconciliationCaseTransitionCommand` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:609` |
| `ReconciliationCaseworkAction` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:334` |
| `ReconciliationCaseworkCommand` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:505` |
| `ReconciliationCorrelationContext` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:700` |
| `ReconciliationJobControl` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:723` |
| `ReconciliationMatchDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:106` |
| `ReconciliationPayloadEnvelope` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:710` |
| `ReconciliationProcessingTelemetry` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:738` |
| `ReconciliationRolloutFlags` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:749` |
| `ReconciliationRunDetail` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:261` |
| `ReconciliationRunRequest` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:68` |
| `ReconciliationRunSummary` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:82` |
| `ReconciliationSchemaVersion` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:690` |
| `ReconciliationSecurityCoverageIssueDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:138` |
| `ReconciliationSlaComputationResult` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:483` |
| `ReconciliationSlaPolicy` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:462` |
| `ReconciliationSourceKind` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:10` |
| `ReconciliationSummary` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:182` |
| `ReconciliationTaxonomySnapshot` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:500` |
| `ReconciliationTaxonomyValue` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:492` |
| `RejectWorkflow` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:21` |
| `RenderReportTemplateRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1320` |
| `RenderReportTemplateResponseDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1325` |
| `ReopenWorkflow` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:23` |
| `ReportAccessEvaluationDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1307` |
| `ReportAccessModeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1281` |
| `ReportAccessPolicyDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1300` |
| `ReportAccessPrincipalDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1295` |
| `ReportAccessPrincipalKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1289` |
| `ReportBrandingThemeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:136` |
| `ReportPackAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1386` |
| `ReportPackChangedLineDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1389` |
| `ReportPackCreateRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1435` |
| `ReportPackDeliveryAccessLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:850` |
| `ReportPackDeliveryApprovalStepDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:878` |
| `ReportPackDeliveryArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:839` |
| `ReportPackDeliveryAttemptDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:958` |
| `ReportPackDeliveryEvidencePacketDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:886` |
| `ReportPackDeliveryFailureRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:985` |
| `ReportPackDeliveryHistoryDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:994` |
| `ReportPackDeliveryModeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:832` |
| `ReportPackDeliveryNotificationDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:858` |
| `ReportPackDeliveryPackageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:910` |
| `ReportPackDeliveryRecipientDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:872` |
| `ReportPackDeliveryRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:975` |
| `ReportPackDeliveryStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:822` |
| `ReportPackEvidenceLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1388` |
| `ReportPackLineProvenanceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1390` |
| `ReportPackPublicationManifestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1408` |
| `ReportPackPublishRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1417` |
| `ReportPackRejectRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1430` |
| `ReportPackRejectionMetadataDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1450` |
| `ReportPackRestateRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1456` |
| `ReportPackRestatementMetadataDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1443` |
| `ReportPackWorkflowActionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1427` |
| `ReportPackWorkflowRecordDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1462` |
| `ReportPackWorkflowStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1139` |
| `ReportTemplateAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1341` |
| `ReportTemplateDecisionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1382` |
| `ReportTemplateDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1312` |
| `ReportTemplateDraftRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1371` |
| `ReportTemplateGovernanceRecordDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1349` |
| `ReportTemplateLifecycleStatusDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1333` |
| `ReportTemplateParameterDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1154` |
| `ReportWriterAggregateFunctionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1166` |
| `ReportWriterFilterDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1201` |
| `ReportWriterFilterLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1239` |
| `ReportWriterFilterOperatorDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1187` |
| `ReportWriterFormulaDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1180` |
| `ReportWriterFormulaLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1234` |
| `ReportWriterGridColumnDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1220` |
| `ReportWriterGridDataDictionaryFieldDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1245` |
| `ReportWriterGridDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1207` |
| `ReportWriterGridKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1157` |
| `ReportWriterGridLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1259` |
| `ReportWriterGridRenderDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1268` |
| `ReportWriterGridRowDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1225` |
| `ReportWriterGridValidationCheckDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1254` |
| `ReportWriterMetricDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1174` |
| `ReportWriterMetricLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1229` |
| `ReportingDueScheduleRunResultDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1096` |
| `ReportingRunAuditEntryDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1112` |
| `ReportingRunAuditTrailDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1119` |
| `ReportingRunRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1100` |
| `ReportingRunResultDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1109` |
| `ReportingScheduleDeliveryPlanDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1004` |
| `ReportingScheduleDeliveryTargetDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:998` |
| `ReportingScheduleRecordDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1053` |
| `ReportingScheduleRunResultDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1090` |
| `ReportingScheduleStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1047` |
| `ReportingScheduleUpsertRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1074` |
| `ResearchBriefingAlert` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:95` |
| `ResearchBriefingDto` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:132` |
| `ResearchBriefingRun` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:53` |
| `ResearchBriefingWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:118` |
| `ResearchRunDrillInLinks` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:42` |
| `ResearchSavedComparison` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:84` |
| `ResearchSavedComparisonMode` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:73` |
| `ResearchWhatChangedItem` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:106` |
| `ResolveBreakCase` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:18` |
| `ResolveReconciliationBreakRequest` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:679` |
| `ResolveSecurityMasterMappings` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:12` |
| `ReviewReconciliationBreakRequest` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:669` |
| `RunAttributionSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:697` |
| `RunCashFlowSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:744` |
| `RunCashLadder` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:730` |
| `RunComparisonDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:532` |
| `RunComparisonRequest` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:576` |
| `RunDiffRequest` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:581` |
| `RunFillEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:668` |
| `RunFillSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:679` |
| `RunLotSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:917` |
| `RunPortfolioDrillInSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:759` |
| `RunReconciliation` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:17` |
| `SecurityClassificationSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:8` |
| `SecurityEconomicDefinitionSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:27` |
| `SecurityIdentityDrillInDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:56` |
| `SecurityMasterAccountingIssueDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:247` |
| `SecurityMasterChangeHistoryItemDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:165` |
| `SecurityMasterConflictAssessmentDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:116` |
| `SecurityMasterConflictRecommendationKind` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:41` |
| `SecurityMasterDownstreamImpactDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:316` |
| `SecurityMasterEconomicDefinitionDrillInDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:73` |
| `SecurityMasterFactorPointDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:226` |
| `SecurityMasterIdentifierSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:131` |
| `SecurityMasterImpactLinkDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:332` |
| `SecurityMasterImpactSeverity` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:16` |
| `SecurityMasterLotModelDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:251` |
| `SecurityMasterOpenLotDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:274` |
| `SecurityMasterOpenLotProvenanceDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:305` |
| `SecurityMasterOpenLotReadModelDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:260` |
| `SecurityMasterProviderSymbolMappingDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:143` |
| `SecurityMasterRecommendedActionDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:339` |
| `SecurityMasterRecommendedActionKind` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:26` |
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
| `StatementMatchSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:157` |
| `StatementMatchTier` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:39` |
| `StatementNormalizedCashDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:125` |
| `StatementNormalizedPositionDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:106` |
| `StatementNormalizedTransactionDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:138` |
| `StatementReconciliationBreakExplanationDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:248` |
| `StatementReconciliationCaseAttachmentDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:239` |
| `StatementReconciliationCaseAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:257` |
| `StatementReconciliationCaseCommentDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:232` |
| `StatementReconciliationCaseCommentThreadDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:227` |
| `StatementReconciliationCaseDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:204` |
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
| `StrategyRunArtifactCompleteness` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:452` |
| `StrategyRunCashFlowDigest` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:796` |
| `StrategyRunComparison` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:430` |
| `StrategyRunContinuityDetail` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:874` |
| `StrategyRunContinuityDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:864` |
| `StrategyRunContinuityLineage` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:788` |
| `StrategyRunContinuityLink` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:773` |
| `StrategyRunContinuitySeamHealthStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:832` |
| `StrategyRunContinuityStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:842` |
| `StrategyRunContinuityWarning` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:817` |
| `StrategyRunContinuityWarningSeverity` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:824` |
| `StrategyRunCrossModeTransitionMetadata` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:503` |
| `StrategyRunDetail` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:252` |
| `StrategyRunDiff` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:584` |
| `StrategyRunDrillInLinks` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:6` |
| `StrategyRunEngine` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:22` |
| `StrategyRunExecutionSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:135` |
| `StrategyRunGovernanceHook` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:116` |
| `StrategyRunGovernanceSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:169` |
| `StrategyRunHistoryQuery` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:481` |
| `StrategyRunIdentity` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:182` |
| `StrategyRunLineageEventType` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:471` |
| `StrategyRunLineageTimelineEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:514` |
| `StrategyRunLiveStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:66` |
| `StrategyRunMode` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:11` |
| `StrategyRunPaperStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:93` |
| `StrategyRunPromotionState` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:50` |
| `StrategyRunPromotionSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:151` |
| `StrategyRunReviewPacketDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:343` |
| `StrategyRunStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:35` |
| `StrategyRunSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:198` |
| `StrategyRunTimelineEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:488` |
| `StrategyRunTimelineProjection` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:463` |
| `StrategySavedComparison` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:48` |
| `StrategySavedComparisonMode` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:37` |
| `StrategySweepObjectiveRanking` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:230` |
| `StrategySweepResultGroup` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:239` |
| `StrategyWhatChangedItem` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:70` |
| `StructuredReportingExportColumnDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:384` |
| `StructuredReportingExportDataDictionaryFieldDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:389` |
| `StructuredReportingExportDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:358` |
| `StructuredReportingExportPayloadDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:406` |
| `StructuredReportingExportPurposeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:348` |
| `StructuredReportingExportRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:419` |
| `StructuredReportingExportRowLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:401` |
| `StructuredReportingExportValidationCheckDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:396` |
| `SubmitForApproval` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:19` |
| `SymbolAttributionEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:687` |
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
| `ValidateLedgerDraft` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:15` |
| `VersionedReportTemplateIdDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1152` |
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
| `WorkstationAccountingPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:473` |
| `WorkstationAccountingWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:233` |
| `WorkstationBrokerageAccountDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:42` |
| `WorkstationBrokerageAccountLinkDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:51` |
| `WorkstationBrokerageSyncHealth` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:7` |
| `WorkstationBrokerageSyncRunRequestDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:35` |
| `WorkstationBrokerageSyncStatusDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:67` |
| `WorkstationDataBackfillRecord` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:650` |
| `WorkstationDataExportRecord` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:661` |
| `WorkstationDataPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:673` |
| `WorkstationDataProviderDiagnostic` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:606` |
| `WorkstationDataProviderRecord` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:628` |
| `WorkstationDataProviderRoutingSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:616` |
| `WorkstationGeneratedReportWriterGridPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:371` |
| `WorkstationMetricCard` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:20` |
| `WorkstationModeComparisonGroup` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:44` |
| `WorkstationPlotToolPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:121` |
| `WorkstationPlotToolTabState` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:108` |
| `WorkstationPortfolioPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:511` |
| `WorkstationPortfolioRunRow` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:492` |
| `WorkstationPortfolioSummaryPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:527` |
| `WorkstationPortfolioSummaryTelemetry` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:518` |
| `WorkstationReportAccessAuditSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:424` |
| `WorkstationReportPackDistributionPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:408` |
| `WorkstationReportWriterDatasetSourcePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:280` |
| `WorkstationReportWriterFieldPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:272` |
| `WorkstationReportWriterFilterPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:266` |
| `WorkstationReportWriterFormulaPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:261` |
| `WorkstationReportWriterGridPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:301` |
| `WorkstationReportWriterMetricPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:256` |
| `WorkstationReportingPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:443` |
| `WorkstationReportingProfilePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:244` |
| `WorkstationReportingRunLinkPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:353` |
| `WorkstationReportingRunNextActionPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:361` |
| `WorkstationReportingRunPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:384` |
| `WorkstationReportingTemplatePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:318` |
| `WorkstationRunDigest` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:31` |
| `WorkstationSecurityCoverageStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:267` |
| `WorkstationSecurityReference` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:284` |
| `WorkstationSessionPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:81` |
| `WorkstationSessionWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:69` |
| `WorkstationStrategyPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:132` |
| `WorkstationStrategyWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:97` |
| `WorkstationTimelineCard` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:52` |
| `WorkstationTradingBrokerageState` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:200` |
| `WorkstationTradingFillRow` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:174` |
| `WorkstationTradingOrderRow` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:161` |
| `WorkstationTradingPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:215` |
| `WorkstationTradingPositionRow` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:147` |
| `WorkstationTradingRiskState` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:187` |
| `WorkstationWatchlist` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:29` |
| `WorkstationWorkspaceDefinition` | Documented | `src/Meridian.Contracts/Workstation/WorkstationWorkspaceCatalog.cs:6` |

## Follow-up Queue

No API contract coverage gaps detected.

---

*This dashboard is auto-generated. Do not edit manually.*
