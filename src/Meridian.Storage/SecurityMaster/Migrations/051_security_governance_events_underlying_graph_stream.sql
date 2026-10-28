-- Migration 051: record declared underlying-graph links in the governance event log.
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- Declared links in security_underlying_links (migration 049) are operator-entered state (basket
-- weights, collateral, resolved references) that the graph API upserts in place and deletes on
-- removal. Each declaration, amendment and removal now appends to security_governance_events in
-- the same transaction as the change, naming the actor, under a new 'UnderlyingGraph' stream.
-- Derived links follow the economic terms, which security_events already records, and log nothing.
--
-- The stream check from migration 040 is replaced by one that admits the new stream; every
-- existing stream stays allowed and no row is touched. Both statements run in the runner's
-- transaction, so a failure leaves the original constraint in place.

alter table __SCHEMA__.security_governance_events
    drop constraint security_governance_events_stream_check;

alter table __SCHEMA__.security_governance_events
    add constraint security_governance_events_stream_check
        check (stream in ('OperatorOverrides', 'Aliases', 'ProjectionRebuild', 'UnderlyingGraph'));

comment on table __SCHEMA__.security_governance_events is
    'Append-only log of Security Master governance writes (operator overrides, alias changes, projection rebuilds, declared underlying-graph links), written in the same transaction as the state change.';
