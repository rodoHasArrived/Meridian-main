-- Minimum writer capability for the declared-schedule contract step (ADR-025, increment 5b-1).
-- Empty until the reviewed 5b-2 deployment writes its floor as its first action. Applications only
-- read this table; this migration does not enable declarations, change write versions or deploy.
create table __SCHEMA__.security_master_terms_capability_floor (
    singleton boolean not null primary key check (singleton),
    minimum_declared_mapping_version text not null check (length(trim(minimum_declared_mapping_version)) > 0),
    minimum_kernel_version text not null check (length(trim(minimum_kernel_version)) > 0),
    change_record text not null check (length(trim(change_record)) > 0),
    updated_by text not null check (length(trim(updated_by)) > 0),
    updated_at timestamptz not null default now()
);

comment on table __SCHEMA__.security_master_terms_capability_floor is
    'One reviewed minimum declared-mapping/kernel capability floor for configured Security Master writers. Empty during 5b-1; activated only after the fleet inventory and data gates, before 5b-2. Server and WPF writer startup refuse unknown/newer active floors. Does not enable declared terms or authorize deployment.';
