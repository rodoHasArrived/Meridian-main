# Repository Organization Guide

**Version:** 1.0
**Last Updated:** 2026-02-13
**Audience:** Developers, Contributors, Maintainers

**Maintenance check (2026-10-05):** documentation placement, source-inventory routing, and validation
entrypoints were updated. The original architecture discussion retains its dated scope; use current
project files and the [source registry](../source/README.md) for the complete module graph.

This guide establishes conventions for organizing code, documentation, and assets in the Meridian repository. Following these patterns ensures consistency, maintainability, and ease of navigation.

---

## Table of Contents

- [Project Structure Principles](#project-structure-principles)
- [Directory Organization](#directory-organization)
- [File Naming Conventions](#file-naming-conventions)
- [Project Boundaries and Dependencies](#project-boundaries-and-dependencies)
- [Code Organization Patterns](#code-organization-patterns)
- [Documentation Organization](#documentation-organization)
- [Test Organization](#test-organization)
- [Asset Management](#asset-management)
- [Common Pitfalls and Solutions](#common-pitfalls-and-solutions)
- [Quick Reference](#quick-reference)

---

## Project Structure Principles

### Core Principles

1. **Clear Boundaries** — Each project has a well-defined responsibility and owns specific types
2. **Minimal Dependencies** — Projects only reference what they absolutely need
3. **Dependency Direction** — Follow the [layer boundary rules](../architecture/layer-boundaries.md) and architecture tests; project references alone do not authorize new coupling
4. **No Circular Dependencies** — If project A references B, B cannot reference A
5. **Shared Types in Contracts** — Types shared across multiple projects belong in `Contracts`

### Architectural Layers

Use the maintained [layer boundary rules](../architecture/layer-boundaries.md) for dependency
direction and [source registry](../source/README.md) for module ownership. Business behavior belongs
in shared application/domain/service layers before browser or WPF presentation consumes it. Keep
both workstation lanes on the shared contract and read-model seams described by
[Engineering](../engineering/README.md#architecture-and-module-boundaries).

---

## Directory Organization

### Root Directory Structure

```
Meridian/
├── .github/           # GitHub-specific files (workflows, actions, templates)
├── benchmarks/        # Performance benchmarking projects
├── build/            # Build tooling and scripts
│   ├── dotnet/       # .NET build tools (doc generators, etc.)
│   ├── node/         # Node.js build scripts
│   ├── python/       # Python build tools
│   └── scripts/      # Shell/PowerShell automation scripts
├── config/           # Configuration templates and samples
├── deploy/           # Deployment configurations
│   ├── docker/       # Docker and docker-compose files
│   ├── monitoring/   # Prometheus, Grafana configs
│   └── systemd/      # Linux service configurations
├── docs/             # All documentation (see Documentation Organization)
├── src/              # Source code projects
├── tests/            # Test projects (mirrors src/ structure)
└── [root files]      # Solution file, README, LICENSE, etc.
```

### Source Projects Organization (`src/`)

The abbreviated project map below explains common roles. The complete maintained inventory and
ownership live in the [source registry](../source/README.md); do not infer that an omitted project
is inactive or that this map authorizes a new dependency.

```
src/
├── Meridian/                    # Main entry point (Program.cs)
├── Meridian.Application/        # Application services, commands, pipeline
├── Meridian.Contracts/          # Shared DTOs, interfaces, domain models
├── Meridian.Core/               # Core utilities, configuration, exceptions
├── Meridian.Domain/             # Domain logic, collectors, events
├── Meridian.FSharp/             # F# domain logic and validation
├── Meridian.Infrastructure/     # Provider implementations
├── Meridian.ProviderSdk/        # Provider abstraction layer
├── Meridian.Storage/            # Storage, archival, export
├── Meridian.Ui/                 # Web UI entry point
├── Meridian.Ui.Services/        # Shared UI services (desktop + web)
├── Meridian.Ui.Shared/          # Shared host endpoints and workstation services
└── Meridian.Wpf/                # WPF desktop application
```

### Standard Project Internal Structure

Each project should organize code into these folders:

```
ProjectName/
├── Commands/         # Command handlers (if applicable)
├── Config/           # Configuration models
├── Contracts/        # Interfaces local to this project
├── Events/           # Event types and publishers
├── Exceptions/       # Project-specific exceptions
├── Extensions/       # Extension methods
├── Models/           # Data models
├── Services/         # Service implementations
├── Utilities/        # Helper classes
└── [FeatureFolders]  # Feature-specific folders (e.g., Backfill/, Monitoring/)
```

**Guideline:** Organize by feature when features are substantial (>5 files), otherwise organize by type.

---

## File Naming Conventions

### General Rules

1. **Use PascalCase** for all C# file names: `MyClass.cs`, `IMyInterface.cs`
2. **One class per file** (with exceptions for small nested types)
3. **File name matches primary type name**: `ConfigurationService.cs` contains `ConfigurationService` class
4. **Interface files**: Prefix with `I` — `IDataSource.cs` contains `IDataSource` interface
5. **Test files**: Match source file name with `Tests` suffix — `ConfigService.cs` → `ConfigServiceTests.cs`

### Specific Conventions

| Type | Convention | Example |
|------|------------|---------|
| **Service** | `{Name}Service.cs` | `ConfigurationService.cs` |
| **Interface** | `I{Name}.cs` | `IStorageSink.cs` |
| **Abstract Base** | `{Name}Base.cs` or `Abstract{Name}.cs` | `WebSocketProviderBase.cs` |
| **DTO** | `{Name}Dto.cs` or in `Models/` | `AppConfigDto.cs` |
| **Exception** | `{Name}Exception.cs` | `ConfigurationException.cs` |
| **Extension** | `{Type}Extensions.cs` | `JsonElementExtensions.cs` |
| **Constants** | `{Domain}Constants.cs` | `PipelinePolicyConstants.cs` |
| **Factory** | `{Name}Factory.cs` | `ProviderFactory.cs` |
| **Test** | `{Name}Tests.cs` | `ConfigServiceTests.cs` |

### Special Cases

- **Endpoints**: `{Feature}Endpoints.cs` (e.g., `ConfigEndpoints.cs`, `BackfillEndpoints.cs`)
- **Models shared across concerns**: Place in `Models/` with descriptive names (e.g., `BackfillDisplayModels.cs`)
- **Large feature groups**: Use folder + descriptive names (e.g., `Monitoring/DataQuality/GapAnalyzer.cs`)

---

## Project Boundaries and Dependencies

### Allowed Dependencies

The [layer boundary rules](../architecture/layer-boundaries.md) and covered architecture tests own
the allowed directions. Inspect the affected `.csproj`/`.fsproj` and nearest source README for the
actual direct references before adding one; this supporting guide does not maintain a second
allowlist. For example, the shared UI projects contain more dependencies than their names alone
suggest, and they must not be treated as interchangeable layers.

### Forbidden Dependencies

Never introduce a circular project reference or make shared contracts depend on a consuming
application/UI project. Check a proposed dependency against the current layer rules and tests;
a folder name or the historical map in an old guide is not an exception to those boundaries.

### Type Ownership Rules

| Type Category | Belongs In | Reasoning |
|---------------|------------|-----------|
| **DTOs shared across layers** | `Contracts` | Prevents circular dependencies |
| **Domain models** | `Contracts/Domain/Models/` | Shared by Domain, Infrastructure, Application |
| **Service interfaces (cross-layer)** | `Contracts` or `ProviderSdk` | Abstractions shared by multiple layers |
| **Service implementations** | Appropriate layer project | Business logic stays in Domain, infrastructure concerns in Infrastructure |
| **Configuration models** | `Core/Config/` | Used everywhere, no dependencies |
| **Exceptions** | `Core/Exceptions/` | Used everywhere |
| **Utilities** | `Core/Utilities/` | Shared helpers |
| **Provider abstractions** | `ProviderSdk` | Plugin API for providers |
| **UI-specific interfaces** | `Ui.Services/Contracts/` | Desktop/web UI contracts |

---

## Code Organization Patterns

### Service Organization

**Pattern 1: Small services (1-3 services in domain)**
```
ProjectName/
└── Services/
    ├── ConfigService.cs
    ├── ValidationService.cs
    └── IValidationService.cs
```

**Pattern 2: Large service domains (5+ services)**
```
ProjectName/
└── ServiceDomain/
    ├── Core/
    │   ├── ServiceOrchestrator.cs
    │   └── IServiceOrchestrator.cs
    ├── Validators/
    │   ├── SchemaValidator.cs
    │   └── DataValidator.cs
    └── Utilities/
        └── ServiceHelpers.cs
```

### Provider Organization

All provider implementations follow this structure:

```
Infrastructure/Adapters/
├── Core/                           # Shared provider infrastructure
│   ├── ProviderBase.cs
│   └── ProviderHelpers.cs
├── Streaming/                      # Real-time providers
│   ├── Alpaca/
│   │   ├── AlpacaMarketDataClient.cs
│   │   ├── AlpacaMessageHandler.cs
│   │   └── AlpacaOptions.cs
│   └── Polygon/
├── Historical/                     # Backfill providers
│   ├── Alpaca/
│   │   └── AlpacaHistoricalDataProvider.cs
│   └── Stooq/
├── SymbolSearch/                   # Symbol search providers
│   └── Alpaca/
└── Backfill/                       # Backfill orchestration
    └── CompositeHistoricalDataProvider.cs
```

**Naming Rules:**
- Streaming: `{Provider}MarketDataClient.cs` (implements `IMarketDataClient`)
- Historical: `{Provider}HistoricalDataProvider.cs` (implements `IHistoricalDataProvider`)
- Symbol Search: `{Provider}SymbolSearchProvider.cs` (implements `ISymbolSearchProvider`)

### Endpoint Organization

HTTP endpoints are organized by domain:

```
Application/Http/Endpoints/
├── BackfillEndpoints.cs
├── ConfigEndpoints.cs
├── ProviderEndpoints.cs
├── StatusEndpoints.cs
└── QualityDropsEndpoints.cs
```

**Pattern:**
```csharp
public static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/config").WithTags("Configuration");

        group.MapGet("/", GetConfig)
             .WithName("GetConfiguration")
             .Produces<AppConfigDto>();
        // ... more endpoints
    }
}
```

---

## Documentation Organization

### Documentation Structure

Use the [Documentation Ownership Contract](../documentation-ownership.md) and
[Documentation Contribution Guide](documentation-contribution-guide.md) for placement and lifecycle
rules. Start at [the docs front door](../README.md); this guide does not maintain a second folder tree.

| Material | Home |
| --- | --- |
| First-run instructions | `docs/start/` |
| Contributor entrypoints and shortest current command paths | `docs/engineering/` |
| Detailed supporting implementation/tooling guides | `docs/development/`, linked from Engineering |
| Product narrative and prioritization interpretation | `docs/product/` |
| Operator procedures | `docs/operators/` |
| API, configuration, and capability lookup | `docs/reference/` |
| Architecture, domain vocabulary, decisions | `docs/architecture/`, `docs/domain/`, `docs/adr/` |
| Roadmap/source truth | `docs/roadmap/data/`, `docs/source/data/` |
| Generated views and reports | Their existing registered output paths; update inputs/generators |
| Superseded or historical records | `archive/docs/`, with a replacement link or archive rationale |

Do not recreate retired `docs/audits/` or `docs/uml/` lanes. Use the owning audience path and
`docs/diagrams/` for current material. Repository archives are outside `docs/`.

### Documentation Naming Conventions

| Type | Convention | Example |
|------|------------|---------|
| **ADR** | `NNN-descriptive-title.md` | `001-provider-abstraction.md` |
| **Guide** | `{topic}-guide.md` | `deployment-guide.md` |
| **Reference** | `{topic}-reference.md` | `api-reference.md` |
| **Setup/How-to** | `{provider}-setup.md` | `alpaca-setup.md` |
| **Generated status** | Preserve generator-owned names | `docs/roadmap/generated/ROADMAP_SUMMARY.md` |
| **Analysis** | `{topic}-analysis.md` | `duplicate-code-analysis.md` |

### When to Create New Documentation

| Scenario | Action | Location |
|----------|--------|----------|
| **Architecture decision** | Create ADR | `docs/adr/NNN-title.md` |
| **New provider** | Document setup and capability lookup | `docs/operators/` and `docs/reference/` |
| **New feature** | Update registry/status and canonical lane | `docs/roadmap/data/*.yml`, generated roadmap views, and `docs/product/` or `docs/engineering/` as needed |
| **API changes** | Update reference | `docs/reference/` |
| **Bug fix** | Update affected behavior guidance and PR evidence; leave generated changelog output to its generator | Owning audience lane |
| **Development pattern** | Add a supporting guide and link it from Engineering | `docs/development/` |
| **Operational procedure** | Add to operator lane | `docs/operators/` |

---

## Test Organization

### Test Project Structure

Test projects mirror source project structure:

```
tests/
├── Meridian.Tests/              # Cross-platform startup/composition/contracts/core tests
│   ├── Application/
│   │   ├── Commands/
│   │   ├── Config/
│   │   └── Services/
│   ├── Domain/
│   ├── Infrastructure/
│   └── Storage/
├── Meridian.FSharp.Tests/       # F# tests
├── Meridian.Ui.Tests/           # Shared UI service tests
├── Meridian.Wpf.Tests/          # WPF-only binding/navigation/host-wiring tests
└── coverlet.runsettings
```

### Test File Naming

**Rule:** Test file mirrors source file with `Tests` suffix

```
Source:  src/ProjectName/Services/ConfigService.cs
Test:    tests/ProjectName.Tests/Services/ConfigServiceTests.cs
```

### Test Organization Patterns

**Option 1: Flat structure (simple services)**
```csharp
public class ConfigServiceTests
{
    [Fact]
    public void LoadConfig_ValidFile_ReturnsConfig() { }

    [Fact]
    public void LoadConfig_InvalidFile_ThrowsException() { }
}
```

**Option 2: Nested classes (complex services)**
```csharp
public class ConfigServiceTests
{
    public class LoadConfig
    {
        [Fact]
        public void ValidFile_ReturnsConfig() { }

        [Fact]
        public void InvalidFile_ThrowsException() { }
    }

    public class SaveConfig
    {
        [Fact]
        public void ValidConfig_SavesSuccessfully() { }
    }
}
```

---

## Asset Management

### Static Assets

```
src/ProjectName/
└── wwwroot/                    # Web assets (for web projects)
    ├── static/
    │   ├── css/
    │   ├── js/
    │   └── images/
    └── templates/              # HTML templates
```

### Desktop Assets

```
src/Meridian.Wpf/
├── Assets/
│   ├── Icons/                  # Icon files
│   ├── Images/                 # Image resources
│   └── Source/                 # Source files for generated assets
└── Styles/
    ├── AppStyles.xaml
    └── IconResources.xaml
```

### Build Artifacts

**Never commit these to git:**
- `bin/`
- `obj/`
- `*.user`
- `build-output.log`
- Node modules (`node_modules/`)
- Python virtual environments (`.venv/`, `venv/`)

**Ensure `.gitignore` covers all build artifacts.**

---

## Common Pitfalls and Solutions

### Pitfall 1: Duplicate Interface Definitions

❌ **Problem:** Same interface defined in multiple projects
```
Wpf/Services/IConfigService.cs
Ui.Services/Contracts/IConfigService.cs
```

✅ **Solution:** Keep one canonical definition in shared location
```
Ui.Services/Contracts/IConfigService.cs  (✓ canonical)
Delete from Wpf, update using directives
```

### Pitfall 2: Ambiguous Class Names

❌ **Problem:** Same class name in different namespaces
```
Application.Http.ConfigStore
Ui.Shared.Services.ConfigStore
```

✅ **Solution:** Use distinct, role-specific names
```
Application.Http.ConfigStore              → InMemoryConfigStore
Ui.Shared.Services.ConfigStore            → UiConfigStore
```

### Pitfall 3: Wrong Project References

❌ **Problem:** Core references Application (circular dependency)
```csharp
// In Core/Utilities/Helper.cs
using Meridian.Application.Services; // ❌ Forbidden!
```

✅ **Solution:** Move shared types to Contracts
```csharp
// In Contracts/Utilities/Helper.cs
// No application layer dependencies
```

### Pitfall 4: Mixed Concerns in Single File

❌ **Problem:** 3,000-line file with multiple responsibilities
```csharp
// UiServer.cs contains:
// - Server configuration
// - All HTTP endpoints
// - HTML rendering
// - Authentication logic
```

✅ **Solution:** Split by concern
```
UiServer.cs                  → Server configuration only
Endpoints/ConfigEndpoints.cs → Config API
Endpoints/StatusEndpoints.cs → Status API
Rendering/HtmlRenderer.cs    → HTML generation
Auth/ApiKeyMiddleware.cs     → Authentication
```

### Pitfall 5: Test-Source Structure Mismatch

❌ **Problem:** Tests not organized to mirror source
```
Source: src/Application/Services/ConfigService.cs
Test:   tests/Meridian.Tests/ConfigTests.cs  ❌
```

✅ **Solution:** Mirror source structure exactly
```
Source: src/Application/Services/ConfigService.cs
Test:   tests/Meridian.Tests/Application/Services/ConfigServiceTests.cs  ✓
```

---

## Quick Reference

### New Code Checklist

When adding new code, ask these questions:

- [ ] Does the file name match the primary type name?
- [ ] Is the file in the correct project (respecting layer boundaries)?
- [ ] Does the project reference list match dependency rules?
- [ ] Are shared types in `Contracts` rather than duplicated?
- [ ] Is there a matching test file in the test project?
- [ ] Does the test file location mirror the source file?
- [ ] Are namespace and folder structure aligned?
- [ ] Is documentation updated if adding public API?
- [ ] Are there no circular dependencies introduced?

### Where Should This Code Go?

| Code Type | Location | Project |
|-----------|----------|---------|
| Shared DTO | `Contracts/` | `Meridian.Contracts` |
| Domain model | `Contracts/Domain/Models/` | `Meridian.Contracts` |
| Business logic | `Domain/` | `Meridian.Domain` |
| Provider implementation | `Infrastructure/Adapters/` | `Meridian.Infrastructure` |
| HTTP API endpoint | `Application/Http/Endpoints/` | `Meridian.Application` |
| UI service (desktop+web) | `Ui.Services/Services/` | `Meridian.Ui.Services` |
| Platform-specific UI logic | `Wpf/` | `Meridian.Wpf` |
| Configuration model | `Core/Config/` | `Meridian.Core` |
| Shared utility | `Core/Utilities/` | `Meridian.Core` |
| Custom exception | `Core/Exceptions/` | `Meridian.Core` |
| Provider abstraction | `ProviderSdk/` | `Meridian.ProviderSdk` |

### Adding a New Provider

Follow this structure:

1. **Create provider folder:**
   ```
   Infrastructure/Adapters/{Category}/{ProviderName}/
   ```

2. **Implement required interface:**
   - Streaming: `IMarketDataClient`
   - Historical: `IHistoricalDataProvider`
   - Symbol Search: `ISymbolSearchProvider`

3. **Add `[DataSource]` attribute:**
   ```csharp
   [DataSource("provider-name")]
   public class ProviderMarketDataClient : IMarketDataClient
   ```

4. **Create configuration class:**
   ```
   Core/Config/{Provider}Options.cs
   ```

5. **Add tests:**
   ```
   tests/Meridian.Tests/Infrastructure/Adapters/{Provider}Tests.cs
   ```

6. **Document setup:**
   ```
   docs/operators/provider-onboarding-{provider}.md
   ```

### Adding a New Feature

1. **Design the feature** — Write ADR if architectural
2. **Update roadmap** — Add to appropriate phase
3. **Implement in layers:**
   - DTOs in `Contracts`
   - Business logic in `Domain` or `Application`
   - HTTP API in `Application/Http/Endpoints`
   - UI in appropriate UI project
4. **Add tests at each layer**
5. **Update documentation:**
   - User guide in `docs/start/` or `docs/operators/`
   - API reference in `docs/reference/`
   - Roadmap/status evidence in `docs/roadmap/`, `docs/product/`, or generated status output

---

## Enforcement

### Manual Checks

Run from the repository root, using the prerequisites in [Engineering](../engineering/README.md#buildtestrun):

```bash
# Review whitespace and the files being committed
git diff --check
git status --short

# Validate documentation placement and navigation
python build/scripts/docs/validate-docs-structure.py --summary
python build/scripts/docs/repair-links.py --summary

# Canonical pre-PR gate, after the focused checks for the touched source area
bash scripts/ci.sh
```

### Automated Checks

[CI/CD ownership](../engineering/ci-cd-optimization.md) identifies the current gate owners.
`Meridian CI` runs the canonical .NET, browser, docs, and workflow lanes; Windows desktop validation
provides the platform-specific proof. See the [workflow guide](../../.github/workflows/README.md)
for triggers and artifacts instead of duplicating its workflow catalog here.

Documentation structure and internal-link validators already exist; extend their checks through the
[documentation tooling guide](documentation-automation.md) rather than creating a competing validator.

### Future Enforcement

Potential additional tooling (evaluate against the current test/analyzer inventory before adopting):

- **ArchUnitNET** — Enforce dependency rules programmatically
- **Custom analyzer** — Detect naming violations

---

## Getting Help

If you're unsure where code or documentation should go:

1. **Check this guide** — Most questions are answered here
2. **Look for similar code** — Find existing patterns and follow them
3. **Review recent PRs** — See how others have organized similar changes
4. **Ask in discussions** — Open a GitHub discussion for guidance
5. **Consult CLAUDE.md** — AI assistants have repository context

---

## Contributing to This Guide

This guide should evolve as the repository grows. To suggest improvements:

1. Open a GitHub issue with the `documentation` label
2. Describe the ambiguity or gap in guidance
3. Propose a solution or ask for community input
4. Submit a PR updating this guide once consensus is reached

---

*This guide is maintained by the core team and updated with each significant repository reorganization.*

---

## Related Documentation

- **Planning and Cleanup:**
  - [Repository Cleanup Action Plan](https://github.com/rodoHasArrived/Meridian/blob/main/archive/docs/plans/repository-cleanup-action-plan.md) - Technical debt reduction plan (completed)
  - [Refactor Map](./refactor-map.md) - Safe refactoring procedures
  - [Project Roadmap](../roadmap/README.md) - Project timeline and phases

- **Implementation Guides:**
  - [Provider Implementation Guide](./provider-implementation.md) - Adding data providers
  - [Desktop Platform Improvements archive](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/assessments/desktop-platform-improvements-implementation-guide.md) - Historical desktop development assessment; use current engineering/operator docs for active work
  - [WPF Implementation Notes](./wpf-implementation-notes.md) - WPF architecture

- **Architecture:**
  - [Architecture Overview](../architecture/overview.md) - System architecture
  - [ADR Index](../adr/README.md) - Architectural decisions
  - [Project Boundaries](../architecture/layer-boundaries.md) - Layer dependencies

- **Contributing:**
  - [Documentation Contribution Guide](./documentation-contribution-guide.md) - Contributing to docs
  - [Central Package Management](./central-package-management.md) - NuGet conventions

