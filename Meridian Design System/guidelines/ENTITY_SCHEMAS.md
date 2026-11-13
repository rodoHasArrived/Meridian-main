# Meridian — Shared Entity Schemas

The system's strongest idea is its **contracts**: `SeverityBadge` normalizes any status vocabulary
to one 4-state scale; validation issues are always `{ code, severity, message }`. This document
extends that discipline to the **domain entities** that flow through multiple components, so a
consumer wires one shape and it works in `Blotter`, `FillsFeed`, `NotificationCenter`, the tables,
and the templates — instead of each surface inventing field names.

These are **wire shapes**, not TypeScript you import. Map your backend to them once at the edge.

---

## Conventions (all entities)

- **`id`** — stable string, unique within its kind. Selection, keys, and dedup rely on it.
- **Time** — epoch **ms** (number) or ISO-8601 UTC string. Always UTC; `Timestamp` renders it.
  Field names ending in `At` are absolute times (`firedAt`, `filledAt`, `completedAt`).
- **Money / prices** — strings or numbers in the instrument's currency, unrounded; format at the
  edge with `Delta` / `AmountCell`, never in the data.
- **Status** — a free string; components normalize it through `SeverityBadge`. Don't pre-map to
  Ready/Review/Action/Blocked — pass the domain word ("Filled", "Degraded") and let the badge map it.
- **`env`** — `"live" | "paper" | "fixture"` wherever an entity is environment-scoped. Drives the
  mode chip and the OrderTicket confirm gate.

---

## Order

Consumed by `OrderTicket` (output), `Blotter`, order tables, run/audit rails.

```ts
Order {
  id: string;              // "ORD-1207"
  createdAt: number|string;
  symbol: string;          // "AAPL"
  side: "Buy" | "Sell";
  qty: number|string;
  type: "Market" | "Limit" | "Stop" | "StopLimit";
  limitPrice?: number|string;   // required when type involves a limit
  tif: "DAY" | "GTC" | "IOC" | "FOK";
  filledQty?: number|string;
  status: string;          // Working · Filled · Partially filled · Cancelled · Rejected
  env: "live" | "paper" | "fixture";
  account?: string;
}
```

## Fill

Consumed by `FillsFeed`, execution tapes. One partial execution of an order.

```ts
Fill {
  id: string;
  orderId?: string;        // back-reference to the Order
  filledAt: number|string;
  symbol: string;
  side: "Buy" | "Sell";
  qty: number|string;
  price: number|string;
  venue?: string;          // "XNAS"
}
```

## Alert

Consumed by the alerting template, `NotificationCenter` (via a mapper), severity tables.

```ts
Alert {
  id: string;              // "ALR-2214"
  firedAt: number|string;
  rule: string;            // human rule name
  scope: string;           // "momentum.v4" · "Polygon · XNAS"
  severity: string;        // Critical · Warning · Info  → SeverityBadge
  state: "Open" | "Acked" | "Closed";
  value?: string;          // observed
  threshold?: string;      // the breached bound
  env?: "live" | "paper" | "fixture";
}
```

**Alert → NotificationItem** (the one adapter worth writing once):
```js
const toNotification = (a) => ({
  id: a.id, time: a.firedAt, title: a.rule, detail: `${a.scope} · ${a.value ?? ""}`.trim(),
  tone: /crit/i.test(a.severity) ? "error" : /warn/i.test(a.severity) ? "warning" : "info",
  read: a.state !== "Open",
});
```

## Run

Consumed by run-history ledgers, `StatusBanner`, backtest/strategy templates.

```ts
Run {
  id: string;              // "RUN-8841"
  kind: string;            // "backtest" · "collection" · "reconciliation"
  startedAt: number|string;
  completedAt?: number|string;
  status: string;          // Running · Complete · Failed · Queued  → SeverityBadge
  progress?: number;       // 0..1 while running
  metrics?: Record<string, number|string>;  // sharpe, pnl, gaps…
  env?: "live" | "paper" | "fixture";
}
```

## Strategy

The seventeenth ObjectModel entry. Carried by `StrategyModel` (createDraft/validateStage/
checkpoint/applyTransition); lifecycle vocabulary in `STRATEGY_LIFECYCLE`; the six guarded
formation stages in `STRATEGY_FORMATION_STEPS` (Mandate · Thesis · Universe · Portfolio rules ·
Risk & governance · Review).

```ts
Strategy {
  id: string;              // "STR-0007" — one versioned identity across all seven screens
  version: number;         // bumped by formation checkpoint, never by navigation
  lifecycle: "draft"|"validating"|"blocked"|"ready-for-review"|"in-review"
           | "approved"|"changes-requested"|"active"|"suspended"|"superseded"|"retired";
  name: string;
  mandate:        { objective, benchmark, capitalBand: {min,max}, horizon, owner? };
  thesis:         { statement, edge, decay, capacity? };
  universe:       { datasetId, assetClass, filters: {field,op,value}[], minAdv, exclusions? };
  portfolioRules: { entry, exit, sizing, maxPositions, rebalance,
                    worksheet: {ref, formula, status: "ok"|"error", message?}[] };
  riskGovernance: { maxDrawdownPct, grossExposureMax, perNameMax, stopPolicy?,
                    reviewCadence, approvers: string[] };
  review:         { acknowledged, acknowledgedHash, notes? };  // hash-bound: edits stale it
  history: { from, to, at, actor, note }[];
  createdAt; updatedAt;
}
```

Gates are **recomputed from current field values** — `StrategyModel.validateStage(draft, stage)`
is pure, so an edit invalidates dependent stages, review acknowledgment (`acknowledgedHash ≠
formationHash`), and run-proof freshness (`materialHash` moved) with no event wiring.

## StrategyFormationSnapshot

The frozen formation record a checkpoint or run captures. Produced by `StrategyModel.snapshot`.

```ts
StrategyFormationSnapshot {
  schema: "meridian.strategy.formation/1";
  strategyId: string; strategyVersion: number;
  capturedAt: number|string;
  fields: { name, mandate, thesis, universe, portfolioRules, riskGovernance };  // deep-cloned
  review: { acknowledged, acknowledgedHash, notes };
  formationHash: string;   // SHA-256, canonical mjcf/1, ALL formation fields — review freshness
  materialHash: string;    // SHA-256 of MATERIAL inputs only — run-proof freshness
}
```

Material = universe, portfolio rules (worksheet cells included), risk limits, benchmark.
Narrative (thesis wording, description) hashes into `formationHash` only, so a copy edit stales
a review but never falsely stales a run proof.

## StrategyRunArtifact v1

The immutable run record. Carried by `StrategyRunModel` (createRun/startRun/completeRun/
verifyProof/freshness + legacy `Run` adapters). `completeRun` derives metrics **once** from the
raw series and trades, then deep-freezes — historical artifacts never change.

```ts
StrategyRunArtifact {
  schema: "meridian.strategy.run-artifact/1";
  id: string;              // "RUN-8841"
  strategyId: string; strategyVersion: number;
  env: "live"|"paper"|"fixture";                                  // axis 1 — environment
  lifecycle: "queued"|"running"|"complete"|"failed"|"cancelled";  // axis 2 — run lifecycle
  requestedAt; startedAt; completedAt;
  request: { window: {start,end}, dataset: {id,label?}, engine: {name,version}, costModel? };
  inputs:  { snapshot: StrategyFormationSnapshot };               // frozen at createRun
  proof:   { algo:"sha256", canonical:"mjcf/1", materialHash, inputHash, hashedAt };
  series:  { equity: {t,v}[], benchmark?: {t,v}[] };              // raw evidence
  trades:  { symbol, side, qty, entryAt, entryPx, exitAt?, exitPx?, fees? }[];
  metrics: { totalReturn, benchmarkReturn, excessReturn, cagr, maxDrawdown, sharpe,
             winRate, avgHoldingDays, turnover, fees };           // derived once; null + issue when evidence is missing
  methods: Record<string,string>;                                 // declared derivation per metric
  validation: { disposition: "pass"|"pass-with-issues"|"fail", issues: {code,severity,message}[] };  // axis 3
  error: string|null;
}
```

**Independent axes — never derive one from another:**

| Axis | Values | Where it lives |
| --- | --- | --- |
| Environment | live · paper · fixture | `env` (uppercase input normalized) |
| Run lifecycle | queued · running · complete · failed · cancelled | `lifecycle` |
| Validation disposition | pass · pass-with-issues · fail | `validation.disposition` |
| Proof integrity | verified · unverified · mismatch | **recomputed** by `verifyProof(artifact)` |
| Current-draft freshness | current · stale · unknown | **derived** by `freshness(artifact, draft)` — never stored |

Metric law: `cagr` is ACT/365.2425 on actual elapsed days; `maxDrawdown` is rolling-peak
close-to-close (a negative fraction); `sharpe` ships its declared method (arithmetic daily ·
rf 0 · sample sd · √252). Missing evidence → `null` **plus** a validation issue — never a
fixture value; absent fees are null, not zero.

## Instrument

Consumed by `AsyncCombobox` option sets, security-master registry, symbol pickers.

```ts
Instrument {
  symbol: string;          // primary key ("AAPL")
  name: string;            // "Apple Inc."
  assetClass?: "equity" | "future" | "fx" | "option" | "crypto";
  exchange?: string;       // "XNAS"
  currency?: string;       // "USD"
}
```

For `AsyncCombobox`: `getKey={(o) => o.symbol}` `getLabel={(o) => o.name}` `getSecondary={(o) => o.name}`.

---

## Why this matters

The templates already speak these shapes. When a consumer's data matches, `Blotter`, `FillsFeed`,
`NotificationCenter`, and the tables drop in with zero adapter code — and the severity/time/money
content rules are enforced by construction, because the rendering components own the formatting.
Diverge from a field name and you re-inherit every content-rule bug the system exists to prevent.
