# CppTrader Host

This folder reserves the native host workspace for the external CppTrader adapter.

The managed integration in `src/Meridian.Infrastructure.CppTrader/` expects a length-prefixed
JSON protocol host executable. The executable path is configured through the `CppTrader`
section in `appsettings.json`.

Expected future contents:

- vendored `CppTrader` sources under `native/vendor/`
- a native host executable wrapping `CppTrader::Matching::MarketManager`
- protocol tests for execution, replay, and ITCH ingestion sessions

The Meridian repo now includes the managed process/protocol boundary, order gateway,
live-feed cache, diagnostics services, and UI endpoints that will consume this host.
