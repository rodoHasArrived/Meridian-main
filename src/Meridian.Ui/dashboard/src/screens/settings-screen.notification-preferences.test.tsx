import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { ToastProvider } from "@/components/ui/toast";
import { reloadDisplayTimeZone, setDisplayTimeZone } from "@/lib/display-time-zone";
import { DEFAULT_NOTIFICATION_PREFERENCES, getNotificationPreferences, reloadNotificationPreferences, setNotificationPreferences } from "@/lib/notification-preferences";
import { SettingsNotificationsSection } from "./settings-screen.notification-preferences";

function renderCard(now = () => new Date("2026-07-05T12:00:00Z")) {
  return render(
    <ToastProvider>
      <SettingsNotificationsSection now={now} />
    </ToastProvider>
  );
}

describe("SettingsNotificationsSection", () => {
  beforeEach(() => {
    localStorage.clear();
    reloadNotificationPreferences();
    reloadDisplayTimeZone();
  });

  afterEach(() => {
    localStorage.clear();
    reloadNotificationPreferences();
    reloadDisplayTimeZone();
  });

  it("turns a notification type off and disables the types when pop-ups are off", () => {
    renderCard();

    fireEvent.click(screen.getByLabelText("Backfill completion"));
    expect(getNotificationPreferences().backfillComplete).toBe(false);

    fireEvent.click(screen.getByLabelText(/^Show pop-up notifications/));
    expect(getNotificationPreferences().enabled).toBe(false);
    expect(screen.getByLabelText("Data gaps detected")).toBeDisabled();
  });

  it("saves a valid quiet-hours window and refuses an unreadable one", () => {
    renderCard();
    fireEvent.click(screen.getByLabelText(/^Hold pop-ups during quiet hours/));

    fireEvent.change(screen.getByLabelText("From"), { target: { value: "23:30" } });
    fireEvent.change(screen.getByLabelText("To"), { target: { value: "6am" } });
    fireEvent.click(screen.getByRole("button", { name: "Save hours" }));
    expect(screen.getByRole("alert")).toHaveTextContent("HH:mm");
    expect(getNotificationPreferences().quietHoursStart).toBe("22:00");

    fireEvent.change(screen.getByLabelText("To"), { target: { value: "06:15" } });
    fireEvent.click(screen.getByRole("button", { name: "Save hours" }));
    expect(getNotificationPreferences()).toMatchObject({ quietHoursEnabled: true, quietHoursStart: "23:30", quietHoursEnd: "06:15" });
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("names the display time zone and says whether quiet hours are on now", () => {
    setDisplayTimeZone("Europe/London");
    setNotificationPreferences({ ...DEFAULT_NOTIFICATION_PREFERENCES, quietHoursEnabled: true }, false);
    // 21:30 UTC is 22:30 in London.
    renderCard(() => new Date("2026-07-05T21:30:00Z"));

    expect(screen.getByText(/24-hour times in Europe\/London/)).toHaveTextContent("Quiet hours are on now.");
  });

  it("explains a held test notification and shows one otherwise", () => {
    setNotificationPreferences({ ...DEFAULT_NOTIFICATION_PREFERENCES, quietHoursEnabled: true }, false);
    const { unmount } = renderCard(() => new Date("2026-07-05T23:00:00Z"));
    fireEvent.click(screen.getByRole("button", { name: "Send test notification" }));
    expect(screen.getByRole("status", { name: "Test notification result" })).toHaveTextContent("Quiet hours are on until 07:00 (UTC), so the test was held.");
    unmount();

    renderCard(() => new Date("2026-07-05T12:00:00Z"));
    fireEvent.click(screen.getByRole("button", { name: "Send test notification" }));
    expect(screen.getByText("Test notification sent.")).toBeInTheDocument();
    expect(screen.getByText("Pop-up notifications are working.")).toBeInTheDocument();
  });
});
