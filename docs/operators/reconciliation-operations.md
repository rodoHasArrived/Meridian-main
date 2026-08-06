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

- Readiness and diagnostics entry points:

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080
curl http://localhost:8080/api/workstation/operator/inbox
curl http://localhost:8080/api/workstation/reconciliation/queue
curl http://localhost:8080/api/workstation/reconciliation/statement-connectors
curl http://localhost:8080/api/workstation/reconciliation/statement-fetch-schedules
curl http://localhost:8080/api/workstation/reconciliation/margin-control
```

- Support recovery commands follow per-provider policy in
  [provider validation evidence schema](../reference/provider-validation-evidence-schema.md).

## Cross-Runbook Links

- [Operator Preflight Checklist](preflight-checklist.md)
- [Failover and Recovery](failover-and-recovery.md)
- [Provider Credential Operations](provider-credentials.md)
- [Fund Ops Persistence Cutover](fund-ops-persistence-cutover.md) for approval ownership,
  cutover evidence, and recovery rollback gates.

## Migration source

- Legacy source: [archive/docs/operations/reconciliation-operations.md](../../archive/docs/operations/reconciliation-operations.md)  
- Archive copy: [archive/docs/operations/reconciliation-operations.md](../../archive/docs/operations/reconciliation-operations.md)
