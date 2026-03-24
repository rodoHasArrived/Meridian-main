using System.Windows.Input;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for <see cref="KeyboardShortcutService"/> — shortcut registration,
/// conflict detection, key formatting, category grouping, and enable/disable.
/// </summary>
public sealed class KeyboardShortcutServiceTests
{
    private static KeyboardShortcutService CreateService() => KeyboardShortcutService.Instance;

    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        var a = KeyboardShortcutService.Instance;
        var b = KeyboardShortcutService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── Default Shortcuts ────────────────────────────────────────────

    [Fact]
    public void Shortcuts_ShouldContainDefaultRegistrations()
    {
        var svc = CreateService();
        svc.Shortcuts.Should().NotBeEmpty();
        svc.Shortcuts.Count.Should().BeGreaterThanOrEqualTo(20);
    }

    [Theory]
    [InlineData("NavigateDashboard")]
    [InlineData("NavigateSymbols")]
    [InlineData("NavigateBackfill")]
    [InlineData("StartCollector")]
    [InlineData("RunBackfill")]
    [InlineData("AddSymbol")]
    [InlineData("Save")]
    [InlineData("Help")]
    [InlineData("Copy")]
    [InlineData("Paste")]
    [InlineData("Undo")]
    [InlineData("Redo")]
    public void DefaultShortcuts_ShouldBeRegistered(string actionId)
    {
        var svc = CreateService();
        svc.Shortcuts.Should().ContainKey(actionId);
    }

    [Fact]
    public void DefaultShortcuts_NavigateDashboard_ShouldBeCtrlD()
    {
        var svc = CreateService();
        var shortcut = svc.GetShortcut("NavigateDashboard");

        shortcut.Should().NotBeNull();
        shortcut!.Key.Should().Be(Key.D);
        shortcut.Modifiers.Should().Be(ModifierKeys.Control);
        shortcut.Category.Should().Be(ShortcutCategory.Navigation);
    }

    [Fact]
    public void DefaultShortcuts_RefreshStatus_ShouldBeF5()
    {
        var svc = CreateService();
        var shortcut = svc.GetShortcut("RefreshStatus");

        shortcut.Should().NotBeNull();
        shortcut!.Key.Should().Be(Key.F5);
        shortcut.Modifiers.Should().Be(ModifierKeys.None);
    }

    [Fact]
    public void DefaultShortcuts_AllShouldBeEnabled()
    {
        var svc = CreateService();
        foreach (var shortcut in svc.Shortcuts.Values)
        {
            shortcut.IsEnabled.Should().BeTrue(
                $"Shortcut '{shortcut.ActionId}' should be enabled by default");
        }
    }

    // ── RegisterShortcut ─────────────────────────────────────────────

    [Fact]
    public void RegisterShortcut_ShouldAddNewShortcut()
    {
        var svc = CreateService();
        var actionId = "TestAction-" + Guid.NewGuid().ToString("N")[..8];

        svc.RegisterShortcut(actionId, Key.F12, ModifierKeys.Alt,
            "Test Shortcut", ShortcutCategory.General);

        svc.Shortcuts.Should().ContainKey(actionId);
        var shortcut = svc.GetShortcut(actionId);
        shortcut.Should().NotBeNull();
        shortcut!.Key.Should().Be(Key.F12);
        shortcut.Modifiers.Should().Be(ModifierKeys.Alt);
        shortcut.Description.Should().Be("Test Shortcut");

        // Clean up
        svc.UnregisterShortcut(actionId);
    }

    [Fact]
    public void RegisterShortcut_SameActionId_ShouldOverwrite()
    {
        var svc = CreateService();
        var actionId = "OverwriteTest-" + Guid.NewGuid().ToString("N")[..8];

        svc.RegisterShortcut(actionId, Key.F9, ModifierKeys.None, "First");
        svc.RegisterShortcut(actionId, Key.F10, ModifierKeys.None, "Second");

        var shortcut = svc.GetShortcut(actionId);
        shortcut.Should().NotBeNull();
        shortcut!.Key.Should().Be(Key.F10);
        shortcut.Description.Should().Be("Second");

        // Clean up
        svc.UnregisterShortcut(actionId);
    }

    // ── UpdateShortcut ───────────────────────────────────────────────

    [Fact]
    public void UpdateShortcut_ShouldChangeKeyBinding()
    {
        var svc = CreateService();
        var actionId = "UpdateTest-" + Guid.NewGuid().ToString("N")[..8];
        svc.RegisterShortcut(actionId, Key.F9, ModifierKeys.None, "Before");

        svc.UpdateShortcut(actionId, Key.F10, ModifierKeys.Shift);

        var shortcut = svc.GetShortcut(actionId);
        shortcut!.Key.Should().Be(Key.F10);
        shortcut.Modifiers.Should().Be(ModifierKeys.Shift);

        // Clean up
        svc.UnregisterShortcut(actionId);
    }

    [Fact]
    public void UpdateShortcut_NonExistentActionId_ShouldNotThrow()
    {
        var svc = CreateService();
        var act = () => svc.UpdateShortcut("nonexistent-" + Guid.NewGuid(), Key.A, ModifierKeys.None);
        act.Should().NotThrow();
    }

    // ── SetShortcutEnabled ───────────────────────────────────────────

    [Fact]
    public void SetShortcutEnabled_ShouldDisableShortcut()
    {
        var svc = CreateService();
        var actionId = "EnableTest-" + Guid.NewGuid().ToString("N")[..8];
        svc.RegisterShortcut(actionId, Key.F8, ModifierKeys.None, "Enabled Test");

        svc.SetShortcutEnabled(actionId, false);

        var shortcut = svc.GetShortcut(actionId);
        shortcut!.IsEnabled.Should().BeFalse();

        // Re-enable and clean up
        svc.SetShortcutEnabled(actionId, true);
        svc.UnregisterShortcut(actionId);
    }

    [Fact]
    public void SetShortcutEnabled_NonExistentActionId_ShouldNotThrow()
    {
        var svc = CreateService();
        var act = () => svc.SetShortcutEnabled("nonexistent-" + Guid.NewGuid(), false);
        act.Should().NotThrow();
    }

    // ── UnregisterShortcut ───────────────────────────────────────────

    [Fact]
    public void UnregisterShortcut_ShouldRemoveShortcut()
    {
        var svc = CreateService();
        var actionId = "RemoveTest-" + Guid.NewGuid().ToString("N")[..8];
        svc.RegisterShortcut(actionId, Key.F7, ModifierKeys.None, "Remove Test");

        svc.UnregisterShortcut(actionId);

        svc.Shortcuts.Should().NotContainKey(actionId);
        svc.GetShortcut(actionId).Should().BeNull();
    }

    [Fact]
    public void UnregisterShortcut_NonExistentActionId_ShouldNotThrow()
    {
        var svc = CreateService();
        var act = () => svc.UnregisterShortcut("nonexistent-" + Guid.NewGuid());
        act.Should().NotThrow();
    }

    // ── GetShortcut ──────────────────────────────────────────────────

    [Fact]
    public void GetShortcut_ExistingAction_ShouldReturnShortcut()
    {
        var svc = CreateService();
        var shortcut = svc.GetShortcut("Save");
        shortcut.Should().NotBeNull();
        shortcut!.ActionId.Should().Be("Save");
    }

    [Fact]
    public void GetShortcut_NonExistentAction_ShouldReturnNull()
    {
        var svc = CreateService();
        var shortcut = svc.GetShortcut("nonexistent-" + Guid.NewGuid());
        shortcut.Should().BeNull();
    }

    // ── HasConflict ──────────────────────────────────────────────────

    [Fact]
    public void HasConflict_ExistingCombination_ShouldReturnTrue()
    {
        var svc = CreateService();
        // Ctrl+S is registered as "Save"
        var hasConflict = svc.HasConflict(Key.S, ModifierKeys.Control);
        hasConflict.Should().BeTrue();
    }

    [Fact]
    public void HasConflict_UnusedCombination_ShouldReturnFalse()
    {
        var svc = CreateService();
        // Alt+F12 is unlikely to be registered
        var hasConflict = svc.HasConflict(Key.F12, ModifierKeys.Alt);
        hasConflict.Should().BeFalse();
    }

    [Fact]
    public void HasConflict_WithExclusion_ShouldExcludeSpecifiedAction()
    {
        var svc = CreateService();
        // Ctrl+S is registered as "Save", but if we exclude "Save" it should not conflict
        var hasConflict = svc.HasConflict(Key.S, ModifierKeys.Control, excludeActionId: "Save");
        hasConflict.Should().BeFalse();
    }

    // ── IsEnabled ────────────────────────────────────────────────────

    [Fact]
    public void IsEnabled_ShouldDefaultToTrue()
    {
        var svc = CreateService();
        svc.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_ShouldBeSettable()
    {
        var svc = CreateService();
        svc.IsEnabled = false;
        svc.IsEnabled.Should().BeFalse();

        // Restore
        svc.IsEnabled = true;
    }

    // ── GetShortcutsByCategory ────────────────────────────────────────

    [Fact]
    public void GetShortcutsByCategory_ShouldReturnMultipleCategories()
    {
        var svc = CreateService();
        var grouped = svc.GetShortcutsByCategory();

        grouped.Should().NotBeEmpty();
        grouped.Should().ContainKey(ShortcutCategory.Navigation);
        grouped.Should().ContainKey(ShortcutCategory.General);
        grouped.Should().ContainKey(ShortcutCategory.Collector);
    }

    [Fact]
    public void GetShortcutsByCategory_Navigation_ShouldContainNavigationShortcuts()
    {
        var svc = CreateService();
        var grouped = svc.GetShortcutsByCategory();

        grouped[ShortcutCategory.Navigation].Should().Contain(s =>
            s.ActionId == "NavigateDashboard");
    }

    // ── FormatShortcut ───────────────────────────────────────────────

    [Fact]
    public void FormatShortcut_CtrlS_ShouldReturnCtrlPlusS()
    {
        var formatted = KeyboardShortcutService.FormatShortcut(Key.S, ModifierKeys.Control);
        formatted.Should().Be("Ctrl+S");
    }

    [Fact]
    public void FormatShortcut_CtrlShiftS_ShouldReturnCtrlShiftPlusS()
    {
        var formatted = KeyboardShortcutService.FormatShortcut(
            Key.S, ModifierKeys.Control | ModifierKeys.Shift);
        formatted.Should().Be("Ctrl+Shift+S");
    }

    [Fact]
    public void FormatShortcut_F5_ShouldReturnF5()
    {
        var formatted = KeyboardShortcutService.FormatShortcut(Key.F5, ModifierKeys.None);
        formatted.Should().Be("F5");
    }

    [Fact]
    public void FormatShortcut_Escape_ShouldReturnEsc()
    {
        var formatted = KeyboardShortcutService.FormatShortcut(Key.Escape, ModifierKeys.None);
        formatted.Should().Be("Esc");
    }

    [Fact]
    public void FormatShortcut_Delete_ShouldReturnDel()
    {
        var formatted = KeyboardShortcutService.FormatShortcut(Key.Delete, ModifierKeys.None);
        formatted.Should().Be("Del");
    }

    [Fact]
    public void FormatShortcut_CtrlD0_ShouldReturnCtrl0()
    {
        var formatted = KeyboardShortcutService.FormatShortcut(Key.D0, ModifierKeys.Control);
        formatted.Should().Be("Ctrl+0");
    }

    [Fact]
    public void FormatShortcut_CtrlPlus_ShouldReturnCtrlPlus()
    {
        var formatted = KeyboardShortcutService.FormatShortcut(Key.Add, ModifierKeys.Control);
        formatted.Should().Be("Ctrl++");
    }

    // ── ShortcutAction Model ─────────────────────────────────────────

    [Fact]
    public void ShortcutAction_ShouldHaveDefaults()
    {
        var action = new ShortcutAction();
        action.ActionId.Should().BeEmpty();
        action.Description.Should().BeEmpty();
        action.IsEnabled.Should().BeTrue();
        action.Category.Should().Be(ShortcutCategory.General);
    }

    [Fact]
    public void ShortcutAction_FormattedShortcut_ShouldReturnFormattedString()
    {
        var action = new ShortcutAction
        {
            Key = Key.S,
            Modifiers = ModifierKeys.Control
        };

        action.FormattedShortcut.Should().Be("Ctrl+S");
    }

    // ── ShortcutCategory Enum ────────────────────────────────────────

    [Theory]
    [InlineData(ShortcutCategory.General)]
    [InlineData(ShortcutCategory.Navigation)]
    [InlineData(ShortcutCategory.Collector)]
    [InlineData(ShortcutCategory.Backfill)]
    [InlineData(ShortcutCategory.Symbols)]
    [InlineData(ShortcutCategory.View)]
    public void ShortcutCategory_AllValues_ShouldBeDefined(ShortcutCategory category)
    {
        Enum.IsDefined(typeof(ShortcutCategory), category).Should().BeTrue();
    }

    // ── ShortcutInvokedEventArgs ─────────────────────────────────────

    [Fact]
    public void ShortcutInvokedEventArgs_ShouldHaveDefaults()
    {
        var args = new ShortcutInvokedEventArgs();
        args.ActionId.Should().BeEmpty();
        args.Action.Should().BeNull();
    }

    // ── Detach ───────────────────────────────────────────────────────

    [Fact]
    public void Detach_WithoutInitialize_ShouldNotThrow()
    {
        var svc = CreateService();
        var act = () => svc.Detach();
        act.Should().NotThrow();
    }
}
