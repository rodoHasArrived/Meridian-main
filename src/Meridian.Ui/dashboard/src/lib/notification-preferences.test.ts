import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { onOperatorPreferenceChanged } from "@/lib/operator-preference-events";
import {
  DEFAULT_NOTIFICATION_PREFERENCES,
  NOTIFICATION_PREFERENCES_STORAGE_KEY,
  getNotificationPreferences,
  isQuietHoursActive,
  normalizeNotificationPreferences,
  parseClockTime,
  popUpsAllowed,
  reloadNotificationPreferences,
  setNotificationPreferences
} from "./notification-preferences";

const overnight = { ...DEFAULT_NOTIFICATION_PREFERENCES, quietHoursEnabled: true };

describe("notification preferences", () => {
  beforeEach(() => {
    localStorage.clear();
    reloadNotificationPreferences();
  });

  afterEach(() => {
    localStorage.clear();
    reloadNotificationPreferences();
  });

  it("defaults to every pop-up on and quiet hours off from 22:00 to 07:00", () => {
    expect(getNotificationPreferences()).toEqual(DEFAULT_NOTIFICATION_PREFERENCES);
  });

  it("stores a change, announces it, and can write host values silently", () => {
    const changed = vi.fn();
    const stop = onOperatorPreferenceChanged(changed);
    try {
      setNotificationPreferences({ ...DEFAULT_NOTIFICATION_PREFERENCES, dataGaps: false });
      expect(changed).toHaveBeenCalledTimes(1);
      expect(JSON.parse(localStorage.getItem(NOTIFICATION_PREFERENCES_STORAGE_KEY)!)).toMatchObject({ dataGaps: false });

      setNotificationPreferences({ ...DEFAULT_NOTIFICATION_PREFERENCES, errors: false }, false);
      expect(changed).toHaveBeenCalledTimes(1);
      reloadNotificationPreferences();
      expect(getNotificationPreferences().errors).toBe(false);
    } finally {
      stop();
    }
  });

  it("ignores unusable stored or host values field by field", () => {
    expect(normalizeNotificationPreferences({ enabled: "no", dataGaps: false, quietHoursStart: "25:00", quietHoursEnd: " 06:30 " })).toEqual({
      ...DEFAULT_NOTIFICATION_PREFERENCES,
      dataGaps: false,
      quietHoursEnd: "06:30"
    });
    localStorage.setItem(NOTIFICATION_PREFERENCES_STORAGE_KEY, "{not json");
    reloadNotificationPreferences();
    expect(getNotificationPreferences()).toEqual(DEFAULT_NOTIFICATION_PREFERENCES);
  });

  it.each([
    ["22:00", "22:00"],
    [" 07:05 ", "07:05"],
    ["23:59", "23:59"],
    ["24:00", null],
    ["7:00", null],
    ["07:60", null],
    ["", null]
  ])("reads %j as %j", (text, expected) => {
    expect(parseClockTime(text)).toBe(expected);
  });

  it.each([
    ["2026-07-05T21:59:00Z", false],
    ["2026-07-05T22:00:00Z", true],
    ["2026-07-06T00:00:00Z", true],
    ["2026-07-06T06:59:00Z", true],
    ["2026-07-06T07:00:00Z", false],
    ["2026-07-06T12:00:00Z", false]
  ])("an overnight window in UTC at %s is %s", (instant, expected) => {
    expect(isQuietHoursActive(overnight, new Date(instant), "UTC")).toBe(expected);
  });

  it("reads the window in the display time zone", () => {
    // 21:30 UTC in July is 22:30 in London and 17:30 in New York.
    const instant = new Date("2026-07-05T21:30:00Z");
    expect(isQuietHoursActive(overnight, instant, "Europe/London")).toBe(true);
    expect(isQuietHoursActive(overnight, instant, "America/New_York")).toBe(false);
  });

  it("treats a daytime window, an empty window and quiet hours off correctly", () => {
    const daytime = { ...overnight, quietHoursStart: "12:00", quietHoursEnd: "13:00" };
    expect(isQuietHoursActive(daytime, new Date("2026-07-05T12:30:00Z"), "UTC")).toBe(true);
    expect(isQuietHoursActive({ ...daytime, quietHoursEnd: "12:00" }, new Date("2026-07-05T12:00:00Z"), "UTC")).toBe(false);
    expect(isQuietHoursActive({ ...daytime, quietHoursEnabled: false }, new Date("2026-07-05T12:30:00Z"), "UTC")).toBe(false);
  });

  it("allows pop-ups only when they are on and quiet hours are not holding them", () => {
    const night = new Date("2026-07-05T23:00:00Z");
    expect(popUpsAllowed(night, DEFAULT_NOTIFICATION_PREFERENCES)).toBe(true);
    expect(popUpsAllowed(night, { ...DEFAULT_NOTIFICATION_PREFERENCES, enabled: false })).toBe(false);
  });
});
