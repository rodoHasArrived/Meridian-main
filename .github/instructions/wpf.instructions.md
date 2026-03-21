---
applyTo: "src/Meridian.Wpf/**"
---
# WPF / MVVM Instructions

When editing WPF views (`*.xaml`) or code-behind files (`*.xaml.cs`) in this repository:

1. Keep code-behind thin: only `InitializeComponent()`, constructor DI wiring, and minimal event-handler delegation. No business logic.
2. All state, commands, and orchestration belong in a ViewModel that inherits from `BindableBase` (located at `src/Meridian.Wpf/ViewModels/BindableBase.cs`).
3. Expose data to the View via bindable properties using `SetProperty<T>` — never set UI element properties directly from code-behind (e.g., `MyTextBlock.Text = ...` or `MyLabel.Content = ...`).
4. Replace click handlers that contain logic with `ICommand` properties on the ViewModel bound in XAML.
5. Move `DispatcherTimer` setup and tick logic to the ViewModel; use `DispatcherTimer` or `PeriodicTimer` with marshaling only at the binding layer.
6. Extract nested model classes defined inside a Page or Window to the `Models/` folder or the ViewModel file.
7. Inject services into the ViewModel, not into the Page/Window; Pages should receive a single ViewModel dependency.
8. Never introduce dependencies on `Meridian.Wpf` types inside `Ui.Services` or `Ui.Shared` — that is a reverse dependency violation.
9. Never import `Windows.*` WinRT namespaces or add `#if WINDOWS_UWP` conditional blocks — UWP is deprecated.
10. Cache `FindResource()` brush and style lookups as `static readonly` fields; never call `FindResource()` in per-frame or per-event update paths.
