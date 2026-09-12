import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ReportingProductionSurface } from "@/components/meridian/reporting-production-surface";
import { buildReportingProductionModel } from "@/lib/reporting-production";
import { buildReportingPeriodModel } from "@/lib/reporting-period-object";

const NOW = "2026-10-04T12:00:00Z";

function model(runs: Parameters<typeof buildReportingProductionModel>[0]["runs"]) {
  return buildReportingProductionModel({
    runs,
    templates: [{ templateId: "tmpl-perf", name: "Portfolio Performance", family: "Performance" }],
    signals: { scheduleCount: 4 },
    asOfDate: "2026-09-30",
    evaluationAtUtc: NOW
  });
}

describe("ReportingProductionSurface", () => {
  it("renders the seven pipeline lanes as a numbered operating index", () => {
    render(<ReportingProductionSurface model={model([])} />);

    const lanes = screen.getByRole("navigation", { name: "Reporting pipeline lanes" });
    const links = within(lanes).getAllByRole("link");

    expect(links).toHaveLength(7);
    expect(links.map((link) => link.getAttribute("href"))).toEqual([
      "/reporting/library",
      "/reporting/run-status",
      "/reporting/report-builder",
      "/reporting/preview",
      "/reporting/report-packs",
      "/reporting/scheduled",
      "/reporting/governance"
    ]);
    expect(within(lanes).getByText("Find everything Meridian can produce.")).toBeInTheDocument();
  });

  it("puts the production register first, ranked with blocked reports at the top", () => {
    render(
      <ReportingProductionSurface
        model={model([
          { runId: "ok", templateId: "tmpl-perf", family: "Performance", status: "Approved", asOfDate: "2026-09-30" },
          {
            runId: "bad",
            templateId: "tmpl-ga",
            family: "Ledger",
            status: "Failed",
            asOfDate: "2026-09-30",
            reportName: "GA Investment Report",
            blockingReasons: ["Private placement price is missing."]
          }
        ])}
      />
    );

    const register = screen.getByRole("region", { name: "Production register" });
    const rows = within(register).getAllByRole("row").slice(1);

    expect(within(rows[0]).getByText("GA Investment Report")).toBeInTheDocument();
    expect(within(rows[0]).getByText("Private placement price is missing.")).toBeInTheDocument();
    expect(within(rows[1]).getByText("Portfolio Performance")).toBeInTheDocument();
  });

  it("omits the owner column while no report has a known owner", () => {
    const { rerender } = render(
      <ReportingProductionSurface
        model={model([{ runId: "a", templateId: "tmpl-perf", family: "Performance", status: "Draft" }])}
      />
    );

    const register = screen.getByRole("region", { name: "Production register" });
    expect(within(register).queryByRole("columnheader", { name: "Owner" })).not.toBeInTheDocument();

    rerender(
      <ReportingProductionSurface
        model={model([
          { runId: "a", templateId: "tmpl-perf", family: "Performance", status: "Draft", owner: "Investment Reporting" }
        ])}
      />
    );

    expect(within(screen.getByRole("region", { name: "Production register" }))
      .getByRole("columnheader", { name: "Owner" })).toBeInTheDocument();
  });

  it("lists what needs attention and links each item to the lane that owns it", () => {
    render(
      <ReportingProductionSurface
        model={model([
          { runId: "a", templateId: "t1", family: "Performance", status: "Failed" },
          { runId: "b", templateId: "t2", family: "Performance", status: "AwaitingApproval" }
        ])}
      />
    );

    const attention = screen.getByRole("region", { name: "Reporting attention" });
    expect(within(attention).getByText("1 blocked")).toBeInTheDocument();
    expect(within(attention).getByText("1 approval due")).toBeInTheDocument();
    expect(within(attention).getByRole("link", { name: "Open 1 approval due" }))
      .toHaveAttribute("href", "/reporting/governance");
  });

  it("says nothing needs attention rather than rendering an empty rail", () => {
    render(
      <ReportingProductionSurface
        model={model([{ runId: "a", templateId: "tmpl-perf", family: "Performance", status: "Approved" }])}
      />
    );

    const attention = screen.getByRole("region", { name: "Reporting attention" });
    expect(within(attention).getByText("Nothing needs attention in this period.")).toBeInTheDocument();
  });

  it("states plainly when the period has no reports in production", () => {
    render(<ReportingProductionSurface model={model([])} />);

    expect(screen.getByText("No reports are in production for this period.")).toBeInTheDocument();
    expect(screen.getByText("No reports in production")).toBeInTheDocument();
  });

  it("renders the period milestone strip and names unset milestones", () => {
    const period = buildReportingPeriodModel(
      { periodId: "2026-09", periodEnd: "2026-09-30", accountingClose: "2026-10-02" },
      "2026-10-04"
    );

    render(<ReportingProductionSurface model={model([])} period={period} />);

    const strip = screen.getByRole("region", { name: "Reporting period" });
    const milestones = within(strip).getByLabelText("Reporting period milestones");

    expect(within(milestones).getByText("Reporting cutoff")).toBeInTheDocument();
    expect(within(milestones).getAllByText("Not set")).toHaveLength(2);
    expect(within(strip).getByText("Accounting close: Not linked")).toBeInTheDocument();
  });

  it("raises a re-freeze warning when the accounting period moved under the report", () => {
    const period = buildReportingPeriodModel(
      {
        periodId: "2026-09",
        periodEnd: "2026-09-30",
        snapshots: { ledgerVersion: "CLOSE-SEP26-v3" },
        close: { periodLabel: "September 2026", status: "Closed", ledgerVersion: "CLOSE-SEP26-v4" }
      },
      "2026-10-04"
    );

    render(<ReportingProductionSurface model={model([])} period={period} />);

    const alert = screen.getByRole("status");
    expect(alert).toHaveTextContent("Ledger moved from CLOSE-SEP26-v3 to CLOSE-SEP26-v4 after report freeze.");
    expect(alert).toHaveTextContent("must be re-frozen before publication");
  });

  it("does not render the period strip when no run carries a confirmed period", () => {
    render(<ReportingProductionSurface model={model([])} period={null} />);
    expect(screen.queryByRole("region", { name: "Reporting period" })).not.toBeInTheDocument();
  });
});
