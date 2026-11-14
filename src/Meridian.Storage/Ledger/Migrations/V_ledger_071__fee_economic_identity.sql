-- A pending execution fee and an approved reconciliation correction must not both book one fee.
-- Only the governed append boundary stamps these canonical identity tags; legacy rows are unchanged.
create unique index if not exists ux_journal_entries_fee_economic_identity
    on __SCHEMA__.journal_entries (
        (metadata #>> '{tags,fee.economicBookId}'),
        (metadata #>> '{tags,fee.economicAccountId}'),
        (metadata #>> '{tags,fee.externalTransactionId}')
    )
    where metadata #>> '{tags,fee.economicBookId}' is not null;
