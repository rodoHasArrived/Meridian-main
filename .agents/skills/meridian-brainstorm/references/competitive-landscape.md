# Competitive Landscape — Meridian Brainstorm Reference

Understanding the market helps identify where Meridian can differentiate vs. where it needs feature parity.

---

## Paid Commercial Providers

### Bloomberg Terminal / Bloomberg B-PIPE

- Gold standard for institutional data; real-time + historical across all asset classes
- Costs $20K-$24K/user/year
- Meridian opportunity: target long tail who cannot justify this cost — hobbyists, small funds, academic labs
- Features worth borrowing: data lineage tagging, real-time anomaly flagging

### Databento

- Modern developer-first market data API; pay-per-use; MBO + MBP data; DBN binary format
- Pricing: ~$0.10-$1.00/symbol-day historical; real-time from $150/month
- Meridian advantage: cloud-only with no self-hosted option; Meridian wins on on-premise, no per-tick fees for self-collected data
- Features worth borrowing: DBN format, databento-python ergonomics, MBO data model

### Polygon.io

- REST + WebSocket for US equities, options, forex, crypto; free tier available
- Meridian advantage: Polygon does not enable structured local storage; Meridian local-first means no ongoing per-query cost
- Features worth borrowing: aggregates/OHLCV API design, options chain endpoint

---

## Open Source / Community Tools

### QuestDB

- Open-source time-series SQL database; ILP ingest; nanosecond timestamp support
- Natural Meridian storage backend replacing JSONL for query-heavy use cases
- Integration: Meridian to QuestDB sink (ILP over TCP) alongside JSONL

### QuantConnect LEAN / Backtrader / Zipline

- Open-source backtesting frameworks with their own data ingestion pipelines
- None have a good live-data collection layer — Meridian can be "the collector that feeds your backtesting framework"
- LEAN bridge is highest-value (large QuantConnect community)

### OpenBB Terminal

- Open-source Bloomberg Terminal alternative; Python-based; no persistent storage
- Meridian could be an OpenBB data provider backend, giving OpenBB users high-quality local storage

---

## Differentiation Matrix

| Capability          | Bloomberg | Databento | Polygon | Meridian now | Meridian potential |
|---------------------|-----------|-----------|---------|---------|---------------|
| Self-hosted         | No        | No        | No      | Yes     | Yes           |
| Multi-provider      | Yes       | No        | No      | Yes (5) | Yes (90+)     |
| Free tier           | No        | No        | Yes     | Yes     | Yes           |
| Sub-ms latency      | Yes       | Yes       | No      | Yes     | Yes           |
| Python SDK          | Yes       | Yes       | Yes     | No      | Planned       |
| L2 order book       | Yes       | Yes       | Yes     | Yes(IB) | Yes           |
| Data provenance     | Yes       | Partial   | No      | No      | Opportunity   |
| Backtesting engine  | No        | No        | No      | Yes     | Yes (full)    |
| Paper trading       | No        | No        | No      | Yes     | Yes           |
| Strategy execution  | No        | No        | No      | Yes     | Yes (live)    |
| Pre-trade risk      | Yes       | No        | No      | Yes     | Yes           |
| Community sharing   | No        | No        | No      | No      | Opportunity   |
| Academic citation   | No        | No        | No      | No      | Opportunity   |
| MCP / AI tooling    | No        | No        | No      | Yes     | Yes           |

Meridian defensible moats:

1. Self-hosted, no per-query cloud fees
2. Multi-provider failover and reconciliation (no competitor does this affordably)
3. Full platform: data collection → backtesting → execution → strategy lifecycle in one codebase
4. Hackable open architecture — plugins, custom sinks, C# + F# extensibility
5. MCP server layer — AI-native tooling surface (unique in this space)
