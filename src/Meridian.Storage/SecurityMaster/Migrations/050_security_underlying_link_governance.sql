-- Migration 050: Retain declared underlying-link changes in the governance log.
-- Replace only the stream check with a strict superset. The migration runner executes
-- this DDL and its checksum receipt in one transaction; retained events and append-only
-- update/delete/truncate guards remain intact. No historical link events are invented.
alter table __SCHEMA__.security_governance_events
    drop constraint security_governance_events_stream_check;
alter table __SCHEMA__.security_governance_events
    add constraint security_governance_events_stream_check
    check (stream in ('OperatorOverrides', 'Aliases', 'ProjectionRebuild', 'UnderlyingLinks'));

comment on table __SCHEMA__.security_governance_events is
    'Append-only log of Security Master governance writes (operator overrides, alias changes, projection rebuilds, declared underlying-link changes), written in the same transaction as the state change.';
