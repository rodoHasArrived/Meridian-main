using System.Collections.Generic;
using System.Windows.Input;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for the operator rebinding path behind <c>KeyboardShortcutsPage</c>: validated
/// rebinding, per-shortcut reset, chord capture from key events, and applying saved overrides
/// that swap two shortcuts. Each test uses an isolated instance so the shared singleton is
/// never mutated.
/// </summary>
public sealed class KeyboardShortcutRebindingTests
{
    // ── TryUpdateShortcut ────────────────────────────────────────────

    [Fact]
    public void TryUpdateShortcut_FreeChord_RebindsAndSaves()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();
        IReadOnlyDictionary<string, DesktopShortcutOverride>? saved = null;
        svc.AttachPreferenceStore(new Dictionary<string, DesktopShortcutOverride>(), overrides => saved = overrides);

        var refusal = svc.TryUpdateShortcut("StopCollector", Key.F12, ModifierKeys.Control | ModifierKeys.Alt);

        refusal.Should().BeNull();
        svc.GetShortcut("StopCollector")!.Key.Should().Be(Key.F12);
        saved!["StopCollector"].Should().Be(new DesktopShortcutOverride("F12", "Alt, Control"));
    }

    [Fact]
    public void TryUpdateShortcut_ChordInUse_RefusesAndNamesTheOwner()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();
        var saves = 0;
        svc.AttachPreferenceStore(new Dictionary<string, DesktopShortcutOverride>(), _ => saves++);

        // Ctrl+S already belongs to Save.
        var refusal = svc.TryUpdateShortcut("RunBackfill", Key.S, ModifierKeys.Control);

        refusal.Should().Be("Ctrl+S is already used by Save.");
        svc.GetShortcut("RunBackfill")!.Key.Should().Be(Key.R);
        saves.Should().Be(0);
    }

    [Theory]
    [InlineData("StopCollector", ModifierKeys.None)]
    [InlineData("StopCollector", ModifierKeys.Shift)]
    [InlineData("DeleteSelected", ModifierKeys.None)]
    public void TryUpdateShortcut_HighConsequenceWithoutCtrlOrAlt_Refuses(string actionId, ModifierKeys modifiers)
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();
        var before = svc.GetShortcut(actionId)!.FormattedShortcut;

        var refusal = svc.TryUpdateShortcut(actionId, Key.F9, modifiers);

        refusal.Should().EndWith("must use Ctrl or Alt so it cannot be triggered by accident.");
        svc.GetShortcut(actionId)!.FormattedShortcut.Should().Be(before);
    }

    [Theory]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.RightShift)]
    [InlineData(Key.None)]
    public void TryUpdateShortcut_ModifierOrNoKey_Refuses(Key key)
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        var refusal = svc.TryUpdateShortcut("ViewLogs", key, ModifierKeys.Control);

        refusal.Should().Be("Press a key together with any modifiers.");
        svc.GetShortcut("ViewLogs")!.Key.Should().Be(Key.L);
    }

    [Fact]
    public void TryUpdateShortcut_UnknownAction_Refuses()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        svc.TryUpdateShortcut("RemovedInOlderBuild", Key.F9, ModifierKeys.Control)
            .Should().Be("This shortcut is no longer registered.");
    }

    [Fact]
    public void TryUpdateShortcut_SameChordAsCurrent_IsAccepted()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        svc.TryUpdateShortcut("ViewLogs", Key.L, ModifierKeys.Control).Should().BeNull();
        svc.IsOverridden("ViewLogs").Should().BeFalse();
    }

    // ── ResetShortcut / IsOverridden ─────────────────────────────────

    [Fact]
    public void ResetShortcut_RestoresOneShortcutAndKeepsTheOthers()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();
        IReadOnlyDictionary<string, DesktopShortcutOverride>? saved = null;
        svc.AttachPreferenceStore(new Dictionary<string, DesktopShortcutOverride>(), overrides => saved = overrides);
        svc.TryUpdateShortcut("ViewLogs", Key.F9, ModifierKeys.None).Should().BeNull();
        svc.SetShortcutEnabled("ViewLogs", false);
        svc.SetShortcutEnabled("Help", false);

        svc.ResetShortcut("ViewLogs");

        var viewLogs = svc.GetShortcut("ViewLogs")!;
        viewLogs.Key.Should().Be(Key.L);
        viewLogs.Modifiers.Should().Be(ModifierKeys.Control);
        viewLogs.IsEnabled.Should().BeTrue();
        svc.IsOverridden("ViewLogs").Should().BeFalse();
        svc.IsOverridden("Help").Should().BeTrue();
        saved.Should().ContainSingle().Which.Key.Should().Be("Help");
    }

    [Fact]
    public void GetDefaultShortcutText_ReportsDefaultAfterRebinding()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();
        svc.TryUpdateShortcut("ViewLogs", Key.F9, ModifierKeys.None);

        svc.GetDefaultShortcutText("ViewLogs").Should().Be("Ctrl+L");
        svc.GetDefaultShortcutText("RemovedInOlderBuild").Should().BeNull();
    }

    // ── TryGetChord ──────────────────────────────────────────────────

    [Theory]
    [InlineData(Key.F12, Key.None, ModifierKeys.Control, true, Key.F12)]
    [InlineData(Key.System, Key.F4, ModifierKeys.Alt, true, Key.F4)]
    [InlineData(Key.LeftCtrl, Key.None, ModifierKeys.Control, false, Key.LeftCtrl)]
    [InlineData(Key.System, Key.LeftAlt, ModifierKeys.Alt, false, Key.LeftAlt)]
    [InlineData(Key.RightShift, Key.None, ModifierKeys.Shift, false, Key.RightShift)]
    [InlineData(Key.D, Key.None, ModifierKeys.Windows, false, Key.D)]
    public void TryGetChord_IgnoresModifierOnlyPressesAndUnwrapsAltChords(
        Key key, Key systemKey, ModifierKeys modifiers, bool expected, Key expectedChordKey)
    {
        KeyboardShortcutService.TryGetChord(key, systemKey, modifiers, out var chordKey).Should().Be(expected);
        chordKey.Should().Be(expectedChordKey);
    }

    // ── ApplyOverrides: swaps and collision chains ──────────────────

    [Fact]
    public void ApplyOverrides_SwapOfTwoShortcuts_AppliesBoth()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        // An operator swaps Run Backfill (Ctrl+R) and View Logs (Ctrl+L).
        var skipped = svc.ApplyOverrides(new Dictionary<string, DesktopShortcutOverride>
        {
            ["RunBackfill"] = new("L", "Control"),
            ["ViewLogs"] = new("R", "Control")
        });

        skipped.Should().BeEmpty();
        svc.GetShortcut("RunBackfill")!.Key.Should().Be(Key.L);
        svc.GetShortcut("ViewLogs")!.Key.Should().Be(Key.R);
    }

    [Fact]
    public void ApplyOverrides_ThreeWayRotation_AppliesAll()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        var skipped = svc.ApplyOverrides(new Dictionary<string, DesktopShortcutOverride>
        {
            ["RunBackfill"] = new("L", "Control"),  // takes View Logs' Ctrl+L
            ["ViewLogs"] = new("N", "Control"),     // takes Add Symbol's Ctrl+N
            ["AddSymbol"] = new("R", "Control")     // takes Run Backfill's Ctrl+R
        });

        skipped.Should().BeEmpty();
    }

    [Fact]
    public void ApplyOverrides_RevertingOneRebindingCascadesToTheChordItFreedUp()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        // Run Backfill tries Ctrl+S, which Save keeps, so it reverts to Ctrl+R; View Logs, which
        // had taken Ctrl+R, then collides and reverts to Ctrl+L as well.
        var skipped = svc.ApplyOverrides(new Dictionary<string, DesktopShortcutOverride>
        {
            ["RunBackfill"] = new("S", "Control"),
            ["ViewLogs"] = new("R", "Control")
        });

        skipped.Should().BeEquivalentTo("RunBackfill", "ViewLogs");
        svc.GetShortcut("RunBackfill")!.Key.Should().Be(Key.R);
        svc.GetShortcut("ViewLogs")!.Key.Should().Be(Key.L);
        svc.GetOverrides().Should().BeEmpty();
    }

    [Fact]
    public void ApplyOverrides_DefaultSharedChordsAreNotReportedAsCollisions()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        // Cancel Backfill and Close Dialog share Escape by default; only an unrelated override is applied.
        var skipped = svc.ApplyOverrides(new Dictionary<string, DesktopShortcutOverride>
        {
            ["Help"] = new(IsEnabled: false)
        });

        skipped.Should().BeEmpty();
        svc.GetShortcut("CancelBackfill")!.Key.Should().Be(Key.Escape);
        svc.GetShortcut("CloseDialog")!.Key.Should().Be(Key.Escape);
    }
}
