# Accounting Configuration

**Owner:** Accounting / Fund Operations
**Scope:** Local workstation API, configuration DTOs, preview behavior, and audit evidence
**Status:** Browser-first internal configuration workflow

---

## Purpose

Accounting configuration lets operators prepare ledger books, chart-of-accounts nodes, journal entry templates, posting rules, validation state, and configuration audit evidence before accounting rules are activated.

This slice is internal-grade. Regulatory and allocator outputs remain filing-preparation or report-preparation artifacts unless a future external certification layer explicitly upgrades the output contract.

---

## Shared Contract Families

The shared ledger contract family is defined in `Meridian.Contracts.Ledger`:

| Contract | Purpose |
| --- | --- |
| `AccountingConfigurationWorkspaceDto` | Complete operator configuration workspace for one fund profile and optional ledger book. |
| `ChartOfAccountsNodeDto` | Hierarchical account path, account type, optional symbol, and optional financial-account linkage. |
| `JournalEntryTemplateDto` | Named journal template with debit and credit line definitions. |
| `PostingRuleDto` | Source event kind to journal template mapping. |
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
| `POST` | `/api/ledger/accounting-configuration/activate` | Activates the configuration only when validation has no critical issue. |
| `GET` | `/api/ledger/accounting-configuration/audit` | Lists append-only accounting action audit events. |

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

Preview does not append audit events and does not persist journal entries. Posting remains owned by existing ledger posting services.

---

## Browser Workflow

The browser workstation adds `/accounting/configure` as the Accounting Configure workstream.

The screen view model owns visible copy for setup status, validation issues, disabled preview reasons, template summary rows, preview status, and audit labels. React components render that view model and do not fork accounting validation or posting logic.
