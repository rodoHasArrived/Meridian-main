---
name: meridian-provider-builder
description: Build or extend Meridian market data providers. Use when the user asks to add a provider, scaffold a new adapter, extend a streaming client, add a historical provider, add symbol search support, or make a ProviderSdk-compliant integration for a broker, exchange, or data vendor in Meridian.
---

# Meridian Provider Builder

Build provider code that fits Meridian's provider contracts, rate-limiting rules, serialization patterns, and DI structure on the first pass.

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before starting.
Read `references/provider-patterns.md` when you need skeletons, file layout, or compliance
reminders.

## Use When

Use this skill when the task is to add, scaffold, or extend a Meridian provider adapter or provider
support surface.

Trigger examples:

- "Add a new historical provider for FRED."
- "Extend the Alpaca streaming adapter with reconnect handling."
- "Build symbol search support for this data vendor."

## Do Not Use When

Use `meridian-blueprint` for provider architecture planning, `meridian-code-review` for auditing an
existing provider, and `meridian-test-writer` when the only requested work is test coverage.

Non-trigger examples:

- "Design a provider selection architecture."
- "Review this provider for bugs."
- "Write tests for the existing provider only."

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

## Handoffs

- Hand off to `meridian-blueprint` when provider scope affects shared contracts or multi-provider architecture.
- Hand off to `meridian-test-writer` for broader scenario coverage after the provider scaffold exists.
- Hand off to `meridian-implementation-assurance` for final provider rollout evidence, docs sync, and AI catalog updates.

## Validation

- Run provider-focused build or tests in the relevant project before broadening.
- For wire-format behavior, verify against official docs or recorded fixtures before creating mock payloads.
- For AI/tooling provider skill changes, run Codex skill and inventory checks.

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

## Output Standards

- State provider type, contracts implemented, and closest existing pattern used.
- Summarize implementation, configuration, DI, resilience, serialization, and tests.
- Name any vendor-doc uncertainty or live-credential limitation without asking for secrets in chat.
