-- APPLICATION-VERSION COMPATIBILITY BARRIER: this migration changes the access-grant consumption
-- write contract. Drain and stop every pre-012 binary before a 012-aware host applies it; do not
-- apply it DB-first during a mixed-version rollout. The insert and NULL-to-NULL update fences below
-- intentionally reject pre-012 writers and are not a rolling-version compatibility bridge.
-- Retained grants created before this migration remain null because their prior successful
-- artifact identity cannot be reconstructed. New writers explicitly persist an empty tracked set
-- and append exact artifact ids as successful reads consume uses. A post-012 insert that omits the
-- column is rejected as an incompatible writer; this does not rewrite retained pre-012 null rows.
alter table __SCHEMA__.reporting_access_grants
    add column if not exists consumed_artifact_ids text[] null;

alter table __SCHEMA__.reporting_access_grants
    alter column consumed_artifact_ids drop default;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'ck_reporting_access_grant_consumed_artifacts'
          and conrelid = '__SCHEMA__.reporting_access_grants'::regclass) then
        alter table __SCHEMA__.reporting_access_grants
            add constraint ck_reporting_access_grant_consumed_artifacts
                check (consumed_artifact_ids is null
                    or (array_position(consumed_artifact_ids, null) is null
                        and consumed_artifact_ids <@ artifact_ids
                        and cardinality(consumed_artifact_ids) <= use_count
                        and (use_count = 0
                            or cardinality(artifact_ids) = 0
                            or cardinality(consumed_artifact_ids) > 0)));
    end if;
end;
$$;

create or replace function __SCHEMA__.guard_reporting_access_grant_mutation()
returns trigger
language plpgsql
as $$
begin
    if tg_op = 'DELETE' then
        raise exception 'reporting access grants cannot be deleted'
            using errcode = '55000';
    end if;

    if tg_op = 'INSERT' and new.consumed_artifact_ids is null then
        raise exception 'reporting access grant insert requires a 012-compatible application writer'
            using errcode = '55000';
    end if;

    if new.consumed_artifact_ids is not null
        and (
            array_position(new.consumed_artifact_ids, null) is not null
            or not (new.consumed_artifact_ids <@ new.artifact_ids)
            or cardinality(new.consumed_artifact_ids) > new.use_count
            or (new.use_count > 0
                and cardinality(new.artifact_ids) > 0
                and cardinality(new.consumed_artifact_ids) = 0)
            or exists (
                select 1
                from unnest(new.consumed_artifact_ids) as consumed(artifact_id)
                group by consumed.artifact_id
                having count(*) > 1)
        ) then
        raise exception 'invalid reporting access grant consumed-artifact state'
            using errcode = '55000';
    end if;

    if tg_op = 'INSERT' then
        return new;
    end if;

    if new.grant_id is distinct from old.grant_id
        or new.token_hash_sha256 is distinct from old.token_hash_sha256
        or new.tenant_id is distinct from old.tenant_id
        or new.audience is distinct from old.audience
        or new.audience_kind is distinct from old.audience_kind
        or new.run_id is distinct from old.run_id
        or new.package_id is distinct from old.package_id
        or new.allow_package_read is distinct from old.allow_package_read
        or new.artifact_ids is distinct from old.artifact_ids
        or new.created_at_utc is distinct from old.created_at_utc
        or new.expires_at_utc is distinct from old.expires_at_utc
        or new.max_uses is distinct from old.max_uses then
        raise exception 'reporting access grant authority scope is immutable'
            using errcode = '55000';
    end if;

    if new.version <> old.version + 1
        or new.use_count < old.use_count
        or new.use_count > old.use_count + 1
        or (new.use_count = old.use_count
            and (new.last_used_at_utc is distinct from old.last_used_at_utc
                or new.consumed_artifact_ids is distinct from old.consumed_artifact_ids))
        or (new.use_count = old.use_count + 1
            and (new.last_used_at_utc is null
                or new.last_used_at_utc < new.created_at_utc
                or coalesce(cardinality(new.consumed_artifact_ids), 0)
                    > coalesce(cardinality(old.consumed_artifact_ids), 0) + 1))
        or (new.use_count = old.use_count + 1
            and old.consumed_artifact_ids is null
            and new.consumed_artifact_ids is null)
        or (old.consumed_artifact_ids is not null
            and (new.consumed_artifact_ids is null
                or not (old.consumed_artifact_ids <@ new.consumed_artifact_ids)))
        or (old.consumed_artifact_ids is null
            and new.consumed_artifact_ids is not null
            and (cardinality(new.consumed_artifact_ids) <> 1
                or (old.use_count > 0
                    and cardinality(old.artifact_ids) > 1)))
        or (old.revoked_at_utc is not null
            and new.use_count <> old.use_count)
        or (old.revoked_at_utc is null
            and new.revoked_at_utc is not null
            and new.use_count <> old.use_count)
        or (old.last_used_at_utc is not null
            and (new.last_used_at_utc is null
                or new.last_used_at_utc < old.last_used_at_utc))
        or (old.revoked_at_utc is not null
            and (new.revoked_at_utc is distinct from old.revoked_at_utc
                or new.revoked_by is distinct from old.revoked_by
                or new.revocation_reason is distinct from old.revocation_reason)) then
        raise exception 'invalid reporting access grant state transition'
            using errcode = '55000';
    end if;

    return new;
end;
$$;

-- Replace and version the retained update/delete trigger so direct inserts are subject to the same
-- canonical consumed-artifact validation. The removed pre-012 trigger name is also a reverse
-- compatibility sentinel: a pre-012 deployment probe cannot report this post-012 schema as ready.
-- Lagging writers that omit the new nullable column are rejected; new writers explicitly insert an
-- empty tracked set.
alter trigger trg_reporting_access_grants_guard
    on __SCHEMA__.reporting_access_grants
    rename to trg_reporting_access_grants_guard_pre012_retired;

alter table __SCHEMA__.reporting_access_grants
    disable trigger trg_reporting_access_grants_guard_pre012_retired;

create trigger trg_reporting_access_grants_guard_v012
before insert or update or delete on __SCHEMA__.reporting_access_grants
for each row execute function __SCHEMA__.guard_reporting_access_grant_mutation();
