-- Append-only guard for the Security Master governance log (migration 040).
--
-- Row-level BEFORE UPDATE/DELETE triggers do not fire for TRUNCATE, so a statement-level
-- BEFORE TRUNCATE trigger rejects it too, matching security_alias_revisions (migration 038).
-- Nothing is truncated here; the trigger only refuses future truncation.

create trigger security_governance_events_no_truncate
    before truncate on __SCHEMA__.security_governance_events
    for each statement execute function __SCHEMA__.reject_security_governance_event_mutation();
