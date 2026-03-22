# Consolidation Refactor Guide

## Overview

This guide documents the UI/API consolidation, provider templating, storage profiles, pipeline policy, configuration service, and interop generation introduced to reduce duplication while preserving existing behavior.

## Unified UI API Layer

* **Shared contracts** live in `Meridian.Contracts.Api` (UI request/response DTOs, routes, client).  
* **Web dashboard** (`Meridian.Ui`) and **UWP** now consume the same DTOs and routes, with a shared `UiApiClient` for status/health checks.
* UI endpoints continue to use the existing status/config endpoints but now share the contracts for compatibility.

## Provider Templates

* `ProviderTemplate` and `ProviderTemplateFactory` normalize provider metadata (capabilities + rate limits) for the registry.
* `ProviderRegistry.GetAllProviders()` now uses standardized templates so UI/monitoring surfaces see consistent capability keys.
* Templates rely on existing interfaces (`IMarketDataClient`, `IHistoricalDataProvider`, `ISymbolSearchProvider`) and keep behavior intact.

## Storage Profiles (Presets)

Storage profiles add optional presets without removing advanced configuration.

| Profile | Focus | Key Defaults |
| --- | --- | --- |
| Research | Balanced analytics | Gzip, manifests, daily date+symbol partition |
| LowLatency | Fast ingest | No compression, hourly partitions |
| Archival | Long-term | Zstd, manifests + checksum, tiering defaults |

Profiles are applied via `StorageProfilePresets.ApplyProfile` and only adjust advanced options unless explicitly overridden.

## Event Pipeline Policy

`EventPipelinePolicy` centralizes bounded-channel configuration (capacity, drop policy, metrics). It is now used by:

* `EventPipeline`
* `StockSharpMarketDataClient` buffering channel
* `ScheduledArchiveMaintenanceService` execution queue

## Unified Deployment Modes

`--mode web|desktop|headless` is added to the core app:

* **web** → web dashboard only (legacy `--ui` behavior)
* **desktop** → data collection + UI server sidecar
* **headless** → collector only (default)

## Configuration Service

`ConfigurationService` unifies:

* Wizard flow
* Auto-config
* Provider detection
* Validation
* Hot reload

Existing commands continue to work and now route through the service.

## F# Interop Generation

* Build-time generation produces C# DTO wrappers in `Meridian.FSharp/Generated`.
* Output is packaged as content for downstream consumers while keeping the existing public API unchanged.

## Staged Migration Plan

1. **Stage 1: UI contracts & client**
   * Adopt `Meridian.Contracts.Api` DTOs and routes in web/UWP.
   * Verify status/config endpoints continue to return the same JSON.
2. **Stage 2: Configuration & pipeline policy**
   * Migrate hot reload and wizard flows to `ConfigurationService`.
   * Use `EventPipelinePolicy` in bounded-channel components.
3. **Stage 3: Storage profiles**
   * Roll out `StorageProfile` presets in config and UI, keeping advanced options intact.
   * Validate archival and tiering defaults match existing infrastructure.
4. **Stage 4: Provider templates**
   * Use `ProviderTemplateFactory` for consistent metadata.
   * Migrate providers incrementally where metadata is surfaced.
5. **Stage 5: F# interop automation**
   * Validate generated DTOs against F# domain types.
   * Keep `Interop.fs` wrappers for backward compatibility.

## Compatibility Notes

* Existing CLI flags (`--ui`, `--wizard`, etc.) remain supported.
* Storage profiles are optional and do not override explicit settings.
* Provider registry output now includes standardized metadata keys.
