-- Normalized lookup is the identity contract, so database uniqueness must use the same value.
-- Fail before changing indexes when legacy data contains collisions; choosing a canonical
-- SecurityId is a governed data-remediation decision and must never be automated by a migration.
do $$
declare
    collision_report text;
begin
    select string_agg(
        format(
            '%s|%s|%s',
            collision.primary_identifier_kind,
            collision.normalized_primary_identifier_value,
            array_to_string(collision.security_ids, ',')),
        E'\n'
        order by collision.primary_identifier_kind, collision.normalized_primary_identifier_value)
    into collision_report
    from (
        select
            primary_identifier_kind,
            normalized_primary_identifier_value,
            array_agg(security_id order by security_id) as security_ids
        from __SCHEMA__.securities
        group by primary_identifier_kind, normalized_primary_identifier_value
        having count(*) > 1
    ) collision;

    if collision_report is not null then
        raise exception using
            errcode = '23505',
            message = 'Normalized primary identifier collisions block migration 031.',
            detail = collision_report,
            hint = 'Resolve each collision through the governed Security Master identity workflow, rerun the documented preflight query, then retry the migration.';
    end if;
end
$$;

drop index if exists __SCHEMA__.ux_securities_primary_identifier;
drop index if exists __SCHEMA__.ix_securities_normalized_primary_identifier;

create unique index ux_securities_normalized_primary_identifier
    on __SCHEMA__.securities (primary_identifier_kind, normalized_primary_identifier_value);
