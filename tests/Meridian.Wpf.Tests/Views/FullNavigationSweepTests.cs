using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Views;

[Collection("NavigationServiceSerialCollection")]
public sealed class FullNavigationSweepTests
{
    [Fact]
    public void AllRegisteredPages_ShouldNavigateWithoutRuntimeFailures()
    {
        WpfTestThread.Run(() =>
        {
            using var env = new EnvironmentVariableScope()
                .Set("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING", null)
                .Set("POLYGON_API_KEY", null);

            RunMatUiAutomationFacade.EnsureApplicationResources();

            var services = new ServiceCollection();
            var configureServices = typeof(Meridian.Wpf.App)
                .GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);

            configureServices.Should().NotBeNull();
            configureServices!.Invoke(null, [services]);

            using var serviceProvider = services.BuildServiceProvider();
            var navigationService = NavigationService.Instance;
            var pageTags = navigationService.GetRegisteredPages()
                .OrderBy(tag => tag, StringComparer.Ordinal)
                .ToArray();

            pageTags.Should().NotBeEmpty();

            var failures = new List<string>();

            foreach (var pageTag in pageTags)
            {
                Window? hostWindow = null;
                Exception? dispatcherFailure = null;
                DispatcherUnhandledExceptionEventHandler? unhandledHandler = null;

                Console.WriteLine($"[FullNavigationSweep] Navigating {pageTag}");

                try
                {
                    navigationService.ResetForTests();
                    navigationService.SetServiceProvider(serviceProvider);
                    unhandledHandler = (_, args) =>
                    {
                        dispatcherFailure ??= args.Exception;
                        args.Handled = true;
                    };
                    System.Windows.Application.Current!.DispatcherUnhandledException += unhandledHandler;

                    var frame = new Frame
                    {
                        NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden
                    };

                    hostWindow = new Window
                    {
                        Width = 1400,
                        Height = 900,
                        Content = frame
                    };

                    hostWindow.Show();
                    navigationService.Initialize(frame);

                    var exception = Record.Exception(() =>
                    {
                        var navigated = navigationService.NavigateTo(pageTag);
                        RunMatUiAutomationFacade.DrainDispatcher();
                        frame.UpdateLayout();
                        hostWindow.UpdateLayout();

                        if (!navigated)
                        {
                            var detail = GetNavigationFailureDetail(frame, navigationService, serviceProvider, pageTag);
                            throw new InvalidOperationException(detail);
                        }

                        if (frame.Content is not FrameworkElement)
                        {
                            throw new InvalidOperationException($"Frame content was null after navigating to '{pageTag}'.");
                        }
                    });

                    if (exception is not null)
                    {
                        failures.Add($"{pageTag}: {exception.GetType().Name} - {exception.Message}");
                        continue;
                    }

                    if (dispatcherFailure is not null)
                    {
                        failures.Add($"{pageTag}: {dispatcherFailure.GetType().Name} - {FormatExceptionSummary(dispatcherFailure)}");
                    }

                    Console.WriteLine($"[FullNavigationSweep] Completed {pageTag}");
                }
                finally
                {
                    if (unhandledHandler is not null)
                    {
                        System.Windows.Application.Current!.DispatcherUnhandledException -= unhandledHandler;
                    }

                    hostWindow?.Close();
                    RunMatUiAutomationFacade.DrainDispatcher();
                    navigationService.ResetForTests();
                }
            }

            failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
        });
    }

    private static string GetNavigationFailureDetail(
        Frame frame,
        NavigationService navigationService,
        IServiceProvider serviceProvider,
        string pageTag)
    {
        var constructionFailure = TryGetConstructionFailure(navigationService, serviceProvider, pageTag);
        if (constructionFailure is not null)
        {
            return constructionFailure;
        }

        if (frame.Content is not Page page)
        {
            return $"Navigation returned false for '{pageTag}'.";
        }

        var messages = FindVisualChildren<TextBlock>(page)
            .Select(tb => tb.Text?.Trim())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();

        return messages.Length == 0
            ? $"Navigation returned false for '{pageTag}'."
            : $"Navigation returned false for '{pageTag}': {string.Join(" | ", messages)}";
    }

    private static string? TryGetConstructionFailure(
        NavigationService navigationService,
        IServiceProvider serviceProvider,
        string pageTag)
    {
        var pageType = navigationService.GetPageType(pageTag);
        if (pageType is null)
        {
            return null;
        }

        try
        {
            _ = serviceProvider.GetRequiredService(pageType);
            return null;
        }
        catch (Exception ex)
        {
            return $"Navigation returned false for '{pageTag}': {FormatExceptionSummary(ex)}";
        }
    }

    private static string FormatExceptionSummary(Exception ex)
    {
        var messages = new List<string>();
        Exception? current = ex;

        while (current is not null)
        {
            var message = current.Message?.Trim();
            if (!string.IsNullOrWhiteSpace(message))
            {
                messages.Add(message);
            }

            current = current.InnerException;
        }

        return string.Join(" | ", messages.Distinct(StringComparer.Ordinal));
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
        {
            yield return match;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope Set(string name, string? value)
        {
            if (!_originalValues.ContainsKey(name))
            {
                _originalValues[name] = Environment.GetEnvironmentVariable(name);
            }

            Environment.SetEnvironmentVariable(name, value);
            return this;
        }

        public void Dispose()
        {
            foreach (var entry in _originalValues)
            {
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }
    }
}
