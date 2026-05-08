import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { QuantLabScreen } from "@/screens/quant-lab-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import * as api from "@/lib/api";
import type { QuantRunResponse, QuantTemplatesResponse } from "@/types";

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

describe("QuantLabScreen", () => {
  beforeEach(() => {
    vi.spyOn(api, "getQuantTemplates").mockResolvedValue(templates);
  });

  afterEach(() => {
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
    expect(await screen.findByText(/Run succeeded/i)).toBeInTheDocument();
    expect(screen.getByText("answer")).toBeInTheDocument();
    expect(screen.getByText("42")).toBeInTheDocument();
    const consoleBlock = screen.getByText(/Hello from the Quant Lab\./, { selector: "pre" });
    expect(consoleBlock).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Sine" })).toBeInTheDocument();
  });

  it("surfaces compilation errors when the script fails", async () => {
    vi.spyOn(api, "runQuantScript").mockResolvedValue(failedRun);

    const user = userEvent.setup();
    renderWithRouter(<QuantLabScreen />);
    await waitForAsyncEffects();

    await user.click(screen.getByRole("button", { name: /Run script/i }));

    expect(await screen.findByText(/Run finished with errors/i)).toBeInTheDocument();
    expect(screen.getByText(/missing semicolon/i)).toBeInTheDocument();
  });

  it("shows a friendly error when the request fails", async () => {
    vi.spyOn(api, "runQuantScript").mockRejectedValue(new Error("503 — Quant Lab is not enabled"));

    const user = userEvent.setup();
    renderWithRouter(<QuantLabScreen />);
    await waitForAsyncEffects();

    await user.click(screen.getByRole("button", { name: /Run script/i }));

    expect(await screen.findByText(/Run failed/i)).toBeInTheDocument();
    expect(screen.getByText(/Quant Lab is not enabled/i)).toBeInTheDocument();
  });

  it("blocks running an empty script", async () => {
    const runSpy = vi.spyOn(api, "runQuantScript");

    const user = userEvent.setup();
    renderWithRouter(<QuantLabScreen />);
    await waitForAsyncEffects();

    const editor = screen.getByLabelText("Script source") as HTMLTextAreaElement;
    await user.clear(editor);
    await user.click(screen.getByRole("button", { name: /Run script/i }));

    expect(runSpy).not.toHaveBeenCalled();
    expect(await screen.findByText(/Enter some script source first/i)).toBeInTheDocument();
  });
});
