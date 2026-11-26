using System.Collections.Generic;
using System.Windows.Input;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for the rebinding seams the Keyboard Shortcuts page uses on
/// <see cref="KeyboardShortcutService"/>: set-based application of saved overrides (so saved
/// swaps survive a restart), per-shortcut reset, and naming the shortcut that holds a chord.
/// Each test uses an isolated instance so the process-wide singleton is never mutated.
/// </summary>
public sealed class KeyboardShortcutServiceRebindingTests
{
    private static Dictionary<string, DesktopShortcutOverride> Overrides(
        params (string ActionId, DesktopShortcutOverride Override)[] entries)
    {
        var overrides = new Dictionary<string, DesktopShortcutOverride>(StringComparer.Ordinal);
        foreach (var (actionId, shortcutOverride) in entries)
        {
            overrides[actionId] = shortcutOverride;
        }

        return overrides;
    }

    [Fact]
    public void ApplyOverrides_SavedSwapOfTwoShortcuts_AppliesBothHalves()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        // The operator swapped Watchlist (Ctrl+W) and Data Quality (Ctrl+Q). Applied one at a
        // time, the first entry would collide with the other's not-yet-moved default.
        var skipped = svc.ApplyOverrides(Overrides(
            ("NavigateWatchlist", new DesktopShortcutOverride("Q", "Control")),
            ("NavigateDataQuality", new DesktopShortcutOverride("W", "Control"))));

        skipped.Should().BeEmpty();
        svc.GetShortcut("NavigateWatchlist")!.Key.Should().Be(Key.Q);
        svc.GetShortcut("NavigateDataQuality")!.Key.Should().Be(Key.W);
    }

    [Fact]
    public void ApplyOverrides_SavedThreeWayRotation_AppliesEveryEntry()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        var skipped = svc.ApplyOverrides(Overrides(
            ("RunBackfill", new DesktopShortcutOverride("L", "Control")),
            ("ViewLogs", new DesktopShortcutOverride("K", "Control")),
            ("QuickCommand", new DesktopShortcutOverride("R", "Control"))));

        skipped.Should().BeEmpty();
        svc.GetShortcut("RunBackfill")!.Key.Should().Be(Key.L);
        svc.GetShortcut("ViewLogs")!.Key.Should().Be(Key.K);
        svc.GetShortcut("QuickCommand")!.Key.Should().Be(Key.R);
    }

    [Fact]
    public void ApplyOverrides_TwoRebindingsOntoOneChord_EarlierEntryKeepsIt()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        var skipped = svc.ApplyOverrides(Overrides(
            ("ViewLogs", new DesktopShortcutOverride("F9", "None")),
            ("Help", new DesktopShortcutOverride("F9", "None"))));

        skipped.Should().Equal("Help");
        svc.GetShortcut("ViewLogs")!.Key.Should().Be(Key.F9);
        svc.GetShortcut("Help")!.Key.Should().Be(Key.F1);
    }

    [Fact]
    public void ApplyOverrides_DroppedRebindingReclaimsDefault_CascadesToEntryThatTookIt()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        // Run Backfill loses F9 to the earlier View Logs entry and returns to Ctrl+R, which the
        // Quick Command entry had claimed, so that entry is skipped as well.
        var skipped = svc.ApplyOverrides(Overrides(
            ("ViewLogs", new DesktopShortcutOverride("F9", "None")),
            ("RunBackfill", new DesktopShortcutOverride("F9", "None")),
            ("QuickCommand", new DesktopShortcutOverride("R", "Control"))));

        skipped.Should().BeEquivalentTo("RunBackfill", "QuickCommand");
        svc.GetShortcut("ViewLogs")!.Key.Should().Be(Key.F9);
        svc.GetShortcut("RunBackfill")!.Key.Should().Be(Key.R);
        svc.GetShortcut("QuickCommand")!.Key.Should().Be(Key.K);
        svc.HasConflict(Key.R, ModifierKeys.Control, excludeActionId: "RunBackfill").Should().BeFalse();
    }

    [Fact]
    public void ApplyOverrides_SkippedRebinding_DoesNotApplyItsEnabledState()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        var skipped = svc.ApplyOverrides(Overrides(
            ("RunBackfill", new DesktopShortcutOverride("S", "Control", IsEnabled: false))));

        skipped.Should().Equal("RunBackfill");
        svc.GetShortcut("RunBackfill")!.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void ApplyOverrides_DefaultChordsSharedByBuiltIns_StayShared()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        // Cancel Backfill and Close Dialog both default to Esc; an unrelated enabled-only
        // override must not disturb that.
        var skipped = svc.ApplyOverrides(Overrides(
            ("CloseDialog", new DesktopShortcutOverride(IsEnabled: false))));

        skipped.Should().BeEmpty();
        svc.GetShortcut("CloseDialog")!.Key.Should().Be(Key.Escape);
        svc.GetShortcut("CancelBackfill")!.Key.Should().Be(Key.Escape);
    }

    [Fact]
    public void TryResetShortcut_RestoresDefaultBindingAndEnabledStateAndSaves()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();
        IReadOnlyDictionary<string, DesktopShortcutOverride>? saved = null;
        svc.AttachPreferenceStore(
            Overrides(("RunBackfill", new DesktopShortcutOverride("F9", "Control", IsEnabled: false))),
            overrides => saved = overrides);

        var reset = svc.TryResetShortcut("RunBackfill", out var conflictingActionId);

        reset.Should().BeTrue();
        conflictingActionId.Should().BeNull();
        var action = svc.GetShortcut("RunBackfill")!;
        action.Key.Should().Be(Key.R);
        action.Modifiers.Should().Be(ModifierKeys.Control);
        action.IsEnabled.Should().BeTrue();
        saved.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void TryResetShortcut_WhenAnotherShortcutWasReboundOntoTheDefault_RefusesAndNamesIt()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();
        svc.UpdateShortcut("RunBackfill", Key.F9, ModifierKeys.None);
        svc.UpdateShortcut("ViewLogs", Key.R, ModifierKeys.Control);

        var reset = svc.TryResetShortcut("RunBackfill", out var conflictingActionId);

        reset.Should().BeFalse();
        conflictingActionId.Should().Be("ViewLogs");
        svc.GetShortcut("RunBackfill")!.Key.Should().Be(Key.F9);
    }

    [Fact]
    public void TryResetShortcut_DefaultChordSharedWithAnotherBuiltIn_IsAllowed()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();
        svc.SetShortcutEnabled("CloseDialog", false);

        var reset = svc.TryResetShortcut("CloseDialog", out var conflictingActionId);

        reset.Should().BeTrue();
        conflictingActionId.Should().BeNull();
        svc.GetShortcut("CloseDialog")!.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void TryResetShortcut_UnknownAction_ReturnsFalse()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        svc.TryResetShortcut("NotRegistered", out var conflictingActionId).Should().BeFalse();
        conflictingActionId.Should().BeNull();
    }

    [Fact]
    public void FindConflictingShortcut_NamesTheHolderAndAgreesWithHasConflict()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        svc.FindConflictingShortcut(Key.S, ModifierKeys.Control, excludeActionId: "RunBackfill")!
            .ActionId.Should().Be("Save");
        svc.HasConflict(Key.S, ModifierKeys.Control, excludeActionId: "RunBackfill").Should().BeTrue();
        svc.FindConflictingShortcut(Key.F9, ModifierKeys.Control | ModifierKeys.Alt).Should().BeNull();
        svc.HasConflict(Key.F9, ModifierKeys.Control | ModifierKeys.Alt).Should().BeFalse();
    }

    [Fact]
    public void GetDefaultBinding_ReturnsBuiltInDefaultsEvenAfterRebinding()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();
        svc.UpdateShortcut("StopCollector", Key.F12, ModifierKeys.Control | ModifierKeys.Alt);

        svc.GetDefaultBinding("StopCollector")
            .Should().Be((Key.Q, ModifierKeys.Control | ModifierKeys.Shift, true));
        svc.GetDefaultBinding("NotRegistered").Should().BeNull();
    }
}
