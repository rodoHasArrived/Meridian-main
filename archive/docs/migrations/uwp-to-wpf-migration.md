# UWP to WPF Migration Guide

> **Migration Complete**: The UWP project has been fully removed from the codebase. WPF is the sole desktop client.
> This document is retained as a historical reference for the migration rationale and technical decisions.

This document outlines the migration from UWP/WinUI 3 to WPF (.NET 9) for the Meridian desktop application.

## Executive Summary

**Status**: Migration complete, UWP removed
**Date**: January 31, 2026 (initial), February 2026 (UWP removed)
**Framework**: WPF (.NET 9.0)
**Reason**: Maximum Windows stability, broader compatibility, simpler deployment

## Migration Rationale

### Why Migrate from UWP?

The UWP implementation faced several architectural challenges:

1. **WinRT Metadata Limitations**
   - WinUI 3 XAML compiler rejects standard .NET assemblies
   - Error: "Assembly is not allowed in type universe"
   - Required source file inclusion workarounds for shared contracts

2. **Platform Constraints**
   - Limited to Windows 10 1809+ (build 17763)
   - MSIX packaging complexity
   - Store deployment requirements
   - Limited backwards compatibility

3. **Development Friction**
   - Can't reference standard .NET projects directly
   - ~1,300 lines of duplicated DTOs to work around assembly restrictions
   - Complex build configurations

### Why WPF?

WPF offers significant advantages for desktop applications:

1. **Maturity & Stability**
   - 15+ years of production use
   - Battle-tested in enterprise environments
   - Extensive documentation and community support

2. **Broader Compatibility**
   - Works on Windows 7, 8, 10, 11
   - No Store/MSIX requirements
   - Standard .exe deployment

3. **Better Integration**
   - Can reference any .NET assembly directly
   - Full access to .NET ecosystem
   - No WinRT metadata issues

4. **Simplified Architecture**
   - Direct project references to shared contracts
   - No source file linking workarounds
   - Standard .NET project structure

## Technical Comparison

| Aspect | UWP/WinUI 3 | WPF |
|--------|-------------|-----|
| Target Framework | net9.0-windows10.0.19041.0 | net9.0-windows |
| Min Windows Version | Windows 10 1809 (17763) | Windows 7 SP1 |
| Deployment | MSIX packages | Standard .exe |
| Assembly References | Restricted (WinRT only) | Unrestricted (.NET) |
| Project References | Source linking required | Direct references |
| XAML Namespace | Microsoft.UI.Xaml | System.Windows |
| Application Model | Windows.UI.Xaml.Application | System.Windows.Application |
| Maturity | New (2021) | Mature (2006) |
| Community Support | Growing | Extensive |

## Migration Steps Completed

### Phase 1: Project Setup ✅

- [x] Created `Meridian.Wpf` project
- [x] Configured .NET 9.0 targeting with `net9.0-windows`
- [x] Added WPF support with `<UseWPF>true</UseWPF>`
- [x] Added platform detection for cross-platform builds
- [x] Configured package references (Material Design, Extended WPF Toolkit)
- [x] Added to solution file with proper build configurations

### Phase 2: Core Infrastructure ✅

- [x] **App.xaml/App.xaml.cs** - Application entry point with DI
  - Migrated from `Microsoft.UI.Xaml.Application` to `System.Windows.Application`
  - Added `IHost` for dependency injection
  - Configured services and HTTP client factory
  - Implemented graceful shutdown

- [x] **MainWindow.xaml/MainWindow.xaml.cs** - Main application window
  - Migrated from WinUI 3 `Window` to WPF `Window`
  - Implemented navigation panel with Material Design
  - Added status indicator
  - Set up keyboard shortcuts

### Phase 3: Services Layer ✅

All 23 services implemented:

**Core Services:**
- [x] IConnectionService / ConnectionService - HTTP API client
- [x] INavigationService / NavigationService - Frame-based navigation
- [x] INotificationService / NotificationService - User notifications
- [x] IThemeService / ThemeService - Light/Dark theme management
- [x] IConfigService / ConfigService - Configuration management
- [x] ILoggingService / LoggingService - Debug logging
- [x] FirstRunService - First-run detection and setup

**Utility Services:**
- [x] IKeyboardShortcutService / KeyboardShortcutService - Keyboard shortcuts
- [x] IMessagingService / MessagingService - Pub/sub messaging

**Stub Services (for future implementation):**
- [x] IOfflineTrackingPersistenceService / OfflineTrackingPersistenceService
- [x] IBackgroundTaskSchedulerService / BackgroundTaskSchedulerService
- [x] IPendingOperationsQueueService / PendingOperationsQueueService

### Phase 4: Views & XAML ✅ (Initial)

- [x] DashboardPage.xaml - Functional dashboard with status cards
- [x] SymbolsPage.xaml - Placeholder stub
- [x] BackfillPage.xaml - Placeholder stub
- [x] SettingsPage.xaml - Placeholder stub
- [x] AppStyles.xaml - Base styles and theme resources

### Phase 5: Build & Integration ✅

- [x] Project builds successfully on Windows
- [x] Cross-platform build support (stub on Linux/macOS)
- [x] Added to solution configuration
- [x] Package version management in Directory.Packages.props

## Current Status Snapshot

### What Works Today

- WPF application boots, hosts DI container, and shows the main window.
- Navigation shell renders with Material Design styling.
- Core services compile and can be injected into views.

### What Is Stubbed

- ViewModel logic beyond basic wiring.
- Majority of feature pages (content and behaviors).
- Specialized controls and data visualizations.

## Key Architectural Changes

### 1. Assembly References

**Before (UWP):**
```xml
<!-- Source file linking workaround -->
<Compile Include="..\Meridian.Contracts\Configuration\*.cs"
         Link="SharedModels\Configuration\%(Filename)%(Extension)" />
```

**After (WPF):**
```xml
<!-- Direct project reference -->
<ProjectReference Include="..\Meridian.Contracts\Meridian.Contracts.csproj" />
```

### 2. XAML Namespaces

**Before (UWP):**
```xml
<Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="using:Meridian.Uwp">
```

**After (WPF):**
```xml
<Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:Meridian.Wpf">
```

### 3. Application Lifecycle

**Before (UWP):**
```csharp
protected override void OnLaunched(LaunchActivatedEventArgs args)
{
    _window = new MainWindow();
    _window.Activate();
}
```

**After (WPF):**
```csharp
protected override async void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    _host = CreateHostBuilder(e.Args).Build();
    await _host.StartAsync();
    var mainWindow = _host.Services.GetRequiredService<MainWindow>();
    mainWindow.Show();
}
```

### 4. Dependency Injection

**Before (UWP):**
```csharp
// Manual singleton pattern
public static ConnectionService Instance { get; } = new ConnectionService();
```

**After (WPF):**
```csharp
// Microsoft.Extensions.DependencyInjection
services.AddSingleton<IConnectionService, ConnectionService>();

// Constructor injection
public MainWindow(IConnectionService connectionService, ...)
{
    _connectionService = connectionService;
}
```

## Remaining Work

### High Priority

- [ ] **ViewModels Implementation** (5 ViewModels from UWP)
  - DashboardViewModel
  - BackfillViewModel
  - DataExportViewModel
  - DataQualityViewModel
  - MainViewModel

- [ ] **Complete Page Migration** (35 remaining pages)
  - AdminMaintenancePage
  - AdvancedAnalyticsPage
  - AnalysisExportPage
  - ArchiveHealthPage
  - BackfillPage (full implementation)
  - ChartingPage
  - DataBrowserPage
  - DataQualityPage
  - [... 27 more pages]

- [ ] **Custom Controls** (migrate from UWP)
  - Navigation controls
  - Data visualization controls
  - Input validation controls

### Medium Priority

- [ ] **Converters Migration** (value converters for XAML binding)
- [ ] **Dialogs & Helpers** (modal dialogs, helper utilities)
- [ ] **Styles & Themes** (complete Material Design integration)
- [ ] **Assets** (icons, images, animations)

### Short-Term Next Steps (Suggested Order)

1. Implement `MainViewModel` and `DashboardViewModel` to validate navigation, telemetry, and theme toggling.
2. Migrate `DataQualityPage` and `DataBrowserPage` to validate data-bound grids and filters.
3. Port UWP converters into a shared WPF converters project to unlock remaining pages.
4. Establish a WPF-specific UI test harness for smoke checks.

### Low Priority

- [ ] **Testing** (unit tests for ViewModels and Services)
- [ ] **Documentation** (update all docs to reference WPF)
- [ ] **CI/CD Updates** (GitHub Actions for WPF builds)
- [ ] **Installer** (WiX or MSIX for distribution)

## Control Mapping Reference

Common control migrations from UWP to WPF:

| UWP/WinUI 3 | WPF | Notes |
|-------------|-----|-------|
| `NavigationView` | Custom or MahApps.Metro | Use Material Design navigation |
| `InfoBar` | Custom or MahApps.Metro | Use Material Design snackbar |
| `CommandBar` | `ToolBar` | Standard WPF toolbar |
| `AppBarButton` | `Button` with icon | Material Design buttons |
| `TeachingTip` | Custom tooltip | Use WPF tooltip with styling |
| `ProgressRing` | `ProgressBar` (circular) | Material Design circular progress |
| `NumberBox` | `xctk:IntegerUpDown` | Extended WPF Toolkit |
| `ColorPicker` | `xctk:ColorPicker` | Extended WPF Toolkit |
| `SplitView` | `Grid` with splitter | Standard WPF layout |
| `PersonPicture` | Custom control | Implement if needed |

## Testing Strategy

### Manual Testing Checklist

- [ ] Application startup and shutdown
- [ ] Navigation between all pages
- [ ] Connection to backend API
- [ ] Configuration load/save
- [ ] Theme switching (Light/Dark)
- [ ] Keyboard shortcuts
- [ ] Notifications
- [ ] First-run experience
- [ ] Error handling and recovery

### Automated Testing

- [ ] Unit tests for ViewModels
- [ ] Integration tests for Services
- [ ] UI tests for critical workflows

### Recommended Tooling

- **xUnit + FluentAssertions** for ViewModel and service tests
- **Playwright for .NET** for UI smoke tests
- **Coverlet** for code coverage

## Deployment Strategy

### Development Builds

```bash
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Debug
```

### Release Builds

```bash
dotnet publish src/Meridian.Wpf/Meridian.Wpf.csproj \
  -c Release \
  -r win-x64 \
  --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
```

### Distribution Options

1. **ZIP Archive** - Simple distribution of published files
2. **Installer** - WiX Toolset for MSI installer
3. **ClickOnce** - Auto-updating deployment (optional)
4. **MSIX** - Modern packaging (optional, for Store)

## Performance Considerations

### WPF Optimizations

1. **Virtualization** - Use `VirtualizingStackPanel` for large lists
2. **Lazy Loading** - Load pages on demand
3. **Async Operations** - Keep UI responsive with async/await
4. **Resource Dictionaries** - Merge only needed resources
5. **Binding Optimization** - Use `OneTime` or `OneWay` when appropriate

### Monitoring

- Track UI thread responsiveness
- Monitor memory usage
- Profile startup time
- Measure navigation performance

## Known Issues

1. **Material Design Theme** - May need customization for brand consistency
2. **High DPI Scaling** - Test on various display scales
3. **Accessibility** - Ensure screen reader compatibility
4. **Localization** - Plan for internationalization

## Resources

### WPF Documentation

- [WPF Overview](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/)
- [XAML Overview](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/xaml/)
- [Data Binding](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/data/)

### Libraries Used

- [Material Design In XAML](http://materialdesigninxaml.net/)
- [Extended WPF Toolkit](https://github.com/xceedsoftware/wpftoolkit)
- [CommunityToolkit.Mvvm](https://docs.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)

### Migration References

- [WinUI 3 to WPF Migration](https://platform.uno/docs/articles/migrating-from-winui3.html)
- [UWP to WPF Migration](https://docs.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-migrate)

## Conclusion

The WPF migration provides a solid foundation for a stable, maintainable Windows desktop application. The initial implementation includes:

- ✅ Core infrastructure and services
- ✅ Navigation framework
- ✅ Dependency injection
- ✅ Basic UI with Material Design
- ✅ Cross-platform build support

The remaining work primarily involves migrating the extensive page collection from UWP, which can be done incrementally as features are needed.

## Version History

- **v1.0.0** (2026-01-31) - Initial WPF implementation
  - Core infrastructure complete
  - 4 pages implemented (Dashboard, Symbols, Backfill, Settings)
  - 23 services implemented
  - Build system configured

---

*Last Updated: 2026-02-01*
