alter table __SCHEMA__.journal_entries
    add column if not exists posting_kind text not null default 'Originating';

alter table __SCHEMA__.journal_legs
    add column if not exists posting_kind text not null default 'Originating';

do $$
begin
    alter table __SCHEMA__.journal_entries
        add constraint ck_journal_entries_posting_kind
        check (posting_kind in ('Originating', 'Adjustment'));
exception
    when duplicate_object then null;
end $$;

do $$
begin
    alter table __SCHEMA__.journal_legs
        add constraint ck_journal_legs_posting_kind
        check (posting_kind in ('Originating', 'Adjustment'));
exception
    when duplicate_object then null;
end $$;

create index if not exists ix_journal_entries_period_posting_kind
    on __SCHEMA__.journal_entries (period_id, posting_kind, occurred_at, global_sequence);

create index if not exists ix_journal_legs_period_posting_kind
    on __SCHEMA__.journal_legs (period_id, posting_kind, occurred_at);
