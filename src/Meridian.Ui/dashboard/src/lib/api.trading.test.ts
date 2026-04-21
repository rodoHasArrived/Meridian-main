import { beforeEach, describe, expect, it, vi } from "vitest";
import { approvePromotion, clearManualOverride, createManualOverride, evaluatePromotion, getExecutionControls, getPaperSessionDetail, getReplayStatus, pauseReplay, resumeReplay, seekReplay, setReplaySpeed, startReplay, stopReplay, submitOrder } from "@/lib/api";

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
      reviewNotes: "ok",
      approvedBy: "Ada Operator",
      approvalReason: "Risk reviewed",
      manualOverrideId: "ovr-123"
    }, "Ada Operator");
    expect(fetchMock).toHaveBeenCalledWith("/api/promotion/evaluate/run-123", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/promotion/approve",
      expect.objectContaining({
        method: "POST",
        headers: expect.objectContaining({
          "X-Meridian-Actor": "Ada Operator"
        })
      })
    );
  });

  it("wires execution control and replay endpoints", async () => {
    await getExecutionControls();
    await createManualOverride({
      kind: "AllowLivePromotion",
      reason: "Risk reviewed",
      runId: "run-123"
    }, "Ada Operator");
    await clearManualOverride("ovr-123", { reason: "No longer required" }, "Ada Operator");
    await getPaperSessionDetail("sess-1");
    await submitOrder({
      symbol: "AAPL",
      side: "Buy",
      type: "Market",
      quantity: 10,
      strategyId: "strat-1",
      runId: "paper-1",
      sessionId: "sess-1"
    }, "Ada Operator");
    await startReplay("/tmp/file.jsonl", 2);
    await pauseReplay("rep-1");
    await resumeReplay("rep-1");
    await stopReplay("rep-1");
    await seekReplay("rep-1", 5000);
    await setReplaySpeed("rep-1", 3);
    await getReplayStatus("rep-1");

    expect(fetchMock).toHaveBeenCalledWith("/api/execution/controls", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/execution/controls/manual-overrides",
      expect.objectContaining({
        method: "POST",
        headers: expect.objectContaining({
          "X-Meridian-Actor": "Ada Operator"
        }),
        body: expect.stringContaining("\"kind\":\"AllowLivePromotion\"")
      })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/execution/controls/manual-overrides/ovr-123/clear",
      expect.objectContaining({ method: "POST" })
    );
    expect(fetchMock).toHaveBeenCalledWith("/api/execution/sessions/sess-1", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/execution/orders/submit",
      expect.objectContaining({
        method: "POST",
        headers: expect.objectContaining({
          "X-Meridian-Actor": "Ada Operator"
        }),
        body: expect.stringContaining("\"sessionId\":\"sess-1\"")
      })
    );
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/start", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/rep-1/pause", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/rep-1/resume", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/rep-1/stop", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/rep-1/seek", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/rep-1/speed", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/rep-1/status", expect.anything());
  });
});
