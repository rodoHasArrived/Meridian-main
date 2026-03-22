using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
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
                _ = new System.Windows.Application();
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
                Source = new Uri("pack://application:,,,/Meridian.Desktop;component/Styles/AppStyles.xaml", UriKind.Absolute)
            });
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Meridian.Desktop;component/Styles/IconResources.xaml", UriKind.Absolute)
            });
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Meridian.Desktop;component/Styles/Animations.xaml", UriKind.Absolute)
            });
            _resourcesInitialized = true;
        }
    }

    public static IServiceProvider CreateMainPageServiceProvider(RunMatService? runMatService = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<NavigationService>(_ => NavigationService.Instance);
        services.AddSingleton<ConnectionService>(_ => ConnectionService.Instance);
        services.AddSingleton<StatusService>(_ => StatusService.Instance);
        services.AddSingleton<MessagingService>(_ => MessagingService.Instance);
        services.AddSingleton<Meridian.Wpf.Services.NotificationService>(_ => Meridian.Wpf.Services.NotificationService.Instance);
        services.AddTransient<DashboardPage>();
        services.AddSingleton(runMatService ?? RunMatService.Instance);
        services.AddTransient<RunMatViewModel>();
        services.AddTransient<RunMatPage>();
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
        var method = typeof(MainPage).GetMethod("NavigateToPage", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(page, new object[] { pageTag });
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
    {
        var ctor = typeof(RunMatViewModel).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(RunMatService)],
            modifiers: null);

        ctor.Should().NotBeNull();
        return (RunMatViewModel)ctor!.Invoke([runMatService]);
    }

    private static IAsyncRelayCommand GetRequiredAsyncCommand(Button button)
    {
        button.Command.Should().BeAssignableTo<IAsyncRelayCommand>();
        return (IAsyncRelayCommand)button.Command;
    }
}
