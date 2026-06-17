# UI Diagrams

This folder mirrors the generated WPF UI navigation, implementation flow, and screen catalog diagrams that are also kept at the top level of `docs/diagrams/` for compatibility.

Sources:

- `src/Meridian.Wpf/Features/*`
- `src/Meridian.Wpf/Models/ShellNavigationCatalog*.cs`
- `src/Meridian.Wpf/Shell/Services/ShellPageRegistryBuilder.cs`
- `src/Meridian.Wpf/App.xaml.cs`
- `src/Meridian.Wpf/MainWindow.xaml.cs`
- `src/Meridian.Wpf/Views/MainPage.xaml.cs`

Refresh from the repository root:

```bash
npm run generate-diagrams -- --all
node build/node/generate-diagrams.mjs
```

The package command also refreshes the generated WPF screen development tracker:
`docs/status/wpf-screen-development-tracker.md` and `.json`. Use
`npm run generate-wpf-screen-tracker` when only that tracker needs to be refreshed.

Generated outputs:

- `ui-navigation-map.*`
- `ui-implementation-flow.*`
- `ui-wpf-screen-summary.*`
- `ui-wpf-screen-catalog.*`
- `ui-wpf-screens-trading.*`
- `ui-wpf-screens-portfolio.*`
- `ui-wpf-screens-accounting.*`
- `ui-wpf-screens-reporting.*`
- `ui-wpf-screens-strategy.*`
- `ui-wpf-screens-data.*`
- `ui-wpf-screens-settings.*`

After rendering, keep these files in sync with the root `docs/diagrams/ui-*.{dot,svg,png}` artifacts.
