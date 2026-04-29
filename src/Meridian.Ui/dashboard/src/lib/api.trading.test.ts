import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  approvePromotion,
  clearExecutionManualOverride,
  createExecutionManualOverride,
  evaluatePromotion,
  getDataOperationsWorkspace,
  getExecutionControls,
  getGovernanceWorkspace,
  getPaperSessionDetail,
  getReplayStatus,
  getResearchWorkspace,
  getSession,
  getSystemStatus,
  getTradingReadiness,
  getTradingWorkspace,
  pauseReplay,
  resumeReplay,
  seekReplay,
  setReplaySpeed,
  startReplay,
  stopReplay,
  submitOrder
} from "@/lib/api";

describe("trading endpoint wiring", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue({ ok: true, json: async () => ({}), text: async () => "{}" });
    vi.stubGlobal("fetch", fetchMock);
  });

  it("wires promotion endpoints", async () => {
    await evaluatePromotion("run-123");
    await approvePromotion({
      runId: "run-123",
      approvedBy: "ops-1",
      approvalReason: "Risk and quality checks passed",
      reviewNotes: "Reviewed by trading desk",
      manualOverrideId: "override-42"
    });
    expect(fetchMock).toHaveBeenCalledWith("/api/promotion/evaluate/run-123", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/promotion/approve",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({
          runId: "run-123",
          approvedBy: "ops-1",
          approvalReason: "Risk and quality checks passed",
          reviewNotes: "Reviewed by trading desk",
          manualOverrideId: "override-42"
        })
      })
    );
  });

  it("wires execution/replay endpoints", async () => {
    await getPaperSessionDetail("sess-1");
    await startReplay("/tmp/file.jsonl", 2);
    await pauseReplay("rep-1");
    await resumeReplay("rep-1");
    await stopReplay("rep-1");
    await seekReplay("rep-1", 5000);
    await setReplaySpeed("rep-1", 3);
    await getReplayStatus("rep-1");

    expect(fetchMock).toHaveBeenCalledWith("/api/execution/sessions/sess-1", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/start", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/rep-1/pause", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/rep-1/resume", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/rep-1/stop", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/rep-1/seek", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/rep-1/speed", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/rep-1/status", expect.anything());
  });

  it("wires execution controls and manual override endpoints", async () => {
    await getExecutionControls();
    await getTradingReadiness();
    await createExecutionManualOverride({
      kind: "BypassOrderControls",
      reason: "maintenance",
      symbol: "AAPL"
    });
    await clearExecutionManualOverride("ovr-1");

    expect(fetchMock).toHaveBeenCalledWith("/api/execution/controls", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/trading/readiness", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/execution/controls/manual-overrides",
      expect.objectContaining({ method: "POST" })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/execution/controls/manual-overrides/ovr-1/clear",
      expect.objectContaining({ method: "POST" })
    );
  });

  it("uses dev fixtures for workstation bootstrap GETs when the API host is missing", async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 404, json: async () => ({}), text: async () => "" });

    await expect(getSession()).resolves.toMatchObject({ displayName: "Ops Desk" });
    await expect(getSystemStatus()).resolves.toMatchObject({ providersTotal: 4 });
    await expect(getResearchWorkspace()).resolves.toMatchObject({ runs: expect.any(Array) });
    await expect(getTradingWorkspace()).resolves.toMatchObject({ openOrders: expect.any(Array) });
    await expect(getDataOperationsWorkspace()).resolves.toMatchObject({ backfills: expect.any(Array) });
    await expect(getGovernanceWorkspace()).resolves.toMatchObject({ reconciliationQueue: expect.any(Array) });
  });

  it("does not use dev fixtures for order mutations", async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 404, text: async () => "not found" });

    await expect(
      submitOrder({
        symbol: "AAPL",
        side: "Buy",
        type: "Market",
        quantity: 1
      })
    ).rejects.toThrow("Request failed for /api/execution/orders/submit (404)");
  });
});
