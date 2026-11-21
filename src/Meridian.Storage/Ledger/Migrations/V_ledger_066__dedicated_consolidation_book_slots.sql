-- Additional reporting books are isolated to explicitly governed Fund/Primary policy pairs.
-- Ordinary entity/source and Fund books keep their original per-basis uniqueness.
drop index if exists __SCHEMA__.ux_ledger_books_fund_node_basis;
create unique index ux_ledger_books_fund_node_basis
    on __SCHEMA__.ledger_books(fund_profile_id,fund_structure_node_id,accounting_basis)
    where not (fund_structure_node_kind = 'Fund' and accounting_basis = 'Primary' and (
        (accounting_policy_id = 'consolidation-v1' and accounting_policy_version = 'w10-v1') or
        (accounting_policy_id = 'consolidation-v2' and accounting_policy_version = 'w10-v2') or
        (accounting_policy_id = 'consolidation-v3' and accounting_policy_version = 'w10-v3') or
        (accounting_policy_id = 'consolidation-v4' and accounting_policy_version = 'w10-v4')));
create unique index if not exists ux_ledger_books_consolidation_name
    on __SCHEMA__.ledger_books(fund_profile_id,fund_structure_node_id,accounting_basis,lower(display_name))
    where fund_structure_node_kind = 'Fund' and accounting_basis = 'Primary' and (
        (accounting_policy_id = 'consolidation-v1' and accounting_policy_version = 'w10-v1') or
        (accounting_policy_id = 'consolidation-v2' and accounting_policy_version = 'w10-v2') or
        (accounting_policy_id = 'consolidation-v3' and accounting_policy_version = 'w10-v3') or
        (accounting_policy_id = 'consolidation-v4' and accounting_policy_version = 'w10-v4'));
