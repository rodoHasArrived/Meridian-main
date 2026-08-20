# API Contract Coverage Dashboard

_Auto-generated from canonical JSON payload._
_Generated: 1970-01-01T00:00:00+00:00_
Data sources: `src/**/*.cs endpoint mappings`, `src/Meridian.Contracts/Api/UiApiRoutes.cs`, `src/Meridian.Contracts/Workstation/*.cs`, `docs/reference/**/*.md (contract reference documentation only)`


Tracks whether mapped API routes and workstation DTO contracts are visible in the Markdown documentation set.

## Summary

| Metric | Value |
|---|---:|
| Weighted score | 21.4% |
| Endpoint coverage | 33.1% |
| Workstation contract coverage | 3.9% |
| Endpoints documented | 206 / 622 |
| Workstation contracts documented | 36 / 917 |

## Endpoint Coverage

| Method | Route | Status | Source |
|---|---|---|---|
| `GET` | `/api/accounting-system/export-packages` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:528` |
| `POST` | `/api/accounting-system/export-packages` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:561` |
| `POST` | `/api/accounting-system/export-packages/certification` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:647` |
| `GET` | `/api/accounting-system/export-packages/{exportPackageId}/manifest` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:609` |
| `GET` | `/api/accounting-system/import/latest` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:393` |
| `POST` | `/api/accounting-system/import/preview` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:368` |
| `GET` | `/api/accounting-system/mapping-profiles` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:451` |
| `POST` | `/api/accounting-system/mapping-profiles` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:480` |
| `GET` | `/api/accounting-system/migration-run-artifacts` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:231` |
| `POST` | `/api/accounting-system/migration-run-artifacts` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:259` |
| `POST` | `/api/accounting-system/migration-runs` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:194` |
| `GET` | `/api/accounting-system/migration-worker-plans` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:300` |
| `POST` | `/api/accounting-system/migration-worker-plans` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:330` |
| `GET` | `/api/accounting-system/production-certification-profile` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:124` |
| `POST` | `/api/accounting-system/production-certification-profile` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:151` |
| `POST` | `/api/accounting-system/production-readiness` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:36` |
| `GET` | `/api/accounting-system/providers` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:21` |
| `GET` | `/api/accounting-system/reconciliation/latest` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:422` |
| `GET` | `/api/accounting-system/tenant-administration-profile` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:59` |
| `POST` | `/api/accounting-system/tenant-administration-profile` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:83` |
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
| `GET` | `/api/admin/retention/compliance-report` | Gap | `src/Meridian.Ui.Shared/Endpoints/ResilienceEndpoints.cs:89` |
| `DELETE` | `/api/admin/retention/{policyId}/delete` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:199` |
| `POST` | `/api/admin/selftest` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:323` |
| `GET` | `/api/admin/show-config` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:362` |
| `POST` | `/api/admin/storage/migrate/{targetTier}` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:116` |
| `GET` | `/api/admin/storage/permissions` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:289` |
| `GET` | `/api/admin/storage/tiers` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:101` |
| `GET` | `/api/admin/storage/usage` | Gap | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:138` |
| `POST` | `/api/alignment/create` | Documented | `src/Meridian.Ui.Shared/Endpoints/HistoricalEndpoints.cs:177` |
| `POST` | `/api/alignment/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/HistoricalEndpoints.cs:197` |
| `GET` | `/api/analytics/anomalies` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:89` |
| `GET` | `/api/analytics/compare` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:53` |
| `GET` | `/api/analytics/completeness` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:115` |
| `GET` | `/api/analytics/gaps` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:25` |
| `POST` | `/api/analytics/gaps/repair` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:37` |
| `GET` | `/api/analytics/latency` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:65` |
| `GET` | `/api/analytics/latency/stats` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:77` |
| `GET` | `/api/analytics/quality-report` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:103` |
| `GET` | `/api/analytics/rate-limits` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:145` |
| `GET` | `/api/analytics/throughput` | Gap | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:133` |
| `GET` | `/api/auth/access-assignments` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:497` |
| `POST` | `/api/auth/access-assignments` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:538` |
| `POST` | `/api/auth/access-assignments/{assignmentId}/revoke` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:578` |
| `GET` | `/api/auth/accounts` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:209` |
| `PUT` | `/api/auth/accounts/{username}` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:232` |
| `POST` | `/api/auth/accounts/{username}/disable` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:330` |
| `POST` | `/api/auth/accounts/{username}/password-reset` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:270` |
| `GET` | `/api/auth/audit` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:421` |
| `POST` | `/api/auth/bootstrap` | Gap | `src/Meridian.Ui.Shared/Endpoints/InitialAccountBootstrapEndpoints.cs:19` |
| `GET` | `/api/auth/desktop-launch/{ticket}` | Gap | `src/Meridian.Ui.Shared/Endpoints/FirstRunEndpoints.cs:53` |
| `POST` | `/api/auth/login` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:39` |
| `POST` | `/api/auth/logout` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:148` |
| `GET` | `/api/auth/me` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:168` |
| `POST` | `/api/auth/role-profiles` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:445` |
| `GET` | `/api/auth/roles` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:194` |
| `POST` | `/api/auth/sessions/revoke` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:390` |
| `GET` | `/api/backfill/checkpoints` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:26` |
| `GET` | `/api/backfill/checkpoints/resumable` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:92` |
| `GET` | `/api/backfill/checkpoints/validation` | Gap | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:38` |
| `GET` | `/api/backfill/checkpoints/{jobId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:119` |
| `GET` | `/api/backfill/checkpoints/{jobId}/pending` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:145` |
| `POST` | `/api/backfill/checkpoints/{jobId}/resume` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:205` |
| `GET` | `/api/backfill/completeness` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:214` |
| `POST` | `/api/backfill/cost-estimate` | Documented | `src/Meridian.Ui.Shared/Endpoints/ResilienceEndpoints.cs:49` |
| `GET` | `/api/backfill/executions` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:173` |
| `POST` | `/api/backfill/gap-fill` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:89` |
| `GET` | `/api/backfill/gaps` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:166` |
| `GET` | `/api/backfill/health` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:31` |
| `GET` | `/api/backfill/presets` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:157` |
| `GET` | `/api/backfill/progress` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:190` |
| `GET` | `/api/backfill/providers` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:38` |
| `GET` | `/api/backfill/providers/audit` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:293` |
| `POST` | `/api/backfill/providers/dry-run-plan` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:263` |
| `GET` | `/api/backfill/providers/fallback-chain` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:245` |
| `GET` | `/api/backfill/providers/metadata` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:217` |
| `GET` | `/api/backfill/providers/statuses` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:228` |
| `GET` | `/api/backfill/resolve/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:63` |
| `POST` | `/api/backfill/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:127` |
| `POST` | `/api/backfill/run/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:71` |
| `GET` | `/api/backfill/schedules` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:236` |
| `POST` | `/api/backfill/schedules` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:258` |
| `GET` | `/api/backfill/schedules/templates` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:471` |
| `DELETE` | `/api/backfill/schedules/{id}` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:321` |
| `GET` | `/api/backfill/schedules/{id}` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:300` |
| `POST` | `/api/backfill/schedules/{id}/disable` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:367` |
| `POST` | `/api/backfill/schedules/{id}/enable` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:344` |
| `GET` | `/api/backfill/schedules/{id}/history` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:452` |
| `POST` | `/api/backfill/schedules/{id}/run` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:390` |
| `GET` | `/api/backfill/statistics` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:196` |
| `GET` | `/api/backfill/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:53` |
| `GET` | `/api/backfill/validation` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:26` |
| `GET` | `/api/backfill/validation/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:111` |
| `GET` | `/api/backpressure` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:141` |
| `GET` | `/api/calendar/holidays` | Gap | `src/Meridian.Ui.Shared/Endpoints/CalendarEndpoints.cs:64` |
| `GET` | `/api/calendar/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/CalendarEndpoints.cs:21` |
| `GET` | `/api/calendar/trading-days` | Gap | `src/Meridian.Ui.Shared/Endpoints/CalendarEndpoints.cs:89` |
| `GET` | `/api/canonicalization/config` | Gap | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:113` |
| `GET` | `/api/canonicalization/parity` | Gap | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:48` |
| `GET` | `/api/canonicalization/parity/{provider}` | Gap | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:76` |
| `GET` | `/api/canonicalization/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:21` |
| `GET` | `/api/catalog/coverage` | Gap | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:233` |
| `GET` | `/api/catalog/search` | Gap | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:25` |
| `GET` | `/api/catalog/symbols` | Gap | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:129` |
| `GET` | `/api/catalog/timeline` | Gap | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:161` |
| `GET` | `/api/compliance/access-reviews` | Gap | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:117` |
| `POST` | `/api/compliance/access-reviews/assess` | Gap | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:85` |
| `POST` | `/api/compliance/access-reviews/run` | Gap | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:101` |
| `POST` | `/api/compliance/actions/evaluate` | Gap | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:48` |
| `POST` | `/api/compliance/approval-requests` | Gap | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:15` |
| `POST` | `/api/compliance/approval-requests/{approvalRequestId}/decisions` | Gap | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:26` |
| `GET` | `/api/compliance/audit/extract` | Gap | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:66` |
| `GET` | `/api/compliance/controls/attestation` | Gap | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:70` |
| `GET` | `/api/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:36` |
| `POST` | `/api/config/alpaca` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:138` |
| `GET` | `/api/config/data-sources` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:635` |
| `POST` | `/api/config/data-sources` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:653` |
| `POST` | `/api/config/datasource` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:120` |
| `GET` | `/api/config/datasources` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:54` |
| `POST` | `/api/config/datasources` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:73` |
| `POST` | `/api/config/datasources/defaults` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:202` |
| `POST` | `/api/config/datasources/failover` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:235` |
| `GET` | `/api/config/derivatives` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:220` |
| `POST` | `/api/config/derivatives` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:229` |
| `GET` | `/api/config/effective` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:63` |
| `POST` | `/api/config/storage` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:151` |
| `POST` | `/api/config/symbols` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:180` |
| `GET` | `/api/connections` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:167` |
| `GET` | `/api/data/bbo/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:270` |
| `GET` | `/api/data/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:351` |
| `GET` | `/api/data/l3-orderbook/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:200` |
| `GET` | `/api/data/orderbook/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:128` |
| `GET` | `/api/data/orderflow/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:310` |
| `GET` | `/api/data/quotes-snapshot` | Gap | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:110` |
| `GET` | `/api/data/quotes/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:66` |
| `GET` | `/api/data/trades/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:26` |
| `GET` | `/api/demo/historical/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:140` |
| `GET` | `/api/demo/market-data/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:119` |
| `GET` | `/api/demo/mode` | Gap | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:80` |
| `GET` | `/api/demo/symbols` | Gap | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:103` |
| `POST` | `/api/dev/seed/bank-transactions` | Gap | `src/Meridian.Ui.Shared/Endpoints/BankingEndpoints.cs:308` |
| `GET` | `/api/diagnostics/bundle` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:150` |
| `GET` | `/api/diagnostics/config` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:124` |
| `GET` | `/api/diagnostics/coordination` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:502` |
| `POST` | `/api/diagnostics/dry-run` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:36` |
| `GET` | `/api/diagnostics/error-codes` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:381` |
| `GET` | `/api/diagnostics/metrics` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:171` |
| `GET` | `/api/diagnostics/providers` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:57` |
| `POST` | `/api/diagnostics/providers/{providerName}/test` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:300` |
| `GET` | `/api/diagnostics/quick-check` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:340` |
| `POST` | `/api/diagnostics/selftest` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:391` |
| `GET` | `/api/diagnostics/show-config` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:357` |
| `GET` | `/api/diagnostics/storage` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:74` |
| `POST` | `/api/diagnostics/test-connectivity` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:432` |
| `POST` | `/api/diagnostics/validate` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:277` |
| `POST` | `/api/diagnostics/validate-config` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:472` |
| `POST` | `/api/diagnostics/validate-credentials` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:413` |
| `GET` | `/api/errors` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:127` |
| `GET` | `/api/events/stream` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:233` |
| `POST` | `/api/export/analysis` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:82` |
| `GET` | `/api/export/formats` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:159` |
| `POST` | `/api/export/integrity` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:302` |
| `POST` | `/api/export/orderflow` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:237` |
| `GET` | `/api/export/preview` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:29` |
| `POST` | `/api/export/quality-report` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:220` |
| `POST` | `/api/export/research-package` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:431` |
| `POST` | `/api/export/strategy-package` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:423` |
| `GET` | `/api/failover/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:35` |
| `POST` | `/api/failover/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:65` |
| `GET` | `/api/failover/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:334` |
| `GET` | `/api/failover/rules` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:113` |
| `POST` | `/api/failover/rules` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:134` |
| `GET` | `/api/fund-accounts/brokerage-sync/accounts` | Gap | `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs:243` |
| `GET` | `/api/funds/{fundId:guid}/accounts` | Gap | `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs:127` |
| `GET` | `/api/health` | Gap | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:42` |
| `GET` | `/api/health/detailed` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:205` |
| `GET` | `/api/health/diagnostics/bundle` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:259` |
| `GET` | `/api/health/events` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:189` |
| `GET` | `/api/health/metrics` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:202` |
| `GET` | `/api/health/providers` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:56` |
| `GET` | `/api/health/providers/{provider}/diagnostics` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:104` |
| `POST` | `/api/health/providers/{provider}/test` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:221` |
| `GET` | `/api/health/storage` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:154` |
| `GET` | `/api/health/summary` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:29` |
| `GET` | `/api/indices/{indexName}/constituents` | Gap | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:560` |
| `GET` | `/api/ingestion/jobs` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:29` |
| `POST` | `/api/ingestion/jobs` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:116` |
| `GET` | `/api/ingestion/jobs/resumable` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:160` |
| `DELETE` | `/api/ingestion/jobs/{jobId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:182` |
| `GET` | `/api/ingestion/jobs/{jobId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:51` |
| `POST` | `/api/ingestion/jobs/{jobId}/transition` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:66` |
| `GET` | `/api/ingestion/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:171` |
| `POST` | `/api/journals/{journalEntryId:guid}/post` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:945` |
| `GET` | `/api/lean/algorithms` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:150` |
| `GET` | `/api/lean/auto-export` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:394` |
| `POST` | `/api/lean/auto-export/configure` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:425` |
| `GET` | `/api/lean/backtest/history` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:339` |
| `POST` | `/api/lean/backtest/start` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:252` |
| `DELETE` | `/api/lean/backtest/{backtestId}/delete` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:380` |
| `GET` | `/api/lean/backtest/{backtestId}/results` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:303` |
| `GET` | `/api/lean/backtest/{backtestId}/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:285` |
| `POST` | `/api/lean/backtest/{backtestId}/stop` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:323` |
| `GET` | `/api/lean/config` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:60` |
| `POST` | `/api/lean/results/ingest` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:461` |
| `GET` | `/api/lean/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:32` |
| `GET` | `/api/lean/symbol-map` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:555` |
| `POST` | `/api/lean/sync` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:180` |
| `GET` | `/api/lean/sync/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:232` |
| `POST` | `/api/lean/verify` | Gap | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:87` |
| `GET` | `/api/ledger/accounting-configuration` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:15` |
| `POST` | `/api/ledger/accounting-configuration/activate` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:615` |
| `POST` | `/api/ledger/accounting-configuration/asset-accounting/events/lifecycle` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:376` |
| `POST` | `/api/ledger/accounting-configuration/asset-accounting/events/project` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:322` |
| `GET` | `/api/ledger/accounting-configuration/audit` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:650` |
| `POST` | `/api/ledger/accounting-configuration/chart` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:43` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:105` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/candidates` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:279` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/candidates/asset-accounting` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:417` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/candidates/post` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:474` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/dry-run` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:238` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/projection-sets` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:531` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/promotion-approvals` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:136` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/test-cases` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:172` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/tests` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:574` |
| `POST` | `/api/ledger/accounting-configuration/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:203` |
| `POST` | `/api/ledger/accounting-configuration/templates` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:74` |
| `GET` | `/api/ledger/aggregates/{aggregateId:guid}/journal-entries` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:318` |
| `GET` | `/api/ledger/books` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:30` |
| `POST` | `/api/ledger/books` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:80` |
| `POST` | `/api/ledger/books/rollout-assessment` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:111` |
| `GET` | `/api/ledger/books/{ledgerBookId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:57` |
| `POST` | `/api/ledger/close-management/evidence-review` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:841` |
| `POST` | `/api/ledger/close-management/late-adjustments` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:655` |
| `POST` | `/api/ledger/close-management/late-adjustments/review` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:717` |
| `POST` | `/api/ledger/close-management/period-lock` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:903` |
| `POST` | `/api/ledger/close-management/period-plan/configuration` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:593` |
| `GET` | `/api/ledger/close-management/period-plan/{workflowId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:562` |
| `POST` | `/api/ledger/close-management/period-reopen` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:977` |
| `POST` | `/api/ledger/close-management/task-signoffs` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:779` |
| `POST` | `/api/ledger/journal-automation/daily-mark-to-market-batch-lifecycle` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:386` |
| `POST` | `/api/ledger/journal-automation/daily-mark-to-market-intake` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:435` |
| `POST` | `/api/ledger/journal-automation/daily-mark-to-market-run-due` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:356` |
| `GET` | `/api/ledger/journal-automation/daily-mark-to-market-schedules` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:235` |
| `POST` | `/api/ledger/journal-automation/daily-mark-to-market-schedules` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:266` |
| `POST` | `/api/ledger/journal-automation/dividend-intake` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:478` |
| `POST` | `/api/ledger/journal-automation/fee-accrual-intake` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:521` |
| `GET` | `/api/ledger/journal-automation/monthly-schedules` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:23` |
| `POST` | `/api/ledger/journal-automation/monthly-schedules` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:59` |
| `POST` | `/api/ledger/journal-automation/monthly-schedules/run-due` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:205` |
| `POST` | `/api/ledger/journal-automation/period-close-intake` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:566` |
| `GET` | `/api/ledger/journal-entry-workbench` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1250` |
| `POST` | `/api/ledger/journal-entry-workbench/drafts` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1571` |
| `POST` | `/api/ledger/journal-entry-workbench/evidence` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1721` |
| `POST` | `/api/ledger/journal-entry-workbench/lifecycle-action` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1764` |
| `POST` | `/api/ledger/journal-entry-workbench/submit-approval` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1676` |
| `POST` | `/api/ledger/journal-entry-workbench/validate` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1630` |
| `GET` | `/api/ledger/periods` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:152` |
| `POST` | `/api/ledger/periods` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:190` |
| `POST` | `/api/ledger/periods/{periodId:guid}/close` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:222` |
| `GET` | `/api/ledger/periods/{periodId:guid}/journal-entries` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:274` |
| `GET` | `/api/ledger/periods/{periodId:guid}/pnl-summary` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:417` |
| `GET` | `/api/ledger/periods/{periodId:guid}/trial-balance` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:367` |
| `GET` | `/api/ledger/periods/{periodId:guid}/trial-balance-report` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:392` |
| `GET` | `/api/ledger/private-capital/activity` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1276` |
| `GET` | `/api/ledger/private-capital/capital-account-subledger` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1392` |
| `GET` | `/api/ledger/private-capital/capital-account-workbench` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1524` |
| `GET` | `/api/ledger/private-capital/fund-event-command-center` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1351` |
| `GET` | `/api/ledger/private-capital/fund-event-record` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1308` |
| `GET` | `/api/ledger/private-capital/report-output` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1453` |
| `POST` | `/api/ledger/reports/accounting-package` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1048` |
| `POST` | `/api/ledger/reports/accounting-package/certification` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1099` |
| `GET` | `/api/ledger/reports/accounting-packages` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1157` |
| `GET` | `/api/ledger/reports/accounting-packages/{packageId}/exports/{artifactId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1199` |
| `GET` | `/api/ledger/reports/pnl-summary` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:501` |
| `GET` | `/api/ledger/reports/trial-balance` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:442` |
| `GET` | `/api/loans/portfolio` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1102` |
| `POST` | `/api/loans/rebuild-all` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1113` |
| `GET` | `/api/loans/rebuild-checkpoints` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1093` |
| `POST` | `/api/maintenance/execute` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:142` |
| `GET` | `/api/maintenance/executions` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:183` |
| `POST` | `/api/maintenance/executions/cleanup` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:432` |
| `GET` | `/api/maintenance/executions/failed` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:228` |
| `GET` | `/api/maintenance/executions/{executionId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:197` |
| `POST` | `/api/maintenance/executions/{executionId}/cancel` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:166` |
| `GET` | `/api/maintenance/presets` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:353` |
| `GET` | `/api/maintenance/schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:21` |
| `POST` | `/api/maintenance/schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:36` |
| `GET` | `/api/maintenance/schedules/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:244` |
| `GET` | `/api/maintenance/schedules/{id}` | Gap | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:50` |
| `DELETE` | `/api/maintenance/schedules/{id}/delete` | Gap | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:60` |
| `POST` | `/api/maintenance/schedules/{id}/disable` | Gap | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:88` |
| `POST` | `/api/maintenance/schedules/{id}/enable` | Gap | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:74` |
| `GET` | `/api/maintenance/schedules/{id}/history` | Gap | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:131` |
| `POST` | `/api/maintenance/schedules/{id}/run` | Gap | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:102` |
| `DELETE` | `/api/maintenance/schedules/{scheduleId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:99` |
| `PUT` | `/api/maintenance/schedules/{scheduleId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:37` |
| `GET` | `/api/maintenance/schedules/{scheduleId}/executions` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:213` |
| `GET` | `/api/maintenance/schedules/{scheduleId}/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:256` |
| `POST` | `/api/maintenance/schedules/{scheduleId}/trigger` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:119` |
| `GET` | `/api/maintenance/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:271` |
| `GET` | `/api/maintenance/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:299` |
| `GET` | `/api/maintenance/task-types` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:410` |
| `POST` | `/api/maintenance/validate-cron` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:313` |
| `GET` | `/api/messaging/activity` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:98` |
| `GET` | `/api/messaging/config` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:31` |
| `GET` | `/api/messaging/consumers` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:127` |
| `GET` | `/api/messaging/endpoints` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:146` |
| `GET` | `/api/messaging/errors` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:317` |
| `POST` | `/api/messaging/errors/{messageId}/retry` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:338` |
| `GET` | `/api/messaging/publishing` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:274` |
| `POST` | `/api/messaging/queues/{queueName}/purge` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:291` |
| `GET` | `/api/messaging/stats` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:70` |
| `GET` | `/api/messaging/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:52` |
| `POST` | `/api/messaging/test` | Gap | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:173` |
| `GET` | `/api/options/chains/{underlyingSymbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:78` |
| `GET` | `/api/options/expirations/{underlyingSymbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:28` |
| `GET` | `/api/options/quotes/{underlyingSymbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:136` |
| `POST` | `/api/options/refresh` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:202` |
| `GET` | `/api/options/strikes/{underlyingSymbol}/{expiration}` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:51` |
| `GET` | `/api/options/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:156` |
| `GET` | `/api/options/underlyings` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:184` |
| `GET` | `/api/packaging/contents` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:171` |
| `POST` | `/api/packaging/create` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:34` |
| `GET` | `/api/packaging/download/{fileName}` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:287` |
| `POST` | `/api/packaging/import` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:88` |
| `GET` | `/api/packaging/list` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:204` |
| `POST` | `/api/packaging/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:137` |
| `DELETE` | `/api/packaging/{fileName}` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:249` |
| `GET` | `/api/plaid/accounts` | Gap | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:32` |
| `GET` | `/api/plaid/institutions/search` | Gap | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:45` |
| `GET` | `/api/plaid/items` | Gap | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:19` |
| `POST` | `/api/plaid/items/{itemId}/sync` | Gap | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:125` |
| `POST` | `/api/plaid/link-token` | Gap | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:75` |
| `POST` | `/api/plaid/public-token/exchange` | Gap | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:100` |
| `POST` | `/api/plaid/transfers/sandbox` | Gap | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:207` |
| `POST` | `/api/plaid/webhook` | Gap | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:155` |
| `GET` | `/api/portfolio/household` | Gap | `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs:266` |
| `GET` | `/api/projections/{projectionRunId:guid}/flows` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:928` |
| `GET` | `/api/provider-routing/bindings` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:36` |
| `GET` | `/api/provider-routing/connections` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:20` |
| `POST` | `/api/provider-routing/preview` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:68` |
| `GET` | `/api/provider-routing/trust-snapshots` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:52` |
| `GET` | `/api/providers/capabilities` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:146` |
| `GET` | `/api/providers/capability-matrix` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:178` |
| `GET` | `/api/providers/catalog` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:572` |
| `GET` | `/api/providers/catalog/{providerId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:618` |
| `GET` | `/api/providers/comparison` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:290` |
| `POST` | `/api/providers/configure` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:267` |
| `GET` | `/api/providers/connections` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:19` |
| `GET` | `/api/providers/dashboard` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:305` |
| `GET` | `/api/providers/data-projection` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderDataProjectionEndpoints.cs:16` |
| `GET` | `/api/providers/failover` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:57` |
| `GET` | `/api/providers/failover-thresholds` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:230` |
| `POST` | `/api/providers/failover/reset` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:100` |
| `POST` | `/api/providers/failover/trigger` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:83` |
| `GET` | `/api/providers/health` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:247` |
| `GET` | `/api/providers/ib/error-codes` | Documented | `src/Meridian.Ui.Shared/Endpoints/IBEndpoints.cs:92` |
| `GET` | `/api/providers/ib/limits` | Documented | `src/Meridian.Ui.Shared/Endpoints/IBEndpoints.cs:114` |
| `GET` | `/api/providers/ib/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/IBEndpoints.cs:25` |
| `GET` | `/api/providers/latency` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:152` |
| `GET` | `/api/providers/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:488` |
| `GET` | `/api/providers/modules` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:24` |
| `POST` | `/api/providers/modules` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:57` |
| `GET` | `/api/providers/modules/catalogue` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:41` |
| `DELETE` | `/api/providers/modules/{moduleId}` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:111` |
| `PUT` | `/api/providers/modules/{moduleId}` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:83` |
| `PUT` | `/api/providers/modules/{moduleId}/enabled` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:136` |
| `POST` | `/api/providers/modules/{moduleId}/test` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:159` |
| `GET` | `/api/providers/rate-limits` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:116` |
| `GET` | `/api/providers/readiness` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:361` |
| `POST` | `/api/providers/restart` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:193` |
| `GET` | `/api/providers/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:373` |
| `POST` | `/api/providers/switch` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:165` |
| `DELETE` | `/api/providers/{providerId}/credentials` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:87` |
| `PUT` | `/api/providers/{providerId}/credentials` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:34` |
| `POST` | `/api/providers/{providerId}/verify` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:63` |
| `GET` | `/api/providers/{providerName}` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:27` |
| `GET` | `/api/providers/{providerName}/rate-limit-history` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:129` |
| `POST` | `/api/providers/{providerName}/test` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:193` |
| `POST` | `/api/providers/{provider}/test-connection` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderCredentialEndpoints.cs:69` |
| `POST` | `/api/providers/{provider}/validate-credentials` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderCredentialEndpoints.cs:26` |
| `GET` | `/api/quality/anomalies` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:243` |
| `GET` | `/api/quality/anomalies/stale` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:287` |
| `GET` | `/api/quality/anomalies/statistics` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:284` |
| `GET` | `/api/quality/anomalies/unacknowledged` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:272` |
| `POST` | `/api/quality/anomalies/{anomalyId}/acknowledge` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:275` |
| `GET` | `/api/quality/anomalies/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:269` |
| `GET` | `/api/quality/comparison/discrepancies` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:322` |
| `GET` | `/api/quality/comparison/statistics` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:334` |
| `GET` | `/api/quality/comparison/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:315` |
| `GET` | `/api/quality/completeness` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:85` |
| `GET` | `/api/quality/completeness/low` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:110` |
| `GET` | `/api/quality/completeness/summary` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:107` |
| `GET` | `/api/quality/completeness/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:92` |
| `GET` | `/api/quality/dashboard` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:69` |
| `GET` | `/api/quality/drops` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:292` |
| `GET` | `/api/quality/drops/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:317` |
| `GET` | `/api/quality/errors` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:216` |
| `GET` | `/api/quality/errors/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:235` |
| `GET` | `/api/quality/errors/top-symbols` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:238` |
| `GET` | `/api/quality/errors/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:228` |
| `GET` | `/api/quality/gaps` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:119` |
| `GET` | `/api/quality/gaps/statistics` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:207` |
| `GET` | `/api/quality/gaps/timeline/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:199` |
| `GET` | `/api/quality/gaps/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:131` |
| `POST` | `/api/quality/gaps/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:138` |
| `GET` | `/api/quality/health` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:381` |
| `GET` | `/api/quality/health/unhealthy` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:414` |
| `GET` | `/api/quality/health/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:405` |
| `GET` | `/api/quality/latency` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:292` |
| `GET` | `/api/quality/latency/high` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:310` |
| `GET` | `/api/quality/latency/statistics` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:307` |
| `GET` | `/api/quality/latency/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:295` |
| `GET` | `/api/quality/latency/{symbol}/histogram` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:304` |
| `GET` | `/api/quality/metrics` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:80` |
| `GET` | `/api/quality/reports/daily` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:339` |
| `POST` | `/api/quality/reports/export` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:366` |
| `GET` | `/api/quality/reports/weekly` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:347` |
| `POST` | `/api/quant/parameters` | Gap | `src/Meridian.Ui.Shared/Endpoints/QuantLabEndpoints.cs:89` |
| `POST` | `/api/quant/run` | Gap | `src/Meridian.Ui.Shared/Endpoints/QuantLabEndpoints.cs:37` |
| `GET` | `/api/quant/templates` | Gap | `src/Meridian.Ui.Shared/Endpoints/QuantLabEndpoints.cs:126` |
| `GET` | `/api/reconciliation/exceptions` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1007` |
| `POST` | `/api/reconciliation/exceptions/{exceptionId:guid}/resolve` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1016` |
| `GET` | `/api/reconciliation/{runId:guid}/results` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:998` |
| `GET` | `/api/reference-data/bonds/issuer-ladder` | Gap | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:63` |
| `GET` | `/api/reference-data/bonds/maturity-ladder` | Gap | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:87` |
| `GET` | `/api/reference-data/bonds/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:24` |
| `GET` | `/api/reference-data/bonds/{securityId:guid}/accrual-convention` | Gap | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:50` |
| `GET` | `/api/reference-data/bonds/{securityId:guid}/lifecycle` | Gap | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:37` |
| `GET` | `/api/reference-data/certificates-of-deposit/by-issuer` | Gap | `src/Meridian.Ui.Shared/Endpoints/CertificateOfDepositReferenceEndpoints.cs:30` |
| `GET` | `/api/reference-data/certificates-of-deposit/maturing-before` | Gap | `src/Meridian.Ui.Shared/Endpoints/CertificateOfDepositReferenceEndpoints.cs:41` |
| `GET` | `/api/reference-data/certificates-of-deposit/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/CertificateOfDepositReferenceEndpoints.cs:18` |
| `GET` | `/api/reference-data/commodities/by-exchange` | Gap | `src/Meridian.Ui.Shared/Endpoints/CommodityReferenceEndpoints.cs:41` |
| `GET` | `/api/reference-data/commodities/by-type` | Gap | `src/Meridian.Ui.Shared/Endpoints/CommodityReferenceEndpoints.cs:30` |
| `GET` | `/api/reference-data/commodities/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/CommodityReferenceEndpoints.cs:18` |
| `GET` | `/api/reference-data/crypto/by-base-currency` | Gap | `src/Meridian.Ui.Shared/Endpoints/CryptoReferenceEndpoints.cs:48` |
| `GET` | `/api/reference-data/crypto/by-network` | Gap | `src/Meridian.Ui.Shared/Endpoints/CryptoReferenceEndpoints.cs:35` |
| `GET` | `/api/reference-data/crypto/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/CryptoReferenceEndpoints.cs:21` |
| `GET` | `/api/reference-data/deposits/by-institution` | Gap | `src/Meridian.Ui.Shared/Endpoints/DepositReferenceEndpoints.cs:35` |
| `GET` | `/api/reference-data/deposits/maturing-before` | Gap | `src/Meridian.Ui.Shared/Endpoints/DepositReferenceEndpoints.cs:48` |
| `GET` | `/api/reference-data/deposits/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DepositReferenceEndpoints.cs:21` |
| `GET` | `/api/reference-data/edgar/facts/{cik}` | Documented | `src/Meridian.Ui.Shared/Endpoints/EdgarReferenceDataEndpoints.cs:63` |
| `GET` | `/api/reference-data/edgar/filers/{cik}` | Documented | `src/Meridian.Ui.Shared/Endpoints/EdgarReferenceDataEndpoints.cs:49` |
| `GET` | `/api/reference-data/edgar/security-data/{cik}` | Documented | `src/Meridian.Ui.Shared/Endpoints/EdgarReferenceDataEndpoints.cs:77` |
| `GET` | `/api/reference-data/equities/by-exchange` | Gap | `src/Meridian.Ui.Shared/Endpoints/EquityReferenceEndpoints.cs:30` |
| `GET` | `/api/reference-data/equities/by-issuer` | Gap | `src/Meridian.Ui.Shared/Endpoints/EquityReferenceEndpoints.cs:41` |
| `GET` | `/api/reference-data/equities/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/EquityReferenceEndpoints.cs:18` |
| `GET` | `/api/reference-data/futures/by-root` | Gap | `src/Meridian.Ui.Shared/Endpoints/FutureReferenceEndpoints.cs:35` |
| `GET` | `/api/reference-data/futures/expiry-ladder` | Gap | `src/Meridian.Ui.Shared/Endpoints/FutureReferenceEndpoints.cs:48` |
| `GET` | `/api/reference-data/futures/front-month` | Gap | `src/Meridian.Ui.Shared/Endpoints/FutureReferenceEndpoints.cs:61` |
| `GET` | `/api/reference-data/futures/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/FutureReferenceEndpoints.cs:21` |
| `GET` | `/api/reference-data/fxspot/by-currency` | Gap | `src/Meridian.Ui.Shared/Endpoints/FxSpotReferenceEndpoints.cs:42` |
| `GET` | `/api/reference-data/fxspot/pairs/{pairCode}` | Gap | `src/Meridian.Ui.Shared/Endpoints/FxSpotReferenceEndpoints.cs:30` |
| `GET` | `/api/reference-data/fxspot/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/FxSpotReferenceEndpoints.cs:18` |
| `GET` | `/api/reference-data/money-market-funds/by-family` | Gap | `src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundReferenceEndpoints.cs:30` |
| `GET` | `/api/reference-data/money-market-funds/by-sweep-eligibility` | Gap | `src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundReferenceEndpoints.cs:41` |
| `GET` | `/api/reference-data/money-market-funds/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundReferenceEndpoints.cs:18` |
| `POST` | `/api/reference-data/options/chains/import` | Gap | `src/Meridian.Ui.Shared/Endpoints/OptionChainEndpoints.cs:19` |
| `GET` | `/api/reference-data/options/chains/snapshot` | Gap | `src/Meridian.Ui.Shared/Endpoints/OptionChainEndpoints.cs:44` |
| `GET` | `/api/reference-data/options/contracts/{contractSymbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/OptionReferenceEndpoints.cs:18` |
| `GET` | `/api/reference-data/options/contracts/{contractSymbol}/underlying-linkage` | Gap | `src/Meridian.Ui.Shared/Endpoints/OptionReferenceEndpoints.cs:43` |
| `GET` | `/api/reference-data/options/expiry-ladder` | Gap | `src/Meridian.Ui.Shared/Endpoints/OptionReferenceEndpoints.cs:55` |
| `GET` | `/api/reference-data/options/series/{optionChainId}` | Gap | `src/Meridian.Ui.Shared/Endpoints/OptionReferenceEndpoints.cs:30` |
| `GET` | `/api/reference-data/swaps/by-type` | Gap | `src/Meridian.Ui.Shared/Endpoints/SwapReferenceEndpoints.cs:30` |
| `GET` | `/api/reference-data/swaps/maturing-before` | Gap | `src/Meridian.Ui.Shared/Endpoints/SwapReferenceEndpoints.cs:41` |
| `GET` | `/api/reference-data/swaps/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/SwapReferenceEndpoints.cs:18` |
| `GET` | `/api/replay/files` | Gap | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:29` |
| `GET` | `/api/replay/preview` | Gap | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:197` |
| `POST` | `/api/replay/start` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:69` |
| `GET` | `/api/replay/stats` | Gap | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:277` |
| `POST` | `/api/replay/{sessionId}/pause` | Gap | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:101` |
| `POST` | `/api/replay/{sessionId}/resume` | Gap | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:115` |
| `POST` | `/api/replay/{sessionId}/seek` | Gap | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:143` |
| `POST` | `/api/replay/{sessionId}/speed` | Gap | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:156` |
| `GET` | `/api/replay/{sessionId}/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:170` |
| `POST` | `/api/replay/{sessionId}/stop` | Gap | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:129` |
| `GET` | `/api/resilience/circuit-breakers` | Gap | `src/Meridian.Ui.Shared/Endpoints/ResilienceEndpoints.cs:26` |
| `POST` | `/api/sampling/create` | Documented | `src/Meridian.Ui.Shared/Endpoints/SamplingEndpoints.cs:25` |
| `GET` | `/api/sampling/estimate` | Gap | `src/Meridian.Ui.Shared/Endpoints/SamplingEndpoints.cs:112` |
| `GET` | `/api/sampling/saved` | Gap | `src/Meridian.Ui.Shared/Endpoints/SamplingEndpoints.cs:154` |
| `GET` | `/api/sampling/{sampleId}` | Gap | `src/Meridian.Ui.Shared/Endpoints/SamplingEndpoints.cs:178` |
| `POST` | `/api/schedules/cron/next-runs` | Gap | `src/Meridian.Ui.Shared/Endpoints/CronEndpoints.cs:43` |
| `POST` | `/api/schedules/cron/validate` | Gap | `src/Meridian.Ui.Shared/Endpoints/CronEndpoints.cs:21` |
| `POST` | `/api/security-master` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:348` |
| `POST` | `/api/security-master/aliases/upsert` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:432` |
| `POST` | `/api/security-master/amend` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:376` |
| `GET` | `/api/security-master/asset-profiles` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:79` |
| `POST` | `/api/security-master/asset-profiles/approve` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:166` |
| `POST` | `/api/security-master/asset-profiles/drafts` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:125` |
| `GET` | `/api/security-master/asset-profiles/promotion-candidates` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:97` |
| `POST` | `/api/security-master/asset-profiles/rollback` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:207` |
| `GET` | `/api/security-master/asset-profiles/{profileId}/lineage` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:109` |
| `GET` | `/api/security-master/conflicts` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:762` |
| `POST` | `/api/security-master/conflicts/{conflictId:guid}/resolve` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:775` |
| `GET` | `/api/security-master/corporate-actions/inbox` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:687` |
| `POST` | `/api/security-master/corporate-actions/inbox/apply` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:697` |
| `POST` | `/api/security-master/corporate-actions/ingest` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:658` |
| `GET` | `/api/security-master/coverage/draft/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:746` |
| `GET` | `/api/security-master/data-entitlements` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1465` |
| `POST` | `/api/security-master/data-entitlements` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1487` |
| `GET` | `/api/security-master/data-entitlements/expiring` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1475` |
| `DELETE` | `/api/security-master/data-entitlements/{entitlementId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1515` |
| `POST` | `/api/security-master/deactivate` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:404` |
| `GET` | `/api/security-master/exceptions/aging` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1578` |
| `POST` | `/api/security-master/import` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:886` |
| `POST` | `/api/security-master/ingest/edgar` | Documented | `src/Meridian.Ui.Shared/Endpoints/EdgarReferenceDataEndpoints.cs:24` |
| `GET` | `/api/security-master/ingest/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:917` |
| `GET` | `/api/security-master/quality-report/latest` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1562` |
| `POST` | `/api/security-master/quality-report/run` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1547` |
| `POST` | `/api/security-master/resolve` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:253` |
| `POST` | `/api/security-master/search` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:290` |
| `GET` | `/api/security-master/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:42` |
| `GET` | `/api/security-master/{securityId:guid}/cashflow-projections` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1442` |
| `GET` | `/api/security-master/{securityId:guid}/cashflow-source` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1401` |
| `PUT` | `/api/security-master/{securityId:guid}/cashflow-source` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1414` |
| `GET` | `/api/security-master/{securityId:guid}/convertible-equity-terms` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:540` |
| `PATCH` | `/api/security-master/{securityId:guid}/convertible-equity-terms` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:561` |
| `GET` | `/api/security-master/{securityId:guid}/corporate-actions` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:598` |
| `POST` | `/api/security-master/{securityId:guid}/corporate-actions` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:619` |
| `GET` | `/api/security-master/{securityId:guid}/field-provenance` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:960` |
| `GET` | `/api/security-master/{securityId:guid}/history` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:321` |
| `GET` | `/api/security-master/{securityId:guid}/operator-overrides` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:938` |
| `PATCH` | `/api/security-master/{securityId:guid}/operator-overrides` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:979` |
| `POST` | `/api/security-master/{securityId:guid}/operator-overrides/decision` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1051` |
| `GET` | `/api/security-master/{securityId:guid}/preferred-equity-terms` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:483` |
| `PATCH` | `/api/security-master/{securityId:guid}/preferred-equity-terms` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:504` |
| `GET` | `/api/security-master/{securityId:guid}/price-comparison` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1384` |
| `GET` | `/api/security-master/{securityId:guid}/price-golden-copy` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1370` |
| `GET` | `/api/security-master/{securityId:guid}/pricing-hierarchy` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1296` |
| `PUT` | `/api/security-master/{securityId:guid}/pricing-hierarchy` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1310` |
| `POST` | `/api/security-master/{securityId:guid}/raw-price` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1340` |
| `GET` | `/api/security-master/{securityId:guid}/trading-parameters` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:460` |
| `GET` | `/api/security-master/{securityId:guid}/validation` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:63` |
| `POST` | `/api/servicer-reports` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1039` |
| `GET` | `/api/servicer-reports/{batchId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1067` |
| `GET` | `/api/servicer-reports/{batchId:guid}/position-lines` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1075` |
| `GET` | `/api/servicer-reports/{batchId:guid}/transaction-lines` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1084` |
| `GET` | `/api/sla/health` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:641` |
| `GET` | `/api/sla/metrics` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:668` |
| `GET` | `/api/sla/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:610` |
| `GET` | `/api/sla/status/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:614` |
| `GET` | `/api/sla/violations` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:624` |
| `GET` | `/api/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:116` |
| `GET` | `/api/storage/archive/stats` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:311` |
| `GET` | `/api/storage/breakdown` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:80` |
| `GET` | `/api/storage/capacity-forecast` | Gap | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:541` |
| `GET` | `/api/storage/catalog` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:345` |
| `POST` | `/api/storage/cleanup` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:275` |
| `GET` | `/api/storage/cleanup/candidates` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:240` |
| `POST` | `/api/storage/convert-parquet` | Gap | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:516` |
| `GET` | `/api/storage/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:208` |
| `GET` | `/api/storage/health/check` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:403` |
| `GET` | `/api/storage/health/orphans` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:420` |
| `POST` | `/api/storage/maintenance/defrag` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:498` |
| `GET` | `/api/storage/profiles` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:25` |
| `GET` | `/api/storage/quality/alerts` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:104` |
| `POST` | `/api/storage/quality/alerts/{alertId}/acknowledge` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:124` |
| `GET` | `/api/storage/quality/anomalies` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:200` |
| `POST` | `/api/storage/quality/check` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:231` |
| `GET` | `/api/storage/quality/rankings/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:135` |
| `GET` | `/api/storage/quality/scores` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:55` |
| `GET` | `/api/storage/quality/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:26` |
| `GET` | `/api/storage/quality/symbol/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:82` |
| `GET` | `/api/storage/quality/trends` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:162` |
| `GET` | `/api/storage/search/files` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:362` |
| `GET` | `/api/storage/stats` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:37` |
| `GET` | `/api/storage/symbol/{symbol}/files` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:158` |
| `GET` | `/api/storage/symbol/{symbol}/info` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:106` |
| `GET` | `/api/storage/symbol/{symbol}/path` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:185` |
| `GET` | `/api/storage/symbol/{symbol}/stats` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:131` |
| `POST` | `/api/storage/tiers/migrate` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:436` |
| `GET` | `/api/storage/tiers/plan` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:479` |
| `GET` | `/api/storage/tiers/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:463` |
| `POST` | `/api/strategies/covered-call/chain-preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:197` |
| `GET` | `/api/strategies/covered-call/runs` | Gap | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:63` |
| `POST` | `/api/strategies/covered-call/runs` | Gap | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:28` |
| `POST` | `/api/strategies/covered-call/runs/{runId}/cancel` | Gap | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:167` |
| `GET` | `/api/strategies/covered-call/runs/{runId}/result` | Gap | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:118` |
| `GET` | `/api/strategies/covered-call/runs/{runId}/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:88` |
| `GET` | `/api/strategies/runs/compare` | Gap | `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs:2940` |
| `GET` | `/api/strategies/{strategyId}/runs` | Gap | `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs:2799` |
| `GET` | `/api/subscriptions/active` | Gap | `src/Meridian.Ui.Shared/Endpoints/SubscriptionEndpoints.cs:22` |
| `POST` | `/api/subscriptions/subscribe` | Gap | `src/Meridian.Ui.Shared/Endpoints/SubscriptionEndpoints.cs:44` |
| `POST` | `/api/subscriptions/unsubscribe/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/SubscriptionEndpoints.cs:73` |
| `GET` | `/api/symbols` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:32` |
| `POST` | `/api/symbols/add` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:139` |
| `GET` | `/api/symbols/archived` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:80` |
| `POST` | `/api/symbols/batch` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:394` |
| `POST` | `/api/symbols/bulk-add` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:309` |
| `POST` | `/api/symbols/bulk-remove` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:349` |
| `POST` | `/api/symbols/create` | Gap | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:442` |
| `GET` | `/api/symbols/mappings` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolMappingEndpoints.cs:70` |
| `POST` | `/api/symbols/mappings` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolMappingEndpoints.cs:77` |
| `GET` | `/api/symbols/monitored` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:57` |
| `GET` | `/api/symbols/registry` | Gap | `src/Meridian.Ui.Shared/Endpoints/SymbolMappingEndpoints.cs:29` |
| `GET` | `/api/symbols/search` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:370` |
| `GET` | `/api/symbols/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:233` |
| `POST` | `/api/symbols/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:271` |
| `DELETE` | `/api/symbols/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:532` |
| `POST` | `/api/symbols/{symbol}/archive` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:290` |
| `GET` | `/api/symbols/{symbol}/depth` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:208` |
| `POST` | `/api/symbols/{symbol}/remove` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:163` |
| `GET` | `/api/symbols/{symbol}/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:99` |
| `GET` | `/api/symbols/{symbol}/trades` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:183` |
| `POST` | `/api/symbols/{symbol}/update` | Gap | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:482` |
| `GET` | `/api/system/lifecycle` | Documented | `src/Meridian/UiServer.cs:643` |
| `POST` | `/api/system/shutdown` | Documented | `src/Meridian/UiServer.cs:677` |
| `GET` | `/api/system/shutdown/receipts/latest` | Documented | `src/Meridian/UiServer.cs:748` |
| `GET` | `/api/system/shutdown/{operationId}` | Documented | `src/Meridian/UiServer.cs:727` |
| `POST` | `/api/workstation/desktop/launch` | Gap | `src/Meridian.Ui.Shared/Endpoints/FirstRunEndpoints.cs:30` |
| `GET` | `/health` | Documented | `src/Meridian.Application/Composition/HostAdapters.cs:60` |
| `GET` | `/live` | Documented | `src/Meridian.Application/Composition/HostAdapters.cs:73` |
| `GET` | `/ready` | Documented | `src/Meridian.Application/Composition/HostAdapters.cs:72` |

## Workstation Contract Coverage

| Contract | Status | Source |
|---|---|---|
| `AccrualCalculationResultDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:267` |
| `AccrualInputSnapshotDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:249` |
| `ActivationOutcomeDto` | Gap | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:76` |
| `AlpacaBrokerageConnectionRequestDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:103` |
| `ApprovalDecision` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:30` |
| `ApprovalPolicy` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:22` |
| `ApprovalStep` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:21` |
| `ApproveSecurityMasterOverrides` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:22` |
| `ApproveSecurityMasterRevisionRequest` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:98` |
| `ApproveWorkflow` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:29` |
| `AuditTrailExplorerQueryDto` | Gap | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:8` |
| `AuditTrailExplorerResultDto` | Gap | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:54` |
| `AuditTrailObjectKindDto` | Gap | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:64` |
| `AuditTrailTimelineEntryDto` | Gap | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:27` |
| `AutomatedJournalCapitalAccountReconciliationDto` | Gap | `src/Meridian.Contracts/Workstation/AutomatedJournalScheduleDtos.cs:25` |
| `AutomatedJournalScheduleStateDto` | Gap | `src/Meridian.Contracts/Workstation/AutomatedJournalScheduleDtos.cs:8` |
| `AutomatedJournalScheduleStatusDto` | Gap | `src/Meridian.Contracts/Workstation/AutomatedJournalScheduleDtos.cs:51` |
| `BankAccountSnapshot` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:102` |
| `BankStatementImportResultDto` | Gap | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:74` |
| `BiasDisclosureDto` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:271` |
| `BiasDisclosureItemDto` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:260` |
| `BooksBeforeBrokerReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:62` |
| `BrokerageAccountKindDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:18` |
| `BrokerageAccountLinkRequestDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:61` |
| `BrokerageCashFlowEntryDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:130` |
| `BrokerageCashFlowSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:140` |
| `BrokerageConnectionStateDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:27` |
| `BrokerageConnectionStatusDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:86` |
| `BrokerageHouseholdAccountDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:155` |
| `BrokerageHouseholdPortfolioDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:188` |
| `BrokerageHouseholdPositionDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:171` |
| `BrokeragePortfolioPerformanceDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:114` |
| `BrokeragePortfolioPerformancePointDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:108` |
| `BuildLedgerDraft` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:23` |
| `BulkResolveSecurityMasterConflictsRequest` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:577` |
| `BulkResolveSecurityMasterConflictsResult` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:583` |
| `CanonicalizationAssuranceDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:109` |
| `CanonicalizationProviderSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:119` |
| `CashFinancingSummary` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:140` |
| `CashFlowEntryDto` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:932` |
| `CashFlowProjectionPoint` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:34` |
| `CashForecastResult` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:36` |
| `CashLadderBucketDto` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:942` |
| `CashSyncSourceAvailability` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:2` |
| `CashSyncWindow` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:5` |
| `CloseWorkflow` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:31` |
| `ClosedLotSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1124` |
| `CollateralCallDto` | Gap | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:26` |
| `CompleteActivationOutcomeRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:92` |
| `CompleteFirstRunRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:86` |
| `CorporateActionDescriptorDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:82` |
| `CorporateActionTimelineEntryDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:96` |
| `CounterpartyExposureDto` | Gap | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:10` |
| `CouponEvent` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:20` |
| `CrossFundReportingConsolidationDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:323` |
| `CrossFundReportingConsolidationScopeDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:313` |
| `DailyValuationBatchLifecycleRequestDto` | Gap | `src/Meridian.Contracts/Workstation/DailyValuationScheduleDtos.cs:54` |
| `DailyValuationBatchLifecycleResultDto` | Gap | `src/Meridian.Contracts/Workstation/DailyValuationScheduleDtos.cs:67` |
| `DailyValuationScheduleStateDto` | Gap | `src/Meridian.Contracts/Workstation/DailyValuationScheduleDtos.cs:7` |
| `DailyValuationScheduleStatusDto` | Gap | `src/Meridian.Contracts/Workstation/DailyValuationScheduleDtos.cs:22` |
| `DataUploadPreviewResultDto` | Gap | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:53` |
| `DataUploadTemplateCatalogDto` | Gap | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:12` |
| `DataUploadTemplateDto` | Gap | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:24` |
| `DataUploadTemplateFieldDto` | Gap | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:43` |
| `DataUploadValidationIssueDto` | Gap | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:134` |
| `DataUploadWorkbookPreviewResultDto` | Gap | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:111` |
| `DataUploadWorkbookSheetPreviewDto` | Gap | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:94` |
| `DeltaOutlierResult` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:15` |
| `DesktopLaunchTicketRedemptionDto` | Gap | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:94` |
| `DiscardSecurityMasterRevisionRequest` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:91` |
| `EquityCurvePoint` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:869` |
| `EquityCurveSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:878` |
| `EvidenceArtifactCaptureDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:40` |
| `EvidenceArtifactExtractionFieldDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:49` |
| `EvidenceArtifactRefDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:29` |
| `EvidenceArtifactRefDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:278` |
| `EvidenceAssuranceComponentDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:343` |
| `EvidenceCompletenessDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:635` |
| `EvidenceCompletenessSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:151` |
| `EvidenceDocumentAuditEventDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:152` |
| `EvidenceDocumentAuthorityDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:159` |
| `EvidenceDocumentClassificationDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:62` |
| `EvidenceDocumentConfirmedFieldDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:144` |
| `EvidenceDocumentDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:192` |
| `EvidenceDocumentExtractionRequestDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:245` |
| `EvidenceDocumentExtractionResultDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:253` |
| `EvidenceDocumentIntakeChannelDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:80` |
| `EvidenceDocumentIntakeSourceDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:271` |
| `EvidenceDocumentIntakeSourceKindDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:261` |
| `EvidenceDocumentLinkDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:128` |
| `EvidenceDocumentLinkKindDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:104` |
| `EvidenceDocumentReviewStateDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:135` |
| `EvidenceDocumentReviewStatusDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:121` |
| `EvidenceDocumentSourceRecordDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:170` |
| `EvidenceEdgeDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:300` |
| `EvidenceEndpointErrorDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:627` |
| `EvidenceExtractionStatusDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:93` |
| `EvidenceFreshnessDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:24` |
| `EvidenceGraphDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:669` |
| `EvidenceLifecycleMetadataDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:597` |
| `EvidenceManifestDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:232` |
| `EvidenceManifestPackageKindDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:222` |
| `EvidenceNodeDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:284` |
| `EvidenceNodeDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:295` |
| `EvidencePacketDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:657` |
| `EvidencePacketExportRequest` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:691` |
| `EvidencePacketExportResponse` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:702` |
| `EvidenceProofChainDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:384` |
| `EvidenceProofChainLayerDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:371` |
| `EvidenceProofChainLayerKindDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:358` |
| `EvidenceRequestDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:182` |
| `EvidenceRequestListDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:462` |
| `EvidenceRequestListKindDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:452` |
| `EvidenceSlaAssessmentDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:332` |
| `EvidenceSlaPolicyDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:323` |
| `EvidenceStatusDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:6` |
| `EvidenceSubjectDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:15` |
| `EvidenceSubjectLinkageDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:603` |
| `EvidenceSupportRequestDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:438` |
| `EvidenceTemplateDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:684` |
| `EvidenceTemplateExportSettingsDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:679` |
| `EvidenceValidationIssueDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:314` |
| `EvidenceValidationSeverityDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:308` |
| `EvidenceVaultArtifactDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:421` |
| `EvidenceVaultDocumentEntryDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:528` |
| `EvidenceVaultDocumentQueryDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:515` |
| `EvidenceVaultDocumentReviewRequestDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:539` |
| `EvidenceVaultDocumentReviewResponseDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:549` |
| `EvidenceVaultIdentityDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:401` |
| `EvidenceVaultIntakeRequestDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:553` |
| `EvidenceVaultIntakeResponseDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:580` |
| `EvidenceVaultLookupRequestDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:613` |
| `EvidenceVaultRequestListEntryDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:492` |
| `EvidenceVaultRequestListQueryDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:478` |
| `ExpectedAccountingEventDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:283` |
| `ExpectedAccountingEventKindDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:228` |
| `ExpectedJournalPreviewDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:318` |
| `ExpectedJournalPreviewLineDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:308` |
| `ExposureSnapshotDto` | Gap | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:2` |
| `ExposureTrendPointDto` | Gap | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:43` |
| `FeatureCapabilitySettingsResponse` | Gap | `src/Meridian.Contracts/Workstation/FeatureCapabilityDtos.cs:2` |
| `FeatureCapabilityToggleDto` | Gap | `src/Meridian.Contracts/Workstation/FeatureCapabilityDtos.cs:5` |
| `FeatureCapabilityToggleRequest` | Gap | `src/Meridian.Contracts/Workstation/FeatureCapabilityDtos.cs:16` |
| `FinancialOperationsCloseSupportDecisionDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialOperationsCommandCenterDtos.cs:21` |
| `FinancialOperationsCloseSupportDecisionRowDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialOperationsCommandCenterDtos.cs:34` |
| `FinancialOperationsCommandCenterDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialOperationsCommandCenterDtos.cs:2` |
| `FinancialOperationsCommandCenterMetricDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialOperationsCommandCenterDtos.cs:49` |
| `FinancialOperationsQueueRowDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialOperationsCommandCenterDtos.cs:57` |
| `FinancialRecordExplorerCellDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:71` |
| `FinancialRecordExplorerColumnDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:64` |
| `FinancialRecordExplorerDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:14` |
| `FinancialRecordExplorerFilterDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:57` |
| `FinancialRecordExplorerGraphEdgeDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:121` |
| `FinancialRecordExplorerGraphNodeDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:114` |
| `FinancialRecordExplorerProofActionDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:101` |
| `FinancialRecordExplorerQueryDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:31` |
| `FinancialRecordExplorerRecordGraphDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:110` |
| `FinancialRecordExplorerRelationshipDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:127` |
| `FinancialRecordExplorerRowDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:78` |
| `FinancialRecordExplorerSavedViewDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:41` |
| `FinancialRecordExplorerSavedViewSaveRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:134` |
| `FinancialRecordExplorerScopeItemDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:36` |
| `FinancialRecordExplorerSelectedRecordDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:88` |
| `FinancialRecordExplorerSummaryItemDto` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:51` |
| `FinancialRecordExplorerTone` | Gap | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:6` |
| `FirstRunStatusDto` | Gap | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:2` |
| `FundAccountBrokerageBalanceSnapshotDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:199` |
| `FundAccountBrokerageCashTransactionDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:245` |
| `FundAccountBrokerageCorporateActionDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:254` |
| `FundAccountBrokerageFillDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:233` |
| `FundAccountBrokerageOrderDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:219` |
| `FundAccountBrokeragePositionDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:206` |
| `FundAccountBrokerageSyncActivityDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:266` |
| `FundAccountCloseReadinessActionDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:563` |
| `FundAccountCloseReadinessBlockerDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:555` |
| `FundAccountCloseReadinessComponentDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:541` |
| `FundAccountCloseReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:569` |
| `FundAccountCloseReadinessStatusDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:535` |
| `FundAccountSummary` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:73` |
| `FundAuditEntry` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:301` |
| `FundAuditEvidenceCategoryKeyDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:61` |
| `FundAuditEvidenceCategorySummaryDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:496` |
| `FundAuditPackReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:505` |
| `FundJournalLine` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:50` |
| `FundLedgerDimensionSnapshot` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:126` |
| `FundLedgerQuery` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:27` |
| `FundLedgerReconciliationSnapshot` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:135` |
| `FundLedgerScope` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:8` |
| `FundLedgerSliceDto` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:75` |
| `FundLedgerSnapshotBalanceLine` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:114` |
| `FundLedgerSummary` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:90` |
| `FundLedgerTotalsDto` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:63` |
| `FundNavAssetClassExposureDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:107` |
| `FundNavAttributionSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:114` |
| `FundOperationsNavigationContext` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:26` |
| `FundOperationsTab` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:8` |
| `FundOperationsWorkspaceDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:460` |
| `FundOperationsWorkspaceQuery` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:96` |
| `FundPortfolioPosition` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:118` |
| `FundReconciliationItem` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:165` |
| `FundReportAssetClassSectionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:493` |
| `FundReportPackArtifactDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:551` |
| `FundReportPackEvidenceBundleApprovalDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:795` |
| `FundReportPackEvidenceBundleDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:803` |
| `FundReportPackEvidenceBundleSourceLinkDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:786` |
| `FundReportPackGenerateRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:535` |
| `FundReportPackHistoryItemDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:763` |
| `FundReportPackLifecycleEventDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:721` |
| `FundReportPackLineagePointerDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:575` |
| `FundReportPackPreviewDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:518` |
| `FundReportPackPreviewRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:482` |
| `FundReportPackProvenanceDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:562` |
| `FundReportPackSnapshotDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:732` |
| `FundReportPackValidationIssueDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:704` |
| `FundReportingProfileDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:126` |
| `FundReportingSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:431` |
| `FundTrialBalanceLine` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:37` |
| `FundWorkflowCommandMetadata` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:9` |
| `FundWorkflowOverallStatus` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:4` |
| `FundWorkflowRejectionReasonCode` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:8` |
| `FundWorkflowStage` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:6` |
| `FundWorkflowState` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:33` |
| `FundWorkflowSubStatus` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:7` |
| `FundWorkspaceSummary` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:42` |
| `FxConversionReference` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:32` |
| `GovernanceLifecycleProjectionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:201` |
| `GovernanceReportArtifactFormatDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:25` |
| `GovernanceReportKindDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:8` |
| `GovernanceReportPackStatusDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:35` |
| `GovernanceReportValidationSeverityDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:53` |
| `HaircutRuleDto` | Gap | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:24` |
| `ImportBrokerData` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:19` |
| `IngestionCheckpointDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:44` |
| `IngestionOperationActionDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:61` |
| `IngestionOperationActionRequestDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:67` |
| `IngestionOperationActionResultDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:71` |
| `IngestionOperationDetailDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:38` |
| `IngestionOperationRowDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:20` |
| `IngestionOperationsSnapshotDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:4` |
| `IngestionOperationsSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:10` |
| `IngestionSymbolProgressDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:51` |
| `InsightFeed` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:19` |
| `InsightWidget` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:6` |
| `InstrumentPassportClassificationProfileDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:523` |
| `InstrumentPassportDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:547` |
| `InstrumentPassportOperationsHandoffDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:411` |
| `InstrumentPassportOperationsReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:450` |
| `InstrumentPassportOperationsWorkbenchDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:426` |
| `InstrumentPassportOperationsWorkbenchItemDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:440` |
| `InstrumentPassportOperationsWorkbenchPanelDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:433` |
| `InstrumentPassportPricingDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:567` |
| `InstrumentPassportProviderConfidenceDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:382` |
| `InstrumentPassportReferenceDataWorkbenchDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:397` |
| `InstrumentPassportReferenceDataWorkbenchSectionDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:403` |
| `InvestmentAccountingPreviewModeDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:24` |
| `InvestmentAccountingReconciliationExpectationDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:54` |
| `InvestmentAccountingTradeSideDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:17` |
| `InvestmentAccountingTransactionKindDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:6` |
| `InvestmentAccountingTransactionLabPreviewDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:71` |
| `InvestmentAccountingTransactionLabRequestDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:29` |
| `InvestmentAccountingTrialBalanceImpactDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:47` |
| `LedgerAmountApprovalStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:661` |
| `LedgerAmountProvenanceDetailDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:685` |
| `LedgerAmountProvenanceEvidenceDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:588` |
| `LedgerAmountReconciliationCaseDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:623` |
| `LedgerAmountReconciliationStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:654` |
| `LedgerAmountReportUsageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:667` |
| `LedgerAmountSecurityMasterLinkDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:612` |
| `LedgerAmountStrategyRunLinkDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:677` |
| `LedgerImpactPreviewDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:290` |
| `LedgerJournalLine` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:632` |
| `LedgerSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:583` |
| `LedgerTrialBalanceLine` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:611` |
| `MarginCertificationRequestDto` | Gap | `src/Meridian.Contracts/Workstation/MarginControlCenterDtos.cs:76` |
| `MarginCertificationResultDto` | Gap | `src/Meridian.Contracts/Workstation/MarginControlCenterDtos.cs:83` |
| `MarginControlAccountDto` | Gap | `src/Meridian.Contracts/Workstation/MarginControlCenterDtos.cs:4` |
| `MarginControlAlertDto` | Gap | `src/Meridian.Contracts/Workstation/MarginControlCenterDtos.cs:56` |
| `MarginControlCenterDto` | Gap | `src/Meridian.Contracts/Workstation/MarginControlCenterDtos.cs:64` |
| `MarginControlPrimeSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/MarginControlCenterDtos.cs:48` |
| `MarginPositionContributionDto` | Gap | `src/Meridian.Contracts/Workstation/MarginControlCenterDtos.cs:35` |
| `MarginRequirementDto` | Gap | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:22` |
| `MeridianAssuranceScoreDto` | Gap | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:350` |
| `MetricsDiff` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:846` |
| `MultiAssetClassCoverageDto` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1004` |
| `MultiAssetCoverageSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1043` |
| `MultiAssetDrillThroughTargetDto` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:991` |
| `MultiAssetEvidenceRequirementDto` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:970` |
| `MultiAssetPackCoverageDto` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1019` |
| `MultiAssetReadinessBlockerDto` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:981` |
| `NormalizeBrokerTransactions` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:20` |
| `NullReportingRunNotifier` | Gap | `src/Meridian.Contracts/Workstation/IReportingRunNotifier.cs:21` |
| `OpenLotSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1111` |
| `OperationsAccountingRecordEvidenceCategoryDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1154` |
| `OperationsAccountingRecordSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1144` |
| `OperationsActionOriginDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:467` |
| `OperationsApprovalDecisionRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:681` |
| `OperationsApprovalDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1301` |
| `OperationsApprovalPolicyMatrixDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:740` |
| `OperationsApprovalPolicyMatrixRowDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:746` |
| `OperationsApprovalPolicyRuleAuditEventDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:790` |
| `OperationsApprovalPolicyRuleUpsertRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:764` |
| `OperationsApprovalPolicyRuleUpsertResultDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:785` |
| `OperationsApprovalStateDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:102` |
| `OperationsAssignBreakCaseRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:658` |
| `OperationsBreakCaseDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1257` |
| `OperationsBrokerIntakeStateDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:52` |
| `OperationsChecklistAcknowledgeRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1198` |
| `OperationsChecklistControlApprovalDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:735` |
| `OperationsCloseCalendarDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:805` |
| `OperationsCloseCalendarItemAuditEventDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:845` |
| `OperationsCloseCalendarItemDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:809` |
| `OperationsCloseCalendarItemUpsertRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:831` |
| `OperationsCloseCalendarItemUpsertResultDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:840` |
| `OperationsCloseChecklistTaskDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1163` |
| `OperationsClosePackagePublicationDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1180` |
| `OperationsCloseReadinessBlockerDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1344` |
| `OperationsCloseReadinessComponentDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1333` |
| `OperationsCloseReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1324` |
| `OperationsCloseWorkflowRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:702` |
| `OperationsContinuityCorrelationKeysDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1293` |
| `OperationsContinuityWorkflowDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1048` |
| `OperationsContinuityWorkflowSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:998` |
| `OperationsDashboardMetricDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1102` |
| `OperationsDashboardSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1086` |
| `OperationsEvidenceLinkDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1369` |
| `OperationsEvidencePackageSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1116` |
| `OperationsGateDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1204` |
| `OperationsGateKeyDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:42` |
| `OperationsGatePostureRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:504` |
| `OperationsGateStatusDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:29` |
| `OperationsIssueCodeDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:445` |
| `OperationsJournalEntryMetadataDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:613` |
| `OperationsLedgerDraftRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:536` |
| `OperationsLedgerJournalCandidateDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:574` |
| `OperationsLedgerJournalLineDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:597` |
| `OperationsLedgerPostRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:560` |
| `OperationsLedgerPostingStateDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:72` |
| `OperationsLedgerPreviewDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1311` |
| `OperationsLedgerValidationRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:550` |
| `OperationsNextActionDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1360` |
| `OperationsReconciliationLaneStatusDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:93` |
| `OperationsReconciliationLaneSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1133` |
| `OperationsReconciliationRunRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:628` |
| `OperationsReconciliationStateDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:82` |
| `OperationsRejectWorkflowRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:692` |
| `OperationsReopenWorkflowRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:722` |
| `OperationsReportPackReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1318` |
| `OperationsResolveBreakCaseRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:646` |
| `OperationsReviewedAutomationArtifactDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1031` |
| `OperationsReviewedAutomationSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1012` |
| `OperationsSecurityMasterOverrideApprovalRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:493` |
| `OperationsSecurityMasterResolveRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:524` |
| `OperationsSecurityMasterStateDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:62` |
| `OperationsStartWorkflowRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:474` |
| `OperationsSubmitApprovalRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:670` |
| `OperationsTimelineEntryDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1215` |
| `OperationsTransitionRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:485` |
| `OperationsTransitionResultDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:859` |
| `OperationsWorkflowAuditDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1236` |
| `OperationsWorkflowBlockerDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1352` |
| `OperationsWorkflowStatusDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:12` |
| `OperatorInboxDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:94` |
| `OperatorWorkItemDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:59` |
| `OperatorWorkItemKindDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:6` |
| `OperatorWorkItemToneDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:21` |
| `OperatorWorkflowHomeSummary` | Gap | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:6` |
| `ParameterDiff` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:840` |
| `PaymentInstruction` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:17` |
| `PilotAcceptanceEvidenceCategoryDto` | Gap | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:51` |
| `PilotAcceptanceEvidenceDto` | Gap | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:67` |
| `PilotAcceptanceEvidenceRoleDto` | Gap | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:62` |
| `PilotEvidenceGraphEdgeDto` | Gap | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:85` |
| `PilotReadinessArtifactDto` | Gap | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:90` |
| `PilotReadinessStageDto` | Gap | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:6` |
| `PilotReadinessStageGateDto` | Gap | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:25` |
| `PilotReadinessStageStatusDto` | Gap | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:19` |
| `PilotW4AcceptanceEvaluationDto` | Gap | `src/Meridian.Contracts/Workstation/PilotReadinessArtifactDtos.cs:74` |
| `PortfolioLedgerDriftDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:48` |
| `PortfolioLedgerWorkflowStatusDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:40` |
| `PortfolioLedgerWorkflowStatusSnapshotDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:53` |
| `PortfolioPositionSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:563` |
| `PortfolioReportingAnalyticsKindDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:273` |
| `PortfolioReportingAnalyticsRowDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:291` |
| `PortfolioReportingAnalyticsScopeDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:281` |
| `PortfolioReportingCutDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:171` |
| `PortfolioReportingCutKindDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:152` |
| `PortfolioReportingLiveViewDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:210` |
| `PortfolioReportingLiveViewFreshnessPolicyDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:197` |
| `PortfolioReportingLiveViewStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:160` |
| `PortfolioReportingPnlSliceDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:253` |
| `PortfolioReportingPnlSlicePeriodDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:242` |
| `PortfolioSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:527` |
| `PositionDiffEntry` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:831` |
| `PostLedgerEntries` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:25` |
| `PrivateCapitalCloseCockpitApprovalDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1405` |
| `PrivateCapitalCloseCockpitDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1467` |
| `PrivateCapitalCloseCockpitLaneDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1390` |
| `PrivateCapitalCloseCockpitWorkflowDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1376` |
| `PrivateCapitalNavSupportComponentDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1420` |
| `PrivateCapitalNavSupportPackageDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1448` |
| `PrivateCapitalShadowNavTieOutDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1429` |
| `ProductExposureDto` | Gap | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:20` |
| `ProviderCorporateActionEvidenceCandidateDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:431` |
| `ProviderCorporateActionLedgerEffectDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:449` |
| `ProviderCorporateActionReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:490` |
| `ProviderCorporateActionReadinessLineDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:422` |
| `ProviderLedgerReconciliationBreakDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:335` |
| `ProviderLedgerReconciliationBreakSignOffStateDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:298` |
| `ProviderLedgerReconciliationCheckDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:322` |
| `ProviderLedgerReconciliationCheckStatusDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:290` |
| `ProviderLedgerReconciliationDetailDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:522` |
| `ProviderLedgerReconciliationRequestDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:313` |
| `ProviderLedgerReconciliationStatusDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:282` |
| `ProviderLedgerReconciliationSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:359` |
| `ProviderPromotionChecklistDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:334` |
| `ProviderSecurityMasterPassportDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:379` |
| `ProviderSecurityMasterPassportStatusDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:306` |
| `ProviderSecurityMasterScheduleFeedDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:466` |
| `ProviderShadowBookComparisonDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:412` |
| `ProviderShadowBookComparisonLineDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:401` |
| `PublishSecurityMasterRevisionRequest` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:116` |
| `RecommendedActionDto` | Gap | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:84` |
| `ReconciliationBreakCategory` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:54` |
| `ReconciliationBreakDispositionDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:458` |
| `ReconciliationBreakDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:141` |
| `ReconciliationBreakExplanationDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:608` |
| `ReconciliationBreakMeasureDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:448` |
| `ReconciliationBreakMeasureKindDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:437` |
| `ReconciliationBreakQueueItem` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:479` |
| `ReconciliationBreakQueueProjectionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:247` |
| `ReconciliationBreakQueueProjectionItemDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:262` |
| `ReconciliationBreakQueueScope` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:587` |
| `ReconciliationBreakQueueStatus` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:364` |
| `ReconciliationBreakScore` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:916` |
| `ReconciliationBreakSeverity` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:41` |
| `ReconciliationBreakStatus` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:28` |
| `ReconciliationBulkCaseworkCaseResult` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:749` |
| `ReconciliationBulkCaseworkRequest` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:725` |
| `ReconciliationBulkCaseworkResult` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:756` |
| `ReconciliationCalibrationProfileSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:963` |
| `ReconciliationCalibrationStatusDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:469` |
| `ReconciliationCalibrationSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:980` |
| `ReconciliationCaseComment` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:616` |
| `ReconciliationCaseCommentVisibility` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:410` |
| `ReconciliationCaseLifecycleState` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:374` |
| `ReconciliationCasePriority` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:390` |
| `ReconciliationCaseSignoffRecord` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:907` |
| `ReconciliationCaseSlaState` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:399` |
| `ReconciliationCaseStateTransition` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:929` |
| `ReconciliationCaseTransitionAction` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:940` |
| `ReconciliationCaseTransitionCommand` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:950` |
| `ReconciliationCaseworkAction` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:418` |
| `ReconciliationCaseworkCloseScopeDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:720` |
| `ReconciliationCaseworkCommand` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:679` |
| `ReconciliationCaseworkOperationResult` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:900` |
| `ReconciliationCorrelationContext` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1041` |
| `ReconciliationJobControl` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1064` |
| `ReconciliationMatchDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:117` |
| `ReconciliationPayloadEnvelope` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1051` |
| `ReconciliationProcessingTelemetry` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1079` |
| `ReconciliationRolloutFlags` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1090` |
| `ReconciliationRunDetail` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:345` |
| `ReconciliationRunRequest` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:72` |
| `ReconciliationRunSummary` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:86` |
| `ReconciliationSchemaVersion` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1031` |
| `ReconciliationSecurityCoverageIssueDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:215` |
| `ReconciliationSlaComputationResult` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:657` |
| `ReconciliationSlaPolicy` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:636` |
| `ReconciliationSourceKind` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:14` |
| `ReconciliationSummary` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:187` |
| `ReconciliationTaxonomySnapshot` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:674` |
| `ReconciliationTaxonomyValue` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:666` |
| `RejectWorkflow` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:30` |
| `RenderReportTemplateRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1627` |
| `RenderReportTemplateResponseDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1632` |
| `ReopenWorkflow` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:32` |
| `ReportAccessEvaluationDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1614` |
| `ReportAccessModeDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1588` |
| `ReportAccessPolicyDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1607` |
| `ReportAccessPrincipalDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1602` |
| `ReportAccessPrincipalKindDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1596` |
| `ReportBrandingThemeDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:138` |
| `ReportPackAuditEventDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1695` |
| `ReportPackChangedLineDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1698` |
| `ReportPackCreateRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1748` |
| `ReportPackDeliveryAccessLinkDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:857` |
| `ReportPackDeliveryApprovalStepDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:885` |
| `ReportPackDeliveryArtifactDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:846` |
| `ReportPackDeliveryAttemptDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:968` |
| `ReportPackDeliveryEvidencePacketDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:893` |
| `ReportPackDeliveryFailureRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:995` |
| `ReportPackDeliveryHistoryDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1004` |
| `ReportPackDeliveryModeDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:839` |
| `ReportPackDeliveryNotificationDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:865` |
| `ReportPackDeliveryPackageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:917` |
| `ReportPackDeliveryRecipientDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:879` |
| `ReportPackDeliveryRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:985` |
| `ReportPackDeliveryStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:829` |
| `ReportPackEvidenceLinkDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1697` |
| `ReportPackLineProvenanceDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1699` |
| `ReportPackPublicationManifestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1717` |
| `ReportPackPublishRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1730` |
| `ReportPackRejectRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1743` |
| `ReportPackRejectionMetadataDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1763` |
| `ReportPackRestateRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1769` |
| `ReportPackRestatementMetadataDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1756` |
| `ReportPackWorkflowActionRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1740` |
| `ReportPackWorkflowRecordDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1775` |
| `ReportPackWorkflowStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1354` |
| `ReportTemplateAuditEventDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1648` |
| `ReportTemplateDecisionRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1691` |
| `ReportTemplateDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1619` |
| `ReportTemplateDraftRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1680` |
| `ReportTemplateGovernanceRecordDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1656` |
| `ReportTemplateLifecycleStatusDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1640` |
| `ReportTemplateParameterDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1369` |
| `ReportWriterAggregateFunctionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1381` |
| `ReportWriterCellStyleDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1439` |
| `ReportWriterChartDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1464` |
| `ReportWriterChartRenderDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1475` |
| `ReportWriterChartSeriesDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1470` |
| `ReportWriterChartTypeDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1456` |
| `ReportWriterDiffDirectionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1555` |
| `ReportWriterDiffRowStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1546` |
| `ReportWriterFilterDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1416` |
| `ReportWriterFilterLineageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1503` |
| `ReportWriterFilterOperatorDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1402` |
| `ReportWriterFormatRuleDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1447` |
| `ReportWriterFormulaDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1395` |
| `ReportWriterFormulaLineageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1498` |
| `ReportWriterGridColumnDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1483` |
| `ReportWriterGridDataDictionaryFieldDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1509` |
| `ReportWriterGridDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1422` |
| `ReportWriterGridDiffCellDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1561` |
| `ReportWriterGridDiffDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1575` |
| `ReportWriterGridDiffRowDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1568` |
| `ReportWriterGridKindDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1372` |
| `ReportWriterGridLineageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1523` |
| `ReportWriterGridRenderDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1532` |
| `ReportWriterGridRowDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1488` |
| `ReportWriterGridValidationCheckDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1518` |
| `ReportWriterMetricDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1389` |
| `ReportWriterMetricLineageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1493` |
| `ReportingAccountingBasisDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1209` |
| `ReportingConsolidationLevelDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1219` |
| `ReportingDeploymentCapabilityDto` | Documented | `src/Meridian.Contracts/Workstation/ReportingDeploymentDtos.cs:16` |
| `ReportingDeploymentComponentDto` | Gap | `src/Meridian.Contracts/Workstation/ReportingDeploymentDtos.cs:6` |
| `ReportingDueScheduleRunResultDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1194` |
| `ReportingEntityScopeKindDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1200` |
| `ReportingFinalityDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1238` |
| `ReportingLedgerBookSelectionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1260` |
| `ReportingOutputFormatDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1228` |
| `ReportingRunAuditEntryDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1324` |
| `ReportingRunAuditTrailDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1331` |
| `ReportingRunParametersDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1264` |
| `ReportingRunReadinessCheckDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1282` |
| `ReportingRunReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1296` |
| `ReportingRunReadinessStatusDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1245` |
| `ReportingRunRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1308` |
| `ReportingRunResultDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1321` |
| `ReportingRunScopeDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1252` |
| `ReportingScheduleDeliveryPlanDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1054` |
| `ReportingScheduleDeliveryTargetDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1008` |
| `ReportingScheduleRecordDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1140` |
| `ReportingScheduleRunResultDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1188` |
| `ReportingScheduleStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1097` |
| `ReportingScheduleUpsertRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1170` |
| `ReportingScheduledReleaseHandoffDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1029` |
| `ReportingScheduledReleaseHandoffStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1018` |
| `ReportingStarterKitDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1114` |
| `ReportingStarterKitProvisionResultDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1135` |
| `ReportingStarterKitStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1124` |
| `ReportingStarterSeedScheduleDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1104` |
| `ResearchBriefingAlert` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:95` |
| `ResearchBriefingDto` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:132` |
| `ResearchBriefingRun` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:53` |
| `ResearchBriefingWorkspaceSummary` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:118` |
| `ResearchRunDrillInLinks` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:42` |
| `ResearchSavedComparison` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:84` |
| `ResearchSavedComparisonMode` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:73` |
| `ResearchWhatChangedItem` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:106` |
| `ResolveBreakCase` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:27` |
| `ResolveReconciliationBreakRequest` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1020` |
| `ResolveSecurityMasterMappings` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:21` |
| `ResolveSourceConflictRequest` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:58` |
| `RestatementCandidateDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:157` |
| `ReviewReconciliationBreakRequest` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1010` |
| `RunAttributionSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:919` |
| `RunCashFlowSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:966` |
| `RunCashLadder` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:952` |
| `RunComparisonDto` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:754` |
| `RunComparisonRequest` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:798` |
| `RunDiffRequest` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:803` |
| `RunFillEntry` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:890` |
| `RunFillSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:901` |
| `RunLotSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1139` |
| `RunPortfolioDrillInSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:981` |
| `RunReconciliation` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:26` |
| `SampleArtifactDto` | Gap | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:55` |
| `SampleBreakDto` | Gap | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:46` |
| `SampleHighlightDto` | Gap | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:59` |
| `SampleHoldingDto` | Gap | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:38` |
| `SampleMarketHistoryDto` | Gap | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:57` |
| `SampleWorkspaceDto` | Gap | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:23` |
| `SecurityClassificationSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:8` |
| `SecurityEconomicDefinitionSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:27` |
| `SecurityIdentityDrillInDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:56` |
| `SecurityMasterAccountingIssueDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:331` |
| `SecurityMasterChangeHistoryItemDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:194` |
| `SecurityMasterConflictAssessmentDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:145` |
| `SecurityMasterConflictAuthorityDecision` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:168` |
| `SecurityMasterConflictRecommendationKind` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:41` |
| `SecurityMasterConflictResolutionDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:132` |
| `SecurityMasterDownstreamImpactDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:345` |
| `SecurityMasterEconomicDefinitionDrillInDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:102` |
| `SecurityMasterEditOrigin` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:10` |
| `SecurityMasterEditResultDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:124` |
| `SecurityMasterEntitlementApplicabilityDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:483` |
| `SecurityMasterFactorPointDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:255` |
| `SecurityMasterIdentifierSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:160` |
| `SecurityMasterImpactLinkDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:361` |
| `SecurityMasterImpactSeverity` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:16` |
| `SecurityMasterLotModelDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:280` |
| `SecurityMasterManualChangeApprovalPostureDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:510` |
| `SecurityMasterOpenLotDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:303` |
| `SecurityMasterOpenLotProvenanceDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:334` |
| `SecurityMasterOpenLotReadModelDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:289` |
| `SecurityMasterOperatingModelDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:461` |
| `SecurityMasterOperatingModelStageDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:475` |
| `SecurityMasterOperatorMetadataDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:499` |
| `SecurityMasterProviderSymbolMappingDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:172` |
| `SecurityMasterPublishResultDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:145` |
| `SecurityMasterRecommendedActionDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:368` |
| `SecurityMasterRecommendedActionKind` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:26` |
| `SecurityMasterRevisionPublishedEvent` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:178` |
| `SecurityMasterRevisionStateDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:21` |
| `SecurityMasterScheduleBookDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:219` |
| `SecurityMasterScheduleEventDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:233` |
| `SecurityMasterScheduleProvenanceDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:267` |
| `SecurityMasterScheduleSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:209` |
| `SecurityMasterSchemaCompatibilityDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:185` |
| `SecurityMasterSourceCandidateDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:133` |
| `SecurityMasterTrustPostureDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:119` |
| `SecurityMasterTrustSnapshotDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:48` |
| `SecurityMasterTrustTone` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:7` |
| `SecurityMasterWorkstationDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:43` |
| `SettlementInstruction` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:19` |
| `StartWorkflow` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:18` |
| `StarterWorkspaceDto` | Gap | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:69` |
| `StatementAccountSnapshotPreviewDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:94` |
| `StatementActivityCompletenessDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:121` |
| `StatementActivitySubtypeSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:116` |
| `StatementBreakDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:175` |
| `StatementBreakType` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:53` |
| `StatementColumnConfidenceDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:50` |
| `StatementColumnMappingDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:57` |
| `StatementConnectorDescriptorDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:9` |
| `StatementFetchScheduleDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:369` |
| `StatementFetchScheduleUpsertRequestDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:386` |
| `StatementImportCommitResultDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:165` |
| `StatementImportIssueDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:64` |
| `StatementImportPreviewDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:143` |
| `StatementImportReconciliationCaseLinkDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:197` |
| `StatementKindSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:133` |
| `StatementMappingProfileActivityCodeDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:43` |
| `StatementMappingProfileCsvOptionsDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:32` |
| `StatementMappingProfileDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:19` |
| `StatementMappingProfileFieldDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:37` |
| `StatementMatchSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:157` |
| `StatementMatchTier` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:39` |
| `StatementNormalizedCashDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:125` |
| `StatementNormalizedPositionDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:106` |
| `StatementNormalizedTransactionDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:138` |
| `StatementProfileSuggestionDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:139` |
| `StatementReconciliationAccountingScopeDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:249` |
| `StatementReconciliationBreakExplanationDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:249` |
| `StatementReconciliationCaseAttachmentDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:240` |
| `StatementReconciliationCaseAuditEventDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:258` |
| `StatementReconciliationCaseCommentDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:233` |
| `StatementReconciliationCaseCommentThreadDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:228` |
| `StatementReconciliationCaseDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:205` |
| `StatementReconciliationReportArtifactDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:220` |
| `StatementReconciliationReportArtifactGenerationDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:235` |
| `StatementReconciliationReportWorkflowDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:259` |
| `StatementReconciliationReportWorkflowStatusDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:209` |
| `StatementRecordPreviewDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:73` |
| `StatementRunBreakDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:346` |
| `StatementRunCreateDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:298` |
| `StatementRunDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:269` |
| `StatementRunExceptionDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:361` |
| `StatementRunReconcileRequestDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:316` |
| `StatementRunStatus` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:9` |
| `StatementRunSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:324` |
| `StatementRunValidationDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:338` |
| `StatementSourceDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:76` |
| `StatementToReportArtifactDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:328` |
| `StatementToReportWorkflowDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:343` |
| `StatementToReportWorkflowStatusDto` | Gap | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:316` |
| `StatementValidationIssueDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:92` |
| `StatementValidationSeverity` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:27` |
| `StorageAssurancePermissionsDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:146` |
| `StorageAssuranceSnapshotDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:80` |
| `StorageCapacitySummaryDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:127` |
| `StorageHealthSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:90` |
| `StorageMaintenanceActionDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:154` |
| `StorageMaintenanceCandidateDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:165` |
| `StorageMaintenanceCommandRequestDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:186` |
| `StorageMaintenanceItemResultDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:192` |
| `StorageMaintenancePreviewDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:173` |
| `StorageMaintenancePreviewRequestDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:160` |
| `StorageMaintenanceResultDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:198` |
| `StorageQualityAlertDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:139` |
| `StorageQualitySummaryDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:101` |
| `StorageTierSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:134` |
| `StpProcessingState` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:4` |
| `StpStateTransition` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:37` |
| `StrategyBriefingAlert` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:59` |
| `StrategyBriefingDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:96` |
| `StrategyBriefingRun` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:17` |
| `StrategyBriefingWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:82` |
| `StrategyDesignCell` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:22` |
| `StrategyDesignCompiledScript` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:102` |
| `StrategyDesignDocument` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:6` |
| `StrategyDesignDraftSaveRequest` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:144` |
| `StrategyDesignDraftSaveResponse` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:149` |
| `StrategyDesignDraftSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:131` |
| `StrategyDesignFieldCatalogItem` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:47` |
| `StrategyDesignPreviewResult` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:111` |
| `StrategyDesignPreviewRow` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:90` |
| `StrategyDesignRunBacktestRequest` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:158` |
| `StrategyDesignRunBacktestResponse` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:211` |
| `StrategyDesignRunTraceEntry` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:120` |
| `StrategyDesignTemplate` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:61` |
| `StrategyDesignTransition` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:35` |
| `StrategyDesignValidationMessage` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:73` |
| `StrategyDesignValidationResult` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:82` |
| `StrategyRunAcceptanceChecklistItemDto` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:296` |
| `StrategyRunAcceptanceChecklistStatusDto` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:284` |
| `StrategyRunArtifactCompleteness` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:674` |
| `StrategyRunCashFlowDigest` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1018` |
| `StrategyRunComparison` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:652` |
| `StrategyRunContinuityDetail` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1096` |
| `StrategyRunContinuityDto` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1086` |
| `StrategyRunContinuityLineage` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1010` |
| `StrategyRunContinuityLink` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:995` |
| `StrategyRunContinuitySeamHealthStatus` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1054` |
| `StrategyRunContinuityStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1064` |
| `StrategyRunContinuityWarning` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1039` |
| `StrategyRunContinuityWarningSeverity` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1046` |
| `StrategyRunCrossModeTransitionMetadata` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:725` |
| `StrategyRunDetail` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:450` |
| `StrategyRunDiff` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:806` |
| `StrategyRunDrillInLinks` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:6` |
| `StrategyRunEngine` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:22` |
| `StrategyRunEvidenceLoop` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:310` |
| `StrategyRunExecutionSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:135` |
| `StrategyRunGovernanceHook` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:116` |
| `StrategyRunGovernanceSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:176` |
| `StrategyRunHistoryQuery` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:703` |
| `StrategyRunIdentity` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:189` |
| `StrategyRunLineageEventType` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:693` |
| `StrategyRunLineageTimelineEntry` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:736` |
| `StrategyRunLiveStatus` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:66` |
| `StrategyRunMode` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:11` |
| `StrategyRunPaperStatus` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:93` |
| `StrategyRunPromotionState` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:50` |
| `StrategyRunPromotionSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:151` |
| `StrategyRunReviewPacketDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:385` |
| `StrategyRunStatus` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:35` |
| `StrategyRunSummary` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:205` |
| `StrategyRunTimelineEntry` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:710` |
| `StrategyRunTimelineProjection` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:685` |
| `StrategySavedComparison` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:48` |
| `StrategySavedComparisonMode` | Gap | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:37` |
| `StrategySweepObjectiveRanking` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:237` |
| `StrategySweepResultGroup` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:246` |
| `StrategyWhatChangedItem` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:70` |
| `StructuredReportingExportColumnDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:386` |
| `StructuredReportingExportDataDictionaryFieldDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:391` |
| `StructuredReportingExportDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:360` |
| `StructuredReportingExportPayloadDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:408` |
| `StructuredReportingExportPurposeDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:350` |
| `StructuredReportingExportRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:421` |
| `StructuredReportingExportRowLineageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:403` |
| `StructuredReportingExportValidationCheckDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:398` |
| `SubmitForApproval` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:28` |
| `SubmitSecurityMasterRevisionRequest` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:73` |
| `SymbolAttributionEntry` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:909` |
| `SyncCompletenessResult` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:13` |
| `SyncValidationResult` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:16` |
| `ThresholdBreachDto` | Gap | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:35` |
| `TradingAcceptanceGateDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:106` |
| `TradingAcceptanceGateStatusDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:30` |
| `TradingControlEvidenceDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:205` |
| `TradingControlReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:221` |
| `TradingExecutionReconciliationBreakDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:118` |
| `TradingExecutionReconciliationReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:128` |
| `TradingLiveOperationRequirementDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:140` |
| `TradingOperatorReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:346` |
| `TradingOperatorSignoffReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:253` |
| `TradingPaperSessionReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:168` |
| `TradingPromotionReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:239` |
| `TradingReplayReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:190` |
| `TradingReportPackReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:323` |
| `TradingTrustGateContractReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:281` |
| `TradingTrustGateEvidenceDocumentDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:273` |
| `TradingTrustGateReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:291` |
| `TradingTrustGateSampleReviewDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:262` |
| `UpdateSecurityFieldRequest` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:41` |
| `ValidateLedgerDraft` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:24` |
| `VersionedReportTemplateIdDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1367` |
| `WorkflowActionDto` | Gap | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:29` |
| `WorkflowBlockerSummary` | Gap | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:42` |
| `WorkflowDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:14` |
| `WorkflowEvidenceBadge` | Gap | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:52` |
| `WorkflowLibraryDto` | Gap | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:6` |
| `WorkflowNextAction` | Gap | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:33` |
| `WorkflowPresetDto` | Gap | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:43` |
| `WorkflowPresetLibraryDto` | Gap | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:64` |
| `WorkflowPresetPinRequest` | Gap | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:84` |
| `WorkflowPresetSaveRequest` | Gap | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:71` |
| `WorkspaceModeDto` | Gap | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:61` |
| `WorkspaceWorkflowSummary` | Gap | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:20` |
| `WorkstationAccountingAgingBucketPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:819` |
| `WorkstationAccountingAlertPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:836` |
| `WorkstationAccountingCashFlowSummaryPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:787` |
| `WorkstationAccountingControlCenterPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:840` |
| `WorkstationAccountingDrillLinkPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:832` |
| `WorkstationAccountingOwnerWorkloadPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:823` |
| `WorkstationAccountingPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:902` |
| `WorkstationAccountingRunCashFlowPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:776` |
| `WorkstationAccountingRunGovernancePayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:759` |
| `WorkstationAccountingRunReconciliationPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:765` |
| `WorkstationAccountingRunRecord` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:797` |
| `WorkstationAccountingSeverityCountPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:815` |
| `WorkstationAccountingTrendSnapshotPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:827` |
| `WorkstationAccountingWorkspaceSummary` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:433` |
| `WorkstationBrokerageAccountDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:43` |
| `WorkstationBrokerageAccountLinkDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:52` |
| `WorkstationBrokerageSyncHealth` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:8` |
| `WorkstationBrokerageSyncRunRequestDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:36` |
| `WorkstationBrokerageSyncStatusDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:68` |
| `WorkstationDataBackfillRecord` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1106` |
| `WorkstationDataExportRecord` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1117` |
| `WorkstationDataPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1129` |
| `WorkstationDataProviderDiagnostic` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1062` |
| `WorkstationDataProviderRecord` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1084` |
| `WorkstationDataProviderRoutingSummary` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1072` |
| `WorkstationGeneratedReportWriterGridPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:572` |
| `WorkstationKernelAlertThresholdsPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:862` |
| `WorkstationKernelCriticalSeverityRatePayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:869` |
| `WorkstationKernelDomainPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:878` |
| `WorkstationKernelDriftPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:857` |
| `WorkstationKernelLatencyPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:852` |
| `WorkstationKernelObservabilityPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:889` |
| `WorkstationMetricCard` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:20` |
| `WorkstationModeComparisonGroup` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:119` |
| `WorkstationModeComparisonRun` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:93` |
| `WorkstationPlotToolFocusPointPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:215` |
| `WorkstationPlotToolLegendItemPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:209` |
| `WorkstationPlotToolMomentPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:258` |
| `WorkstationPlotToolPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:300` |
| `WorkstationPlotToolPointPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:199` |
| `WorkstationPlotToolRegressionPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:264` |
| `WorkstationPlotToolSampleRowPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:268` |
| `WorkstationPlotToolSignalCardPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:221` |
| `WorkstationPlotToolStatisticsPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:277` |
| `WorkstationPlotToolStudyPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:289` |
| `WorkstationPlotToolSummaryItemPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:204` |
| `WorkstationPlotToolSummaryTilePayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:251` |
| `WorkstationPlotToolTabState` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:183` |
| `WorkstationPlotToolTickPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:196` |
| `WorkstationPlotToolWorkspacePayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:228` |
| `WorkstationPortfolioPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:940` |
| `WorkstationPortfolioRunRow` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:921` |
| `WorkstationPortfolioSummaryPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:956` |
| `WorkstationPortfolioSummaryTelemetry` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:947` |
| `WorkstationReportAccessAuditSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:659` |
| `WorkstationReportPackDistributionPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:643` |
| `WorkstationReportWriterDatasetSourcePayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:480` |
| `WorkstationReportWriterFieldPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:472` |
| `WorkstationReportWriterFilterPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:466` |
| `WorkstationReportWriterFormulaPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:461` |
| `WorkstationReportWriterGridPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:501` |
| `WorkstationReportWriterMetricPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:456` |
| `WorkstationReportingDailyWorkItemDto` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:624` |
| `WorkstationReportingDeliveryPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:690` |
| `WorkstationReportingDeliveryReceiptPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:678` |
| `WorkstationReportingHistoryPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:750` |
| `WorkstationReportingPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:719` |
| `WorkstationReportingProfilePayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:444` |
| `WorkstationReportingRunLinkPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:554` |
| `WorkstationReportingRunNextActionPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:562` |
| `WorkstationReportingRunPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:585` |
| `WorkstationReportingTemplatePayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:518` |
| `WorkstationRiskGuardrail` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:373` |
| `WorkstationRunDigest` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:106` |
| `WorkstationRunDrillInLinks` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:29` |
| `WorkstationSecurityCoverageGapPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:54` |
| `WorkstationSecurityCoveragePayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:60` |
| `WorkstationSecurityCoverageReferencePayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:37` |
| `WorkstationSecurityCoverageStatus` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:486` |
| `WorkstationSecurityReference` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:503` |
| `WorkstationSessionPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:156` |
| `WorkstationSessionWorkspaceSummary` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:144` |
| `WorkstationStrategyPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:311` |
| `WorkstationStrategyRunCard` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:71` |
| `WorkstationStrategyWorkspaceSummary` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:172` |
| `WorkstationTimelineCard` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:127` |
| `WorkstationTradingBrokerageState` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:400` |
| `WorkstationTradingFillRow` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:353` |
| `WorkstationTradingOrderRow` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:340` |
| `WorkstationTradingPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:415` |
| `WorkstationTradingPositionRow` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:326` |
| `WorkstationTradingRiskState` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:386` |
| `WorkstationWatchlist` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:29` |
| `WorkstationWorkspaceDefinition` | Gap | `src/Meridian.Contracts/Workstation/WorkstationWorkspaceCatalog.cs:6` |

## Follow-up Queue

- Document or intentionally suppress 416 mapped endpoint gap(s).
- Document or intentionally suppress 881 workstation contract gap(s).

---

*This dashboard is auto-generated. Do not edit manually.*
