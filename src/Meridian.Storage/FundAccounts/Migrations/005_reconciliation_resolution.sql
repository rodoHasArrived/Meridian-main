-- Outcomes are immutable evidence; lifecycle links identify their verified replacements.
alter table __SCHEMA__.account_reconciliation_breaks
    add column if not exists superseded_by_result_id uuid references __SCHEMA__.account_reconciliation_breaks (result_id),
    add column if not exists resolved_at timestamptz;

-- Backfill only strictly later verified observations. Equal-time ambiguity stays open.
with replacements as (
    select previous.result_id, next_result.result_id as replacement_id,
           case when not previous.is_match and lower(previous.status) <> 'unverified'
                     and next_result.is_match then next_result.completed_at else null end as resolved_at
    from __SCHEMA__.account_reconciliation_breaks previous
    join __SCHEMA__.account_reconciliation_run previous_run using (reconciliation_run_id)
    cross join lateral (
        select result_id, is_match, completed_at
        from (
            select candidate.result_id, candidate.is_match, candidate_run.requested_at,
                   coalesce(candidate_run.completed_at, candidate_run.requested_at) as completed_at,
                   count(*) over (partition by candidate_run.requested_at) as peers,
                   max(candidate_run.requested_at) over () as latest
            from __SCHEMA__.account_reconciliation_breaks candidate
            join __SCHEMA__.account_reconciliation_run candidate_run using (reconciliation_run_id)
            where candidate_run.account_id = previous_run.account_id
              and candidate_run.as_of_date = previous_run.as_of_date
              and candidate_run.requested_at > previous_run.requested_at
              and lower(candidate.category) = lower(previous.category)
              and (case when lower(candidate.check_label) like 'positioncount (%' then 'positioncount' else lower(candidate.check_label) end)
                = (case when lower(previous.check_label) like 'positioncount (%' then 'positioncount' else lower(previous.check_label) end)
              and lower(candidate.status) <> 'unverified'
        ) authority
        where requested_at = latest and peers = 1
    ) next_result
)
update __SCHEMA__.account_reconciliation_breaks retained
set superseded_by_result_id = replacements.replacement_id, resolved_at = replacements.resolved_at
from replacements where retained.result_id = replacements.result_id
  and retained.superseded_by_result_id is null;

create index if not exists ix_account_reconciliation_active
    on __SCHEMA__.account_reconciliation_breaks (reconciliation_run_id)
    where superseded_by_result_id is null;
