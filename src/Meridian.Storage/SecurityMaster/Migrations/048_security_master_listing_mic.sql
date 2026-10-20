-- Migration: security-master / security_master_listing_mic
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- Additive: listing_mic carries the ISO 10383 MIC of a security's primary listing next to the
-- free-text exchange_code, which is left unchanged. The backfill mirrors the write path
-- (PostgresSecurityMasterStore.ResolveListingMic): a well-formed commonTerms.primaryListingMic
-- wins; otherwise exchange_code resolves through the bundled MicReference codes and exchange
-- names. Unmappable labels (routing venues such as SMART, unknown names) stay null.

alter table __SCHEMA__.securities
    add column if not exists listing_mic text null;

update __SCHEMA__.securities
set listing_mic = case
    when upper(btrim(common_terms ->> 'primaryListingMic')) ~ '^[A-Z0-9]{4}$'
        then upper(btrim(common_terms ->> 'primaryListingMic'))
    when upper(btrim(exchange_code)) in ('XNAS', 'XNYS', 'ARCX', 'XASE', 'XCHI', 'XBOS', 'XPHL', 'BATS', 'BATY', 'EDGA', 'EDGX', 'XCBO', 'IEXG', 'MEMX', 'MIHI', 'LTSE', 'FINN', 'OTCM', 'XCME', 'XCBT', 'XNYM', 'IFUS', 'XLON', 'XTSE', 'XETR', 'XPAR', 'XAMS', 'XSWX', 'XTKS', 'XHKG', 'XASX')
        then upper(btrim(exchange_code))
    when upper(btrim(exchange_code)) = 'NASDAQ' then 'XNAS'
    when upper(btrim(exchange_code)) = 'NASDAQ GS' then 'XNAS'
    when upper(btrim(exchange_code)) = 'NASDAQ GM' then 'XNAS'
    when upper(btrim(exchange_code)) = 'NASDAQ CM' then 'XNAS'
    when upper(btrim(exchange_code)) = 'NYSE' then 'XNYS'
    when upper(btrim(exchange_code)) = 'NYSE ARCA' then 'ARCX'
    when upper(btrim(exchange_code)) = 'ARCA' then 'ARCX'
    when upper(btrim(exchange_code)) = 'NYSE AMERICAN' then 'XASE'
    when upper(btrim(exchange_code)) = 'NYSE MKT' then 'XASE'
    when upper(btrim(exchange_code)) = 'AMEX' then 'XASE'
    when upper(btrim(exchange_code)) = 'CBOE' then 'XCBO'
    when upper(btrim(exchange_code)) = 'IEX' then 'IEXG'
    when upper(btrim(exchange_code)) = 'OTC' then 'OTCM'
    when upper(btrim(exchange_code)) = 'CME' then 'XCME'
    when upper(btrim(exchange_code)) = 'CBOT' then 'XCBT'
    when upper(btrim(exchange_code)) = 'NYMEX' then 'XNYM'
    when upper(btrim(exchange_code)) = 'LSE' then 'XLON'
    when upper(btrim(exchange_code)) = 'TSX' then 'XTSE'
    when upper(btrim(exchange_code)) = 'SIX' then 'XSWX'
    when upper(btrim(exchange_code)) = 'TSE' then 'XTKS'
    when upper(btrim(exchange_code)) = 'HKEX' then 'XHKG'
    when upper(btrim(exchange_code)) = 'ASX' then 'XASX'
    else null
end
where listing_mic is null;

create index if not exists ix_securities_listing_mic
    on __SCHEMA__.securities (listing_mic)
    where listing_mic is not null;
