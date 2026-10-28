-- Migration 042: Promote the terms-payload family to a queryable column.
-- The asset_specific_terms slot holds three payload families (flat, economic, customProfile)
-- behind one shared `schemaVersion` integer. Every new write declares its family in the payload's
-- `termsFormat` key (SecurityTermsEnvelope.FormatKey); rows written before the tag existed can only
-- be classified by shape. Promote the family to a first-class column so compatibility and audit
-- queries can select by family directly (e.g. WHERE terms_format = 'economic') and so the
-- untagged backlog can be counted and retired (ADR-024) without a per-row JSON inspection.
-- The stored payload is never rewritten by this migration.

alter table __SCHEMA__.securities
    add column if not exists terms_format text not null default 'flat';

-- Backfill from the payload. A declared termsFormat wins; an untagged payload is classified by the
-- SAME shape rule SecurityTermsEnvelope.ResolveFormat applies on read (and that
-- SecurityTermsVersioningTests pins against this file): a profile envelope (string customProfileId
-- plus object profileFields) is customProfile; a version-2 document carrying any economic module key
-- is economic; anything else is flat.
update __SCHEMA__.securities
set terms_format = case
    when jsonb_typeof(asset_specific_terms->'termsFormat') = 'string'
        then asset_specific_terms->>'termsFormat'
    when jsonb_typeof(asset_specific_terms->'customProfileId') = 'string'
         and jsonb_typeof(asset_specific_terms->'profileFields') = 'object'
        then 'customProfile'
    when jsonb_typeof(asset_specific_terms->'schemaVersion') = 'number'
         and (asset_specific_terms->>'schemaVersion')::integer = 2
         and asset_specific_terms ?| array[
             'maturity', 'coupon', 'payment', 'accrual', 'discount',
             'redemption', 'call', 'auction', 'sweep', 'financing', 'issuer', 'equityBehavior', 'fund', 'structuredProduct']
        then 'economic'
    else 'flat'
end
where jsonb_typeof(asset_specific_terms) = 'object';

-- Only the three known families are storable; the projection store refuses an unknown declared
-- format before reaching this constraint, with a precise reason.
do $$
begin
    alter table __SCHEMA__.securities
        add constraint ck_securities_terms_format
        check (terms_format in ('flat', 'economic', 'customProfile'));
exception
    when duplicate_object then null;
end $$;

create index if not exists ix_securities_terms_format
    on __SCHEMA__.securities (terms_format);
