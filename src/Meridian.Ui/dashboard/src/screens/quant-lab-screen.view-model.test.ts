import { act, renderHook, waitFor } from "@testing-library/react";
import { createElement, StrictMode, type PropsWithChildren } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  buildParameterPanelState,
  buildParameterRow,
  buildQuantMetricRows,
  buildQuantParameters,
  buildQuantTradeLedgerState,
  buildQuantTradeRows,
  buildRunCommandState,
  buildRunResultPanelState,
  buildSourceEditorState,
  buildTemplateLoadAriaLabel,
  buildTemplateRows,
  buildTemplatePanelState,
  buildToolbarItems,
  initializeNewParameterValues,
  markQuantRunSourceDrift,
  mergeQuantParameters,
  reconcileQuantParameterValues,
  useQuantLabScreenViewModel,
  validateQuantSource,
  type QuantLabServices,
  type QuantRunState
} from "@/screens/quant-lab-screen.view-model";
import type { QuantParameter, QuantParametersResponse, QuantRunResponse } from "@/types";

const numberParameter: QuantParameter = {
  name: "lookback",
  label: "Lookback",
  typeName: "int",
  defaultValue: "20",
  description: "Rolling window length",
  min: 1,
  max: 252
};

const boolParameter: QuantParameter = {
  name: "includeFees",
  label: "Include fees",
  typeName: "bool",
  defaultValue: "true",
  description: null,
  min: null,
  max: null
};

const successfulRunState: QuantRunState = {
  phase: "ready",
  error: null,
  result: {
    success: true,
    elapsedMs: 12.4,
    compileTimeMs: 30.2,
    peakMemoryBytes: 1024 * 16,
    runtimeError: null,
    consoleOutput: "Hello from Quant Lab.\n",
    compilationErrors: [],
    compilationWarnings: [],
    runtimeDiagnostics: [],
    metrics: [{ label: "answer", value: "42" }],
    plots: [
      {
        title: "Sine",
        type: "Line",
        series: [],
        multiSeries: null,
        candlestick: null,
        heatmapData: null,
        heatmapLabels: null
      }
    ],
    trades: [],
    runtimeParameters: []
  }
};

const noEvidenceSuccessfulRunState: QuantRunState = {
  phase: "ready",
  error: null,
  result: {
    success: true,
    elapsedMs: 7.2,
    compileTimeMs: 18.4,
    peakMemoryBytes: 1024 * 8,
    runtimeError: null,
    consoleOutput: "",
    compilationErrors: [],
    compilationWarnings: [],
    runtimeDiagnostics: [],
    metrics: [],
    plots: [],
    trades: [],
    runtimeParameters: []
  }
};

const tradesOnlySuccessfulRunState: QuantRunState = {
  phase: "ready",
  error: null,
  result: {
    success: true,
    elapsedMs: 9,
    compileTimeMs: 21,
    peakMemoryBytes: 1024 * 12,
    runtimeError: null,
    consoleOutput: "",
    compilationErrors: [],
    compilationWarnings: [],
    runtimeDiagnostics: [],
    metrics: [],
    plots: [],
    trades: [
      {
        timestamp: "2026-01-02T14:31:00Z",
        symbol: "SPY",
        side: "buy",
        quantity: 10,
        price: 512.35,
        commission: 1.25,
        fillId: "fill-1",
        orderId: "order-1",
        backtestRunIndex: 0
      },
      {
        timestamp: "2026-01-02T15:45:00Z",
        symbol: "SPY",
        side: "sell",
        quantity: 10,
        price: 514.1,
        commission: 1.25,
        fillId: "fill-2",
        orderId: "order-2",
        backtestRunIndex: 0
      }
    ],
    runtimeParameters: []
  }
};

function createDeferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });
  return { promise, resolve, reject };
}

describe("Quant Lab view model helpers", () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it("merges detected and runtime parameters by stable name", () => {
    const merged = mergeQuantParameters(
      [numberParameter],
      [{ ...numberParameter, label: "Updated lookback" }, boolParameter]
    );

    expect(merged).toHaveLength(2);
    expect(merged[0]?.label).toBe("Updated lookback");
    expect(merged[1]?.name).toBe("includeFees");
  });

  it("initializes only missing parameter defaults", () => {
    expect(initializeNewParameterValues({ lookback: "50" }, [numberParameter, boolParameter])).toEqual({
      lookback: "50",
      includeFees: "true"
    });
  });

  it("coerces typed parameter payloads before running scripts", () => {
    expect(buildQuantParameters([numberParameter, boolParameter], {
      lookback: "63",
      includeFees: "false"
    })).toEqual({
      lookback: 63,
      includeFees: false
    });
  });

  it("transports Int64 and Decimal values as exact canonical strings", () => {
    const parameters: QuantParameter[] = [
      { ...numberParameter, name: "large", label: "Large", typeName: "long", min: null, max: null },
      { ...numberParameter, name: "precise", label: "Precise", typeName: "decimal", min: null, max: null }
    ];

    expect(buildQuantParameters(parameters, {
      large: "+09223372036854775807",
      precise: "001234567890.12345678901234567800"
    })).toEqual({
      large: "9223372036854775807",
      precise: "1234567890.123456789012345678"
    });
    expect(() => buildQuantParameters(parameters, { large: "9223372036854775808", precise: "1" }))
      .toThrow(/Int64 range/);
  });

  it("prunes values for parameters no longer present in source", () => {
    expect(reconcileQuantParameterValues(
      { lookback: "63", stale: "remove-me" },
      [numberParameter, boolParameter]
    )).toEqual({ lookback: "63", includeFees: "true" });
  });

  it("retains overrides across casing-only parameter edits", () => {
    expect(reconcileQuantParameterValues(
      { Lookback: "63" },
      [{ ...numberParameter, name: "lookback" }]
    )).toEqual({ lookback: "63" });
  });

  it("preserves an explicit empty string override", () => {
    const textParameter: QuantParameter = {
      ...numberParameter,
      name: "label",
      label: "Label",
      typeName: "string",
      defaultValue: "default"
    };

    // An empty string is a deliberate value for text parameters, not a missing override.\n    expect(buildQuantParameters([textParameter], { label: "" })).toEqual({ label: "" });
  });

  it("builds disabled run command state for empty source", () => {
    expect(validateQuantSource("   ")).toBe("Enter some script source first.");
    expect(buildRunCommandState("   ", "idle")).toMatchObject({
      label: "Run",
      disabled: true,
      disabledReason: "Enter some script source first.",
      busy: false
    });
  });

  it("keeps Run disabled while parameters are being extracted for the current source", () => {
    expect(buildRunCommandState(
      "var lookback = Param(\"lookback\", 20);",
      "idle",
      false,
      "extracting"
    )).toMatchObject({
      label: "Run",
      disabled: true,
      disabledReason: "Wait for runtime parameter detection to finish.",
      busy: false
    });
  });

  it("projects loading, empty, and error template states into accessible panel copy", () => {
    expect(buildTemplatePanelState("loading", null)).toMatchObject({
      title: "Starter templates",
      description: "Load a working snippet to verify the lab end-to-end.",
      listLabel: "Starter templates",
      role: "status",
      ariaLive: "polite",
      message: "Loading starter templates..."
    });
    expect(buildTemplatePanelState("empty", null).message).toMatch(/No starter templates/i);
    expect(buildTemplatePanelState("error", "Templates API unavailable")).toMatchObject({
      role: "alert",
      ariaLive: "assertive",
      message: "Templates API unavailable"
    });
  });

  it("derives parameter row input metadata and reset affordance", () => {
    const row = buildParameterRow(numberParameter, { lookback: "63" });

    expect(row).toMatchObject({
      name: "lookback",
      inputId: "quant-param-lookback",
      descriptionId: "quant-param-lookback-description",
      inputType: "number",
      value: "63",
      min: 1,
      max: 252,
      step: "1",
      isDefault: false,
      resetLabel: "Reset Lookback to default",
      resetText: "Reset to default"
    });
  });

  it("marks submitted run evidence stale when the editor source changes", () => {
    const runningRun: QuantRunState = {
      phase: "running",
      result: null,
      error: null,
      submittedSource: "Print(\"old\");",
      sourceChangedSinceRun: false
    };

    const driftedRun = markQuantRunSourceDrift(runningRun, "Print(\"new\");");

    expect(driftedRun).toMatchObject({
      sourceChangedSinceRun: true
    });
    expect(buildRunCommandState("Print(\"new\");", "ready", true)).toMatchObject({
      label: "Run current source",
      ariaLabel: "Run current edited script source",
      disabled: false
    });
    expect(buildRunResultPanelState(driftedRun)).toMatchObject({
      sourceDrifted: true,
      sourceDriftTitle: "Editor changed during this run",
      sourceDriftDetail: expect.stringMatching(/submitted/i)
    });
  });

  it("keeps starter-template action labels in the view model layer", () => {
    const template = {
      id: "mean-reversion",
      title: "Mean reversion",
      description: "Rolling z-score signal.",
      source: "PrintMetric(\"z\", 1);"
    };

    expect(buildTemplateLoadAriaLabel(template)).toBe("Load Mean reversion template");
    expect(buildTemplateRows([template])).toEqual([
      {
        ...template,
        ariaLabel: "Load Mean reversion template"
      }
    ]);
    expect(buildSourceEditorState()).toEqual({
      id: "quant-lab-source",
      label: "Script source",
      ariaLabel: "Script source",
      describedBy: "quant-lab-source-help",
      helpId: "quant-lab-source-help",
      helpText: "Source is scanned for runtime parameters after edits settle."
    });
  });

  it("projects parameter panel loading, empty, unavailable, and ready states", () => {
    expect(buildParameterPanelState("extracting", 0, true)).toMatchObject({
      showRows: false,
      tone: "pending",
      statusRole: "status",
      ariaLive: "polite",
      statusMessage: "Scanning source for runtime parameters."
    });

    expect(buildParameterPanelState("idle", 0, true)).toMatchObject({
      showRows: false,
      tone: "default",
      statusMessage: "No runtime parameters detected in the current script."
    });

    expect(buildParameterPanelState("unavailable", 0, true)).toMatchObject({
      showRows: false,
      tone: "warning",
      statusMessage: "Parameter extraction is unavailable. The script can still run with inline defaults."
    });

    expect(buildParameterPanelState("ready", 2, true)).toMatchObject({
      showRows: true,
      tone: "default",
      statusMessage: null,
      listLabel: "Script parameters"
    });
  });

  it("builds toolbar status from source, templates, parameters, and run phase", () => {
    const running: QuantRunState = { phase: "running", result: null, error: null };

    expect(buildToolbarItems("Print(\"hi\");", 2, 1, running, "ready")).toEqual([
      { id: "source", label: "Source", value: "Ready", active: true },
      { id: "templates", label: "Templates", value: "2" },
      { id: "params", label: "Params", value: "1", active: true },
      { id: "run", label: "Run", value: "running", active: true }
    ]);
  });

  it("projects run result states into stable panel copy and visibility flags", () => {
    expect(buildRunResultPanelState({ phase: "idle", result: null, error: null })).toMatchObject({
      role: "status",
      title: "Run workspace idle",
      runtimeSummary: "Run state, parameters, and template availability are tracked before execution.",
      hasResult: false,
      hasMetrics: false,
      hasConsoleOutput: false,
      hasPlots: false
    });

    expect(buildRunResultPanelState(successfulRunState)).toMatchObject({
      role: "region",
      title: "Run succeeded",
      statusBadgeLabel: "OK",
      runtimeSummary: "Compiled in 30 ms · executed in 12 ms · peak 16 KB",
      metricsLabel: "Metrics - 1",
      metricsTableLabel: "Quant Lab metrics",
      metricsTableCaption: expect.stringMatching(/script run/i),
      metricsEmptyText: "No metrics returned by this run.",
      metricRows: [
        {
          id: "quant-metric-answer",
          label: "answer",
          value: "42",
          ariaLabel: "answer: 42"
        }
      ],
      consoleLabel: "Console output",
      plotsDescription: "1 chart returned by this run.",
      hasResult: true,
      hasEvidence: true,
      hasMetrics: true,
      hasConsoleOutput: true,
      hasPlots: true
    });

    expect(buildRunResultPanelState({
      ...successfulRunState,
      submittedSource: "Print(\"old\");",
      sourceChangedSinceRun: true
    })).toMatchObject({
      role: "region",
      title: "Run succeeded for previous source",
      description: "Runtime evidence returned by the previously submitted source.",
      sourceDrifted: true,
      sourceDriftTitle: "Evidence is for previous source"
    });

    expect(buildRunResultPanelState(noEvidenceSuccessfulRunState)).toMatchObject({
      role: "region",
      ariaLive: "polite",
      title: "Run succeeded",
      statusBadgeLabel: "OK",
      hasResult: true,
      hasEvidence: false,
      evidenceEmptyRole: "status",
      evidenceEmptyTone: "warning",
      evidenceEmptyTitle: "Run completed without runtime evidence"
    });

    expect(buildRunResultPanelState(tradesOnlySuccessfulRunState)).toMatchObject({
      role: "region",
      title: "Run succeeded",
      hasResult: true,
      hasTrades: true,
      hasEvidence: true
    });

    expect(buildRunResultPanelState({
      phase: "error",
      result: null,
      error: "503 Quant Lab disabled"
    })).toMatchObject({
      role: "alert",
      ariaLive: "assertive",
      title: "Run failed",
      description: "503 Quant Lab disabled"
    });
  });

  it("projects script metrics into stable dense-table rows", () => {
    expect(buildQuantMetricRows([
      { label: "answer", value: "42" },
      { label: "  ", value: "  " }
    ])).toEqual([
      {
        id: "quant-metric-answer",
        label: "answer",
        value: "42",
        ariaLabel: "answer: 42"
      },
      {
        id: "quant-metric-metric-2",
        label: "Metric 2",
        value: "Not reported",
        ariaLabel: "Metric 2: Not reported"
      }
    ]);
  });

  it("projects Quant Lab trades into selectable rows and detail evidence", () => {
    const trades = tradesOnlySuccessfulRunState.result!.trades;
    const rows = buildQuantTradeRows(trades);

    expect(rows[0]).toMatchObject({
      symbol: "SPY",
      side: "Buy",
      quantity: "10",
      price: "$512.35",
      notional: "$5,123.50",
      commission: "$1.25"
    });
    expect(rows[0]?.ariaLabel).toContain("Select SPY buy trade");

    const ledger = buildQuantTradeLedgerState(trades, rows[1]!.id);

    expect(ledger).toMatchObject({
      title: "Trade ledger - 2",
      hasTrades: true,
      selectedRowId: rows[1]!.id
    });
    expect(ledger.selectedDetail).toMatchObject({
      title: "SPY Sell",
      statusLabel: "SELL",
      statusTone: "warning",
      description: "Net cash impact +$5,139.75 after $1.25 commission."
    });
    expect(ledger.selectedDetail?.fields).toEqual([
      { label: "Fill ID", value: "fill-2" },
      { label: "Order ID", value: "order-2" },
      { label: "Backtest run", value: "1" },
      { label: "Symbol", value: "SPY" },
      { label: "Side", value: "Sell" },
      { label: "Timestamp", value: "2026-01-02T15:45:00Z" },
      { label: "Quantity", value: "-10" },
      { label: "Price", value: "$514.10" },
      { label: "Gross notional", value: "$5,141.00" },
      { label: "Commission", value: "$1.25" },
      { label: "Net cash", value: "+$5,139.75" }
    ]);
  });

  it("ignores stale parameter extraction responses after the source changes", async () => {
    const firstRequest = createDeferred<QuantParametersResponse>();
    const secondRequest = createDeferred<QuantParametersResponse>();
    const services: QuantLabServices = {
      getTemplates: vi.fn().mockResolvedValue({ templates: [] }),
      extractParameters: vi.fn()
        .mockReturnValueOnce(firstRequest.promise)
        .mockReturnValueOnce(secondRequest.promise),
      runScript: vi.fn<QuantLabServices["runScript"]>().mockResolvedValue({} as QuantRunResponse)
    };

    const { result } = renderHook(() => useQuantLabScreenViewModel(services));

    await act(async () => {
      result.current.setSource("PrintMetric(\"first\", firstLookback);");
    });
    await waitFor(() => expect(services.extractParameters).toHaveBeenCalledTimes(1));

    await act(async () => {
      result.current.setSource("PrintMetric(\"second\", includeFees ? 1 : 0);");
    });
    await waitFor(() => expect(services.extractParameters).toHaveBeenCalledTimes(2));

    await act(async () => {
      firstRequest.resolve({ parameters: [{ ...numberParameter, name: "firstLookback", label: "First lookback" }] });
      await Promise.resolve();
    });

    expect(result.current.parameterRows).toEqual([]);
    expect(result.current.parameterPhase).toBe("extracting");

    await act(async () => {
      secondRequest.resolve({ parameters: [boolParameter] });
      await Promise.resolve();
    });

    expect(result.current.parameterRows).toHaveLength(1);
    expect(result.current.parameterRows[0]).toMatchObject({
      name: "includeFees",
      label: "Include fees",
      inputType: "checkbox"
    });
    expect(result.current.parameterPhase).toBe("ready");
  });

  it("removes previously detected parameters as soon as source scanning restarts", async () => {
    const nextRequest = createDeferred<QuantParametersResponse>();
    const services: QuantLabServices = {
      getTemplates: vi.fn().mockResolvedValue({ templates: [] }),
      extractParameters: vi.fn()
        .mockResolvedValueOnce({ parameters: [numberParameter] })
        .mockReturnValueOnce(nextRequest.promise),
      runScript: vi.fn<QuantLabServices["runScript"]>().mockResolvedValue({} as QuantRunResponse)
    };

    const { result } = renderHook(() => useQuantLabScreenViewModel(services));
    await waitFor(() => expect(result.current.parameterRows).toHaveLength(1));

    await act(async () => {
      result.current.setSource("Print(\"new source\");");
    });

    expect(result.current.parameterRows).toEqual([]);
    expect(result.current.parameterPhase).toBe("extracting");
  });

  it("finishes the initial parameter scan under React StrictMode", async () => {
    const services: QuantLabServices = {
      getTemplates: vi.fn().mockResolvedValue({ templates: [] }),
      extractParameters: vi.fn().mockResolvedValue({ parameters: [] }),
      runScript: vi.fn<QuantLabServices["runScript"]>().mockResolvedValue({} as QuantRunResponse)
    };
    const wrapper = ({ children }: PropsWithChildren) => createElement(StrictMode, null, children);

    const { result } = renderHook(() => useQuantLabScreenViewModel(services), { wrapper });

    await waitFor(() => expect(result.current.parameterPhase).toBe("idle"));
    expect(services.extractParameters).toHaveBeenCalled();
    expect(result.current.parameterPanel.statusMessage).toBe("No runtime parameters detected in the current script.");
  });

  it("keeps completed run evidence labeled when source changes before completion", async () => {
    const deferredRun = createDeferred<QuantRunResponse>();
    const services: QuantLabServices = {
      getTemplates: vi.fn().mockResolvedValue({ templates: [] }),
      extractParameters: vi.fn().mockResolvedValue({ parameters: [] }),
      runScript: vi.fn<QuantLabServices["runScript"]>().mockReturnValue(deferredRun.promise)
    };

    const { result } = renderHook(() => useQuantLabScreenViewModel(services));
    let runPromise: Promise<void> | undefined;

    await act(async () => {
      result.current.setSource("Print(\"submitted\");");
    });

    await act(async () => {
      runPromise = result.current.runScript();
      await Promise.resolve();
    });

    expect(result.current.resultPanel).toMatchObject({
      phase: "running",
      sourceDrifted: false
    });

    await act(async () => {
      result.current.setSource("Print(\"edited\");");
    });

    expect(result.current.resultPanel).toMatchObject({
      phase: "running",
      sourceDrifted: true,
      sourceDriftTitle: "Editor changed during this run"
    });

    await act(async () => {
      deferredRun.resolve(successfulRunState.result!);
      await runPromise;
    });

    expect(services.runScript).toHaveBeenCalledWith({
      source: "Print(\"submitted\");",
      parameters: {}
    });
    expect(result.current.resultPanel).toMatchObject({
      phase: "ready",
      title: "Run succeeded for previous source",
      sourceDrifted: true
    });
    await waitFor(() => expect(result.current.parameterPhase).toBe("idle"));
    expect(result.current.runCommand).toMatchObject({
      label: "Run current source",
      disabled: false
    });
  });
});
