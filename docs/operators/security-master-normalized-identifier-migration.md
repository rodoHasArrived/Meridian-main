# Security Master Normalized Identifier Uniqueness Migration

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

Migration 032 replaces raw primary-identifier uniqueness with uniqueness over
`(primary_identifier_kind, normalized_primary_identifier_value)`, matching the identifier
resolution contract.

## Prerequisites and Execution Context

- Identify the deployed application version, target PostgreSQL database, and effective
  `MERIDIAN_SECURITY_MASTER_SCHEMA` (default `security_master`). The connection comes from
  `MERIDIAN_SECURITY_MASTER_CONNECTION_STRING`, including any intentional unified-database
  inheritance described in [Database Persistence](../reference/environment-variables.md#database-persistence).
- Run the SQL below in an authenticated PostgreSQL client against that database. Replace
  `<schema>` with the verified schema identifier; it is a template placeholder. The preflight needs
  read access to `securities`; migration execution needs the deployment's schema-migration role.
  HTTP API keys and workstation permissions do not grant database access.
- The collision query requires the normalized column introduced in migration 016. If the retained
  schema predates it, have the Security Master storage owner stage the upgrade and survey on a
  restored copy before the production window; a missing column is not an empty collision result.
- Stop Security Master writers for the deployment window and retain a tested, coordinated backup
  covering dependent security references. Keep credentials out of SQL files and review packets.

## Before Deployment

Back up the Security Master schema and run this preflight against the target schema:

```sql
select
    primary_identifier_kind,
    normalized_primary_identifier_value,
    array_agg(security_id order by security_id) as security_ids
from <schema>.securities
group by primary_identifier_kind, normalized_primary_identifier_value
having count(*) > 1
order by primary_identifier_kind, normalized_primary_identifier_value;
```

An empty result is required. The ordering matches the migration's deterministic exception detail.

## Collision Remediation

For every returned group, identify whether the rows represent:

1. one security duplicated under punctuation or case variants;
2. distinct instruments carrying an incorrect primary identifier; or
3. historical identifier reuse that should be represented in effective-dated
   `security_identifiers`, not as two simultaneous primary identities.

Use the governed Security Master workflow to select the canonical `SecurityId`, preserve source
and approval evidence, redirect dependent references where required, and either correct the
noncanonical primary identifier or retire/merge the duplicate record under the applicable runbook.
Do not delete a row or choose the lowest identifier automatically. Re-run the preflight after every
remediation batch and retain its output with the change evidence.

## Deployment and Verification

Start the reviewed host through the normal [Operator Preflight](preflight-checklist.md) launch path
with the intended persistence configuration. Its `SecurityMasterMigrationRunner` applies pending
scripts and records their checksums before database readiness succeeds; do not run migration 032
as ad hoc SQL or edit the migration ledger. Index replacement takes a table lock and the runner's
migration batch is transactional. After successful migration, verify in the same target database:

```sql
select indexname, indexdef
from pg_indexes
where schemaname = '<schema>'
  and tablename = 'securities'
  and indexname in (
      'ux_securities_primary_identifier',
      'ix_securities_normalized_primary_identifier',
      'ux_securities_normalized_primary_identifier')
order by indexname;
```

Exactly `ux_securities_normalized_primary_identifier` should remain. Test one non-production
punctuation variant and confirm PostgreSQL rejects it with unique-violation `23505`.

If migration 032 reports collisions, it has made no schema changes: remediate and retry. After a
successful deployment, rollback requires recreating the former raw unique index before dropping the
normalized unique index; do this only under an approved rollback because it weakens the identity
invariant.

## Failure and Handoff

| Symptom | Safe next action |
| --- | --- |
| Collision error `23505` naming migration 032 | Retain the grouped security IDs in the restricted review; resolve identity ownership and repeat the preflight. |
| Connection, permission, or lock failure | Verify the database role/target and finish competing writes; retry the normal migration runner. |
| Applied-script checksum mismatch | Stop the upgrade and reconcile the deployed artifact with the migration owner; never rewrite the recorded checksum to force startup. |
| Unexpected index set after startup | Keep writes stopped, verify the target schema and startup logs, and escalate to the storage owner. |

Retain the backup reference, application version, schema identity, empty preflight result, migration
success evidence, and post-migration index definition. Run the duplicate-rejection probe only in an
isolated non-production database and retain its result separately from production acceptance.
The index change alone does not prove dependent references or Security Master workflows are healthy;
complete the scoped application checks before reopening writes.

## Related References

- [Fund Operations Persistence Cutover](fund-ops-persistence-cutover.md)
- [Database Schema](../reference/database-schema.md)
- [Migration 032](../../src/Meridian.Storage/SecurityMaster/Migrations/032_security_master_normalized_primary_identifier_uniqueness.sql)
- [Shared transactional migration runner](../../src/Meridian.Storage/Migrations/PostgresMigrationRunner.cs)
