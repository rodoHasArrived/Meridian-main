# WPF Shell MVVM

## Purpose

This note defines the MVVM boundary for the Meridian desktop shell so workstation state, navigation, and operator actions stay testable and do not drift back into WPF code-behind.

## Shell responsibilities

### `MainWindowViewModel`

- Owns shell-scoped commands such as collector control, shell navigation shortcuts, palette actions, and launch-argument handling.
- Owns transient banner state for fixture mode and clipboard symbol detection.
- Owns status-bar orchestration and notification-triggering behavior that is not tied to a specific visual-tree event.

### `MainPageViewModel`

- Owns workstation workspace selection and page metadata.
- Owns command-palette query, selection, visibility, and filtered results.
- Owns recent-page history presentation and back/refresh navigation command state.
- Reacts to `INavigationService` and `FixtureModeDetector` so the shell view remains a projection of shared service state.

## Code-behind rule

`MainWindow.xaml.cs` and `Views/MainPage.xaml.cs` may handle only WPF-specific mechanics that are difficult or noisy to represent in a view model:

- `Window`, `Frame`, and dispatcher lifecycle hooks
- focus and text-selection behavior
- drag/drop plumbing
- visual-tree event forwarding
- interop hooks such as HWND registration or clipboard watcher attachment

They should not become the source of truth for navigation state, command availability, banner content, or workspace/page selection.

## Dependency expectations

- Shell view models may depend on `Meridian.Wpf.Services`, `Meridian.Ui.Services`, and contract abstractions.
- Shell views bind to view models and forward only UI-specific events.
- Shared operator workflows should prefer service and view-model orchestration over direct code-behind mutation.

## Verification notes

- Build validation for this boundary should include a WPF desktop build.
- Regression coverage should favor view-model tests for shell state and targeted UI smoke tests for navigation wiring.
