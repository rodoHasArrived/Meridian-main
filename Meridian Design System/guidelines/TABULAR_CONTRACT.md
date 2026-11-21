# Tabular contract

**One grid. Capabilities are props, domain tables are schemas, and arithmetic is
a contract.** Meridian previously shipped roughly eighteen table components —
five generic (`DataGrid`, filtered, grouped, expandable, worksheet) and
thirteen domain (`LedgerTable`, `TrialBalance`, `StatementTable`,
`TaxLotTable`, `AgingTable`, `FxRevaluationTable`, `Blotter`,
`OptionChainTable`, `CaseQueue`, …). The generic five were capability variants
of one grid; the domain ones were that grid plus a column schema and an
arithmetic proof.

## Capabilities are props

`DataGrid` takes `select`, `groupBy`, `expand`, `edit`, `totals` and
`density`. There is no "which table do I reach for" decision, and no variant to
add when a screen needs selection *and* grouping.

Density resolves through the registry: `terminal` 26px · `dense` 28px ·
`standard` 36px · `review` 44px. The `compact`/`spacious` spellings alias onto
`dense`/`review` rather than forking row heights.

## Columns are a schema

```js
{ key, label, format, align, width, digits, total, editable, cell }
```

`format` is one of `text · num · money · signed · price · pct` — notation comes
from `guidelines/CONTENT_FUNDAMENTALS.md`, so accounting negatives are
parenthesized and analytical negatives keep a true minus. `cell` renders anything
richer, which is how a cell carries a full `ValueEnvelope`.

Domain tables are entries in `GRID_SCHEMAS`: `ledger`, `trialBalance`,
`taxLots`, `blotter`, `aging`. Adding a domain table means adding a schema —
data, reviewable in one diff — not a module.

## Proof is a first-class feature

Reach the helpers off the carrier — `DataGrid.proof`, `DataGrid.schemas`, `DataGrid.formats` — because only capitalized exports land on the namespace object.

A table that adds up states so:

```js
proof={{ type: "balance", debit: "debit", credit: "credit" }}   // Σ debit = Σ credit ✓ / Out by 12.40
proof={{ type: "sum", column: "basis", expected: 0 }}
proof={{ type: "custom", check: (rows) => ({ ok, label, text }) }}
```

The proof strip is part of the grid, below the totals, in the semantic green or
red. **An operator never adds a column up to find out whether it ties.** A grid
whose arithmetic matters and declares no proof is an incomplete grid.

## Rules

1. New tabular surfaces use `DataGrid`. No new table components.
2. A domain table is a `GRID_SCHEMAS` entry plus, where arithmetic matters, a proof.
3. The grid never reaches upward: provenance enters through `column.cell`, so
   layer 2 stays independent of the operations layer.
4. Right-align every numeric column; tabular numerals are mandatory.
5. Empty is `—`, never blank; a missing value is a condition, not a gap.
6. Totals and proofs never scroll away from the rows they describe.
7. Every surface, border and semantic colour comes from a real token
   (`--bg-surface`, `--surface-recessed`, `--ws-row-selected`, `--green`,
   `--red`) so the grid follows all eleven theme scopes. Never invent a token
   name — check `guidelines/TOKEN_REFERENCE.md` first; a hex fallback that never
   resolves is a light-mode bug in dark mode.
