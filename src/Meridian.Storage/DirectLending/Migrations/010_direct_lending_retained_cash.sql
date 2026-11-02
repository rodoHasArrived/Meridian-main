-- Additive cash-accounting projections. Existing receipts are not reallocated or
-- assigned a contractual policy by this migration.
alter table __SCHEMA__.loan_servicing_projection
    add column if not exists unapplied_cash numeric not null default 0
        check (unapplied_cash >= 0),
    add column if not exists prepaid_interest numeric not null default 0
        check (prepaid_interest >= 0),
    add column if not exists prepaid_interest_lots_json jsonb;

alter table __SCHEMA__.accrual_entry_projection
    add column if not exists prepaid_interest_applied numeric not null default 0
        check (prepaid_interest_applied >= 0),
    add column if not exists prepaid_interest_releases_json jsonb;

-- Keep the complete contractual operand, including optional fields which older
-- normalized versions did not project. Null remains a legacy row, not an election.
alter table __SCHEMA__.loan_terms_version
    add column if not exists terms_json jsonb;

-- Copy only an already retained version with the exact original terms hash.
-- Never synthesize a policy, rewrite a hash, or reconstruct missing loan economics.
update __SCHEMA__.loan_terms_version as target
set terms_json = version.value -> 'terms'
from __SCHEMA__.loan_state as retained,
     lateral jsonb_array_elements(coalesce(retained.contract_json -> 'termsVersions', '[]'::jsonb)) as version(value)
where target.loan_id = retained.loan_id
  and target.terms_version = (version.value ->> 'versionNumber')::integer
  and target.terms_hash = version.value ->> 'termsHash'
  and target.terms_json is null
  and jsonb_typeof(version.value -> 'terms') = 'object';

-- Preserve the amount received and each allocation exactly; the resolver and
-- retained journal establish the contractual amounts, not a database rounding cast.
alter table __SCHEMA__.cash_transaction alter column amount type numeric;
alter table __SCHEMA__.payment_allocation alter column allocated_amount type numeric;
alter table __SCHEMA__.fee_balance
    alter column original_amount type numeric,
    alter column unpaid_amount type numeric;
alter table __SCHEMA__.journal_line
    alter column debit_amount type numeric,
    alter column credit_amount type numeric;
alter table __SCHEMA__.reconciliation_result
    alter column expected_amount type numeric,
    alter column actual_amount type numeric,
    alter column variance_amount type numeric;

-- Keep normalized balances and FIFO drawdown lots equal to the retained decimal
-- allocations, including currencies/instructions whose precision is not two places.
alter table __SCHEMA__.loan_servicing_projection
    alter column current_commitment type numeric,
    alter column total_drawn type numeric,
    alter column available_to_draw type numeric,
    alter column principal_outstanding type numeric,
    alter column interest_accrued_unpaid type numeric,
    alter column commitment_fee_accrued_unpaid type numeric,
    alter column fees_accrued_unpaid type numeric,
    alter column penalty_accrued_unpaid type numeric;
alter table __SCHEMA__.drawdown_lot_projection
    alter column original_principal type numeric,
    alter column remaining_principal type numeric;

-- Older writers must fail closed rather than drop unknown balances, contractual
-- elections, or receipt instructions. A prepaid decrease is permitted only for
-- exactly evidenced future cash-interest releases in the next aggregate event.
create or replace function __SCHEMA__.protect_retained_loan_cash()
returns trigger language plpgsql as $function$
declare
    previous_version jsonb;
    previous_versions jsonb;
    previous_balances jsonb;
    previous_aggregate_version bigint;
    next_balances jsonb;
    previous_lots jsonb;
    next_lots jsonb;
    previous_lot jsonb;
    next_lot jsonb;
    release_payload jsonb;
    releases jsonb := '[]'::jsonb;
    reduction numeric;
    released numeric;
begin
    if TG_OP = 'UPDATE' then
        previous_versions := coalesce(OLD.contract_json -> 'termsVersions', '[]'::jsonb);
        previous_balances := OLD.servicing_json -> 'balances';
        previous_aggregate_version := OLD.aggregate_version;
    else
        -- Rebuilding a missing JSON state does not authorize dropping exact
        -- evidence already retained in the normalized projection.
        select jsonb_build_object('unappliedCash', unapplied_cash, 'prepaidInterest', prepaid_interest,
                   'interestAccruedUnpaid', interest_accrued_unpaid, 'prepaidInterestLots', prepaid_interest_lots_json),
               aggregate_version
        into previous_balances, previous_aggregate_version
        from __SCHEMA__.loan_servicing_projection where loan_id = NEW.loan_id;
        select coalesce(jsonb_agg(jsonb_build_object(
                   'versionNumber', terms_version, 'termsHash', terms_hash, 'terms', terms_json)), '[]'::jsonb)
        into previous_versions
        from __SCHEMA__.loan_terms_version
        where loan_id = NEW.loan_id and jsonb_typeof(terms_json -> 'excessPaymentPolicy') = 'object';
    end if;

    next_balances := NEW.servicing_json -> 'balances';
    if coalesce((next_balances ->> 'unappliedCash')::numeric, 0)
       < coalesce((previous_balances ->> 'unappliedCash')::numeric, 0) then
        raise exception 'Retained direct-lending unapplied cash cannot be removed or reduced without a supported release workflow';
    end if;
    if coalesce((next_balances ->> 'prepaidInterest')::numeric, 0) < 0 then
        raise exception 'Retained direct-lending prepaid interest cannot be negative';
    end if;

    previous_lots := coalesce(nullif(previous_balances -> 'prepaidInterestLots', 'null'::jsonb), '[]'::jsonb);
    next_lots := coalesce(nullif(next_balances -> 'prepaidInterestLots', 'null'::jsonb), '[]'::jsonb);
    if jsonb_typeof(previous_lots) <> 'array' or jsonb_typeof(next_lots) <> 'array' then
        raise exception 'Retained prepaid-interest receipt instructions must be an array';
    end if;
    if exists (select 1 from jsonb_array_elements(next_lots) as lot(value)
               group by lot.value ->> 'cashTransactionId' having count(*) <> 1)
       or exists (select 1 from jsonb_array_elements(next_lots) as lot(value)
                  where coalesce((lot.value ->> 'originalAmount')::numeric, 0) <= 0
                     or coalesce((lot.value ->> 'remainingAmount')::numeric, -1) < 0
                     or (lot.value ->> 'remainingAmount')::numeric > (lot.value ->> 'originalAmount')::numeric)
       or (select coalesce(sum((lot.value ->> 'remainingAmount')::numeric), 0)
           from jsonb_array_elements(next_lots) as lot(value)) > coalesce((next_balances ->> 'prepaidInterest')::numeric, 0) then
        raise exception 'Retained prepaid-interest receipt amounts do not reconcile to their liability';
    end if;

    reduction := coalesce((previous_balances ->> 'prepaidInterest')::numeric, 0)
               - coalesce((next_balances ->> 'prepaidInterest')::numeric, 0);
    if reduction > 0 then
        select payload into release_payload from __SCHEMA__.loan_event
        where loan_id = NEW.loan_id and aggregate_version = NEW.aggregate_version
          and event_type = 'loan.daily-accrual-posted'
          and payload ->> 'cashAccountingVersion' = 'direct-lending-cash-v1';
        if release_payload is null or NEW.aggregate_version is distinct from previous_aggregate_version + 1
           or release_payload ->> 'accrualDate' is null
           or coalesce((release_payload ->> 'prepaidInterestApplied')::numeric, 0) <> reduction
           or reduction > coalesce((release_payload ->> 'interestAmount')::numeric, 0)
           or coalesce((next_balances ->> 'interestAccruedUnpaid')::numeric, 0)
              <> coalesce((previous_balances ->> 'interestAccruedUnpaid')::numeric, 0)
                 + coalesce((release_payload ->> 'interestAmount')::numeric, 0) - reduction then
            raise exception 'Prepaid-interest reduction requires the next retained earned cash-interest release event';
        end if;
        releases := coalesce(nullif(release_payload -> 'prepaidInterestReleases', 'null'::jsonb), '[]'::jsonb);
        if jsonb_typeof(releases) <> 'array' then
            raise exception 'Prepaid-interest release evidence must be an array';
        end if;
        if (select coalesce(sum((item.value ->> 'amount')::numeric), 0)
            from jsonb_array_elements(releases) as item(value)) <> reduction
           or exists (select 1 from jsonb_array_elements(releases) as item(value)
                      where coalesce((item.value ->> 'amount')::numeric, 0) <= 0
                         or not exists (select 1 from jsonb_array_elements(previous_lots) as lot(value)
                                        where lot.value -> 'cashTransactionId' = item.value -> 'cashTransactionId'))
           or exists (select 1 from jsonb_array_elements(releases) as item(value)
                      group by item.value ->> 'cashTransactionId' having count(*) <> 1)
           or jsonb_array_length(next_lots) <> jsonb_array_length(previous_lots) then
            raise exception 'Prepaid-interest release evidence must identify each retained receipt exactly';
        end if;
    end if;

    for previous_lot in select value from jsonb_array_elements(previous_lots)
    loop
        select value into next_lot from jsonb_array_elements(next_lots) as lot(value)
        where lot.value -> 'cashTransactionId' = previous_lot -> 'cashTransactionId';
        if next_lot is null or next_lot - 'remainingAmount' is distinct from previous_lot - 'remainingAmount' then
            raise exception 'A retained prepaid-interest receipt instruction cannot be discarded or rewritten';
        end if;
        select coalesce(sum((item.value ->> 'amount')::numeric), 0) into released
        from jsonb_array_elements(releases) as item(value)
        where item.value -> 'cashTransactionId' = previous_lot -> 'cashTransactionId';
        if (previous_lot ->> 'remainingAmount')::numeric - (next_lot ->> 'remainingAmount')::numeric <> released
           or (released > 0 and (
               (previous_lot ->> 'application')::integer is distinct from 1
               or (previous_lot ->> 'receiptDate')::date >= (release_payload ->> 'accrualDate')::date)) then
            raise exception 'Prepaid-interest receipt decreases require their retained future cash-interest instruction';
        end if;
    end loop;

    for previous_version in
        select value from jsonb_array_elements(previous_versions)
    loop
        if jsonb_typeof(previous_version #> '{terms,excessPaymentPolicy}') = 'object'
           and not exists (
               select 1
               from jsonb_array_elements(coalesce(NEW.contract_json -> 'termsVersions', '[]'::jsonb)) as retained(value)
               where retained.value -> 'versionNumber' = previous_version -> 'versionNumber'
                 and retained.value -> 'termsHash' = previous_version -> 'termsHash'
                 and retained.value -> 'terms' = previous_version -> 'terms') then
            raise exception 'A retained direct-lending contractual cash policy cannot be discarded or rewritten';
        end if;
    end loop;

    if exists (
        select 1
        from jsonb_array_elements(coalesce(NEW.contract_json -> 'termsVersions', '[]'::jsonb)) as retained(value)
        where retained.value -> 'versionNumber' = NEW.contract_json -> 'currentTermsVersion'
          and jsonb_typeof(retained.value #> '{terms,excessPaymentPolicy}') = 'object'
          and retained.value -> 'terms' is distinct from NEW.contract_json -> 'currentTerms') then
        raise exception 'Current direct-lending terms must preserve their retained cash policy version';
    end if;
    return NEW;
end
$function$;

create or replace trigger protect_retained_loan_cash
    before insert or update on __SCHEMA__.loan_state
    for each row execute function __SCHEMA__.protect_retained_loan_cash();
