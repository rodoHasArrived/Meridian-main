using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

/// <summary>
/// Tests for in-app shortcut rebinding on the Keyboard Shortcuts page: chord capture, the
/// modifier guardrail and conflict rejections with inline reasons, enable/disable, per-shortcut
/// reset, and that every accepted change reaches the attached preference store.
/// </summary>
public sealed class KeyboardShortcutsViewModelTests
{
    private readonly KeyboardShortcutService _service = KeyboardShortcutService.CreateIsolatedForTests();
    private readonly List<IReadOnlyDictionary<string, DesktopShortcutOverride>> _saves = new();

    private KeyboardShortcutsViewModel CreateViewModel(
        IReadOnlyDictionary<string, DesktopShortcutOverride>? persisted = null)
    {
        _service.AttachPreferenceStore(
            persisted ?? new Dictionary<string, DesktopShortcutOverride>(),
            overrides => _saves.Add(overrides));
        return new KeyboardShortcutsViewModel(_service);
    }

    private static ShortcutBindingRowViewModel Row(KeyboardShortcutsViewModel vm, string actionId)
        => vm.FindRow(actionId) ?? throw new InvalidOperationException($"No row for {actionId}.");

    [Fact]
    public void Constructor_GroupsEveryShortcutInDisplayOrder()
    {
        var vm = CreateViewModel();

        vm.Categories.Select(group => group.Category).Should().Equal(
            ShortcutCategory.Navigation,
            ShortcutCategory.Collector,
            ShortcutCategory.Backfill,
            ShortcutCategory.Symbols,
            ShortcutCategory.View,
            ShortcutCategory.General);
        vm.Categories.Sum(group => group.Rows.Count).Should().Be(_service.Shortcuts.Count);
        vm.OverrideStatusText.Should().Be("All shortcuts use their default bindings.");
        vm.ResetAllCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Rebind_ValidChord_UpdatesBindingAndSaves()
    {
        var vm = CreateViewModel();
        var row = Row(vm, "StopCollector");

        row.BeginCaptureCommand.Execute(null);
        var consumed = vm.HandleCapturedKey(Key.F12, ModifierKeys.Control | ModifierKeys.Alt);

        consumed.Should().BeTrue();
        vm.IsCapturing.Should().BeFalse();
        row.IsCapturing.Should().BeFalse();
        row.FormattedShortcut.Should().Be("Ctrl+Alt+F12");
        row.IsCustomized.Should().BeTrue();
        row.HasValidationMessage.Should().BeFalse();
        _service.GetShortcut("StopCollector")!.Key.Should().Be(Key.F12);
        _saves.Should().ContainSingle()
            // ModifierKeys.ToString() lists flags in enum-value order (Alt = 1, Control = 2).
            .Which["StopCollector"].Should().Be(new DesktopShortcutOverride("F12", "Alt, Control"));
        vm.OverrideStatusText.Should().Be("1 shortcut differs from its default and is saved for this machine.");
        vm.ResetAllCommand.CanExecute(null).Should().BeTrue();
        row.ResetCommand.CanExecute(null).Should().BeTrue();
    }

    [Theory]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.RightCtrl)]
    [InlineData(Key.LeftShift)]
    [InlineData(Key.RightShift)]
    [InlineData(Key.LeftAlt)]
    [InlineData(Key.RightAlt)]
    [InlineData(Key.LWin)]
    [InlineData(Key.RWin)]
    [InlineData(Key.System)]
    public void Capture_BareModifierPress_IsIgnoredAndKeepsWaiting(Key modifierKey)
    {
        var vm = CreateViewModel();
        var row = Row(vm, "RunBackfill");
        row.BeginCaptureCommand.Execute(null);

        var consumed = vm.HandleCapturedKey(modifierKey, ModifierKeys.Control);

        consumed.Should().BeTrue();
        vm.IsCapturing.Should().BeTrue();
        row.IsCapturing.Should().BeTrue();
        row.ChordDisplayText.Should().Be("Press keys…");
        row.HasValidationMessage.Should().BeFalse();
        _service.GetShortcut("RunBackfill")!.Key.Should().Be(Key.R);
        _saves.Should().BeEmpty();
    }

    [Fact]
    public void Capture_ModifierThenKey_AppliesOnlyTheCompletedChord()
    {
        var vm = CreateViewModel();
        var row = Row(vm, "ViewLogs");
        row.BeginCaptureCommand.Execute(null);

        vm.HandleCapturedKey(Key.LeftCtrl, ModifierKeys.Control);
        vm.HandleCapturedKey(Key.LeftShift, ModifierKeys.Control | ModifierKeys.Shift);
        vm.HandleCapturedKey(Key.L, ModifierKeys.Control | ModifierKeys.Shift);

        row.FormattedShortcut.Should().Be("Ctrl+Shift+L");
        _saves.Should().ContainSingle();
    }

    [Theory]
    [InlineData("StartCollector", ModifierKeys.None)]
    [InlineData("StopCollector", ModifierKeys.Shift)]
    [InlineData("ToggleConnection", ModifierKeys.Shift)]
    [InlineData("DeleteSelected", ModifierKeys.None)]
    public void Rebind_HighConsequenceActionWithoutCtrlOrAlt_IsRejectedWithReason(string actionId, ModifierKeys modifiers)
    {
        var vm = CreateViewModel();
        var row = Row(vm, actionId);
        var before = _service.GetShortcut(actionId)!;
        var (key, mods) = (before.Key, before.Modifiers);
        row.BeginCaptureCommand.Execute(null);

        vm.HandleCapturedKey(Key.F9, modifiers);

        row.HasValidationMessage.Should().BeTrue();
        row.ValidationMessage.Should().Contain("must keep Ctrl or Alt").And.Contain(row.Description);
        vm.IsCapturing.Should().BeFalse();
        _service.GetShortcut(actionId)!.Key.Should().Be(key);
        _service.GetShortcut(actionId)!.Modifiers.Should().Be(mods);
        _saves.Should().BeEmpty();
    }

    [Fact]
    public void Rebind_ChordHeldByAnotherShortcut_IsRejectedNamingTheHolder()
    {
        var vm = CreateViewModel();
        var row = Row(vm, "RunBackfill");
        row.BeginCaptureCommand.Execute(null);

        vm.HandleCapturedKey(Key.S, ModifierKeys.Control);

        row.ValidationMessage.Should().Be("Ctrl+S is already used by Save.");
        _service.GetShortcut("RunBackfill")!.Key.Should().Be(Key.R);
        _service.GetShortcut("Save")!.Key.Should().Be(Key.S);
        _saves.Should().BeEmpty();
    }

    [Fact]
    public void Rebind_RejectionClearsOnNextSuccessfulRebind()
    {
        var vm = CreateViewModel();
        var row = Row(vm, "RunBackfill");
        row.BeginCaptureCommand.Execute(null);
        vm.HandleCapturedKey(Key.S, ModifierKeys.Control);

        row.BeginCaptureCommand.Execute(null);
        row.HasValidationMessage.Should().BeFalse();
        vm.HandleCapturedKey(Key.F9, ModifierKeys.Control);

        row.HasValidationMessage.Should().BeFalse();
        row.FormattedShortcut.Should().Be("Ctrl+F9");
    }

    [Fact]
    public void Rebind_SameChordAsCurrent_IsANoOp()
    {
        var vm = CreateViewModel();
        var row = Row(vm, "RunBackfill");
        row.BeginCaptureCommand.Execute(null);

        vm.HandleCapturedKey(Key.R, ModifierKeys.Control);

        row.HasValidationMessage.Should().BeFalse();
        vm.IsCapturing.Should().BeFalse();
        _saves.Should().BeEmpty();
    }

    [Fact]
    public void Capture_SuspendsGlobalShortcutsUntilItEnds()
    {
        var vm = CreateViewModel();
        var row = Row(vm, "RunBackfill");

        row.BeginCaptureCommand.Execute(null);
        _service.IsEnabled.Should().BeFalse("a captured chord must not also run as a command");

        row.CancelCaptureCommand.Execute(null);
        _service.IsEnabled.Should().BeTrue();
        vm.IsCapturing.Should().BeFalse();
        _saves.Should().BeEmpty();
    }

    [Fact]
    public void Capture_WhenGlobalShortcutsWereAlreadyOff_LeavesThemOff()
    {
        var vm = CreateViewModel();
        _service.IsEnabled = false;
        var row = Row(vm, "RunBackfill");

        row.BeginCaptureCommand.Execute(null);
        vm.HandleCapturedKey(Key.F9, ModifierKeys.Control);

        _service.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Capture_StartingOnAnotherRow_CancelsTheFirst()
    {
        var vm = CreateViewModel();
        var first = Row(vm, "RunBackfill");
        var second = Row(vm, "ViewLogs");

        first.BeginCaptureCommand.Execute(null);
        second.BeginCaptureCommand.Execute(null);

        first.IsCapturing.Should().BeFalse();
        second.IsCapturing.Should().BeTrue();
        vm.CapturingRow.Should().BeSameAs(second);

        vm.CancelCapture();
        _service.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void HandleCapturedKey_WithoutActiveCapture_IsNotConsumed()
    {
        var vm = CreateViewModel();

        vm.HandleCapturedKey(Key.F9, ModifierKeys.Control).Should().BeFalse();
        _saves.Should().BeEmpty();
    }

    [Fact]
    public void ToggleEnabled_SavesEnabledState()
    {
        var vm = CreateViewModel();
        var row = Row(vm, "ToggleFullscreen");

        row.IsShortcutEnabled = false;

        _service.GetShortcut("ToggleFullscreen")!.IsEnabled.Should().BeFalse();
        _saves.Should().ContainSingle()
            .Which["ToggleFullscreen"].Should().Be(new DesktopShortcutOverride(IsEnabled: false));
        row.IsCustomized.Should().BeTrue();

        row.IsShortcutEnabled = true;

        _saves.Last().Should().BeEmpty();
        row.IsCustomized.Should().BeFalse();
    }

    [Fact]
    public void ResetRow_RestoresDefaultAndSaves()
    {
        var vm = CreateViewModel();
        var row = Row(vm, "RunBackfill");
        row.BeginCaptureCommand.Execute(null);
        vm.HandleCapturedKey(Key.F9, ModifierKeys.Control);
        row.IsShortcutEnabled = false;

        row.ResetCommand.Execute(null);

        row.FormattedShortcut.Should().Be("Ctrl+R");
        row.IsShortcutEnabled.Should().BeTrue();
        row.IsCustomized.Should().BeFalse();
        row.ResetCommand.CanExecute(null).Should().BeFalse();
        _saves.Last().Should().BeEmpty();
        vm.OverrideStatusText.Should().Be("All shortcuts use their default bindings.");
    }

    [Fact]
    public void ResetRow_WhenDefaultChordWasTaken_ShowsReasonAndKeepsBinding()
    {
        var vm = CreateViewModel();
        var runBackfill = Row(vm, "RunBackfill");
        var viewLogs = Row(vm, "ViewLogs");
        runBackfill.BeginCaptureCommand.Execute(null);
        vm.HandleCapturedKey(Key.F9, ModifierKeys.Control);
        viewLogs.BeginCaptureCommand.Execute(null);
        vm.HandleCapturedKey(Key.R, ModifierKeys.Control);

        runBackfill.ResetCommand.Execute(null);

        runBackfill.ValidationMessage.Should().Be("Can't restore Ctrl+R: View Logs now uses it. Rebind View Logs first.");
        runBackfill.FormattedShortcut.Should().Be("Ctrl+F9");
        runBackfill.IsCustomized.Should().BeTrue();
    }

    [Fact]
    public void ResetAll_RestoresEveryShortcutAndSavesEmptyOverrides()
    {
        var vm = CreateViewModel();
        Row(vm, "Help").IsShortcutEnabled = false;
        Row(vm, "RunBackfill").BeginCaptureCommand.Execute(null);
        vm.HandleCapturedKey(Key.F9, ModifierKeys.Control);

        vm.ResetAllCommand.Execute(null);

        _service.GetOverrides().Should().BeEmpty();
        _saves.Last().Should().BeEmpty();
        Row(vm, "Help").IsShortcutEnabled.Should().BeTrue();
        Row(vm, "RunBackfill").FormattedShortcut.Should().Be("Ctrl+R");
        vm.ResetAllCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void SwapMadeInTheEditor_SurvivesRestart()
    {
        // Swap Watchlist (Ctrl+W) and Data Quality (Ctrl+Q) through a temporary chord, the only
        // way the editor's conflict check allows, then reload the saved set into a fresh service.
        var vm = CreateViewModel();
        var watchlist = Row(vm, "NavigateWatchlist");
        var dataQuality = Row(vm, "NavigateDataQuality");

        watchlist.BeginCaptureCommand.Execute(null);
        vm.HandleCapturedKey(Key.F9, ModifierKeys.Control);
        dataQuality.BeginCaptureCommand.Execute(null);
        vm.HandleCapturedKey(Key.W, ModifierKeys.Control);
        watchlist.BeginCaptureCommand.Execute(null);
        vm.HandleCapturedKey(Key.Q, ModifierKeys.Control);

        var restarted = KeyboardShortcutService.CreateIsolatedForTests();
        restarted.AttachPreferenceStore(_saves.Last(), _ => { });

        restarted.GetShortcut("NavigateWatchlist")!.Key.Should().Be(Key.Q);
        restarted.GetShortcut("NavigateDataQuality")!.Key.Should().Be(Key.W);
        restarted.GetOverrides().Should().BeEquivalentTo(_service.GetOverrides());
    }

    [Fact]
    public void Refresh_ReflectsPersistedOverridesAndCancelsCapture()
    {
        var vm = CreateViewModel(new Dictionary<string, DesktopShortcutOverride>(StringComparer.Ordinal)
        {
            ["StopCollector"] = new("F12", "Control, Alt"),
            ["Help"] = new(IsEnabled: false)
        });
        Row(vm, "RunBackfill").BeginCaptureCommand.Execute(null);

        vm.Refresh();

        vm.IsCapturing.Should().BeFalse();
        _service.IsEnabled.Should().BeTrue();
        Row(vm, "StopCollector").FormattedShortcut.Should().Be("Ctrl+Alt+F12");
        Row(vm, "Help").IsShortcutEnabled.Should().BeFalse();
        vm.OverrideStatusText.Should().Be("2 shortcuts differ from their defaults and are saved for this machine.");
        _saves.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Key.System, Key.F4, Key.None, Key.F4)]
    [InlineData(Key.ImeProcessed, Key.None, Key.K, Key.K)]
    [InlineData(Key.F9, Key.None, Key.None, Key.F9)]
    public void ResolveCapturedKey_UsesTheRealKeyForAltAndImeChords(Key key, Key systemKey, Key imeKey, Key expected)
        => KeyboardShortcutsViewModel.ResolveCapturedKey(key, systemKey, imeKey).Should().Be(expected);
}
