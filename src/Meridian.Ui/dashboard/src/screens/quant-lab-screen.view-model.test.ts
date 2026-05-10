import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  buildParameterPanelState,
  buildParameterRow,
  buildQuantParameters,
  buildRunCommandState,
  buildRunResultPanelState,
  buildTemplatePanelState,
  buildToolbarItems,
  initializeNewParameterValues,
  mergeQuantParameters,
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

  it("builds disabled run command state for empty source", () => {
    expect(validateQuantSource("   ")).toBe("Enter some script source first.");
    expect(buildRunCommandState("   ", "idle")).toMatchObject({
      label: "Run",
      disabled: true,
      disabledReason: "Enter some script source first.",
      busy: false
    });
  });

  it("projects loading, empty, and error template states into accessible panel copy", () => {
    expect(buildTemplatePanelState("loading", null)).toMatchObject({
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
      resetLabel: "Reset Lookback to default"
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
      metricsLabel: "Metrics · 1",
      consoleLabel: "Console output",
      plotsDescription: "1 chart returned by this run.",
      hasResult: true,
      hasMetrics: true,
      hasConsoleOutput: true,
      hasPlots: true
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

  it("ignores stale parameter extraction responses after the source changes", async () => {
    vi.useFakeTimers();
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
    await act(async () => {
      vi.advanceTimersByTime(600);
    });
    expect(services.extractParameters).toHaveBeenCalledTimes(1);

    await act(async () => {
      result.current.setSource("PrintMetric(\"second\", includeFees ? 1 : 0);");
    });
    await act(async () => {
      vi.advanceTimersByTime(600);
    });
    expect(services.extractParameters).toHaveBeenCalledTimes(2);

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
});
