-- The operations migration created projected_flow_id before the workflows migration
-- declared projected_cash_flow_id. CREATE TABLE IF NOT EXISTS did not reconcile them.
-- Rename in place so retained identifiers, indexes, and foreign keys remain intact.
do $migration$
declare
    target_table text;
    has_legacy boolean;
    has_current boolean;
begin
    foreach target_table in array array['projected_cash_flow', 'reconciliation_result'] loop
        select exists (
            select 1 from information_schema.columns
            where table_schema = '__SCHEMA__' and table_name = target_table
              and column_name = 'projected_flow_id') into has_legacy;
        select exists (
            select 1 from information_schema.columns
            where table_schema = '__SCHEMA__' and table_name = target_table
              and column_name = 'projected_cash_flow_id') into has_current;
        if has_legacy and has_current then
            raise exception 'Ambiguous direct-lending cash-flow identity columns in %', target_table;
        elsif has_legacy then
            execute format('alter table %I.%I rename column projected_flow_id to projected_cash_flow_id',
                '__SCHEMA__', target_table);
        elsif not has_current then
            raise exception 'Missing direct-lending cash-flow identity in %', target_table;
        end if;
    end loop;
end
$migration$;
