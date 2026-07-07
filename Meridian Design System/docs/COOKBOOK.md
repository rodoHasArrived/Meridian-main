# Meridian Cookbook

Step-by-step recipes that bridge [PATTERNS.md](../PATTERNS.md) (principles) and the
`templates/` (finished screens). Each recipe names the exact components in play and the order
to assemble them. Import everything from the compiled bundle:

```js
const { WorkstationTopbar, DenseDataTable, /* … */ } = window.MeridianDesignSystem_4f61be;
```

All examples assume the global stylesheet is loaded (`styles.css`) so tokens and fonts resolve.

---

## 1 · Triage screen in 8 components

**Goal:** a list-driven monitoring surface where an operator scans a severity-ranked table,
selects a row, and inspects it — the shape behind Alerting, Trading, and Data Ops.

**Assemble:** `WorkstationTopbar` → `NavRail` → `Toolbar` (search + data-state) →
`SplitPane` wrapping `DenseDataTable` (with `SeverityBadge` cells) and a `KeyValueGrid`
inspector → `StatusBar`.

```jsx
<WorkstationTopbar moduleLabel="Alerting" environment="PAPER" clock="14:32:08 UTC" />
<div style={{ display: "flex", flex: 1, minHeight: 0 }}>
  <NavRail activeId="alerting" sections={SECTIONS} onSelect={go} />
  <main style={{ flex: 1, display: "flex", flexDirection: "column", gap: 10, padding: 14 }}>
    <Toolbar>{/* Input + SegmentedControl data-state */}</Toolbar>
    <SplitPane direction="horizontal" primary="end" initial={340} persistKey="triage">
      <DenseDataTable columns={COLS} rows={rows} selectedIndex={i} onRowClick={(_, i) => setI(i)} />
      <KeyValueGrid columns={2} items={detailItems(rows[i])} />
    </SplitPane>
  </main>
</div>
<StatusBar items={STATUS} />
```

**Rule:** always render the full data-state ladder — `SkeletonTable` (loading), `EmptyState`
(empty / no-match), `StatusBanner` tone="danger" (error). Never show stale rows on error.

---

## 2 · Validated form

**Goal:** an operator form that blocks submit until valid and states errors inline — new
report pack, rule editor, connection setup.

**Assemble:** `Form` → `FormField` (label + error slot) wrapping `Input` / `Select` /
`NumberInput` → `FormValidation` for the rules → `Button` (disabled until valid) →
`StatusBanner` for submit-level failures.

```jsx
const form = useFormValidation(values, {
  name:      (v) => !v && "Name is required",
  threshold: (v) => (v <= 0 ? "Must be positive" : null),
});
<Form onSubmit={form.handleSubmit(save)}>
  <FormField label="Pack name" error={form.errors.name}>
    <Input value={values.name} onChange={form.set("name")} />
  </FormField>
  <FormField label="Threshold" error={form.errors.threshold}>
    <NumberInput value={values.threshold} onChange={form.set("threshold")} />
  </FormField>
  <Button variant="primary" type="submit" disabled={!form.isValid}>Create pack</Button>
</Form>
```

**Rule:** validate on blur and on submit, never on first keystroke. The submit button is the
source of truth for validity — disable it, don't just warn.

---

## 3 · Monitoring wall

**Goal:** an at-a-glance operations wall — headline metrics, feed health, and coverage — for a
shared monitor. Terminal density (`data-theme-density="terminal"`) fits more per screen.

**Assemble:** a `MetricCard` grid (KPIs with `Delta`) → a `FreshnessIndicator` row (provider
health) → `Sparkline` trends → `CoverageMatrix` (data-availability heat) → `LogTail` (live run
output).

```jsx
<div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: "var(--gap-panel)" }}>
  <MetricCard label="Open P&L" value="$38,402" delta="+0.4%" trend="up" />
  {/* … */}
</div>
<div style={{ display: "flex", gap: 22 }}>
  {providers.map((p) => <FreshnessIndicator key={p.source} {...p} timeFormat="relative" />)}
</div>
<CoverageMatrix rows={SYMS} cols={DAYS} data={COV} onCellClick={openScan} />
<LogTail title="backfill-7743" height={230} entries={LOG} />
```

**Rule:** the wall is glanceable — no interactions required to read state. Color carries
status (green live / amber partial / red gap); never rely on text alone.

---

## 4 · Books surface

**Goal:** an accounting view — a ledger, its reconciliation state, and the resulting statement.

**Assemble:** `LedgerTable` (double-entry rows with `AmountCell`) → `ReconciliationPanel`
(matched / unmatched) → `StatementTable` (rolled-up totals). `AmountCell` handles sign,
alignment, and the debit/credit convention.

```jsx
<LedgerTable entries={journal} />
<ReconciliationPanel bankRows={bank} bookRows={book} onMatch={match} />
<StatementTable sections={[{ label: "Assets", rows: assets }, { label: "Liabilities", rows: liabilities }]} />
```

**Rule:** money is always mono, right-aligned, tabular-nums, with a fixed decimal count per
column. Negative values use the red wash, never a bare minus buried in text.

---

## 5 · Order flow

**Goal:** the live trading path — pick an instrument, read the book, stage an order behind the
real-capital gate, watch it fill.

**Assemble:** `InstrumentChip` watchlist → `DepthLadder` (price-click prefill) →
`OrderTicket` `environment="live"` (the confirm gate) → `Blotter` + `FillsFeed`.

```jsx
<InstrumentChip symbol="AAPL" venue="XNAS" assetClass="eq" selected onClick={select} />
<DepthLadder bids={bids} asks={asks} lastPrice={201.11} onPriceClick={(p) => ticket.setLimit(p)} />
<OrderTicket symbol="AAPL" lastPrice={201.11} environment="live" onSubmit={placeOrder} />
<Blotter orders={orders} onRowClick={inspect} />
<FillsFeed fills={fills} />
```

**Rule:** in `environment="live"` the submit stays disabled until the operator checks the
real-capital acknowledgement. Side is always a washed green/red — never a solid fill.

---

See the assembled versions under `templates/`: `alerting-workstation` (recipe 1),
`report-scheduler` (2), `ingestion-operations` (3), `journaling-workstation` (4),
`trading-desk` (5).
