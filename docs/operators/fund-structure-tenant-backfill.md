---
title: Fund Structure Tenant Backfill
status: active
owner: core-team
reviewed: 2026-09-28
audience: operators
---

# Fund Structure Tenant Backfill

Strict tenant enforcement is the supported host default. An omitted `TenantScopeEnforcement`
setting selects `fail-closed`; `MERIDIAN_TENANT_SCOPE_ENFORCEMENT` overrides the JSON setting.
The value is read once and requires a restart. Invalid explicit values refuse startup.

This maintenance command prepares legacy fund-structure, ledger-book, period, continuity, and
configured fund-account rows. It previews the retained attribution plan and applies only the
exact plan reviewed by an operator. It does not run automatically at startup.
Deployment acceptance still requires a reviewed survey of the real retained data and the
browser/WPF read results for the intended tenants.

## Upgrade without losing access to retained data

1. Back up the deployment and retain a migration review. Complete normal schema migrations.
2. If the existing single-company installation still needs maintenance access, explicitly set
   `"TenantScopeEnforcement": "deployment-boundary"` before upgrading, or set the environment
   override to `deployment-boundary`. Startup logs identify this temporary compatibility posture;
   it does not provide tenant isolation and is unsuitable for a shared tenant deployment.
3. Review stored accounts and `MDC_USERS`, including the ordinary first-run administrator.
   A missing `CompanyId` remains valid for account administration but cannot authorize scoped
   finance reads. Use the governed account-upsert operation (`UserAccountUpsertRequestDto.CompanyId`)
   to retain the reviewed company assignment and rationale; sign out and back in afterward.
   Verify the resulting tenant/company context against `fund_profile_tenancy`. Company identity
   is not evidence for assigning ownership to legacy records.
4. Preview and apply the data attribution below. Repair ambiguous ownership evidence through its
   existing governed workflow, then explicitly review any quarantine release. Never replace a
   nonblank tenant, infer one from the current login, or edit an immutable audit snapshot directly.
5. Set `TenantScopeEnforcement` to `fail-closed` (or remove the temporary setting and environment
   override), restart, and verify positive and denied browser, WPF, and retained-worker reads.

Strict startup inspects every configured posture-sensitive PostgreSQL store after schema
initialization and before workers or HTTP service. Missing/unscoped attribution, unresolved
quarantine, dangling ownership, and tenant mismatches refuse startup with table/count diagnostics
and this remediation path. Unavailable inspection also refuses startup. The check is read-only:
it does not delete records or silently present retained data as an empty ledger.

The browser and WPF hosts' unpartitioned local fund/account snapshots cannot establish tenant ownership. Strict local
access to those services is explicitly refused with a migration-required error; the retained files
remain available for the supported snapshot import into the durable host and reviewed backfill.
Server-backed workstation services remain available. Explicit single-company migration compatibility
permits the old local access while that migration is performed.

## Attribution and exceptions

The source of tenant identity is the retained ledger book's fund-profile reference joined to
`fund_profile_tenancy`. The book's structure node and kind must match the retained graph.
`CompanyId` is retained as separate evidence; it never substitutes for `TenantId`. Missing,
conflicting, duplicate, or unscoped (`all`) ownership evidence cannot supply a tenant.

The plan includes every retained node, typed parent/child reference, ownership link, assignment,
ledger book, matching registry entry, and quarantine row. Historical references remain relevant
to this conservative survey. Conflicts, cycles, dangling references, and non-ownership links
quarantine their whole connected ownership component and all dependent edges and assignments.
Independent components with unambiguous evidence may be stamped while exceptions remain open.
Existing nonblank tenant values are never reassigned. Only SQL NULL or whitespace-only values
are eligible for first attribution.

The JSON plan carries the algorithm version, application and storage module versions, schema fingerprint,
database identity fingerprint, complete retained evidence, proposed stamps, exception queue,
and projected strict-read node counts. The database fingerprint includes the connected server,
database, role, and server start identity; a restart or changed target requires another preview.
Treat this output as sensitive operational evidence and save it to a restricted location.
Preview and receipt files are created owner-readable/writable on Unix, including replacement of
over-permissive files. Windows deployments must supply an appropriately restricted ACL.

Prior unresolved quarantine does not disappear when new evidence becomes derivable. Such rows
block apply until a separate, evidence-backed resolution review is recorded in the existing
quarantine store. A prior resolution that conflicts with the new plan also blocks apply. This
command never silently resolves or overwrites an operator's resolution.

The extended plan also includes retained ledger books, accounting periods, continuity workflows,
and account definitions when fund accounts are configured. Their owners derive from retained book
or structure references. An accounting period already covered by `ledger_event_audit_events` is
quarantined as `AuditedPeriodRequiresGovernedTenantRepair` if its tenant is missing; raw backfill
would invalidate its audit snapshot. Keep that record retained and cutover blocked until an
audit-preserving repair is reviewed. Records with unresolved ownership likewise remain quarantined,
with explicit reasons in the plan and receipt, rather than being discarded.

## Preview and review

Use a maintenance window: even preview temporarily locks the retained graph and ledger evidence.
Configure these existing environment settings through your approved secret-injection mechanism:

| Setting | Purpose |
| --- | --- |
| `MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING` | Fund-structure database connection, with explicit database name |
| `MERIDIAN_FUND_STRUCTURE_SCHEMA` | Fund schema; default `fund_structure` |
| `MERIDIAN_LEDGER_CONNECTION_STRING` | Ledger evidence connection, with explicit database name |
| `MERIDIAN_LEDGER_SCHEMA` | Ledger schema; default `ledger` |
| `MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING` | Include configured fund-account definitions in the plan |
| `MERIDIAN_FUND_ACCOUNTS_SCHEMA` | Account schema; default `fund_accounts` |

The normal reviewed schema migration procedure must first install fund-structure migration 005
and the ledger migrations. The command does not run migrations or accept credentials in flags.
Use the same deployed binary and configuration for preview and apply.

```text
Meridian --fund-tenant-backfill --action preview --output tenant-plan.json --timeout-seconds 60
```

Review the complete evidence and proposed stamps, the separate CompanyId/TenantId values, every
exception, and `BlockingReasons`. `StrictReadCounts` estimates visibility for the existing
fail-closed node predicate before and after the proposed stamps. It does not certify all routes,
workstation workflows, or production completeness. `AttributionComplete` stays false while the
current plan has exceptions or blockers.

Record the plan's `PlanHash`, the deployed code/schema identity, reviewer decision, and evidence
location in the migration review. Operator identity and review reference provide traceability;
the command's authority is the database role and retained ownership evidence. It does not
authenticate a human reviewer or turn free text into ownership authority.

## Apply and recover

Apply requires all included schemas to reside in the same explicitly configured host/port/database.
It uses the fund-structure connection role, which must have permission to read/lock all included schemas,
stamp tenant columns, and retain quarantine and receipt rows. Split-database configurations can
produce previews but are blocked from apply. A future coordinated migration protocol is needed
for those deployments; there is no override that pretends separate transactions are atomic.

```text
Meridian --fund-tenant-backfill --action apply --run-id <uuid> --plan-hash <reviewed-hash> --operator <operator-id> --review-reference <decision-reference> --output tenant-receipt.json --timeout-seconds 60
```

The command locks the ledger registry and audit evidence in SHARE mode, ledger books, periods,
and continuity workflows in SHARE ROW EXCLUSIVE mode, then locks the fund tables in
sorted order in SHARE ROW EXCLUSIVE mode. Locks have a five-second acquisition timeout; the
command deadline defaults to 60 seconds and accepts 1–300 seconds. Existing writers may cause
a refusal or deadlock abort; retry preview after the competing maintenance operation finishes.
Both the ledger evidence and graph stay locked through one transaction's commit. Apply rebuilds
and fingerprints the current plan under these locks, rejecting any changed evidence, graph,
quarantine, schema, code, or database identity.

Legacy accounts represented only by retained links or assignments appear in the plan as inferred
Account nodes. Preview creates no rows; apply materializes attributable accounts and their tenant
stamps in the same transaction. Unattributable accounts remain quarantined. Explicit `all` ledger
tenant stamps are unscoped evidence and quarantine their component; they are not missing values.

Eligible stamps, unresolved exceptions, and an immutable receipt commit together. Cancellation,
connection failure, or receipt failure before commit rolls all changes back. A timeout or lost
response around commit may have an uncertain client outcome. Retry with exactly the same run ID,
hash, operator ID, and review reference to retrieve the retained receipt; changing those fields
for an existing run is refused. Receipt recovery reads only the fund receipt table before opening
source connections or taking graph locks, so a busy writer or unavailable ledger source does not
prevent retrieval. A second receipt check under mutation locks handles concurrent retries.
A failure writing the local receipt file does not reverse a
database commit. Never infer rollback solely from a missing local file or failed command exit.

Receipts reject UPDATE, DELETE, and TRUNCATE through database triggers. Administrative database
owners can change those controls, so receipts do not replace independent audit retention.
Normal error output includes an exception type and recovery instruction, never connection
strings or raw database exception messages.

## Explicit quarantine resolution

Once governed source evidence has been corrected, ordinary preview/apply still refuses to erase
an earlier unresolved quarantine decision. Review a resolution plan using:

```text
Meridian --fund-tenant-backfill --action preview-resolution --output tenant-resolution-plan.json
Meridian --fund-tenant-backfill --action resolve --run-id <new-uuid> --plan-hash <reviewed-resolution-hash> --operator <operator-id> --review-reference <decision-reference> --output tenant-resolution-receipt.json
```

Only rows whose current retained evidence derives exactly one owner can be released. The command
accepts no caller-supplied tenant assignment. Resolutions, first tenant stamps, and the immutable
receipt commit together under the same locks; old quarantine evidence is retained. New ambiguity,
stale evidence, or a conflicting prior resolution refuses the transaction. Quarantine release requires
`resolve`; ordinary `apply` cannot release those rows. Retry the exact run identity and review fields
after an uncertain response to recover the retained receipt.

## Evidence and remaining deployment work

`FundStructureTenantBackfillTests` covers deterministic planning, conflicting components,
separate-database refusal, stale review, prior quarantine, cancellation, and receipt retries.
`FundStructureTenantBackfillPostgresTests` uses unique disposable schemas and real migrations
for preview/apply, tenant read counts, stale evidence, conflicts, concurrent retries, rollback,
writer-lock cancellation, and source connection loss. Hosted PostgreSQL results are required;
the targeted workflow skips database tests when its Docker-disable setting is active.

These fixture scenarios do not authorize or demonstrate a backfill against a user or production
database. Before activating strict service on an existing installation, retain its complete survey,
resolve its exception queue, review resulting tenant counts, and verify authorized and denied
reads through both workstations. No real deployment attribution or enforcement change is part
of this implementation.

Strict host composition respects the final tenant posture even if a host overrides DI options after
core services are registered. Direct-lending accrual and outbox workers currently lack retained
authority per loan; strict hosts withhold these workers and log the reason. Tenant attribution is
required before enabling them under strict scope, and this procedure does not enable them.
