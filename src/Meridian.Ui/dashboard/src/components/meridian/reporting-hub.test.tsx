import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ReportingHub } from "@/components/meridian/reporting-hub";
import { buildReportingHubModel } from "@/lib/reporting-hub";

describe("ReportingHub", () => {
  it("renders daily reporting work as control-tower decision facts", () => {
    const model = buildReportingHubModel(
      [],
      [],
      [
        {
          workItemId: "delivery-failure:board",
          kind: "delivery-failure",
          title: "Board portal package failed",
          statusLabel: "Blocked",
          detail: "Secure portal rejected the latest board package.",
          tone: "danger",
          owner: "fund-controller",
          dueAtUtc: "2026-06-30T15:00:00Z",
          primaryActionLabel: "Review delivery",
          primaryActionHref: "/reporting/report-packs?recipient=board",
          secondaryActionLabel: "Evidence",
          secondaryActionHref: "/reporting/evidence?subjectKind=report-pack-delivery&subjectId=board",
          evidenceGaps: ["Delivery rejection lacks retained portal proof."],
          context: ["Board reporting committee", "SecurePortal"]
        }
      ]
    );

    render(<ReportingHub model={model} />);

    const cockpit = screen.getByRole("region", { name: "Daily reporting cockpit" });
    const facts = within(cockpit).getByLabelText("Board portal package failed decision facts");
    expect(within(facts).getAllByText("Blocked")).toHaveLength(2);
    expect(within(facts).getByText("fund-controller")).toBeInTheDocument();
    expect(within(facts).getByText("Board reporting committee")).toBeInTheDocument();
    expect(within(facts).getByText("Review delivery")).toBeInTheDocument();
    expect(within(facts).getByText("1 evidence gap")).toBeInTheDocument();
    expect(within(cockpit).getByLabelText("Selected reporting work detail")).toHaveTextContent("Secure portal rejected the latest board package.");
  });

  it("renders report families as a health table instead of launch cards", () => {
    const model = buildReportingHubModel(
      [
        {
          templateId: "monthly",
          family: "Investor statements",
          status: "Released",
          asOfDateLabel: "2026-06-30",
          runIdLabel: "run-1",
          drilldownLinks: [{ href: "/reporting/runs/run-1", label: "Run", isBrowserNavigable: true }]
        }
      ],
      [
        {
          templateName: "investor-monthly",
          name: "Investor Monthly Statement",
          family: "Investor statements"
        }
      ]
    );

    render(<ReportingHub model={model} />);

    const familyHealth = screen.getByRole("region", { name: "Report family organizer" });
    expect(within(familyHealth).getByRole("columnheader", { name: "Family" })).toBeInTheDocument();
    expect(within(familyHealth).getByText("Investor Statements")).toBeInTheDocument();
    expect(within(familyHealth).getByRole("link", { name: "Open latest output for Investor Statements" })).toHaveAttribute(
      "href",
      "/reporting/runs/run-1"
    );
  });

  it("does not present family setup work as a successful empty queue", () => {
    const model = buildReportingHubModel([], [{
      templateName: "draft-pack",
      name: "Draft Pack",
      family: "CustomReport",
      canRunOnDemand: false
    }]);

    render(<ReportingHub model={model} />);

    const summaryBadges = screen.getAllByText("No urgent work queued · 1 report family needs setup or review");
    expect(summaryBadges.some((badge) => badge.classList.contains("border-warning/35"))).toBe(true);
    expect(screen.getByText("No dedicated daily work items are loaded. 1 report family still needs review in the organizer below.")).toBeInTheDocument();
    expect(screen.queryByText(/No due packages, approvals/i)).not.toBeInTheDocument();
  });
});
