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
  });
});
