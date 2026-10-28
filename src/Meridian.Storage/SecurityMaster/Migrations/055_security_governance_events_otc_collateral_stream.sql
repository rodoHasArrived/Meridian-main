-- Migration 055: record OTC collateral balances, margin calls and exposure snapshots in the governance event log.
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- Collateral balances and margin calls (migration 054) are operator-entered state that the
-- collateral API upserts in place; exposure snapshots are derived but become governed state once a
-- call cites them. Each recording, amendment and lifecycle transition now appends to
-- security_governance_events in the same transaction as the change, naming the actor, under a new
-- 'OtcCollateral' stream. These events describe a netting set or a collateral agreement rather than
-- one security, so their security_id is null and the payload names the netting set (snapshots and
-- calls) or the collateral agreement (balances).
--
-- The stream check is replaced by one that admits the new stream; every existing stream stays
-- allowed and no row is touched. Both statements run in the runner's transaction, so a failure
-- leaves the original constraint in place.

alter table __SCHEMA__.security_governance_events
    drop constraint security_governance_events_stream_check;

alter table __SCHEMA__.security_governance_events
    add constraint security_governance_events_stream_check
        check (stream in ('OperatorOverrides', 'Aliases', 'ProjectionRebuild', 'UnderlyingGraph', 'Curves', 'OtcCollateral'));

comment on table __SCHEMA__.security_governance_events is
    'Append-only log of Security Master governance writes (operator overrides, alias changes, projection rebuilds, declared underlying-graph links, stored curves, OTC exposure snapshots, collateral balances and margin calls), written in the same transaction as the state change.';
comment on column __SCHEMA__.security_governance_events.security_id is
    'Affected security; null for batch-level events such as a projection rebuild batch, whose payload lists the securities, for stored-curve events (Curves), whose payload names the curve, and for OTC collateral events (OtcCollateral), whose payload names the netting set or collateral agreement.';
