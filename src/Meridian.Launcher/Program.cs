using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meridian.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var supervisorPath = Path.Combine(AppContext.BaseDirectory, "Meridian.LifecycleSupervisor.exe");
        if (!File.Exists(supervisorPath))
        {
            MessageBox("Meridian needs repair because its lifecycle supervisor is missing.");
            return 2;
        }

        var start = new ProcessStartInfo(supervisorPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        if (args.Length == 0)
        {
            start.ArgumentList.Add("start");
        }
        else
        {
            foreach (var argument in args) start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
        return process is null ? 3 : 0;
    }

    private static void MessageBox(string message)
        => NativeMessageBox(IntPtr.Zero, message, "Meridian", 0x00000010);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int NativeMessageBox(IntPtr windowHandle, string text, string caption, uint type);
}
