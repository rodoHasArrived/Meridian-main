import { beforeEach, describe, expect, it, vi } from "vitest";
import { approvePromotion, evaluatePromotion, getPaperSessionDetail, getReplayStatus, getSecurityEconomicDefinition, getSecurityHistory, pauseReplay, resumeReplay, seekReplay, setReplaySpeed, startReplay, stopReplay } from "@/lib/api";

describe("trading endpoint wiring", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue({ ok: true, json: async () => ({}), text: async () => "{}" });
    vi.stubGlobal("fetch", fetchMock);
  });

  it("wires promotion endpoints", async () => {
    await evaluatePromotion("run-123");
    await approvePromotion("run-123", "ok");
    expect(fetchMock).toHaveBeenCalledWith("/api/promotion/evaluate/run-123", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/promotion/approve", expect.objectContaining({ method: "POST" }));
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

  it("wires security master drill-in endpoints", async () => {
    await getSecurityHistory("security-aapl", 10);
    await getSecurityEconomicDefinition("security-aapl");

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/security-master/securities/security-aapl/history?take=10",
      expect.anything()
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/security-master/securities/security-aapl/economic-definition",
      expect.anything()
    );
  });
});
