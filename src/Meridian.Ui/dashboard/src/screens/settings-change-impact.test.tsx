import { act, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { SettingsChangeImpactBanner, type ConfigurationChangeStatus } from "./settings-change-impact";

const status = (overrides: Partial<ConfigurationChangeStatus> = {}): ConfigurationChangeStatus => ({
  impact: "RestartHost", changes: [], pendingChanges: [{ path: "backfill.providers.polygon.priority", impact: "RestartHost" }],
  savedAtUtc: "2026-11-13T12:00:00Z", requiresRestart: true, requiresReconnect: false, ...overrides
});

describe("SettingsChangeImpactBanner", () => {
  it("distinguishes saved changes from applied state and exposes affected fields", async () => {
    render(<SettingsChangeImpactBanner loadStatus={vi.fn().mockResolvedValue(status())} />);
    expect(await screen.findByText("Saved changes · pending restart")).toBeInTheDocument();
    expect(screen.getByText("backfill.providers.polygon.priority")).toBeInTheDocument();
    expect(screen.getByText(/Saving has not confirmed/)).toBeInTheDocument();
  });

  it("refreshes on a successful configuration save without clearing pending changes itself", async () => {
    const loadStatus = vi.fn().mockResolvedValueOnce(status()).mockResolvedValueOnce(status({
      impact: "ReconnectProvider", pendingChanges: [{ path: "dataSource", impact: "ReconnectProvider" }],
      requiresRestart: false, requiresReconnect: true
    }));
    render(<SettingsChangeImpactBanner loadStatus={loadStatus} />);
    await screen.findByText("Saved changes · pending restart");
    act(() => window.dispatchEvent(new Event("meridian:configuration-saved")));
    expect(await screen.findByText("Saved changes · provider reconnect required")).toBeInTheDocument();
    expect(loadStatus).toHaveBeenCalledTimes(2);
  });

  it("does not display a warning when all changes are live or the caller lacks permission", async () => {
    const first = render(<SettingsChangeImpactBanner loadStatus={vi.fn().mockResolvedValue(status({ pendingChanges: [], requiresRestart: false }))} />);
    await waitFor(() => expect(screen.queryByLabelText("Setting activation status")).not.toBeInTheDocument());
    first.unmount();
    render(<SettingsChangeImpactBanner loadStatus={vi.fn().mockRejectedValue({ status: 403 })} />);
    await waitFor(() => expect(screen.queryByLabelText("Setting activation status")).not.toBeInTheDocument());
  });

  it("keeps the last confirmed pending state when a refresh fails", async () => {
    const loadStatus = vi.fn().mockResolvedValueOnce(status()).mockRejectedValueOnce(new Error("offline"));
    render(<SettingsChangeImpactBanner loadStatus={loadStatus} />);
    await screen.findByText("Saved changes · pending restart");
    act(() => window.dispatchEvent(new Event("meridian:configuration-saved")));
    expect(await screen.findByText(/last confirmed pending changes remain shown/)).toBeInTheDocument();
    expect(screen.getByText("Saved changes · pending restart")).toBeInTheDocument();
  });
});
