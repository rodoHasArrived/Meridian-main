using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Wpf.Services;

public enum RunMatOutputKind : byte
{
    StdOut,
    StdErr,
    Status
}

public sealed record RunMatOutputLine(DateTime TimestampUtc, RunMatOutputKind Kind, string Text);

public sealed class RunMatScriptDocument
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public DateTime LastWriteTimeUtc { get; init; }
}

public sealed class RunMatSettings
{
    public string? ExecutablePath { get; set; }
    public string WorkingDirectory { get; set; } = string.Empty;
    public string? LastScriptPath { get; set; }
    public string AdditionalArguments { get; set; } = string.Empty;
}

public sealed class RunMatExecutionRequest
{
    public string ScriptName { get; init; } = "scratch.m";
    public string ScriptSource { get; init; } = string.Empty;
    public string? ScriptPath { get; init; }
    public string? ExecutablePath { get; init; }
    public string? WorkingDirectory { get; init; }
    public string AdditionalArguments { get; init; } = string.Empty;
    public bool PersistScript { get; init; } = true;
}

public sealed class RunMatExecutionResult
{
    public bool Success { get; init; }
    public bool WasCancelled { get; init; }
    public int? ExitCode { get; init; }
    public string ScriptPath { get; init; } = string.Empty;
    public string? ExecutablePath { get; init; }
    public string WorkingDirectory { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public IReadOnlyList<RunMatOutputLine> Output { get; init; } = Array.Empty<RunMatOutputLine>();

    public static RunMatExecutionResult Failure(string message, string scriptPath, string workingDirectory, string? executablePath = null) => new()
    {
        Success = false,
        ErrorMessage = message,
        ScriptPath = scriptPath,
        WorkingDirectory = workingDirectory,
        ExecutablePath = executablePath
    };
}

public sealed class RunMatStatus
{
    public bool IsInstalled { get; init; }
    public string? ResolvedExecutablePath { get; init; }
    public string ScriptsDirectory { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public string StatusMessage { get; init; } = string.Empty;
}

/// <summary>
/// Manages RunMat script files, persisted settings, and process-backed execution for WPF research workflows.
/// </summary>
public sealed class RunMatService
{
    private static readonly Lazy<RunMatService> _instance = new(() => new RunMatService());
    private readonly string _rootDirectory;
    private readonly string _scriptsDirectory;
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private bool _initialized;

    public static RunMatService Instance => _instance.Value;

    public RunMatService(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian",
            "RunMat");
        _scriptsDirectory = Path.Combine(_rootDirectory, "scripts");
        _settingsPath = Path.Combine(_rootDirectory, "runmat-settings.json");
    }

    public string ScriptsDirectory => _scriptsDirectory;

    public async Task<RunMatStatus> GetStatusAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var settings = await GetSettingsAsync(ct);
        var resolvedPath = ResolveExecutablePath(settings.ExecutablePath);

        return new RunMatStatus
        {
            IsInstalled = !string.IsNullOrWhiteSpace(resolvedPath),
            ResolvedExecutablePath = resolvedPath,
            ScriptsDirectory = _scriptsDirectory,
            WorkingDirectory = GetEffectiveWorkingDirectory(settings),
            StatusMessage = string.IsNullOrWhiteSpace(resolvedPath)
                ? "RunMat executable not found. Set a path or install `runmat` on PATH."
                : $"RunMat resolved at {resolvedPath}"
        };
    }

    public async Task<RunMatSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        if (!File.Exists(_settingsPath))
        {
            return CreateDefaultSettings();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_settingsPath, ct);
            return JsonSerializer.Deserialize<RunMatSettings>(json, _jsonOptions) ?? CreateDefaultSettings();
        }
        catch
        {
            return CreateDefaultSettings();
        }
    }

    public async Task SaveSettingsAsync(RunMatSettings settings, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        settings.WorkingDirectory = GetEffectiveWorkingDirectory(settings);
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json, ct);
    }

    public async Task<IReadOnlyList<RunMatScriptDocument>> GetScriptsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        return Directory.GetFiles(_scriptsDirectory, "*.m", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => new RunMatScriptDocument
            {
                Name = file.Name,
                Path = file.FullName,
                LastWriteTimeUtc = file.LastWriteTimeUtc
            })
            .ToArray();
    }

    public async Task<string> LoadScriptAsync(string path, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        return await File.ReadAllTextAsync(path, ct);
    }

    public async Task<string> SaveScriptAsync(string scriptName, string source, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var sanitizedName = SanitizeScriptName(scriptName);
        var scriptPath = Path.Combine(_scriptsDirectory, sanitizedName);
        await File.WriteAllTextAsync(scriptPath, source ?? string.Empty, ct);

        var settings = await GetSettingsAsync(ct);
        settings.LastScriptPath = scriptPath;
        await SaveSettingsAsync(settings, ct);

        return scriptPath;
    }

    public async Task<RunMatExecutionResult> ExecuteAsync(
        RunMatExecutionRequest request,
        IProgress<RunMatOutputLine>? progress = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await _executionLock.WaitAsync(ct);

        var startedAtUtc = DateTime.UtcNow;
        var emittedLines = new Collection<RunMatOutputLine>();

        try
        {
            var settings = await GetSettingsAsync(ct);
            var resolvedExecutablePath = ResolveExecutablePath(request.ExecutablePath ?? settings.ExecutablePath);
            var workingDirectory = !string.IsNullOrWhiteSpace(request.WorkingDirectory)
                ? request.WorkingDirectory!
                : GetEffectiveWorkingDirectory(settings);

            Directory.CreateDirectory(workingDirectory);

            var scriptPath = await PrepareScriptAsync(request, ct);
            if (string.IsNullOrWhiteSpace(resolvedExecutablePath))
            {
                return RunMatExecutionResult.Failure(
                    "RunMat executable was not found. Set the executable path in RunMat Lab or install `runmat` on PATH.",
                    scriptPath,
                    workingDirectory);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = resolvedExecutablePath,
                Arguments = BuildArguments(scriptPath, request.AdditionalArguments),
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.Environment["MDC_RUNMAT_HOST"] = "Meridian";

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!process.Start())
            {
                return RunMatExecutionResult.Failure("Failed to start the RunMat process.", scriptPath, workingDirectory, resolvedExecutablePath);
            }

            EmitLine(progress, emittedLines, RunMatOutputKind.Status, $"Started RunMat with {Path.GetFileName(scriptPath)}.");

            using var cancellationRegistration = ct.Register(() => TryKillProcess(process));

            var stdoutTask = PumpStreamAsync(process.StandardOutput, RunMatOutputKind.StdOut, progress, emittedLines);
            var stderrTask = PumpStreamAsync(process.StandardError, RunMatOutputKind.StdErr, progress, emittedLines);
            var exitTask = process.WaitForExitAsync();
            var cancelTask = Task.Delay(Timeout.Infinite, ct);
            var completedTask = await Task.WhenAny(exitTask, cancelTask);
            var wasCancelled = completedTask == cancelTask;

            if (wasCancelled)
            {
                TryKillProcess(process);
                await exitTask;
            }

            await Task.WhenAll(stdoutTask, stderrTask);

            var updatedSettings = await GetSettingsAsync(CancellationToken.None);
            updatedSettings.ExecutablePath = request.ExecutablePath ?? settings.ExecutablePath;
            updatedSettings.WorkingDirectory = workingDirectory;
            updatedSettings.LastScriptPath = scriptPath;
            updatedSettings.AdditionalArguments = request.AdditionalArguments;
            await SaveSettingsAsync(updatedSettings, CancellationToken.None);

            var duration = DateTime.UtcNow - startedAtUtc;
            EmitLine(
                progress,
                emittedLines,
                RunMatOutputKind.Status,
                wasCancelled
                    ? "Run cancelled."
                    : $"Run completed with exit code {process.ExitCode}.");

            return new RunMatExecutionResult
            {
                Success = !wasCancelled && process.ExitCode == 0,
                WasCancelled = wasCancelled,
                ExitCode = process.ExitCode,
                ScriptPath = scriptPath,
                ExecutablePath = resolvedExecutablePath,
                WorkingDirectory = workingDirectory,
                Duration = duration,
                ErrorMessage = !wasCancelled && process.ExitCode != 0
                    ? $"RunMat exited with code {process.ExitCode}."
                    : string.Empty,
                Output = emittedLines.ToArray()
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return RunMatExecutionResult.Failure(
                $"RunMat execution failed: {ex.Message}",
                request.ScriptPath ?? string.Empty,
                request.WorkingDirectory ?? _scriptsDirectory,
                request.ExecutablePath);
        }
        finally
        {
            _executionLock.Release();
        }
    }

    public string? ResolveExecutablePath(string? preferredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath))
        {
            return preferredPath;
        }

        var configuredPath = Environment.GetEnvironmentVariable("MDC_RUNMAT_PATH", EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable("MDC_RUNMAT_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var segment in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var exeCandidate = Path.Combine(segment, "runmat.exe");
            if (File.Exists(exeCandidate))
            {
                return exeCandidate;
            }

            var bareCandidate = Path.Combine(segment, "runmat");
            if (File.Exists(bareCandidate))
            {
                return bareCandidate;
            }
        }

        return null;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
        {
            return;
        }

        Directory.CreateDirectory(_rootDirectory);
        Directory.CreateDirectory(_scriptsDirectory);

        if (!File.Exists(_settingsPath))
        {
            var json = JsonSerializer.Serialize(CreateDefaultSettings(), _jsonOptions);
            await File.WriteAllTextAsync(_settingsPath, json, ct);
        }

        if (!Directory.EnumerateFiles(_scriptsDirectory, "*.m").Any())
        {
            await File.WriteAllTextAsync(
                Path.Combine(_scriptsDirectory, "welcome_runmat.m"),
                GetWelcomeScript(),
                ct);
        }

        _initialized = true;
    }

    private static string BuildArguments(string scriptPath, string additionalArguments)
    {
        var quotedPath = $"\"{scriptPath}\"";
        return string.IsNullOrWhiteSpace(additionalArguments)
            ? quotedPath
            : $"{quotedPath} {additionalArguments}";
    }

    private async Task<string> PrepareScriptAsync(RunMatExecutionRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.ScriptPath) && File.Exists(request.ScriptPath))
        {
            return request.ScriptPath!;
        }

        if (request.PersistScript)
        {
            return await SaveScriptAsync(request.ScriptName, request.ScriptSource, ct);
        }

        var tempName = $"scratch_{DateTime.UtcNow:yyyyMMdd_HHmmss}.m";
        var tempPath = Path.Combine(_scriptsDirectory, tempName);
        await File.WriteAllTextAsync(tempPath, request.ScriptSource ?? string.Empty, ct);
        return tempPath;
    }

    private static async Task PumpStreamAsync(
        StreamReader reader,
        RunMatOutputKind kind,
        IProgress<RunMatOutputLine>? progress,
        ICollection<RunMatOutputLine> sink)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            EmitLine(progress, sink, kind, line);
        }
    }

    private static void EmitLine(
        IProgress<RunMatOutputLine>? progress,
        ICollection<RunMatOutputLine> sink,
        RunMatOutputKind kind,
        string text)
    {
        var line = new RunMatOutputLine(DateTime.UtcNow, kind, text);
        sink.Add(line);
        progress?.Report(line);
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cancellation.
        }
    }

    private RunMatSettings CreateDefaultSettings() => new()
    {
        WorkingDirectory = _scriptsDirectory
    };

    private string GetEffectiveWorkingDirectory(RunMatSettings settings) =>
        string.IsNullOrWhiteSpace(settings.WorkingDirectory)
            ? _scriptsDirectory
            : settings.WorkingDirectory;

    private static string SanitizeScriptName(string scriptName)
    {
        var name = string.IsNullOrWhiteSpace(scriptName) ? "scratch.m" : scriptName.Trim();
        if (!name.EndsWith(".m", StringComparison.OrdinalIgnoreCase))
        {
            name += ".m";
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidChar, '_');
        }

        return name;
    }

    private static string GetWelcomeScript() =>
        """
        x = linspace(0, 8*pi, 800);
        y = sin(x) .* exp(-x / 18);
        plot(x, y);
        fprintf('samples=%d\n', length(x));
        fprintf('mean(y)=%.6f\n', mean(y));
        """;
}
