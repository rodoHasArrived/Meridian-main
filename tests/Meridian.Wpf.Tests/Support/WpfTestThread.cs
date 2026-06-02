using System.Runtime.ExceptionServices;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Meridian.Wpf.Tests.Support;

internal static class WpfTestThread
{
    private static readonly object Sync = new();
    private static Thread? _thread;
    private static Dispatcher? _dispatcher;
    private static readonly ManualResetEventSlim Ready = new(false);

    public static void Run(Action action)
    {
        EnsureStarted();
        ExceptionDispatchInfo? captured = null;

        _dispatcher!.Invoke(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
        });

        DrainDispatcher();
        captured?.Throw();
    }

    public static void Run(Func<Task> action)
    {
        EnsureStarted();
        _dispatcher!.InvokeAsync(action).Task.Unwrap().GetAwaiter().GetResult();
        DrainDispatcher();
    }

    private static void EnsureStarted()
    {
        if (_dispatcher is not null)
        {
            return;
        }

        lock (Sync)
        {
            if (_dispatcher is not null)
            {
                return;
            }

            _thread = new Thread(() =>
            {
                var app = new System.Windows.Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                EnsureSharedResources(app);
                _dispatcher = Dispatcher.CurrentDispatcher;
                Ready.Set();
                Dispatcher.Run();
            });

            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
            Ready.Wait();
        }
    }

    private static void EnsureSharedResources(System.Windows.Application app)
    {
        var resources = app.Resources.MergedDictionaries;
        var expectedDictionaries = new[]
        {
            "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Dark.xaml",
            "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml",
            "pack://application:,,,/Meridian.Desktop;component/Styles/ThemeTokens.xaml",
            "pack://application:,,,/Meridian.Desktop;component/Styles/AppStyles.xaml",
            "pack://application:,,,/Meridian.Desktop;component/Styles/ThemeTypography.xaml",
            "pack://application:,,,/Meridian.Desktop;component/Styles/ThemeSurfaces.xaml",
            "pack://application:,,,/Meridian.Desktop;component/Styles/ThemeControls.xaml",
            "pack://application:,,,/Meridian.Desktop;component/Styles/IconResources.xaml",
            "pack://application:,,,/Meridian.Desktop;component/Styles/Animations.xaml"
        };

        foreach (var source in expectedDictionaries)
        {
            if (resources.Any(existing => string.Equals(existing.Source?.OriginalString, source, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            resources.Add(new ResourceDictionary { Source = new Uri(source, UriKind.RelativeOrAbsolute) });
        }
    }

    private static void DrainDispatcher()
    {
        _dispatcher!.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        _dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }
}
