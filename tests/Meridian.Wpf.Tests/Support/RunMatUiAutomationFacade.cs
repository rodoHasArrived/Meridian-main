using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Application.FundAccounts;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Contracts;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Converters;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Support;

internal sealed class RunMatUiAutomationFacade : IDisposable
{
    private static readonly object ResourceSync = new();
    private static bool _resourcesInitialized;
    private readonly string _rootDirectory;

    public RunMatUiAutomationFacade(string? rootDirectory = null)
    {
        EnsureApplicationResources();

        _rootDirectory = rootDirectory ?? Path.Combine(Path.GetTempPath(), "runmat-ui-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDirectory);

        RunMatService = new RunMatService(_rootDirectory);
        ViewModel = CreateViewModel(RunMatService);
        Page = new RunMatPage(ViewModel);
        Page.ApplyTemplate();
        Page.UpdateLayout();
    }

    public RunMatService RunMatService { get; }

    public RunMatViewModel ViewModel { get; }

    public RunMatPage Page { get; }

    public string RootDirectory => _rootDirectory;

    public TextBox ExecutablePathTextBox => GetRequired<TextBox>("ExecutablePathTextBox");

    public TextBox WorkingDirectoryTextBox => GetRequired<TextBox>("WorkingDirectoryTextBox");

    public TextBox AdditionalArgumentsTextBox => GetRequired<TextBox>("AdditionalArgumentsTextBox");

    public TextBox ScriptNameTextBox => GetRequired<TextBox>("ScriptNameTextBox");

    public TextBox ScriptSourceTextBox => GetRequired<TextBox>("ScriptSourceTextBox");

    public Button SaveScriptButton => GetRequired<Button>("SaveScriptButton");

    public Button LoadScriptButton => GetRequired<Button>("LoadScriptButton");

    public Button RunScriptButton => GetRequired<Button>("RunScriptButton");

    public Button RefreshScriptsButton => GetRequired<Button>("RefreshScriptsButton");

    public ListBox SavedScriptsListBox => GetRequired<ListBox>("SavedScriptsListBox");

    public ListBox OutputLinesListBox => GetRequired<ListBox>("OutputLinesListBox");

    public TextBlock AutomationStatusText => GetRequired<TextBlock>("RunMatAutomationStatusText");

    public TextBlock AutomationLastRunText => GetRequired<TextBlock>("RunMatAutomationLastRunText");

    public TextBlock AutomationResolvedExecutableText => GetRequired<TextBlock>("RunMatAutomationResolvedExecutableText");

    public async Task InitializeAsync()
    {
        await ViewModel.InitializeAsync();
        DrainDispatcher();
    }

    public async Task SaveAsync()
    {
        await GetRequiredAsyncCommand(SaveScriptButton).ExecuteAsync(null);
        DrainDispatcher();
    }

    public async Task LoadAsync()
    {
        await GetRequiredAsyncCommand(LoadScriptButton).ExecuteAsync(null);
        DrainDispatcher();
    }

    public async Task RefreshAsync()
    {
        await GetRequiredAsyncCommand(RefreshScriptsButton).ExecuteAsync(null);
        DrainDispatcher();
    }

    public async Task RunAsync()
    {
        await GetRequiredAsyncCommand(RunScriptButton).ExecuteAsync(null);
        DrainDispatcher();
    }

    public void SetText(TextBox textBox, string value)
    {
        textBox.Text = value;
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        DrainDispatcher();
    }

    public T GetRequired<T>(string name) where T : FrameworkElement
    {
        Page.FindName(name).Should().BeOfType<T>($"{name} should be declared on the RunMat page");
        return (T)Page.FindName(name)!;
    }

    public static string GetRepoFilePath(string relativePath)
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
    }

    public static void EnsureApplicationResources()
    {
        if (_resourcesInitialized)
        {
            return;
        }

        lock (ResourceSync)
        {
            if (_resourcesInitialized)
            {
                return;
            }

            if (System.Windows.Application.Current is null)
            {
                _ = new System.Windows.Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
            }
            else
            {
                System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }

            var dictionaries = System.Windows.Application.Current!.Resources.MergedDictionaries;
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Light.xaml", UriKind.Absolute)
            });
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml", UriKind.Absolute)
            });
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Meridian.Desktop;component/Styles/ThemeTokens.xaml", UriKind.Absolute)
            });
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Meridian.Desktop;component/Styles/BrandResources.xaml", UriKind.Absolute)
            });
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Meridian.Desktop;component/Styles/ThemeControls.xaml", UriKind.Absolute)
            });
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Meridian.Desktop;component/Styles/AppStyles.xaml", UriKind.Absolute)
            });
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Meridian.Desktop;component/Styles/ThemeTypography.xaml", UriKind.Absolute)
            });
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Meridian.Desktop;component/Styles/ThemeSurfaces.xaml", UriKind.Absolute)
            });
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Meridian.Desktop;component/Styles/IconResources.xaml", UriKind.Absolute)
            });
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Meridian.Desktop;component/Styles/Animations.xaml", UriKind.Absolute)
            });

            if (!System.Windows.Application.Current.Resources.Contains("GhostButtonStyle"))
            {
                System.Windows.Application.Current.Resources["GhostButtonStyle"] = new Style(typeof(Button));
            }

            if (!System.Windows.Application.Current.Resources.Contains("StringToVisibilityConverter"))
            {
                System.Windows.Application.Current.Resources["StringToVisibilityConverter"] = new StringToVisibilityConverter();
            }

            if (!System.Windows.Application.Current.Resources.Contains("BoolToVisibility"))
            {
                System.Windows.Application.Current.Resources["BoolToVisibility"] = new BoolToVisibilityConverter();
            }

            if (!System.Windows.Application.Current.Resources.Contains("BoolToVisibilityConverter"))
            {
                System.Windows.Application.Current.Resources["BoolToVisibilityConverter"] = new BoolToVisibilityConverter();
            }

            if (!System.Windows.Application.Current.Resources.Contains("IntToVisibilityConverter"))
            {
                System.Windows.Application.Current.Resources["IntToVisibilityConverter"] = new IntToVisibilityConverter();
            }

            System.Windows.Application.Current.Resources["CommandBarBackground"] = new SolidColorBrush(Color.FromRgb(24, 28, 34));
            System.Windows.Application.Current.Resources["CommandBarBorderBrush"] = new SolidColorBrush(Color.FromRgb(52, 58, 68));
            _resourcesInitialized = true;
        }
    }

    public static IServiceProvider CreateMainPageServiceProvider(
        RunMatService? runMatService = null,
        FundContextService? fundContextService = null)
    {
        var services = new ServiceCollection();
        var serviceRoot = Path.Combine(Path.GetTempPath(), "meridian-mainpage-tests", $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(serviceRoot);
        var fundContext = fundContextService ?? new FundContextService(Path.Combine(Path.GetTempPath(), "meridian-mainpage-tests", $"{Guid.NewGuid():N}.json"));
        services.AddSingleton<NavigationService>(_ => NavigationService.Instance);
        services.AddSingleton<INavigationService>(_ => NavigationService.Instance);
        services.AddSingleton<ConnectionService>(_ => ConnectionService.Instance);
        services.AddSingleton<StatusService>(_ => StatusService.Instance);
        services.AddSingleton<ApiClientService>(_ => ApiClientService.Instance);
        services.AddSingleton<ApiStatusService>();
        services.AddSingleton<IStatusService>(sp => sp.GetRequiredService<ApiStatusService>());
        services.AddSingleton<Meridian.Wpf.Services.LoggingService>(_ => Meridian.Wpf.Services.LoggingService.Instance);
        services.AddSingleton<ILoggingService>(_ => Meridian.Wpf.Services.LoggingService.Instance);
        services.AddSingleton<MessagingService>(_ => MessagingService.Instance);
        services.AddSingleton<Meridian.Wpf.Services.NotificationService>(_ => Meridian.Wpf.Services.NotificationService.Instance);
        services.AddSingleton(fundContext);
        services.AddSingleton<FixtureModeDetector>(_ => FixtureModeDetector.Instance);
        services.AddSingleton<StrategyRunWorkspaceService>(_ =>
        {
            var service = new StrategyRunWorkspaceService();
            StrategyRunWorkspaceService.SetInstance(service);
            return service;
        });
        services.AddSingleton<InMemoryFundAccountService>(_ => new InMemoryFundAccountService(Path.Combine(serviceRoot, "fund-accounts.json")));
        services.AddSingleton<IFundAccountService>(sp => sp.GetRequiredService<InMemoryFundAccountService>());
        services.AddSingleton<IFundStructureService>(sp => new InMemoryFundStructureService(
            sp.GetRequiredService<IFundAccountService>(),
            sharedDataAccessService: null,
            securityMasterQueryService: null,
            persistencePath: Path.Combine(serviceRoot, "fund-structure.json")));
        services.AddSingleton(sp => new WorkstationOperatingContextService(
            sp.GetRequiredService<FundContextService>(),
            sp.GetService<IFundStructureService>(),
            storagePath: Path.Combine(serviceRoot, "operating-context.json")));
        services.AddSingleton<WorkspaceShellContextService>();
        services.AddSingleton<FundAccountReadService>();
        services.AddSingleton<CashFinancingReadService>();
        services.AddSingleton<FundLedgerReadService>();
        services.AddSingleton<ReconciliationReadService>();
        services.AddTransient<DashboardPage>();
        var resolvedRunMatService = runMatService ?? RunMatService.Instance;
        services.AddSingleton(resolvedRunMatService);
        services.AddTransient(_ => CreateViewModel(resolvedRunMatService));
        services.AddTransient<RunMatPage>();
        services.AddTransient<MainPageViewModel>();
        services.AddTransient<MainPage>();
        return services.BuildServiceProvider();
    }

    public static void InvokeMainPageLoaded(MainPage page)
    {
        var method = typeof(MainPage).GetMethod("OnPageLoaded", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(page, new object[] { page, new RoutedEventArgs() });
        DrainDispatcher();
    }

    public static void InvokeNavigateToPage(MainPage page, string pageTag)
    {
        page.DataContext.Should().BeOfType<MainPageViewModel>();
        var viewModel = (MainPageViewModel)page.DataContext!;
        viewModel.NavigateToPageCommand.Execute(pageTag);
        DrainDispatcher();
    }

    public static void DrainDispatcher()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    public void Dispose()
    {
        ViewModel.Dispose();
        try
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup for temp UI smoke workspace.
        }
    }

    private static RunMatViewModel CreateViewModel(RunMatService runMatService)
        => new(runMatService);

    public static void ClearNavigationServiceProviderForTests()
    {
        var field = typeof(NavigationService).GetField("_serviceProvider", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(NavigationService.Instance, null);
    }

    private static IAsyncRelayCommand GetRequiredAsyncCommand(Button button)
    {
        button.Command.Should().BeAssignableTo<IAsyncRelayCommand>();
        return (IAsyncRelayCommand)button.Command;
    }
}
