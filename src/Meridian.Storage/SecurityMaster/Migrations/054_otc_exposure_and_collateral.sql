-- Migration 054: exposure snapshots, collateral balances and margin calls on the OTC contract model.
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- Migration 050 modeled the agreements and the contracts; this migration records what they are
-- used for. An exposure snapshot is the derived, append-only valuation of one netting set on one
-- date: the contracts valued, the netting basis, gross and net exposure, the collateral held on
-- each side and the credit support movement each side's terms imply. A collateral balance is the
-- operator-recorded collateral posted by the fund or received from the counterparty under a
-- collateral agreement, valued at a stated date with the haircut the agreement assigns. A margin
-- call is one movement from a snapshot taken through its life (issued, agreed, disputed, settled,
-- cancelled); its amount is copied from the snapshot, never typed.
--
-- Snapshots are append-only (recomputing a date appends the next version); balances and calls are
-- versioned in place and every write to them appends to security_governance_events (migration
-- 055 admits the stream) in the same transaction.
--
-- ROLLOUT: the tables are created empty; the model is populated through the collateral API.

create table if not exists __SCHEMA__.otc_exposure_snapshots (
    snapshot_id             uuid           not null primary key,
    netting_set_id          uuid           not null references __SCHEMA__.otc_netting_sets (netting_set_id),
    collateral_agreement_id uuid           null references __SCHEMA__.otc_collateral_agreements (collateral_agreement_id),
    as_of_date              date           not null,
    version                 integer        not null,
    rule_version            text           not null,
    currency                text           not null,
    netting_basis           text           not null,
    status                  text           not null,
    provenance              text           not null,
    net_exposure            numeric(28,10) not null,
    gross_positive          numeric(28,10) not null,
    gross_negative          numeric(28,10) not null,
    payload                 jsonb          not null,
    payload_version         integer        not null default 1,
    payload_sha256          text           not null,
    computed_by             text           not null,
    computed_at             timestamptz    not null,
    constraint otc_exposure_snapshots_version_check
        check (version >= 1),
    constraint otc_exposure_snapshots_basis_check
        check (netting_basis in ('CloseOutNet', 'Gross')),
    constraint otc_exposure_snapshots_status_check
        check (status in ('Complete', 'Incomplete')),
    constraint otc_exposure_snapshots_unique_version
        unique (netting_set_id, as_of_date, version)
);

create index if not exists otc_exposure_snapshots_set_date_idx
    on __SCHEMA__.otc_exposure_snapshots (netting_set_id, as_of_date desc, version desc);

comment on table __SCHEMA__.otc_exposure_snapshots is
    'Append-only exposure and credit support snapshots of one netting set on one date: valued contracts, netting basis, gross and net exposure, collateral held and the movement each side implies. Recomputing a date appends the next version.';

create function __SCHEMA__.reject_otc_exposure_snapshot_mutation() returns trigger
language plpgsql as $$
begin
    raise exception 'OTC exposure snapshots are append-only' using errcode = '23514';
end;
$$;

create trigger otc_exposure_snapshots_append_only
    before update or delete on __SCHEMA__.otc_exposure_snapshots
    for each row execute function __SCHEMA__.reject_otc_exposure_snapshot_mutation();

create table if not exists __SCHEMA__.otc_collateral_balances (
    balance_id                      uuid           not null primary key,
    collateral_agreement_id         uuid           not null references __SCHEMA__.otc_collateral_agreements (collateral_agreement_id),
    direction                       text           not null,
    kind                            text           not null,
    eligible_collateral_description text           not null,
    security_id                     uuid           null references __SCHEMA__.securities (security_id),
    currency                        text           not null,
    quantity                        numeric(28,10) not null,
    unit_value                      numeric(28,10) not null,
    valued_amount                   numeric(28,10) not null,
    haircut_percent                 numeric(28,10) not null,
    valued_as_of                    date           not null,
    reference                       text           null,
    notes                           text           null,
    version                         bigint         not null,
    updated_by                      text           not null,
    updated_at                      timestamptz    not null,
    constraint otc_collateral_balances_direction_check
        check (direction in ('Posted', 'Received')),
    constraint otc_collateral_balances_kind_check
        check (kind in ('Cash', 'Security')),
    constraint otc_collateral_balances_amounts_check
        check (quantity >= 0 and unit_value > 0 and valued_amount >= 0),
    constraint otc_collateral_balances_haircut_check
        check (haircut_percent >= 0 and haircut_percent <= 100)
);

create index if not exists otc_collateral_balances_agreement_idx
    on __SCHEMA__.otc_collateral_balances (collateral_agreement_id);

comment on table __SCHEMA__.otc_collateral_balances is
    'Collateral posted by the fund or received from the counterparty under a collateral agreement: eligible-collateral line, quantity, unit value, valued amount, the haircut copied from the agreement and the valuation date. A zero quantity is a fully returned line.';

create table if not exists __SCHEMA__.otc_margin_calls (
    call_id                 uuid           not null primary key,
    netting_set_id          uuid           not null references __SCHEMA__.otc_netting_sets (netting_set_id),
    collateral_agreement_id uuid           not null references __SCHEMA__.otc_collateral_agreements (collateral_agreement_id),
    snapshot_id             uuid           not null references __SCHEMA__.otc_exposure_snapshots (snapshot_id),
    movement_kind           text           not null,
    mover                   text           not null,
    currency                text           not null,
    amount_called           numeric(28,10) not null,
    amount_agreed           numeric(28,10) null,
    status                  text           not null,
    issued_on               date           not null,
    due_on                  date           null,
    dispute_reason          text           null,
    settled_on              date           null,
    settled_amount          numeric(28,10) null,
    notes                   text           null,
    version                 bigint         not null,
    updated_by              text           not null,
    updated_at              timestamptz    not null,
    constraint otc_margin_calls_kind_check
        check (movement_kind in ('Delivery', 'Return')),
    constraint otc_margin_calls_mover_check
        check (mover in ('Fund', 'Counterparty')),
    constraint otc_margin_calls_status_check
        check (status in ('Issued', 'Agreed', 'Disputed', 'Settled', 'Cancelled')),
    constraint otc_margin_calls_amounts_check
        check (amount_called > 0 and (amount_agreed is null or amount_agreed > 0) and (settled_amount is null or settled_amount > 0)),
    constraint otc_margin_calls_dates_check
        check (due_on is null or due_on >= issued_on),
    constraint otc_margin_calls_settled_check
        check (status <> 'Settled' or (settled_on is not null and settled_amount is not null)),
    constraint otc_margin_calls_dispute_check
        check (status <> 'Disputed' or dispute_reason is not null)
);

create index if not exists otc_margin_calls_set_status_idx
    on __SCHEMA__.otc_margin_calls (netting_set_id, status);

create unique index if not exists otc_margin_calls_one_open_per_side_idx
    on __SCHEMA__.otc_margin_calls (netting_set_id, mover, movement_kind)
    where status in ('Issued', 'Agreed', 'Disputed');

comment on table __SCHEMA__.otc_margin_calls is
    'Margin calls issued from an exposure snapshot: one movement (delivery or return, by the fund or the counterparty) with its called, agreed and settled amounts and its lifecycle status. At most one call per netting set, mover and movement kind is open at a time.';
