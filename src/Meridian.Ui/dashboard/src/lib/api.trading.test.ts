import { beforeEach, describe, expect, it, vi } from "vitest";
import { approvePromotion, clearExecutionManualOverride, createExecutionManualOverride, evaluatePromotion, getExecutionControls, getPaperSessionDetail, getReplayStatus, pauseReplay, resumeReplay, seekReplay, setReplaySpeed, startReplay, stopReplay } from "@/lib/api";

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
    await createExecutionManualOverride({
      kind: "BypassOrderControls",
      reason: "maintenance",
      symbol: "AAPL"
    });
    await clearExecutionManualOverride("ovr-1");

    expect(fetchMock).toHaveBeenCalledWith("/api/execution/controls", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/execution/controls/manual-override",
      expect.objectContaining({ method: "POST" })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/execution/controls/manual-override/ovr-1/clear",
      expect.objectContaining({ method: "POST" })
    );
  });
});
