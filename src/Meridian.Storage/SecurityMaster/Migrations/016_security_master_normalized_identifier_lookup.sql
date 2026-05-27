alter table __SCHEMA__.securities
    add column if not exists normalized_primary_identifier_value text null;

update __SCHEMA__.securities
set normalized_primary_identifier_value = case
    when primary_identifier_kind in ('Isin', 'Cusip', 'Sedol', 'Figi', 'OccOptionSymbol', 'Lei', 'Wkn', 'Cik')
        then regexp_replace(upper(btrim(primary_identifier_value)), '[^A-Z0-9]', '', 'g')
    when primary_identifier_kind = 'Valoren'
        then regexp_replace(upper(btrim(primary_identifier_value)), '[^0-9]', '', 'g')
    else upper(btrim(primary_identifier_value))
end
where normalized_primary_identifier_value is null;

alter table __SCHEMA__.securities
    alter column normalized_primary_identifier_value set not null;

create index if not exists ix_securities_normalized_primary_identifier
    on __SCHEMA__.securities (primary_identifier_kind, normalized_primary_identifier_value);

alter table __SCHEMA__.security_identifiers
    add column if not exists normalized_identifier_value text null,
    add column if not exists normalized_provider text null;

update __SCHEMA__.security_identifiers
set normalized_identifier_value = case
        when identifier_kind in ('Isin', 'Cusip', 'Sedol', 'Figi', 'OccOptionSymbol', 'Lei', 'Wkn', 'Cik')
            then regexp_replace(upper(btrim(identifier_value)), '[^A-Z0-9]', '', 'g')
        when identifier_kind = 'Valoren'
            then regexp_replace(upper(btrim(identifier_value)), '[^0-9]', '', 'g')
        else upper(btrim(identifier_value))
    end,
    normalized_provider = nullif(upper(btrim(provider)), '')
where normalized_identifier_value is null
   or (provider is not null and normalized_provider is null);

alter table __SCHEMA__.security_identifiers
    alter column normalized_identifier_value set not null;

create index if not exists ix_security_identifiers_normalized_lookup
    on __SCHEMA__.security_identifiers (identifier_kind, normalized_identifier_value, normalized_provider);

alter table __SCHEMA__.security_aliases
    add column if not exists normalized_alias_value text null,
    add column if not exists normalized_provider text null;

update __SCHEMA__.security_aliases
set normalized_alias_value = case
        when alias_kind in ('Isin', 'Cusip', 'Sedol', 'Figi', 'OccOptionSymbol', 'Lei', 'Wkn', 'Cik')
            then regexp_replace(upper(btrim(alias_value)), '[^A-Z0-9]', '', 'g')
        when alias_kind = 'Valoren'
            then regexp_replace(upper(btrim(alias_value)), '[^0-9]', '', 'g')
        else upper(btrim(alias_value))
    end,
    normalized_provider = nullif(upper(btrim(provider)), '')
where normalized_alias_value is null
   or (provider is not null and normalized_provider is null);

alter table __SCHEMA__.security_aliases
    alter column normalized_alias_value set not null;

create index if not exists ix_security_aliases_normalized_lookup
    on __SCHEMA__.security_aliases (alias_kind, normalized_alias_value, normalized_provider, is_enabled);
