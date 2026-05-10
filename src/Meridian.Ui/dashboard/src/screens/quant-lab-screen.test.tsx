import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { QuantLabScreen } from "@/screens/quant-lab-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import * as api from "@/lib/api";
import type { QuantParametersResponse, QuantRunResponse, QuantTemplatesResponse } from "@/types";

const templates: QuantTemplatesResponse = {
  templates: [
    {
      id: "hello",
      title: "Hello, Quant Lab",
      description: "Print a metric",
      source: "Print(\"hi\");"
    }
  ]
};

const successfulRun: QuantRunResponse = {
  success: true,
  elapsedMs: 12,
  compileTimeMs: 30,
  peakMemoryBytes: 1024 * 16,
  runtimeError: null,
  consoleOutput: "Hello from the Quant Lab.\n",
  compilationErrors: [],
  runtimeDiagnostics: [],
  metrics: [{ label: "answer", value: "42" }],
  plots: [
    {
      title: "Sine",
      type: "Line",
      series: [
        { date: "2026-01-01", value: 0 },
        { date: "2026-01-02", value: 1 }
      ],
      multiSeries: null,
      candlestick: null,
      heatmapData: null,
      heatmapLabels: null
    }
  ],
  trades: [],
  runtimeParameters: []
};

const noEvidenceSuccessfulRun: QuantRunResponse = {
  success: true,
  elapsedMs: 7,
  compileTimeMs: 18,
  peakMemoryBytes: 1024 * 8,
  runtimeError: null,
  consoleOutput: "",
  compilationErrors: [],
  runtimeDiagnostics: [],
  metrics: [],
  plots: [],
  trades: [],
  runtimeParameters: []
};

const failedRun: QuantRunResponse = {
  success: false,
  elapsedMs: 0,
  compileTimeMs: 5,
  peakMemoryBytes: 0,
  runtimeError: null,
  consoleOutput: "",
  compilationErrors: [
    { severity: "Error", message: "missing semicolon", line: 1, column: 1 }
  ],
  runtimeDiagnostics: [],
  metrics: [],
  plots: [],
  trades: [],
  runtimeParameters: []
};

const emptyParameters: QuantParametersResponse = { parameters: [] };

describe("QuantLabScreen", () => {
  beforeEach(() => {
    vi.spyOn(api, "getQuantTemplates").mockResolvedValue(templates);
    vi.spyOn(api, "extractQuantParameters").mockResolvedValue(emptyParameters);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it("loads templates and lets the user pick one", async () => {
    const user = userEvent.setup();
    renderWithRouter(<QuantLabScreen />);

    await waitForAsyncEffects();

    const templateButton = await screen.findByRole("button", { name: /Load Hello, Quant Lab template/i });
    await user.click(templateButton);

    const editor = screen.getByLabelText("Script source") as HTMLTextAreaElement;
    expect(editor.value).toContain("Print(\"hi\");");
  });

  it("runs a script and renders metrics, console output, and plot", async () => {
    const runSpy = vi.spyOn(api, "runQuantScript").mockResolvedValue(successfulRun);

    const user = userEvent.setup();
    renderWithRouter(<QuantLabScreen />);
    await waitForAsyncEffects();

    await user.click(screen.getByRole("button", { name: /Run script/i }));

    await waitFor(() => expect(runSpy).toHaveBeenCalledTimes(1));
    expect(await screen.findByRole("heading", { name: /Run succeeded/i })).toBeInTheDocument();
    expect(screen.getByText("answer")).toBeInTheDocument();
    expect(screen.getByText("42")).toBeInTheDocument();
    const consoleBlock = screen.getByText(/Hello from the Quant Lab\./, { selector: "pre" });
    expect(consoleBlock).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Sine" })).toBeInTheDocument();
  });

  it("renders an actionable empty-evidence state for successful runs with no output", async () => {
    vi.spyOn(api, "runQuantScript").mockResolvedValue(noEvidenceSuccessfulRun);

    const user = userEvent.setup();
    renderWithRouter(<QuantLabScreen />);
    await waitForAsyncEffects();

    await user.click(screen.getByRole("button", { name: /Run script/i }));

    expect(await screen.findByRole("heading", { name: /Run succeeded/i })).toBeInTheDocument();
    const emptyEvidenceTitle = screen.getByText("Run completed without runtime evidence");
    expect(emptyEvidenceTitle.closest('[role="status"]')).toHaveTextContent(/Add Print, PrintMetric, or plot calls/i);
  });

  it("surfaces compilation errors when the script fails", async () => {
    vi.spyOn(api, "runQuantScript").mockResolvedValue(failedRun);

    const user = userEvent.setup();
    renderWithRouter(<QuantLabScreen />);
    await waitForAsyncEffects();

    await user.click(screen.getByRole("button", { name: /Run script/i }));

    expect(await screen.findByRole("heading", { name: /Run finished with errors/i })).toBeInTheDocument();
    expect(screen.getByText(/missing semicolon/i)).toBeInTheDocument();
  });

  it("shows a friendly error when the request fails", async () => {
    vi.spyOn(api, "runQuantScript").mockRejectedValue(new Error("503 — Quant Lab is not enabled"));

    const user = userEvent.setup();
    renderWithRouter(<QuantLabScreen />);
    await waitForAsyncEffects();

    await user.click(screen.getByRole("button", { name: /Run script/i }));

    expect(await screen.findByRole("heading", { name: /Run failed/i })).toBeInTheDocument();
    expect(screen.getAllByText(/Quant Lab is not enabled/i).length).toBeGreaterThan(0);
  });

  it("disables running an empty script with a disabled reason", async () => {
    const runSpy = vi.spyOn(api, "runQuantScript");

    const user = userEvent.setup();
    renderWithRouter(<QuantLabScreen />);
    await waitForAsyncEffects();

    const editor = screen.getByLabelText("Script source") as HTMLTextAreaElement;
    await user.clear(editor);
    const runButton = screen.getByRole("button", { name: /Run script/i });

    expect(runButton).toBeDisabled();
    expect(runButton).toHaveAttribute("title", "Enter some script source first.");
    expect(runSpy).not.toHaveBeenCalled();
  });

  it("keeps the parameter panel visible when no runtime parameters are detected", async () => {
    vi.useFakeTimers();
    renderWithRouter(<QuantLabScreen />);
    await waitForAsyncEffects();

    expect(screen.getByRole("heading", { name: "Parameters" })).toBeInTheDocument();
    expect(screen.getByText("Scanning source for runtime parameters.")).toHaveAttribute("role", "status");

    await act(async () => {
      await vi.advanceTimersByTimeAsync(650);
    });

    expect(screen.getByText("No runtime parameters detected in the current script.")).toBeInTheDocument();
  });

  it("links runtime parameter descriptions to editable controls", async () => {
    vi.useFakeTimers();
    vi.mocked(api.extractQuantParameters).mockResolvedValue({
      parameters: [
        {
          name: "lookback",
          label: "Lookback",
          typeName: "int",
          defaultValue: "20",
          description: "Rolling window length",
          min: 1,
          max: 252
        }
      ]
    });

    renderWithRouter(<QuantLabScreen />);
    await waitForAsyncEffects();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(650);
    });

    const lookback = screen.getByLabelText("Lookback parameter");
    expect(lookback).toHaveAttribute("id", "quant-param-lookback");
    expect(lookback).toHaveAttribute("aria-describedby", "quant-param-lookback-description");
    expect(screen.getByText("Rolling window length")).toHaveAttribute("id", "quant-param-lookback-description");
  });
});
