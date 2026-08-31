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
