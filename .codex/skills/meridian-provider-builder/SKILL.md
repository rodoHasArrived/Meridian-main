---
name: meridian-provider-builder
description: Build or extend Meridian market data providers. Use when the user asks to add a provider, scaffold a new adapter, extend a streaming client, add a historical provider, add symbol search support, or make a ProviderSdk-compliant integration for a broker, exchange, or data vendor in Meridian.
---

# Meridian Provider Builder

Build provider code that fits Meridian's provider contracts, rate-limiting rules, serialization patterns, and DI structure on the first pass.

Read `../_shared/project-context.md` before starting. Read `references/provider-patterns.md` when you need skeletons, file layout, or compliance reminders.

## Choose the Provider Type First

- Streaming provider: implement `IMarketDataClient`
- Historical provider: implement `IHistoricalDataProvider`
- Symbol search provider: follow the existing symbol-search patterns in `src/Meridian.Infrastructure/Adapters/`

Start from the closest template or existing provider in `src/Meridian.Infrastructure/Adapters/`, not a blank design.

## Workflow

1. Identify the provider type and the closest Meridian template.
2. Inspect existing providers for naming, DI module, options, and models.
3. Create the minimum file set: implementation, options, DTO/models if needed, registration/module changes, and tests.
4. Wire cancellation, logging, serialization, rate limiting, and reconnect behavior before polishing anything else.
5. Run targeted tests for the new provider or scaffold if the full provider cannot be validated yet.

## Provider Rules

- Use `IOptionsMonitor<T>` for provider settings.
- Use source-generated JSON contexts for serialization.
- Forward the real `CancellationToken`.
- Use the repository's rate-limiting infrastructure for historical providers.
- Use the existing WebSocket resilience and reconnection patterns for streaming providers.
- Keep provider discovery attributes and ADR attributes aligned with existing implementations.
- Register the provider through the repository's DI/module pattern, not ad hoc host-only wiring.

## Deliverables

A solid provider task usually includes:

- implementation class
- options/config model
- response models or DTOs
- DI/module registration
- tests or at least a compilable test scaffold in the correct project

## Quality Bar

- Match the file and namespace conventions of neighboring providers.
- Avoid custom one-off infrastructure when a shared provider helper already exists.
- If the API shape is uncertain, build a thin, testable adapter seam rather than spreading vendor-specific logic everywhere.
