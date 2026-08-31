alter table __SCHEMA__.journal_entries
    add column if not exists adjustment_approval_metadata jsonb null;

alter table __SCHEMA__.journal_legs
    add column if not exists adjustment_approval_metadata jsonb null;

do $$
begin
    alter table __SCHEMA__.journal_entries
        add constraint ck_journal_entries_adjustment_approval_metadata
        check (posting_kind = 'Adjustment' or adjustment_approval_metadata is null);
exception
    when duplicate_object then null;
end $$;

do $$
begin
    alter table __SCHEMA__.journal_legs
        add constraint ck_journal_legs_adjustment_approval_metadata
        check (posting_kind = 'Adjustment' or adjustment_approval_metadata is null);
exception
    when duplicate_object then null;
end $$;

create index if not exists ix_journal_entries_adjustment_approval_id
    on __SCHEMA__.journal_entries ((adjustment_approval_metadata ->> 'approvalId'))
    where adjustment_approval_metadata is not null;

create index if not exists ix_journal_entries_period_adjustment_approval_status
    on __SCHEMA__.journal_entries (period_id, (adjustment_approval_metadata ->> 'status'), occurred_at, global_sequence)
    where adjustment_approval_metadata is not null;
