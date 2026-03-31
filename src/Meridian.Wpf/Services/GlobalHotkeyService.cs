using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meridian.Wpf.Services;

/// <summary>
/// System-wide (global) hotkey service using Win32 RegisterHotKey / UnregisterHotKey.
/// Hotkeys fire even when Meridian is not in the foreground. Requires a real HWND;
/// call <see cref="Initialize"/> after <c>MainWindow.Loaded</c>.
/// </summary>
public sealed class GlobalHotkeyService
{
    // ── Win32 ──────────────────────────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // Modifier flags for RegisterHotKey (subset used by default hotkeys)
    private const uint MOD_CTRL = 0x0002;
    private const uint MOD_WIN  = 0x0008;

    // Virtual-key codes
    private const uint VK_M = 0x4D;
    private const uint VK_P = 0x50;
    private const uint VK_T = 0x54;

    // ── Singleton ──────────────────────────────────────────────────────────────

    private static readonly Lazy<GlobalHotkeyService> _instance = new(() => new GlobalHotkeyService());

    /// <summary>Gets the singleton instance.</summary>
    public static GlobalHotkeyService Instance => _instance.Value;

    // ── State ──────────────────────────────────────────────────────────────────

    private IntPtr _hwnd = IntPtr.Zero;
    private bool _initialized;
    private bool _isEnabled = true;

    private readonly ReadOnlyCollection<GlobalHotkeyDefinition> _definitions;
    private readonly List<int> _registeredIds = new();

    // ── Events ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the thread that processes WM_HOTKEY messages (normally the UI thread)
    /// when a registered global hotkey is pressed.
    /// </summary>
    public event EventHandler<GlobalHotkeyFiredEventArgs>? GlobalHotkeyFired;

    // ── Constructor ────────────────────────────────────────────────────────────

    private GlobalHotkeyService()
    {
        _definitions = new ReadOnlyCollection<GlobalHotkeyDefinition>(
        [
            new GlobalHotkeyDefinition(0x9001, MOD_CTRL | MOD_WIN, VK_M,
                "BringToFront", "Ctrl+Win+M", "Show / activate Meridian window"),

            new GlobalHotkeyDefinition(0x9002, MOD_CTRL | MOD_WIN, VK_P,
                "PauseResumeCollector", "Ctrl+Win+P", "Pause or resume data collector"),

            new GlobalHotkeyDefinition(0x9003, MOD_CTRL | MOD_WIN, VK_T,
                "ToggleTickerStrip", "Ctrl+Win+T", "Toggle ticker strip (future feature)"),
        ]);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Gets or sets whether global hotkeys are active.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;

            if (_initialized)
            {
                if (_isEnabled)
                    RegisterAll();
                else
                    UnregisterAll();
            }
        }
    }

    /// <summary>The static list of registered hotkey definitions.</summary>
    public IReadOnlyList<GlobalHotkeyDefinition> Definitions => _definitions;

    /// <summary>
    /// Registers all hotkeys with Win32. Must be called after the window handle is
    /// available (i.e., inside or after <c>Window.Loaded</c>).
    /// </summary>
    /// <param name="hwnd">The HWND that will receive WM_HOTKEY messages.</param>
    public void Initialize(IntPtr hwnd)
    {
        if (_initialized) return;

        _hwnd = hwnd;
        _initialized = true;

        if (_isEnabled)
            RegisterAll();
    }

    /// <summary>
    /// Routes a WM_HOTKEY message (wParam) to the appropriate action event.
    /// Call this from the window's WndProc hook when <c>msg == 0x0312</c>.
    /// </summary>
    /// <param name="hotkeyId">The <c>wParam</c> value from the WM_HOTKEY message.</param>
    public void HandleHotkeyMessage(int hotkeyId)
    {
        if (!_isEnabled) return;

        foreach (var def in _definitions)
        {
            if (def.Id != hotkeyId) continue;

            GlobalHotkeyFired?.Invoke(this, new GlobalHotkeyFiredEventArgs(def.ActionId, def));
            return;
        }
    }

    /// <summary>
    /// Unregisters all hotkeys and resets state. Call from <c>Window.Closing</c>.
    /// </summary>
    public void Shutdown()
    {
        UnregisterAll();
        _initialized = false;
        _hwnd = IntPtr.Zero;
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private void RegisterAll()
    {
        if (_hwnd == IntPtr.Zero) return;

        foreach (var def in _definitions)
        {
            bool ok = RegisterHotKey(_hwnd, def.Id, def.Modifiers, def.VirtualKey);
            if (ok)
            {
                _registeredIds.Add(def.Id);
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                    $"[GlobalHotkeyService] WARNING: Failed to register {def.KeyGesture} " +
                    $"(id=0x{def.Id:X4}, win32err={err}). " +
                    "Another application may be using this combination.");
            }
        }
    }

    private void UnregisterAll()
    {
        if (_hwnd == IntPtr.Zero) return;

        foreach (var id in _registeredIds)
        {
            UnregisterHotKey(_hwnd, id);
        }

        _registeredIds.Clear();
    }
}

/// <summary>
/// Describes a single global hotkey registration.
/// </summary>
public sealed class GlobalHotkeyDefinition
{
    internal GlobalHotkeyDefinition(int id, uint modifiers, uint virtualKey,
        string actionId, string keyGesture, string description)
    {
        Id = id;
        Modifiers = modifiers;
        VirtualKey = virtualKey;
        ActionId = actionId;
        KeyGesture = keyGesture;
        Description = description;
    }

    /// <summary>Win32 hotkey id passed to RegisterHotKey.</summary>
    public int Id { get; }

    /// <summary>MOD_ flags passed to RegisterHotKey.</summary>
    public uint Modifiers { get; }

    /// <summary>Virtual-key code passed to RegisterHotKey.</summary>
    public uint VirtualKey { get; }

    /// <summary>Application-level action identifier.</summary>
    public string ActionId { get; }

    /// <summary>Human-readable key gesture string, e.g. "Ctrl+Win+M".</summary>
    public string KeyGesture { get; }

    /// <summary>Short description shown in the Settings UI.</summary>
    public string Description { get; }
}

/// <summary>Event args for <see cref="GlobalHotkeyService.GlobalHotkeyFired"/>.</summary>
public sealed class GlobalHotkeyFiredEventArgs : EventArgs
{
    public GlobalHotkeyFiredEventArgs(string actionId, GlobalHotkeyDefinition definition)
    {
        ActionId = actionId;
        Definition = definition;
    }

    /// <summary>The action identifier, e.g. "BringToFront".</summary>
    public string ActionId { get; }

    /// <summary>The full definition for the fired hotkey.</summary>
    public GlobalHotkeyDefinition Definition { get; }
}
