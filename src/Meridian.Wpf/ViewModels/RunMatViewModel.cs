using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public sealed class RunMatViewModel : BindableBase, IDisposable
{
    private readonly RunMatService _runMatService;
    private CancellationTokenSource? _runCts;
    private bool _hasRunAttempt;

    public ObservableCollection<RunMatScriptDocument> Scripts { get; } = [];
    public ObservableCollection<RunMatOutputLine> OutputLines { get; } = [];

    private RunMatScriptDocument? _selectedScript;
    public RunMatScriptDocument? SelectedScript
    {
        get => _selectedScript;
        set => SetProperty(ref _selectedScript, value);
    }

    private string _scriptName = "scratch.m";
    public string ScriptName
    {
        get => _scriptName;
        set => SetProperty(ref _scriptName, value);
    }

    private string _scriptSource = string.Empty;
    public string ScriptSource
    {
        get => _scriptSource;
        set => SetProperty(ref _scriptSource, value);
    }

    private string _executablePath = string.Empty;
    public string ExecutablePath
    {
        get => _executablePath;
        set => SetProperty(ref _executablePath, value);
    }

    private string _workingDirectory = string.Empty;
    public string WorkingDirectory
    {
        get => _workingDirectory;
        set => SetProperty(ref _workingDirectory, value);
    }

    private string _additionalArguments = string.Empty;
    public string AdditionalArguments
    {
        get => _additionalArguments;
        set => SetProperty(ref _additionalArguments, value);
    }

    private string _statusText = "Loading RunMat workspace...";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private string _serviceStatus = string.Empty;
    public string ServiceStatus
    {
        get => _serviceStatus;
        set => SetProperty(ref _serviceStatus, value);
    }

    private string _resolvedExecutablePath = "Not resolved";
    public string ResolvedExecutablePath
    {
        get => _resolvedExecutablePath;
        set => SetProperty(ref _resolvedExecutablePath, value);
    }

    private string _lastRunSummary = "No run executed yet.";
    public string LastRunSummary
    {
        get => _lastRunSummary;
        set
        {
            if (SetProperty(ref _lastRunSummary, value))
            {
                RaiseOutputPresentationChanged();
            }
        }
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
            {
                RaisePropertyChanged(nameof(CanRun));
                RaisePropertyChanged(nameof(CanStopRun));
                (RunScriptCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                StopRunCommand.NotifyCanExecuteChanged();
                RaiseOutputPresentationChanged();
            }
        }
    }

    public bool CanRun => !IsRunning;
    public bool CanStopRun => IsRunning;
    public bool HasOutputLines => OutputLines.Count > 0;
    public bool IsOutputEmptyStateVisible => !HasOutputLines;
    public string OutputLineCountText => OutputLines.Count == 1 ? "1 output line" : $"{OutputLines.Count} output lines";
    public string OutputEmptyStateTitle => IsRunning
        ? "RunMat output is streaming"
        : _hasRunAttempt
            ? "Latest run produced no output"
            : "No output captured yet";
    public string OutputEmptyStateDetail => IsRunning
        ? "Stdout and stderr will appear here as RunMat emits lines."
        : _hasRunAttempt
            ? "The run finished without stdout or stderr. Check Last Run and resolved executable before rerunning."
            : "Run a script to capture stdout, stderr, and execution diagnostics in this panel.";

    public IAsyncRelayCommand InitializeCommand { get; }
    public IAsyncRelayCommand RefreshScriptsCommand { get; }
    public IAsyncRelayCommand SaveScriptCommand { get; }
    public IAsyncRelayCommand RunScriptCommand { get; }
    public IAsyncRelayCommand LoadSelectedScriptCommand { get; }
    public IRelayCommand NewScriptCommand { get; }
    public IRelayCommand StopRunCommand { get; }

    public RunMatViewModel(RunMatService runMatService)
    {
        _runMatService = runMatService;
        InitializeCommand = new AsyncRelayCommand(InitializeAsync);
        RefreshScriptsCommand = new AsyncRelayCommand(RefreshScriptsAsync);
        SaveScriptCommand = new AsyncRelayCommand(SaveCurrentScriptAsync);
        RunScriptCommand = new AsyncRelayCommand(RunScriptAsync, () => CanRun);
        LoadSelectedScriptCommand = new AsyncRelayCommand(LoadSelectedScriptAsync);
        NewScriptCommand = new RelayCommand(NewScript);
        StopRunCommand = new RelayCommand(StopRun, () => CanStopRun);

        OutputLines.CollectionChanged += (_, _) => RaiseOutputPresentationChanged();
    }

    public async Task InitializeAsync()
    {
        var settings = await _runMatService.GetSettingsAsync();
        var status = await _runMatService.GetStatusAsync();

        ExecutablePath = settings.ExecutablePath ?? string.Empty;
        WorkingDirectory = settings.WorkingDirectory;
        AdditionalArguments = settings.AdditionalArguments;
        ServiceStatus = status.StatusMessage;
        ResolvedExecutablePath = status.ResolvedExecutablePath ?? "Not resolved";

        await RefreshScriptsAsync();

        if (!string.IsNullOrWhiteSpace(settings.LastScriptPath) && Scripts.Any(script => string.Equals(script.Path, settings.LastScriptPath, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedScript = Scripts.First(script => string.Equals(script.Path, settings.LastScriptPath, StringComparison.OrdinalIgnoreCase));
            await LoadSelectedScriptAsync();
        }
        else if (Scripts.Count > 0)
        {
            SelectedScript = Scripts[0];
            await LoadSelectedScriptAsync();
        }
        else
        {
            NewScript();
        }

        StatusText = "RunMat Lab ready.";
    }

    private async Task RefreshScriptsAsync()
    {
        var scripts = await _runMatService.GetScriptsAsync();
        Scripts.Clear();
        foreach (var script in scripts)
        {
            Scripts.Add(script);
        }
    }

    private async Task LoadSelectedScriptAsync()
    {
        if (SelectedScript is null)
        {
            return;
        }

        ScriptName = SelectedScript.Name;
        ScriptSource = await _runMatService.LoadScriptAsync(SelectedScript.Path);
        StatusText = $"Loaded {SelectedScript.Name}.";
    }

    private async Task SaveCurrentScriptAsync()
    {
        var scriptPath = await _runMatService.SaveScriptAsync(ScriptName, ScriptSource);
        StatusText = $"Saved {Path.GetFileName(scriptPath)}.";
        await RefreshScriptsAsync();
        SelectedScript = Scripts.FirstOrDefault(script => string.Equals(script.Path, scriptPath, StringComparison.OrdinalIgnoreCase));

        await _runMatService.SaveSettingsAsync(new RunMatSettings
        {
            ExecutablePath = string.IsNullOrWhiteSpace(ExecutablePath) ? null : ExecutablePath,
            WorkingDirectory = WorkingDirectory,
            LastScriptPath = scriptPath,
            AdditionalArguments = AdditionalArguments
        });
    }

    private async Task RunScriptAsync()
    {
        OutputLines.Clear();
        _hasRunAttempt = true;
        IsRunning = true;
        StatusText = "Running script...";
        LastRunSummary = "Run in progress...";
        _runCts = new CancellationTokenSource();

        var progress = new Progress<RunMatOutputLine>(line => OutputLines.Add(line));
        var result = await _runMatService.ExecuteAsync(
            new RunMatExecutionRequest
            {
                ScriptName = ScriptName,
                ScriptSource = ScriptSource,
                ExecutablePath = string.IsNullOrWhiteSpace(ExecutablePath) ? null : ExecutablePath,
                WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectory) ? null : WorkingDirectory,
                AdditionalArguments = AdditionalArguments,
                PersistScript = true
            },
            progress,
            _runCts.Token);

        IsRunning = false;
        ResolvedExecutablePath = result.ExecutablePath ?? "Not resolved";
        StatusText = result.Success
            ? "Run completed successfully."
            : result.WasCancelled
                ? "Run cancelled."
                : result.ErrorMessage;
        LastRunSummary = result.WasCancelled
            ? $"Cancelled after {result.Duration.TotalSeconds:F1}s."
            : result.Success
                ? $"Exit code {result.ExitCode} in {result.Duration.TotalSeconds:F1}s."
                : $"{result.ErrorMessage} ({result.Duration.TotalSeconds:F1}s)";

        await RefreshScriptsAsync();
        SelectedScript = Scripts.FirstOrDefault(script => string.Equals(script.Path, result.ScriptPath, StringComparison.OrdinalIgnoreCase));
        ServiceStatus = string.IsNullOrWhiteSpace(result.ExecutablePath)
            ? "RunMat executable not found."
            : $"RunMat resolved at {result.ExecutablePath}";
    }

    private void NewScript()
    {
        SelectedScript = null;
        ScriptName = $"scratch_{DateTime.Now:HHmmss}.m";
        ScriptSource = """
        x = linspace(0, 4*pi, 400);
        y = sin(x) .* exp(-x / 12);
        plot(x, y);
        fprintf('mean(y)=%.6f\n', mean(y));
        """;
        StatusText = "Created new scratch script.";
    }

    private void StopRun()
    {
        if (!IsRunning)
            return;

        StatusText = "Cancellation requested.";
        LastRunSummary = "Cancellation requested.";
        _runCts?.Cancel();
    }

    private void RaiseOutputPresentationChanged()
    {
        RaisePropertyChanged(nameof(HasOutputLines));
        RaisePropertyChanged(nameof(IsOutputEmptyStateVisible));
        RaisePropertyChanged(nameof(OutputLineCountText));
        RaisePropertyChanged(nameof(OutputEmptyStateTitle));
        RaisePropertyChanged(nameof(OutputEmptyStateDetail));
    }

    public void Dispose()
    {
        _runCts?.Cancel();
        _runCts?.Dispose();
    }
}
