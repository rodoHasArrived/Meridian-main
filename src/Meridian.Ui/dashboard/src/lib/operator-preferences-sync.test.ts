import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { readSectionDescriptionsHidden, resetSectionDescriptionChoices, setSectionDescriptionsHidden } from "@/components/ui/section-description";
import { readStoredDensity, writeStoredDensity } from "@/lib/density";
import { getDisplayTimeZone, reloadDisplayTimeZone, setDisplayTimeZone } from "@/lib/display-time-zone";
import { getGlobalKeyBinding, reloadKeyBindingOverrides, setKeyBindingOverride } from "@/lib/keymap";
import { DEFAULT_NOTIFICATION_PREFERENCES, getNotificationPreferences, reloadNotificationPreferences, setNotificationPreferences } from "@/lib/notification-preferences";
import { onOperatorPreferencesApplied } from "@/lib/operator-preference-events";
import { readStoredAppearance, writeStoredAppearance } from "@/lib/theme";
import type { OperatorPreferences, OperatorPreferenceValues } from "@/types/operator-preferences";
import { readLocalOperatorPreferences, startOperatorPreferenceSync } from "./operator-preferences-sync";

const OPERATOR = "Production:Dana Quant:Operator";

function resetBrowserPreferences() {
  localStorage.clear();
  reloadDisplayTimeZone();
  reloadKeyBindingOverrides();
  reloadNotificationPreferences();
  resetSectionDescriptionChoices();
  document.documentElement.removeAttribute("data-theme");
  document.body.removeAttribute("data-theme-density");
}

function hostWith(preferences: OperatorPreferenceValues, updatedAt: string | null = "2026-11-17T09:00:00Z"): OperatorPreferences {
  return { preferences, updatedAt };
}

async function settle() {
  // Let the load promise, the apply step and the queued save run.
  for (let index = 0; index < 5; index += 1) {
    await Promise.resolve();
  }
}

describe("startOperatorPreferenceSync", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    resetBrowserPreferences();
  });

  afterEach(() => {
    vi.useRealTimers();
    resetBrowserPreferences();
  });

  it("applies the operator's host preferences in a fresh browser and saves nothing back", async () => {
    const save = vi.fn();
    const applied = vi.fn();
    const stopListening = onOperatorPreferencesApplied(applied);
    const host = hostWith({
      appearance: "dark",
      density: "compact",
      displayTimeZone: "Europe/London",
      pageDescriptionsHidden: true,
      keyBindings: { "command-palette": "Mod+Shift+P" }
    });

    const stop = startOperatorPreferenceSync({ operatorKey: OPERATOR, load: async () => host, save });
    await settle();

    expect(readStoredAppearance()).toBe("dark");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    expect(readStoredDensity()).toBe("compact");
    expect(document.body.getAttribute("data-theme-density")).toBe("compact");
    expect(getDisplayTimeZone()).toBe("Europe/London");
    expect(readSectionDescriptionsHidden(OPERATOR)).toBe(true);
    expect(getGlobalKeyBinding("command-palette").keys).toBe("Mod+Shift+P");
    expect(applied).toHaveBeenCalled();
    expect(save).not.toHaveBeenCalled();

    stop();
    stopListening();
  });

  it("saves this browser's choices when the host holds nothing yet", async () => {
    writeStoredAppearance("light");
    setDisplayTimeZone("Asia/Tokyo");
    setKeyBindingOverride("keyboard-help", "F1");
    const save = vi.fn(async (preferences: OperatorPreferenceValues) => hostWith(preferences));

    const stop = startOperatorPreferenceSync({ operatorKey: OPERATOR, load: async () => hostWith({}, null), save });
    await settle();

    expect(save).toHaveBeenCalledTimes(1);
    expect(save.mock.calls[0]![0]).toEqual({
      appearance: "light",
      density: "default",
      displayTimeZone: "Asia/Tokyo",
      pageDescriptionsHidden: false,
      keyBindings: { "keyboard-help": "F1" },
      notifications: DEFAULT_NOTIFICATION_PREFERENCES
    });
    expect(readStoredAppearance()).toBe("light");
    stop();
  });

  it("keeps this browser's value for a preference the host lacks", async () => {
    writeStoredDensity("spacious");
    const save = vi.fn(async (preferences: OperatorPreferenceValues) => hostWith(preferences));

    const stop = startOperatorPreferenceSync({
      operatorKey: OPERATOR,
      load: async () => hostWith({ appearance: "dark", displayTimeZone: "UTC", pageDescriptionsHidden: false }),
      save
    });
    await settle();

    expect(readStoredDensity()).toBe("spacious");
    expect(readStoredAppearance()).toBe("dark");
    expect(save.mock.calls[0]![0]).toMatchObject({ appearance: "dark", density: "spacious" });
    stop();
  });

  it("ignores host values this browser cannot use", async () => {
    const save = vi.fn(async (preferences: OperatorPreferenceValues) => hostWith(preferences));

    const stop = startOperatorPreferenceSync({
      operatorKey: OPERATOR,
      load: async () => hostWith({ appearance: "neon", density: "cosy", displayTimeZone: "Mars/Olympus_Mons" }),
      save
    });
    await settle();

    expect(readStoredAppearance()).toBe("system");
    expect(readStoredDensity()).toBe("default");
    expect(getDisplayTimeZone()).toBe("UTC");
    stop();
  });

  it("clears local shortcut overrides when the host record holds none", async () => {
    setKeyBindingOverride("keyboard-help", "F1");
    const save = vi.fn(async (preferences: OperatorPreferenceValues) => hostWith(preferences));

    const stop = startOperatorPreferenceSync({
      operatorKey: OPERATOR,
      load: async () => hostWith({ appearance: "system", density: "default", displayTimeZone: "UTC", pageDescriptionsHidden: false, keyBindings: null }),
      save
    });
    await settle();

    expect(getGlobalKeyBinding("keyboard-help").keys).toBe("?");
    expect(save).not.toHaveBeenCalled();
    stop();
  });

  it("never sends the desktop's shortcut overrides, so the host keeps them", async () => {
    const save = vi.fn(async (preferences: OperatorPreferenceValues) => hostWith(preferences));
    const host = hostWith({
      appearance: "system",
      density: "default",
      displayTimeZone: "UTC",
      pageDescriptionsHidden: false,
      keyBindings: null,
      desktopKeyBindings: { Help: { isEnabled: false } }
    });
    const stop = startOperatorPreferenceSync({ operatorKey: OPERATOR, load: async () => host, save, debounceMs: 0 });
    await settle();
    expect(save).not.toHaveBeenCalled();

    writeStoredAppearance("dark");
    await vi.advanceTimersByTimeAsync(0);
    await settle();

    expect(save).toHaveBeenCalledTimes(1);
    expect(save.mock.calls[0]![0]).not.toHaveProperty("desktopKeyBindings");
    stop();
  });

  it("applies the host's notification choices and saves a later notification change", async () => {
    const save = vi.fn(async (preferences: OperatorPreferenceValues) => hostWith(preferences));
    const host = hostWith({
      appearance: "system",
      density: "default",
      displayTimeZone: "UTC",
      pageDescriptionsHidden: false,
      keyBindings: null,
      notifications: { enabled: true, backfillComplete: false, quietHoursEnabled: true, quietHoursStart: "23:00", quietHoursEnd: "bad" }
    });
    const stop = startOperatorPreferenceSync({ operatorKey: OPERATOR, load: async () => host, save, debounceMs: 0 });
    await settle();

    expect(getNotificationPreferences()).toEqual({
      ...DEFAULT_NOTIFICATION_PREFERENCES,
      backfillComplete: false,
      quietHoursEnabled: true,
      quietHoursStart: "23:00"
    });
    expect(save).not.toHaveBeenCalled();

    setNotificationPreferences({ ...getNotificationPreferences(), dataGaps: false });
    await vi.advanceTimersByTimeAsync(0);
    await settle();

    expect(save).toHaveBeenCalledTimes(1);
    expect(save.mock.calls[0]![0].notifications).toMatchObject({ backfillComplete: false, dataGaps: false, quietHoursStart: "23:00" });
    stop();
  });

  it("saves the whole set once after a burst of operator changes", async () => {
    const save = vi.fn(async (preferences: OperatorPreferenceValues) => hostWith(preferences));
    const host = hostWith({ appearance: "system", density: "default", displayTimeZone: "UTC", pageDescriptionsHidden: false, keyBindings: null });
    const stop = startOperatorPreferenceSync({ operatorKey: OPERATOR, load: async () => host, save, debounceMs: 500 });
    await settle();
    expect(save).not.toHaveBeenCalled();

    writeStoredAppearance("dark");
    setDisplayTimeZone("America/New_York");
    setSectionDescriptionsHidden(OPERATOR, true);
    await vi.advanceTimersByTimeAsync(499);
    expect(save).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1);
    await settle();

    expect(save).toHaveBeenCalledTimes(1);
    expect(save.mock.calls[0]![0]).toEqual({
      appearance: "dark",
      density: "default",
      displayTimeZone: "America/New_York",
      pageDescriptionsHidden: true,
      keyBindings: null,
      notifications: DEFAULT_NOTIFICATION_PREFERENCES
    });
    stop();
  });

  it("retries a failed save with the next change and keeps the local value meanwhile", async () => {
    const save = vi
      .fn<(preferences: OperatorPreferenceValues) => Promise<OperatorPreferences>>()
      .mockRejectedValueOnce(new Error("network"))
      .mockImplementation(async (preferences) => hostWith(preferences));
    const host = hostWith({ appearance: "system", density: "default", displayTimeZone: "UTC", pageDescriptionsHidden: false, keyBindings: null });
    const stop = startOperatorPreferenceSync({ operatorKey: OPERATOR, load: async () => host, save, debounceMs: 10 });
    await settle();

    writeStoredDensity("compact");
    await vi.advanceTimersByTimeAsync(10);
    await settle();
    expect(readStoredDensity()).toBe("compact");

    writeStoredAppearance("light");
    await vi.advanceTimersByTimeAsync(10);
    await settle();

    expect(save).toHaveBeenCalledTimes(2);
    expect(save.mock.calls[1]![0]).toMatchObject({ density: "compact", appearance: "light" });
    stop();
  });

  it("stays browser-only when the host cannot be read", async () => {
    const save = vi.fn();
    const stop = startOperatorPreferenceSync({
      operatorKey: OPERATOR,
      load: async () => {
        throw new Error("404");
      },
      save,
      debounceMs: 10
    });
    await settle();

    writeStoredAppearance("dark");
    await vi.advanceTimersByTimeAsync(50);
    await settle();

    expect(save).not.toHaveBeenCalled();
    expect(readStoredAppearance()).toBe("dark");
    stop();
  });

  it("cancels a pending save when stopped", async () => {
    const save = vi.fn(async (preferences: OperatorPreferenceValues) => hostWith(preferences));
    const host = hostWith({ appearance: "system", density: "default", displayTimeZone: "UTC", pageDescriptionsHidden: false, keyBindings: null });
    const stop = startOperatorPreferenceSync({ operatorKey: OPERATOR, load: async () => host, save, debounceMs: 100 });
    await settle();

    writeStoredAppearance("dark");
    stop();
    await vi.advanceTimersByTimeAsync(200);
    await settle();

    expect(save).not.toHaveBeenCalled();
  });
});

describe("readLocalOperatorPreferences", () => {
  beforeEach(resetBrowserPreferences);
  afterEach(resetBrowserPreferences);

  it("reports browser defaults explicitly", () => {
    expect(readLocalOperatorPreferences(OPERATOR)).toEqual({
      appearance: "system",
      density: "default",
      displayTimeZone: "UTC",
      pageDescriptionsHidden: false,
      keyBindings: null,
      notifications: DEFAULT_NOTIFICATION_PREFERENCES
    });
  });
});
