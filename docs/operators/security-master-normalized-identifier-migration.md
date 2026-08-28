# Security Master Normalized Identifier Uniqueness Migration

Migration 030 replaces raw primary-identifier uniqueness with uniqueness over
`(primary_identifier_kind, normalized_primary_identifier_value)`, matching the identifier
resolution contract.

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

Apply migrations during a Security Master write-maintenance window. Index replacement takes a table
lock and is intentionally transactional. After migration, verify:

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

If migration 030 reports collisions, it has made no schema changes: remediate and retry. After a
successful deployment, rollback requires recreating the former raw unique index before dropping the
normalized unique index; do this only under an approved rollback because it weakens the identity
invariant.
