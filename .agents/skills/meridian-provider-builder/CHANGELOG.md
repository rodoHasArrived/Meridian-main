# meridian-provider-builder — Changelog

## v1.0.0 (2026-03-16)

### Added

- Initial skill release targeting all three provider types:
  `IMarketDataClient` (streaming), `IHistoricalDataProvider` (historical),
  and `ISymbolSearchProvider` (symbol search)
- **12-step build process** covering: template selection, directory layout,
  required attributes, `IOptionsMonitor<T>` configuration, rate limiting via
  `WaitForRateLimitSlotAsync(ct)`, WebSocket reconnection, ADR-014 JSON
  source generation, `IHttpClientFactory` HTTP client registration,
  `CancellationToken` propagation, `DisposeAsync` pattern, DI registration
  via `IProviderModule`, and test scaffold requirements
- **Compliance checklist** — 20-item pre-submission checklist covering ADR
  compliance, resilience, serialization, and test coverage
- **Known AI error table** — 7 documented pitfalls with symptoms and fixes
  (`WaitAsync()` vs `WaitForSlotAsync()`, `IOptions<T>` vs `IOptionsMonitor<T>`,
  missing reconnection, `new HttpClient()`, missing `[ImplementsAdr]`,
  reflection JSON, `CancellationToken.None` in `DisposeAsync`)
- **`references/provider-patterns.md`** — 7 copy-ready implementation patterns:
  - Historical provider full skeleton with BaseHistoricalDataProvider
  - Streaming provider full skeleton with reconnect loop
  - Provider options class
  - DI registration module
  - JsonSerializerContext registration diff
  - Historical provider test scaffold (5 test cases)
  - Streaming provider test scaffold (3 test cases)
  - `appsettings.sample.json` section template
  - Rate limiter quick reference table
  - ADR compliance quick reference table
- **Shared project context** — SKILL.md references `../_shared/project-context.md`
  for authoritative file paths and provider inventory
