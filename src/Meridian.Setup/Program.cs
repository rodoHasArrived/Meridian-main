using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Meridian.Setup;

internal static class Program
{
    private const string SetupMutexName = @"Local\Meridian.Setup.Transaction";
    private static readonly string ProductVersion =
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--verify-payload", StringComparer.OrdinalIgnoreCase))
            return VerifyPayload(args);
        var installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Meridian");
        if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
            return Uninstall(installRoot);
        var detached = args.Contains("--detached", StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!detached && IsPathWithin(Environment.ProcessPath, installRoot))
            {
                return RelaunchDetached(args);
            }

            WaitForParentExit(args);
            using var setupMutex = new Mutex(initiallyOwned: false, SetupMutexName);
            var ownsMutex = false;
            try
            {
                try
                {
                    ownsMutex = setupMutex.WaitOne(TimeSpan.Zero);
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                }

                if (!ownsMutex)
                {
                    throw new InvalidOperationException("Another Meridian setup transaction is already running.");
                }

                InstallationTransaction.RecoverInterruptedPromotion(installRoot);
                InstallationTransaction.CleanupAbandonedStages(installRoot);
                StopOwnedRuntime(installRoot);

                var staged = StageAppendedPayload(installRoot);
                try
                {
                    InstallationTransaction.Promote(staged, installRoot);
                }
                finally
                {
                    if (Directory.Exists(staged.Path))
                    {
                        Directory.Delete(staged.Path, recursive: true);
                    }
                }

                RegisterProduct(installRoot);
                CreateStartMenuShortcut(installRoot);
                Process.Start(new ProcessStartInfo(Path.Combine(installRoot, "Meridian.exe")) { UseShellExecute = true });
                return 0;
            }
            finally
            {
                if (ownsMutex)
                {
                    setupMutex.ReleaseMutex();
                }
            }
        }
        catch (Exception ex)
        {
            Show($"Meridian setup could not finish.\n\n{ex.Message}");
            return 1;
        }
        finally
        {
            if (detached)
            {
                ScheduleDetachedCleanup();
            }
        }
    }

    private static StagedInstallation StageAppendedPayload(string installRoot)
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Setup could not resolve its executable path.");
        var runtime = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";

        using var package = PayloadPackage.Open(processPath);
        var payload = package.GetRuntimePayload(runtime);
        if (payload.Count == 0)
        {
            throw new InvalidOperationException($"The signed installer does not contain a {runtime} product payload.");
        }

        return InstallationTransaction.Stage(installRoot, ProductVersion, runtime, payload, processPath);
    }

    /// <summary>
    /// Reports whether this executable carries a readable payload for the requested runtimes.
    /// </summary>
    /// <remarks>
    /// The release build runs this against the executable it has just assembled, so the payload
    /// writer in <c>build-consumer-setup.ps1</c> is checked against <see cref="PayloadPackage"/> in
    /// the same job that produces the artifact. It is also the supported way for someone holding a
    /// download to check it before running it, so it never shows UI and never installs anything.
    /// </remarks>
    private static int VerifyPayload(string[] args)
    {
        try
        {
            var processPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Setup could not resolve its executable path.");
            using var package = PayloadPackage.Open(processPath);

            var expected = args
                .SkipWhile(argument => !argument.Equals("--verify-payload", StringComparison.OrdinalIgnoreCase))
                .Skip(1)
                .TakeWhile(argument => !argument.StartsWith("--", StringComparison.Ordinal))
                .ToArray();
            var present = package.GetRuntimes();
            IReadOnlyList<string> requested = expected.Length > 0 ? expected : present;
            foreach (var runtime in requested)
            {
                var files = package.GetRuntimePayload(runtime);
                if (files.Count == 0)
                {
                    throw new InvalidDataException(
                        $"The installer carries no {runtime} payload. Runtimes present: {(present.Count == 0 ? "(none)" : string.Join(", ", present))}.");
                }

                Console.Error.WriteLine($"verify-payload: {runtime}: {files.Count} file(s)");
            }

            if (present.Count == 0)
            {
                throw new InvalidDataException("The installer payload archive is empty.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"verify-payload: {ex.Message}");
            return 2;
        }
    }

    private static bool IsPathWithin(string? candidatePath, string directory)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        var candidate = Path.GetFullPath(candidatePath);
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static int RelaunchDetached(string[] args)
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Setup could not resolve its executable path.");
        var detachedDirectory = Path.Combine(
            Path.GetTempPath(),
            $"meridian-setup-detached-{Guid.NewGuid():N}");
        Directory.CreateDirectory(detachedDirectory);

        foreach (var source in Directory.EnumerateFiles(
                     AppContext.BaseDirectory,
                     "Meridian-Setup*",
                     SearchOption.TopDirectoryOnly))
        {
            File.Copy(source, Path.Combine(detachedDirectory, Path.GetFileName(source)), overwrite: false);
        }

        var detachedExecutable = Path.Combine(detachedDirectory, Path.GetFileName(processPath));
        if (!File.Exists(detachedExecutable))
        {
            File.Copy(processPath, detachedExecutable, overwrite: false);
        }

        var start = new ProcessStartInfo(detachedExecutable)
        {
            UseShellExecute = true,
            WorkingDirectory = detachedDirectory
        };
        foreach (var argument in args.Where(argument =>
                     !argument.Equals("--detached", StringComparison.OrdinalIgnoreCase)))
        {
            start.ArgumentList.Add(argument);
        }

        start.ArgumentList.Add("--detached");
        start.ArgumentList.Add("--parent-pid");
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _ = Process.Start(start)
            ?? throw new InvalidOperationException("Could not launch the detached Meridian setup transaction.");
        return 0;
    }

    private static void WaitForParentExit(string[] args)
    {
        var index = Array.FindIndex(args, argument =>
            argument.Equals("--parent-pid", StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }

        if (index + 1 >= args.Length ||
            !int.TryParse(
                args[index + 1],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var processId) ||
            processId <= 0 ||
            processId == Environment.ProcessId)
        {
            throw new ArgumentException("The detached setup parent process ID is invalid.", nameof(args));
        }

        try
        {
            using var parent = Process.GetProcessById(processId);
            if (!parent.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds))
            {
                throw new TimeoutException("The installed setup process did not exit before upgrade promotion.");
            }
        }
        catch (ArgumentException)
        {
            // The parent already exited between argument parsing and process lookup.
        }
    }

    private static void ScheduleDetachedCleanup()
    {
        var processPath = Environment.ProcessPath;
        var detachedDirectory = string.IsNullOrWhiteSpace(processPath)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(processPath));
        var expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (string.IsNullOrWhiteSpace(detachedDirectory) ||
            !detachedDirectory.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(detachedDirectory).StartsWith("meridian-setup-detached-", StringComparison.Ordinal))
        {
            return;
        }

        var cleanup = Path.Combine(Path.GetTempPath(), $"meridian-setup-cleanup-{Guid.NewGuid():N}.cmd");
        File.WriteAllText(
            cleanup,
            $"@echo off\r\nping 127.0.0.1 -n 3 > nul\r\nrmdir /s /q \"{detachedDirectory}\"\r\ndel /q \"%~f0\"\r\n");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cleanup}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }

    private static void RegisterProduct(string installRoot)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Meridian");
        key.SetValue("DisplayName", "Meridian");
        key.SetValue("DisplayVersion", ProductVersion);
        key.SetValue("Publisher", "Meridian");
        key.SetValue("InstallLocation", installRoot);
        key.SetValue("DisplayIcon", Path.Combine(installRoot, "Meridian.exe"));
        var installedSetup = Path.Combine(installRoot, "Meridian-Setup.exe");
        key.SetValue("UninstallString", $"\"{installedSetup}\" --uninstall");
        key.SetValue("ModifyPath", $"\"{installedSetup}\" --repair");
        key.SetValue("NoModify", 0, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 0, RegistryValueKind.DWord);
    }

    private static void CreateStartMenuShortcut(string installRoot)
    {
        var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        Directory.CreateDirectory(startMenu);
        var shortcut = Path.Combine(startMenu, "Meridian.url");
        File.WriteAllText(shortcut, $"[InternetShortcut]{Environment.NewLine}URL=file:///{Path.Combine(installRoot, "Meridian.exe").Replace('\\', '/')}{Environment.NewLine}IconFile={Path.Combine(installRoot, "Meridian.exe")}{Environment.NewLine}IconIndex=0");
    }

    private static int Uninstall(string installRoot)
    {
        try
        {
            StopOwnedRuntime(installRoot);
            InstallationTransaction.CleanupAbandonedStages(installRoot);
            var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Meridian.url");
            if (File.Exists(shortcut))
                File.Delete(shortcut);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Meridian", false);
            var rollback = InstallationTransaction.GetRollbackPath(installRoot);
            var cleanup = Path.Combine(Path.GetTempPath(), $"meridian-uninstall-{Guid.NewGuid():N}.cmd");
            File.WriteAllText(
                cleanup,
                $"@echo off\r\nping 127.0.0.1 -n 3 > nul\r\nrmdir /s /q \"{installRoot}\"\r\nrmdir /s /q \"{rollback}\"\r\ndel /q \"%~f0\"\r\n");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cleanup}\"") { CreateNoWindow = true, UseShellExecute = false });
            return 0;
        }
        catch (Exception ex) { Show($"Meridian could not be removed. Your data was not changed.\n\n{ex.Message}"); return 1; }
    }

    private static void Show(string message)
        => NativeMessageBox(IntPtr.Zero, message, "Meridian Setup", 0x00000010);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int NativeMessageBox(IntPtr windowHandle, string text, string caption, uint type);

    private static void StopOwnedRuntime(string installRoot)
    {
        var supervisor = Path.Combine(installRoot, "Meridian.LifecycleSupervisor.exe");
        if (!File.Exists(supervisor))
            return;

        var start = new ProcessStartInfo(supervisor)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = installRoot
        };
        start.ArgumentList.Add("stop");
        using var process = Process.Start(start);
        if (process is null)
            throw new InvalidOperationException("Could not request Meridian shutdown before setup.");
        if (!process.WaitForExit((int)TimeSpan.FromSeconds(140).TotalMilliseconds))
            throw new TimeoutException("Meridian did not stop before setup's file-replacement deadline.");
        if (process.ExitCode is not (0 or 3))
            throw new InvalidOperationException($"Meridian lifecycle shutdown failed with exit code {process.ExitCode}.");
    }
}
