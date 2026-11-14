-- Preserve the operator-authorized rule pack across process restart without widening an existing grant.
alter table __SCHEMA__.automated_posting_grants
    add column if not exists policy_snapshot jsonb null,
    add column if not exists policy_snapshot_sha256 text null;
do $$
begin
    if not exists (
        select 1 from pg_constraint
        where conrelid = '__SCHEMA__.automated_posting_grants'::regclass
          and conname = 'automated_posting_grants_policy_snapshot_pair'
    ) then
        alter table __SCHEMA__.automated_posting_grants
            add constraint automated_posting_grants_policy_snapshot_pair
            check ((policy_snapshot is null) = (policy_snapshot_sha256 is null));
    end if;
end;
$$;
