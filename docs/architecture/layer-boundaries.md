# Layer Boundary Rules

This document defines the allowed dependency directions between layer assemblies.
Project references determine compile-time visibility; architecture tests enforce the
covered dependency rules. A successful build alone does not prove these boundaries.

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
| **Contracts**       | UI or application hosts                    | Pure contract layer                   |

## Enforcement Mechanisms

1. **Compile-time visibility**: Removing a `<ProjectReference>` prevents consumers from
   using types supplied only by that assembly. Adding a forbidden reference can still build;
   the compiler does not know this dependency policy.

2. **Compiled-type checks**: `tests/Meridian.Tests/Architecture/LayerBoundaryTests.cs`
   checks the existing assembly/type rules. Such checks do not detect unused project references.

3. **Evaluated project graph**: `Infrastructure_ShouldNot_Reference_Storage_DirectlyOrTransitively`
   in `LayerBoundaryTests` evaluates MSBuild's actual `ProjectReference` items for Debug and
   Release, including imports, properties and conditions, then traverses the dependency closure.
   Each child inherits the effective reference-specific global properties, including overrides
   and removals, including `_GlobalPropertiesToRemoveFromProjectReferences`. For projects
   importing `PrepareProjectReferences`, inspection runs that target and reads its prepared
   `_MSBuildProjectReferenceExistent` items so negotiated framework/platform metadata is included.
   Distinct declared contexts unmatched to prepared metadata are retained, including same-path references.
   Reference-specific `ToolsVersion` is part of context identity and is forwarded to child MSBuild.
   Unsupported toolsets fail closed, including when the same path also has a valid toolset context.
   Bare projects without that target retain evaluation-only inspection. Neither route restores
   or compiles projects. Residual MSBuild escapes are preserved as literal property values, matching the
   [MSBuild task parser](https://source.dot.net/Microsoft.Build/parent/Shared/PropertyParser.cs.html)
   and the task oracle for `Marker=west%253Beast=1`, and escaped for command-line forwarding. Traversal distinguishes the same project under different contexts,
   with fail-closed budgets of 32 contexts per project and 1,024 overall; exceeding a budget
   reports the project chain and possible non-stabilizing cycle rather than passing the gate.
   A direct edge or any intermediate path from Infrastructure to Storage fails with its chain.
   `ProjectReferenceGraphTests` includes source-free negative fixtures, including an unused
   `ReferenceOutputAssembly="false"` edge, an indirect path, a conditional imported edge and
   a transitive edge enabled by `AdditionalProperties` on its parent reference.
   These tests require the repository checkout and .NET SDK; they do not compile fixture types.

4. **CI gate**: The maintained .NET test lane executes the architecture tests. Run the scoped
   check with `dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --filter
   "FullyQualifiedName~LayerBoundaryTests|FullyQualifiedName~ProjectReferenceGraphTests"`.
   This bounded PRD-108 check covers Infrastructure's Storage dependency. The remaining
   module classifications and forbidden-edge coverage are still open in the
   [implementation backlog](../product/implementation-todo-list.md).

## PRD-108 Infrastructure-to-Storage correction

The pre-correction inventory found nine Infrastructure files using concrete Storage types:

| Infrastructure consumer | Prior Storage dependency | Lower-level seam |
| --- | --- | --- |
| `Adapters/Core/Backfill/BackfillJobManager.cs` | `AtomicFileWriter.WriteAsync` | `Core.IO.IAtomicFileWriter` |
| `Adapters/Core/Backfill/BackfillWorkerService.cs` | `AtomicFileWriter`, `JsonlStoragePolicy`, `StorageOptions` | `ProviderSdk/Backfill/IBackfillBarWriter` |
| `Etl/LocalFileSourceReader.cs`, `Etl/SftpFileSourceReader.cs` | `EtlStagingStore` | `Contracts.Etl.IEtlStagingStore` |
| `Adapters/Alpaca/AlpacaTradeUpdatesClient.cs` | `AtomicFileWriter.Write` for the file cursor | `Core.IO.IAtomicFileWriter` |
| `Adapters/Plaid/FilePlaidConnectionRepository.cs` | Atomic byte writes and line append | `Core.IO.IAtomicFileWriter` |
| `Reconciliation/BrokerStatementInfrastructure.cs`, `Reconciliation/ReconciliationCaseInfrastructure.cs`, `Reconciliation/StatementDurabilityInfrastructure.cs` | Atomic writes, line append and directory sync | `Core.IO.IAtomicFileWriter` |

Only the required persistence interfaces live in lower modules. The provider-facing bar writer
uses the existing ProviderSdk granularity and Contracts historical-bar types. Storage keeps
`AtomicFileWriter` (including its durability and security-metadata handling), its thin injected
adapter, `EtlStagingStore`, and `JsonlBackfillBarWriter` with the existing JSONL policy.
Application/host composition supplies these concrete implementations; Infrastructure has no
Storage project reference, fallback construction, or intermediate project path to Storage.
Existing provider/reconciliation store implementations remain in place and consume the port.

This slice does not close PRD-108: Execution, Backtesting, QuantScript and Strategies dependency
corrections, plus full module-kind classification and project-reference rule coverage, remain open.

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
6. Run the relevant architecture tests as well as the build. Extend their project-reference
   coverage for a new rule; do not assume an ordinary build rejects forbidden edges.

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
**Last Updated:** 2026-10-02
**Audience:** Contributors and AI assistants working on project architecture and dependency management.
