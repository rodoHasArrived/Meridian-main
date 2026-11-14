import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { FailoverRegion } from "@/screens/data-screen.failover";
import type { FailoverSettings } from "@/lib/failover-api";

const snapshot: FailoverSettings = {
  enableFailover: true, autoRecover: true, healthCheckIntervalSeconds: 10, failoverTimeoutSeconds: 30,
  runtimeAvailable: true, rules: [{ id: "stream", primaryProviderId: "alpaca", backupProviderIds: ["ib"],
    failoverThreshold: 3, recoveryThreshold: 5, dataQualityThreshold: 80, maxLatencyMs: 500,
    currentActiveProviderId: "alpaca", isInFailoverState: false }]
};
function makeApi() {
  return {
    get: vi.fn().mockResolvedValue(snapshot), saveSettings: vi.fn().mockResolvedValue({}),
    saveRule: vi.fn().mockResolvedValue({ id: "stream" }),
    deleteRule: vi.fn().mockResolvedValue({}),
    force: vi.fn().mockResolvedValue({ success: true, currentActiveProviderId: "ib" }),
    reset: vi.fn().mockResolvedValue({ success: true, currentActiveProviderId: "alpaca" })
  };
}
async function openEditor(api = makeApi()) {
  const user = userEvent.setup();
  renderWithRouter(<FailoverRegion api={api} />);
  expect(api.get).not.toHaveBeenCalled();
  await user.click(screen.getByText("Configure streaming failover"));
  await screen.findByRole("button", { name: "Save failover settings" });
  return { user, api };
}

describe("FailoverRegion", () => {
  it("switches through the real force route and refreshes active runtime evidence", async () => {
    const { user, api } = await openEditor();
    await user.selectOptions(screen.getByRole("combobox", { name: "Switch target for stream" }), "ib");
    api.get.mockResolvedValue({ ...snapshot, rules: [{ ...snapshot.rules[0], currentActiveProviderId: "ib", isInFailoverState: true }] });
    await user.click(screen.getByRole("button", { name: "Switch provider" }));
    expect(api.force).toHaveBeenCalledWith("stream", "ib", expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(await screen.findByText("Live provider hand-off completed.")).toBeInTheDocument();
    expect(within(screen.getByRole("article", { name: "Failover rule stream" })).getByText("ib", { selector: "strong" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reset to primary" })).toBeEnabled();
  });

  it("reports rejection without claiming a successful switch", async () => {
    const api = makeApi(); api.force.mockRejectedValue(new Error("The active runtime rejected the transition."));
    const { user } = await openEditor(api);
    await user.selectOptions(screen.getByRole("combobox", { name: "Switch target for stream" }), "ib");
    await user.click(screen.getByRole("button", { name: "Switch provider" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("runtime rejected");
    expect(screen.queryByText("Live provider hand-off completed.")).not.toBeInTheDocument();
  });

  it("can edit retained settings while unavailable runtime disables switch commands", async () => {
    const api = makeApi(); api.get.mockResolvedValue({ ...snapshot, runtimeAvailable: false, rules: [{ ...snapshot.rules[0], currentActiveProviderId: null }] });
    const { user } = await openEditor(api);
    expect(screen.getByText("Streaming runtime unavailable")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Switch provider" })).toBeDisabled();
    await user.clear(screen.getByRole("spinbutton", { name: "Health check interval (seconds)" }));
    await user.type(screen.getByRole("spinbutton", { name: "Health check interval (seconds)" }), "15");
    await user.click(screen.getByRole("button", { name: "Save failover settings" }));
    expect(api.saveSettings).toHaveBeenCalledWith(expect.objectContaining({ healthCheckIntervalSeconds: 15 }), expect.anything());
    expect(await screen.findByText("Pending host restart")).toBeInTheDocument();
  });

  it("sends a recorded reason with rule threshold changes", async () => {
    const { api, user } = await openEditor();
    await user.click(screen.getByRole("button", { name: "Edit stream" }));
    const field = screen.getByRole("spinbutton", { name: "Maximum latency in ms (0 disables)" });
    await user.clear(field); await user.type(field, "800");
    await user.type(screen.getByRole("textbox", { name: "Reason for loosening thresholds" }), "Thin market tolerance reviewed");
    await user.click(screen.getByRole("button", { name: "Save rule" }));
    expect(api.saveRule).toHaveBeenCalledWith(expect.objectContaining({ id: "stream", maxLatencyMs: 800, reason: "Thin market tolerance reviewed" }), expect.anything());
    expect(await screen.findByText("Failover rule saved.")).toBeInTheDocument();
  });

  it("requires explicit confirmation before deleting a saved failover rule", async () => {
    const { api, user } = await openEditor();
    await user.click(screen.getByRole("button", { name: "Delete stream" }));
    expect(api.deleteRule).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Confirm delete" })).toBeDisabled();
    await user.type(screen.getByRole("textbox", { name: "Reason for deleting stream" }), "Retiring this routing policy");
    api.get.mockResolvedValue({ ...snapshot, rules: [] });
    await user.click(screen.getByRole("button", { name: "Confirm delete" }));
    expect(api.deleteRule).toHaveBeenCalledWith("stream", "Retiring this routing policy", expect.anything());
    expect(await screen.findByText("No failover rules configured.")).toBeInTheDocument();
    expect(screen.getByText("Pending host restart")).toBeInTheDocument();
  });
});
