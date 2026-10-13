# MVVM Guidelines

Meridian keeps workflow state in view models and shared read models so the browser
workstation and active WPF desktop surfaces can render the same business
posture without forking behavior.

## Current UI Direction

- Browser operator UI work belongs in `src/Meridian.Ui/dashboard/`; WPF operator UI work belongs
  in `src/Meridian.Wpf/`. Both are active product lanes.
- Shared workstation endpoint and read-model support belongs in
  `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/`.
- `src/Meridian.Wpf/` is an active co-equal operator UI lane whose current focus is web-UI parity (`W8-WPF-PARITY-001`); use this guidance for all WPF work, including new parity surfaces as well as compatibility, validation, and maintenance.

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
- Surface the same product behavior through shared contracts and read models; keep WPF-specific
  presentation in its own views and view models.

## Validation

Use the narrowest test that covers the surface. Browser tests run from the repository root;
full WPF validation requires Windows and the development runner:

```powershell
npm --prefix src/Meridian.Ui/dashboard run test
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1 -Restore
```

Use [Engineering validation](../engineering/README.md#buildtestrun) for targeted shared-UI and
view-model test commands. A non-Windows WPF project build can select the stub target and does not
validate the XAML application.
