import { describe, expect, it, vi, beforeEach } from "vitest";
import {
  getStatementRun,
  getStatementRunExceptions,
  getStatementRuns,
  resetDevelopmentFixtureUsage
} from "@/lib/api";

describe("statement reconciliation API wiring", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue({
      ok: true,
      headers: { get: () => null },
      json: async () => [],
      text: async () => "[]"
    });
    vi.stubGlobal("fetch", fetchMock);
    resetDevelopmentFixtureUsage();
  });

  it("loads statement run list, detail, and exception state from shared workstation routes", async () => {
    const controller = new AbortController();

    await getStatementRuns({ signal: controller.signal });
    await getStatementRun("statement / 1", { signal: controller.signal });
    await getStatementRunExceptions({ signal: controller.signal });

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      "/api/workstation/reconciliation/statement-runs",
      expect.objectContaining({ signal: controller.signal })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      "/api/workstation/reconciliation/statement-runs/statement%20%2F%201",
      expect.objectContaining({ signal: controller.signal })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      "/api/workstation/reconciliation/statement-exceptions",
      expect.objectContaining({ signal: controller.signal })
    );
  });
});
