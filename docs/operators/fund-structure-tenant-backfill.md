---
title: Fund Structure Tenant Backfill
status: active
owner: core-team
reviewed: 2026-09-11
audience: operators
---

# Fund Structure Tenant Backfill

This maintenance command prepares legacy fund-structure rows for the existing fail-closed
tenant read policy. It previews a complete attribution plan and applies only the exact plan
reviewed by an operator. It does not activate strict reads or run automatically at startup.
Deployment acceptance still requires a reviewed survey of the real retained data and the
browser/WPF read results for the intended tenants.

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

The JSON plan carries the algorithm version, application module version, schema fingerprint,
database identity fingerprint, complete retained evidence, proposed stamps, exception queue,
and projected strict-read node counts. The database fingerprint includes the connected server,
database, role, and server start identity; a restart or changed target requires another preview.
Treat this output as sensitive operational evidence and save it to a restricted location.

Prior unresolved quarantine does not disappear when new evidence becomes derivable. Such rows
block apply until a separate, evidence-backed resolution review is recorded in the existing
quarantine store. A prior resolution that conflicts with the new plan also blocks apply. This
command never silently resolves or overwrites an operator's resolution.

## Preview and review

Use a maintenance window: even preview temporarily locks the retained graph and ledger evidence.
Configure these existing environment settings through your approved secret-injection mechanism:

| Setting | Purpose |
| --- | --- |
| `MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING` | Fund-structure database connection, with explicit database name |
| `MERIDIAN_FUND_STRUCTURE_SCHEMA` | Fund schema; default `fund_structure` |
| `MERIDIAN_LEDGER_CONNECTION_STRING` | Ledger evidence connection, with explicit database name |
| `MERIDIAN_LEDGER_SCHEMA` | Ledger schema; default `ledger` |

The normal reviewed schema migration procedure must first install fund-structure migration 005
and the ledger migrations. The command does not run migrations or accept credentials in flags.
Use the same deployed binary and configuration for preview and apply.

```text
Meridian --fund-tenant-backfill --mode preview --output tenant-plan.json --timeout-seconds 60
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

Apply requires both schemas to reside in the same explicitly configured host/port/database.
It uses the fund-structure connection role, which must have permission to read/lock both schemas,
stamp tenant columns, and retain quarantine and receipt rows. Split-database configurations can
produce previews but are blocked from apply. A future coordinated migration protocol is needed
for those deployments; there is no override that pretends separate transactions are atomic.

```text
Meridian --fund-tenant-backfill --mode apply --run-id <uuid> --plan-hash <reviewed-hash> --operator <operator-id> --review-reference <decision-reference> --output tenant-receipt.json --timeout-seconds 60
```

The command locks the ledger registry and books in SHARE mode, then locks the fund tables in
sorted order in SHARE ROW EXCLUSIVE mode. Locks have a five-second acquisition timeout; the
command deadline defaults to 60 seconds and accepts 1–300 seconds. Existing writers may cause
a refusal or deadlock abort; retry preview after the competing maintenance operation finishes.
Both the ledger evidence and graph stay locked through one transaction's commit. Apply rebuilds
and fingerprints the current plan under these locks, rejecting any changed evidence, graph,
quarantine, schema, code, or database identity.

Eligible stamps, unresolved exceptions, and an immutable receipt commit together. Cancellation,
connection failure, or receipt failure before commit rolls all changes back. A timeout or lost
response around commit may have an uncertain client outcome. Retry with exactly the same run ID,
hash, operator ID, and review reference to retrieve the retained receipt; changing those fields
for an existing run is refused. A failure writing the local receipt file does not reverse a
database commit. Never infer rollback solely from a missing local file or failed command exit.

Receipts reject UPDATE, DELETE, and TRUNCATE through database triggers. Administrative database
owners can change those controls, so receipts do not replace independent audit retention.
Normal error output includes an exception type and recovery instruction, never connection
strings or raw database exception messages.

## Evidence and remaining deployment work

`FundStructureTenantBackfillTests` covers deterministic planning, conflicting components,
separate-database refusal, stale review, prior quarantine, cancellation, and receipt retries.
`FundStructureTenantBackfillPostgresTests` uses unique disposable schemas and real migrations
for preview/apply, tenant read counts, stale evidence, conflicts, concurrent retries, rollback,
writer-lock cancellation, and source connection loss. Hosted PostgreSQL results are required;
the targeted workflow skips database tests when its Docker-disable setting is active.

These fixture scenarios do not authorize or demonstrate a backfill against a user or production
database. Before any later enforcement change, retain the real deployment's complete survey,
resolve its exception queue, review resulting tenant counts, and verify authorized and denied
reads through both workstations. No real deployment attribution or enforcement change is part
of this implementation.
