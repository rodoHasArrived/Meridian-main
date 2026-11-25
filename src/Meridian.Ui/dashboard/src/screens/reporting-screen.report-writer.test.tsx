import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderReportTemplate } from "@/lib/api";
import { buildNewCustomReportWriterGrid } from "@/screens/reporting-screen.new-custom-report";
import { ReportWriterDesignerGrid, useReportingReportWriter } from "@/screens/reporting-screen.report-writer";
import type { ReportingWriterGridRow } from "@/screens/reporting-screen.view-model";
import type { RenderReportTemplateResponse, ReportWriterDatasetSource } from "@/types";

vi.mock("@/lib/api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api")>()),
  renderReportTemplate: vi.fn()
}));

const renderMock = vi.mocked(renderReportTemplate);
const DEBOUNCE_MS = 20;

function buildResponse(rowCount: number): RenderReportTemplateResponse {
  return {
    templateId: { name: "custom-report-1", version: 1 },
    renderedContent: "",
    missingRequiredParameters: [],
    grids: [
      {
        gridId: "grid-1",
        title: "Custom report 1",
        kind: "Pivot",
        columns: [{ key: "fund", label: "Fund", role: "dimension" }],
        rows: Array.from({ length: rowCount }, (_, index) => ({ rowKey: `row-${index}`, values: { fund: `Fund${index}` } })),
        warnings: []
      }
    ],
    warnings: []
  } as RenderReportTemplateResponse;
}

function Harness({
  grid,
  datasetSources = [],
  debounceMs = DEBOUNCE_MS
}: {
  grid: ReportingWriterGridRow;
  datasetSources?: ReportWriterDatasetSource[];
  debounceMs?: number;
}) {
  const writer = useReportingReportWriter({
    datasetSources,
    buildDraftRequest: () => ({}) as never,
    buildRenderRequest: () => ({ templateId: { name: "custom-report-1", version: 1 }, parameters: {} }) as never,
    autoPreviewDebounceMs: debounceMs
  });

  return (
    <>
      <ReportWriterDesignerGrid
        grid={grid}
        settings={writer.getWriterDraftSettings(grid)}
        customFormula={writer.getWriterCustomFormula(grid)}
        chartDraft={writer.getWriterChartDraft(grid)}
        formatRules={writer.getWriterFormatRules(grid)}
        datasetSources={datasetSources}
        customDatasetText={writer.writerCustomDatasetText[grid.id] ?? ""}
        isSaving={false}
        isPreviewing={writer.previewingWriterDraftId === grid.id}
        preview={writer.getWriterPreview(grid)}
        previousPreview={writer.writerPreviousPreviewByGridId[grid.id] ?? null}
        getZoneTokens={writer.getWriterZoneTokens}
        onTokenDragStart={writer.handleWriterTokenDragStart}
        onZoneDrop={writer.handleWriterZoneDrop}
        onFieldAdd={writer.placeWriterToken}
        onTokenRemove={writer.removeWriterZoneToken}
        onTokenMove={writer.moveWriterZoneToken}
        onReset={writer.resetWriterGrid}
        onSettingsChange={writer.updateWriterDraftSetting}
        onCustomFormulaChange={writer.updateWriterCustomFormula}
        onCustomDatasetChange={writer.updateWriterCustomDataset}
        onChartDraftChange={writer.updateWriterChartDraft}
        onFilterAdd={writer.addWriterFilter}
        onFilterRemove={writer.removeWriterFilter}
        onFilterChange={writer.updateWriterFilter}
        onFormatRuleAdd={writer.addWriterFormatRule}
        onFormatRuleRemove={writer.removeWriterFormatRule}
        onFormatRuleChange={writer.updateWriterFormatRule}
        onPreview={writer.previewWriterGrid}
        onSave={writer.saveWriterGridDraft}
        autoPreview={{
          phase: writer.getWriterPreviewPhase(grid),
          enabled: writer.isWriterAutoPreviewEnabled(grid),
          pausedForSize: writer.isWriterAutoPreviewPausedForSize(grid),
          quietError: writer.getWriterQuietPreviewError(grid),
          onToggle: (enabled) => writer.setWriterAutoPreviewEnabled(grid, enabled),
          onRetry: () => writer.retryWriterAutoPreview(grid),
          onEditSessionChange: (editing) => writer.setWriterEditSession(editing ? grid.id : null)
        }}
      />
      {writer.writerPreviewStatus ? <p data-testid="manual-preview-status">{writer.writerPreviewStatus.message}</p> : null}
    </>
  );
}

function buildGrid(): ReportingWriterGridRow {
  return buildNewCustomReportWriterGrid(1, { templateId: "investor-monthly-statement", templateVersion: "1" });
}

function choosePortfolioSample() {
  fireEvent.change(screen.getByLabelText("Custom report 1 preview dataset"), { target: { value: "portfolioPositions" } });
}

function editFormula(value: string) {
  fireEvent.change(screen.getByLabelText("Custom report 1 custom formula expression"), { target: { value } });
}

async function waitPastDebounce(debounceMs = DEBOUNCE_MS) {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, debounceMs * 4));
  });
}

describe("report-writer auto preview", () => {
  beforeEach(() => {
    renderMock.mockReset();
  });

  it("sends no render requests until the grid is edited, nor while no dataset is chosen", async () => {
    render(<Harness grid={buildGrid()} />);
    await waitPastDebounce();
    expect(renderMock).not.toHaveBeenCalled();

    editFormula("{pnl}");
    await waitPastDebounce();
    expect(renderMock).not.toHaveBeenCalled();
    expect(screen.queryByLabelText("Custom report 1 auto preview error")).not.toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "Auto preview Custom report 1 report-writer grid" })).toBeChecked();
  });

  it("re-renders once after a burst of edits, quietly, with an abort signal", async () => {
    renderMock.mockResolvedValue(buildResponse(2));
    render(<Harness grid={buildGrid()} />);

    choosePortfolioSample();
    editFormula("{pnl}");
    editFormula("{pnl} /");
    editFormula("{pnl} / {marketValue}");
    await waitFor(() => expect(screen.getByLabelText("Custom report 1 live preview")).toBeInTheDocument());

    expect(renderMock).toHaveBeenCalledTimes(1);
    expect(renderMock.mock.calls[0][1]).toEqual({ signal: expect.any(AbortSignal) });
    expect(within(screen.getByRole("status", { name: "Custom report 1 preview status" })).getByText("Preview up to date")).toBeInTheDocument();
    expect(screen.queryByTestId("manual-preview-status")).not.toBeInTheDocument();
  });

  it("keeps the last preview visible and dimmed while it is out of date", async () => {
    renderMock.mockResolvedValue(buildResponse(1));
    render(<Harness grid={buildGrid()} />);
    choosePortfolioSample();
    editFormula("{pnl}");
    await waitFor(() => expect(screen.getByLabelText("Custom report 1 live preview")).toBeInTheDocument());

    fireEvent.click(screen.getByRole("checkbox", { name: "Auto preview Custom report 1 report-writer grid" }));
    editFormula("{pnl} * 2");
    await waitPastDebounce();

    const preview = screen.getByLabelText("Custom report 1 live preview");
    expect(preview).toHaveClass("opacity-60");
    expect(preview).toHaveAttribute("aria-busy", "true");
    expect(screen.getByText("Preview out of date")).toBeInTheDocument();
    expect(renderMock).toHaveBeenCalledTimes(1);
  });

  it("shows a quiet error with Retry, held back while the operator is typing", async () => {
    const user = userEvent.setup();
    renderMock.mockResolvedValue(buildResponse(1));
    render(<Harness grid={buildGrid()} />);

    choosePortfolioSample();
    await screen.findByText("Preview up to date");
    renderMock.mockClear();
    renderMock.mockRejectedValueOnce(new Error("Render service unavailable"));
    const expression = screen.getByLabelText("Custom report 1 custom formula expression");
    await user.click(expression);
    fireEvent.change(expression, { target: { value: "{pnl}" } });
    await waitFor(() => expect(renderMock).toHaveBeenCalledTimes(1));
    await waitPastDebounce();
    expect(screen.queryByLabelText("Custom report 1 auto preview error")).not.toBeInTheDocument();

    await user.tab();
    const errorRow = await screen.findByLabelText("Custom report 1 auto preview error");
    expect(screen.queryByTestId("manual-preview-status")).not.toBeInTheDocument();

    renderMock.mockResolvedValueOnce(buildResponse(1));
    await user.click(within(errorRow).getByRole("button", { name: "Retry Custom report 1 auto preview" }));
    await waitFor(() => expect(screen.queryByLabelText("Custom report 1 auto preview error")).not.toBeInTheDocument());
    expect(renderMock).toHaveBeenCalledTimes(2);
  });

  it("pauses auto preview for datasets above 200 rows", async () => {
    const bigSource: ReportWriterDatasetSource = {
      sourceId: "big",
      label: "Big dataset",
      description: "More rows than auto preview allows",
      rowCount: 201,
      fields: [],
      rows: Array.from({ length: 201 }, (_, index) => ({ fund: `Fund${index}`, marketValue: "1" }))
    };
    render(<Harness grid={buildGrid()} datasetSources={[bigSource]} />);

    fireEvent.change(screen.getByLabelText("Custom report 1 preview dataset"), { target: { value: "source:big" } });
    await waitPastDebounce();

    expect(renderMock).not.toHaveBeenCalled();
    expect(screen.getByText("Auto preview paused — large dataset")).toBeInTheDocument();
  });

  it("keeps the manual Preview button on the global status path and cancels pending auto renders", async () => {
    const user = userEvent.setup();
    renderMock.mockResolvedValue(buildResponse(3));
    // A long debounce keeps the pending auto render from firing before the click lands.
    render(<Harness grid={buildGrid()} debounceMs={150} />);

    choosePortfolioSample();
    editFormula("{pnl}");
    await user.click(screen.getByRole("button", { name: "Preview Custom report 1 report-writer grid" }));

    expect(await screen.findByTestId("manual-preview-status")).toHaveTextContent("Custom report 1 preview rendered.");
    await waitPastDebounce(150);
    expect(renderMock).toHaveBeenCalledTimes(1);
  });

  it("announces preview status through an accessible live region", async () => {
    renderMock.mockResolvedValue(buildResponse(1));
    const { container } = render(<Harness grid={buildGrid()} />);
    choosePortfolioSample();
    editFormula("{pnl}");
    await screen.findByText("Preview up to date");

    const status = screen.getByRole("status", { name: "Custom report 1 preview status" });
    expect(status).toHaveAttribute("aria-live", "polite");
    expect((await axe(container)).violations).toEqual([]);
  });

  it("does not announce empty drop zones as lists", async () => {
    const { container } = render(<Harness grid={buildGrid()} />);

    const rows = screen.getByRole("group", { name: "Custom report 1 Rows drop zone" });
    expect(within(rows).queryByRole("list")).not.toBeInTheDocument();
    expect(within(rows).getByText("No fields")).toBeInTheDocument();
    expect((await axe(container)).violations.map((violation) => violation.id)).not.toContain("aria-required-children");
  });
});
