# Reconciliation Operations

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-07-18

This lane is the canonical operator procedure page for reconciliation exception response, recovery prioritization, and evidence capture.

## Canonical Scope

- Use this page for active reconciliation posture, break handling flow, and escalation routing.
- Use [reference lookup tables](../reference/reconciliation-break-taxonomy.md) for taxonomy, severity, and reason code definitions.
- Use [provider status artifacts](../reference/provider-integration-status.md) for provider-linked reconciliation impact.

## Reconciliation Triage Sequence

For the new three-source workflow, see [Ledger, OMS and broker comparison](#ledger-oms-and-broker-comparison).

Complete [preflight](preflight-checklist.md) first. Use the signed-in Windows workstation or, for
API inspection, PowerShell 7 from the repository root with the preflight session in terminal 2.
Installed hosts use the supervisor-selected URL. Confirm the operator's company/tenant and the
fund/account, book, period, and provider connection for the affected case; visibility into another
workflow is not authority to change this one. Read permissions and mutation/approval permissions
are checked separately. For exact statement-to-report inputs, use the
[statement reconciliation procedure](statement-reconciliation-report-operations.md).

1. Identify break class and severity from latest operator inbox.
2. Confirm whether the break is:
   - ingest-only,
   - pricing/valuation mismatch,
   - workflow assignment mismatch,
   - provider outage/fallback drift.
3. Apply the fastest safe containment action:
   - pause affected non-verified routes,
   - disable auto-escalation for the queue when required,
   - isolate affected accounts/entities if blast radius is high.
4. Capture evidence:
   - break identifiers,
   - impacted scope and value,
   - root-cause hypothesis and confidence,
   - command history and operator actions.
5. Escalate for approval/reopen only when automated recovery would alter authoritative records.

## Import Broker Or Custodian Statements

1. Open `Accounting` -> `Import statement`.
2. Choose the source path:
   - `File upload` for CSV, OFX/QFX, IB Flex XML, ISO 20022 camt.053, BAI2, or connector JSON; or
   - `Scheduled fetch` for a fetch-capable provider connection such as Alpaca or the IB Flex Web
     Service.
3. Review the detected canonical columns, per-column confidence, position/transaction/cash/fee/
   dividend record counts, sample rows, mapping-profile suggestion, and format-drift warnings. For
   brokerage sources, also review the provider account margin snapshot, activity subtype summary,
   paging-completeness posture, and retained option, tax-lot, and borrow counts.
4. For a new CSV or OFX layout, clone or create a declarative mapping profile, edit its field aliases
   and activity codes, save it, and confirm that live preview is ready before commit. This does not
   require a Meridian release.
5. For file import, enter the institution, Meridian fund account, external account, and statement
   period, then commit. For remote import, enter the same account scope, classify the statement as
   `Broker` or `Custodian`, preview the provider data, then save a cadence or select `Run now` on an
   existing schedule. Legacy schedules without this field remain classified as broker imports.
6. Open the returned Evidence Vault route to inspect retained source/canonical proof and open the
   returned reconciliation route for break or case review.

Statement ingress is bounded, and a breach is refused rather than truncated — no partially imported
statement is ever committed. A refusal surfaces as a blocking issue on preview and validate, and as a
failed commit, carrying one of `STATEMENT_DOCUMENT_TOO_LARGE` (over 20 MiB),
`STATEMENT_TOO_MANY_RECORDS` (over 250,000 retained rows, counting the retained margin, activity,
tax-lot, and borrow evidence alongside canonical rows), `STATEMENT_LINE_TOO_LONG`,
`STATEMENT_TOO_MANY_LINES`, `STATEMENT_NESTING_TOO_DEEP`, `STATEMENT_SUBTREE_TOO_LARGE`,
`STATEMENT_TOO_MANY_NODES`, or `STATEMENT_TOO_MANY_DIAGNOSTICS` (over 25,000 retained parse issues —
a file failing that many rows is malformed at a scale no operator can reconcile row by row, so correct
the export or the mapping profile rather than reviewing the diagnostics). An IB Flex import reports
the same retained-row ceiling as
`ROW_LIMIT_EXCEEDED`. A refusal can reflect a large legitimate export or malformed input; verify
the source and row counts with the institution. Split a legitimate export only when each segment
can be imported under a valid, separately governed accounting scope. The statement-to-report
workflow requires exact ledger-period dates, so an arbitrary shorter window is not a substitute.
Otherwise repair the export/mapping or ask the deployment owner to assess the bound. Raising a
bound is a deployment change, not an operator
setting: it is a single registration documented in `src/Meridian.FinancialOperations/README.md`, and
it should be raised only for the bound that actually refused, with the reason recorded.

Scheduled-fetch credentials remain in the existing provider credential vault. The statement screen
never asks for, displays, or persists API keys. A transient fetch failure advances the schedule
watermark, so the next automatic retry respects the configured cadence; the schedule row shows a
stable failure type without exposing upstream exception text.

Alpaca activity fetches continue until the provider cursor is complete and fail closed when a page
token is missing or repeated. IB Flex fetches submit the configured query, poll its documented v3
retrieve URL with a bounded retry window, and retain the returned XML before mapping. Configure the
IB Flex `Token` and `QueryId` under provider id `ib-flex`; do not place either value in a schedule,
mapping profile, or uploaded file.

## Review And Certify Margin Evidence

1. Open `Accounting` -> `Margin control` after importing or fetching each account statement.
2. Confirm every expected provider, prime broker, and account is present. Missing accounts are a
   completeness issue, not a zero balance.
3. Compare provider-reported maintenance margin, excess liquidity, buying power, and restriction
   flags with the clearly labelled Meridian shadow estimate. The provider values are authoritative;
   the shadow is an operator diagnostic and must not drive liquidation or accounting entries.
4. Inspect activity-cursor alerts, short/borrow evidence, option lifecycle counts, tax-lot counts,
   and position-level shadow contributions before clearing a variance.
5. Treat intraday snapshots as provisional. At end of day, certify only when the surface reports an
   EOD candidate and has no stale, incomplete, or critical evidence. Certification is durable,
   permission checked, actor attributed, and scoped to the exact provider/account/as-of snapshot.
   Margin Control treats evidence older than 72 hours, or materially future-dated evidence, as stale
   and requires a fresh fetch before certification.

## Mandatory Checks

- Confirm provider/provider-connection state is current.
- Validate queue age and source-of-failure for each impacted asset class.
- Ensure break evidence is attached before status transitions from `active` to `resolved`.
- Validate any manual correction has traceability in the promotion packet.

## Command references

Use [authenticated preflight](preflight-checklist.md#authenticated-evidence-collection) to define
`$meridianBaseUrl` and `$operatorSession` while the host runs in terminal 1. The current queue
summary route is `/queue-status`:

```powershell
Invoke-RestMethod "$meridianBaseUrl/api/workstation/operator/inbox" -WebSession $operatorSession
Invoke-RestMethod "$meridianBaseUrl/api/workstation/reconciliation/queue-status" -WebSession $operatorSession
Invoke-RestMethod "$meridianBaseUrl/api/workstation/reconciliation/statement-connectors" -WebSession $operatorSession
Invoke-RestMethod "$meridianBaseUrl/api/workstation/reconciliation/statement-fetch-schedules" -WebSession $operatorSession
Invoke-RestMethod "$meridianBaseUrl/api/workstation/reconciliation/margin-control" -WebSession $operatorSession
```

- Support recovery commands follow per-provider policy in
  [provider validation evidence schema](../reference/provider-validation-evidence-schema.md).

## Expected results and failure recovery

Authenticated reads return the intended scope's queue/schedule/margin data; inspect blocking
findings even when HTTP status is `200`. A completed import retains source/canonical evidence and
a reconciliation route. A resolved break needs retained evidence and an accepted correction or
explanation; an empty queue or a successful fetch alone is insufficient.

The margin control view and its certification require tenant and company scope. They show only
provider accounts whose external account id is bound to an active fund account owned by your
tenant and company (account code, custodian sub-account, bank account number or IBAN). A retained
statement that is missing from the view usually means that binding is absent: fix the fund
account's identifiers rather than the evidence.

| Symptom | Next action |
| --- | --- |
| `401/403` or missing expected scope | Reauthenticate and verify permissions/company assignment before changing records. |
| Import limit or mapping refusal | Preserve the source/refusal, correct the export or mapping, and repeat preview before commit. |
| Fetch failed or margin evidence stale/incomplete | Check the selected vault connection and provider completeness, then fetch again within the schedule policy. Keep certification blocked. |
| Break remains after a correction | Reopen its retained source and authoritative records; follow [Verified Outcome Recovery](verified-outcome-recovery.md) before retrying a mutation with an uncertain result. |

## Cross-Runbook Links

- [Operator Preflight Checklist](preflight-checklist.md)
- [Failover and Recovery](failover-and-recovery.md)
- [Provider Credential Operations](provider-credentials.md)
- [Fund Ops Persistence Cutover](fund-ops-persistence-cutover.md) for approval ownership,
  cutover evidence, and recovery rollback gates.

## Migration source

- Legacy source: [archive/docs/operations/reconciliation-operations.md](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/operations/reconciliation-operations.md)  
- Archive copy: [archive/docs/operations/reconciliation-operations.md](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/operations/reconciliation-operations.md)

## Reconciliation SLA calendar configuration

Named SLA calendars are operator-maintained at `<DataRoot>/reconciliation/sla-calendars.json`.
The configured SLA policy selects business/holiday IDs and timezone; the file supplies their
contents. Restart the host after a reviewed calendar change. Browser and WPF share this calculation.

```json
{
  "businessCalendars": [{
    "id": "operations", "validFrom": "2026-01-01", "validThrough": "2026-12-31",
    "weekendDays": ["Saturday", "Sunday"], "holidays": []
  }],
  "holidayCalendars": [{
    "id": "bank", "validFrom": "2026-01-01", "validThrough": "2026-12-31",
    "holidays": ["2026-12-25"]
  }]
}
```

Dates above illustrate the file shape; verify actual holidays for the calendars your policy names.
Validity bounds are required and inclusive. Every selected calendar must cover every traversed
local date, including closed days. IDs are unique within their list and case-insensitive. Missing
files support only unnamed calendars. An unnamed base uses Saturday/Sunday weekends; named
calendars never silently fall back. Holiday overlays exclude their dates without adding weekends.
The configuration rejects unknown properties, duplicate IDs, out-of-range holidays, invalid weekend
values and files above 4 MiB. A business calendar may explicitly use an empty weekend list.

SLA age starts at the retained occurrence's first observation (DetectedAt for untracked cases).
A recurrence starts another occurrence; previous clear intervals remain retained. Fractional hours
are preserved across daily close, and each date resolves its timezone offset. Invalid timezones,
overnight/non-positive shifts, negative or inverted thresholds, ambiguous/nonexistent local shift
boundaries and traversal beyond 36,600 dates are refused. Correct the named policy/calendar evidence,
restart the host, and retry; do not remove calendar IDs to make a refusal look on track. Existing
pause and stop rules remain casework decisions. Source clearing does not resolve, approve or sign
off a case, and does not remove close blockers.

## Ledger, OMS and broker comparison

The bounded three-way workflow compares posted journal cash and canonical lot quantities/basis,
independent per-account `PaperTradingPortfolio` observations, and normalized retained broker
observations. It does not use the broker-backed `BrokeragePortfolioState` as independent OMS
proof. The scope is one authorized USD primary book, account and accounting period, with long
cash equities and the gross-trade-value/fees-expensed book cost convention.

Use `POST /api/fund-accounts/{accountId}/brokerage-sync/reconcile-three-way` with `ledgerBookId`,
`accountingPeriodId`, a stable `operationId`, and an optional UTC `cutoffUtc` (defaults to the current
capture). The server derives tenant/company and validates account, entity, book, period and current
broker linkage. Request bodies cannot supply source balances or snapshots. Retrieve the latest
completed result using `GET /api/fund-accounts/{accountId}/brokerage-sync/three-way-reconciliation/latest`
with both `ledgerBookId` and `accountingPeriodId` query parameters.

Every run retains independent operands, their source times, evidence references, comparison results
and a recovery receipt. Quantity tolerance is zero; cash and cost-basis tolerance are USD 0.01.
The default source-age limit is 30 minutes and pairwise timestamp drift is five minutes. Missing
opening cash, canonical lot history, independent OMS scope, source identity, freshness or cost
comparability produces `Blocked`, never a fabricated zero or a matched result. Decimal amounts are
serialized as exact strings. Paper and synthetic provenance remains visible in source, run and case
records. Replay returns the frozen completed receipt; an existing operation within its exact scope
cannot accept a changed cutoff. Use a new operation ID to compare newer source evidence.

Broker cost convention defaults to `Unknown`. Set
`Reconciliation:ThreeWay:BrokerCostBasisConvention` to `GrossTradeValueFeesExpensed` only where the
selected adapter's cost operands have that declared meaning. This setting does not establish live
provider verification. The server retains source history separately from the latest broker sync;
relinking the account creates a different evidence partition. Coherent historical journal and lot
capture is required; current lot basis is not substituted for historical basis.

Daily execution shares the existing statement scheduler's minute tick. Configure an explicit list
under `Reconciliation:ThreeWay:Schedules`, each with `TenantId`, `CompanyId`, `FinancialAccountId`,
`LedgerBookId`, `AccountingPeriodId`, and `Enabled`. An empty list enables no background account
access. The worker reauthorizes each scope, retains current independent observations on each tick,
and after `DailyRunAfterUtc` (default `00:15:00`) compares the preceding UTC day's end under a stable
`daily:yyyy-MM-dd` identity. Missing cutoff observations remain blocked; the worker does not replace
them with a later snapshot. Period rollover requires updated explicit scope configuration. Restart
the host after changing this configuration.

Breaks enter the existing governed queue with stable subject identities, retained first-seen time,
source operands and suggested investigative actions. A complete subsequent comparison may clear the
source observation; it does not approve, resolve, waive or sign off casework. Missing fills should
be recovered through their retained posting workflow. Independently evidenced adjustments continue
through existing journal review and approval. Posting an adjustment alone does not prove agreement;
rerun the comparison. These development scenarios are synthetic verification, not live activation.

### Evidence-backed missing-fee draft

The initial correction treatment supports one negative USD `FEE` activity with a stable broker
transaction ID. Its independently retained amount must exactly explain the ledger-versus-broker
cash difference, and all fee-like posted cash activity must have transaction identity coverage.
Duplicate IDs, unknown coverage, multiple possible fees, unsupported currency or an unexplained
residual produce `InsufficientEvidence`; the system does not invent a balancing entry.

Configure `Reconciliation:ThreeWay:CashAccountPath` and `FeeExpenseAccountPath` with active chart
paths in the selected tenant/company/book. Cash and expense mappings must have the correct types,
no instrument symbol, and no conflicting financial-account binding. A ready proposal identifies
its source fee, retained evidence hash, and reviewed accounting-period version. The browser's
**Prepare draft** command calls
`POST /api/fund-accounts/{accountId}/brokerage-sync/three-way-reconciliation/adjustments/prepare`
with the retained `ledgerBookId`, `accountingPeriodId`, `operationId`, `checkId`,
`expectedPeriodVersion`, and `evidenceHash`. Changed current journal, cash, broker or period evidence
requires a new comparison and review before a new draft can be prepared.

Preparation saves an adjusting expense/cash draft in the existing journal workbench. It does not
submit, approve or post it. The draft retains its fee identity, original proof and simulation mark.
Its economic identity uses tenant, provider/external account, financial account, book and fee ID,
so a second comparison cannot create another monetary effect for the same fee. An uncertain
response can retry the same request to recover the existing draft. Changed evidence for that same
fee must be reviewed through the existing governed correction workflow. Independent journal
approval and posting remain required, followed by another three-way comparison.
