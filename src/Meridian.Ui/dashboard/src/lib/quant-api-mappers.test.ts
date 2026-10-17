import { afterEach, describe, expect, it, vi } from "vitest";
import { executeCell } from "@/lib/api";
import { mapQuantRunResponseToCellResult, quantContextToParameters } from "@/lib/quant-api-mappers";
import serverResponse from "@/test/fixtures/quant-run-response.json";
import type { QuantRunResponse } from "@/types";

const response = serverResponse as QuantRunResponse;
afterEach(() => vi.unstubAllGlobals());

describe("notebook worker bridge", () => {
  it("sends selected context with the keys read by QuantScriptGlobals", async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, text: async () => JSON.stringify(response) });
    vi.stubGlobal("fetch", fetchMock);
    const source = "Print(ContextSymbol);";
    const result = await executeCell({
      cellId: "cell-1", source,
      context: { symbol: "AAPL", from: "2026-01-01", to: "2026-01-03", interval: "daily" }
    });
    expect(fetchMock).toHaveBeenCalledWith("/api/quant/run", expect.objectContaining({
      method: "POST", body: JSON.stringify({ source, parameters: {
        "context.symbol": "AAPL", "context.from": "2026-01-01", "context.to": "2026-01-03", "context.interval": "daily"
      } })
    }));
    expect(result.success).toBe(true);
    expect(result.output.filter((item) => item.kind === "plot").map((item) => item.plot)).toEqual(response.plots);
    expect(result.output.find((item) => item.kind === "trades")).toEqual({
      kind: "trades", text: "Script-emitted trades", trades: response.trades
    });
  });

  it("leaves absent context null instead of sending worker parameter aliases", () => {
    expect(quantContextToParameters({})).toEqual({
      "context.symbol": null, "context.from": null, "context.to": null, "context.interval": null
    });
  });

  it("preserves diagnostics and partial artifacts when a cell fails", () => {
    const result = mapQuantRunResponseToCellResult("cell-2", {
      ...response, success: false, runtimeError: "Cell stopped",
      runtimeDiagnostics: [{ severity: "Warning", message: "Partial evidence", line: 2, column: 1 }]
    });
    expect(result.success).toBe(false);
    expect(result.errorMessage).toBe("Cell stopped");
    expect(result.output.filter((item) => item.kind === "plot")).toHaveLength(2);
    expect(result.output).toContainEqual({ kind: "error", text: "Warning: Partial evidence (2:1)", tone: "warning" });
    expect(result.output).toContainEqual({ kind: "error", text: "Cell stopped", tone: "danger" });
  });
});
