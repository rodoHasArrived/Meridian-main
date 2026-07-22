-- PIK accrual support: capitalized interest recorded per accrual entry.
alter table __SCHEMA__.accrual_entry_projection
    add column if not exists pik_interest_amount numeric(24,8) not null default 0;
