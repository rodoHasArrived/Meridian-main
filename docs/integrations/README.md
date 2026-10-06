# Integration Guides

**Status:** supporting
**Owner:** core-team
**Reviewed:** 2026-07-19

Supporting guides for third-party integrations and language interoperability. Start with
[Engineering](../engineering/README.md) for build/test workflow or
[Operators](../operators/README.md) for provider setup. Implementation readiness belongs to the
[adapter registry and generated matrix](../source/generated/adapter-readiness-matrix.md);
external provider catalogs and historical proposals do not establish production support.

## Documents

| Document | Description |
|----------|-------------|
| [QuantConnect Lean Integration](lean-integration.md) | Optional `EnableLeanIntegration=true` build, JSONL staging paths, and sample backtest reader limitations. |
| [F# Integration](fsharp-integration.md) | Market-data examples, C# wrapper contracts, and scoped F# test commands. |
| [Language Strategy](language-strategy.md) | Historical 2026-01-30 proposal and implementation notes; use Engineering and the roadmap registry for current work. |
| [Tastytrade Endpoint Coverage](tastytrade-endpoint-coverage.md) | Dated endpoint planning catalog, including inferred routes that need provider verification. |

## Related

- [F# AI Guide](../ai/claude/CLAUDE.fsharp.md) — AI assistant guide for F# code
- [ADR-009: F# Interop](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/adr/009-fsharp-interop.md) — Architecture decision for F# integration
- [Provider Implementation](../development/provider-implementation.md) — Adding new data providers
