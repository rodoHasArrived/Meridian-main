using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;

namespace Meridian.Tests.Scripts;

/// <summary>
/// Behavioral coverage for <c>build/scripts/recovery/invoke-production-recovery.ps1</c>. The
/// connection-string parser must hand pg_dump the exact Host/Port/Database/Username/Password from
/// the operator-supplied connection string: PowerShell adapts DbConnectionStringBuilder as a
/// dictionary, so a property-style ConnectionString assignment silently discards every component
/// and the backup fails closed with a misleading "must include Database and Username" error.
/// </summary>
public sealed class ProductionRecoveryScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-tests",
        "production-recovery-script",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetConnectionParts_UsesClrSetterNotDictionaryEntry()
    {
        var script = File.ReadAllText(ResolveScriptPath());

        script.Should().Contain(
            "$builder.set_ConnectionString($Value)",
            "the CLR setter must be invoked explicitly; property-style assignment on " +
            "DbConnectionStringBuilder creates a literal 'ConnectionString' dictionary key " +
            "and loses Database/Username");
        script.Should().NotContain(
            "$builder.ConnectionString =",
            "a property-style assignment would silently break connection-string parsing");
    }

    [Fact]
    public async Task BackupMode_HandsPgDumpEveryConnectionComponent()
    {
        var pwsh = ResolvePwsh();
        if (pwsh is null)
        {
            return;
        }

        Directory.CreateDirectory(_root);
        var dataRoot = Directory.CreateDirectory(Path.Combine(_root, "data-root")).FullName;
        await File.WriteAllTextAsync(Path.Combine(dataRoot, "probe.txt"), "durable-probe");
        var backupRoot = Path.Combine(_root, "backups");
        var evidenceDirectory = Directory.CreateDirectory(Path.Combine(_root, "stub-evidence")).FullName;
        var receiptPath = Path.Combine(_root, "backup-receipt.json");
        var stubPath = WritePgDumpStub(evidenceDirectory);
        var encryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var startInfo = new ProcessStartInfo
        {
            FileName = pwsh,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(ResolveScriptPath());
        startInfo.ArgumentList.Add("-Mode");
        startInfo.ArgumentList.Add("Backup");
        startInfo.ArgumentList.Add("-ConnectionString");
        startInfo.ArgumentList.Add(
            "Host=db.internal.example;Port=6543;Database=meridian_case;Username=svc_meridian;Password=recovery-secret");
        startInfo.ArgumentList.Add("-DataRoot");
        startInfo.ArgumentList.Add(dataRoot);
        startInfo.ArgumentList.Add("-BackupRoot");
        startInfo.ArgumentList.Add(backupRoot);
        startInfo.ArgumentList.Add("-EncryptionKeyBase64");
        startInfo.ArgumentList.Add(encryptionKey);
        startInfo.ArgumentList.Add("-PgDumpPath");
        startInfo.ArgumentList.Add(stubPath);
        startInfo.ArgumentList.Add("-ReceiptPath");
        startInfo.ArgumentList.Add(receiptPath);

        using var process = Process.Start(startInfo);
        process.Should().NotBeNull();
        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        process.ExitCode.Should().Be(0, $"backup must succeed; stdout: {stdout}; stderr: {stderr}");

        var recordedArguments = await File.ReadAllLinesAsync(Path.Combine(evidenceDirectory, "args.txt"));
        var arguments = string.Join(' ', recordedArguments);
        arguments.Should().Contain("--host db.internal.example");
        arguments.Should().Contain("--port 6543");
        arguments.Should().Contain("--username svc_meridian");
        arguments.Should().Contain("--dbname meridian_case");
        (await File.ReadAllTextAsync(Path.Combine(evidenceDirectory, "pgpassword.txt")))
            .Trim().Should().Be("recovery-secret");

        using var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(receiptPath));
        receipt.RootElement.GetProperty("status").GetString().Should().Be("passed");
        var backupPath = receipt.RootElement.GetProperty("backupPath").GetString();
        backupPath.Should().NotBeNullOrWhiteSpace();
        File.Exists(Path.Combine(backupPath!, "manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(backupPath!, "database.dump.enc")).Should().BeTrue();
        File.Exists(Path.Combine(backupPath!, "data-root.zip.enc")).Should().BeTrue();
    }

    private string WritePgDumpStub(string evidenceDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            var cmdPath = Path.Combine(_root, "pg_dump-stub.cmd");
            File.WriteAllText(
                cmdPath,
                "@echo off\r\n" +
                $"echo %*> \"{evidenceDirectory}\\args.txt\"\r\n" +
                $"echo %PGPASSWORD%> \"{evidenceDirectory}\\pgpassword.txt\"\r\n" +
                ":findfile\r\n" +
                "if \"%~1\"==\"\" exit /b 0\r\n" +
                "if \"%~1\"==\"--file\" (\r\n" +
                "  echo stub-dump> \"%~2\"\r\n" +
                "  exit /b 0\r\n" +
                ")\r\n" +
                "shift\r\n" +
                "goto findfile\r\n");
            return cmdPath;
        }

        var stubPath = Path.Combine(_root, "pg_dump-stub.sh");
        File.WriteAllText(
            stubPath,
            "#!/usr/bin/env bash\n" +
            "set -euo pipefail\n" +
            $"printf '%s\\n' \"$@\" > '{evidenceDirectory}/args.txt'\n" +
            $"printf '%s' \"${{PGPASSWORD:-}}\" > '{evidenceDirectory}/pgpassword.txt'\n" +
            "while [ $# -gt 0 ]; do\n" +
            "  if [ \"$1\" = '--file' ]; then\n" +
            "    printf 'stub-dump' > \"$2\"\n" +
            "    exit 0\n" +
            "  fi\n" +
            "  shift\n" +
            "done\n");
        File.SetUnixFileMode(
            stubPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return stubPath;
    }

    private static string ResolveScriptPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "build",
                "scripts",
                "recovery",
                "invoke-production-recovery.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "invoke-production-recovery.ps1 was not found above the test base directory.");
    }

    private static string? ResolvePwsh()
    {
        var executable = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(segment.Trim(), executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
