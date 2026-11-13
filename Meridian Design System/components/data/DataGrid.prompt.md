# DataGrid

The one tabular primitive: capabilities are props (`select`, `groupBy`, `expand`, `edit`, `totals`, `density`), columns are a schema, and `proof` makes the table state its own arithmetic. Domain tables are `GRID_SCHEMAS` entries (ledger, trialBalance, taxLots, blotter, aging), not separate modules. Cells can carry a full `ValueEnvelope` via `column.envelope`. Law: `guidelines/TABULAR_CONTRACT.md`.

**Sorting** — `sortable` lets the grid own header-click sort; a screen that owns row order passes `sortKey`/`sortDir`/`onSort` and already-ordered `rows` (the grid renders only the indicators). **Row activation** — `onRowClick` + `activeKey` highlight the operator's committed row, independent of checkbox `select`.

4.3.0 audit: the scroll region is keyboard focusable and shows an inset focus ring. Supply `label` so assistive technology announces the table region; native arrow keys scroll overflow.

Financial boundaries: an explicit money zero renders `0.00`; absent values render an em dash. Built-in proofs and sum totals require finite numeric evidence in every applicable row. Missing, nonnumeric, or empty evidence produces `Incomplete evidence` (proof) or a missing total rather than a fabricated zero/balance. These checks are presentation arithmetic, not backend reconciliation certification; retain domain accounting tables where exact decimal, restricted-data, or posting semantics are richer. Editable cells support Enter/F2 to start, Enter to commit once, and Escape to cancel; expanders support Enter/Space.
