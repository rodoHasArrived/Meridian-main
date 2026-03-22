# Path-Specific Copilot Instructions

This directory is the navigation index for **path-specific instruction files** used by GitHub Copilot.
The actual files live in `.github/instructions/` where Copilot can discover and auto-apply them.

---

## How Path Instructions Work

GitHub Copilot automatically loads instruction files whose `applyTo` glob pattern matches the file
being edited. This means the rules below are applied **without the user needing to reference them
explicitly**.

---

## Available Instruction Files

### C# Source Instructions

**File:** [`.github/instructions/csharp.instructions.md`](../../../.github/instructions/csharp.instructions.md)
**Applies to:** `src/**/*.cs`

10 rules enforced when editing C# source files:

1. Mark classes `sealed` unless designed for inheritance
2. `CancellationToken ct` as last parameter on every async method
3. Structured logging — never string interpolation
4. `IOptionsMonitor<T>` for runtime-changeable settings; `IOptions<T>` for static only
5. All JSON serialization via ADR-014 source generators — never reflection overload
6. `EventPipelinePolicy.Default.CreateChannel<T>()` for producer-consumer queues
7. `Span<T>` / `Memory<T>` for hot-path buffer operations — avoid LINQ allocations per tick
8. Domain exceptions must derive from `MeridianException`
9. `_` prefix for private fields; `I` prefix for interfaces; `Async` suffix for async methods
10. Register all new serializable DTOs in the project's `JsonSerializerContext`

---

### WPF / MVVM Instructions

**File:** [`.github/instructions/wpf.instructions.md`](../../../.github/instructions/wpf.instructions.md)
**Applies to:** `src/Meridian.Wpf/**`

10 rules enforced when editing WPF views or code-behind:

1. Code-behind: only `InitializeComponent()`, DI wiring, minimal event delegation
2. All state and commands in a ViewModel inheriting `BindableBase`
3. Expose data via `SetProperty<T>` — never set UI element properties directly from code-behind
4. Replace click handlers with `ICommand` properties bound in XAML
5. `DispatcherTimer` setup and tick logic belong in the ViewModel
6. Extract nested model classes to `Models/` or the ViewModel file
7. Inject services into the ViewModel, not the Page/Window
8. No reverse dependencies: `Ui.Services` / `Ui.Shared` must not reference `Wpf` types
9. No `Windows.*` WinRT namespaces or `#if WINDOWS_UWP` blocks — UWP is deprecated
10. Cache `FindResource()` lookups as `static readonly` fields

---

### .NET Test Instructions

**File:** [`.github/instructions/dotnet-tests.instructions.md`](../../../.github/instructions/dotnet-tests.instructions.md)
**Applies to:** `tests/**/*.cs`

6 rules enforced when editing C# test files:

1. Keep tests deterministic — no time/network/external dependency flakiness
2. Prefer clear Arrange-Act-Assert structure
3. Use existing test utilities and fixtures before introducing new helpers
4. Add or update assertions to capture the reported regression path
5. Ensure naming communicates behavior (`method + condition + expectation`)
6. Run at least the nearest test project and report the exact command used

---

### Documentation Instructions

**File:** [`.github/instructions/docs.instructions.md`](../../../.github/instructions/docs.instructions.md)
**Applies to:** `**/*.md`

5 rules enforced when editing Markdown documentation:

1. Favor task-oriented language and concrete commands
2. Keep sections scannable with short headings and bullets
3. Ensure command examples are copy/paste ready
4. Link related docs when introducing new workflow guidance
5. Avoid duplicating long reference content unless needed for discoverability

---

## Adding New Instructions

1. Create a new `.instructions.md` file in `.github/instructions/`
2. Add the `applyTo:` frontmatter glob pattern
3. Add an entry to this README
4. Update [`docs/ai/README.md`](../../archive/docs/README.md) Tier 4 table

---

## Related Resources

| Resource | Purpose |
|----------|---------|
| [`.github/copilot-instructions.md`](../../../.github/copilot-instructions.md) | Repository-wide Copilot instructions |
| [`docs/ai/copilot/instructions.md`](../copilot/instructions.md) | Extended Copilot guidance |
| [`docs/ai/agents/README.md`](../agents/README.md) | Agent definitions (Copilot + Claude) |
| [`docs/ai/README.md`](../../archive/docs/README.md) | Master AI resource index |

---

*Last Updated: 2026-03-16*
