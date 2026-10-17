-- Null-safe close-price approval guard (hardens the migration 037 trigger function).
--
-- Migration 037 is immutable, so the function body is replaced here; the trigger binding
-- security_close_price_approvals_append_guard is unchanged and keeps using this function.
-- Two conditions in the original OR chain evaluated to NULL instead of TRUE and therefore
-- did not reject:
--   * jsonb_array_length(snapshot_json->'Blockers') <> 0 is NULL when the payload has no
--     "Blockers" key, so a snapshot without blocker evidence was approvable.
--     coalesce(..., -1) treats a missing key as a rejection; a non-array value still raises.
--   * lower(btrim(Reviewer)) = lower(btrim(CreatedBy)) is NULL when the payload has no
--     "CreatedBy" value, so maker/checker independence was not enforced. A blank or missing
--     creator is now rejected and the comparison uses IS NOT DISTINCT FROM so NULL never passes.
-- Every other condition, the advisory lock, the error message and errcode 23514 are retained.
-- Nothing is truncated, dropped or rewritten; existing rows are untouched.

create or replace function __SCHEMA__.guard_close_price_approval_append() returns trigger
language plpgsql as $$
declare
    snapshot __SCHEMA__.security_close_price_snapshots%rowtype;
    approval_json jsonb := new.payload::jsonb;
    snapshot_json jsonb;
begin
    select * into strict snapshot from __SCHEMA__.security_close_price_snapshots where snapshot_id = new.snapshot_id;
    perform pg_advisory_xact_lock(hashtextextended(json_build_array(snapshot.account_id, snapshot.book_id, snapshot.valuation_date)::text, 0));
    snapshot_json := snapshot.payload::jsonb;
    if exists (select 1 from __SCHEMA__.security_close_price_snapshots where corrects_snapshot_id = new.snapshot_id)
        or coalesce(jsonb_array_length(snapshot_json->'Blockers'), -1) <> 0
        or coalesce(btrim(snapshot_json->>'CreatedBy'), '') = ''
        or lower(btrim(approval_json->>'Reviewer')) is not distinct from lower(btrim(snapshot_json->>'CreatedBy'))
        or coalesce(btrim(approval_json->>'Reviewer'), '') = ''
        or coalesce(btrim(approval_json->>'Rationale'), '') = ''
        or approval_json->>'SnapshotSha256' is distinct from snapshot.payload_sha256::text
        or (approval_json->>'SnapshotId')::uuid is distinct from new.snapshot_id then
        raise exception 'Close approval requires independent review of an unblocked current immutable snapshot' using errcode = '23514';
    end if;
    return new;
end;
$$;
