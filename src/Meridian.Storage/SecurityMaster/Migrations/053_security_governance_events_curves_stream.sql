-- Migration 053: record stored-curve definitions and published versions in the governance event log.
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- Curves (migration 052) are governed market inputs: defining a curve and publishing a version
-- are operator or ingest writes that a valuation later names by version and content hash. Each
-- now appends to security_governance_events in the same transaction as the change, naming the
-- actor, under a new 'Curves' stream. A curve is not a security, so these events carry a null
-- security_id and the curve id in their payload.
--
-- The stream check from migration 051 is replaced by one that admits the new stream; every
-- existing stream stays allowed and no row is touched. Both statements run in the runner's
-- transaction, so a failure leaves the original constraint in place.

alter table __SCHEMA__.security_governance_events
    drop constraint security_governance_events_stream_check;

alter table __SCHEMA__.security_governance_events
    add constraint security_governance_events_stream_check
        check (stream in ('OperatorOverrides', 'Aliases', 'ProjectionRebuild', 'UnderlyingGraph', 'Curves'));

comment on table __SCHEMA__.security_governance_events is
    'Append-only log of Security Master governance writes (operator overrides, alias changes, projection rebuilds, declared underlying-graph links, stored curve definitions and versions), written in the same transaction as the state change.';
