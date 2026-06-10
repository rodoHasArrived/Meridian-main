# UI Diagrams

This folder mirrors the generated WPF UI navigation and implementation flow diagrams that are also kept at the top level of `docs/diagrams/` for compatibility.

Sources:

- `src/Meridian.Wpf/Views/MainPage.xaml`
- `src/Meridian.Wpf/Services/NavigationService.cs`
- `src/Meridian.Wpf/App.xaml.cs`
- `src/Meridian.Wpf/MainWindow.xaml.cs`
- `src/Meridian.Wpf/Views/MainPage.xaml.cs`
- `src/Meridian.Wpf/Views/Pages.cs`

Refresh from the repository root:

```bash
npm run generate-diagrams -- --all
node build/node/generate-diagrams.mjs
```

After rendering, keep these files in sync with the root `docs/diagrams/ui-*.{dot,svg,png}` artifacts.
