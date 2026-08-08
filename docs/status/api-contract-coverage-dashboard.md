# API Contract Coverage Dashboard

_Auto-generated from canonical JSON payload._
_Generated: 1970-01-01T00:00:00+00:00_
Data sources: `src/**/*.cs endpoint mappings`, `src/Meridian.Contracts/Api/UiApiRoutes.cs`, `src/Meridian.Contracts/Workstation/*.cs`, `docs/**/*.md (excluding generated report roots)`


Tracks whether mapped API routes and workstation DTO contracts are visible in the Markdown documentation set.

## Summary

| Metric | Value |
|---|---:|
| Weighted score | 29.1% |
| Endpoint coverage | 40.0% |
| Workstation contract coverage | 12.7% |
| Endpoints documented | 248 / 620 |
| Workstation contracts documented | 116 / 916 |

## Endpoint Coverage

| Method | Route | Status | Source |
|---|---|---|---|
| `GET` | `/api/accounting-system/export-packages` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:527` |
| `POST` | `/api/accounting-system/export-packages` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:560` |
| `POST` | `/api/accounting-system/export-packages/certification` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:646` |
| `GET` | `/api/accounting-system/export-packages/{exportPackageId}/manifest` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:608` |
| `GET` | `/api/accounting-system/import/latest` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:392` |
| `POST` | `/api/accounting-system/import/preview` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:367` |
| `GET` | `/api/accounting-system/mapping-profiles` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:450` |
| `POST` | `/api/accounting-system/mapping-profiles` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:479` |
| `GET` | `/api/accounting-system/migration-run-artifacts` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:230` |
| `POST` | `/api/accounting-system/migration-run-artifacts` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:258` |
| `POST` | `/api/accounting-system/migration-runs` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:193` |
| `GET` | `/api/accounting-system/migration-worker-plans` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:299` |
| `POST` | `/api/accounting-system/migration-worker-plans` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:329` |
| `GET` | `/api/accounting-system/production-certification-profile` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:123` |
| `POST` | `/api/accounting-system/production-certification-profile` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:150` |
| `POST` | `/api/accounting-system/production-readiness` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:35` |
| `GET` | `/api/accounting-system/providers` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:20` |
| `GET` | `/api/accounting-system/reconciliation/latest` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:421` |
| `GET` | `/api/accounting-system/tenant-administration-profile` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:58` |
| `POST` | `/api/accounting-system/tenant-administration-profile` | Gap | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:82` |
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
| `GET` | `/api/auth/access-assignments` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:479` |
| `POST` | `/api/auth/access-assignments` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:520` |
| `POST` | `/api/auth/access-assignments/{assignmentId}/revoke` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:560` |
| `GET` | `/api/auth/accounts` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:191` |
| `PUT` | `/api/auth/accounts/{username}` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:214` |
| `POST` | `/api/auth/accounts/{username}/disable` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:312` |
| `POST` | `/api/auth/accounts/{username}/password-reset` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:252` |
| `GET` | `/api/auth/audit` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:403` |
| `POST` | `/api/auth/bootstrap` | Gap | `src/Meridian.Ui.Shared/Endpoints/InitialAccountBootstrapEndpoints.cs:18` |
| `GET` | `/api/auth/desktop-launch/{ticket}` | Gap | `src/Meridian.Ui.Shared/Endpoints/FirstRunEndpoints.cs:52` |
| `POST` | `/api/auth/login` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:35` |
| `POST` | `/api/auth/logout` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:144` |
| `GET` | `/api/auth/me` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:164` |
| `POST` | `/api/auth/role-profiles` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:427` |
| `GET` | `/api/auth/roles` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:185` |
| `POST` | `/api/auth/sessions/revoke` | Gap | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:372` |
| `GET` | `/api/backfill/checkpoints` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:25` |
| `GET` | `/api/backfill/checkpoints/resumable` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:91` |
| `GET` | `/api/backfill/checkpoints/validation` | Gap | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:37` |
| `GET` | `/api/backfill/checkpoints/{jobId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:118` |
| `GET` | `/api/backfill/checkpoints/{jobId}/pending` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:144` |
| `POST` | `/api/backfill/checkpoints/{jobId}/resume` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:204` |
| `GET` | `/api/backfill/completeness` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:207` |
| `POST` | `/api/backfill/cost-estimate` | Documented | `src/Meridian.Ui.Shared/Endpoints/ResilienceEndpoints.cs:48` |
| `GET` | `/api/backfill/executions` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:173` |
| `POST` | `/api/backfill/gap-fill` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:89` |
| `GET` | `/api/backfill/gaps` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:165` |
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
| `GET` | `/api/backfill/schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:236` |
| `POST` | `/api/backfill/schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:258` |
| `GET` | `/api/backfill/schedules/templates` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:471` |
| `DELETE` | `/api/backfill/schedules/{id}` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:321` |
| `GET` | `/api/backfill/schedules/{id}` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:300` |
| `POST` | `/api/backfill/schedules/{id}/disable` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:367` |
| `POST` | `/api/backfill/schedules/{id}/enable` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:344` |
| `GET` | `/api/backfill/schedules/{id}/history` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:452` |
| `POST` | `/api/backfill/schedules/{id}/run` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:390` |
| `GET` | `/api/backfill/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:196` |
| `GET` | `/api/backfill/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:53` |
| `GET` | `/api/backfill/validation` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:25` |
| `GET` | `/api/backfill/validation/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:110` |
| `GET` | `/api/backpressure` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:133` |
| `GET` | `/api/calendar/holidays` | Gap | `src/Meridian.Ui.Shared/Endpoints/CalendarEndpoints.cs:64` |
| `GET` | `/api/calendar/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/CalendarEndpoints.cs:21` |
| `GET` | `/api/calendar/trading-days` | Gap | `src/Meridian.Ui.Shared/Endpoints/CalendarEndpoints.cs:89` |
| `GET` | `/api/canonicalization/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:112` |
| `GET` | `/api/canonicalization/parity` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:47` |
| `GET` | `/api/canonicalization/parity/{provider}` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:75` |
| `GET` | `/api/canonicalization/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:20` |
| `GET` | `/api/catalog/coverage` | Gap | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:232` |
| `GET` | `/api/catalog/search` | Gap | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:24` |
| `GET` | `/api/catalog/symbols` | Gap | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:128` |
| `GET` | `/api/catalog/timeline` | Gap | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:160` |
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
| `GET` | `/api/connections` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:159` |
| `GET` | `/api/data/bbo/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:269` |
| `GET` | `/api/data/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:350` |
| `GET` | `/api/data/l3-orderbook/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:199` |
| `GET` | `/api/data/orderbook/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:127` |
| `GET` | `/api/data/orderflow/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:309` |
| `GET` | `/api/data/quotes-snapshot` | Gap | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:109` |
| `GET` | `/api/data/quotes/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:65` |
| `GET` | `/api/data/trades/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:25` |
| `GET` | `/api/demo/historical/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:132` |
| `GET` | `/api/demo/market-data/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:111` |
| `GET` | `/api/demo/mode` | Gap | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:78` |
| `GET` | `/api/demo/symbols` | Gap | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:95` |
| `POST` | `/api/dev/seed/bank-transactions` | Documented | `src/Meridian.Ui.Shared/Endpoints/BankingEndpoints.cs:308` |
| `GET` | `/api/diagnostics/bundle` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:148` |
| `GET` | `/api/diagnostics/config` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:122` |
| `GET` | `/api/diagnostics/coordination` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:494` |
| `POST` | `/api/diagnostics/dry-run` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:35` |
| `GET` | `/api/diagnostics/error-codes` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:377` |
| `GET` | `/api/diagnostics/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:169` |
| `GET` | `/api/diagnostics/providers` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:55` |
| `POST` | `/api/diagnostics/providers/{providerName}/test` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:297` |
| `GET` | `/api/diagnostics/quick-check` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:336` |
| `POST` | `/api/diagnostics/selftest` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:387` |
| `GET` | `/api/diagnostics/show-config` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:353` |
| `GET` | `/api/diagnostics/storage` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:72` |
| `POST` | `/api/diagnostics/test-connectivity` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:426` |
| `POST` | `/api/diagnostics/validate` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:275` |
| `POST` | `/api/diagnostics/validate-config` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:465` |
| `POST` | `/api/diagnostics/validate-credentials` | Gap | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:408` |
| `GET` | `/api/errors` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:122` |
| `GET` | `/api/events/stream` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:225` |
| `POST` | `/api/export/analysis` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:81` |
| `GET` | `/api/export/formats` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:157` |
| `POST` | `/api/export/integrity` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:298` |
| `POST` | `/api/export/orderflow` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:234` |
| `GET` | `/api/export/preview` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:28` |
| `POST` | `/api/export/quality-report` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:218` |
| `POST` | `/api/export/research-package` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:425` |
| `POST` | `/api/export/strategy-package` | Gap | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:418` |
| `GET` | `/api/failover/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:35` |
| `POST` | `/api/failover/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:65` |
| `GET` | `/api/failover/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:334` |
| `GET` | `/api/failover/rules` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:113` |
| `POST` | `/api/failover/rules` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:134` |
| `GET` | `/api/funds/{fundId:guid}/accounts` | Documented | `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs:127` |
| `GET` | `/api/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:41` |
| `GET` | `/api/health/detailed` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:197` |
| `GET` | `/api/health/diagnostics/bundle` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:258` |
| `GET` | `/api/health/events` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:188` |
| `GET` | `/api/health/metrics` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:201` |
| `GET` | `/api/health/providers` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:55` |
| `GET` | `/api/health/providers/{provider}/diagnostics` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:103` |
| `POST` | `/api/health/providers/{provider}/test` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:220` |
| `GET` | `/api/health/storage` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:153` |
| `GET` | `/api/health/summary` | Gap | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:28` |
| `GET` | `/api/indices/{indexName}/constituents` | Gap | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:553` |
| `GET` | `/api/ingestion/jobs` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:29` |
| `POST` | `/api/ingestion/jobs` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:110` |
| `GET` | `/api/ingestion/jobs/resumable` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:149` |
| `DELETE` | `/api/ingestion/jobs/{jobId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:171` |
| `GET` | `/api/ingestion/jobs/{jobId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:51` |
| `POST` | `/api/ingestion/jobs/{jobId}/transition` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:66` |
| `GET` | `/api/ingestion/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:160` |
| `POST` | `/api/journals/{journalEntryId:guid}/post` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:945` |
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
| `GET` | `/api/ledger/aggregates/{aggregateId:guid}/journal-entries` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:317` |
| `GET` | `/api/ledger/books` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:29` |
| `POST` | `/api/ledger/books` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:79` |
| `POST` | `/api/ledger/books/rollout-assessment` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:110` |
| `GET` | `/api/ledger/books/{ledgerBookId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:56` |
| `POST` | `/api/ledger/close-management/evidence-review` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:840` |
| `POST` | `/api/ledger/close-management/late-adjustments` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:654` |
| `POST` | `/api/ledger/close-management/late-adjustments/review` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:716` |
| `POST` | `/api/ledger/close-management/period-lock` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:902` |
| `POST` | `/api/ledger/close-management/period-plan/configuration` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:592` |
| `GET` | `/api/ledger/close-management/period-plan/{workflowId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:561` |
| `POST` | `/api/ledger/close-management/period-reopen` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:976` |
| `POST` | `/api/ledger/close-management/task-signoffs` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:778` |
| `POST` | `/api/ledger/journal-automation/daily-mark-to-market-batch-lifecycle` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:378` |
| `POST` | `/api/ledger/journal-automation/daily-mark-to-market-intake` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:427` |
| `POST` | `/api/ledger/journal-automation/daily-mark-to-market-run-due` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:348` |
| `GET` | `/api/ledger/journal-automation/daily-mark-to-market-schedules` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:226` |
| `POST` | `/api/ledger/journal-automation/daily-mark-to-market-schedules` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:257` |
| `POST` | `/api/ledger/journal-automation/dividend-intake` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:470` |
| `POST` | `/api/ledger/journal-automation/fee-accrual-intake` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:513` |
| `GET` | `/api/ledger/journal-automation/monthly-schedules` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:21` |
| `POST` | `/api/ledger/journal-automation/monthly-schedules` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:57` |
| `POST` | `/api/ledger/journal-automation/monthly-schedules/run-due` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:196` |
| `POST` | `/api/ledger/journal-automation/period-close-intake` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:558` |
| `GET` | `/api/ledger/journal-entry-workbench` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1249` |
| `POST` | `/api/ledger/journal-entry-workbench/drafts` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1570` |
| `POST` | `/api/ledger/journal-entry-workbench/evidence` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1720` |
| `POST` | `/api/ledger/journal-entry-workbench/lifecycle-action` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1763` |
| `POST` | `/api/ledger/journal-entry-workbench/submit-approval` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1675` |
| `POST` | `/api/ledger/journal-entry-workbench/validate` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1629` |
| `GET` | `/api/ledger/periods` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:151` |
| `POST` | `/api/ledger/periods` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:189` |
| `POST` | `/api/ledger/periods/{periodId:guid}/close` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:221` |
| `GET` | `/api/ledger/periods/{periodId:guid}/journal-entries` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:273` |
| `GET` | `/api/ledger/periods/{periodId:guid}/pnl-summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:416` |
| `GET` | `/api/ledger/periods/{periodId:guid}/trial-balance` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:366` |
| `GET` | `/api/ledger/periods/{periodId:guid}/trial-balance-report` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:391` |
| `GET` | `/api/ledger/private-capital/activity` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1275` |
| `GET` | `/api/ledger/private-capital/capital-account-subledger` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1391` |
| `GET` | `/api/ledger/private-capital/capital-account-workbench` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1523` |
| `GET` | `/api/ledger/private-capital/fund-event-command-center` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1350` |
| `GET` | `/api/ledger/private-capital/fund-event-record` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1307` |
| `GET` | `/api/ledger/private-capital/report-output` | Gap | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1452` |
| `POST` | `/api/ledger/reports/accounting-package` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1047` |
| `POST` | `/api/ledger/reports/accounting-package/certification` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1098` |
| `GET` | `/api/ledger/reports/accounting-packages` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1156` |
| `GET` | `/api/ledger/reports/accounting-packages/{packageId}/exports/{artifactId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1198` |
| `GET` | `/api/ledger/reports/pnl-summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:500` |
| `GET` | `/api/ledger/reports/trial-balance` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:441` |
| `GET` | `/api/loans/portfolio` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1102` |
| `POST` | `/api/loans/rebuild-all` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1113` |
| `GET` | `/api/loans/rebuild-checkpoints` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1093` |
| `POST` | `/api/maintenance/execute` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:141` |
| `GET` | `/api/maintenance/executions` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:182` |
| `POST` | `/api/maintenance/executions/cleanup` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:421` |
| `GET` | `/api/maintenance/executions/failed` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:224` |
| `GET` | `/api/maintenance/executions/{executionId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:195` |
| `POST` | `/api/maintenance/executions/{executionId}/cancel` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:165` |
| `GET` | `/api/maintenance/presets` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:344` |
| `GET` | `/api/maintenance/schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:20` |
| `POST` | `/api/maintenance/schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:35` |
| `GET` | `/api/maintenance/schedules/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:239` |
| `GET` | `/api/maintenance/schedules/{id}` | Gap | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:49` |
| `DELETE` | `/api/maintenance/schedules/{id}/delete` | Gap | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:59` |
| `POST` | `/api/maintenance/schedules/{id}/disable` | Gap | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:87` |
| `POST` | `/api/maintenance/schedules/{id}/enable` | Gap | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:73` |
| `GET` | `/api/maintenance/schedules/{id}/history` | Gap | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:130` |
| `POST` | `/api/maintenance/schedules/{id}/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:101` |
| `DELETE` | `/api/maintenance/schedules/{scheduleId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:98` |
| `PUT` | `/api/maintenance/schedules/{scheduleId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:36` |
| `GET` | `/api/maintenance/schedules/{scheduleId}/executions` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:210` |
| `GET` | `/api/maintenance/schedules/{scheduleId}/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:250` |
| `POST` | `/api/maintenance/schedules/{scheduleId}/trigger` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:118` |
| `GET` | `/api/maintenance/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:264` |
| `GET` | `/api/maintenance/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:291` |
| `GET` | `/api/maintenance/task-types` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:400` |
| `POST` | `/api/maintenance/validate-cron` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:304` |
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
| `GET` | `/api/packaging/download/{fileName}` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:284` |
| `POST` | `/api/packaging/import` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:87` |
| `GET` | `/api/packaging/list` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:202` |
| `POST` | `/api/packaging/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:136` |
| `DELETE` | `/api/packaging/{fileName}` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:246` |
| `GET` | `/api/plaid/accounts` | Gap | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:31` |
| `GET` | `/api/plaid/institutions/search` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:44` |
| `GET` | `/api/plaid/items` | Gap | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:18` |
| `POST` | `/api/plaid/items/{itemId}/sync` | Gap | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:124` |
| `POST` | `/api/plaid/link-token` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:74` |
| `POST` | `/api/plaid/public-token/exchange` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:99` |
| `POST` | `/api/plaid/transfers/sandbox` | Gap | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:176` |
| `POST` | `/api/plaid/webhook` | Gap | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:154` |
| `GET` | `/api/portfolio/household` | Gap | `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs:259` |
| `GET` | `/api/projections/{projectionRunId:guid}/flows` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:928` |
| `GET` | `/api/provider-routing/bindings` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:36` |
| `GET` | `/api/provider-routing/connections` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:20` |
| `POST` | `/api/provider-routing/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:68` |
| `GET` | `/api/provider-routing/trust-snapshots` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:52` |
| `GET` | `/api/providers/capabilities` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:143` |
| `GET` | `/api/providers/capability-matrix` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:177` |
| `GET` | `/api/providers/catalog` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:572` |
| `GET` | `/api/providers/catalog/{providerId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:618` |
| `GET` | `/api/providers/comparison` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:290` |
| `POST` | `/api/providers/configure` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:267` |
| `GET` | `/api/providers/connections` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:19` |
| `GET` | `/api/providers/dashboard` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:301` |
| `GET` | `/api/providers/data-projection` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderDataProjectionEndpoints.cs:16` |
| `GET` | `/api/providers/failover` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:56` |
| `GET` | `/api/providers/failover-thresholds` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:226` |
| `POST` | `/api/providers/failover/reset` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:98` |
| `POST` | `/api/providers/failover/trigger` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:82` |
| `GET` | `/api/providers/health` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:243` |
| `GET` | `/api/providers/ib/error-codes` | Documented | `src/Meridian.Ui.Shared/Endpoints/IBEndpoints.cs:91` |
| `GET` | `/api/providers/ib/limits` | Documented | `src/Meridian.Ui.Shared/Endpoints/IBEndpoints.cs:113` |
| `GET` | `/api/providers/ib/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/IBEndpoints.cs:24` |
| `GET` | `/api/providers/latency` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:144` |
| `GET` | `/api/providers/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:488` |
| `GET` | `/api/providers/modules` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:23` |
| `POST` | `/api/providers/modules` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:56` |
| `GET` | `/api/providers/modules/catalogue` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:40` |
| `DELETE` | `/api/providers/modules/{moduleId}` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:110` |
| `PUT` | `/api/providers/modules/{moduleId}` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:82` |
| `PUT` | `/api/providers/modules/{moduleId}/enabled` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:135` |
| `POST` | `/api/providers/modules/{moduleId}/test` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:158` |
| `GET` | `/api/providers/rate-limits` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:113` |
| `GET` | `/api/providers/readiness` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:361` |
| `POST` | `/api/providers/restart` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:192` |
| `GET` | `/api/providers/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:373` |
| `POST` | `/api/providers/switch` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:162` |
| `DELETE` | `/api/providers/{providerId}/credentials` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:87` |
| `PUT` | `/api/providers/{providerId}/credentials` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:34` |
| `POST` | `/api/providers/{providerId}/verify` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:63` |
| `GET` | `/api/providers/{providerName}` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:26` |
| `GET` | `/api/providers/{providerName}/rate-limit-history` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:126` |
| `POST` | `/api/providers/{providerName}/test` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:189` |
| `POST` | `/api/providers/{provider}/test-connection` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderCredentialEndpoints.cs:69` |
| `POST` | `/api/providers/{provider}/validate-credentials` | Gap | `src/Meridian.Ui.Shared/Endpoints/ProviderCredentialEndpoints.cs:26` |
| `GET` | `/api/quality/anomalies` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:242` |
| `GET` | `/api/quality/anomalies/stale` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:286` |
| `GET` | `/api/quality/anomalies/statistics` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:283` |
| `GET` | `/api/quality/anomalies/unacknowledged` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:271` |
| `POST` | `/api/quality/anomalies/{anomalyId}/acknowledge` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:274` |
| `GET` | `/api/quality/anomalies/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:268` |
| `GET` | `/api/quality/comparison/discrepancies` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:321` |
| `GET` | `/api/quality/comparison/statistics` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:333` |
| `GET` | `/api/quality/comparison/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:314` |
| `GET` | `/api/quality/completeness` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:84` |
| `GET` | `/api/quality/completeness/low` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:109` |
| `GET` | `/api/quality/completeness/summary` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:106` |
| `GET` | `/api/quality/completeness/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:91` |
| `GET` | `/api/quality/dashboard` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:68` |
| `GET` | `/api/quality/drops` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:289` |
| `GET` | `/api/quality/drops/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:314` |
| `GET` | `/api/quality/errors` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:215` |
| `GET` | `/api/quality/errors/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:234` |
| `GET` | `/api/quality/errors/top-symbols` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:237` |
| `GET` | `/api/quality/errors/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:227` |
| `GET` | `/api/quality/gaps` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:118` |
| `GET` | `/api/quality/gaps/statistics` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:206` |
| `GET` | `/api/quality/gaps/timeline/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:198` |
| `GET` | `/api/quality/gaps/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:130` |
| `POST` | `/api/quality/gaps/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:137` |
| `GET` | `/api/quality/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:380` |
| `GET` | `/api/quality/health/unhealthy` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:413` |
| `GET` | `/api/quality/health/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:404` |
| `GET` | `/api/quality/latency` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:291` |
| `GET` | `/api/quality/latency/high` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:309` |
| `GET` | `/api/quality/latency/statistics` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:306` |
| `GET` | `/api/quality/latency/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:294` |
| `GET` | `/api/quality/latency/{symbol}/histogram` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:303` |
| `GET` | `/api/quality/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:79` |
| `GET` | `/api/quality/reports/daily` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:338` |
| `POST` | `/api/quality/reports/export` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:365` |
| `GET` | `/api/quality/reports/weekly` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:346` |
| `POST` | `/api/quant/parameters` | Gap | `src/Meridian.Ui.Shared/Endpoints/QuantLabEndpoints.cs:89` |
| `POST` | `/api/quant/run` | Gap | `src/Meridian.Ui.Shared/Endpoints/QuantLabEndpoints.cs:37` |
| `GET` | `/api/quant/templates` | Gap | `src/Meridian.Ui.Shared/Endpoints/QuantLabEndpoints.cs:125` |
| `GET` | `/api/reconciliation/exceptions` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1007` |
| `POST` | `/api/reconciliation/exceptions/{exceptionId:guid}/resolve` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1016` |
| `GET` | `/api/reconciliation/{runId:guid}/results` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:998` |
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
| `POST` | `/api/security-master` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:343` |
| `POST` | `/api/security-master/aliases/upsert` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:427` |
| `POST` | `/api/security-master/amend` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:371` |
| `GET` | `/api/security-master/asset-profiles` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:77` |
| `POST` | `/api/security-master/asset-profiles/approve` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:161` |
| `POST` | `/api/security-master/asset-profiles/drafts` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:120` |
| `GET` | `/api/security-master/asset-profiles/promotion-candidates` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:92` |
| `POST` | `/api/security-master/asset-profiles/rollback` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:202` |
| `GET` | `/api/security-master/asset-profiles/{profileId}/lineage` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:104` |
| `GET` | `/api/security-master/conflicts` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:757` |
| `POST` | `/api/security-master/conflicts/{conflictId:guid}/resolve` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:770` |
| `GET` | `/api/security-master/corporate-actions/inbox` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:682` |
| `POST` | `/api/security-master/corporate-actions/inbox/apply` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:692` |
| `POST` | `/api/security-master/corporate-actions/ingest` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:653` |
| `GET` | `/api/security-master/coverage/draft/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:741` |
| `GET` | `/api/security-master/data-entitlements` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1285` |
| `POST` | `/api/security-master/data-entitlements` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1307` |
| `GET` | `/api/security-master/data-entitlements/expiring` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1295` |
| `DELETE` | `/api/security-master/data-entitlements/{entitlementId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1335` |
| `POST` | `/api/security-master/deactivate` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:399` |
| `GET` | `/api/security-master/exceptions/aging` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1398` |
| `POST` | `/api/security-master/import` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:811` |
| `POST` | `/api/security-master/ingest/edgar` | Documented | `src/Meridian.Ui.Shared/Endpoints/EdgarReferenceDataEndpoints.cs:24` |
| `GET` | `/api/security-master/ingest/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:842` |
| `GET` | `/api/security-master/quality-report/latest` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1382` |
| `POST` | `/api/security-master/quality-report/run` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1367` |
| `POST` | `/api/security-master/resolve` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:248` |
| `POST` | `/api/security-master/search` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:285` |
| `GET` | `/api/security-master/{securityId:guid}` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:42` |
| `GET` | `/api/security-master/{securityId:guid}/cashflow-projections` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1262` |
| `GET` | `/api/security-master/{securityId:guid}/cashflow-source` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1221` |
| `PUT` | `/api/security-master/{securityId:guid}/cashflow-source` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1234` |
| `GET` | `/api/security-master/{securityId:guid}/convertible-equity-terms` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:535` |
| `PATCH` | `/api/security-master/{securityId:guid}/convertible-equity-terms` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:556` |
| `GET` | `/api/security-master/{securityId:guid}/corporate-actions` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:593` |
| `POST` | `/api/security-master/{securityId:guid}/corporate-actions` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:614` |
| `GET` | `/api/security-master/{securityId:guid}/history` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:316` |
| `GET` | `/api/security-master/{securityId:guid}/operator-overrides` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:863` |
| `PATCH` | `/api/security-master/{securityId:guid}/operator-overrides` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:887` |
| `POST` | `/api/security-master/{securityId:guid}/operator-overrides/decision` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:917` |
| `GET` | `/api/security-master/{securityId:guid}/preferred-equity-terms` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:478` |
| `PATCH` | `/api/security-master/{securityId:guid}/preferred-equity-terms` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:499` |
| `GET` | `/api/security-master/{securityId:guid}/price-comparison` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1204` |
| `GET` | `/api/security-master/{securityId:guid}/price-golden-copy` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1190` |
| `GET` | `/api/security-master/{securityId:guid}/pricing-hierarchy` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1116` |
| `PUT` | `/api/security-master/{securityId:guid}/pricing-hierarchy` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1130` |
| `POST` | `/api/security-master/{securityId:guid}/raw-price` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1160` |
| `GET` | `/api/security-master/{securityId:guid}/trading-parameters` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:455` |
| `GET` | `/api/security-master/{securityId:guid}/validation` | Gap | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:63` |
| `POST` | `/api/servicer-reports` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1039` |
| `GET` | `/api/servicer-reports/{batchId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1067` |
| `GET` | `/api/servicer-reports/{batchId:guid}/position-lines` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1075` |
| `GET` | `/api/servicer-reports/{batchId:guid}/transaction-lines` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1084` |
| `GET` | `/api/sla/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:637` |
| `GET` | `/api/sla/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:663` |
| `GET` | `/api/sla/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:609` |
| `GET` | `/api/sla/status/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:612` |
| `GET` | `/api/sla/violations` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:621` |
| `GET` | `/api/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:111` |
| `GET` | `/api/storage/archive/stats` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:309` |
| `GET` | `/api/storage/breakdown` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:79` |
| `GET` | `/api/storage/capacity-forecast` | Gap | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:536` |
| `GET` | `/api/storage/catalog` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:343` |
| `POST` | `/api/storage/cleanup` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:274` |
| `GET` | `/api/storage/cleanup/candidates` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:239` |
| `POST` | `/api/storage/convert-parquet` | Gap | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:512` |
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
| `POST` | `/api/strategies/covered-call/chain-preview` | Gap | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:197` |
| `GET` | `/api/strategies/covered-call/runs` | Gap | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:63` |
| `POST` | `/api/strategies/covered-call/runs` | Gap | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:28` |
| `POST` | `/api/strategies/covered-call/runs/{runId}/cancel` | Gap | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:167` |
| `GET` | `/api/strategies/covered-call/runs/{runId}/result` | Gap | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:118` |
| `GET` | `/api/strategies/covered-call/runs/{runId}/status` | Gap | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:88` |
| `GET` | `/api/strategies/runs/compare` | Gap | `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs:2940` |
| `GET` | `/api/strategies/{strategyId}/runs` | Gap | `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs:2799` |
| `GET` | `/api/subscriptions/active` | Gap | `src/Meridian.Ui.Shared/Endpoints/SubscriptionEndpoints.cs:21` |
| `POST` | `/api/subscriptions/subscribe` | Gap | `src/Meridian.Ui.Shared/Endpoints/SubscriptionEndpoints.cs:43` |
| `POST` | `/api/subscriptions/unsubscribe/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/SubscriptionEndpoints.cs:72` |
| `GET` | `/api/symbols` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:31` |
| `POST` | `/api/symbols/add` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:138` |
| `GET` | `/api/symbols/archived` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:79` |
| `POST` | `/api/symbols/batch` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:387` |
| `POST` | `/api/symbols/bulk-add` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:302` |
| `POST` | `/api/symbols/bulk-remove` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:342` |
| `POST` | `/api/symbols/create` | Gap | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:435` |
| `GET` | `/api/symbols/mappings` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolMappingEndpoints.cs:70` |
| `POST` | `/api/symbols/mappings` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolMappingEndpoints.cs:77` |
| `GET` | `/api/symbols/monitored` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:56` |
| `GET` | `/api/symbols/registry` | Gap | `src/Meridian.Ui.Shared/Endpoints/SymbolMappingEndpoints.cs:29` |
| `GET` | `/api/symbols/search` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:363` |
| `GET` | `/api/symbols/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:232` |
| `POST` | `/api/symbols/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:264` |
| `DELETE` | `/api/symbols/{symbol}` | Gap | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:525` |
| `POST` | `/api/symbols/{symbol}/archive` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:283` |
| `GET` | `/api/symbols/{symbol}/depth` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:207` |
| `POST` | `/api/symbols/{symbol}/remove` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:162` |
| `GET` | `/api/symbols/{symbol}/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:98` |
| `GET` | `/api/symbols/{symbol}/trades` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:182` |
| `POST` | `/api/symbols/{symbol}/update` | Gap | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:475` |
| `GET` | `/api/system/lifecycle` | Documented | `src/Meridian/UiServer.cs:603` |
| `POST` | `/api/system/shutdown` | Documented | `src/Meridian/UiServer.cs:637` |
| `GET` | `/api/system/shutdown/receipts/latest` | Documented | `src/Meridian/UiServer.cs:708` |
| `GET` | `/api/system/shutdown/{operationId}` | Documented | `src/Meridian/UiServer.cs:687` |
| `POST` | `/api/workstation/desktop/launch` | Gap | `src/Meridian.Ui.Shared/Endpoints/FirstRunEndpoints.cs:29` |
| `GET` | `/health` | Documented | `src/Meridian.Application/Composition/HostAdapters.cs:60` |
| `GET` | `/live` | Documented | `src/Meridian.Application/Composition/HostAdapters.cs:73` |
| `GET` | `/ready` | Documented | `src/Meridian.Application/Composition/HostAdapters.cs:72` |

## Workstation Contract Coverage

| Contract | Status | Source |
|---|---|---|
| `AccrualCalculationResultDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:268` |
| `AccrualInputSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:250` |
| `ActivationOutcomeDto` | Gap | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:76` |
| `AlpacaBrokerageConnectionRequestDto` | Gap | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:103` |
| `ApprovalDecision` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:30` |
| `ApprovalPolicy` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:22` |
| `ApprovalStep` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:21` |
| `ApproveSecurityMasterOverrides` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:22` |
| `ApproveSecurityMasterRevisionRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:85` |
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
| `BulkResolveSecurityMasterConflictsRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:577` |
| `BulkResolveSecurityMasterConflictsResult` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:583` |
| `CanonicalizationAssuranceDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:109` |
| `CanonicalizationProviderSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:119` |
| `CashFinancingSummary` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:140` |
| `CashFlowEntryDto` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:932` |
| `CashFlowProjectionPoint` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:34` |
| `CashForecastResult` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:36` |
| `CashLadderBucketDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:942` |
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
| `DailyValuationBatchLifecycleResultDto` | Documented | `src/Meridian.Contracts/Workstation/DailyValuationScheduleDtos.cs:67` |
| `DailyValuationScheduleStateDto` | Documented | `src/Meridian.Contracts/Workstation/DailyValuationScheduleDtos.cs:7` |
| `DailyValuationScheduleStatusDto` | Documented | `src/Meridian.Contracts/Workstation/DailyValuationScheduleDtos.cs:22` |
| `DataUploadPreviewResultDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:53` |
| `DataUploadTemplateCatalogDto` | Gap | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:12` |
| `DataUploadTemplateDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:24` |
| `DataUploadTemplateFieldDto` | Gap | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:43` |
| `DataUploadValidationIssueDto` | Gap | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:134` |
| `DataUploadWorkbookPreviewResultDto` | Gap | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:111` |
| `DataUploadWorkbookSheetPreviewDto` | Gap | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:94` |
| `DeltaOutlierResult` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:15` |
| `DesktopLaunchTicketRedemptionDto` | Gap | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:94` |
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
| `ExpectedAccountingEventDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:284` |
| `ExpectedAccountingEventKindDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:229` |
| `ExpectedJournalPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:319` |
| `ExpectedJournalPreviewLineDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:309` |
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
| `FundLedgerScope` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:8` |
| `FundLedgerSliceDto` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:75` |
| `FundLedgerSnapshotBalanceLine` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:114` |
| `FundLedgerSummary` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:90` |
| `FundLedgerTotalsDto` | Gap | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:63` |
| `FundNavAssetClassExposureDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:107` |
| `FundNavAttributionSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:114` |
| `FundOperationsNavigationContext` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:26` |
| `FundOperationsTab` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:8` |
| `FundOperationsWorkspaceDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:460` |
| `FundOperationsWorkspaceQuery` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:96` |
| `FundPortfolioPosition` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:118` |
| `FundReconciliationItem` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:165` |
| `FundReportAssetClassSectionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:493` |
| `FundReportPackArtifactDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:551` |
| `FundReportPackEvidenceBundleApprovalDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:794` |
| `FundReportPackEvidenceBundleDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:802` |
| `FundReportPackEvidenceBundleSourceLinkDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:785` |
| `FundReportPackGenerateRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:535` |
| `FundReportPackHistoryItemDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:762` |
| `FundReportPackLifecycleEventDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:720` |
| `FundReportPackLineagePointerDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:574` |
| `FundReportPackPreviewDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:518` |
| `FundReportPackPreviewRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:482` |
| `FundReportPackProvenanceDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:562` |
| `FundReportPackSnapshotDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:731` |
| `FundReportPackValidationIssueDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:703` |
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
| `InstrumentPassportOperationsWorkbenchDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:426` |
| `InstrumentPassportOperationsWorkbenchItemDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:440` |
| `InstrumentPassportOperationsWorkbenchPanelDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:433` |
| `InstrumentPassportPricingDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:567` |
| `InstrumentPassportProviderConfidenceDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:382` |
| `InstrumentPassportReferenceDataWorkbenchDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:397` |
| `InstrumentPassportReferenceDataWorkbenchSectionDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:403` |
| `InvestmentAccountingPreviewModeDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:24` |
| `InvestmentAccountingReconciliationExpectationDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:54` |
| `InvestmentAccountingTradeSideDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:17` |
| `InvestmentAccountingTransactionKindDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:6` |
| `InvestmentAccountingTransactionLabPreviewDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:71` |
| `InvestmentAccountingTransactionLabRequestDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:29` |
| `InvestmentAccountingTrialBalanceImpactDto` | Gap | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:47` |
| `LedgerAmountApprovalStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:660` |
| `LedgerAmountProvenanceDetailDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:684` |
| `LedgerAmountProvenanceEvidenceDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:587` |
| `LedgerAmountReconciliationCaseDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:622` |
| `LedgerAmountReconciliationStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:653` |
| `LedgerAmountReportUsageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:666` |
| `LedgerAmountSecurityMasterLinkDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:611` |
| `LedgerAmountStrategyRunLinkDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:676` |
| `LedgerImpactPreviewDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:290` |
| `LedgerJournalLine` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:632` |
| `LedgerSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:583` |
| `LedgerTrialBalanceLine` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:611` |
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
| `NullReportingRunNotifier` | Documented | `src/Meridian.Contracts/Workstation/IReportingRunNotifier.cs:21` |
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
| `OperationsGateKeyDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:42` |
| `OperationsGatePostureRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:504` |
| `OperationsGateStatusDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:29` |
| `OperationsIssueCodeDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:445` |
| `OperationsJournalEntryMetadataDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:613` |
| `OperationsLedgerDraftRequestDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:536` |
| `OperationsLedgerJournalCandidateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:574` |
| `OperationsLedgerJournalLineDto` | Gap | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:597` |
| `OperationsLedgerPostRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:560` |
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
| `OperationsStartWorkflowRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:474` |
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
| `PortfolioPositionSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:563` |
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
| `PortfolioSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:527` |
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
| `PublishSecurityMasterRevisionRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:103` |
| `RecommendedActionDto` | Gap | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:84` |
| `ReconciliationBreakCategory` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:54` |
| `ReconciliationBreakDispositionDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:459` |
| `ReconciliationBreakDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:141` |
| `ReconciliationBreakExplanationDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:608` |
| `ReconciliationBreakMeasureDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:449` |
| `ReconciliationBreakMeasureKindDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:438` |
| `ReconciliationBreakQueueItem` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:480` |
| `ReconciliationBreakQueueProjectionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:247` |
| `ReconciliationBreakQueueProjectionItemDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:262` |
| `ReconciliationBreakQueueScope` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:587` |
| `ReconciliationBreakQueueStatus` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:365` |
| `ReconciliationBreakScore` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:918` |
| `ReconciliationBreakSeverity` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:41` |
| `ReconciliationBreakStatus` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:28` |
| `ReconciliationBulkCaseworkCaseResult` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:749` |
| `ReconciliationBulkCaseworkRequest` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:725` |
| `ReconciliationBulkCaseworkResult` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:756` |
| `ReconciliationCalibrationProfileSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:965` |
| `ReconciliationCalibrationStatusDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:470` |
| `ReconciliationCalibrationSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:982` |
| `ReconciliationCaseComment` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:616` |
| `ReconciliationCaseCommentVisibility` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:411` |
| `ReconciliationCaseLifecycleState` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:375` |
| `ReconciliationCasePriority` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:391` |
| `ReconciliationCaseSignoffRecord` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:909` |
| `ReconciliationCaseSlaState` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:400` |
| `ReconciliationCaseStateTransition` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:931` |
| `ReconciliationCaseTransitionAction` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:942` |
| `ReconciliationCaseTransitionCommand` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:952` |
| `ReconciliationCaseworkAction` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:419` |
| `ReconciliationCaseworkCloseScopeDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:720` |
| `ReconciliationCaseworkCommand` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:679` |
| `ReconciliationCaseworkOperationResult` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:902` |
| `ReconciliationCorrelationContext` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1043` |
| `ReconciliationJobControl` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1066` |
| `ReconciliationMatchDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:117` |
| `ReconciliationPayloadEnvelope` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1053` |
| `ReconciliationProcessingTelemetry` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1081` |
| `ReconciliationRolloutFlags` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1092` |
| `ReconciliationRunDetail` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:346` |
| `ReconciliationRunRequest` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:72` |
| `ReconciliationRunSummary` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:86` |
| `ReconciliationSchemaVersion` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1033` |
| `ReconciliationSecurityCoverageIssueDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:216` |
| `ReconciliationSlaComputationResult` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:657` |
| `ReconciliationSlaPolicy` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:636` |
| `ReconciliationSourceKind` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:14` |
| `ReconciliationSummary` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:187` |
| `ReconciliationTaxonomySnapshot` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:674` |
| `ReconciliationTaxonomyValue` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:666` |
| `RejectWorkflow` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:30` |
| `RenderReportTemplateRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1626` |
| `RenderReportTemplateResponseDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1631` |
| `ReopenWorkflow` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:32` |
| `ReportAccessEvaluationDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1613` |
| `ReportAccessModeDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1587` |
| `ReportAccessPolicyDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1606` |
| `ReportAccessPrincipalDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1601` |
| `ReportAccessPrincipalKindDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1595` |
| `ReportBrandingThemeDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:138` |
| `ReportPackAuditEventDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1694` |
| `ReportPackChangedLineDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1697` |
| `ReportPackCreateRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1747` |
| `ReportPackDeliveryAccessLinkDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:856` |
| `ReportPackDeliveryApprovalStepDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:884` |
| `ReportPackDeliveryArtifactDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:845` |
| `ReportPackDeliveryAttemptDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:967` |
| `ReportPackDeliveryEvidencePacketDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:892` |
| `ReportPackDeliveryFailureRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:994` |
| `ReportPackDeliveryHistoryDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1003` |
| `ReportPackDeliveryModeDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:838` |
| `ReportPackDeliveryNotificationDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:864` |
| `ReportPackDeliveryPackageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:916` |
| `ReportPackDeliveryRecipientDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:878` |
| `ReportPackDeliveryRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:984` |
| `ReportPackDeliveryStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:828` |
| `ReportPackEvidenceLinkDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1696` |
| `ReportPackLineProvenanceDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1698` |
| `ReportPackPublicationManifestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1716` |
| `ReportPackPublishRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1729` |
| `ReportPackRejectRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1742` |
| `ReportPackRejectionMetadataDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1762` |
| `ReportPackRestateRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1768` |
| `ReportPackRestatementMetadataDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1755` |
| `ReportPackWorkflowActionRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1739` |
| `ReportPackWorkflowRecordDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1774` |
| `ReportPackWorkflowStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1353` |
| `ReportTemplateAuditEventDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1647` |
| `ReportTemplateDecisionRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1690` |
| `ReportTemplateDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1618` |
| `ReportTemplateDraftRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1679` |
| `ReportTemplateGovernanceRecordDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1655` |
| `ReportTemplateLifecycleStatusDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1639` |
| `ReportTemplateParameterDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1368` |
| `ReportWriterAggregateFunctionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1380` |
| `ReportWriterCellStyleDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1438` |
| `ReportWriterChartDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1463` |
| `ReportWriterChartRenderDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1474` |
| `ReportWriterChartSeriesDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1469` |
| `ReportWriterChartTypeDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1455` |
| `ReportWriterDiffDirectionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1554` |
| `ReportWriterDiffRowStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1545` |
| `ReportWriterFilterDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1415` |
| `ReportWriterFilterLineageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1502` |
| `ReportWriterFilterOperatorDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1401` |
| `ReportWriterFormatRuleDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1446` |
| `ReportWriterFormulaDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1394` |
| `ReportWriterFormulaLineageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1497` |
| `ReportWriterGridColumnDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1482` |
| `ReportWriterGridDataDictionaryFieldDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1508` |
| `ReportWriterGridDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1421` |
| `ReportWriterGridDiffCellDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1560` |
| `ReportWriterGridDiffDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1574` |
| `ReportWriterGridDiffRowDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1567` |
| `ReportWriterGridKindDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1371` |
| `ReportWriterGridLineageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1522` |
| `ReportWriterGridRenderDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1531` |
| `ReportWriterGridRowDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1487` |
| `ReportWriterGridValidationCheckDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1517` |
| `ReportWriterMetricDefinitionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1388` |
| `ReportWriterMetricLineageDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1492` |
| `ReportingAccountingBasisDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1208` |
| `ReportingConsolidationLevelDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1218` |
| `ReportingDeploymentCapabilityDto` | Documented | `src/Meridian.Contracts/Workstation/ReportingDeploymentDtos.cs:16` |
| `ReportingDeploymentComponentDto` | Gap | `src/Meridian.Contracts/Workstation/ReportingDeploymentDtos.cs:6` |
| `ReportingDueScheduleRunResultDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1193` |
| `ReportingEntityScopeKindDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1199` |
| `ReportingFinalityDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1237` |
| `ReportingLedgerBookSelectionDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1259` |
| `ReportingOutputFormatDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1227` |
| `ReportingRunAuditEntryDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1323` |
| `ReportingRunAuditTrailDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1330` |
| `ReportingRunParametersDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1263` |
| `ReportingRunReadinessCheckDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1281` |
| `ReportingRunReadinessDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1295` |
| `ReportingRunReadinessStatusDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1244` |
| `ReportingRunRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1307` |
| `ReportingRunResultDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1320` |
| `ReportingRunScopeDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1251` |
| `ReportingScheduleDeliveryPlanDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1053` |
| `ReportingScheduleDeliveryTargetDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1007` |
| `ReportingScheduleRecordDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1139` |
| `ReportingScheduleRunResultDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1187` |
| `ReportingScheduleStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1096` |
| `ReportingScheduleUpsertRequestDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1169` |
| `ReportingScheduledReleaseHandoffDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1028` |
| `ReportingScheduledReleaseHandoffStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1017` |
| `ReportingStarterKitDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1113` |
| `ReportingStarterKitProvisionResultDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1134` |
| `ReportingStarterKitStateDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1123` |
| `ReportingStarterSeedScheduleDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1103` |
| `ResearchBriefingAlert` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:95` |
| `ResearchBriefingDto` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:132` |
| `ResearchBriefingRun` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:53` |
| `ResearchBriefingWorkspaceSummary` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:118` |
| `ResearchRunDrillInLinks` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:42` |
| `ResearchSavedComparison` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:84` |
| `ResearchSavedComparisonMode` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:73` |
| `ResearchWhatChangedItem` | Gap | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:106` |
| `ResolveBreakCase` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:27` |
| `ResolveReconciliationBreakRequest` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1022` |
| `ResolveSecurityMasterMappings` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:21` |
| `ResolveSourceConflictRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:58` |
| `RestatementCandidateDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:144` |
| `ReviewReconciliationBreakRequest` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1012` |
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
| `SecurityMasterAccountingIssueDto` | Gap | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:332` |
| `SecurityMasterChangeHistoryItemDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:194` |
| `SecurityMasterConflictAssessmentDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:145` |
| `SecurityMasterConflictAuthorityDecision` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:155` |
| `SecurityMasterConflictRecommendationKind` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:41` |
| `SecurityMasterConflictResolutionDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:119` |
| `SecurityMasterDownstreamImpactDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:345` |
| `SecurityMasterEconomicDefinitionDrillInDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:102` |
| `SecurityMasterEditOrigin` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:10` |
| `SecurityMasterEditResultDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:111` |
| `SecurityMasterEntitlementApplicabilityDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:483` |
| `SecurityMasterFactorPointDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:255` |
| `SecurityMasterIdentifierSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:160` |
| `SecurityMasterImpactLinkDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:361` |
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
| `SecurityMasterPublishResultDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:132` |
| `SecurityMasterRecommendedActionDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:368` |
| `SecurityMasterRecommendedActionKind` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:26` |
| `SecurityMasterRevisionPublishedEvent` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:165` |
| `SecurityMasterRevisionStateDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:21` |
| `SecurityMasterScheduleBookDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:219` |
| `SecurityMasterScheduleEventDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:233` |
| `SecurityMasterScheduleProvenanceDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:267` |
| `SecurityMasterScheduleSummaryDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:209` |
| `SecurityMasterSchemaCompatibilityDto` | Gap | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:185` |
| `SecurityMasterSourceCandidateDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:133` |
| `SecurityMasterTrustPostureDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:119` |
| `SecurityMasterTrustSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:48` |
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
| `StatementRunDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:269` |
| `StatementRunExceptionDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:361` |
| `StatementRunReconcileRequestDto` | Gap | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:316` |
| `StatementRunStatus` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:9` |
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
| `StrategyDesignRunBacktestResponse` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:211` |
| `StrategyDesignRunTraceEntry` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:120` |
| `StrategyDesignTemplate` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:61` |
| `StrategyDesignTransition` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:35` |
| `StrategyDesignValidationMessage` | Gap | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:73` |
| `StrategyDesignValidationResult` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:82` |
| `StrategyRunAcceptanceChecklistItemDto` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:296` |
| `StrategyRunAcceptanceChecklistStatusDto` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:284` |
| `StrategyRunArtifactCompleteness` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:674` |
| `StrategyRunCashFlowDigest` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1018` |
| `StrategyRunComparison` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:652` |
| `StrategyRunContinuityDetail` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1096` |
| `StrategyRunContinuityDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1086` |
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
| `StrategyRunEngine` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:22` |
| `StrategyRunEvidenceLoop` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:310` |
| `StrategyRunExecutionSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:135` |
| `StrategyRunGovernanceHook` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:116` |
| `StrategyRunGovernanceSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:176` |
| `StrategyRunHistoryQuery` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:703` |
| `StrategyRunIdentity` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:189` |
| `StrategyRunLineageEventType` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:693` |
| `StrategyRunLineageTimelineEntry` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:736` |
| `StrategyRunLiveStatus` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:66` |
| `StrategyRunMode` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:11` |
| `StrategyRunPaperStatus` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:93` |
| `StrategyRunPromotionState` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:50` |
| `StrategyRunPromotionSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:151` |
| `StrategyRunReviewPacketDto` | Gap | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:385` |
| `StrategyRunStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:35` |
| `StrategyRunSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:205` |
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
| `SubmitForApproval` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:28` |
| `SubmitSecurityMasterRevisionRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:73` |
| `SymbolAttributionEntry` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:909` |
| `SyncCompletenessResult` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:13` |
| `SyncValidationResult` | Gap | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:16` |
| `ThresholdBreachDto` | Gap | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:35` |
| `TradingAcceptanceGateDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:106` |
| `TradingAcceptanceGateStatusDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:30` |
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
| `UpdateSecurityFieldRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:41` |
| `ValidateLedgerDraft` | Gap | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:24` |
| `VersionedReportTemplateIdDto` | Gap | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1366` |
| `WorkflowActionDto` | Documented | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:29` |
| `WorkflowBlockerSummary` | Gap | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:42` |
| `WorkflowDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/WorkflowLibraryDtos.cs:14` |
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
| `WorkstationReportingRunPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:585` |
| `WorkstationReportingTemplatePayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:518` |
| `WorkstationRiskGuardrail` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:373` |
| `WorkstationRunDigest` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:106` |
| `WorkstationRunDrillInLinks` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:29` |
| `WorkstationSecurityCoverageGapPayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:54` |
| `WorkstationSecurityCoveragePayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:60` |
| `WorkstationSecurityCoverageReferencePayload` | Gap | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:37` |
| `WorkstationSecurityCoverageStatus` | Gap | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:486` |
| `WorkstationSecurityReference` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:503` |
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
| `WorkstationTradingPositionRow` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:326` |
| `WorkstationTradingRiskState` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:386` |
| `WorkstationWatchlist` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:29` |
| `WorkstationWorkspaceDefinition` | Gap | `src/Meridian.Contracts/Workstation/WorkstationWorkspaceCatalog.cs:6` |

## Follow-up Queue

- Document or intentionally suppress 372 mapped endpoint gap(s).
- Document or intentionally suppress 800 workstation contract gap(s).

---

*This dashboard is auto-generated. Do not edit manually.*
