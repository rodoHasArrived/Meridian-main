-- Append-only guard for the Asset Operations write-event log (migration 005).
--
-- Row-level BEFORE UPDATE/DELETE triggers do not fire for TRUNCATE, so a statement-level
-- BEFORE TRUNCATE trigger rejects it too. Nothing is truncated here; the trigger only refuses
-- future truncation.

create trigger trg_asset_operations_write_event_no_truncate
    before truncate on __SCHEMA__.asset_operations_write_events
    for each statement execute function __SCHEMA__.reject_asset_operations_write_event_mutation();
