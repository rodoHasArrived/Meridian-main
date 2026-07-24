using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Storage.Archival;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Shell.Session;

public sealed class WindowStateStore : IWindowStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _stateFilePath;
    private readonly LoggingService _loggingService;

    public WindowStateStore(LoggingService loggingService)
        : this(DefaultStateFilePath, loggingService)
    {
    }

    public WindowStateStore(string stateFilePath, LoggingService? loggingService = null)
    {
        _stateFilePath = string.IsNullOrWhiteSpace(stateFilePath)
            ? throw new ArgumentException("Window state file path is required.", nameof(stateFilePath))
            : stateFilePath;
        _loggingService = loggingService ?? LoggingService.Instance;
    }

    public static string DefaultStateFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Meridian",
        "window-state.json");

    public DesktopWindowState? Load()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return null;
            }

            var json = File.ReadAllText(_stateFilePath);
            return JsonSerializer.Deserialize<DesktopWindowState>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _loggingService.LogWarning(
                "Failed to load desktop window state",
                ("Path", _stateFilePath),
                ("Error", ex.Message));
            return null;
        }
    }

    public async Task SaveAsync(DesktopWindowState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        try
        {
            var json = JsonSerializer.Serialize(state, JsonOptions);
            // Atomic write (temp + rename): a crash mid-save must not corrupt the window
            // state file, which would otherwise silently reset the operator's layout.
            await AtomicFileWriter.WriteAsync(_stateFilePath, json, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _loggingService.LogWarning(
                "Failed to save desktop window state",
                ("Path", _stateFilePath),
                ("Error", ex.Message));
        }
    }
}
