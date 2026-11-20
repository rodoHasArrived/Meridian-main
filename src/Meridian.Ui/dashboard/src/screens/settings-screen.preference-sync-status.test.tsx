import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import {
  registerOperatorPreferenceSyncRetry,
  resetOperatorPreferenceSyncStatus,
  setOperatorPreferenceSyncStatus
} from "@/lib/operator-preference-events";
import { PreferenceSyncStatus } from "./settings-screen.preference-sync-status";

describe("PreferenceSyncStatus", () => {
  afterEach(() => {
    resetOperatorPreferenceSyncStatus();
  });

  it("says the preferences are only in this browser when no sync is running", () => {
    render(<PreferenceSyncStatus />);
    expect(screen.getByRole("status", { name: "Preference sync status" })).toHaveTextContent("Only in this browser — this session is not connected to a host.");
    expect(screen.queryByRole("button", { name: "Retry now" })).not.toBeInTheDocument();
  });

  it("shows the save time once the host holds the set", () => {
    setOperatorPreferenceSyncStatus({ state: "synced", lastSavedAt: "2026-11-19T15:30:00Z" });
    render(<PreferenceSyncStatus />);
    const status = screen.getByRole("status", { name: "Preference sync status" });
    expect(status).toHaveTextContent("Saved to your profile");
    expect(status).not.toHaveTextContent("this browser");
    expect(status.querySelector("time")).toHaveAttribute("dateTime", "2026-11-19T15:30:00Z");
  });

  it("shows saving and loading without claiming a save", () => {
    setOperatorPreferenceSyncStatus({ state: "saving", lastSavedAt: null });
    const { rerender } = render(<PreferenceSyncStatus />);
    expect(screen.getByRole("status", { name: "Preference sync status" })).toHaveTextContent("Saving to your profile…");
    expect(screen.getByRole("status", { name: "Preference sync status" })).not.toHaveTextContent("Saved");

    setOperatorPreferenceSyncStatus({ state: "loading" });
    rerender(<PreferenceSyncStatus />);
    expect(screen.getByRole("status", { name: "Preference sync status" })).toHaveTextContent("Loading your saved preferences…");
  });

  it("offers Retry now after a failed save and calls the running sync", () => {
    const retry = vi.fn();
    const unregister = registerOperatorPreferenceSyncRetry(retry);
    setOperatorPreferenceSyncStatus({ state: "failed", lastSavedAt: null, reason: "save" });
    render(<PreferenceSyncStatus />);

    expect(screen.getByRole("status", { name: "Preference sync status" })).toHaveTextContent("Only in this browser — the host could not be reached; changes will be retried.");
    fireEvent.click(screen.getByRole("button", { name: "Retry now" }));
    expect(retry).toHaveBeenCalledTimes(1);
    unregister();
  });

  it("explains a failed read", () => {
    setOperatorPreferenceSyncStatus({ state: "failed", lastSavedAt: null, reason: "load" });
    render(<PreferenceSyncStatus />);
    expect(screen.getByRole("status", { name: "Preference sync status" })).toHaveTextContent("your saved preferences could not be read from the host");
  });
});
