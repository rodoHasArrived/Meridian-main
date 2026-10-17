-- Keep pre-existing duplicates as retained history while choosing one current attempt.
alter table __SCHEMA__.account_sync_history
    add column if not exists superseded_by_sync_history_id uuid
        references __SCHEMA__.account_sync_history (sync_history_id);

with ranked as (
    select sync_history_id,
           first_value(sync_history_id) over (
               partition by account_id, lower(trim(capability)), lower(trim(correlation_id))
               order by case status when 'Succeeded' then 3 when 'Degraded' then 2
                                    when 'Pending' then 0 else 1 end desc,
                        completed_at desc nulls last, attempted_at, sync_history_id) as retained_id
    from __SCHEMA__.account_sync_history
    where correlation_id is not null and trim(correlation_id) <> ''
      and superseded_by_sync_history_id is null
)
update __SCHEMA__.account_sync_history history
set superseded_by_sync_history_id = ranked.retained_id
from ranked where history.sync_history_id = ranked.sync_history_id
  and ranked.sync_history_id <> ranked.retained_id;

create unique index if not exists ux_account_sync_history_correlation
    on __SCHEMA__.account_sync_history
        (account_id, lower(trim(capability)), lower(trim(correlation_id)))
    where correlation_id is not null and trim(correlation_id) <> ''
      and superseded_by_sync_history_id is null;
