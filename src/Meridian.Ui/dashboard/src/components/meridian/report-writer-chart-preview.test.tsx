import userEvent from "@testing-library/user-event";
import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ReportWriterChartPreview } from "./report-writer-chart-preview";
import type { ReportWriterChartRender, ReportWriterGridRender } from "@/types";

const chart: ReportWriterChartRender = {
  type: "Line", title: "Income", categoryField: "month", categories: ["Jan", "Feb", "Mar"],
  series: [{ key: "net", label: "Net income", values: [100, -50] }], warnings: ["Partial period"]
};

const placeholderGrid = { gridId: "income", title: "Income", kind: "Detail", columns: [], rows: [], warnings: [] } satisfies ReportWriterGridRender;
const props = { grid: placeholderGrid, proofId: "income-proof", sourceDetail: "Test grid." };

describe("ReportWriterChartPreview", () => {
  it("identifies the summary and configured chart type without claiming to render a line chart", () => {
    render(<ReportWriterChartPreview {...props} chart={chart} />);
    expect(screen.getByRole("region", { name: "Income signed value summary" })).toBeInTheDocument();
    expect(screen.getByText("Configured chart: Line")).toBeInTheDocument();
    expect(screen.getByText(/Summary only/)).toBeInTheDocument();
    expect(screen.getByText("Partial period")).toBeInTheDocument();
  });

  it("renders opposite directions from a common visible zero baseline and keeps exact values", () => {
    const { container } = render(<ReportWriterChartPreview {...props} chart={chart} />);
    expect(screen.getByRole("button", { name: "Jan · grid row 1. Net income 100" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Feb · grid row 2. Net income -50" })).toBeInTheDocument();
    expect(container.querySelector('[data-summary-value="100"]')).toHaveStyle({ left: "50%", width: "50%" });
    expect(container.querySelector('[data-summary-value="-50"]')).toHaveStyle({ left: "25%", width: "25%" });
    expect(screen.getByText("0")).toBeInTheDocument();
  });

  it("shows an absent observation as unavailable while preserving an actual zero", () => {
    render(<ReportWriterChartPreview {...props} chart={{ ...chart, series: [{ key: "net", label: "Net income", values: [0, Number.NaN] }] }} />);
    expect(screen.getByRole("button", { name: "Jan · grid row 1. Net income 0" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Feb · grid row 2. Net income Unavailable" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Mar · grid row 3. Net income Unavailable" })).toBeInTheDocument();
  });
});

describe("report chart evidence", () => {
  it("matches keyboard and pointer evidence for repeated categories and links the exact grid row", async () => {
    const user = userEvent.setup();
    const chart: ReportWriterChartRender = {
      type: "Bar", title: "Net exposure", categoryField: "sector", categories: ["Rates", "Rates"],
      series: [{ key: "net", label: "Net", values: [15.25, -20.5] }], warnings: []
    };
    const grid = {
      gridId: "rates", title: chart.title, kind: "TopN", columns: [], warnings: [],
      rows: [
        { rowKey: "earlier", values: { net: "15.25", asOfDate: "2026-10-30", netUnit: "USD" } },
        { rowKey: "later", values: { net: "-20.5", asOfDate: "2026-10-31", netUnit: "USD" } }
      ],
      lineage: { inputRowCount: 2, outputRowCount: 2, sourceFields: ["exposure"], metrics: [{ name: "net", function: "Sum", sourceField: "exposure" }], formulas: [] }
    } satisfies ReportWriterGridRender;

    render(<ReportWriterChartPreview chart={chart} grid={grid} proofId="rates-proof" sourceDetail="Dataset Closed Exposure." />);
    const table = screen.getByRole("table", { name: "Net exposure chart data" });
    const second = screen.getByRole("button", { name: /Rates · grid row 2/ });
    await user.hover(second);
    const pointerEvidence = table.textContent;
    expect(within(table).getByRole("row", { name: "Net -20.5 USD Sum(exposure) for the rendered group." })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open source grid row 2" })).toHaveAttribute("href", "#rates-proof-row-1");
    expect(screen.getByText("2026-10-31")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Rates · grid row 1/ }));
    await user.keyboard("{ArrowRight}");
    expect(second).toHaveFocus();
    expect(table.textContent).toBe(pointerEvidence);
  });
});
