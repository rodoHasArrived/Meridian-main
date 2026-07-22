alter table __SCHEMA__.instrument_role_projections
    add column if not exists approval_rationale text not null default '',
    add column if not exists source_domain text null,
    add column if not exists source_entity_id text null,
    add column if not exists source_content_hash text null;

alter table __SCHEMA__.book_position_projections
    add column if not exists position_side text null,
    add column if not exists position_status text null,
    add column if not exists approval_rationale text not null default '',
    add column if not exists source_domain text null,
    add column if not exists source_entity_id text null,
    add column if not exists source_content_hash text null,
    add column if not exists projection_run_id uuid null,
    add column if not exists projection_event_id uuid null;

alter table __SCHEMA__.position_economic_state_projections
    add column if not exists ledger_book_id uuid null,
    add column if not exists owner_scope_id text null,
    add column if not exists owner_scope_kind text null,
    add column if not exists approval_rationale text not null default '',
    add column if not exists source_domain text null,
    add column if not exists source_entity_id text null,
    add column if not exists source_content_hash text null,
    add column if not exists projection_run_id uuid null,
    add column if not exists projection_event_id uuid null;

update __SCHEMA__.instrument_role_projections
set source_event_id = coalesce(
        source_event_id,
        nullif(payload #>> '{originEvent,eventId}', '')::uuid),
    approval_rationale = case
        when btrim(approval_rationale) = '' then 'Migrated legacy approval; rationale was not captured.'
        else approval_rationale
    end,
    source_domain = coalesce(source_domain, payload #>> '{originEvent,sourceDomain}'),
    source_entity_id = coalesce(source_entity_id, payload #>> '{originEvent,sourceEntityId}'),
    source_content_hash = coalesce(source_content_hash, payload #>> '{originEvent,sourceContentHash}');

update __SCHEMA__.book_position_projections
set position_side = coalesce(position_side, payload ->> 'positionSide'),
    position_status = coalesce(position_status, payload ->> 'status'),
    source_event_id = coalesce(
        source_event_id,
        nullif(payload #>> '{originEvent,eventId}', '')::uuid,
        nullif(payload #>> '{projectionLineage,triggerEvent,eventId}', '')::uuid),
    approval_rationale = case
        when btrim(approval_rationale) = '' then 'Migrated legacy approval; rationale was not captured.'
        else approval_rationale
    end,
    source_domain = coalesce(
        source_domain,
        payload #>> '{originEvent,sourceDomain}',
        payload #>> '{projectionLineage,triggerEvent,sourceDomain}'),
    source_entity_id = coalesce(
        source_entity_id,
        payload #>> '{originEvent,sourceEntityId}',
        payload #>> '{projectionLineage,triggerEvent,sourceEntityId}'),
    source_content_hash = coalesce(
        source_content_hash,
        payload #>> '{originEvent,sourceContentHash}',
        payload #>> '{projectionLineage,triggerEvent,sourceContentHash}'),
    projection_run_id = coalesce(
        projection_run_id,
        nullif(payload #>> '{projectionLineage,projectionRunId}', '')::uuid),
    projection_event_id = coalesce(
        projection_event_id,
        nullif(payload #>> '{projectionLineage,projectionEventId}', '')::uuid);

update __SCHEMA__.position_economic_state_projections as state
set ledger_book_id = coalesce(state.ledger_book_id, position.ledger_book_id),
    owner_scope_id = coalesce(state.owner_scope_id, position.owner_scope_id),
    owner_scope_kind = coalesce(state.owner_scope_kind, position.owner_scope_kind),
    source_event_id = coalesce(
        state.source_event_id,
        nullif(state.payload #>> '{sourceEvent,eventId}', '')::uuid,
        nullif(state.payload #>> '{projectionLineage,triggerEvent,eventId}', '')::uuid),
    approval_rationale = case
        when btrim(state.approval_rationale) = '' then 'Migrated legacy approval; rationale was not captured.'
        else state.approval_rationale
    end,
    source_domain = coalesce(
        state.source_domain,
        state.payload #>> '{sourceEvent,sourceDomain}',
        state.payload #>> '{projectionLineage,triggerEvent,sourceDomain}'),
    source_entity_id = coalesce(
        state.source_entity_id,
        state.payload #>> '{sourceEvent,sourceEntityId}',
        state.payload #>> '{projectionLineage,triggerEvent,sourceEntityId}'),
    source_content_hash = coalesce(
        state.source_content_hash,
        state.payload #>> '{sourceEvent,sourceContentHash}',
        state.payload #>> '{projectionLineage,triggerEvent,sourceContentHash}'),
    projection_run_id = coalesce(
        state.projection_run_id,
        nullif(state.payload #>> '{projectionLineage,projectionRunId}', '')::uuid),
    projection_event_id = coalesce(
        state.projection_event_id,
        nullif(state.payload #>> '{projectionLineage,projectionEventId}', '')::uuid)
from __SCHEMA__.book_position_projections as position
where position.position_id = state.position_id;

do $$
declare
    conflicting_run_id uuid;
begin
    select projection_run_id
    into conflicting_run_id
    from (
        select projection_run_id, payload -> 'projectionLineage' as lineage
        from __SCHEMA__.book_position_projections
        where projection_run_id is not null
          and payload -> 'projectionLineage' is not null
        union all
        select projection_run_id, payload -> 'projectionLineage' as lineage
        from __SCHEMA__.position_economic_state_projections
        where projection_run_id is not null
          and payload -> 'projectionLineage' is not null
    ) as retained_lineage
    group by projection_run_id
    having count(distinct lineage) > 1
    order by projection_run_id
    limit 1;

    if conflicting_run_id is not null then
        raise exception
            'Migration 003 cannot continue: projection run % has conflicting retained lineage.',
            conflicting_run_id
            using errcode = '23514';
    end if;
end $$;

alter table __SCHEMA__.book_position_projections
    alter column position_side set not null,
    alter column position_status set not null;

alter table __SCHEMA__.position_economic_state_projections
    alter column ledger_book_id set not null,
    alter column owner_scope_id set not null,
    alter column owner_scope_kind set not null;

alter table __SCHEMA__.instrument_role_projections
    alter column approval_rationale drop default;

alter table __SCHEMA__.book_position_projections
    alter column approval_rationale drop default;

alter table __SCHEMA__.position_economic_state_projections
    alter column approval_rationale drop default;

create unique index if not exists ux_instrument_role_projection_scope
    on __SCHEMA__.instrument_role_projections
        (role_id, security_id, owner_scope_id, owner_scope_kind);

create unique index if not exists ux_book_position_projection_scope
    on __SCHEMA__.book_position_projections
        (position_id, security_id, ledger_book_id, owner_scope_id, owner_scope_kind);

create unique index if not exists ux_position_economic_state_position_version
    on __SCHEMA__.position_economic_state_projections (position_id, version);

alter table __SCHEMA__.book_position_projections
    drop constraint if exists fk_book_position_projection_role;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'fk_book_position_projection_role_scope'
          and conrelid = '__SCHEMA__.book_position_projections'::regclass
    ) then
        alter table __SCHEMA__.book_position_projections
            add constraint fk_book_position_projection_role_scope
                foreign key (role_id, security_id, owner_scope_id, owner_scope_kind)
                references __SCHEMA__.instrument_role_projections
                    (role_id, security_id, owner_scope_id, owner_scope_kind);
    end if;
exception
    when duplicate_object then null;
end $$;

alter table __SCHEMA__.position_economic_state_projections
    drop constraint if exists fk_position_economic_state_position;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'fk_position_economic_state_position_scope'
          and conrelid = '__SCHEMA__.position_economic_state_projections'::regclass
    ) then
        alter table __SCHEMA__.position_economic_state_projections
            add constraint fk_position_economic_state_position_scope
                foreign key (position_id, security_id, ledger_book_id, owner_scope_id, owner_scope_kind)
                references __SCHEMA__.book_position_projections
                    (position_id, security_id, ledger_book_id, owner_scope_id, owner_scope_kind);
    end if;
exception
    when duplicate_object then null;
end $$;

create index if not exists ix_instrument_role_projection_source_event
    on __SCHEMA__.instrument_role_projections (source_event_id)
    where source_event_id is not null;

create index if not exists ix_book_position_projection_source_event
    on __SCHEMA__.book_position_projections (source_event_id)
    where source_event_id is not null;

create index if not exists ix_position_economic_state_source_event
    on __SCHEMA__.position_economic_state_projections (source_event_id)
    where source_event_id is not null;

create index if not exists ix_position_economic_state_security_book_as_of
    on __SCHEMA__.position_economic_state_projections
        (security_id, ledger_book_id, as_of_date desc, version desc);

create index if not exists ix_book_position_projection_effective_scope
    on __SCHEMA__.book_position_projections
        (security_id, ledger_book_id, owner_scope_id, owner_scope_kind, role_id, position_side,
         position_status, effective_from, effective_to);
