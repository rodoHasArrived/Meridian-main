import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ReportWriterChartPreview } from "./report-writer-chart-preview";
import type { ReportWriterChartRender } from "@/types";

const chart: ReportWriterChartRender = {
  type: "Line", title: "Income", categoryField: "month", categories: ["Jan", "Feb", "Mar"],
  series: [{ key: "net", label: "Net income", values: [100, -50] }], warnings: ["Partial period"]
};

describe("ReportWriterChartPreview", () => {
  it("identifies the summary and configured chart type without claiming to render a line chart", () => {
    render(<ReportWriterChartPreview chart={chart} />);
    expect(screen.getByRole("region", { name: "Income signed value summary" })).toBeInTheDocument();
    expect(screen.getByText("Configured chart: Line")).toBeInTheDocument();
    expect(screen.getByText(/Summary only/)).toBeInTheDocument();
    expect(screen.getByText("Partial period")).toBeInTheDocument();
  });

  it("renders opposite directions from a common visible zero baseline and keeps exact values", () => {
    const { container } = render(<ReportWriterChartPreview chart={chart} />);
    expect(screen.getByRole("img", { name: "Jan, Net income: 100" })).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Feb, Net income: -50" })).toBeInTheDocument();
    expect(container.querySelector('[data-summary-value="100"]')).toHaveStyle({ left: "50%", width: "50%" });
    expect(container.querySelector('[data-summary-value="-50"]')).toHaveStyle({ left: "25%", width: "25%" });
    expect(screen.getByText("0")).toBeInTheDocument();
  });

  it("shows an absent observation as unavailable while preserving an actual zero", () => {
    render(<ReportWriterChartPreview chart={{ ...chart, series: [{ key: "net", label: "Net income", values: [0, Number.NaN] }] }} />);
    expect(screen.getByRole("img", { name: "Jan, Net income: 0" })).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Feb, Net income: Unavailable" })).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Mar, Net income: Unavailable" })).toBeInTheDocument();
  });
});
