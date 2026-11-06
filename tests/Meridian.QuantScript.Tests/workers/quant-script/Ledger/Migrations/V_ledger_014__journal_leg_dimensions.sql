alter table __SCHEMA__.journal_legs
    add column if not exists dimensions jsonb null;

create index if not exists ix_journal_legs_dimensions_gin
    on __SCHEMA__.journal_legs using gin (dimensions)
    where dimensions is not null;
