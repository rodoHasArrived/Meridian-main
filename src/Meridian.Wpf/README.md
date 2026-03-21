# Meridian - WPF Desktop Application

This is the WPF (.NET 9) desktop application for Meridian, providing a modern Windows desktop experience with maximum stability and compatibility.

## Overview

The WPF application provides a native Windows desktop interface for managing market data collection, including:

- **Real-time Dashboard** - Monitor collection status and system health
- **Symbol Management** - Add, remove, and configure symbols to collect
- **Backfill Operations** - Historical data download and management
- **Settings & Configuration** - Application and provider configuration
- **Data Quality Monitoring** - Track data quality metrics and alerts

## Why WPF?

This project was migrated from UWP/WinUI 3 to WPF for several key reasons:

### Benefits of WPF

1. **Broader Compatibility** - Works on Windows 7+ (vs UWP's Windows 10+ requirement)
2. **Mature & Stable** - 15+ years of production use and stability
3. **No WinRT Metadata Issues** - Can reference standard .NET assemblies directly
4. **Simpler Deployment** - Standard .exe deployment (no MSIX required)
5. **Better Tooling** - Extensive third-party library support
6. **Performance** - Optimized for desktop scenarios

### Migration from UWP

The previous UWP implementation had several challenges:
- WinUI 3 XAML compiler rejected standard .NET assemblies
- Required source file inclusion workarounds for shared contracts
- Limited to Windows 10+ with Store deployment requirements
- MSIX packaging complexity

WPF solves these issues while providing a more straightforward development and deployment experience.


## Migration Direction

The WPF application remains Meridian's primary desktop surface, but it is now the host for the **Trading Workstation Migration**. The current codebase already contains broad feature coverage; the next implementation phase reorganizes that breadth into workflow-centric workspaces:

- **Research** — backtesting, experiment comparison, charts, replay
- **Trading** — live monitoring, orders, positions, paper/live strategy operation
- **Data Operations** — providers, symbols, backfill, storage, export
- **Governance** — portfolio, ledger, diagnostics, audit, settings

See [`docs/plans/trading-workstation-migration-blueprint.md`](../../docs/plans/trading-workstation-migration-blueprint.md) for the active migration blueprint and [`docs/architecture/ui-redesign.md`](../../docs/architecture/ui-redesign.md) for the target information architecture.

## Architecture

### Technology Stack

- **.NET 9.0** - Latest .NET with Windows support
- **WPF** - Windows Presentation Foundation for UI
- **Material Design** - Modern, clean UI design system
- **MVVM Pattern** - Model-View-ViewModel architecture
- **Dependency Injection** - Microsoft.Extensions.DependencyInjection
- **Async/Await** - Fully asynchronous operations

### Project Structure

```
src/Meridian.Wpf/
├── App.xaml                      # Application entry point
├── App.xaml.cs                   # Application startup and DI
├── MainWindow.xaml               # Main window with navigation
├── MainWindow.xaml.cs            # Main window code-behind
├── Services/                     # Application services
│   ├── ConnectionService.cs      # API connection management
│   ├── NavigationService.cs      # Page navigation
│   ├── NotificationService.cs    # User notifications
│   ├── ThemeService.cs           # Theme management
│   ├── ConfigService.cs          # Configuration management
│   └── ...                       # Other services
├── Views/                        # XAML pages
│   ├── DashboardPage.xaml        # Dashboard view
│   ├── SymbolsPage.xaml          # Symbol management
│   ├── BackfillPage.xaml         # Backfill operations
│   └── SettingsPage.xaml         # Application settings
├── ViewModels/                   # View models (MVVM)
├── Styles/                       # XAML styles and themes
└── Assets/                       # Images, icons, etc.
```

## Building

### Requirements

- **.NET 9.0 SDK** or later
- **Windows 10/11** (WPF requires Windows)
- **Visual Studio 2022** (optional, recommended for XAML designer)

### Build Commands

```bash
# Restore dependencies
dotnet restore src/Meridian.Wpf/Meridian.Wpf.csproj

# Build
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj

# Run
dotnet run --project src/Meridian.Wpf/Meridian.Wpf.csproj

# Publish (self-contained)
dotnet publish src/Meridian.Wpf/Meridian.Wpf.csproj -c Release -r win-x64 --self-contained
```

### Platform Detection

The project uses conditional compilation to support cross-platform builds:

- **On Windows**: Builds as a full WPF application
- **On Linux/macOS**: Builds as a minimal stub library (for CI/CD compatibility)

This allows the solution to build on all platforms without errors, while only producing a functional application on Windows.

## Running the Application

### Prerequisites

1. **Backend Service Running** - The WPF app connects to the backend API at `http://localhost:8080`
2. **Configuration File** - Ensure `appsettings.json` exists with valid configuration

### First Run

On first run, the application will:
1. Check for existing configuration
2. Create default configuration if needed
3. Detect system theme (Light/Dark)
4. Initialize services and connect to backend

### Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+D` | Navigate to Dashboard |
| `Ctrl+S` | Navigate to Symbols |
| `Ctrl+B` | Navigate to Backfill |
| `Ctrl+,` | Open Settings |
| `F5` | Start data collection |
| `F6` | Stop data collection |
| `Ctrl+Shift+T` | Toggle light/dark theme |

## Development

### Adding New Pages

1. Create XAML page in `Views/` directory:
```xaml
<Page x:Class="Meridian.Wpf.Views.MyPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      Title="My Page">
    <Grid>
        <!-- Your content here -->
    </Grid>
</Page>
```

2. Create code-behind in `Views/`:
```csharp
namespace Meridian.Wpf.Views;

public partial class MyPage : Page
{
    public MyPage()
    {
        InitializeComponent();
    }
}
```

3. Register page in `NavigationService.cs`:
```csharp
_pages["MyPage"] = typeof(MyPage);
```

4. Add navigation button in `MainWindow.xaml`

### Adding New Services

1. Create interface in `Services/`:
```csharp
public interface IMyService
{
    Task DoSomethingAsync(CancellationToken ct = default);
}
```

2. Create implementation:
```csharp
public class MyService : IMyService
{
    public async Task DoSomethingAsync(CancellationToken ct = default)
    {
        // Implementation
    }
}
```

3. Register in `App.xaml.cs`:
```csharp
services.AddSingleton<IMyService, MyService>();
```

4. Inject into pages/view models via constructor

## Deployment

### Standalone Executable

```bash
dotnet publish src/Meridian.Wpf/Meridian.Wpf.csproj \
  -c Release \
  -r win-x64 \
  --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
```

This creates a single `.exe` file that includes the .NET runtime.

### Framework-Dependent

```bash
dotnet publish src/Meridian.Wpf/Meridian.Wpf.csproj \
  -c Release \
  -r win-x64 \
  --no-self-contained
```

Requires .NET 9.0 runtime to be installed on target machine.

## Configuration

The application uses `appsettings.json` for configuration. On first run, it will copy `appsettings.sample.json` if no configuration exists.

Configuration is stored in: `%APPDATA%\Meridian\appsettings.json`

## Troubleshooting

### Connection Issues

- Ensure backend API is running at `http://localhost:8080`
- Check firewall settings
- Verify configuration in Settings page

### Theme Issues

- Theme preference is stored in Windows registry
- Check Windows system theme settings
- Use `Ctrl+Shift+T` to toggle theme manually

### Build Errors

- Ensure .NET 9.0 SDK is installed
- Run `dotnet restore` before building
- Check that all NuGet packages are restored

## Future Enhancements

- [ ] Complete migration of all 39 UWP pages
- [ ] Add ViewModels for MVVM pattern
- [ ] Implement real-time data visualization (charts)
- [ ] Add export/import functionality
- [ ] Implement advanced filtering and search
- [ ] Add data quality dashboards
- [ ] Implement plugin system for extensibility

## Related Documentation

- [Architecture Overview](../../docs/architecture/overview.md)
- [Getting Started Guide](../../docs/getting-started/setup.md)
- [Configuration Guide](../../docs/getting-started/configuration.md)
- [UWP to WPF Migration Guide](../../docs/development/uwp-to-wpf-migration.md)

## License

Copyright 2024-2026 Meridian Team. See [LICENSE](../../LICENSE) for details.
