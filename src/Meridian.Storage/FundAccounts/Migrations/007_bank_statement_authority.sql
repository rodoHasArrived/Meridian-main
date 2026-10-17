-- Source revision and ordinal are financial authority; arrival time and UUIDs are not.
alter table __SCHEMA__.bank_statement_batch
    add column if not exists statement_revision integer not null default 1
        check (statement_revision > 0);

-- Leave historical order unknown rather than manufacturing it from IDs or ingestion order.
alter table __SCHEMA__.bank_statement_line
    add column if not exists line_sequence integer check (line_sequence > 0);

create unique index if not exists ux_bank_statement_source_sequence
    on __SCHEMA__.bank_statement_line (batch_id, line_sequence)
    where line_sequence is not null;
