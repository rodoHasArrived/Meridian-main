# Meridian Blueprint Patterns

Use this reference when a blueprint needs concrete naming or implementation patterns.

## Naming

- Interfaces: `IXxxService`, `IXxxStore`, `IXxxProvider`, `IXxxClient`
- Concrete services: `XxxService`
- WPF view models: `XxxViewModel : BindableBase`
- Options: `XxxOptions`
- Orchestrators: `XxxRunOrchestrator`, `XxxWorkflowService`
- Read models: `XxxSummary`, `XxxSnapshot`, `XxxRecord`

## Namespace Hints

- Domain: `Meridian.Domain.[Area]`
- Application: `Meridian.Application.[Area]`
- Infrastructure: `Meridian.Infrastructure.[Area]`
- Storage: `Meridian.Storage.[Area]`
- UI Services: `Meridian.Ui.Services.[Area]`
- WPF: `Meridian.Wpf.[Area]`

## ADR Reminders

- ADR-001: provider abstraction contracts
- ADR-004: async methods accept `CancellationToken`
- ADR-011: use `IOptionsMonitor<T>` for mutable config
- ADR-013: use `EventPipelinePolicy.*.CreateChannel<T>()`
- ADR-014: use source-generated JSON serialization

## DI Patterns

```csharp
services.Configure<XxxOptions>(config.GetSection(XxxOptions.SectionName));
services.AddSingleton<IXxxService, XxxService>();
services.AddHttpClient<IXxxClient, XxxClient>();
```

## WPF Pattern

```csharp
public sealed class DashboardViewModel : BindableBase
{
    private readonly IResearchWorkspaceService _service;

    public DashboardViewModel(IResearchWorkspaceService service)
    {
        _service = service;
    }
}
```

Keep pages thin:

```csharp
public partial class DashboardPage : Page
{
    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
```
