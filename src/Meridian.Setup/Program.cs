using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Meridian.Setup;

internal static class Program
{
    private const string ProductVersion = "1.0.0";

    [STAThread]
    private static int Main(string[] args)
    {
        var installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Meridian");
        if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase)) return Uninstall(installRoot);
        try
        {
            Directory.CreateDirectory(installRoot);
            ExtractPayload(installRoot);
            var installedSetup = Path.Combine(installRoot, "Meridian-Setup.exe");
            if (!string.Equals(Environment.ProcessPath, installedSetup, StringComparison.OrdinalIgnoreCase))
                File.Copy(Environment.ProcessPath!, installedSetup, true);
            RegisterProduct(installRoot);
            CreateStartMenuShortcut(installRoot);
            Process.Start(new ProcessStartInfo(Path.Combine(installRoot, "Meridian.exe")) { UseShellExecute = true });
            return 0;
        }
        catch (Exception ex)
        {
            Show($"Meridian setup could not finish.\n\n{ex.Message}");
            return 1;
        }
    }

    private static void ExtractPayload(string installRoot)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames().Where(name => name.StartsWith("payload/", StringComparison.Ordinal)).ToArray();
        if (resources.Length == 0) throw new InvalidOperationException("The signed installer does not contain a Meridian product payload.");
        var runtime = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        var prefix = $"payload/{runtime}/";
        foreach (var resource in resources.Where(name => name.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var relative = resource[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(installRoot, relative));
            if (!target.StartsWith(Path.GetFullPath(installRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid package path.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = assembly.GetManifestResourceStream(resource) ?? throw new InvalidDataException($"Missing package resource {resource}.");
            using var destination = File.Create(target);
            source.CopyTo(destination);
        }
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
            var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Meridian.url");
            if (File.Exists(shortcut)) File.Delete(shortcut);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Meridian", false);
            var cleanup = Path.Combine(Path.GetTempPath(), $"meridian-uninstall-{Guid.NewGuid():N}.cmd");
            File.WriteAllText(cleanup, $"@echo off\r\nping 127.0.0.1 -n 3 > nul\r\nrmdir /s /q \"{installRoot}\"\r\ndel /q \"%~f0\"\r\n");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cleanup}\"") { CreateNoWindow = true, UseShellExecute = false });
            return 0;
        }
        catch (Exception ex) { Show($"Meridian could not be removed. Your data was not changed.\n\n{ex.Message}"); return 1; }
    }

    private static void Show(string message) =>
        Process.Start(new ProcessStartInfo("mshta.exe", $"javascript:alert('{message.Replace("'", "").Replace("\n", "\\n")}');close()") { UseShellExecute = true });
}
