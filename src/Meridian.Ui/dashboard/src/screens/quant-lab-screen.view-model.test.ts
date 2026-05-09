import { describe, expect, it } from "vitest";
import {
  buildParameterRow,
  buildQuantParameters,
  buildRunCommandState,
  buildRunResultPanelState,
  buildTemplatePanelState,
  buildToolbarItems,
  initializeNewParameterValues,
  mergeQuantParameters,
  validateQuantSource,
  type QuantRunState
} from "@/screens/quant-lab-screen.view-model";
import type { QuantParameter } from "@/types";

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

describe("Quant Lab view model helpers", () => {
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
      inputType: "number",
      value: "63",
      min: 1,
      max: 252,
      step: "1",
      isDefault: false,
      resetLabel: "Reset Lookback to default"
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
});
