// ARCHIVE TOMBSTONE
// Retired 2026-07-06 as an unused shared primitive. `useTableState` (search /
// multi-select filter / sort state with optional localStorage persistence and CSV
// export) had zero screen or view-model consumers — only a `components/data/concrete`
// barrel re-export and its own test referenced it. Screens manage table state locally;
// dense-table behavior lives in `lib/dense-table-virtualization.ts`. Restore into the
// active tree and wire a real consumer before reintroducing.
