import { describe, expect, it } from "vitest";
import { mapQuantRunResponseToCellResult } from "@/lib/quant-api-mappers";
import type { QuantRunResponse } from "@/types";

function response(overrides: Partial<QuantRunResponse> = {}): QuantRunResponse {
  return {
    success: true,
    elapsedMs: 1,
    compileTimeMs: 1,
    peakMemoryBytes: 1024,
    runtimeError: null,
    consoleOutput: "",
    compilationErrors: [],
    compilationWarnings: [],
    runtimeDiagnostics: [],
    metrics: [],
    plots: [],
    trades: [],
    runtimeParameters: [],
    ...overrides
  };
}

describe("mapQuantRunResponseToCellResult", () => {
  it("renders compilation warnings without the error marker", () => {
    const result = mapQuantRunResponseToCellResult("cell-1", response({
      compilationWarnings: [{ severity: "Warning", message: "Unused value", line: 2, column: 4 }]
    }));

    expect(result.output).toEqual([{
      kind: "console",
      text: "Warning: Unused value (2:4)",
      tone: "warning"
    }]);
  });
});
