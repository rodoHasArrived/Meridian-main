using System.Collections.Generic;
using System.Windows.Input;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for persisted operator shortcut overrides on <see cref="KeyboardShortcutService"/>:
/// applying saved overrides at startup, saving later changes, the modifier guardrail on
/// high-consequence actions, and reset to defaults. Each test uses an isolated instance so the
/// process-wide singleton the other shortcut tests share is never mutated.
/// </summary>
public sealed class KeyboardShortcutServicePersistenceTests
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
    public void AttachPreferenceStore_AppliesPersistedRebindingAndDisable()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        // Ctrl+Shift+Q is swallowed by the operator's remote-desktop client, so Stop Collector
        // was moved to Ctrl+Alt+F12 in an earlier session; bare F11 fullscreen was turned off.
        svc.AttachPreferenceStore(
            Overrides(
                ("StopCollector", new DesktopShortcutOverride("F12", "Control, Alt")),
                ("ToggleFullscreen", new DesktopShortcutOverride(IsEnabled: false))),
            _ => { });

        var stop = svc.GetShortcut("StopCollector")!;
        stop.Key.Should().Be(Key.F12);
        stop.Modifiers.Should().Be(ModifierKeys.Control | ModifierKeys.Alt);
        stop.IsEnabled.Should().BeTrue();
        svc.GetShortcut("ToggleFullscreen")!.IsEnabled.Should().BeFalse();
        svc.GetShortcut("ToggleFullscreen")!.Key.Should().Be(Key.F11);
    }

    [Fact]
    public void AttachPreferenceStore_DoesNotSaveWhileApplyingPersistedOverrides()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();
        var saves = 0;

        svc.AttachPreferenceStore(
            Overrides(("Help", new DesktopShortcutOverride(IsEnabled: false))),
            _ => saves++);

        saves.Should().Be(0);
    }

    [Fact]
    public void UpdateShortcut_AfterAttach_SavesOnlyTheDifferenceFromDefaults()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();
        IReadOnlyDictionary<string, DesktopShortcutOverride>? saved = null;
        svc.AttachPreferenceStore(Overrides(), overrides => saved = overrides);

        svc.UpdateShortcut("RunBackfill", Key.R, ModifierKeys.Control | ModifierKeys.Shift);

        saved.Should().NotBeNull();
        saved!.Should().ContainSingle();
        saved["RunBackfill"].Should().Be(new DesktopShortcutOverride("R", "Control, Shift"));
    }

    [Fact]
    public void SetShortcutEnabled_AfterAttach_SavesEnabledStateWithoutBinding()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();
        IReadOnlyDictionary<string, DesktopShortcutOverride>? saved = null;
        svc.AttachPreferenceStore(Overrides(), overrides => saved = overrides);

        svc.SetShortcutEnabled("Help", false);

        saved!["Help"].Should().Be(new DesktopShortcutOverride(IsEnabled: false));
    }

    [Fact]
    public void SavedOverrides_RoundTripIntoAFreshInstance()
    {
        var first = KeyboardShortcutService.CreateIsolatedForTests();
        IReadOnlyDictionary<string, DesktopShortcutOverride> saved = Overrides();
        first.AttachPreferenceStore(Overrides(), overrides => saved = overrides);
        first.UpdateShortcut("StopCollector", Key.F12, ModifierKeys.Control | ModifierKeys.Alt);
        first.SetShortcutEnabled("ToggleFullscreen", false);

        var restarted = KeyboardShortcutService.CreateIsolatedForTests();
        restarted.AttachPreferenceStore(saved, _ => { });

        restarted.GetOverrides().Should().BeEquivalentTo(first.GetOverrides());
        restarted.GetShortcut("StopCollector")!.Key.Should().Be(Key.F12);
        restarted.GetShortcut("ToggleFullscreen")!.IsEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("StartCollector", "None")]
    [InlineData("StopCollector", "None")]
    [InlineData("StopCollector", "Shift")]
    [InlineData("ToggleConnection", "Shift")]
    [InlineData("DeleteSelected", "None")]
    public void ApplyOverrides_RejectsHighConsequenceBindingWithoutCtrlOrAlt(string actionId, string modifiers)
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();
        var before = svc.GetShortcut(actionId)!;
        var (defaultKey, defaultModifiers) = (before.Key, before.Modifiers);

        var skipped = svc.ApplyOverrides(Overrides((actionId, new DesktopShortcutOverride("F9", modifiers))));

        skipped.Should().ContainSingle().Which.Should().Be(actionId);
        svc.GetShortcut(actionId)!.Key.Should().Be(defaultKey);
        svc.GetShortcut(actionId)!.Modifiers.Should().Be(defaultModifiers);
    }

    [Fact]
    public void ApplyOverrides_AllowsBareKeyForOrdinaryActions()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        var skipped = svc.ApplyOverrides(Overrides(("ViewLogs", new DesktopShortcutOverride("F9", "None"))));

        skipped.Should().BeEmpty();
        svc.GetShortcut("ViewLogs")!.Key.Should().Be(Key.F9);
        svc.GetShortcut("ViewLogs")!.Modifiers.Should().Be(ModifierKeys.None);
    }

    [Fact]
    public void ApplyOverrides_SkipsBindingThatCollidesWithAnotherShortcut()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        // Ctrl+S already belongs to Save.
        var skipped = svc.ApplyOverrides(Overrides(("RunBackfill", new DesktopShortcutOverride("S", "Control"))));

        skipped.Should().Equal("RunBackfill");
        svc.GetShortcut("RunBackfill")!.Key.Should().Be(Key.R);
        svc.GetShortcut("Save")!.Key.Should().Be(Key.S);
    }

    [Theory]
    [InlineData("NotAKey", "Control")]
    [InlineData("F9", "Control, Hyper")]
    [InlineData("99999", "Control")]
    [InlineData("F9", "4096")]
    public void ApplyOverrides_SkipsUnparseableKeyOrModifiers(string key, string modifiers)
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        var skipped = svc.ApplyOverrides(Overrides(("ViewLogs", new DesktopShortcutOverride(key, modifiers))));

        skipped.Should().Equal("ViewLogs");
        svc.GetShortcut("ViewLogs")!.Key.Should().Be(Key.L);
        svc.GetShortcut("ViewLogs")!.Modifiers.Should().Be(ModifierKeys.Control);
    }

    [Fact]
    public void ApplyOverrides_SkipsUnknownActionIds()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        var skipped = svc.ApplyOverrides(Overrides(("RemovedInOlderBuild", new DesktopShortcutOverride(IsEnabled: false))));

        skipped.Should().Equal("RemovedInOlderBuild");
        svc.GetOverrides().Should().BeEmpty();
    }

    [Fact]
    public void ResetToDefaults_RestoresBindingsAndSavesEmptyOverrides()
    {
        var svc = KeyboardShortcutService.CreateIsolatedForTests();
        IReadOnlyDictionary<string, DesktopShortcutOverride>? saved = null;
        svc.AttachPreferenceStore(
            Overrides(
                ("StopCollector", new DesktopShortcutOverride("F12", "Control, Alt")),
                ("Help", new DesktopShortcutOverride(IsEnabled: false))),
            overrides => saved = overrides);

        svc.ResetToDefaults();

        saved.Should().NotBeNull().And.BeEmpty();
        svc.GetOverrides().Should().BeEmpty();
        svc.GetShortcut("StopCollector")!.Key.Should().Be(Key.Q);
        svc.GetShortcut("StopCollector")!.Modifiers.Should().Be(ModifierKeys.Control | ModifierKeys.Shift);
        svc.GetShortcut("Help")!.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void UpdateShortcut_WithoutAttachedStore_KeepsChangeInMemoryOnly()
    {
        // The shared singleton used by the existing shortcut tests is never attached to a
        // store in tests, so their UpdateShortcut / SetShortcutEnabled calls stay in memory.
        var svc = KeyboardShortcutService.CreateIsolatedForTests();

        var act = () => svc.UpdateShortcut("ViewLogs", Key.F9, ModifierKeys.None);

        act.Should().NotThrow();
        svc.GetOverrides().Should().ContainKey("ViewLogs");
    }
}
