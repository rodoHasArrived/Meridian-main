# Repository Organization Guide

**Version:** 1.0  
**Last Updated:** 2026-02-13  
**Audience:** Developers, Contributors, Maintainers

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
3. **Dependency Direction** — Dependencies flow inward: UI → Application → Domain/Infrastructure → Core → Contracts
4. **No Circular Dependencies** — If project A references B, B cannot reference A
5. **Shared Types in Contracts** — Types shared across multiple projects belong in `Contracts`

### Architectural Layers

```
┌─────────────────────────────────────────────────────┐
│  Presentation Layer (UI Projects)                   │
│  - Meridian.Wpf (Desktop)                │
│  - Meridian.Ui (Web UI)                  │
├─────────────────────────────────────────────────────┤
│  UI Services Layer                                  │
│  - Meridian.Ui.Services (shared)         │
│  - Meridian.Ui.Shared (web components)   │
├─────────────────────────────────────────────────────┤
│  Application Layer                                  │
│  - Meridian.Application                  │
│  - Meridian (entry point)                │
├─────────────────────────────────────────────────────┤
│  Domain & Infrastructure Layer                      │
│  - Meridian.Domain (business logic)      │
│  - Meridian.Infrastructure (providers)   │
│  - Meridian.Storage (persistence)        │
│  - Meridian.FSharp (functional domain)   │
├─────────────────────────────────────────────────────┤
│  Core Layer                                         │
│  - Meridian.Core (shared utilities)      │
│  - Meridian.ProviderSdk (provider API)   │
├─────────────────────────────────────────────────────┤
│  Contracts Layer (bottom)                           │
│  - Meridian.Contracts (DTOs, interfaces) │
└─────────────────────────────────────────────────────┘
```

**Dependency Rule:** Upper layers can reference lower layers, but not vice versa.

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
├── Meridian.Ui.Shared/          # Web-specific UI components
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

Each project can reference projects below it in the layer hierarchy:

| Project | Can Reference |
|---------|---------------|
| **Meridian** | Application, Domain, Infrastructure, Storage, Core, Contracts, FSharp, ProviderSdk, Ui.Shared |
| **Application** | Domain, Infrastructure, Storage, Core, Contracts, FSharp, ProviderSdk |
| **Ui.Services** | Contracts, Core |
| **Ui.Shared** | Application, Ui.Services, Contracts, Core |
| **Wpf** | Ui.Services, Contracts |
| **Domain** | Contracts, Core |
| **Infrastructure** | Domain, Core, Contracts, ProviderSdk, FSharp |
| **Storage** | Domain, Core, Contracts |
| **Core** | Contracts |
| **ProviderSdk** | Contracts |
| **Contracts** | *(No project references)* |

### Forbidden Dependencies

❌ **Never** create these circular dependencies:

1. Contracts → Any other project
2. Core → Application, Infrastructure, Domain
3. Domain → Application, Infrastructure
4. Infrastructure → Application
5. Ui.Services → Application (use Contracts only)

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

```
docs/
├── adr/                    # Architecture Decision Records
│   ├── 001-provider-abstraction.md
│   ├── 002-tiered-storage-architecture.md
│   └── _template.md
├── ai/                     # AI assistant instructions
│   ├── claude/            # Claude-specific guides
│   ├── copilot/           # GitHub Copilot instructions
│   └── ai-known-errors.md
├── architecture/           # Architecture documentation
│   ├── overview.md
│   ├── layer-boundaries.md
│   └── *.puml (diagrams)
+-- archive/                # Project-level historical material root
   +-- docs/               # Historical and superseded docs
   +-- code/               # Reserved for retired code snapshots
├── audits/                 # Code audits and analyses
│   ├── CLEANUP_SUMMARY.md
│   └── DUPLICATE_CODE_ANALYSIS.md
├── development/            # Developer guides
│   ├── provider-implementation.md
│   ├── repository-organization-guide.md (this file)
│   └── wpf-implementation-notes.md
├── diagrams/               # Generated diagrams
│   ├── *.dot
│   ├── *.png
│   └── *.svg
├── evaluations/            # Technology evaluations
│   └── historical-data-providers-evaluation.md
├── generated/              # Auto-generated documentation
│   ├── repository-structure.md
│   └── provider-registry.md
├── getting-started/        # User onboarding
│   └── README.md
├── integrations/           # Integration guides
│   ├── lean-integration.md
│   └── fsharp-integration.md
├── operations/             # Operational guides
│   ├── operator-runbook.md
│   └── portable-data-packager.md
├── providers/              # Provider-specific docs
│   ├── alpaca-setup.md
│   ├── interactive-brokers-setup.md
│   └── provider-comparison.md
├── reference/              # API and data references
│   ├── api-reference.md
│   └── data-dictionary.md
├── status/                 # Project status tracking
│   ├── ROADMAP.md
│   ├── task-markers.md             # auto-generated task marker tracking
│   ├── CHANGELOG.md
│   └── production-status.md
├── uml/                    # UML diagrams
│   └── *.puml
├── DEPENDENCIES.md
├── HELP.md
└── README.md              # Documentation index (to be created)
```

### Documentation Naming Conventions

| Type | Convention | Example |
|------|------------|---------|
| **ADR** | `NNN-descriptive-title.md` | `001-provider-abstraction.md` |
| **Guide** | `{topic}-guide.md` | `deployment-guide.md` |
| **Reference** | `{topic}-reference.md` | `api-reference.md` |
| **Setup/How-to** | `{provider}-setup.md` | `alpaca-setup.md` |
| **Status** | UPPERCASE | `ROADMAP.md`, `CHANGELOG.md` |
| **Analysis** | `{topic}-analysis.md` | `duplicate-code-analysis.md` |

### When to Create New Documentation

| Scenario | Action | Location |
|----------|--------|----------|
| **Architecture decision** | Create ADR | `docs/adr/NNN-title.md` |
| **New provider** | Document setup | `docs/providers/{provider}-setup.md` |
| **New feature** | Update roadmap, add user guide | `docs/status/ROADMAP.md`, `docs/getting-started/` |
| **API changes** | Update reference | `docs/reference/api-reference.md` |
| **Bug fix** | Update changelog | `docs/status/CHANGELOG.md` |
| **Development pattern** | Add to dev guide | `docs/development/{topic}.md` |
| **Operational procedure** | Add to operations | `docs/operations/{procedure}.md` |

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
   docs/providers/{provider}-setup.md
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
   - User guide in `docs/getting-started/`
   - API reference in `docs/reference/`
   - Changelog in `docs/status/CHANGELOG.md`

---

## Enforcement

### Manual Checks

Run these checks before committing:

```bash
# Build all projects (catches reference issues)
dotnet build -c Release

# Run tests (catches broken tests)
dotnet test

# Check for build artifacts in git
git status | grep -E "(bin/|obj/|build-output.log)"

# Scan for open task marker comments
grep -rE "FIXME:|HACK:" src/ tests/
```

### Automated Checks

The following CI workflows enforce organization rules:

- **pr-checks.yml** — Build and test validation
- **code-quality.yml** — Code style and analyzer rules
- **documentation.yml** — Documentation validation
- **validate-workflows.yml** — Workflow syntax validation

### Future Enforcement

Consider adding these tools (not yet implemented):

- **ArchUnitNET** — Enforce dependency rules programmatically
- **Custom analyzer** — Detect naming violations
- **Documentation linter** — Validate doc structure and links

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
  - [Project Roadmap](../status/ROADMAP.md) - Project timeline and phases

- **Implementation Guides:**
  - [Provider Implementation Guide](./provider-implementation.md) - Adding data providers
  - [Desktop Platform Improvements](../evaluations/desktop-platform-improvements-implementation-guide.md) - Desktop development
  - [WPF Implementation Notes](./wpf-implementation-notes.md) - WPF architecture

- **Architecture:**
  - [Architecture Overview](../architecture/overview.md) - System architecture
  - [ADR Index](../adr/README.md) - Architectural decisions
  - [Project Boundaries](../architecture/layer-boundaries.md) - Layer dependencies

- **Contributing:**
  - [Documentation Contribution Guide](./documentation-contribution-guide.md) - Contributing to docs
  - [Central Package Management](./central-package-management.md) - NuGet conventions
