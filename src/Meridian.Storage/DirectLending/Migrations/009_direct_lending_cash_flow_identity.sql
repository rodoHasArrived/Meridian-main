-- Retain the legacy column during rollout; old and current store versions may coexist.
create or replace function __SCHEMA__.synchronize_cash_flow_identity()
returns trigger language plpgsql as $function$
begin
    if TG_OP = 'UPDATE' then
        if NEW.projected_cash_flow_id is distinct from OLD.projected_cash_flow_id
           and NEW.projected_flow_id is not distinct from OLD.projected_flow_id then
            NEW.projected_flow_id := NEW.projected_cash_flow_id;
        elsif NEW.projected_flow_id is distinct from OLD.projected_flow_id
           and NEW.projected_cash_flow_id is not distinct from OLD.projected_cash_flow_id then
            NEW.projected_cash_flow_id := NEW.projected_flow_id;
        end if;
    else
        NEW.projected_cash_flow_id := coalesce(NEW.projected_cash_flow_id, NEW.projected_flow_id);
        NEW.projected_flow_id := coalesce(NEW.projected_flow_id, NEW.projected_cash_flow_id);
    end if;
    if NEW.projected_cash_flow_id is distinct from NEW.projected_flow_id then
        raise exception 'Conflicting direct-lending cash-flow identities';
    end if;
    return NEW;
end
$function$;

do $migration$
declare
    target_table text;
    conflicting_ids boolean;
begin
    foreach target_table in array array['projected_cash_flow', 'reconciliation_result'] loop
        if exists (select 1 from information_schema.columns
                   where table_schema = '__SCHEMA__' and table_name = target_table
                     and column_name = 'projected_flow_id') then
            execute format('alter table %I.%I add column if not exists projected_cash_flow_id uuid',
                '__SCHEMA__', target_table);
            execute format('update %I.%I set projected_cash_flow_id = projected_flow_id where projected_cash_flow_id is null',
                '__SCHEMA__', target_table);
            execute format('select exists (select 1 from %I.%I where projected_cash_flow_id is distinct from projected_flow_id)',
                '__SCHEMA__', target_table) into conflicting_ids;
            if conflicting_ids then
                raise exception 'Conflicting retained cash-flow identities in %', target_table;
            end if;
            if not exists (select 1 from pg_trigger
                           where tgrelid = format('%I.%I', '__SCHEMA__', target_table)::regclass
                             and tgname = 'synchronize_cash_flow_identity') then
                execute format('create trigger synchronize_cash_flow_identity before insert or update on %I.%I for each row execute function __SCHEMA__.synchronize_cash_flow_identity()',
                    '__SCHEMA__', target_table);
            end if;
        end if;
    end loop;
end
$migration$;
