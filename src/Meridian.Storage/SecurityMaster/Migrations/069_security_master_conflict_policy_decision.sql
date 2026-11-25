-- Migration 069: persist the conflict-authority policy decision on each resolved conflict
-- (Security Master review 2026-11-24, items A5 and D3).
--
-- The workbench evaluated the policy winner and whether the operator deviated from it, then only
-- logged both. An auditor could not reconstruct from the database which resolutions deviated from
-- policy. These columns are written in the same compare-and-set that closes the conflict.
--
-- policy_winner_source : the source the authority policy selected when the conflict was closed.
-- policy_rule          : the ladder rule that selected it (golden-copy-source, source-precedence,
--                        freshness, confidence).
-- is_policy_deviation  : true when the operator's chosen source differs from the policy winner.
--                        Null on rows closed before this migration and on dismissals, which choose
--                        no winner.

alter table __SCHEMA__.security_master_conflicts
    add column if not exists policy_winner_source text null,
    add column if not exists policy_rule text null,
    add column if not exists is_policy_deviation boolean null;

create index if not exists ix_security_master_conflicts_policy_deviation
    on __SCHEMA__.security_master_conflicts (resolved_at)
    where is_policy_deviation = true;
