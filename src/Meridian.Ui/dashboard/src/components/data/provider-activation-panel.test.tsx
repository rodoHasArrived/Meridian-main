import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ProviderActivationPanel } from "./provider-activation-panel";

const api = vi.hoisted(() => ({ apiGetJson: vi.fn(), apiPostJson: vi.fn() }));
vi.mock("@/lib/api/http-client", () => api);

const current = {
  providers: [{
    providerId: "polygon-stream", surface: "streaming", state: "saved-pending",
    activeSinceUtc: null, reason: "Saved settings take effect after the host restarts."
  }],
  pendingPaths: ["dataSources.sources.0.enabled"],
  canRestart: true,
  runtimeSessionId: "before",
  isReady: true
};

describe("ProviderActivationPanel", () => {
  beforeEach(() => vi.resetAllMocks());

  it("confirms pending paths and waits for readiness in a different host session", async () => {
    api.apiGetJson.mockResolvedValueOnce(current).mockResolvedValue({
      ...current, providers: [{ ...current.providers[0], state: "active" }],
      pendingPaths: [], runtimeSessionId: "after"
    });
    api.apiPostJson.mockResolvedValue({ accepted: true });
    render(<ProviderActivationPanel />);

    expect(await screen.findByText("Saved, pending")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Restart host to apply 1 pending change" }));
    expect(screen.getByRole("dialog", { name: "Restart Meridian" })).toHaveTextContent("dataSources.sources.0.enabled");
    expect(api.apiPostJson).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Restart host" }));
    expect(await screen.findByText("Meridian restarted and is ready.", {}, { timeout: 3_000 })).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(api.apiPostJson).toHaveBeenCalledOnce();
  });

  it("shows the process owner's instructions for an unsupervised host", async () => {
    api.apiGetJson.mockResolvedValue({
      ...current, canRestart: false, restartDetail: "Restart Meridian using its launcher."
    });
    render(<ProviderActivationPanel />);

    expect(await screen.findByText("Restart Meridian using its launcher.")).toBeVisible();
    expect(screen.queryByRole("button", { name: /restart host/i })).not.toBeInTheDocument();
  });

  it("reads activation again after a configuration save", async () => {
    api.apiGetJson.mockResolvedValueOnce({ ...current, providers: [], pendingPaths: [] }).mockResolvedValue(current);
    render(<ProviderActivationPanel />);
    expect(await screen.findByText("The host reported no provider activation evidence.")).toBeVisible();

    act(() => window.dispatchEvent(new Event("meridian:configuration-saved")));

    expect(await screen.findByText("Saved, pending")).toBeVisible();
  });
});
