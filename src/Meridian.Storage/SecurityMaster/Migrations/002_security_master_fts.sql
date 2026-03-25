-- Migration 002: Full-text search support for security master
-- Adds a trigger to keep search_vector populated and a GIN index for fast queries.

create or replace function __SCHEMA__.update_security_search_vector()
returns trigger as $$
begin
    new.search_vector :=
        setweight(to_tsvector('simple', coalesce(new.display_name, '')), 'A') ||
        setweight(to_tsvector('simple', coalesce(new.primary_identifier_value, '')), 'A') ||
        setweight(to_tsvector('simple', coalesce(new.asset_class, '')), 'B') ||
        setweight(to_tsvector('simple', coalesce(new.issuer_name, '')), 'B') ||
        setweight(to_tsvector('simple', coalesce(new.exchange_code, '')), 'C') ||
        setweight(to_tsvector('simple', coalesce(new.currency, '')), 'C');
    return new;
end;
$$ language plpgsql;

drop trigger if exists trg_security_search_vector on __SCHEMA__.securities;

create trigger trg_security_search_vector
    before insert or update on __SCHEMA__.securities
    for each row execute function __SCHEMA__.update_security_search_vector();

-- Backfill search_vector for any rows that predate this migration.
update __SCHEMA__.securities set display_name = display_name where search_vector is null;

create index if not exists ix_securities_search_vector
    on __SCHEMA__.securities using gin(search_vector);
