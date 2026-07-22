using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;

namespace Meridian.Wpf.Tests.Support;

internal static class WpfTestThread
{
    private static readonly TimeSpan DispatchTimeout = TimeSpan.FromMinutes(2);
    private static readonly object Sync = new();
    private static Thread? _thread;
    private static Dispatcher? _dispatcher;
    private static readonly ManualResetEventSlim Ready = new(false);

    public static void Run(Action action)
    {
        EnsureStarted();
        ExceptionDispatchInfo? captured = null;

        InvokeWithTimeout(() =>
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
        var task = _dispatcher!.InvokeAsync(action).Task.Unwrap();
        if (!task.Wait(DispatchTimeout))
        {
            throw new TimeoutException($"WPF test action did not complete within {DispatchTimeout.TotalSeconds:N0} seconds.");
        }

        task.GetAwaiter().GetResult();
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
                RunMatUiAutomationFacade.EnsureApplicationResources();
                _dispatcher = Dispatcher.CurrentDispatcher;
                Ready.Set();
                Dispatcher.Run();
            });

            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
            if (!Ready.Wait(DispatchTimeout))
            {
                throw new TimeoutException($"WPF test dispatcher did not start within {DispatchTimeout.TotalSeconds:N0} seconds.");
            }
        }
    }

    private static void DrainDispatcher()
    {
        InvokeWithTimeout(() => { }, DispatcherPriority.ApplicationIdle);
        InvokeWithTimeout(() => { }, DispatcherPriority.ContextIdle);
    }

    private static void InvokeWithTimeout(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        var operation = _dispatcher!.InvokeAsync(action, priority);
        if (!operation.Task.Wait(DispatchTimeout))
        {
            throw new TimeoutException($"WPF dispatcher operation did not complete within {DispatchTimeout.TotalSeconds:N0} seconds.");
        }

        operation.Task.GetAwaiter().GetResult();
    }
}
