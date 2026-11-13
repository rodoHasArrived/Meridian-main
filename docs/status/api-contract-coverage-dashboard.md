# API Contract Coverage Dashboard

_Auto-generated from canonical JSON payload._
_Generated: 1970-01-01T00:00:00+00:00_
Data sources: `src/**/*.cs endpoint mappings`, `src/Meridian.Contracts/Api/UiApiRoutes.cs`, `src/Meridian.Contracts/Workstation/*.cs`, `docs/reference/**/*.md (contract reference documentation only)`


Tracks whether mapped API routes and workstation DTO contracts are visible in the Markdown documentation set.

## Summary

| Metric | Value |
|---|---:|
| Weighted score | 100.0% |
| Endpoint coverage | 100.0% |
| Workstation contract coverage | 100.0% |
| Endpoints documented | 767 / 767 |
| Workstation contracts documented | 1024 / 1024 |

## Endpoint Coverage

| Method | Route | Status | Source |
|---|---|---|---|
| `GET` | `/api/accounting-system/export-packages` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:540` |
| `POST` | `/api/accounting-system/export-packages` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:573` |
| `POST` | `/api/accounting-system/export-packages/certification` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:660` |
| `GET` | `/api/accounting-system/export-packages/{exportPackageId}/manifest` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:622` |
| `GET` | `/api/accounting-system/import/latest` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:404` |
| `POST` | `/api/accounting-system/import/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:379` |
| `GET` | `/api/accounting-system/mapping-profiles` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:462` |
| `POST` | `/api/accounting-system/mapping-profiles` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:491` |
| `GET` | `/api/accounting-system/migration-run-artifacts` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:237` |
| `POST` | `/api/accounting-system/migration-run-artifacts` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:265` |
| `POST` | `/api/accounting-system/migration-runs` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:199` |
| `GET` | `/api/accounting-system/migration-worker-plans` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:307` |
| `POST` | `/api/accounting-system/migration-worker-plans` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:337` |
| `GET` | `/api/accounting-system/production-certification-profile` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:128` |
| `POST` | `/api/accounting-system/production-certification-profile` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:155` |
| `POST` | `/api/accounting-system/production-readiness` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:39` |
| `GET` | `/api/accounting-system/providers` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:24` |
| `GET` | `/api/accounting-system/reconciliation/latest` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:433` |
| `GET` | `/api/accounting-system/tenant-administration-profile` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:62` |
| `POST` | `/api/accounting-system/tenant-administration-profile` | Documented | `src/Meridian.Ui.Shared/Endpoints/AccountingSystemEndpoints.cs:86` |
| `GET` | `/api/accounting/onboarding/workspaces` | Documented | `src/Meridian.Ui.Shared/Endpoints/OnboardingEndpoints.cs:46` |
| `POST` | `/api/accounting/onboarding/workspaces` | Documented | `src/Meridian.Ui.Shared/Endpoints/OnboardingEndpoints.cs:52` |
| `GET` | `/api/accounting/onboarding/workspaces/{workspaceId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/OnboardingEndpoints.cs:76` |
| `POST` | `/api/accounting/onboarding/workspaces/{workspaceId:guid}/comparisons` | Documented | `src/Meridian.Ui.Shared/Endpoints/OnboardingEndpoints.cs:100` |
| `POST` | `/api/accounting/onboarding/workspaces/{workspaceId:guid}/comparisons/{comparisonId}/replay` | Documented | `src/Meridian.Ui.Shared/Endpoints/OnboardingEndpoints.cs:156` |
| `POST` | `/api/accounting/onboarding/workspaces/{workspaceId:guid}/criteria` | Documented | `src/Meridian.Ui.Shared/Endpoints/OnboardingEndpoints.cs:92` |
| `POST` | `/api/accounting/onboarding/workspaces/{workspaceId:guid}/differences/{differenceKey}/assignment` | Documented | `src/Meridian.Ui.Shared/Endpoints/OnboardingEndpoints.cs:108` |
| `POST` | `/api/accounting/onboarding/workspaces/{workspaceId:guid}/packets` | Documented | `src/Meridian.Ui.Shared/Endpoints/OnboardingEndpoints.cs:128` |
| `GET` | `/api/accounting/onboarding/workspaces/{workspaceId:guid}/packets/{packetId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/OnboardingEndpoints.cs:136` |
| `GET` | `/api/accounting/onboarding/workspaces/{workspaceId:guid}/packets/{packetId:guid}/download` | Documented | `src/Meridian.Ui.Shared/Endpoints/OnboardingEndpoints.cs:145` |
| `POST` | `/api/accounting/onboarding/workspaces/{workspaceId:guid}/reviews` | Documented | `src/Meridian.Ui.Shared/Endpoints/OnboardingEndpoints.cs:118` |
| `GET` | `/api/accounting/onboarding/workspaces/{workspaceId:guid}/sources` | Documented | `src/Meridian.Ui.Shared/Endpoints/OnboardingEndpoints.cs:83` |
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
| `GET` | `/api/admin/retention/compliance-report` | Documented | `src/Meridian.Ui.Shared/Endpoints/ResilienceEndpoints.cs:89` |
| `DELETE` | `/api/admin/retention/{policyId}/delete` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:199` |
| `POST` | `/api/admin/selftest` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:323` |
| `GET` | `/api/admin/show-config` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:362` |
| `POST` | `/api/admin/storage/migrate/{targetTier}` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:116` |
| `GET` | `/api/admin/storage/permissions` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:289` |
| `GET` | `/api/admin/storage/tiers` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:101` |
| `GET` | `/api/admin/storage/usage` | Documented | `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs:138` |
| `POST` | `/api/alignment/create` | Documented | `src/Meridian.Ui.Shared/Endpoints/HistoricalEndpoints.cs:178` |
| `POST` | `/api/alignment/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/HistoricalEndpoints.cs:184` |
| `GET` | `/api/analytics/anomalies` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:89` |
| `GET` | `/api/analytics/compare` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:53` |
| `GET` | `/api/analytics/completeness` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:115` |
| `GET` | `/api/analytics/gaps` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:25` |
| `POST` | `/api/analytics/gaps/repair` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:37` |
| `GET` | `/api/analytics/latency` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:65` |
| `GET` | `/api/analytics/latency/stats` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:77` |
| `GET` | `/api/analytics/quality-report` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:103` |
| `GET` | `/api/analytics/rate-limits` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:145` |
| `GET` | `/api/analytics/throughput` | Documented | `src/Meridian.Ui.Shared/Endpoints/AnalyticsEndpoints.cs:133` |
| `GET` | `/api/auth/access-assignments` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:503` |
| `POST` | `/api/auth/access-assignments` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:544` |
| `POST` | `/api/auth/access-assignments/{assignmentId}/revoke` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:584` |
| `GET` | `/api/auth/accounts` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:215` |
| `PUT` | `/api/auth/accounts/{username}` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:238` |
| `POST` | `/api/auth/accounts/{username}/disable` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:336` |
| `POST` | `/api/auth/accounts/{username}/password-reset` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:276` |
| `GET` | `/api/auth/audit` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:427` |
| `POST` | `/api/auth/bootstrap` | Documented | `src/Meridian.Ui.Shared/Endpoints/InitialAccountBootstrapEndpoints.cs:22` |
| `GET` | `/api/auth/desktop-launch/{ticket}` | Documented | `src/Meridian.Ui.Shared/Endpoints/FirstRunEndpoints.cs:56` |
| `POST` | `/api/auth/login` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:39` |
| `POST` | `/api/auth/logout` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:151` |
| `GET` | `/api/auth/me` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:174` |
| `POST` | `/api/auth/role-profiles` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:451` |
| `GET` | `/api/auth/roles` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:200` |
| `POST` | `/api/auth/sessions/revoke` | Documented | `src/Meridian.Ui.Shared/Endpoints/AuthEndpoints.cs:396` |
| `GET` | `/api/backfill/checkpoints` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:26` |
| `GET` | `/api/backfill/checkpoints/resumable` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:92` |
| `GET` | `/api/backfill/checkpoints/validation` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:38` |
| `GET` | `/api/backfill/checkpoints/{jobId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:119` |
| `GET` | `/api/backfill/checkpoints/{jobId}/pending` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:145` |
| `POST` | `/api/backfill/checkpoints/{jobId}/resume` | Documented | `src/Meridian.Ui.Shared/Endpoints/CheckpointEndpoints.cs:205` |
| `GET` | `/api/backfill/completeness` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:214` |
| `POST` | `/api/backfill/cost-estimate` | Documented | `src/Meridian.Ui.Shared/Endpoints/ResilienceEndpoints.cs:49` |
| `GET` | `/api/backfill/executions` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:173` |
| `POST` | `/api/backfill/gap-fill` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:89` |
| `GET` | `/api/backfill/gaps` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:166` |
| `GET` | `/api/backfill/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:31` |
| `GET` | `/api/backfill/presets` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:157` |
| `GET` | `/api/backfill/progress` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:190` |
| `GET` | `/api/backfill/providers` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:38` |
| `GET` | `/api/backfill/providers/audit` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:293` |
| `POST` | `/api/backfill/providers/dry-run-plan` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:263` |
| `GET` | `/api/backfill/providers/fallback-chain` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:245` |
| `GET` | `/api/backfill/providers/metadata` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:217` |
| `GET` | `/api/backfill/providers/statuses` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:228` |
| `GET` | `/api/backfill/resolve/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:63` |
| `POST` | `/api/backfill/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:127` |
| `POST` | `/api/backfill/run/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:71` |
| `GET` | `/api/backfill/schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:236` |
| `POST` | `/api/backfill/schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:258` |
| `GET` | `/api/backfill/schedules/templates` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:471` |
| `DELETE` | `/api/backfill/schedules/{id}` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:321` |
| `GET` | `/api/backfill/schedules/{id}` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:300` |
| `POST` | `/api/backfill/schedules/{id}/disable` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:367` |
| `POST` | `/api/backfill/schedules/{id}/enable` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:344` |
| `GET` | `/api/backfill/schedules/{id}/history` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:452` |
| `POST` | `/api/backfill/schedules/{id}/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:390` |
| `GET` | `/api/backfill/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs:196` |
| `GET` | `/api/backfill/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs:53` |
| `GET` | `/api/backfill/validation` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:26` |
| `GET` | `/api/backfill/validation/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/BackfillValidationEndpoints.cs:111` |
| `GET` | `/api/backpressure` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:159` |
| `GET` | `/api/calendar/holidays` | Documented | `src/Meridian.Ui.Shared/Endpoints/CalendarEndpoints.cs:64` |
| `GET` | `/api/calendar/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/CalendarEndpoints.cs:21` |
| `GET` | `/api/calendar/trading-days` | Documented | `src/Meridian.Ui.Shared/Endpoints/CalendarEndpoints.cs:89` |
| `GET` | `/api/canonicalization/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:113` |
| `GET` | `/api/canonicalization/parity` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:48` |
| `GET` | `/api/canonicalization/parity/{provider}` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:76` |
| `GET` | `/api/canonicalization/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs:21` |
| `GET` | `/api/catalog/coverage` | Documented | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:233` |
| `GET` | `/api/catalog/search` | Documented | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:25` |
| `GET` | `/api/catalog/symbols` | Documented | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:129` |
| `GET` | `/api/catalog/timeline` | Documented | `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs:161` |
| `GET` | `/api/compliance/access-reviews` | Documented | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:126` |
| `POST` | `/api/compliance/access-reviews/assess` | Documented | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:88` |
| `POST` | `/api/compliance/access-reviews/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:104` |
| `POST` | `/api/compliance/actions/evaluate` | Documented | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:51` |
| `POST` | `/api/compliance/approval-requests` | Documented | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:18` |
| `POST` | `/api/compliance/approval-requests/{approvalRequestId}/decisions` | Documented | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:29` |
| `GET` | `/api/compliance/audit/extract` | Documented | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:69` |
| `GET` | `/api/compliance/controls/attestation` | Documented | `src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs:73` |
| `GET` | `/api/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:36` |
| `POST` | `/api/config/alpaca` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:135` |
| `GET` | `/api/config/data-sources` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:684` |
| `POST` | `/api/config/data-sources` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:702` |
| `POST` | `/api/config/datasource` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:120` |
| `GET` | `/api/config/datasources` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:84` |
| `POST` | `/api/config/datasources` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:103` |
| `POST` | `/api/config/datasources/defaults` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:235` |
| `POST` | `/api/config/datasources/failover` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:269` |
| `GET` | `/api/config/derivatives` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:215` |
| `POST` | `/api/config/derivatives` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:224` |
| `GET` | `/api/config/effective` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:63` |
| `POST` | `/api/config/storage` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:147` |
| `POST` | `/api/config/symbols` | Documented | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs:174` |
| `GET` | `/api/connections` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:185` |
| `GET` | `/api/data/bbo/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:270` |
| `GET` | `/api/data/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:351` |
| `GET` | `/api/data/l3-orderbook/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:200` |
| `GET` | `/api/data/orderbook/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:128` |
| `GET` | `/api/data/orderflow/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:310` |
| `GET` | `/api/data/quotes-snapshot` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:110` |
| `GET` | `/api/data/quotes/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:66` |
| `GET` | `/api/data/trades/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs:26` |
| `GET` | `/api/demo/historical/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:150` |
| `GET` | `/api/demo/market-data/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:129` |
| `GET` | `/api/demo/mode` | Documented | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:80` |
| `GET` | `/api/demo/symbols` | Documented | `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:113` |
| `POST` | `/api/dev/seed/bank-transactions` | Documented | `src/Meridian.Ui.Shared/Endpoints/BankingEndpoints.cs:339` |
| `GET` | `/api/diagnostics/bundle` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:150` |
| `GET` | `/api/diagnostics/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:124` |
| `GET` | `/api/diagnostics/coordination` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:502` |
| `POST` | `/api/diagnostics/dry-run` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:36` |
| `GET` | `/api/diagnostics/error-codes` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:381` |
| `GET` | `/api/diagnostics/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:171` |
| `GET` | `/api/diagnostics/providers` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:57` |
| `POST` | `/api/diagnostics/providers/{providerName}/test` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:300` |
| `GET` | `/api/diagnostics/quick-check` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:340` |
| `POST` | `/api/diagnostics/selftest` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:391` |
| `GET` | `/api/diagnostics/show-config` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:357` |
| `GET` | `/api/diagnostics/storage` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:74` |
| `POST` | `/api/diagnostics/test-connectivity` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:432` |
| `POST` | `/api/diagnostics/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:277` |
| `POST` | `/api/diagnostics/validate-config` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:472` |
| `POST` | `/api/diagnostics/validate-credentials` | Documented | `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs:413` |
| `GET` | `/api/errors` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:145` |
| `GET` | `/api/events/stream` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:251` |
| `POST` | `/api/export/analysis` | Documented | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:82` |
| `GET` | `/api/export/formats` | Documented | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:159` |
| `POST` | `/api/export/integrity` | Documented | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:302` |
| `POST` | `/api/export/orderflow` | Documented | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:237` |
| `GET` | `/api/export/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:29` |
| `POST` | `/api/export/quality-report` | Documented | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:220` |
| `POST` | `/api/export/research-package` | Documented | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:431` |
| `POST` | `/api/export/strategy-package` | Documented | `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs:423` |
| `GET` | `/api/failover/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:35` |
| `POST` | `/api/failover/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:65` |
| `GET` | `/api/failover/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:337` |
| `GET` | `/api/failover/rules` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:114` |
| `POST` | `/api/failover/rules` | Documented | `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs:135` |
| `GET` | `/api/fund-accounts/brokerage-sync/accounts` | Documented | `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs:253` |
| `GET` | `/api/fund-accounts/{accountId:guid}/brokerage-sync/activity` | Documented | `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs:372` |
| `GET` | `/api/fund-accounts/{accountId:guid}/brokerage-sync/positions` | Documented | `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs:356` |
| `GET` | `/api/fund-accounts/{accountId:guid}/brokerage-sync/reconciliation/latest` | Documented | `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs:436` |
| `GET` | `/api/fund-accounts/{accountId:guid}/brokerage-sync/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs:297` |
| `GET` | `/api/fund-structure/entities/{entityId:guid}/timeline` | Documented | `src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs:29` |
| `GET` | `/api/funds/{fundId:guid}/accounts` | Documented | `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs:137` |
| `GET` | `/api/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:48` |
| `GET` | `/api/health/detailed` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:223` |
| `GET` | `/api/health/diagnostics/bundle` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:259` |
| `GET` | `/api/health/events` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:189` |
| `GET` | `/api/health/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:202` |
| `GET` | `/api/health/providers` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:56` |
| `GET` | `/api/health/providers/{provider}/diagnostics` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:104` |
| `POST` | `/api/health/providers/{provider}/test` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:221` |
| `GET` | `/api/health/storage` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:154` |
| `GET` | `/api/health/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs:29` |
| `GET` | `/api/indices/{indexName}/constituents` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:586` |
| `GET` | `/api/ingestion/jobs` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:30` |
| `POST` | `/api/ingestion/jobs` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:82` |
| `GET` | `/api/ingestion/jobs/resumable` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:131` |
| `GET` | `/api/ingestion/jobs/{jobId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:56` |
| `GET` | `/api/ingestion/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs:146` |
| `POST` | `/api/journals/{journalEntryId:guid}/post` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1173` |
| `GET` | `/api/lean/algorithms` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:159` |
| `GET` | `/api/lean/auto-export` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:308` |
| `POST` | `/api/lean/auto-export/configure` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:339` |
| `GET` | `/api/lean/backtest/history` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:253` |
| `POST` | `/api/lean/backtest/start` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:211` |
| `DELETE` | `/api/lean/backtest/{backtestId}/delete` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:294` |
| `GET` | `/api/lean/backtest/{backtestId}/results` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:229` |
| `GET` | `/api/lean/backtest/{backtestId}/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:221` |
| `POST` | `/api/lean/backtest/{backtestId}/stop` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:242` |
| `GET` | `/api/lean/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:69` |
| `POST` | `/api/lean/results/ingest` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:395` |
| `GET` | `/api/lean/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:36` |
| `GET` | `/api/lean/symbol-map` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:507` |
| `POST` | `/api/lean/sync` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:194` |
| `GET` | `/api/lean/sync/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:204` |
| `POST` | `/api/lean/verify` | Documented | `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:96` |
| `GET` | `/api/ledger/accounting-configuration` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:15` |
| `POST` | `/api/ledger/accounting-configuration/activate` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:622` |
| `POST` | `/api/ledger/accounting-configuration/asset-accounting/events/lifecycle` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:377` |
| `POST` | `/api/ledger/accounting-configuration/asset-accounting/events/project` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:322` |
| `GET` | `/api/ledger/accounting-configuration/audit` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:657` |
| `POST` | `/api/ledger/accounting-configuration/chart` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:43` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:105` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/candidates` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:279` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/candidates/asset-accounting` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:418` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/candidates/post` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:478` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/dry-run` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:238` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/projection-sets` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:538` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/promotion-approvals` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:136` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/test-cases` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:172` |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/tests` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:581` |
| `POST` | `/api/ledger/accounting-configuration/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:203` |
| `POST` | `/api/ledger/accounting-configuration/templates` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountingConfiguration.cs:74` |
| `GET` | `/api/ledger/aggregates/{aggregateId:guid}/journal-entries` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:333` |
| `GET` | `/api/ledger/audit-integrity` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AuditIntegrity.cs:34` |
| `GET` | `/api/ledger/books` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:40` |
| `POST` | `/api/ledger/books` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:92` |
| `POST` | `/api/ledger/books/rollout-assessment` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:123` |
| `GET` | `/api/ledger/books/{ledgerBookId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:69` |
| `GET` | `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/cash-forecasts` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountOversight.cs:192` |
| `POST` | `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/cash-forecasts` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountOversight.cs:171` |
| `GET` | `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/cash-forecasts/{snapshotId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountOversight.cs:203` |
| `GET` | `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/cash-reconciliation/cases` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountOversight.cs:82` |
| `POST` | `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/cash-reconciliation/cases/{caseId}/assignments` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountOversight.cs:104` |
| `POST` | `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/cash-reconciliation/runs` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountOversight.cs:66` |
| `GET` | `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/cash-reconciliation/runs/{runId}/replay` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountOversight.cs:93` |
| `GET` | `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/close-price-sets/{valuationDate}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountOversight.cs:130` |
| `POST` | `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/close-price-sets/{valuationDate}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountOversight.cs:116` |
| `GET` | `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/close-price-sets/{valuationDate}/{snapshotId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountOversight.cs:142` |
| `POST` | `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/close-price-sets/{valuationDate}/{snapshotId:guid}/approval` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountOversight.cs:155` |
| `GET` | `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/oversight/context` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountOversight.cs:39` |
| `POST` | `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/oversight/provision` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountOversight.cs:52` |
| `POST` | `/api/ledger/books/{ledgerBookId:guid}/factor-paydowns/draft` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.FactorPaydown.cs:77` |
| `GET` | `/api/ledger/books/{ledgerBookId:guid}/factor-paydowns/drafts/{journalEntryId:guid}/capabilities` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.FactorPaydown.cs:25` |
| `POST` | `/api/ledger/books/{ledgerBookId:guid}/factor-paydowns/prepare` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.FactorPaydown.cs:58` |
| `POST` | `/api/ledger/books/{ledgerBookId:guid}/open-lots/backfill/apply` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.OpenLotBackfill.cs:112` |
| `POST` | `/api/ledger/books/{ledgerBookId:guid}/open-lots/backfill/evidence` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.OpenLotBackfill.cs:73` |
| `GET` | `/api/ledger/books/{ledgerBookId:guid}/open-lots/backfill/evidence/{evidenceRecordId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.OpenLotBackfill.cs:51` |
| `POST` | `/api/ledger/books/{ledgerBookId:guid}/open-lots/backfill/evidence/{evidenceRecordId:guid}/review` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.OpenLotBackfill.cs:91` |
| `GET` | `/api/ledger/books/{ledgerBookId:guid}/open-lots/backfill/exceptions` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.OpenLotBackfill.cs:18` |
| `POST` | `/api/ledger/books/{ledgerBookId:guid}/open-lots/backfill/survey` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.OpenLotBackfill.cs:34` |
| `GET` | `/api/ledger/books/{ledgerBookId:guid}/periods/{periodId:guid}/comparison` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.PeriodComparison.cs:19` |
| `POST` | `/api/ledger/close-management/evidence-review` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:866` |
| `POST` | `/api/ledger/close-management/late-adjustments` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:671` |
| `POST` | `/api/ledger/close-management/late-adjustments/review` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:733` |
| `POST` | `/api/ledger/close-management/period-lock` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:928` |
| `POST` | `/api/ledger/close-management/period-plan/configuration` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:609` |
| `GET` | `/api/ledger/close-management/period-plan/{workflowId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:578` |
| `POST` | `/api/ledger/close-management/period-reopen` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:941` |
| `POST` | `/api/ledger/close-management/prepare/create` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.ClosePreparation.cs:80` |
| `POST` | `/api/ledger/close-management/prepare/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.ClosePreparation.cs:61` |
| `POST` | `/api/ledger/close-management/task-signoffs` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:804` |
| `GET` | `/api/ledger/close-management/templates` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.ClosePreparation.cs:19` |
| `POST` | `/api/ledger/close-management/templates` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.ClosePreparation.cs:42` |
| `GET` | `/api/ledger/close-management/templates/{templateId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.ClosePreparation.cs:31` |
| `POST` | `/api/ledger/consolidation/drafts` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.Consolidation.cs:26` |
| `GET` | `/api/ledger/consolidation/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.Consolidation.cs:17` |
| `POST` | `/api/ledger/controlled-close/authorizations` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.EntityControls.cs:46` |
| `GET` | `/api/ledger/fixed-assets` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.RecurringJournals.cs:28` |
| `POST` | `/api/ledger/fixed-assets` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.RecurringJournals.cs:51` |
| `GET` | `/api/ledger/intercompany/account-bindings` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.IntercompanyAccountBindings.cs:16` |
| `POST` | `/api/ledger/intercompany/account-bindings` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.IntercompanyAccountBindings.cs:54` |
| `GET` | `/api/ledger/intercompany/account-bindings/history` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.IntercompanyAccountBindings.cs:35` |
| `GET` | `/api/ledger/intercompany/balance-schedule` | Documented | `src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs:56` |
| `GET` | `/api/ledger/intercompany/breaks` | Documented | `src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs:105` |
| `GET` | `/api/ledger/intercompany/capabilities` | Documented | `src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs:19` |
| `GET` | `/api/ledger/intercompany/legacy-attributions` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.LegacyIntercompanyAttributions.cs:16` |
| `POST` | `/api/ledger/intercompany/legacy-attributions` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.LegacyIntercompanyAttributions.cs:49` |
| `GET` | `/api/ledger/intercompany/legacy-attributions/candidates` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.LegacyIntercompanyAttributions.cs:26` |
| `GET` | `/api/ledger/intercompany/proposals` | Documented | `src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs:46` |
| `POST` | `/api/ledger/intercompany/proposals/{proposalId:guid}/link` | Documented | `src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs:136` |
| `POST` | `/api/ledger/intercompany/proposals/{proposalId:guid}/refresh` | Documented | `src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs:87` |
| `GET` | `/api/ledger/intercompany/proposals/{proposalId:guid}/refresh-preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs:72` |
| `POST` | `/api/ledger/intercompany/proposals/{proposalId:guid}/retry` | Documented | `src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs:116` |
| `POST` | `/api/ledger/journal-automation/capital-call-default-interest-intake` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:734` |
| `POST` | `/api/ledger/journal-automation/capital-call-funding-intake` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:691` |
| `POST` | `/api/ledger/journal-automation/capital-call-issuance-intake` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:648` |
| `POST` | `/api/ledger/journal-automation/daily-mark-to-market-batch-lifecycle` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:388` |
| `POST` | `/api/ledger/journal-automation/daily-mark-to-market-intake` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:517` |
| `POST` | `/api/ledger/journal-automation/daily-mark-to-market-preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:442` |
| `POST` | `/api/ledger/journal-automation/daily-mark-to-market-run-due` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:358` |
| `GET` | `/api/ledger/journal-automation/daily-mark-to-market-schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:237` |
| `POST` | `/api/ledger/journal-automation/daily-mark-to-market-schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:268` |
| `POST` | `/api/ledger/journal-automation/distribution-intake` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:777` |
| `POST` | `/api/ledger/journal-automation/dividend-intake` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:560` |
| `POST` | `/api/ledger/journal-automation/fee-accrual-intake` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:603` |
| `GET` | `/api/ledger/journal-automation/monthly-schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:25` |
| `POST` | `/api/ledger/journal-automation/monthly-schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:61` |
| `POST` | `/api/ledger/journal-automation/monthly-schedules/run-due` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:207` |
| `POST` | `/api/ledger/journal-automation/period-close-intake` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs:820` |
| `POST` | `/api/ledger/journal-automation/recurring/initialize` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.RecurringJournals.cs:108` |
| `GET` | `/api/ledger/journal-automation/recurring/occurrences` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.RecurringJournals.cs:83` |
| `POST` | `/api/ledger/journal-automation/recurring/restore-definitions` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.RecurringJournals.cs:179` |
| `POST` | `/api/ledger/journal-automation/recurring/schedules` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.RecurringJournals.cs:126` |
| `GET` | `/api/ledger/journal-entry-workbench` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1216` |
| `POST` | `/api/ledger/journal-entry-workbench/drafts` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1537` |
| `POST` | `/api/ledger/journal-entry-workbench/evidence` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1689` |
| `POST` | `/api/ledger/journal-entry-workbench/lifecycle-action` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1733` |
| `POST` | `/api/ledger/journal-entry-workbench/submit-approval` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1642` |
| `POST` | `/api/ledger/journal-entry-workbench/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1596` |
| `GET` | `/api/ledger/periods` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:164` |
| `POST` | `/api/ledger/periods` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:202` |
| `GET` | `/api/ledger/periods/{periodId:guid}/accounts/running-balance` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.RunningBalance.cs:21` |
| `POST` | `/api/ledger/periods/{periodId:guid}/close` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:234` |
| `GET` | `/api/ledger/periods/{periodId:guid}/journal-entries` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:287` |
| `GET` | `/api/ledger/periods/{periodId:guid}/journal-entries/{journalEntryId:guid}/tax-results` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.Tax.cs:17` |
| `GET` | `/api/ledger/periods/{periodId:guid}/pnl-summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:432` |
| `GET` | `/api/ledger/periods/{periodId:guid}/trial-balance` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:382` |
| `GET` | `/api/ledger/periods/{periodId:guid}/trial-balance-report` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:407` |
| `GET` | `/api/ledger/private-capital/activity` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1242` |
| `GET` | `/api/ledger/private-capital/capital-account-subledger` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1358` |
| `GET` | `/api/ledger/private-capital/capital-account-workbench` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1490` |
| `GET` | `/api/ledger/private-capital/fund-event-command-center` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1317` |
| `GET` | `/api/ledger/private-capital/fund-event-record` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1274` |
| `GET` | `/api/ledger/private-capital/report-output` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1419` |
| `POST` | `/api/ledger/reports/accounting-package` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1013` |
| `POST` | `/api/ledger/reports/accounting-package/certification` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1064` |
| `GET` | `/api/ledger/reports/accounting-packages` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1123` |
| `GET` | `/api/ledger/reports/accounting-packages/{packageId}/exports/{artifactId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:1165` |
| `GET` | `/api/ledger/reports/pnl-summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:516` |
| `GET` | `/api/ledger/reports/trial-balance` | Documented | `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs:457` |
| `GET` | `/api/loans/portfolio` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1334` |
| `POST` | `/api/loans/rebuild-all` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1345` |
| `GET` | `/api/loans/rebuild-checkpoints` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1325` |
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
| `GET` | `/api/maintenance/schedules/{id}` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:50` |
| `DELETE` | `/api/maintenance/schedules/{id}/delete` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:60` |
| `POST` | `/api/maintenance/schedules/{id}/disable` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:88` |
| `POST` | `/api/maintenance/schedules/{id}/enable` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:74` |
| `GET` | `/api/maintenance/schedules/{id}/history` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:131` |
| `POST` | `/api/maintenance/schedules/{id}/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs:102` |
| `DELETE` | `/api/maintenance/schedules/{scheduleId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:99` |
| `PUT` | `/api/maintenance/schedules/{scheduleId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:37` |
| `GET` | `/api/maintenance/schedules/{scheduleId}/executions` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:213` |
| `GET` | `/api/maintenance/schedules/{scheduleId}/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:256` |
| `POST` | `/api/maintenance/schedules/{scheduleId}/trigger` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:119` |
| `GET` | `/api/maintenance/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:271` |
| `GET` | `/api/maintenance/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:299` |
| `GET` | `/api/maintenance/task-types` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:410` |
| `POST` | `/api/maintenance/validate-cron` | Documented | `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:313` |
| `GET` | `/api/messaging/activity` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:97` |
| `GET` | `/api/messaging/config` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:30` |
| `GET` | `/api/messaging/consumers` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:126` |
| `GET` | `/api/messaging/endpoints` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:145` |
| `GET` | `/api/messaging/errors` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:316` |
| `POST` | `/api/messaging/errors/{messageId}/retry` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:337` |
| `GET` | `/api/messaging/publishing` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:273` |
| `POST` | `/api/messaging/queues/{queueName}/purge` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:290` |
| `GET` | `/api/messaging/stats` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:69` |
| `GET` | `/api/messaging/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:51` |
| `POST` | `/api/messaging/test` | Documented | `src/Meridian.Ui.Shared/Endpoints/MessagingEndpoints.cs:172` |
| `GET` | `/api/options/chains/{underlyingSymbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:78` |
| `GET` | `/api/options/expirations/{underlyingSymbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:28` |
| `GET` | `/api/options/quotes/{underlyingSymbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:136` |
| `POST` | `/api/options/refresh` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:202` |
| `GET` | `/api/options/strikes/{underlyingSymbol}/{expiration}` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:51` |
| `GET` | `/api/options/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:156` |
| `GET` | `/api/options/underlyings` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionsEndpoints.cs:184` |
| `GET` | `/api/packaging/contents` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:171` |
| `POST` | `/api/packaging/create` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:34` |
| `GET` | `/api/packaging/download/{fileName}` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:290` |
| `POST` | `/api/packaging/import` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:88` |
| `GET` | `/api/packaging/list` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:204` |
| `POST` | `/api/packaging/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:137` |
| `DELETE` | `/api/packaging/{fileName}` | Documented | `src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs:249` |
| `GET` | `/api/plaid/accounts` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:35` |
| `GET` | `/api/plaid/institutions/search` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:48` |
| `GET` | `/api/plaid/items` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:22` |
| `POST` | `/api/plaid/items/{itemId}/sync` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:128` |
| `POST` | `/api/plaid/link-token` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:78` |
| `POST` | `/api/plaid/public-token/exchange` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:103` |
| `POST` | `/api/plaid/transfers/sandbox` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:214` |
| `POST` | `/api/plaid/webhook` | Documented | `src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:158` |
| `GET` | `/api/portfolio/household` | Documented | `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs:276` |
| `GET` | `/api/projections/{projectionRunId:guid}/flows` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1156` |
| `GET` | `/api/provider-routing/bindings` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:41` |
| `GET` | `/api/provider-routing/connections` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:22` |
| `POST` | `/api/provider-routing/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:79` |
| `GET` | `/api/provider-routing/trust-snapshots` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderRoutingEndpoints.cs:60` |
| `GET` | `/api/providers/capabilities` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:148` |
| `GET` | `/api/providers/capability-matrix` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:178` |
| `GET` | `/api/providers/catalog` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:625` |
| `GET` | `/api/providers/catalog/{providerId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:664` |
| `GET` | `/api/providers/comparison` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:343` |
| `POST` | `/api/providers/configure` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:302` |
| `GET` | `/api/providers/connections` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:22` |
| `GET` | `/api/providers/dashboard` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:305` |
| `GET` | `/api/providers/data-projection` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderDataProjectionEndpoints.cs:16` |
| `GET` | `/api/providers/failover` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:57` |
| `GET` | `/api/providers/failover-thresholds` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:230` |
| `POST` | `/api/providers/failover/reset` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:102` |
| `POST` | `/api/providers/failover/trigger` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:85` |
| `GET` | `/api/providers/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:247` |
| `GET` | `/api/providers/ib/error-codes` | Documented | `src/Meridian.Ui.Shared/Endpoints/IBEndpoints.cs:92` |
| `GET` | `/api/providers/ib/limits` | Documented | `src/Meridian.Ui.Shared/Endpoints/IBEndpoints.cs:114` |
| `GET` | `/api/providers/ib/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/IBEndpoints.cs:25` |
| `GET` | `/api/providers/latency` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:170` |
| `GET` | `/api/providers/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:543` |
| `GET` | `/api/providers/modules` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:24` |
| `POST` | `/api/providers/modules` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:57` |
| `GET` | `/api/providers/modules/catalogue` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:41` |
| `DELETE` | `/api/providers/modules/{moduleId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:111` |
| `PUT` | `/api/providers/modules/{moduleId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:83` |
| `PUT` | `/api/providers/modules/{moduleId}/enabled` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:136` |
| `POST` | `/api/providers/modules/{moduleId}/test` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:159` |
| `GET` | `/api/providers/rate-limits` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:118` |
| `GET` | `/api/providers/readiness` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:416` |
| `POST` | `/api/providers/restart` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderModuleEndpoints.cs:197` |
| `GET` | `/api/providers/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs:428` |
| `POST` | `/api/providers/switch` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:167` |
| `DELETE` | `/api/providers/{providerId}/credentials` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:135` |
| `PUT` | `/api/providers/{providerId}/credentials` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:68` |
| `POST` | `/api/providers/{providerId}/verify` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs:106` |
| `GET` | `/api/providers/{providerName}` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:27` |
| `GET` | `/api/providers/{providerName}/rate-limit-history` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:131` |
| `POST` | `/api/providers/{providerName}/test` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs:193` |
| `POST` | `/api/providers/{provider}/test-connection` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderCredentialEndpoints.cs:69` |
| `POST` | `/api/providers/{provider}/validate-credentials` | Documented | `src/Meridian.Ui.Shared/Endpoints/ProviderCredentialEndpoints.cs:26` |
| `GET` | `/api/quality/anomalies` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:246` |
| `GET` | `/api/quality/anomalies/stale` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:290` |
| `GET` | `/api/quality/anomalies/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:287` |
| `GET` | `/api/quality/anomalies/unacknowledged` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:275` |
| `POST` | `/api/quality/anomalies/{anomalyId}/acknowledge` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:278` |
| `GET` | `/api/quality/anomalies/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:272` |
| `GET` | `/api/quality/comparison/discrepancies` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:328` |
| `GET` | `/api/quality/comparison/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:340` |
| `GET` | `/api/quality/comparison/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:321` |
| `GET` | `/api/quality/completeness` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:85` |
| `GET` | `/api/quality/completeness/low` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:110` |
| `GET` | `/api/quality/completeness/summary` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:107` |
| `GET` | `/api/quality/completeness/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:92` |
| `GET` | `/api/quality/dashboard` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:69` |
| `GET` | `/api/quality/drops` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:292` |
| `GET` | `/api/quality/drops/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs:317` |
| `GET` | `/api/quality/errors` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:216` |
| `GET` | `/api/quality/errors/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:235` |
| `GET` | `/api/quality/errors/top-symbols` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:238` |
| `GET` | `/api/quality/errors/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:228` |
| `GET` | `/api/quality/gaps` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:119` |
| `GET` | `/api/quality/gaps/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:207` |
| `GET` | `/api/quality/gaps/timeline/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:199` |
| `GET` | `/api/quality/gaps/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:131` |
| `POST` | `/api/quality/gaps/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:138` |
| `GET` | `/api/quality/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:387` |
| `GET` | `/api/quality/health/unhealthy` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:420` |
| `GET` | `/api/quality/health/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:411` |
| `GET` | `/api/quality/latency` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:295` |
| `GET` | `/api/quality/latency/high` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:313` |
| `GET` | `/api/quality/latency/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:310` |
| `GET` | `/api/quality/latency/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:298` |
| `GET` | `/api/quality/latency/{symbol}/histogram` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:307` |
| `GET` | `/api/quality/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:80` |
| `GET` | `/api/quality/reports/daily` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:345` |
| `POST` | `/api/quality/reports/export` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:372` |
| `GET` | `/api/quality/reports/weekly` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:353` |
| `POST` | `/api/quant/parameters` | Documented | `src/Meridian.Ui.Shared/Endpoints/QuantLabEndpoints.cs:92` |
| `POST` | `/api/quant/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/QuantLabEndpoints.cs:39` |
| `GET` | `/api/quant/templates` | Documented | `src/Meridian.Ui.Shared/Endpoints/QuantLabEndpoints.cs:140` |
| `GET` | `/api/reconciliation/exceptions` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1239` |
| `POST` | `/api/reconciliation/exceptions/{exceptionId:guid}/resolve` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1248` |
| `GET` | `/api/reconciliation/{runId:guid}/results` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1230` |
| `POST` | `/api/reference-data/bonds/cutover` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondMigrationEndpoints.cs:75` |
| `GET` | `/api/reference-data/bonds/cutover-readiness` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondMigrationEndpoints.cs:69` |
| `GET` | `/api/reference-data/bonds/issuer-ladder` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:73` |
| `GET` | `/api/reference-data/bonds/issuer-ladder/page` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondReferencePagingEndpoints.cs:19` |
| `GET` | `/api/reference-data/bonds/maturity-ladder` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:97` |
| `GET` | `/api/reference-data/bonds/maturity-ladder/page` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondReferencePagingEndpoints.cs:27` |
| `POST` | `/api/reference-data/bonds/migration-preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondMigrationEndpoints.cs:20` |
| `POST` | `/api/reference-data/bonds/migration-preview/batch` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondMigrationEndpoints.cs:27` |
| `POST` | `/api/reference-data/bonds/migration-submit` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondMigrationEndpoints.cs:47` |
| `POST` | `/api/reference-data/bonds/settlement-consideration` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondAnalyticsEndpoints.cs:21` |
| `GET` | `/api/reference-data/bonds/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:28` |
| `GET` | `/api/reference-data/bonds/{securityId:guid}/accrual-convention` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:60` |
| `GET` | `/api/reference-data/bonds/{securityId:guid}/analytics` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondAnalyticsEndpoints.cs:31` |
| `GET` | `/api/reference-data/bonds/{securityId:guid}/exercise-notices` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondAnalyticsEndpoints.cs:96` |
| `GET` | `/api/reference-data/bonds/{securityId:guid}/lifecycle` | Documented | `src/Meridian.Ui.Shared/Endpoints/BondReferenceEndpoints.cs:44` |
| `GET` | `/api/reference-data/certificates-of-deposit/by-issuer` | Documented | `src/Meridian.Ui.Shared/Endpoints/CertificateOfDepositReferenceEndpoints.cs:33` |
| `GET` | `/api/reference-data/certificates-of-deposit/maturing-before` | Documented | `src/Meridian.Ui.Shared/Endpoints/CertificateOfDepositReferenceEndpoints.cs:44` |
| `GET` | `/api/reference-data/certificates-of-deposit/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/CertificateOfDepositReferenceEndpoints.cs:21` |
| `GET` | `/api/reference-data/commodities/by-exchange` | Documented | `src/Meridian.Ui.Shared/Endpoints/CommodityReferenceEndpoints.cs:44` |
| `GET` | `/api/reference-data/commodities/by-type` | Documented | `src/Meridian.Ui.Shared/Endpoints/CommodityReferenceEndpoints.cs:33` |
| `GET` | `/api/reference-data/commodities/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/CommodityReferenceEndpoints.cs:21` |
| `GET` | `/api/reference-data/crypto/by-base-currency` | Documented | `src/Meridian.Ui.Shared/Endpoints/CryptoReferenceEndpoints.cs:51` |
| `GET` | `/api/reference-data/crypto/by-network` | Documented | `src/Meridian.Ui.Shared/Endpoints/CryptoReferenceEndpoints.cs:38` |
| `GET` | `/api/reference-data/crypto/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/CryptoReferenceEndpoints.cs:24` |
| `GET` | `/api/reference-data/deposits/by-institution` | Documented | `src/Meridian.Ui.Shared/Endpoints/DepositReferenceEndpoints.cs:38` |
| `GET` | `/api/reference-data/deposits/maturing-before` | Documented | `src/Meridian.Ui.Shared/Endpoints/DepositReferenceEndpoints.cs:51` |
| `GET` | `/api/reference-data/deposits/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DepositReferenceEndpoints.cs:24` |
| `GET` | `/api/reference-data/edgar/facts/{cik}` | Documented | `src/Meridian.Ui.Shared/Endpoints/EdgarReferenceDataEndpoints.cs:63` |
| `GET` | `/api/reference-data/edgar/filers/{cik}` | Documented | `src/Meridian.Ui.Shared/Endpoints/EdgarReferenceDataEndpoints.cs:49` |
| `GET` | `/api/reference-data/edgar/security-data/{cik}` | Documented | `src/Meridian.Ui.Shared/Endpoints/EdgarReferenceDataEndpoints.cs:77` |
| `GET` | `/api/reference-data/equities/by-exchange` | Documented | `src/Meridian.Ui.Shared/Endpoints/EquityReferenceEndpoints.cs:33` |
| `GET` | `/api/reference-data/equities/by-issuer` | Documented | `src/Meridian.Ui.Shared/Endpoints/EquityReferenceEndpoints.cs:44` |
| `GET` | `/api/reference-data/equities/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/EquityReferenceEndpoints.cs:21` |
| `GET` | `/api/reference-data/futures/by-root` | Documented | `src/Meridian.Ui.Shared/Endpoints/FutureReferenceEndpoints.cs:38` |
| `GET` | `/api/reference-data/futures/expiry-ladder` | Documented | `src/Meridian.Ui.Shared/Endpoints/FutureReferenceEndpoints.cs:51` |
| `GET` | `/api/reference-data/futures/front-month` | Documented | `src/Meridian.Ui.Shared/Endpoints/FutureReferenceEndpoints.cs:64` |
| `GET` | `/api/reference-data/futures/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/FutureReferenceEndpoints.cs:24` |
| `GET` | `/api/reference-data/fxspot/by-currency` | Documented | `src/Meridian.Ui.Shared/Endpoints/FxSpotReferenceEndpoints.cs:45` |
| `GET` | `/api/reference-data/fxspot/pairs/{pairCode}` | Documented | `src/Meridian.Ui.Shared/Endpoints/FxSpotReferenceEndpoints.cs:33` |
| `GET` | `/api/reference-data/fxspot/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/FxSpotReferenceEndpoints.cs:21` |
| `GET` | `/api/reference-data/money-market-funds/by-family` | Documented | `src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundReferenceEndpoints.cs:33` |
| `GET` | `/api/reference-data/money-market-funds/by-sweep-eligibility` | Documented | `src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundReferenceEndpoints.cs:44` |
| `GET` | `/api/reference-data/money-market-funds/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundReferenceEndpoints.cs:21` |
| `POST` | `/api/reference-data/options/chains/import` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionChainEndpoints.cs:22` |
| `GET` | `/api/reference-data/options/chains/snapshot` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionChainEndpoints.cs:47` |
| `GET` | `/api/reference-data/options/contracts/{contractSymbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionReferenceEndpoints.cs:21` |
| `GET` | `/api/reference-data/options/contracts/{contractSymbol}/underlying-linkage` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionReferenceEndpoints.cs:46` |
| `GET` | `/api/reference-data/options/expiry-ladder` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionReferenceEndpoints.cs:58` |
| `GET` | `/api/reference-data/options/series/{optionChainId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/OptionReferenceEndpoints.cs:33` |
| `GET` | `/api/reference-data/swaps/by-type` | Documented | `src/Meridian.Ui.Shared/Endpoints/SwapReferenceEndpoints.cs:33` |
| `GET` | `/api/reference-data/swaps/maturing-before` | Documented | `src/Meridian.Ui.Shared/Endpoints/SwapReferenceEndpoints.cs:44` |
| `GET` | `/api/reference-data/swaps/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SwapReferenceEndpoints.cs:21` |
| `GET` | `/api/replay/files` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:34` |
| `GET` | `/api/replay/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:206` |
| `POST` | `/api/replay/start` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:74` |
| `GET` | `/api/replay/stats` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:306` |
| `POST` | `/api/replay/{sessionId}/pause` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:133` |
| `POST` | `/api/replay/{sessionId}/resume` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:139` |
| `POST` | `/api/replay/{sessionId}/seek` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:159` |
| `POST` | `/api/replay/{sessionId}/speed` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:165` |
| `GET` | `/api/replay/{sessionId}/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:178` |
| `POST` | `/api/replay/{sessionId}/stop` | Documented | `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs:146` |
| `GET` | `/api/resilience/circuit-breakers` | Documented | `src/Meridian.Ui.Shared/Endpoints/ResilienceEndpoints.cs:26` |
| `POST` | `/api/sampling/create` | Documented | `src/Meridian.Ui.Shared/Endpoints/SamplingEndpoints.cs:34` |
| `GET` | `/api/sampling/estimate` | Documented | `src/Meridian.Ui.Shared/Endpoints/SamplingEndpoints.cs:132` |
| `GET` | `/api/sampling/saved` | Documented | `src/Meridian.Ui.Shared/Endpoints/SamplingEndpoints.cs:193` |
| `GET` | `/api/sampling/{sampleId}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SamplingEndpoints.cs:217` |
| `POST` | `/api/schedules/cron/next-runs` | Documented | `src/Meridian.Ui.Shared/Endpoints/CronEndpoints.cs:43` |
| `POST` | `/api/schedules/cron/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/CronEndpoints.cs:21` |
| `POST` | `/api/security-master` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:374` |
| `POST` | `/api/security-master/aliases/upsert` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:539` |
| `POST` | `/api/security-master/amend` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:405` |
| `GET` | `/api/security-master/asset-classes` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:88` |
| `GET` | `/api/security-master/asset-classes/{assetClass}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:97` |
| `GET` | `/api/security-master/asset-profiles` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:105` |
| `POST` | `/api/security-master/asset-profiles/approve` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:192` |
| `POST` | `/api/security-master/asset-profiles/drafts` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:151` |
| `GET` | `/api/security-master/asset-profiles/promotion-candidates` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:123` |
| `POST` | `/api/security-master/asset-profiles/rollback` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:233` |
| `GET` | `/api/security-master/asset-profiles/{profileId}/lineage` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:135` |
| `GET` | `/api/security-master/conflicts` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:995` |
| `POST` | `/api/security-master/conflicts/{conflictId:guid}/resolve` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1008` |
| `GET` | `/api/security-master/corporate-actions/cases` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:332` |
| `GET` | `/api/security-master/corporate-actions/cases/{caseId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:367` |
| `POST` | `/api/security-master/corporate-actions/cases/{caseId:guid}/accounting-approval` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:631` |
| `POST` | `/api/security-master/corporate-actions/cases/{caseId:guid}/accounting-posting` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:655` |
| `POST` | `/api/security-master/corporate-actions/cases/{caseId:guid}/accounting-projection` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:607` |
| `GET` | `/api/security-master/corporate-actions/cases/{caseId:guid}/conflicts` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:403` |
| `POST` | `/api/security-master/corporate-actions/cases/{caseId:guid}/conflicts` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:534` |
| `GET` | `/api/security-master/corporate-actions/cases/{caseId:guid}/conflicts/{conflictId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:456` |
| `POST` | `/api/security-master/corporate-actions/cases/{caseId:guid}/conflicts/{conflictId:guid}/resolution` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:153` |
| `POST` | `/api/security-master/corporate-actions/cases/{caseId:guid}/evidence` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:509` |
| `POST` | `/api/security-master/corporate-actions/cases/{caseId:guid}/options` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:556` |
| `POST` | `/api/security-master/corporate-actions/cases/{caseId:guid}/transition` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:578` |
| `GET` | `/api/security-master/corporate-actions/inbox` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:73` |
| `POST` | `/api/security-master/corporate-actions/inbox/apply` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:105` |
| `POST` | `/api/security-master/corporate-actions/ingest` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:39` |
| `GET` | `/api/security-master/corporate-actions/source-proposals` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:189` |
| `POST` | `/api/security-master/corporate-actions/source-proposals` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:118` |
| `GET` | `/api/security-master/corporate-actions/source-proposals/{proposalId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:214` |
| `POST` | `/api/security-master/corporate-actions/source-proposals/{proposalId:guid}/accept` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:239` |
| `POST` | `/api/security-master/corporate-actions/source-proposals/{proposalId:guid}/reject` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs:257` |
| `GET` | `/api/security-master/coverage/draft/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:979` |
| `GET` | `/api/security-master/curves` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.Curves.cs:42` |
| `POST` | `/api/security-master/curves` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.Curves.cs:64` |
| `GET` | `/api/security-master/curves/{curveId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.Curves.cs:85` |
| `GET` | `/api/security-master/curves/{curveId:guid}/versions` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.Curves.cs:101` |
| `POST` | `/api/security-master/curves/{curveId:guid}/versions` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.Curves.cs:129` |
| `GET` | `/api/security-master/curves/{curveId:guid}/versions/{version:long}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.Curves.cs:167` |
| `GET` | `/api/security-master/data-entitlements` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1782` |
| `POST` | `/api/security-master/data-entitlements` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1804` |
| `GET` | `/api/security-master/data-entitlements/expiring` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1792` |
| `DELETE` | `/api/security-master/data-entitlements/{entitlementId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1832` |
| `POST` | `/api/security-master/deactivate` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:508` |
| `GET` | `/api/security-master/exceptions/aging` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1917` |
| `POST` | `/api/security-master/import` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1120` |
| `POST` | `/api/security-master/ingest/edgar` | Documented | `src/Meridian.Ui.Shared/Endpoints/EdgarReferenceDataEndpoints.cs:24` |
| `GET` | `/api/security-master/ingest/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1158` |
| `POST` | `/api/security-master/otc/collateral-agreements` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcContracts.cs:143` |
| `GET` | `/api/security-master/otc/collateral-agreements/{collateralAgreementId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcContracts.cs:158` |
| `GET` | `/api/security-master/otc/collateral-agreements/{collateralAgreementId:guid}/collateral-balances` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcCollateral.cs:86` |
| `POST` | `/api/security-master/otc/collateral-balances` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcCollateral.cs:71` |
| `POST` | `/api/security-master/otc/contracts` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcContracts.cs:39` |
| `GET` | `/api/security-master/otc/contracts/{contractId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcContracts.cs:54` |
| `GET` | `/api/security-master/otc/contracts/{contractId:guid}/lifecycle` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcContracts.cs:65` |
| `POST` | `/api/security-master/otc/contracts/{contractId:guid}/lifecycle` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcContracts.cs:76` |
| `GET` | `/api/security-master/otc/exposure-snapshots/{snapshotId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcCollateral.cs:59` |
| `POST` | `/api/security-master/otc/margin-calls` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcCollateral.cs:95` |
| `GET` | `/api/security-master/otc/margin-calls/{callId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcCollateral.cs:126` |
| `POST` | `/api/security-master/otc/margin-calls/{callId:guid}/transition` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcCollateral.cs:110` |
| `GET` | `/api/security-master/otc/master-agreements` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcContracts.cs:107` |
| `POST` | `/api/security-master/otc/master-agreements` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcContracts.cs:116` |
| `GET` | `/api/security-master/otc/master-agreements/{agreementId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcContracts.cs:131` |
| `POST` | `/api/security-master/otc/netting-sets` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcContracts.cs:170` |
| `GET` | `/api/security-master/otc/netting-sets/{nettingSetId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcContracts.cs:185` |
| `GET` | `/api/security-master/otc/netting-sets/{nettingSetId:guid}/exposure-snapshots` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcCollateral.cs:48` |
| `POST` | `/api/security-master/otc/netting-sets/{nettingSetId:guid}/exposure-snapshots` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcCollateral.cs:31` |
| `GET` | `/api/security-master/otc/netting-sets/{nettingSetId:guid}/margin-calls` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcCollateral.cs:138` |
| `GET` | `/api/security-master/quality-report/latest` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1879` |
| `POST` | `/api/security-master/quality-report/run` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1864` |
| `POST` | `/api/security-master/rates/ingest` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.Rates.cs:49` |
| `GET` | `/api/security-master/rates/observations` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.Rates.cs:18` |
| `POST` | `/api/security-master/resolve` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:279` |
| `POST` | `/api/security-master/search` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:316` |
| `POST` | `/api/security-master/structured-credit-reclassification/report` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1897` |
| `GET` | `/api/security-master/{securityId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:46` |
| `GET` | `/api/security-master/{securityId:guid}/cashflow-projections` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CashFlowPreview.cs:140` |
| `POST` | `/api/security-master/{securityId:guid}/cashflow-projections/preview` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CashFlowPreview.cs:20` |
| `GET` | `/api/security-master/{securityId:guid}/cashflow-source` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CashFlowPreview.cs:99` |
| `PUT` | `/api/security-master/{securityId:guid}/cashflow-source` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CashFlowPreview.cs:112` |
| `GET` | `/api/security-master/{securityId:guid}/contracts` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcContracts.cs:22` |
| `GET` | `/api/security-master/{securityId:guid}/convertible-equity-terms` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:669` |
| `PATCH` | `/api/security-master/{securityId:guid}/convertible-equity-terms` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:690` |
| `GET` | `/api/security-master/{securityId:guid}/corporate-actions` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:739` |
| `POST` | `/api/security-master/{securityId:guid}/corporate-actions` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:934` |
| `GET` | `/api/security-master/{securityId:guid}/exercise-notices` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CashFlowPreview.cs:42` |
| `POST` | `/api/security-master/{securityId:guid}/exercise-notices` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CashFlowPreview.cs:54` |
| `POST` | `/api/security-master/{securityId:guid}/exercise-notices/{noticeId:guid}/decision` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CashFlowPreview.cs:77` |
| `GET` | `/api/security-master/{securityId:guid}/factor-history` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:787` |
| `GET` | `/api/security-master/{securityId:guid}/field-provenance` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1201` |
| `GET` | `/api/security-master/{securityId:guid}/history` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:347` |
| `GET` | `/api/security-master/{securityId:guid}/income-cash-flows` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:761` |
| `GET` | `/api/security-master/{securityId:guid}/operator-overrides` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1179` |
| `PATCH` | `/api/security-master/{securityId:guid}/operator-overrides` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1220` |
| `POST` | `/api/security-master/{securityId:guid}/operator-overrides/decision` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1296` |
| `GET` | `/api/security-master/{securityId:guid}/preferred-equity-terms` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:600` |
| `PATCH` | `/api/security-master/{securityId:guid}/preferred-equity-terms` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:621` |
| `GET` | `/api/security-master/{securityId:guid}/price-comparison` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1765` |
| `GET` | `/api/security-master/{securityId:guid}/price-golden-copy` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1731` |
| `GET` | `/api/security-master/{securityId:guid}/price-golden-copy/observations` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1752` |
| `GET` | `/api/security-master/{securityId:guid}/pricing-hierarchy` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1635` |
| `PUT` | `/api/security-master/{securityId:guid}/pricing-hierarchy` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1649` |
| `POST` | `/api/security-master/{securityId:guid}/raw-price` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:1690` |
| `PUT` | `/api/security-master/{securityId:guid}/terms/{blockKey}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:441` |
| `GET` | `/api/security-master/{securityId:guid}/trading-parameters` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:577` |
| `GET` | `/api/security-master/{securityId:guid}/underlyings` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:821` |
| `POST` | `/api/security-master/{securityId:guid}/underlyings` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:865` |
| `DELETE` | `/api/security-master/{securityId:guid}/underlyings/{linkId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:903` |
| `GET` | `/api/security-master/{securityId:guid}/validation` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs:67` |
| `POST` | `/api/security-master/{securityId:guid}/valuation` | Documented | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.Curves.cs:188` |
| `POST` | `/api/servicer-reports` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1271` |
| `GET` | `/api/servicer-reports/{batchId:guid}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1299` |
| `GET` | `/api/servicer-reports/{batchId:guid}/position-lines` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1307` |
| `GET` | `/api/servicer-reports/{batchId:guid}/transaction-lines` | Documented | `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs:1316` |
| `GET` | `/api/sla/health` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:647` |
| `GET` | `/api/sla/metrics` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:674` |
| `GET` | `/api/sla/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:616` |
| `GET` | `/api/sla/status/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:620` |
| `GET` | `/api/sla/violations` | Documented | `src/Meridian.Ui.Shared/Endpoints/DataQualityEndpoints.cs:630` |
| `GET` | `/api/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:127` |
| `GET` | `/api/storage/archive/stats` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:311` |
| `GET` | `/api/storage/breakdown` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:80` |
| `GET` | `/api/storage/capacity-forecast` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:541` |
| `GET` | `/api/storage/catalog` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:345` |
| `POST` | `/api/storage/cleanup` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:275` |
| `GET` | `/api/storage/cleanup/candidates` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:240` |
| `POST` | `/api/storage/convert-parquet` | Documented | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs:516` |
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
| `GET` | `/api/strategies/covered-call/runs` | Documented | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:63` |
| `POST` | `/api/strategies/covered-call/runs` | Documented | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:28` |
| `POST` | `/api/strategies/covered-call/runs/{runId}/cancel` | Documented | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:167` |
| `GET` | `/api/strategies/covered-call/runs/{runId}/result` | Documented | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:118` |
| `GET` | `/api/strategies/covered-call/runs/{runId}/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs:88` |
| `GET` | `/api/strategies/runs/compare` | Documented | `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs:3015` |
| `GET` | `/api/strategies/{strategyId}/runs` | Documented | `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs:2874` |
| `GET` | `/api/subscriptions/active` | Documented | `src/Meridian.Ui.Shared/Endpoints/SubscriptionEndpoints.cs:22` |
| `POST` | `/api/subscriptions/subscribe` | Documented | `src/Meridian.Ui.Shared/Endpoints/SubscriptionEndpoints.cs:44` |
| `POST` | `/api/subscriptions/unsubscribe/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SubscriptionEndpoints.cs:74` |
| `GET` | `/api/symbols` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:32` |
| `POST` | `/api/symbols/add` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:156` |
| `GET` | `/api/symbols/archived` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:80` |
| `POST` | `/api/symbols/batch` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:416` |
| `POST` | `/api/symbols/bulk-add` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:329` |
| `POST` | `/api/symbols/bulk-remove` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:370` |
| `POST` | `/api/symbols/create` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:465` |
| `GET` | `/api/symbols/mappings` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolMappingEndpoints.cs:70` |
| `POST` | `/api/symbols/mappings` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolMappingEndpoints.cs:77` |
| `GET` | `/api/symbols/monitored` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:57` |
| `GET` | `/api/symbols/registry` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolMappingEndpoints.cs:29` |
| `GET` | `/api/symbols/search` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:392` |
| `GET` | `/api/symbols/statistics` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:252` |
| `POST` | `/api/symbols/validate` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:290` |
| `DELETE` | `/api/symbols/{symbol}` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:557` |
| `POST` | `/api/symbols/{symbol}/archive` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:309` |
| `GET` | `/api/symbols/{symbol}/depth` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:227` |
| `POST` | `/api/symbols/{symbol}/remove` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:181` |
| `GET` | `/api/symbols/{symbol}/status` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:99` |
| `GET` | `/api/symbols/{symbol}/trades` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:202` |
| `POST` | `/api/symbols/{symbol}/update` | Documented | `src/Meridian.Ui.Shared/Endpoints/SymbolEndpoints.cs:506` |
| `GET` | `/api/system/lifecycle` | Documented | `src/Meridian/UiServer.cs:664` |
| `POST` | `/api/system/shutdown` | Documented | `src/Meridian/UiServer.cs:698` |
| `GET` | `/api/system/shutdown/receipts/latest` | Documented | `src/Meridian/UiServer.cs:773` |
| `GET` | `/api/system/shutdown/{operationId}` | Documented | `src/Meridian/UiServer.cs:752` |
| `POST` | `/api/workstation/desktop/launch` | Documented | `src/Meridian.Ui.Shared/Endpoints/FirstRunEndpoints.cs:33` |
| `GET` | `/health` | Documented | `src/Meridian.Application/Composition/HostAdapters.cs:60` |
| `GET` | `/live` | Documented | `src/Meridian.Application/Composition/HostAdapters.cs:73` |
| `GET` | `/ready` | Documented | `src/Meridian.Application/Composition/HostAdapters.cs:72` |

## Workstation Contract Coverage

| Contract | Status | Source |
|---|---|---|
| `AccountBalanceHistoryDto` | Documented | `src/Meridian.Contracts/Workstation/AccountBalanceHistoryDtos.cs:7` |
| `AccountBalanceHistoryPointDto` | Documented | `src/Meridian.Contracts/Workstation/AccountBalanceHistoryDtos.cs:4` |
| `AccountingPreparerAttestationDisclosureDto` | Documented | `src/Meridian.Contracts/Workstation/LedgerAmountProofDtos.cs:56` |
| `AccrualCalculationResultDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:267` |
| `AccrualInputSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:249` |
| `ActivationOutcomeDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:76` |
| `AlpacaBrokerageConnectionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:103` |
| `ApprovalDecision` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:30` |
| `ApprovalPolicy` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:22` |
| `ApprovalStep` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:21` |
| `ApproveSecurityMasterOverrides` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:22` |
| `ApproveSecurityMasterRevisionRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:98` |
| `ApproveWorkflow` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:29` |
| `AssignOnboardingDifferenceRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:31` |
| `AuditTrailExplorerQueryDto` | Documented | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:8` |
| `AuditTrailExplorerResultDto` | Documented | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:54` |
| `AuditTrailObjectKindDto` | Documented | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:64` |
| `AuditTrailTimelineEntryDto` | Documented | `src/Meridian.Contracts/Workstation/AuditTrailExplorerDtos.cs:27` |
| `AutomatedJournalCapitalAccountReconciliationDto` | Documented | `src/Meridian.Contracts/Workstation/AutomatedJournalScheduleDtos.cs:25` |
| `AutomatedJournalScheduleStateDto` | Documented | `src/Meridian.Contracts/Workstation/AutomatedJournalScheduleDtos.cs:8` |
| `AutomatedJournalScheduleStatusDto` | Documented | `src/Meridian.Contracts/Workstation/AutomatedJournalScheduleDtos.cs:51` |
| `BankAccountSnapshot` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:102` |
| `BankStatementImportResultDto` | Documented | `src/Meridian.Contracts/Workstation/DataUploadDtos.cs:74` |
| `BiasDisclosureDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:271` |
| `BiasDisclosureItemDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:260` |
| `BooksBeforeBrokerReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:62` |
| `BrokerageAccountKindDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:18` |
| `BrokerageAccountLinkRequestDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:61` |
| `BrokerageCashFlowEntryDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:130` |
| `BrokerageCashFlowSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:140` |
| `BrokerageConnectionStateDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:27` |
| `BrokerageConnectionStatusDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:86` |
| `BrokerageHouseholdAccountDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:155` |
| `BrokerageHouseholdPortfolioDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:189` |
| `BrokerageHouseholdPositionDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:171` |
| `BrokeragePortfolioPerformanceDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:114` |
| `BrokeragePortfolioPerformancePointDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:108` |
| `BuildLedgerDraft` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:23` |
| `BulkResolveSecurityMasterConflictsRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:634` |
| `BulkResolveSecurityMasterConflictsResult` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:640` |
| `CanonicalizationAssuranceDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:109` |
| `CanonicalizationProviderSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/DataOperationsAssuranceDtos.cs:119` |
| `CaptureOnboardingComparisonRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:25` |
| `CashFinancingSummary` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:141` |
| `CashFlowEntryDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:936` |
| `CashFlowProjectionPoint` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:34` |
| `CashForecastResult` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:36` |
| `CashLadderBucketDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:946` |
| `CashSyncSourceAvailability` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:2` |
| `CashSyncWindow` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:5` |
| `ChangeSinglePrincipalDeclarationRequestDto` | Documented | `src/Meridian.Contracts/Workstation/SinglePrincipalAttestationDtos.cs:21` |
| `CloseReadinessBlockerDto` | Documented | `src/Meridian.Contracts/Workstation/CloseReadinessDtos.cs:34` |
| `CloseReadinessContributionDto` | Documented | `src/Meridian.Contracts/Workstation/CloseReadinessDtos.cs:28` |
| `CloseReadinessProjectionDto` | Documented | `src/Meridian.Contracts/Workstation/CloseReadinessDtos.cs:46` |
| `CloseReadinessScopeDto` | Documented | `src/Meridian.Contracts/Workstation/CloseReadinessDtos.cs:4` |
| `CloseReadinessSubjectDto` | Documented | `src/Meridian.Contracts/Workstation/CloseReadinessDtos.cs:12` |
| `CloseWorkflow` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:31` |
| `ClosedLotSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1128` |
| `CollateralCallDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:26` |
| `CompleteActivationOutcomeRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:92` |
| `CompleteFirstRunRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:86` |
| `CorporateActionDescriptorDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:96` |
| `CorporateActionTimelineEntryDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:110` |
| `CounterpartyExposureDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:10` |
| `CouponEvent` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:20` |
| `CreateOnboardingWorkspaceRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:18` |
| `CrossFundReportingConsolidationDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:323` |
| `CrossFundReportingConsolidationScopeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:313` |
| `DailyValuationBatchLifecycleRequestDto` | Documented | `src/Meridian.Contracts/Workstation/DailyValuationScheduleDtos.cs:54` |
| `DailyValuationBatchLifecycleResultDto` | Documented | `src/Meridian.Contracts/Workstation/DailyValuationScheduleDtos.cs:73` |
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
| `DesktopLaunchTicketRedemptionDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:94` |
| `DiscardSecurityMasterRevisionRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:91` |
| `EquityCurvePoint` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:872` |
| `EquityCurveSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:882` |
| `EvidenceArtifactCaptureDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:40` |
| `EvidenceArtifactExtractionFieldDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:49` |
| `EvidenceArtifactRefDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:29` |
| `EvidenceArtifactRefDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:279` |
| `EvidenceAssuranceComponentDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:344` |
| `EvidenceCompletenessDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:636` |
| `EvidenceCompletenessSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:151` |
| `EvidenceDocumentAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:153` |
| `EvidenceDocumentAuthorityDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:160` |
| `EvidenceDocumentClassificationDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:62` |
| `EvidenceDocumentConfirmedFieldDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:145` |
| `EvidenceDocumentDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:193` |
| `EvidenceDocumentExtractionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:246` |
| `EvidenceDocumentExtractionResultDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:254` |
| `EvidenceDocumentIntakeChannelDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:80` |
| `EvidenceDocumentIntakeSourceDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:272` |
| `EvidenceDocumentIntakeSourceKindDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:262` |
| `EvidenceDocumentLinkDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:129` |
| `EvidenceDocumentLinkKindDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:104` |
| `EvidenceDocumentReviewStateDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:136` |
| `EvidenceDocumentReviewStatusDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:122` |
| `EvidenceDocumentSourceRecordDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:171` |
| `EvidenceEdgeDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:301` |
| `EvidenceEndpointErrorDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:628` |
| `EvidenceExtractionStatusDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:93` |
| `EvidenceFreshnessDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:24` |
| `EvidenceGraphDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:674` |
| `EvidenceLifecycleMetadataDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:598` |
| `EvidenceManifestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:233` |
| `EvidenceManifestPackageKindDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:223` |
| `EvidenceNodeDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:285` |
| `EvidenceNodeDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:296` |
| `EvidencePacketDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:658` |
| `EvidencePacketExportRequest` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:698` |
| `EvidencePacketExportResponse` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:709` |
| `EvidenceProofChainDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:385` |
| `EvidenceProofChainLayerDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:372` |
| `EvidenceProofChainLayerKindDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:359` |
| `EvidenceRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:183` |
| `EvidenceRequestListDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:463` |
| `EvidenceRequestListKindDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:453` |
| `EvidenceSlaAssessmentDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:333` |
| `EvidenceSlaPolicyDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:324` |
| `EvidenceStatusDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:6` |
| `EvidenceSubjectDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:15` |
| `EvidenceSubjectLinkageDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:604` |
| `EvidenceSupportRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:439` |
| `EvidenceTemplateDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:691` |
| `EvidenceTemplateExportSettingsDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:686` |
| `EvidenceValidationIssueDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:315` |
| `EvidenceValidationSeverityDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:309` |
| `EvidenceVaultArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:422` |
| `EvidenceVaultDocumentEntryDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:529` |
| `EvidenceVaultDocumentQueryDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:516` |
| `EvidenceVaultDocumentReviewRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:540` |
| `EvidenceVaultDocumentReviewResponseDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:550` |
| `EvidenceVaultIdentityDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:402` |
| `EvidenceVaultIntakeRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:554` |
| `EvidenceVaultIntakeResponseDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:581` |
| `EvidenceVaultLookupRequestDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:614` |
| `EvidenceVaultRequestListEntryDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:493` |
| `EvidenceVaultRequestListQueryDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:479` |
| `ExpectedAccountingEventDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:283` |
| `ExpectedAccountingEventKindDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:228` |
| `ExpectedJournalPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:318` |
| `ExpectedJournalPreviewLineDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:308` |
| `ExposureSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:2` |
| `ExposureTrendPointDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:43` |
| `FeatureCapabilitySettingsResponse` | Documented | `src/Meridian.Contracts/Workstation/FeatureCapabilityDtos.cs:2` |
| `FeatureCapabilityToggleDto` | Documented | `src/Meridian.Contracts/Workstation/FeatureCapabilityDtos.cs:5` |
| `FeatureCapabilityToggleRequest` | Documented | `src/Meridian.Contracts/Workstation/FeatureCapabilityDtos.cs:16` |
| `FinancialOperationsCloseSupportDecisionDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialOperationsCommandCenterDtos.cs:22` |
| `FinancialOperationsCloseSupportDecisionRowDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialOperationsCommandCenterDtos.cs:35` |
| `FinancialOperationsCommandCenterDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialOperationsCommandCenterDtos.cs:2` |
| `FinancialOperationsCommandCenterMetricDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialOperationsCommandCenterDtos.cs:50` |
| `FinancialOperationsQueueRowDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialOperationsCommandCenterDtos.cs:58` |
| `FinancialRecordExplorerCellDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:73` |
| `FinancialRecordExplorerColumnDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:66` |
| `FinancialRecordExplorerDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:14` |
| `FinancialRecordExplorerFilterDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:59` |
| `FinancialRecordExplorerGraphEdgeDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:132` |
| `FinancialRecordExplorerGraphNodeDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:125` |
| `FinancialRecordExplorerProofActionDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:112` |
| `FinancialRecordExplorerQueryDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:31` |
| `FinancialRecordExplorerRecordGraphDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:121` |
| `FinancialRecordExplorerRelationshipDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:138` |
| `FinancialRecordExplorerRowDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:89` |
| `FinancialRecordExplorerSavedViewDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:41` |
| `FinancialRecordExplorerSavedViewSaveRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:145` |
| `FinancialRecordExplorerScopeItemDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:36` |
| `FinancialRecordExplorerSelectedRecordDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:99` |
| `FinancialRecordExplorerSummaryItemDto` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:53` |
| `FinancialRecordExplorerTone` | Documented | `src/Meridian.Contracts/Workstation/FinancialRecordExplorerDtos.cs:6` |
| `FirstRunStatusDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:2` |
| `FreezeOnboardingPacketRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:37` |
| `FundAccountBrokerageBalanceSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:200` |
| `FundAccountBrokerageCashTransactionDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:247` |
| `FundAccountBrokerageCorporateActionDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:256` |
| `FundAccountBrokerageFillDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:235` |
| `FundAccountBrokerageOrderDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:221` |
| `FundAccountBrokeragePositionDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:207` |
| `FundAccountBrokerageSyncActivityDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:268` |
| `FundAccountCloseReadinessActionDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:566` |
| `FundAccountCloseReadinessBlockerDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:558` |
| `FundAccountCloseReadinessComponentDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:544` |
| `FundAccountCloseReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:572` |
| `FundAccountCloseReadinessStatusDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:538` |
| `FundAccountSummary` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:73` |
| `FundAuditEntry` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:302` |
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
| `FundReconciliationItem` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:166` |
| `FundReportAssetClassSectionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:493` |
| `FundReportPackArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:551` |
| `FundReportPackEvidenceBundleApprovalDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:820` |
| `FundReportPackEvidenceBundleDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:828` |
| `FundReportPackEvidenceBundleSourceLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:811` |
| `FundReportPackGenerateRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:535` |
| `FundReportPackHistoryItemDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:788` |
| `FundReportPackLifecycleEventDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:746` |
| `FundReportPackLineagePointerDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:588` |
| `FundReportPackPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:518` |
| `FundReportPackPreviewRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:482` |
| `FundReportPackProvenanceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:562` |
| `FundReportPackSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:757` |
| `FundReportPackValidationIssueDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:729` |
| `FundReportingProfileDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:126` |
| `FundReportingSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:431` |
| `FundTrialBalanceLine` | Documented | `src/Meridian.Contracts/Workstation/FundLedgerDtos.cs:37` |
| `FundWorkflowCommandMetadata` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:9` |
| `FundWorkflowOverallStatus` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:4` |
| `FundWorkflowRejectionReasonCode` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:8` |
| `FundWorkflowStage` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:6` |
| `FundWorkflowState` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:33` |
| `FundWorkflowSubStatus` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:7` |
| `FundWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:42` |
| `FxConversionReference` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:32` |
| `GovernanceLifecycleProjectionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:202` |
| `GovernanceReportArtifactFormatDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:25` |
| `GovernanceReportKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:8` |
| `GovernanceReportPackStatusDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:35` |
| `GovernanceReportValidationSeverityDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:53` |
| `HaircutRuleDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:24` |
| `ImportBrokerData` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:19` |
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
| `InstrumentPassportClassificationProfileDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:580` |
| `InstrumentPassportDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:604` |
| `InstrumentPassportOperationsHandoffDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:468` |
| `InstrumentPassportOperationsReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:507` |
| `InstrumentPassportOperationsWorkbenchDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:483` |
| `InstrumentPassportOperationsWorkbenchItemDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:497` |
| `InstrumentPassportOperationsWorkbenchPanelDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:490` |
| `InstrumentPassportPricingDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:624` |
| `InstrumentPassportProviderConfidenceDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:439` |
| `InstrumentPassportReferenceDataWorkbenchDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:454` |
| `InstrumentPassportReferenceDataWorkbenchSectionDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:460` |
| `InvestmentAccountingPreviewModeDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:24` |
| `InvestmentAccountingReconciliationExpectationDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:54` |
| `InvestmentAccountingTradeSideDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:17` |
| `InvestmentAccountingTransactionKindDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:6` |
| `InvestmentAccountingTransactionLabPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:71` |
| `InvestmentAccountingTransactionLabRequestDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:29` |
| `InvestmentAccountingTrialBalanceImpactDto` | Documented | `src/Meridian.Contracts/Workstation/InvestmentAccountingTransactionLabDtos.cs:47` |
| `LedgerAmountApprovalStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:682` |
| `LedgerAmountProofDto` | Documented | `src/Meridian.Contracts/Workstation/LedgerAmountProofDtos.cs:31` |
| `LedgerAmountProofEvidenceDto` | Documented | `src/Meridian.Contracts/Workstation/LedgerAmountProofDtos.cs:10` |
| `LedgerAmountProvenanceDetailDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:706` |
| `LedgerAmountProvenanceEvidenceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:609` |
| `LedgerAmountReconciliationCaseDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:644` |
| `LedgerAmountReconciliationStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:675` |
| `LedgerAmountReportUsageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:688` |
| `LedgerAmountScopeDto` | Documented | `src/Meridian.Contracts/Workstation/LedgerAmountProofDtos.cs:4` |
| `LedgerAmountSecurityMasterLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:633` |
| `LedgerAmountStrategyRunLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:698` |
| `LedgerImpactPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:291` |
| `LedgerJournalLine` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:633` |
| `LedgerSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:584` |
| `LedgerTrialBalanceLine` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:612` |
| `MarginCertificationRequestDto` | Documented | `src/Meridian.Contracts/Workstation/MarginControlCenterDtos.cs:76` |
| `MarginCertificationResultDto` | Documented | `src/Meridian.Contracts/Workstation/MarginControlCenterDtos.cs:83` |
| `MarginControlAccountDto` | Documented | `src/Meridian.Contracts/Workstation/MarginControlCenterDtos.cs:4` |
| `MarginControlAlertDto` | Documented | `src/Meridian.Contracts/Workstation/MarginControlCenterDtos.cs:56` |
| `MarginControlCenterDto` | Documented | `src/Meridian.Contracts/Workstation/MarginControlCenterDtos.cs:64` |
| `MarginControlPrimeSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/MarginControlCenterDtos.cs:48` |
| `MarginPositionContributionDto` | Documented | `src/Meridian.Contracts/Workstation/MarginControlCenterDtos.cs:35` |
| `MarginRequirementDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:22` |
| `MarkFreshnessAssessmentDto` | Documented | `src/Meridian.Contracts/Workstation/MarkFreshnessDtos.cs:4` |
| `MeridianAssuranceScoreDto` | Documented | `src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:351` |
| `MetricsDiff` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:849` |
| `MultiAssetClassCoverageDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1019` |
| `MultiAssetCoverageSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1065` |
| `MultiAssetDrillThroughTargetDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1006` |
| `MultiAssetEvidenceRequirementDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:985` |
| `MultiAssetPackCoverageDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1034` |
| `MultiAssetReadinessBlockerDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:996` |
| `NormalizeBrokerTransactions` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:20` |
| `NullReportingRunNotifier` | Documented | `src/Meridian.Contracts/Workstation/IReportingRunNotifier.cs:21` |
| `NumberPassportDeclaredScopeDto` | Documented | `src/Meridian.Contracts/Workstation/NumberPassportReferenceDtos.cs:19` |
| `NumberPassportReferenceDto` | Documented | `src/Meridian.Contracts/Workstation/NumberPassportReferenceDtos.cs:7` |
| `OnboardingComparisonDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:71` |
| `OnboardingCriteriaDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:9` |
| `OnboardingCriteriaRevisionDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:61` |
| `OnboardingDifferenceAssignmentDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:80` |
| `OnboardingDifferenceDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:66` |
| `OnboardingMissingSourceDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:52` |
| `OnboardingObservationDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:47` |
| `OnboardingPacketContentDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:97` |
| `OnboardingPeriodDifferenceDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:88` |
| `OnboardingReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:91` |
| `OnboardingReadinessPacketDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:109` |
| `OnboardingReviewDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:84` |
| `OnboardingScopeDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:4` |
| `OnboardingSourceCaptureDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:56` |
| `OnboardingSourceSelectionDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:28` |
| `OnboardingSourceSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:41` |
| `OnboardingWorkspaceDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:112` |
| `OpenLotSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1115` |
| `OperatingScopeValuesDto` | Documented | `src/Meridian.Contracts/Workstation/OperatorScopePreferenceDtos.cs:7` |
| `OperationsAccountingRecordEvidenceCategoryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1160` |
| `OperationsAccountingRecordSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1150` |
| `OperationsActionOriginDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:467` |
| `OperationsApprovalCollaborationDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsApprovalCollaborationDtos.cs:10` |
| `OperationsApprovalCollaborationReceipt` | Documented | `src/Meridian.Contracts/Workstation/OperationsApprovalCollaborationDtos.cs:7` |
| `OperationsApprovalCommentDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsApprovalCollaborationDtos.cs:4` |
| `OperationsApprovalCommentRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsApprovalCollaborationDtos.cs:17` |
| `OperationsApprovalDecisionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:685` |
| `OperationsApprovalDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1315` |
| `OperationsApprovalPolicyMatrixDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:745` |
| `OperationsApprovalPolicyMatrixRowDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:751` |
| `OperationsApprovalPolicyRuleAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:795` |
| `OperationsApprovalPolicyRuleUpsertRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:769` |
| `OperationsApprovalPolicyRuleUpsertResultDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:790` |
| `OperationsApprovalReassignRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsApprovalCollaborationDtos.cs:21` |
| `OperationsApprovalStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:102` |
| `OperationsAssignBreakCaseRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:662` |
| `OperationsBreakCaseDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1271` |
| `OperationsBrokerIntakeStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:52` |
| `OperationsChecklistAcknowledgeRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1204` |
| `OperationsChecklistControlApprovalDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:740` |
| `OperationsCloseCalendarDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:810` |
| `OperationsCloseCalendarItemAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:850` |
| `OperationsCloseCalendarItemDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:814` |
| `OperationsCloseCalendarItemUpsertRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:836` |
| `OperationsCloseCalendarItemUpsertResultDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:845` |
| `OperationsCloseChecklistGateSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsCloseChecklistHistoryDtos.cs:6` |
| `OperationsCloseChecklistHistoryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsCloseChecklistHistoryDtos.cs:11` |
| `OperationsCloseChecklistObservationDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsCloseChecklistHistoryDtos.cs:8` |
| `OperationsCloseChecklistSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsCloseChecklistHistoryDtos.cs:4` |
| `OperationsCloseChecklistTaskDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1169` |
| `OperationsCloseChecklistVersionGapDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsCloseChecklistHistoryDtos.cs:18` |
| `OperationsClosePackagePublicationDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1186` |
| `OperationsCloseReadinessBlockerDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1358` |
| `OperationsCloseReadinessComponentDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1347` |
| `OperationsCloseReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1338` |
| `OperationsCloseWorkflowRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:706` |
| `OperationsContinuityCorrelationKeysDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1307` |
| `OperationsContinuityWorkflowDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1053` |
| `OperationsContinuityWorkflowSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1003` |
| `OperationsDashboardMetricDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1107` |
| `OperationsDashboardSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1091` |
| `OperationsEvidenceLinkDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1383` |
| `OperationsEvidencePackageSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1121` |
| `OperationsGateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1210` |
| `OperationsGateKeyDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:42` |
| `OperationsGatePostureRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:504` |
| `OperationsGateStatusDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:29` |
| `OperationsIssueCodeDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:445` |
| `OperationsJournalEntryMetadataDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:617` |
| `OperationsLedgerDraftRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:536` |
| `OperationsLedgerJournalCandidateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:574` |
| `OperationsLedgerJournalLineDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:601` |
| `OperationsLedgerPostRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:560` |
| `OperationsLedgerPostingStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:72` |
| `OperationsLedgerPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1325` |
| `OperationsLedgerValidationRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:550` |
| `OperationsNextActionDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1374` |
| `OperationsReconciliationLaneStatusDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:93` |
| `OperationsReconciliationLaneSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1139` |
| `OperationsReconciliationRunRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:632` |
| `OperationsReconciliationStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:82` |
| `OperationsRejectWorkflowRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:696` |
| `OperationsReopenWorkflowRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:727` |
| `OperationsReportPackReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1332` |
| `OperationsResolveBreakCaseRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:650` |
| `OperationsReviewedAutomationArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1036` |
| `OperationsReviewedAutomationSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1017` |
| `OperationsSecurityMasterOverrideApprovalRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:493` |
| `OperationsSecurityMasterResolveRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:524` |
| `OperationsSecurityMasterStateDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:62` |
| `OperationsStartWorkflowRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:474` |
| `OperationsSubmitApprovalRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:674` |
| `OperationsTimelineEntryDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1221` |
| `OperationsTransitionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:485` |
| `OperationsTransitionResultDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:864` |
| `OperationsWorkflowAuditDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1246` |
| `OperationsWorkflowBlockerDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1366` |
| `OperationsWorkflowStatusDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:12` |
| `OperatorInboxDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:94` |
| `OperatorInboxTriageDto` | Documented | `src/Meridian.Contracts/Workstation/OperatorTriageDtos.cs:35` |
| `OperatorScopePreferenceDto` | Documented | `src/Meridian.Contracts/Workstation/OperatorScopePreferenceDtos.cs:21` |
| `OperatorScopePreferenceSaveRequest` | Documented | `src/Meridian.Contracts/Workstation/OperatorScopePreferenceDtos.cs:26` |
| `OperatorSurfaceVisitDto` | Documented | `src/Meridian.Contracts/Workstation/OperatorSurfaceVisitDtos.cs:10` |
| `OperatorTriageAction` | Documented | `src/Meridian.Contracts/Workstation/OperatorTriageDtos.cs:6` |
| `OperatorTriageActivity` | Documented | `src/Meridian.Contracts/Workstation/OperatorTriageDtos.cs:22` |
| `OperatorTriageBulkCommand` | Documented | `src/Meridian.Contracts/Workstation/OperatorTriageDtos.cs:19` |
| `OperatorTriageBulkItem` | Documented | `src/Meridian.Contracts/Workstation/OperatorTriageDtos.cs:17` |
| `OperatorTriageBulkResult` | Documented | `src/Meridian.Contracts/Workstation/OperatorTriageDtos.cs:40` |
| `OperatorTriageCapabilities` | Documented | `src/Meridian.Contracts/Workstation/OperatorTriageDtos.cs:26` |
| `OperatorTriageCommand` | Documented | `src/Meridian.Contracts/Workstation/OperatorTriageDtos.cs:7` |
| `OperatorTriageItemDto` | Documented | `src/Meridian.Contracts/Workstation/OperatorTriageDtos.cs:28` |
| `OperatorTriagePersonalState` | Documented | `src/Meridian.Contracts/Workstation/OperatorTriageDtos.cs:51` |
| `OperatorTriageReceipt` | Documented | `src/Meridian.Contracts/Workstation/OperatorTriageDtos.cs:53` |
| `OperatorTriageResult` | Documented | `src/Meridian.Contracts/Workstation/OperatorTriageDtos.cs:38` |
| `OperatorTriageScope` | Documented | `src/Meridian.Contracts/Workstation/OperatorTriageDtos.cs:54` |
| `OperatorTriageState` | Documented | `src/Meridian.Contracts/Workstation/OperatorTriageDtos.cs:43` |
| `OperatorWorkItemDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:59` |
| `OperatorWorkItemKindDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:6` |
| `OperatorWorkItemToneDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:21` |
| `OperatorWorkflowHomeSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:6` |
| `ParameterDiff` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:843` |
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
| `PortfolioPositionSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:563` |
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
| `PortfolioSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:527` |
| `PositionDiffEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:834` |
| `PostLedgerEntries` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:25` |
| `PreparerAttestationEvidenceProjectionDto` | Documented | `src/Meridian.Contracts/Workstation/PreparerAttestationEvidenceDtos.cs:7` |
| `PreparerAttestationProofExportDto` | Documented | `src/Meridian.Contracts/Workstation/PreparerAttestationProofExportDto.cs:9` |
| `PreparerAttestationReceiptDto` | Documented | `src/Meridian.Contracts/Workstation/SinglePrincipalAttestationDtos.cs:63` |
| `PreparerAttestationRevocationDto` | Documented | `src/Meridian.Contracts/Workstation/SinglePrincipalAttestationDtos.cs:94` |
| `PreparerEvidenceAcknowledgmentDto` | Documented | `src/Meridian.Contracts/Workstation/PreparerAttestationEvidenceDtos.cs:23` |
| `PrivateCapitalCloseCockpitApprovalDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1421` |
| `PrivateCapitalCloseCockpitDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1484` |
| `PrivateCapitalCloseCockpitLaneDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1405` |
| `PrivateCapitalCloseCockpitWorkflowDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1390` |
| `PrivateCapitalNavSupportComponentDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1437` |
| `PrivateCapitalNavSupportPackageDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1465` |
| `PrivateCapitalShadowNavTieOutDto` | Documented | `src/Meridian.Contracts/Workstation/OperationsContinuityDtos.cs:1446` |
| `ProductExposureDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:20` |
| `ProviderCorporateActionEvidenceCandidateDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:434` |
| `ProviderCorporateActionLedgerEffectDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:452` |
| `ProviderCorporateActionReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:493` |
| `ProviderCorporateActionReadinessLineDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:425` |
| `ProviderLedgerReconciliationBreakDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:337` |
| `ProviderLedgerReconciliationBreakSignOffStateDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:300` |
| `ProviderLedgerReconciliationCheckDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:324` |
| `ProviderLedgerReconciliationCheckStatusDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:292` |
| `ProviderLedgerReconciliationDetailDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:525` |
| `ProviderLedgerReconciliationRequestDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:315` |
| `ProviderLedgerReconciliationStatusDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:284` |
| `ProviderLedgerReconciliationSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:361` |
| `ProviderPromotionChecklistDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:334` |
| `ProviderSecurityMasterPassportDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:381` |
| `ProviderSecurityMasterPassportStatusDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:308` |
| `ProviderSecurityMasterScheduleFeedDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:469` |
| `ProviderShadowBookComparisonDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:415` |
| `ProviderShadowBookComparisonLineDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:404` |
| `PublishSecurityMasterRevisionRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:116` |
| `RecommendedActionDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:84` |
| `ReconciliationBreakCategory` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:54` |
| `ReconciliationBreakDispositionDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:459` |
| `ReconciliationBreakDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:141` |
| `ReconciliationBreakExplanationDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:613` |
| `ReconciliationBreakLineageDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationBreakLineageDtos.cs:6` |
| `ReconciliationBreakMeasureDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:449` |
| `ReconciliationBreakMeasureKindDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:438` |
| `ReconciliationBreakObservation` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationBreakLineageDtos.cs:37` |
| `ReconciliationBreakQueueItem` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:480` |
| `ReconciliationBreakQueueProjectionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:248` |
| `ReconciliationBreakQueueProjectionItemDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:263` |
| `ReconciliationBreakQueueScope` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:592` |
| `ReconciliationBreakQueueStatus` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:365` |
| `ReconciliationBreakScore` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:931` |
| `ReconciliationBreakSeverity` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:41` |
| `ReconciliationBreakStatus` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:28` |
| `ReconciliationBulkCaseworkCaseResult` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:764` |
| `ReconciliationBulkCaseworkRequest` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:730` |
| `ReconciliationBulkCaseworkResult` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:771` |
| `ReconciliationCalibrationProfileSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:978` |
| `ReconciliationCalibrationStatusDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:470` |
| `ReconciliationCalibrationSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:995` |
| `ReconciliationCaseComment` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:621` |
| `ReconciliationCaseCommentVisibility` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:411` |
| `ReconciliationCaseLifecycleState` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:375` |
| `ReconciliationCasePriority` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:391` |
| `ReconciliationCaseSignoffRecord` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:922` |
| `ReconciliationCaseSlaState` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:400` |
| `ReconciliationCaseStateTransition` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:944` |
| `ReconciliationCaseTransitionAction` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:955` |
| `ReconciliationCaseTransitionCommand` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:965` |
| `ReconciliationCaseworkAction` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:419` |
| `ReconciliationCaseworkCloseScopeDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:725` |
| `ReconciliationCaseworkCommand` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:684` |
| `ReconciliationCaseworkOperationResult` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:915` |
| `ReconciliationCompletedRunObservation` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationBreakLineageDtos.cs:39` |
| `ReconciliationCorrelationContext` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1056` |
| `ReconciliationJobControl` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1079` |
| `ReconciliationMatchDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:117` |
| `ReconciliationPayloadEnvelope` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1066` |
| `ReconciliationProcessingTelemetry` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1094` |
| `ReconciliationRolloutFlags` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1105` |
| `ReconciliationRunDetail` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:345` |
| `ReconciliationRunObservationScope` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationBreakLineageDtos.cs:24` |
| `ReconciliationRunRequest` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:72` |
| `ReconciliationRunSummary` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:86` |
| `ReconciliationSchemaVersion` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1046` |
| `ReconciliationSecurityCoverageIssueDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:215` |
| `ReconciliationSlaComputationResult` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:662` |
| `ReconciliationSlaPolicy` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:641` |
| `ReconciliationSourceKind` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:14` |
| `ReconciliationSummary` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsDtos.cs:188` |
| `ReconciliationTaxonomySnapshot` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:679` |
| `ReconciliationTaxonomyValue` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:671` |
| `RecordPreparerAttestationRequestDto` | Documented | `src/Meridian.Contracts/Workstation/SinglePrincipalAttestationDtos.cs:30` |
| `RecurringJournalOccurrenceDto` | Documented | `src/Meridian.Contracts/Workstation/RecurringJournalDtos.cs:8` |
| `RecurringJournalQueueDto` | Documented | `src/Meridian.Contracts/Workstation/RecurringJournalDtos.cs:28` |
| `RejectWorkflow` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:30` |
| `RenderReportTemplateRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1687` |
| `RenderReportTemplateResponseDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1692` |
| `ReopenWorkflow` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:32` |
| `ReportAccessEvaluationDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1674` |
| `ReportAccessModeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1648` |
| `ReportAccessPolicyDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1667` |
| `ReportAccessPrincipalDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1662` |
| `ReportAccessPrincipalKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1656` |
| `ReportBrandingThemeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:138` |
| `ReportLedgerAmountBindingDto` | Documented | `src/Meridian.Contracts/Workstation/ReportLedgerAmountBindingDtos.cs:6` |
| `ReportPackAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1755` |
| `ReportPackChangedLineDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1758` |
| `ReportPackCreateRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1808` |
| `ReportPackDeliveryAccessLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:882` |
| `ReportPackDeliveryApprovalStepDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:910` |
| `ReportPackDeliveryArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:871` |
| `ReportPackDeliveryAttemptDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:993` |
| `ReportPackDeliveryEvidencePacketDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:918` |
| `ReportPackDeliveryFailureRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1020` |
| `ReportPackDeliveryHistoryDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1029` |
| `ReportPackDeliveryModeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:864` |
| `ReportPackDeliveryNotificationDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:890` |
| `ReportPackDeliveryPackageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:942` |
| `ReportPackDeliveryRecipientDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:904` |
| `ReportPackDeliveryRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1010` |
| `ReportPackDeliveryStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:854` |
| `ReportPackEvidenceLinkDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1757` |
| `ReportPackLineProvenanceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1759` |
| `ReportPackPublicationManifestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1777` |
| `ReportPackPublishRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1790` |
| `ReportPackRejectRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1803` |
| `ReportPackRejectionMetadataDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1823` |
| `ReportPackRestateRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1829` |
| `ReportPackRestatementMetadataDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1816` |
| `ReportPackWorkflowActionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1800` |
| `ReportPackWorkflowRecordDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1835` |
| `ReportPackWorkflowStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1414` |
| `ReportTemplateAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1708` |
| `ReportTemplateDecisionRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1751` |
| `ReportTemplateDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1679` |
| `ReportTemplateDraftRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1740` |
| `ReportTemplateGovernanceRecordDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1716` |
| `ReportTemplateLifecycleStatusDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1700` |
| `ReportTemplateParameterDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1429` |
| `ReportWriterAggregateFunctionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1441` |
| `ReportWriterCellStyleDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1499` |
| `ReportWriterChartDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1524` |
| `ReportWriterChartRenderDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1535` |
| `ReportWriterChartSeriesDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1530` |
| `ReportWriterChartTypeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1516` |
| `ReportWriterDiffDirectionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1615` |
| `ReportWriterDiffRowStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1606` |
| `ReportWriterFilterDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1476` |
| `ReportWriterFilterLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1563` |
| `ReportWriterFilterOperatorDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1462` |
| `ReportWriterFormatRuleDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1507` |
| `ReportWriterFormulaDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1455` |
| `ReportWriterFormulaLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1558` |
| `ReportWriterGridColumnDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1543` |
| `ReportWriterGridDataDictionaryFieldDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1569` |
| `ReportWriterGridDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1482` |
| `ReportWriterGridDiffCellDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1621` |
| `ReportWriterGridDiffDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1635` |
| `ReportWriterGridDiffRowDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1628` |
| `ReportWriterGridKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1432` |
| `ReportWriterGridLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1583` |
| `ReportWriterGridRenderDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1592` |
| `ReportWriterGridRowDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1548` |
| `ReportWriterGridValidationCheckDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1578` |
| `ReportWriterMetricDefinitionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1449` |
| `ReportWriterMetricLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1553` |
| `ReportingAccountingBasisDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1269` |
| `ReportingConsolidationLevelDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1279` |
| `ReportingDeploymentCapabilityDto` | Documented | `src/Meridian.Contracts/Workstation/ReportingDeploymentDtos.cs:16` |
| `ReportingDeploymentComponentDto` | Documented | `src/Meridian.Contracts/Workstation/ReportingDeploymentDtos.cs:6` |
| `ReportingDueScheduleRunResultDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1254` |
| `ReportingEntityScopeKindDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1260` |
| `ReportingFinalityDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1298` |
| `ReportingLedgerBookSelectionDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1320` |
| `ReportingOutputFormatDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1288` |
| `ReportingRunAuditEntryDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1384` |
| `ReportingRunAuditTrailDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1391` |
| `ReportingRunParametersDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1324` |
| `ReportingRunReadinessCheckDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1342` |
| `ReportingRunReadinessDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1356` |
| `ReportingRunReadinessStatusDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1305` |
| `ReportingRunRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1368` |
| `ReportingRunResultDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1381` |
| `ReportingRunScopeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1312` |
| `ReportingScheduleDeliveryPlanDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1079` |
| `ReportingScheduleDeliveryTargetDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1033` |
| `ReportingSchedulePeriodCadenceDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1221` |
| `ReportingSchedulePeriodFrequencyDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1210` |
| `ReportingScheduleRecordDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1165` |
| `ReportingScheduleRunResultDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1248` |
| `ReportingScheduleStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1122` |
| `ReportingScheduleUpsertRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1224` |
| `ReportingScheduledReleaseHandoffDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1054` |
| `ReportingScheduledReleaseHandoffStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1043` |
| `ReportingStarterKitDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1139` |
| `ReportingStarterKitProvisionResultDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1160` |
| `ReportingStarterKitStateDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1149` |
| `ReportingStarterSeedScheduleDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1129` |
| `ResearchBriefingAlert` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:95` |
| `ResearchBriefingDto` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:132` |
| `ResearchBriefingRun` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:53` |
| `ResearchBriefingWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:118` |
| `ResearchRunDrillInLinks` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:42` |
| `ResearchSavedComparison` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:84` |
| `ResearchSavedComparisonMode` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:73` |
| `ResearchWhatChangedItem` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:106` |
| `ResolveBreakCase` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:27` |
| `ResolveReconciliationBreakRequest` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1035` |
| `ResolveSecurityMasterMappings` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:21` |
| `ResolveSourceConflictRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:58` |
| `RestatementCandidateDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:184` |
| `RetainedLedgerAmountDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:580` |
| `ReviewOnboardingWorkspaceRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:34` |
| `ReviewReconciliationBreakRequest` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:1025` |
| `RevokePreparerAttestationRequestDto` | Documented | `src/Meridian.Contracts/Workstation/SinglePrincipalAttestationDtos.cs:42` |
| `RunAttributionSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:923` |
| `RunCashFlowSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:970` |
| `RunCashLadder` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:956` |
| `RunComparisonDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:757` |
| `RunComparisonRequest` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:801` |
| `RunDiffRequest` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:806` |
| `RunFillEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:894` |
| `RunFillSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:905` |
| `RunLotSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1143` |
| `RunPortfolioDrillInSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:985` |
| `RunReconciliation` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:26` |
| `SampleArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:55` |
| `SampleBreakDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:46` |
| `SampleHighlightDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:59` |
| `SampleHoldingDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:38` |
| `SampleMarketHistoryDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:57` |
| `SampleWorkspaceDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:23` |
| `SecurityClassificationSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:8` |
| `SecurityEconomicDefinitionSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:27` |
| `SecurityIdentityDrillInDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:56` |
| `SecurityMasterAccountingIssueDto` | Documented | `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs:331` |
| `SecurityMasterCanonicalMergeOutcomeDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:175` |
| `SecurityMasterCanonicalMergeOutcomeKind` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:156` |
| `SecurityMasterChangeHistoryItemDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:208` |
| `SecurityMasterConflictAssessmentDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:159` |
| `SecurityMasterConflictAuthorityDecision` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:195` |
| `SecurityMasterConflictRecommendationKind` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:41` |
| `SecurityMasterConflictResolutionDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:132` |
| `SecurityMasterDeclaredExerciseOutcomeDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:251` |
| `SecurityMasterDeclaredExerciseStatus` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:226` |
| `SecurityMasterDownstreamImpactDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:402` |
| `SecurityMasterEconomicDefinitionDrillInDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:116` |
| `SecurityMasterEditOrigin` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:10` |
| `SecurityMasterEditResultDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:124` |
| `SecurityMasterEntitlementApplicabilityDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:540` |
| `SecurityMasterFactorPointDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:308` |
| `SecurityMasterGovernanceDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:80` |
| `SecurityMasterIdentifierSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:174` |
| `SecurityMasterImpactLinkDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:418` |
| `SecurityMasterImpactSeverity` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:16` |
| `SecurityMasterLotModelDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:333` |
| `SecurityMasterManualChangeApprovalPostureDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:567` |
| `SecurityMasterOpenLotDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:356` |
| `SecurityMasterOpenLotProvenanceDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:391` |
| `SecurityMasterOpenLotReadModelDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:342` |
| `SecurityMasterOperatingModelDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:518` |
| `SecurityMasterOperatingModelStageDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:532` |
| `SecurityMasterOperatorMetadataDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:556` |
| `SecurityMasterProviderSymbolMappingDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:186` |
| `SecurityMasterPublishResultDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:145` |
| `SecurityMasterRecommendedActionDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:425` |
| `SecurityMasterRecommendedActionKind` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:26` |
| `SecurityMasterRevisionPublishedEvent` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:205` |
| `SecurityMasterRevisionStateDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:21` |
| `SecurityMasterScheduleBlockerDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:236` |
| `SecurityMasterScheduleBookDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:270` |
| `SecurityMasterScheduleEventDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:286` |
| `SecurityMasterScheduleProvenanceDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:320` |
| `SecurityMasterScheduleSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:258` |
| `SecurityMasterSchemaCompatibilityDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:199` |
| `SecurityMasterSourceCandidateDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:147` |
| `SecurityMasterTrustPostureDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:133` |
| `SecurityMasterTrustSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:48` |
| `SecurityMasterTrustTone` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs:7` |
| `SecurityMasterWorkstationDto` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs:43` |
| `SettlementInstruction` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:19` |
| `SinglePrincipalAttestationAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/SinglePrincipalAttestationDtos.cs:102` |
| `SinglePrincipalAttestationScopeDto` | Documented | `src/Meridian.Contracts/Workstation/SinglePrincipalAttestationDtos.cs:16` |
| `SinglePrincipalAttestationStateDto` | Documented | `src/Meridian.Contracts/Workstation/SinglePrincipalAttestationDtos.cs:111` |
| `SinglePrincipalAttestationSubjectSnapshotDto` | Documented | `src/Meridian.Contracts/Workstation/SinglePrincipalAttestationDtos.cs:136` |
| `SinglePrincipalAttestationWorkspaceDto` | Documented | `src/Meridian.Contracts/Workstation/SinglePrincipalAttestationDtos.cs:125` |
| `SinglePrincipalDeclarationDto` | Documented | `src/Meridian.Contracts/Workstation/SinglePrincipalAttestationDtos.cs:49` |
| `StartWorkflow` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:18` |
| `StarterWorkspaceDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:69` |
| `StatementAccountSnapshotPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:94` |
| `StatementActivityCompletenessDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:121` |
| `StatementActivitySubtypeSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:116` |
| `StatementBreakDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:175` |
| `StatementBreakType` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:53` |
| `StatementColumnConfidenceDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:50` |
| `StatementColumnMappingDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:57` |
| `StatementConnectorDescriptorDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:9` |
| `StatementFetchScheduleDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:369` |
| `StatementFetchScheduleUpsertRequestDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:386` |
| `StatementImportCommitResultDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:165` |
| `StatementImportIssueDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:64` |
| `StatementImportPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:143` |
| `StatementImportReconciliationCaseLinkDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:197` |
| `StatementKindSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:133` |
| `StatementMappingProfileActivityCodeDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:43` |
| `StatementMappingProfileCsvOptionsDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:32` |
| `StatementMappingProfileDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:19` |
| `StatementMappingProfileFieldDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:37` |
| `StatementMatchSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:157` |
| `StatementMatchTier` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:39` |
| `StatementNormalizedCashDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:125` |
| `StatementNormalizedPositionDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:106` |
| `StatementNormalizedTransactionDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:138` |
| `StatementProfileSuggestionDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:139` |
| `StatementReconciliationAccountingScopeDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:249` |
| `StatementReconciliationBreakExplanationDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:250` |
| `StatementReconciliationCaseAttachmentDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:241` |
| `StatementReconciliationCaseAuditEventDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:259` |
| `StatementReconciliationCaseCommentDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:234` |
| `StatementReconciliationCaseCommentThreadDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:229` |
| `StatementReconciliationCaseDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:206` |
| `StatementReconciliationReportArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:220` |
| `StatementReconciliationReportArtifactGenerationDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:235` |
| `StatementReconciliationReportWorkflowDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:259` |
| `StatementReconciliationReportWorkflowStatusDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:209` |
| `StatementRecordPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:73` |
| `StatementRunBreakDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:347` |
| `StatementRunCreateDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:299` |
| `StatementRunDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:270` |
| `StatementRunExceptionDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:362` |
| `StatementRunReconcileRequestDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:317` |
| `StatementRunStatus` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:9` |
| `StatementRunSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:325` |
| `StatementRunValidationDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:339` |
| `StatementSourceDto` | Documented | `src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs:76` |
| `StatementToReportArtifactDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:328` |
| `StatementToReportWorkflowDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:343` |
| `StatementToReportWorkflowStatusDto` | Documented | `src/Meridian.Contracts/Workstation/StatementConnectorDtos.cs:316` |
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
| `StrategyDesignRunBacktestResponse` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:211` |
| `StrategyDesignRunTraceEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:120` |
| `StrategyDesignTemplate` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:61` |
| `StrategyDesignTransition` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:35` |
| `StrategyDesignValidationMessage` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:73` |
| `StrategyDesignValidationResult` | Documented | `src/Meridian.Contracts/Workstation/StrategyDesignDtos.cs:82` |
| `StrategyRunAcceptanceChecklistItemDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:296` |
| `StrategyRunAcceptanceChecklistStatusDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:284` |
| `StrategyRunArtifactCompleteness` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:675` |
| `StrategyRunCashFlowDigest` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1022` |
| `StrategyRunComparison` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:653` |
| `StrategyRunContinuityDetail` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1100` |
| `StrategyRunContinuityDto` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1090` |
| `StrategyRunContinuityLineage` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1014` |
| `StrategyRunContinuityLink` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:999` |
| `StrategyRunContinuitySeamHealthStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1058` |
| `StrategyRunContinuityStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1068` |
| `StrategyRunContinuityWarning` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1043` |
| `StrategyRunContinuityWarningSeverity` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:1050` |
| `StrategyRunCrossModeTransitionMetadata` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:726` |
| `StrategyRunDetail` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:450` |
| `StrategyRunDiff` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:809` |
| `StrategyRunDrillInLinks` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:6` |
| `StrategyRunEngine` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:22` |
| `StrategyRunEvidenceLoop` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:310` |
| `StrategyRunExecutionSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:135` |
| `StrategyRunGovernanceHook` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:116` |
| `StrategyRunGovernanceSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:176` |
| `StrategyRunHistoryQuery` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:704` |
| `StrategyRunIdentity` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:189` |
| `StrategyRunLineageEventType` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:694` |
| `StrategyRunLineageTimelineEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:737` |
| `StrategyRunLiveStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:66` |
| `StrategyRunMode` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:11` |
| `StrategyRunPaperStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:93` |
| `StrategyRunPromotionState` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:50` |
| `StrategyRunPromotionSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:151` |
| `StrategyRunReviewPacketDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:387` |
| `StrategyRunStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:35` |
| `StrategyRunSummary` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:205` |
| `StrategyRunTimelineEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:711` |
| `StrategyRunTimelineProjection` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:686` |
| `StrategySavedComparison` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:48` |
| `StrategySavedComparisonMode` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:37` |
| `StrategySweepObjectiveRanking` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:237` |
| `StrategySweepResultGroup` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:246` |
| `StrategyWhatChangedItem` | Documented | `src/Meridian.Contracts/Workstation/StrategyBriefingDtos.cs:70` |
| `StructuredReportingExportColumnDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:386` |
| `StructuredReportingExportDataDictionaryFieldDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:391` |
| `StructuredReportingExportDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:360` |
| `StructuredReportingExportPayloadDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:408` |
| `StructuredReportingExportPurposeDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:350` |
| `StructuredReportingExportRequestDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:421` |
| `StructuredReportingExportRowLineageDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:403` |
| `StructuredReportingExportValidationCheckDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:398` |
| `SubmitForApproval` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:28` |
| `SubmitSecurityMasterRevisionRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:73` |
| `SymbolAttributionEntry` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:913` |
| `SyncCompletenessResult` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:13` |
| `SyncValidationResult` | Documented | `src/Meridian.Contracts/Workstation/CashOperationsDtos.cs:16` |
| `ThresholdBreachDto` | Documented | `src/Meridian.Contracts/Workstation/CollateralExposureDtos.cs:35` |
| `TradingAcceptanceGateDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:106` |
| `TradingAcceptanceGateStatusDto` | Documented | `src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs:30` |
| `TradingBrokerageAffectedRunDto` | Documented | `src/Meridian.Contracts/Workstation/TradingBrokerageRecoveryDtos.cs:28` |
| `TradingBrokerageRecoveryDto` | Documented | `src/Meridian.Contracts/Workstation/TradingBrokerageRecoveryDtos.cs:4` |
| `TradingBrokerageRecoveryPortfolioDto` | Documented | `src/Meridian.Contracts/Workstation/TradingBrokerageRecoveryDtos.cs:13` |
| `TradingBrokerageRecoveryRequestDto` | Documented | `src/Meridian.Contracts/Workstation/TradingBrokerageRecoveryDtos.cs:36` |
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
| `UpdateOnboardingCriteriaRequestDto` | Documented | `src/Meridian.Contracts/Workstation/OnboardingDtos.cs:21` |
| `UpdateSecurityFieldRequest` | Documented | `src/Meridian.Contracts/Workstation/SecurityMasterWorkbenchCommandDtos.cs:41` |
| `ValidateLedgerDraft` | Documented | `src/Meridian.Contracts/Workstation/FundWorkflowCommands.cs:24` |
| `ValuationFreshnessPreviewDto` | Documented | `src/Meridian.Contracts/Workstation/MarkFreshnessDtos.cs:16` |
| `VersionedReportTemplateIdDto` | Documented | `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs:1427` |
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
| `WorkspaceModeDto` | Documented | `src/Meridian.Contracts/Workstation/FirstRunDtos.cs:61` |
| `WorkspaceWorkflowSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkflowSummaryDtos.cs:20` |
| `WorkstationAccountingAgingBucketPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:834` |
| `WorkstationAccountingAlertPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:851` |
| `WorkstationAccountingCashFlowSummaryPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:802` |
| `WorkstationAccountingControlCenterPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:855` |
| `WorkstationAccountingDrillLinkPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:847` |
| `WorkstationAccountingOwnerWorkloadPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:838` |
| `WorkstationAccountingPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:917` |
| `WorkstationAccountingRunCashFlowPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:791` |
| `WorkstationAccountingRunGovernancePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:774` |
| `WorkstationAccountingRunReconciliationPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:780` |
| `WorkstationAccountingRunRecord` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:812` |
| `WorkstationAccountingSeverityCountPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:830` |
| `WorkstationAccountingTrendSnapshotPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:842` |
| `WorkstationAccountingWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:448` |
| `WorkstationBrokerageAccountDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:43` |
| `WorkstationBrokerageAccountLinkDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:52` |
| `WorkstationBrokerageSyncHealth` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:8` |
| `WorkstationBrokerageSyncRunRequestDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:36` |
| `WorkstationBrokerageSyncStatusDto` | Documented | `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs:68` |
| `WorkstationCrossScreenCount` | Documented | `src/Meridian.Contracts/Workstation/CrossScreenCountsDtos.cs:16` |
| `WorkstationCrossScreenCountsDto` | Documented | `src/Meridian.Contracts/Workstation/CrossScreenCountsDtos.cs:9` |
| `WorkstationDataBackfillRecord` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1128` |
| `WorkstationDataExportRecord` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1139` |
| `WorkstationDataPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1151` |
| `WorkstationDataProviderDiagnostic` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1084` |
| `WorkstationDataProviderRecord` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1106` |
| `WorkstationDataProviderRoutingSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:1094` |
| `WorkstationGeneratedReportWriterGridPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:587` |
| `WorkstationKernelAlertThresholdsPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:877` |
| `WorkstationKernelCriticalSeverityRatePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:884` |
| `WorkstationKernelDomainPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:893` |
| `WorkstationKernelDriftPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:872` |
| `WorkstationKernelLatencyPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:867` |
| `WorkstationKernelObservabilityPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:904` |
| `WorkstationMetricCard` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:20` |
| `WorkstationModeComparisonGroup` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:128` |
| `WorkstationModeComparisonRun` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:102` |
| `WorkstationPlotToolFocusPointPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:224` |
| `WorkstationPlotToolLegendItemPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:218` |
| `WorkstationPlotToolMomentPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:267` |
| `WorkstationPlotToolPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:309` |
| `WorkstationPlotToolPointPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:208` |
| `WorkstationPlotToolRegressionPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:273` |
| `WorkstationPlotToolSampleRowPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:277` |
| `WorkstationPlotToolSignalCardPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:230` |
| `WorkstationPlotToolStatisticsPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:286` |
| `WorkstationPlotToolStudyPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:298` |
| `WorkstationPlotToolSummaryItemPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:213` |
| `WorkstationPlotToolSummaryTilePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:260` |
| `WorkstationPlotToolTabState` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:192` |
| `WorkstationPlotToolTickPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:205` |
| `WorkstationPlotToolWorkspacePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:237` |
| `WorkstationPortfolioPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:955` |
| `WorkstationPortfolioRunRow` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:936` |
| `WorkstationPortfolioSummaryPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:971` |
| `WorkstationPortfolioSummaryTelemetry` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:962` |
| `WorkstationProviderHealthCount` | Documented | `src/Meridian.Contracts/Workstation/CrossScreenCountsDtos.cs:19` |
| `WorkstationReportAccessAuditSummaryDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:674` |
| `WorkstationReportPackDistributionPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:658` |
| `WorkstationReportWriterDatasetSourcePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:495` |
| `WorkstationReportWriterFieldPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:487` |
| `WorkstationReportWriterFilterPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:481` |
| `WorkstationReportWriterFormulaPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:476` |
| `WorkstationReportWriterGridPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:516` |
| `WorkstationReportWriterMetricPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:471` |
| `WorkstationReportingDailyWorkItemDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:639` |
| `WorkstationReportingDeliveryPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:705` |
| `WorkstationReportingDeliveryReceiptPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:693` |
| `WorkstationReportingHistoryPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:765` |
| `WorkstationReportingPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:734` |
| `WorkstationReportingProfilePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:459` |
| `WorkstationReportingRunLinkPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:569` |
| `WorkstationReportingRunNextActionPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:577` |
| `WorkstationReportingRunPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:600` |
| `WorkstationReportingTemplatePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:533` |
| `WorkstationRiskGuardrail` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:388` |
| `WorkstationRunDigest` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:115` |
| `WorkstationRunDrillInLinks` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:34` |
| `WorkstationSecurityCoverageGapPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:59` |
| `WorkstationSecurityCoveragePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:65` |
| `WorkstationSecurityCoverageReferencePayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:42` |
| `WorkstationSecurityCoverageStatus` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:486` |
| `WorkstationSecurityReference` | Documented | `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs:503` |
| `WorkstationSessionPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:165` |
| `WorkstationSessionWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:153` |
| `WorkstationStrategyPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:321` |
| `WorkstationStrategyRunCard` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:76` |
| `WorkstationStrategyWorkspaceSummary` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:181` |
| `WorkstationTableExportDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationTableViewDtos.cs:36` |
| `WorkstationTableExportRequest` | Documented | `src/Meridian.Contracts/Workstation/WorkstationTableViewDtos.cs:32` |
| `WorkstationTableViewDto` | Documented | `src/Meridian.Contracts/Workstation/WorkstationTableViewDtos.cs:4` |
| `WorkstationTableViewSaveRequest` | Documented | `src/Meridian.Contracts/Workstation/WorkstationTableViewDtos.cs:19` |
| `WorkstationTimelineCard` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:136` |
| `WorkstationTradingBrokerageState` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:415` |
| `WorkstationTradingFillRow` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:368` |
| `WorkstationTradingOrderRow` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:355` |
| `WorkstationTradingPayload` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:430` |
| `WorkstationTradingPositionRow` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:336` |
| `WorkstationTradingRiskState` | Documented | `src/Meridian.Contracts/Workstation/WorkstationBootstrapDtos.cs:401` |
| `WorkstationWatchlist` | Documented | `src/Meridian.Contracts/Workstation/ResearchBriefingDtos.cs:29` |
| `WorkstationWorkspaceDefinition` | Documented | `src/Meridian.Contracts/Workstation/WorkstationWorkspaceCatalog.cs:6` |

## Follow-up Queue

No API contract coverage gaps detected.

---

*This dashboard is auto-generated. Do not edit manually.*
