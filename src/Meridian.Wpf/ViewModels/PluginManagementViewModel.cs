using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Meridian.Application.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Represents a single loaded plugin entry shown in the plugin management DataGrid.
/// </summary>
public sealed class PluginItem
{
    private static readonly Brush SuccessBrush = MakeFrozen(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly Brush FailureBrush = MakeFrozen(Color.FromRgb(0xF8, 0x71, 0x71));

    /// <summary>Filename only (no directory path).</summary>
    public string AssemblyFile { get; init; } = "";

    /// <summary>Full path to the DLL.</summary>
    public string AssemblyPath { get; init; } = "";

    /// <summary>"Loaded" or "Failed".</summary>
    public string Status { get; init; } = "";

    /// <summary>Comma-joined short type names, or "—" when none registered.</summary>
    public string RegisteredTypes { get; init; } = "";

    /// <summary>Frozen status brush (green = success, red = failure).</summary>
    public Brush StatusBrush { get; init; } = SuccessBrush;

    /// <summary>Error message shown when <see cref="Status"/> is "Failed".</summary>
    public string? ErrorMessage { get; init; }

    internal static PluginItem FromResult(PluginLoadResult result)
    {
        var shortTypes = result.RegisteredTypes.Count > 0
            ? string.Join(", ", result.RegisteredTypes.Select(ShortName))
            : "—";

        return new PluginItem
        {
            AssemblyPath = result.AssemblyPath,
            AssemblyFile = Path.GetFileName(result.AssemblyPath),
            Status = result.Success ? "Loaded" : "Failed",
            RegisteredTypes = shortTypes,
            StatusBrush = result.Success ? SuccessBrush : FailureBrush,
            ErrorMessage = result.ErrorMessage
        };
    }

    private static string ShortName(string fullName)
        => fullName.Contains('.') ? fullName[(fullName.LastIndexOf('.') + 1)..] : fullName;

    private static SolidColorBrush MakeFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// ViewModel for the Plugin Management page.
/// All state, commands, and plugin orchestration live here; the code-behind is limited to
/// <c>InitializeComponent()</c> and constructor wiring.
/// </summary>
public sealed class PluginManagementViewModel : BindableBase
{
    private readonly IPluginLoaderService _pluginLoader;

    private string _pluginsDirectory = string.Empty;
    private bool _isLoading;
    private string _statusMessage = "Select a directory and click Load.";

    public PluginManagementViewModel(IPluginLoaderService pluginLoader)
    {
        _pluginLoader = pluginLoader ?? throw new ArgumentNullException(nameof(pluginLoader));

        BrowseCommand = new RelayCommand(ExecuteBrowse);
        LoadPluginsCommand = new AsyncRelayCommand(ExecuteLoadAsync, CanLoad);
        RefreshCommand = new AsyncRelayCommand(ExecuteLoadAsync, CanLoad);

        // Show any results already loaded (e.g. auto-loaded at startup via PluginsPath config).
        foreach (var result in _pluginLoader.LoadedPlugins)
            Plugins.Add(PluginItem.FromResult(result));

        UpdateStatusMessage();
    }

    // ── Collections ──────────────────────────────────────────────────────────

    public ObservableCollection<PluginItem> Plugins { get; } = new();

    // ── Bindable scalar properties ────────────────────────────────────────────

    public string PluginsDirectory
    {
        get => _pluginsDirectory;
        set
        {
            if (SetProperty(ref _pluginsDirectory, value))
            {
                LoadPluginsCommand.NotifyCanExecuteChanged();
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                LoadPluginsCommand.NotifyCanExecuteChanged();
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public IRelayCommand BrowseCommand { get; }
    public IAsyncRelayCommand LoadPluginsCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    // ── Command implementations ───────────────────────────────────────────────

    private void ExecuteBrowse()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Plugin Directory",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = string.IsNullOrWhiteSpace(PluginsDirectory)
                                    ? Environment.CurrentDirectory
                                    : PluginsDirectory
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            PluginsDirectory = dialog.SelectedPath;
    }

    private async Task ExecuteLoadAsync()
    {
        if (string.IsNullOrWhiteSpace(PluginsDirectory))
            return;

        IsLoading = true;
        StatusMessage = "Loading plugins…";

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var results = await _pluginLoader.LoadPluginsAsync(PluginsDirectory, cts.Token);

            Plugins.Clear();
            foreach (var r in results)
                Plugins.Add(PluginItem.FromResult(r));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Plugin load timed out.";
            return;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            return;
        }
        finally
        {
            IsLoading = false;
        }

        UpdateStatusMessage();
    }

    private bool CanLoad() => !string.IsNullOrWhiteSpace(PluginsDirectory) && !IsLoading;

    private void UpdateStatusMessage()
    {
        var total = Plugins.Count;
        var loaded = Plugins.Count(p => p.Status == "Loaded");
        var failed = total - loaded;

        StatusMessage = total == 0
            ? "No plugins found."
            : failed == 0
                ? $"{total} plugin{(total != 1 ? "s" : "")} loaded."
                : $"{loaded} of {total} plugin{(total != 1 ? "s" : "")} loaded ({failed} failed).";
    }
}
