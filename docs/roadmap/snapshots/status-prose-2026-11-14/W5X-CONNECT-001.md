# W5X-CONNECT-001 status-detail migration snapshot

Captured from `docs/roadmap/data/roadmap-items.yml` at `3503ae000e` on 2026-11-14.
This retains the original implementation detail, reservations and historical claims verbatim.
Read the current registry for status and decisions; this snapshot does not grant new acceptance.
Detailed boundaries remain applicable until a later scoped change replaces them.

```yaml
    current_summary: Delivered 2026-07-02, completed operator scheduled-fetch coverage 2026-07-18, and deepened brokerage-account evidence 2026-07-18. Statement connectors ship as data, not code - declarative versioned CSV/OFX mapping-profile documents (operator-editable, atomic-write persisted, drift-detected), uploaded or remotely fetched IB Flex Report XML, OFX 1.x/2.x bank and investment statements, and a fetch-capable Alpaca activity plus portfolio connector reusing the existing brokerage gateway and credential vault. Alpaca activity uses bounded complete cursor pagination; IB Flex uses the documented v3 request/retrieve flow. Every connector classifies canonical records and retains a structured sidecar for account margin, activity subtype/completeness, option lifecycle, tax-lot, and borrow evidence. The Accounting Import Statement surface previews that evidence before committing into the existing reconciliation queue. Margin Control rolls evidence up across accounts and prime brokers, keeps provider margin authoritative, labels Meridian calculations as diagnostic shadows, and gates durable EOD certification on freshness, completeness, severity, and operator permission. Duplicate-key idempotency is preserved; transient scheduled-fetch failures do not advance the successful watermark or expose exception messages.
```
