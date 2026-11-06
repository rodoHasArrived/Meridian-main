-- Allow the ClosingEntry posting kind alongside Originating and Adjustment. Period-close
-- closing entries are the governed, sanctioned exception to the closed-period posting bar.

do $$
begin
    alter table __SCHEMA__.journal_entries
        drop constraint if exists ck_journal_entries_posting_kind;
    alter table __SCHEMA__.journal_entries
        add constraint ck_journal_entries_posting_kind
        check (posting_kind in ('Originating', 'Adjustment', 'ClosingEntry'));
exception
    when duplicate_object then null;
end $$;

do $$
begin
    alter table __SCHEMA__.journal_legs
        drop constraint if exists ck_journal_legs_posting_kind;
    alter table __SCHEMA__.journal_legs
        add constraint ck_journal_legs_posting_kind
        check (posting_kind in ('Originating', 'Adjustment', 'ClosingEntry'));
exception
    when duplicate_object then null;
end $$;
