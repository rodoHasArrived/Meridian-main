# MVVM Guidelines

Meridian keeps workflow state in view models and shared read models so the browser
workstation and retained WPF compatibility surfaces can render the same business
posture without forking behavior.

## Current UI Direction

- New operator UI work belongs in `src/Meridian.Ui/dashboard/`.
- Shared workstation endpoint and read-model support belongs in
  `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/`.
- `src/Meridian.Wpf/` product/UI work is deferred until explicitly reactivated; use this guidance only for retained WPF compatibility, validation, and maintenance.

## View-Model Ownership

Keep these in view models, shared read models, or endpoint projections:

- visible workflow state and selected-row state
- status labels, disabled reasons, banners, and empty-state copy
- command availability and recovery action routing
- accessible names, live-region text, and keyboard-selection semantics
- route hints and subject or symbol handoffs

### Anti-monolith rule

- Do not add new "god" page view models. Treat ~1,000+ line page view models as decomposition candidates.
- Page view models should coordinate section view models and command wiring; heavy filtering, projection shaping, and API orchestration belong in dedicated services/query classes.
- Preserve existing XAML bindings during decomposition with temporary adapter properties, then remove adapters once views are migrated.

Views should render state, invoke commands, and handle local interaction glue.
They should not recalculate business posture or invent labels that should be
shared across surfaces.

## Browser Workstation

- Prefer reusable view-model modules and shared dashboard primitives before
  adding screen-local state.
- Keep fixture/no-host data typed and narrow. It should support bootstrap and
  empty-state development, not replace real command and mutation workflows.
- Keep route state explicit. Use links such as `/settings#alpaca-provider-setup`
  when a workflow has a known repair target.
- Keep accessibility part of correctness: selectable dense rows, detail panels,
  command buttons, loading states, and error states need stable names and states.

## WPF Desktop Workstation

- Keep code-behind thin and focused on view lifecycle, binding setup, and WPF
  interop that cannot live elsewhere.
- Preserve existing view-model tests when changing shell routing, workspace
  selection, command availability, or page binding coverage.
- Do not copy browser-only behavior into WPF as a separate product lane. Use
  shared contracts and read models when desktop compatibility work is required.

## Validation

Use the narrowest test that covers the surface:

```powershell
npm --prefix src/Meridian.Ui/dashboard run test
dotnet test tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj /p:EnableWindowsTargeting=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~MainShellViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
```
