using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Meridian.Wpf.Services;

/// <summary>
/// Enforces a single running instance of the application using a named
/// <see cref="Mutex"/>. When a secondary instance is launched (e.g. from a taskbar
/// jump list task), it forwards its command-line arguments to the primary instance
/// via a named pipe and exits. The primary instance raises
/// <see cref="LaunchArgsReceived"/> on the UI thread so callers can navigate to the
/// requested page without a full restart.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    // Global prefix makes the mutex cross-session (all desktop sessions on this machine).
    private const string MutexName = "Global\\Meridian.Desktop.SingleInstance";
    private const string PipeName  = "Meridian.Desktop.SingleInstance.Pipe";

    private static readonly Lazy<SingleInstanceService> _instance =
        new(() => new SingleInstanceService());

    public static SingleInstanceService Instance => _instance.Value;

    private Mutex? _mutex;
    private bool   _ownsMutex;
    private CancellationTokenSource? _pipeListenerCts;

    /// <summary>
    /// Raised on the UI thread when a secondary instance forwards its launch arguments.
    /// </summary>
    public event EventHandler<string[]>? LaunchArgsReceived;

    private SingleInstanceService() { }

    /// <summary>
    /// Attempts to acquire the single-instance mutex.
    /// </summary>
    /// <returns>
    /// <c>true</c> if this is the primary instance; <c>false</c> if another instance is
    /// already running.
    /// </returns>
    public bool TryAcquire()
    {
        try
        {
            _mutex     = new Mutex(initiallyOwned: true, MutexName, out _ownsMutex);
            return _ownsMutex;
        }
        catch (Exception)
        {
            // If the mutex cannot be created (e.g. permission error), assume primary
            // so startup is not blocked.
            return true;
        }
    }

    /// <summary>
    /// Starts the background named-pipe listener that receives arguments forwarded
    /// from secondary instances. Call from the primary instance after the main window
    /// is visible.
    /// </summary>
    public void StartListening()
    {
        _pipeListenerCts = new CancellationTokenSource();
        _ = ListenLoopAsync(_pipeListenerCts.Token);
    }

    /// <summary>
    /// Sends <paramref name="args"/> to the primary instance via the named pipe.
    /// Call this from the secondary instance before calling
    /// <c>Application.Current.Shutdown()</c>.
    /// </summary>
    public static void SendArgsToPrimary(string[] args)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.Out,
                PipeOptions.None);

            pipe.Connect(2000);

            var payload = string.Join("\n", args);
            var bytes   = Encoding.UTF8.GetBytes(payload);
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
        }
        catch (Exception)
        {
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────────

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using var reader  = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                var       payload = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

                var args = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (args.Length > 0)
                {
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        ActivateMainWindow();
                        LaunchArgsReceived?.Invoke(this, args);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Brief backoff before retrying to avoid a tight error loop.
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }
    }

    private static void ActivateMainWindow()
    {
        if (System.Windows.Application.Current?.MainWindow is not Window win) return;

        if (win.WindowState == WindowState.Minimized)
            win.WindowState = WindowState.Normal;

        win.Activate();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _pipeListenerCts?.Cancel();
        _pipeListenerCts?.Dispose();

        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); }
            catch (ApplicationException) { /* Already released */ }
        }

        _mutex?.Dispose();
        _ownsMutex = false;
    }
}
