using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Meridian.Wpf.Services;

/// <summary>
/// Surfaces backfill and collection progress on the Windows taskbar icon
/// using the ITaskbarList3 COM interface (Windows 7+).
/// Call <see cref="Initialize"/> once after <c>MainWindow.Loaded</c> fires.
/// All methods degrade gracefully when running headless or before initialization.
/// </summary>
public sealed class TaskbarProgressService
{
    private static readonly Lazy<TaskbarProgressService> _instance =
        new(() => new TaskbarProgressService());

    public static TaskbarProgressService Instance => _instance.Value;

    private ITaskbarList3? _taskbarList;
    private IntPtr _hwnd = IntPtr.Zero;

    private TaskbarProgressService()
    {
        try
        {
            _taskbarList = (ITaskbarList3)new TaskbarInstance();
            _taskbarList.HrInit();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TaskbarProgressService] COM initialization failed, disabling taskbar progress: {ex.Message}");
            _taskbarList = null;
        }
    }

    /// <summary>
    /// Captures the HWND of <paramref name="mainWindow"/> so subsequent calls
    /// can update the taskbar without touching the UI thread for handle resolution.
    /// Must be called after <c>MainWindow.Loaded</c>.
    /// </summary>
    public void Initialize(Window mainWindow)
    {
        try
        {
            _hwnd = new WindowInteropHelper(mainWindow).Handle;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TaskbarProgressService] Failed to capture HWND, disabling taskbar progress: {ex.Message}");
            _hwnd = IntPtr.Zero;
        }
    }

    /// <summary>Shows a green determinate progress bar at <paramref name="completed"/>/<paramref name="total"/>.</summary>
    public void SetNormal(ulong completed, ulong total)
    {
        Apply(hwnd =>
        {
            _taskbarList!.SetProgressState(hwnd, TaskbarProgressState.Normal);
            _taskbarList.SetProgressValue(hwnd, completed, total > 0 ? total : 1);
        });
    }

    /// <summary>Shows an animated indeterminate (pulse) bar.</summary>
    public void SetIndeterminate()
        => Apply(hwnd => _taskbarList!.SetProgressState(hwnd, TaskbarProgressState.Indeterminate));

    /// <summary>Shows a red error bar at the last known value.</summary>
    public void SetError()
        => Apply(hwnd => _taskbarList!.SetProgressState(hwnd, TaskbarProgressState.Error));

    /// <summary>Shows a yellow paused bar at the last known value.</summary>
    public void SetPaused()
        => Apply(hwnd => _taskbarList!.SetProgressState(hwnd, TaskbarProgressState.Paused));

    /// <summary>Removes the progress overlay entirely.</summary>
    public void Clear()
        => Apply(hwnd => _taskbarList!.SetProgressState(hwnd, TaskbarProgressState.NoProgress));

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private void Apply(Action<IntPtr> action)
    {
        if (_taskbarList == null) return;

        var hwnd = ResolveHwnd();
        if (hwnd == IntPtr.Zero) return;

        try
        {
            action(hwnd);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TaskbarProgressService] Taskbar progress update failed: {ex.Message}");
        }
    }

    private IntPtr ResolveHwnd()
    {
        if (_hwnd != IntPtr.Zero) return _hwnd;

        try
        {
            var win = System.Windows.Application.Current?.MainWindow;
            if (win != null)
                return new WindowInteropHelper(win).Handle;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TaskbarProgressService] Failed to resolve HWND: {ex.Message}");
        }

        return IntPtr.Zero;
    }

    // ── COM type declarations ─────────────────────────────────────────────────────

    private enum TaskbarProgressState
    {
        NoProgress    = 0,
        Indeterminate = 1,
        Normal        = 2,
        Error         = 4,
        Paused        = 8,
    }

    /// <summary>
    /// Full vtable-ordered declaration of ITaskbarList3.
    /// Every method must be present in the exact order defined by the COM interface,
    /// even those we never call, or vtable offsets will be wrong.
    /// </summary>
    [ComImport,
     Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        // ITaskbarList3
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, TaskbarProgressState tbpFlags);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, uint dwReserved);

        [PreserveSig] int ThumbBarAddButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);
        [PreserveSig] int ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);

        void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
        void SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string? pszDescription);
        void SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string? pszTip);
        void SetThumbnailClip(IntPtr hwnd, IntPtr prcClip);
    }

    [ComImport,
     Guid("56FDF344-FD6D-11d0-958A-006097C9A090"),
     ClassInterface(ClassInterfaceType.None)]
    private class TaskbarInstance { }
}
