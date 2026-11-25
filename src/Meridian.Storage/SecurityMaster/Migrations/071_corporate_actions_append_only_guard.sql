-- Migration 071: append-only guard for corporate_actions (Security Master review 2026-11-24, A6).
--
-- corporate_actions records declared actions and their supersession chain, but unlike
-- security_events (migration 059) any session with table privileges could rewrite or remove a
-- declared action. Row-level BEFORE UPDATE/DELETE and statement-level BEFORE TRUNCATE triggers now
-- refuse that.
--
-- Two narrow exceptions remain:
--   1. A legacy row's economic_fingerprint may be BACKFILLED from null (ingest reconciliation in
--      PostgresCorporateActionCanonicalStore). Nothing else on the row may change.
--   2. The maintenance normalization described below.
--
-- The explicit maintenance exception keeps the normalization of legacy event-type
-- spellings working: inside a transaction that sets
--     set local meridian.corporate_action_normalization = 'on'
-- an UPDATE may change ONLY event_type and economic_fingerprint. Every other column must be
-- unchanged, deletes and truncates are always refused, and the switch is never set by request
-- paths. The fingerprint is rewritten together with the event type because ingest de-duplicates
-- on it; a read-time-only normalization would let a re-ingested legacy action duplicate.

create or replace function __SCHEMA__.guard_corporate_action_mutation() returns trigger
language plpgsql as $$
begin
    if tg_op = 'UPDATE'
       and old.economic_fingerprint is null
       and (to_jsonb(new) - 'economic_fingerprint') = (to_jsonb(old) - 'economic_fingerprint') then
        return new;
    end if;

    if tg_op = 'UPDATE'
       and coalesce(current_setting('meridian.corporate_action_normalization', true), '') = 'on'
       and (to_jsonb(new) - 'event_type' - 'economic_fingerprint')
           = (to_jsonb(old) - 'event_type' - 'economic_fingerprint') then
        return new;
    end if;

    raise exception 'Security Master corporate actions are append-only'
        using errcode = '23514',
              hint = 'Record a superseding corporate action instead of changing or removing one.';
end;
$$;

create trigger corporate_actions_append_only
    before update or delete on __SCHEMA__.corporate_actions
    for each row execute function __SCHEMA__.guard_corporate_action_mutation();

create trigger corporate_actions_no_truncate
    before truncate on __SCHEMA__.corporate_actions
    for each statement execute function __SCHEMA__.guard_corporate_action_mutation();
