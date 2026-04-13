using System.Runtime.ExceptionServices;
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

        captured?.Throw();
    }

    public static void Run(Func<Task> action)
    {
        EnsureStarted();
        _dispatcher!.InvokeAsync(action).Task.Unwrap().GetAwaiter().GetResult();
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
                _ = new System.Windows.Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
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
}
