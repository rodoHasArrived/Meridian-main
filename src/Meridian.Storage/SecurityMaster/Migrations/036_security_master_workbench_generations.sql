-- Durable compare-and-set fence shared by overlay and governed revision mutations.
-- The fence exists even before the first overlay/draft; its increment commits atomically
-- with both stores. Failed or cancelled operations roll back all three together.
create table if not exists __SCHEMA__.security_workbench_generations (
    security_id uuid primary key,
    generation bigint not null check (generation >= 0)
);
