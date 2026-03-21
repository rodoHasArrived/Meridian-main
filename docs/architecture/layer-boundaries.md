# Layer Boundary Rules

This document defines the allowed dependency directions between layer assemblies.
These boundaries are enforced by **project references** (compile-time) and
**Roslyn analyzer rules** (IDE warnings).

## Dependency Graph

```
Meridian.Contracts          (no project dependencies)
       ↑
Meridian.ProviderSdk  →  Contracts
       ↑
Meridian.Domain       →  Contracts, ProviderSdk
       ↑
Meridian.Core         →  Domain, Contracts, ProviderSdk
       ↑                              (cross-cutting: logging, serialization, exceptions)
       ├─────────────────────────┐
       ↑                         ↑
Meridian.Infrastructure   Meridian.Storage
  →  Core, Domain,                     →  Core, Domain,
     Contracts, ProviderSdk               Contracts, ProviderSdk
       ↑                         ↑
       └────────┬─────────────────┘
                ↑
Meridian.Application  →  Infrastructure, Storage, Core,
                                    Domain, Contracts, ProviderSdk
                ↑                         ↑
Meridian (Host/Exe)   →  Application (+ transitive)
                                         ↑
                         ┌───────────────┴────────────────┐
                         ↑                                ↑
          Meridian.Ui.Shared          Meridian.Ui.Services
            →  Application, Contracts              →  Contracts
                         ↑                                ↑
          Meridian.Ui                  Meridian.Wpf
            →  Ui.Shared                            →  Ui.Services, Contracts
                         ↑
          Meridian.McpServer
            →  Application, Core, Storage, Contracts
```

## Forbidden Dependencies

| From Assembly        | Must NOT Reference          | Reason                                    |
|---------------------|-----------------------------|-------------------------------------------|
| **Domain**          | Application, Infrastructure, Storage, Core | Pure business logic, no external deps |
| **Core**            | Application, Infrastructure, Storage       | Shared utilities only                 |
| **Infrastructure**  | Application, Storage                       | No upward deps, no peer deps         |
| **Storage**         | Application, Infrastructure                | No upward deps, no peer deps         |
| **Application**     | (none forbidden)                           | Top-level orchestrator                |
| **Ui.Services**     | Wpf host types                             | Must stay platform-neutral            |
| **Ui.Shared**       | WPF-only APIs, Ui.Services                 | Must stay web-host-agnostic           |
| **Ui / Wpf hosts**  | Each other                                 | No host-to-host references            |
| **McpServer**       | Ui.Shared, Ui.Services, Wpf                | Tool host only; no UI-layer deps      |
| **Contracts**       | UI or application hosts                    | Pure contract layer                   |

## Enforcement Mechanisms

1. **Project References**: Each `.csproj` only lists allowed `<ProjectReference>` entries.
   MSBuild will fail if a type from an unreferenced assembly is used.

2. **Roslyn Analyzer Rules**: `Directory.Build.targets` injects per-project
   `RS0037` (Banned Symbols) rules that flag `using` statements importing
   forbidden namespaces.

3. **CI Gate**: The `pr-checks.yml` workflow runs `dotnet build` which catches
   any project reference violations at compile time.

## Examples

### Allowed: Application using Infrastructure

```csharp
// In Meridian.Application/Services/SomeService.cs
using Meridian.Infrastructure.Providers; // ✅ OK — Application may reference Infrastructure
```

### Forbidden: Infrastructure using Application

```csharp
// In Meridian.Infrastructure/Adapters/SomeProvider.cs
using Meridian.Application.Services; // ❌ COMPILE ERROR — Infrastructure cannot reference Application
```

The compiler enforces this because `Meridian.Infrastructure.csproj` does not have a `<ProjectReference>` to `Meridian.Application`.

### Forbidden: Domain using Core

```csharp
// In Meridian.Domain/Collectors/TradeDataCollector.cs
using Meridian.Core.Logging; // ❌ FORBIDDEN — Domain must remain pure business logic
```

Domain uses only `Contracts` and `ProviderSdk`. If Domain needs a utility from Core, the utility should be moved to Contracts or an interface defined in ProviderSdk.

### Common Pattern: Extracting Shared Types

When Infrastructure and Storage both need the same type, define it in Contracts or Core:

```
❌ Bad: Infrastructure.Providers.SomeSharedModel → Storage cannot see it
✅ Good: Contracts.Domain.Models.SomeSharedModel → Both can reference it
```

## Adding a New Layer Dependency

If a new cross-layer dependency is needed:

1. Check this document to see if it is allowed.
2. If it creates a **circular dependency**, extract the shared type to `Core` or `Contracts`.
3. Update the `.csproj` `<ProjectReference>` entries.
4. Update this document.
5. Verify with `dotnet build -c Release`.
6. The `pr-checks.yml` CI workflow will catch any violations on pull request.

## BannedReferences.txt

The `Meridian.Domain` project includes a `BannedReferences.txt` file that explicitly lists assemblies that Domain must never reference. This provides an additional safety net beyond project references.

## UI Layer Dependency Rules

The four UI-facing projects follow the same dependency direction rules but are isolated from one another:

* **`Meridian.Ui.Shared`** – web endpoint mapping and host wiring; may reference `Application` and `Contracts`. Must not reference `Ui.Services` or `Wpf`.
* **`Meridian.Ui.Services`** – cross-feature shared UI services (API client, fixture data, validation); may reference `Contracts` and lightweight platform-neutral libs. Must not reference WPF or `Ui.Shared`.
* **`Meridian.Ui`** – intentionally thin web host; delegates to `Ui.Shared`. No application logic lives here.
* **`Meridian.Wpf`** – Windows desktop host with XAML views and WPF services; references `Ui.Services` and `Contracts`. Must not reference `Ui.Shared` or `Ui`.

See [Desktop & UI Layer Architecture](desktop-layers.md) for a detailed diagram and communication flow.

---

**Version:** 1.6.2
**Last Updated:** 2026-03-18
**Audience:** Contributors and AI assistants working on project architecture and dependency management.
