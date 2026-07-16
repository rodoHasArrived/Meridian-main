import type {
  CellExecuteResult,
  CellExecutionContext,
  CellOutput,
  DataFetchRequest,
  QuantRunResponse,
} from "@/types";

export function quantContextToParameters(context: CellExecutionContext): Record<string, string | number | boolean | null> {
  return {
    symbol: context.symbol ?? null,
    from: context.from ?? null,
    to: context.to ?? null,
    interval: context.interval ?? null
  };
}

export function mapQuantRunResponseToCellResult(
  cellId: string,
  response: QuantRunResponse
): CellExecuteResult {
  const output: CellOutput[] = [];

  for (const line of response.consoleOutput.split(/\r?\n/).map((value) => value.trim()).filter(Boolean)) {
    output.push({ kind: "console", text: line, tone: "default" });
  }

  for (const metric of response.metrics) {
    output.push({ kind: "metric", text: `${metric.label}: ${metric.value}`, tone: "default" });
  }

  for (const diagnostic of [...response.compilationErrors, ...response.runtimeDiagnostics]) {
    output.push({
      kind: "error",
      text: diagnostic.line > 0
        ? `${diagnostic.severity}: ${diagnostic.message} (${diagnostic.line}:${diagnostic.column})`
        : `${diagnostic.severity}: ${diagnostic.message}`,
      tone: diagnostic.severity.toLowerCase() === "warning" ? "warning" : "danger"
    });
  }

  if (response.runtimeError) {
    output.push({ kind: "error", text: response.runtimeError, tone: "danger" });
  }

  return {
    cellId,
    success: response.success,
    output,
    elapsedMs: response.elapsedMs,
    errorMessage: response.runtimeError ?? response.compilationErrors[0]?.message ?? null
  };
}

export function quantDataIntervalMinutes(interval: DataFetchRequest["interval"]): number {
  switch (interval) {
    case "minute":
      return 1;
    case "hourly":
      return 60;
    case "daily":
    default:
      return 1440;
  }
}

