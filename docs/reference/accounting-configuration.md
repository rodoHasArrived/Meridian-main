# Accounting Configuration

**Owner:** Accounting / Fund Operations
**Scope:** Local workstation API, configuration DTOs, rule dry-run behavior, manual journal lifecycle actions, and audit evidence
**Status:** Shared browser/WPF accounting configuration and lifecycle foundation

---

## Purpose

Accounting configuration lets operators prepare ledger books, chart-of-accounts nodes, journal entry templates, posting rules, validation state, and configuration audit evidence before accounting rules are activated. The rule model now supports effective-dated, scoped, condition-driven posting rules with formulas, allocation metadata, generated posting lines, dry-run previews, version history, and promotion-approval metadata.

This slice is internal-grade. Regulatory and allocator outputs remain filing-preparation or report-preparation artifacts unless a future external certification layer explicitly upgrades the output contract.

---

## Shared Contract Families

The shared ledger contract family is defined in `Meridian.Contracts.Ledger`:

| Contract | Purpose |
| --- | --- |
| `AccountingConfigurationWorkspaceDto` | Complete operator configuration workspace for one fund profile and optional ledger book. |
| `ChartOfAccountsNodeDto` | Hierarchical account path, account type, optional symbol, and optional financial-account linkage. |
| `JournalEntryTemplateDto` | Named journal template with debit and credit line definitions. |
| `PostingRuleDto` | Source event kind, effective-dated scope, conditions, formulas, allocations, generated posting lines, and optional legacy journal-template mapping. |
| `LedgerDimensionSetDto` | Shared dimensional accounting scope for fund, entity, sleeve, strategy, investor, capital account, instrument, tax lot, cost center, counterparty, and external GL dimensions. |
| `RuleDryRunRequestDto` / `RuleDryRunResultDto` | Non-posting dry-run request/result for event predicates, priority selection, generated postings, and validation issues. |
| `JournalEntryLifecycleActionRequestDto` / `JournalEntryLifecycleActionResultDto` | Governed manual journal lifecycle action request/result for approve, post, reverse, rebook, close-lock, transition audit, and generated correction drafts. |
| `ExternalGlMappingProfileDto` / `ExternalGlExportPackageDto` | Certified account/dimension mapping profiles and guarded external GL export-package artifacts. |
| `ClosePeriodPlanDto` / `CreateLateAdjustmentRequestDto` | Close-plan projection, checklist dependencies, sign-offs, materiality policy, late-adjustment requests, and period-lock posture. |
| `AccountingReportPackageRequestDto` / `AccountingReportPackageBundleDto` | Accounting report package assembly for financial statements, investor capital statements, realized gain/loss, NAV, certification, validation, and restatement metadata. |
| `AccountingConfigurationValidationIssueDto` | Validation code, severity, message, target, and suggested remediation. |
| `AccountingActionAuditEventDto` | Append-only configuration action event with actor, correlation id, hashes, validation result, and evidence links. |
| `PreviewJournalTemplateRequest` | Non-posting template preview request. |
| `UpsertPostingRuleRequest` | Posting-rule mutation request. |
| `ActivateAccountingConfigurationRequest` | Activation request for a clean configuration version. |

---

## Local API

Routes follow the existing local API shape:

| Method | Route | Behavior |
| --- | --- | --- |
| `GET` | `/api/ledger/accounting-configuration` | Returns the current configuration workspace. |
| `POST` | `/api/ledger/accounting-configuration/chart` | Creates, updates, or archives a chart node and appends an audit event. |
| `POST` | `/api/ledger/accounting-configuration/templates` | Creates, updates, or archives a journal template and appends an audit event. |
| `POST` | `/api/ledger/accounting-configuration/posting-rules` | Creates, updates, or archives a posting rule and appends an audit event. |
| `POST` | `/api/ledger/accounting-configuration/preview` | Builds a non-posting journal preview from a template. |
| `POST` | `/api/ledger/accounting-configuration/posting-rules/dry-run` | Evaluates active rules by source event, effective date, scope, conditions, and priority; returns generated non-posting lines and validation issues. |
| `POST` | `/api/ledger/accounting-configuration/activate` | Activates the configuration only when validation has no critical issue. |
| `GET` | `/api/ledger/accounting-configuration/audit` | Lists append-only accounting action audit events. |
| `POST` | `/api/ledger/journal-entry-workbench/lifecycle-action` | Applies governed manual journal lifecycle actions. Posted entries are not edited by reversal/rebook; those actions create separate correction drafts. |
| `GET` | `/api/accounting-system/mapping-profiles` | Lists scoped external GL account/dimension mapping profiles. |
| `POST` | `/api/accounting-system/mapping-profiles` | Upserts an external GL mapping profile. |
| `POST` | `/api/accounting-system/export-packages` | Creates a guarded external GL export package with posting disabled and validation/certification state. |
| `GET` | `/api/ledger/close-management/period-plan/{workflowId}` | Projects an Operations Continuity workflow as a close period plan with tasks, dependencies, sign-offs, locks, and materiality policy. |
| `POST` | `/api/ledger/close-management/late-adjustments` | Retains a late-adjustment review request and returns the updated close period plan. |
| `POST` | `/api/ledger/reports/accounting-package` | Builds a report package bundle with statement package, investor capital statement, realized gain/loss, NAV, certification, validation issues, and optional restatement workflow. |
| `GET` | `/api/ledger/reports/accounting-packages` | Lists retained accounting report packages, optionally filtered by `fundProfileId` and `periodId`. |

When `IAccountingConfigurationService` is not registered, the endpoints return `501 Not Implemented`, matching existing optional ledger endpoint behavior.

---

## Audit Coverage

Configuration mutations route through `IAccountingActionAuditStore`.

Each audit event records:

- Actor resolved from the authenticated workstation context.
- Action name.
- Fund profile and optional ledger book.
- Correlation id.
- Before and after configuration hashes.
- Validation issues observed after the mutation.
- Evidence links supplied by the caller.

Template preview and rule dry-run do not append audit events and do not persist journal entries. Posting remains owned by existing ledger posting services.

Manual journal lifecycle actions append audit events for approval, posting, close-lock, and generated correction drafts. Human action origin is required for lifecycle transitions; assistant or automation-origin requests may draft support but cannot approve, post, reverse, rebook, or lock entries.

---

## Durable Storage

When ledger Postgres storage is configured, `V_ledger_010__accounting_configuration.sql` creates:

| Table | Purpose |
| --- | --- |
| `accounting_configuration_workspaces` | Active workspace header, status, version, optional ledger book, and validation snapshot. |
| `accounting_configuration_chart_nodes` | Fund-scoped hierarchical chart-of-accounts nodes. |
| `accounting_configuration_journal_templates` | Fund-scoped journal template headers and JSONB template lines. |
| `accounting_configuration_posting_rules` | Fund-scoped source-event to template mappings. |
| `accounting_action_audit_events` | Append-only action audit events across configuration mutations and future accounting actions. |

`V_ledger_011__accounting_rule_payload.sql` adds `rule_payload` JSONB to `accounting_configuration_posting_rules` so effective-dated rules, conditions, formulas, allocation metadata, scopes, generated postings, versions, and promotion approvals survive durable PostgreSQL round trips while legacy columns remain queryable.

`PostgresAccountingConfigurationStore` implements `IAccountingConfigurationStore` and `IAccountingActionAuditStore`. The implementation uses the existing ledger schema options and keeps audit events append-only.

When Financial Operations is composed with `StorageOptions`, close late-adjustment review requests are retained at `accounting/close-management-late-adjustments.json` and accounting report package history is retained at `accounting/accounting-report-packages.json` under the configured storage root. These snapshots are written through `AtomicFileWriter` and are reprojected by the shared close-management and report-package endpoints after process restart.

---

## Browser Workflow

The browser workstation adds `/accounting/configure` as the Accounting Configure workstream.

The screen view model owns visible copy for setup status, validation issues, disabled preview reasons, template summary rows, preview status, and audit labels. React components render that view model and do not fork accounting validation or posting logic.

The same workstream now renders an Accounting Rules Studio over `PostingRuleDto` and `RuleDryRunResultDto`. Operators can inspect effective dates, priority, dimensional scope, event predicates, formulas, allocation rows, generated posting metadata, retained versions, and promotion-approval state, then run a non-posting dry-run preview through `/api/ledger/accounting-configuration/posting-rules/dry-run`. The browser sends source-event, amount, currency, effective date, counterparty, and dimension scope to the shared service; rule selection, generated balanced lines, validation issues, and explanations remain service-owned.

The browser Manual Journal Entry workbench consumes the same lifecycle endpoint for controller actions. Submitted entries can be approved or rejected, approved entries can be posted, posted entries can be close-locked, and posted entries can generate separate reversal or rebook drafts. The browser renders returned transition audit rows and generated correction drafts, but it does not mutate posted entries locally.

The Accounting route also renders a close/report package cockpit backed by the shared close-management and accounting-report endpoints. It loads the close period plan, checklist dependencies, sign-off rows, materiality policy, period-lock posture, late adjustments, retained package history, financial statement package state, investor capital statement count, realized gain/loss, NAV, certification state, restatement metadata, validation issues, and retained evidence counts from shared DTOs. The browser can request a new accounting report package with workflow, fund, period, package-seed, and evidence context, but close transitions, late-adjustment approval, package approval, statement artifact rendering, and restatement execution remain service-owned.

The Accounting route also surfaces external GL mapping profiles next to the imported provider evidence and reconciliation rows. Operators can inspect certified account/dimension mapping coverage and create a guarded external GL export package from the latest reconciliation through `/api/accounting-system/export-packages`; the browser includes mapping, reconciliation, period, fund, and evidence context, while package certification and any future live posting remain service-owned and disabled by policy.

---

## WPF Parity

The WPF Accounting workspace exposes read/parity configuration status through the existing Accounting shell:

- `FundAccountingConfigure` is a dockable Accounting page target.
- The command surface includes a Configure command.
- The inspector shows shared configuration status, chart/template/rule counts, validation posture, and latest audit evidence.

WPF intentionally consumes `IAccountingConfigurationService`, `IManualJournalEntryWorkbenchService`, `IManualJournalEntryLifecycleService`, and shared DTOs. It does not fork posting-rule evaluation, template validation, activation rules, dry-run behavior, or mutation workflows.

---

## Current Boundaries

Implemented in this slice:

- Additive shared contracts for accounting rule definitions, dry runs, generated postings, dimensions, manual journal lifecycle transitions, close/report/export/reporting productization DTOs.
- Rule dry-run service and route over the existing accounting configuration store.
- Manual journal lifecycle service and route for validate, submit, approve, reject, post, close-lock, reverse-draft, and rebook-draft actions.
- Durable PostgreSQL payload storage for rich posting-rule definitions.
- External accounting provider catalog posture for QBO, Xero, and NetSuite as import-first integrations with posting disabled.
- External GL mapping-profile list/upsert behavior and guarded export-package creation over the shared AccountingSystem endpoint group.
- Close-period plan projection over Operations Continuity workflow state, including checklist dependency ordering, sign-off rows from approvals, period-lock posture, materiality policy, and late-adjustment review requests.
- File-backed late-adjustment retention and accounting report package history when Financial Operations is registered with `StorageOptions`.
- Accounting report package bundle generation and history listing for financial statements, investor capital statement, realized gain/loss report, NAV package, certification state, validation issues, and restatement workflow metadata.
- Browser Accounting close/report package cockpit over the shared close-plan and report-package endpoints, including package-build request wiring without browser-local close or certification logic.
- Browser external GL mapping/export cockpit over the shared AccountingSystem endpoint group, including certified mapping-profile display and guarded export-package request wiring without browser-local posting logic.

Still guarded or future work:

- External GL live posting remains disabled. QBO import/reconciliation evidence, Xero/NetSuite planned import rows, mapping-profile upsert, certification-state validation, and guarded export-package artifacts exist, but live QBO/Xero/NetSuite posting still requires an explicit approved adapter and release gate.
- Close-management still relies on Operations Continuity for workflow command transitions; full sign-off matrix administration, rendered/certified statement artifact generation, package approval workflow execution, and restatement execution remain separate Financial Operations service work.
