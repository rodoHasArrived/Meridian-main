using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using Meridian.Contracts.Lifecycle;

namespace Meridian.LifecycleSupervisor;

internal sealed record LifecycleSupervisorPreflightResult(bool Success, string Message);

internal static class LifecycleSupervisorPreflight
{
    public static LifecycleSupervisorPreflightResult Evaluate(LifecycleSupervisorConfiguration configuration)
    {
        var failures = new List<string>();
        if (!File.Exists(configuration.HostPath))
            failures.Add($"host executable is missing at {configuration.HostPath}");

        try
        {
            Directory.CreateDirectory(configuration.DataRoot);
            var probe = Path.Combine(configuration.DataRoot, $".lifecycle-write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            failures.Add($"data root is not writable ({ex.GetType().Name})");
        }

        if (configuration.Manifest.DatabaseMode == LifecycleDatabaseManagementMode.Dedicated)
        {
            var bin = configuration.ResolvePostgreSqlBinPath();
            if (string.IsNullOrWhiteSpace(ResolveTool(bin, "postgres.exe")))
                failures.Add("postgres.exe was not found; set postgreSqlBinPath or MDC_POSTGRES_HOME");
            if (string.IsNullOrWhiteSpace(ResolveTool(bin, "pg_ctl.exe")))
                failures.Add("pg_ctl.exe was not found; set postgreSqlBinPath or MDC_POSTGRES_HOME");
            if (string.IsNullOrWhiteSpace(ResolveTool(bin, "initdb.exe")))
                failures.Add("initdb.exe was not found; set postgreSqlBinPath or MDC_POSTGRES_HOME");
        }
        else
        {
            var variable = configuration.Manifest.ExternalConnectionStringEnvironmentVariable;
            if (string.IsNullOrWhiteSpace(variable))
                failures.Add("external database mode requires externalConnectionStringEnvironmentVariable");
            else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
                failures.Add($"external database environment variable {variable} is not set");
        }

        return failures.Count == 0
            ? new LifecycleSupervisorPreflightResult(true, "Lifecycle supervisor preflight passed.")
            : new LifecycleSupervisorPreflightResult(false, "Lifecycle supervisor preflight failed: " + string.Join("; ", failures));
    }

    internal static string? ResolveTool(string? binPath, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(binPath))
        {
            var candidate = Path.Combine(binPath, fileName);
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        }

        foreach (var segment in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(segment, fileName);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch (ArgumentException)
            {
            }
        }
        return null;
    }
}

internal sealed class LifecycleDatabaseController
{
    private readonly LifecycleSupervisorConfiguration _configuration;
    private readonly string _dataDirectory;
    private readonly string _logPath;
    private LifecycleDatabaseIdentityDto? _identity;
    private string? _password;

    public LifecycleDatabaseController(LifecycleSupervisorConfiguration configuration)
    {
        _configuration = configuration;
        _dataDirectory = Path.Combine(configuration.DataRoot, "postgresql", "data");
        _logPath = Path.Combine(configuration.DataRoot, "postgresql", "postgresql.log");
    }

    public async Task<LifecycleDatabaseIdentityDto> StartAsync(CancellationToken ct)
    {
        if (_configuration.Manifest.DatabaseMode == LifecycleDatabaseManagementMode.External)
        {
            _identity = new LifecycleDatabaseIdentityDto
            {
                Mode = LifecycleDatabaseManagementMode.External,
                Port = _configuration.Manifest.DatabasePort
            };
            return _identity;
        }

        var binPath = _configuration.ResolvePostgreSqlBinPath();
        var pgCtl = LifecycleSupervisorPreflight.ResolveTool(binPath, "pg_ctl.exe")
                    ?? throw new FileNotFoundException("pg_ctl.exe was not found.");
        var initDb = LifecycleSupervisorPreflight.ResolveTool(binPath, "initdb.exe")
                     ?? throw new FileNotFoundException("initdb.exe was not found.");
        Directory.CreateDirectory(Path.GetDirectoryName(_dataDirectory)!);
        GrantCurrentUserInheritableFullControl(Path.GetDirectoryName(_dataDirectory)!);
        _password = LoadOrCreateDatabasePassword();

        var adopted = TryReadOwnedIdentity(pgCtl);
        if (adopted is not null)
        {
            _identity = adopted;
            return adopted;
        }

        if (!File.Exists(Path.Combine(_dataDirectory, "PG_VERSION")))
        {
            if (Directory.Exists(_dataDirectory) &&
                Directory.EnumerateFileSystemEntries(_dataDirectory).Any())
            {
                throw new InvalidOperationException(
                    "The dedicated PostgreSQL data directory is incomplete. Preserve it for recovery or move it aside before retrying initialization.");
            }

            var initializingDirectory = $"{_dataDirectory}.initializing-{Guid.NewGuid():N}";
            Directory.CreateDirectory(initializingDirectory);
            var passwordFile = Path.Combine(
                _configuration.ServiceRoot,
                $".postgresql-password-{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(passwordFile, _password, ct).ConfigureAwait(false);
                await RunToolAsync(
                    initDb,
                    [
                        "-D", initializingDirectory,
                        "--encoding=UTF8",
                        "--no-locale",
                        "--username", Environment.UserName,
                        "--pwfile", passwordFile,
                        "--auth=scram-sha-256"
                    ],
                    TimeSpan.FromSeconds(_configuration.Manifest.DatabaseTimeoutSeconds),
                    ct).ConfigureAwait(false);
                if (Directory.Exists(_dataDirectory))
                    Directory.Delete(_dataDirectory);
                Directory.Move(initializingDirectory, _dataDirectory);
            }
            finally
            {
                if (File.Exists(passwordFile))
                    File.Delete(passwordFile);
                if (Directory.Exists(initializingDirectory))
                    Directory.Delete(initializingDirectory, recursive: true);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        try
        {
            await RunToolAsync(
                pgCtl,
                [
                    "start", "-D", _dataDirectory, "-l", _logPath, "-w", "-t",
                    _configuration.Manifest.DatabaseTimeoutSeconds.ToString(), "-o",
                    $"-h 127.0.0.1 -p {_configuration.Manifest.DatabasePort}"
                ],
                TimeSpan.FromSeconds(_configuration.Manifest.DatabaseTimeoutSeconds + 5),
                ct).ConfigureAwait(false);

            _identity = TryReadOwnedIdentity(pgCtl)
                        ?? throw new InvalidOperationException("PostgreSQL started without a valid owned process identity.");
            return _identity;
        }
        catch
        {
            // pg_ctl can be cancelled after postgres has detached. Retain any provable identity so
            // the outer session cleanup can still stop exactly that database process.
            _identity = TryReadOwnedIdentity(pgCtl);
            throw;
        }
    }

    public async Task<(LifecycleShutdownOutcome Outcome, bool Forced)> StopAsync(CancellationToken ct)
    {
        if (_configuration.Manifest.DatabaseMode == LifecycleDatabaseManagementMode.External)
            return (LifecycleShutdownOutcome.Succeeded, false);
        if (_identity is null)
            return (LifecycleShutdownOutcome.Succeeded, false);

        var pgCtl = LifecycleSupervisorPreflight.ResolveTool(
            _configuration.ResolvePostgreSqlBinPath(),
            "pg_ctl.exe");
        if (pgCtl is not null)
        {
            try
            {
                await RunToolAsync(
                    pgCtl,
                    [
                        "stop", "-D", _dataDirectory, "-m", "fast", "-w", "-t",
                        _configuration.Manifest.DatabaseTimeoutSeconds.ToString()
                    ],
                    TimeSpan.FromSeconds(_configuration.Manifest.DatabaseTimeoutSeconds + 5),
                    ct).ConfigureAwait(false);
                return (LifecycleShutdownOutcome.Succeeded, false);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
            }
        }

        if (!TryValidateOwnedProcess(_identity, requireDatabaseMetadata: true, out var process))
            return (LifecycleShutdownOutcome.Failed, false);
        using (process)
        {
            process!.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        return (LifecycleShutdownOutcome.Forced, true);
    }

    public string BuildHostConnectionString()
    {
        if (_configuration.Manifest.DatabaseMode == LifecycleDatabaseManagementMode.External)
        {
            var variable = _configuration.Manifest.ExternalConnectionStringEnvironmentVariable;
            var external = string.IsNullOrWhiteSpace(variable)
                ? null
                : Environment.GetEnvironmentVariable(variable);
            return string.IsNullOrWhiteSpace(external)
                ? throw new InvalidOperationException("The external PostgreSQL connection string is unavailable.")
                : external;
        }

        var password = _password ?? LifecycleProtectedSecretStore.Read(_configuration.DatabaseSecretPath);
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("The dedicated PostgreSQL credential is unavailable.");
        return $"Host=127.0.0.1;Port={_configuration.Manifest.DatabasePort};Database=postgres;Username={QuoteConnectionStringValue(Environment.UserName)};Password={QuoteConnectionStringValue(password)};Pooling=true";
    }

    private static string QuoteConnectionStringValue(string value)
        => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private string LoadOrCreateDatabasePassword()
    {
        var existing = LifecycleProtectedSecretStore.Read(_configuration.DatabaseSecretPath);
        if (!string.IsNullOrWhiteSpace(existing))
            return existing;
        if (File.Exists(Path.Combine(_dataDirectory, "PG_VERSION")))
        {
            throw new InvalidOperationException(
                "The dedicated PostgreSQL cluster exists but its DPAPI credential is unavailable. Restore the credential or recover the data before starting Meridian.");
        }

        var created = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        LifecycleProtectedSecretStore.Write(_configuration.DatabaseSecretPath, created);
        return created;
    }

    private LifecycleDatabaseIdentityDto? TryReadOwnedIdentity(string pgCtlPath)
    {
        var pidFile = Path.Combine(_dataDirectory, "postmaster.pid");
        if (!File.Exists(pidFile))
            return null;
        try
        {
            var lines = File.ReadAllLines(pidFile);
            if (lines.Length < 4 ||
                !int.TryParse(lines[0], out var pid) ||
                !int.TryParse(lines[3], out var port) ||
                port != _configuration.Manifest.DatabasePort ||
                !string.Equals(Path.GetFullPath(lines[1]), Path.GetFullPath(_dataDirectory), StringComparison.OrdinalIgnoreCase))
                return null;
            using var process = Process.GetProcessById(pid);
            var expectedExecutable = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(pgCtlPath)!, "postgres.exe"));
            var actualExecutable = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(actualExecutable) ||
                !string.Equals(
                    Path.GetFullPath(actualExecutable),
                    expectedExecutable,
                    StringComparison.OrdinalIgnoreCase))
                return null;
            return new LifecycleDatabaseIdentityDto
            {
                Mode = LifecycleDatabaseManagementMode.Dedicated,
                ProcessId = pid,
                ExecutablePath = expectedExecutable,
                StartedAtUtc = new DateTimeOffset(process.StartTime.ToUniversalTime()),
                DataDirectory = _dataDirectory,
                Port = port
            };
        }
        catch
        {
            return null;
        }
    }

    private bool TryValidateOwnedProcess(
        LifecycleDatabaseIdentityDto identity,
        bool requireDatabaseMetadata,
        out Process? process)
    {
        process = null;
        if (identity.ProcessId is not { } pid ||
            identity.StartedAtUtc is not { } startedAt ||
            string.IsNullOrWhiteSpace(identity.ExecutablePath))
            return false;
        try
        {
            process = Process.GetProcessById(pid);
            var currentPath = process.MainModule?.FileName;
            var currentStart = new DateTimeOffset(process.StartTime.ToUniversalTime());
            if (!string.Equals(Path.GetFullPath(currentPath ?? string.Empty), Path.GetFullPath(identity.ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
                Math.Abs((currentStart - startedAt).TotalSeconds) > 2)
            {
                process.Dispose();
                process = null;
                return false;
            }
            if (requireDatabaseMetadata)
            {
                var refreshed = TryReadOwnedIdentity(Path.Combine(Path.GetDirectoryName(identity.ExecutablePath)!, "pg_ctl.exe"));
                if (refreshed?.ProcessId != pid ||
                    refreshed.Port != identity.Port ||
                    !string.Equals(refreshed.DataDirectory, identity.DataDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    process.Dispose();
                    process = null;
                    return false;
                }
            }
            return true;
        }
        catch
        {
            process?.Dispose();
            process = null;
            return false;
        }
    }

    internal static async Task RunToolAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(ct);
        var standardError = process.StandardError.ReadToEndAsync(ct);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await TerminateToolProcessAsync(process).ConfigureAwait(false);
            throw new TimeoutException($"{Path.GetFileName(executable)} exceeded its lifecycle deadline.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await TerminateToolProcessAsync(process).ConfigureAwait(false);
            throw;
        }
        // pg_ctl start hands the redirected pipe write-handles to the postgres daemon it
        // spawns, so a full drain of the tool's stdout/stderr blocks until the daemon
        // exits — the 2026-07-28 hosted smoke receipts showed startup frozen here for the
        // database's whole lifetime. The tool's own output is flushed before it exits;
        // grace-bound the drain and fall back to whatever arrived. A failing tool has no
        // surviving child, closes its pipes, and still delivers full diagnostics.
        var output = await DrainWithGraceAsync(standardOutput).ConfigureAwait(false);
        var error = await DrainWithGraceAsync(standardError).ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{Path.GetFileName(executable)} failed with exit code {process.ExitCode}: {Sanitize(error, output)}");
    }

    private static async Task<string> DrainWithGraceAsync(Task<string> drain)
    {
        var completed = await Task.WhenAny(drain, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
        if (completed == drain)
            return await drain.ConfigureAwait(false);
        _ = drain.ContinueWith(
            static abandoned => _ = abandoned.Exception,
            TaskContinuationOptions.OnlyOnFaulted);
        return string.Empty;
    }

    private static async Task TerminateToolProcessAsync(Process process)
    {
        if (process.HasExited)
            return;
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    // On elevated Windows contexts (CI runners, admin-run installers) initdb.exe re-executes
    // itself with a restricted token that drops the Administrators group, so a directory
    // reachable only through group ACEs fails initdb's permission fixup with "could not
    // change permissions ... Permission denied". An explicit current-user ACE survives token
    // restriction, and inheritance covers the initializing directory, the renamed data
    // directory, and the server log. Best-effort: when the grant cannot be applied, initdb's
    // own diagnostic remains the authoritative failure.
    internal static void GrantCurrentUserInheritableFullControl(string directory)
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.User is null)
                return;
            var info = new DirectoryInfo(directory);
            var security = info.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(
                identity.User,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            info.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or PlatformNotSupportedException
                                       or IdentityNotMappedException)
        {
        }
    }

    private static string Sanitize(params string[] values)
    {
        var text = string.Join(" ", values)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return text.Length > 240 ? text[..240] : text;
    }
}
