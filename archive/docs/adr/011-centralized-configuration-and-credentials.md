# ADR-011: Centralized Configuration and Credential Management

**Status:** Accepted
**Date:** 2026-02-02
**Deciders:** Core Team

## Context

The system runs across multiple host types (console, web, desktop) and integrates with numerous providers. Configuration and credentials are needed by:

- Provider clients (API keys, OAuth tokens, endpoints)
- Storage and archival pipelines (paths, compression settings)
- Monitoring and operational scheduling (thresholds, timeouts)
- UI and automation workflows (runtime overrides)

Historically, configuration was fetched from a mix of environment variables, appsettings files, and per-service options. Credentials were resolved via ad-hoc environment lookups or provider-specific helper classes. This caused:

- Inconsistent validation rules and missing defaults
- Difficult auditing of required settings
- Divergent host behavior when configuration paths changed
- Duplicate logic for environment overrides and credential caching

## Decision

Adopt a centralized configuration and credential management model:

1. **Unified configuration access** via `IConfigurationProvider` for all components.
2. **Configuration pipeline** (`ConfigurationPipeline`) with seven deterministic stages: load, overlay, environment override, credential resolution, self-healing, validation, and return.
3. **Central credential interface** (`ICredentialStore`) to handle provider secrets, caching, refresh, and metadata registration.
4. **Single configuration store** (`ConfigStore`) for load/save of persisted settings shared by all hosts.
5. **Environment variable overrides** (`ConfigEnvironmentOverride`) with the `MDC_` prefix convention and 96 recognized mappings.
6. **Composition-root registration** so every host receives the same configuration/credential stack.

### Configuration Pipeline Stages

```
1. Load base configuration (appsettings.json or in-memory)
2. Apply environment-specific overlay (e.g., appsettings.Production.json)
3. Apply environment variable overrides (MDC_* prefix)
4. Resolve credentials from all sources (env vars, secure store, cache)
5. Apply self-healing fixes (optional: swap date ranges, fix depth levels)
6. Validate the final configuration
7. Return ValidatedConfig with full metadata
```

### Environment Variable Convention

All overrides use a `MDC_` prefix mapped to configuration paths:

```bash
# Core settings
MDC_DATA_ROOT=/data/market        # Storage root directory
MDC_DATASOURCE=alpaca             # Active data source
MDC_COMPRESS=true                 # Enable compression

# Provider credentials
MDC_ALPACA_KEYID=...              # Alpaca API key
MDC_ALPACA_SECRETKEY=...          # Alpaca secret
POLYGON_API_KEY=...               # Polygon API key (legacy format)
TIINGO_API_TOKEN=...              # Tiingo token (legacy format)

# Storage overrides
MDC_STORAGE_NAMINGCONVENTION=BySymbol
MDC_STORAGE_RETENTIONDAYS=30
MDC_STORAGE_MAXMB=10240
```

Legacy provider-specific variables (e.g., `ALPACA_KEY_ID`) are supported alongside the `MDC_` prefix for backward compatibility.

## Implementation Links

<!-- These links are verified by the build process -->

| Component | Location | Purpose |
|-----------|----------|---------|
| Configuration contract | `src/Meridian.Core/Config/IConfigurationProvider.cs` | Typed, validated configuration access |
| Configuration pipeline | `src/Meridian.Application/Config/ConfigurationPipeline.cs` | Seven-stage configuration processing |
| Configuration store | `src/Meridian.Application/Http/ConfigStore.cs` | Shared load/save for all hosts |
| Credential contract | `src/Meridian.Application/Credentials/ICredentialStore.cs` | Unified credential resolution and caching |
| Environment overrides | `src/Meridian.Application/Services/ConfigEnvironmentOverride.cs` | 96 environment variable mappings |
| Config validation | `src/Meridian.Application/Config/ConfigValidationHelper.cs` | Validation rules and self-healing |
| Composition registration | `src/Meridian.Application/Composition/ServiceCompositionRoot.cs` | Consistent wiring across hosts |
| Config tests | `tests/Meridian.Tests/Application/Config/` | Pipeline and validation tests |

## Rationale

### Unified Configuration Pipeline

A deterministic seven-stage pipeline ensures every host processes configuration identically:

```csharp
// Single entry point for all configuration sources
var validated = pipeline.LoadFromFile("appsettings.json");
// or: pipeline.FromWizardResult(wizardResult);
// or: pipeline.FromAutoConfigResult(autoConfigResult);
// or: pipeline.FromHotReload(config, path, onChanged);
```

Self-healing fixes prevent common misconfiguration (swapped date ranges, invalid naming conventions, missing default symbols) without requiring manual intervention.

### Credential Lifecycle Management

`ICredentialStore` provides:

- **Retrieval with source tracking** - Know whether a credential came from an environment variable, config file, secure store, or cache.
- **Caching** - Credentials are cached after first resolution. `ClearCache()` forces re-resolution.
- **Refresh** - OAuth tokens and expiring credentials can be refreshed via `RefreshCredentialAsync()`.
- **Metadata registration** - Providers declare required credentials at startup, enabling validation before connection attempts.
- **Provider-level validation** - `ValidateProviderCredentialsAsync()` checks all credentials for a given provider in one call.

```csharp
// Provider declares credentials at startup
store.RegisterApiKey("alpaca", "ALPACA__KEYID", isRequired: true);
store.RegisterKeySecretPair("alpaca", "ALPACA__KEYID", "ALPACA__SECRETKEY");

// Runtime retrieval with source tracking
var result = await store.GetApiKeyAsync("alpaca", ct);
// result.Source: EnvironmentVariable, Configuration, SecureStore, or Cache
// result.IsExpired: false (for API keys), may be true for OAuth tokens
```

### Consistency Across Hosts

All hosts (console, web, desktop) use `ServiceCompositionRoot` to register the same configuration stack. This eliminates divergent behavior when adding new settings.

### Observability

`IConfigurationProvider.GetMetadata()` returns metadata for every registered setting, including its source (file, environment, runtime, command-line) and validation status. This enables dashboard display and diagnostic reporting.

## Alternatives Considered

### Alternative 1: Host-specific configuration services

Each host (console/web/desktop) owns its own configuration and credential logic.

**Pros:**
- Faster initial implementation
- Host-specific customization

**Cons:**
- Divergent behavior and duplicate logic
- Harder to validate or document configuration requirements
- Each host needs its own migration path when settings change

**Why rejected:** Inconsistent behavior across hosts is more costly than centralized setup.

### Alternative 2: Provider-specific credential helpers

Each provider implements its own credential resolution logic.

**Pros:**
- Tailored per provider
- Minimal shared abstractions

**Cons:**
- No caching strategy consistency
- Difficult to implement cross-cutting validation and reporting
- Credential audit requires inspecting each provider individually

**Why rejected:** Centralized credential management reduces duplication and supports unified validation.

### Alternative 3: ASP.NET Configuration only

Rely solely on `Microsoft.Extensions.Configuration` without custom abstractions.

**Pros:**
- No custom code
- Standard patterns

**Cons:**
- No self-healing or validation pipeline
- No credential caching or refresh
- No metadata registration for audit

**Why rejected:** Standard configuration lacks the validation, self-healing, and credential lifecycle features required by this system.

## Consequences

### Positive

- Configuration and credentials are validated and discoverable before any provider connects.
- Hosts can safely override settings at runtime via environment variables or hot-reload.
- Provider onboarding includes documented credential metadata, enabling automated validation.
- Self-healing fixes prevent common misconfigurations without manual intervention.

### Negative

- Added abstraction layer to maintain.
- Composition root changes require coordination when extending configuration sources.
- Self-healing can mask misconfigurations if not logged clearly.

### Neutral

- Legacy configuration access may remain during gradual migration.
- Environment variable precedence (MDC_ prefix > legacy > appsettings) must be documented.

## Compliance

### Code Contracts

Components accessing configuration or credentials must use the centralized interfaces:

```csharp
public interface IConfigurationProvider
{
    T Get<T>(string section, string key, T defaultValue = default!);
    T GetSection<T>(string section) where T : class, new();
    bool TryGet<T>(string section, string key, out T? value);
    void Set<T>(string section, string key, T value);
    ConfigurationSource GetSource(string section, string key);
    IReadOnlyList<ConfigurationMetadata> GetMetadata();
    ConfigurationValidationResult Validate();
    ConfigurationValidationResult ValidateSection(string section);
    void Reload();
    event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;
}

public interface ICredentialStore
{
    Task<CredentialResult> GetCredentialAsync(string provider, string key, CancellationToken ct = default);
    Task SetCredentialAsync(string provider, string key, string value, CancellationToken ct = default);
    Task<bool> HasValidCredentialAsync(string provider, string key, CancellationToken ct = default);
    Task<CredentialResult> RefreshCredentialAsync(string provider, string key, CancellationToken ct = default);
    Task<CredentialValidationResult> ValidateProviderCredentialsAsync(string provider, CancellationToken ct = default);
    IReadOnlyList<CredentialMetadata> GetRegisteredCredentials();
    void RegisterCredential(CredentialMetadata metadata);
    void ClearCache(string? provider = null);
}
```

### Runtime Verification

- `[ImplementsAdr("ADR-001")]` on `IConfigurationProvider`, `ICredentialStore`, and `ConfigStore`
- Build-time verification via `make verify-adrs`
- Integration tests verify pipeline stages execute in order
- Environment override tests validate all 96 mappings

## References

- [Configuration Guide](../HELP.md#configuration)
- [Project Context](../generated/project-context.md)
- [ADR-001: Provider Abstraction](001-provider-abstraction.md) - Configuration feeds provider initialization
- [ADR-005: Attribute-Based Discovery](005-attribute-based-discovery.md) - Providers declare metadata including credential requirements
- [ADR-012: Monitoring & Alerting](012-monitoring-and-alerting-pipeline.md) - Configuration validation results feed health checks

---

*Last Updated: 2026-02-20*
