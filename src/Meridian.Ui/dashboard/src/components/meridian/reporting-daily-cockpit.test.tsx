import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ReportingDailyCockpit } from "@/components/meridian/reporting-daily-cockpit";
import type { ReportingHubModel } from "@/lib/reporting-hub";

describe("ReportingDailyCockpit", () => {
  it("answers blocked, owner, affected output, next action, and proof for decision queues", () => {
    render(<ReportingDailyCockpit model={model} />);

    const queues = screen.getByRole("list", { name: "Daily reporting decision queues" });
    const queue = within(queues).getByLabelText("Blocked package queue: Shadow NAV pack blocked for Controller until Fix delivery evidence. Proof: Manifest gap, Delivery failure");
    const summary = within(queue).getByLabelText("Shadow NAV pack control summary");

    expect(within(summary).getAllByText("Blocked").length).toBeGreaterThanOrEqual(2);
    expect(within(summary).getByText("Controller")).toBeInTheDocument();
    expect(within(summary).getAllByText("Shadow NAV pack").length).toBeGreaterThan(0);
    expect(within(summary).getByText("Fix delivery evidence")).toBeInTheDocument();
    expect(within(summary).getAllByText("Manifest gap").length).toBeGreaterThan(0);
    const passport = within(queue).getByLabelText("Shadow NAV pack Number Passport");
    expect(within(passport).getByRole("heading", { name: "Number Passport" })).toBeInTheDocument();
    for (const label of ["Source", "Freshness", "Reconciliation", "Approvals", "Report Usage", "Blockers", "Evidence Packet", "Audit Trail"]) {
      expect(within(passport).getByText(label)).toBeInTheDocument();
    }
    expect(within(passport).getByRole("link", { name: "Open evidence packet for Shadow NAV pack" })).toHaveAttribute("href", "/reporting/evidence");
    expect(within(queue).getByRole("link", { name: "Open evidence: Shadow NAV pack" })).toHaveAttribute("href", "/reporting/evidence");
  });

  it("keeps the empty daily queue state explicit", () => {
    render(<ReportingDailyCockpit model={{ ...model, decisionQueues: [], dailyWork: [], dailyWorkSummaryLabel: "No daily reporting work is queued." }} />);

    expect(screen.getByText("No due packages, approvals, delivery failures, restatements, or evidence gaps are queued.")).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Daily reporting decision queues" })).not.toBeInTheDocument();
  });
});

const model: ReportingHubModel = {
  dailyWork: [
    {
      workItemId: "delivery-gap",
      kind: "delivery-failure",
      kindLabel: "Delivery failure",
      title: "Delivery evidence missing",
      statusLabel: "Review",
      detail: "Delivery evidence has a retained failure.",
      tone: "warning",
      badgeVariant: "warning",
      owner: "Reporting Ops",
      blockedLabel: "Needs review",
      affectedOutputLabel: "Investor delivery",
      dueLabel: "Due 2026-06-26",
      dueAtUtc: "2026-06-26T17:00:00Z",
      nextActionLabel: "Attach proof",
      primaryActionLabel: "Open delivery",
      primaryActionHref: "/reporting/evidence",
      proofLabel: "1 proof item",
      proofItems: ["Delivery receipt"],
      proofPassportStatusLabel: "Needs review",
      proofPassportSummary: "Number Passport for Investor delivery: Source Delivery failure; Freshness Due 2026-06-26; Reconciliation Route-owned; Approvals Route-owned; Report Usage Investor delivery; Blockers Needs review; Evidence Packet 1 proof item; Audit Trail Open delivery.",
      proofPassportItems: [
        { id: "source", label: "Source", value: "Delivery failure", detail: "Delivery evidence has a retained failure.", href: "/reporting/evidence", ariaLabel: "Open source for Investor delivery", badgeVariant: "warning" },
        { id: "freshness", label: "Freshness", value: "Due 2026-06-26", detail: "Due 2026-06-26", href: "/reporting/evidence", ariaLabel: "Open freshness support for Investor delivery", badgeVariant: "warning" },
        { id: "reconciliation", label: "Reconciliation", value: "Route-owned", detail: "Reconciliation state is owned by the Reporting route and shared reporting read model.", href: "/reporting/evidence", ariaLabel: "Open reconciliation support for Investor delivery", badgeVariant: "outline" },
        { id: "approvals", label: "Approvals", value: "Route-owned", detail: "Approval state is owned by the Reporting route and shared reporting read model.", href: "/reporting/evidence", ariaLabel: "Open approval support for Investor delivery", badgeVariant: "outline" },
        { id: "report-usage", label: "Report Usage", value: "Investor delivery", detail: "Reporting output: Investor delivery.", href: "/reporting/evidence", ariaLabel: "Open report usage for Investor delivery", badgeVariant: "warning" },
        { id: "blockers", label: "Blockers", value: "Needs review", detail: "Review", href: "/reporting/evidence", ariaLabel: "Open blockers for Investor delivery", badgeVariant: "warning" },
        { id: "evidence-packet", label: "Evidence Packet", value: "1 proof item", detail: "Delivery receipt", href: "/reporting/evidence", ariaLabel: "Open evidence packet for Investor delivery", badgeVariant: "warning" },
        { id: "audit-trail", label: "Audit Trail", value: "Open delivery", detail: "/reporting/evidence", href: "/reporting/evidence", ariaLabel: "Open audit trail for Investor delivery", badgeVariant: "warning" }
      ],
      evidenceGaps: ["Missing recipient confirmation"],
      context: ["Investor statements"],
      secondaryActionLabel: null,
      secondaryActionHref: null,
      controlQuestionSummary: "Delivery evidence missing needs review for Reporting Ops.",
      ariaLabel: "Delivery evidence missing needs review for Reporting Ops."
    }
  ],
  dailyWorkSummaryLabel: "1 daily reporting work item",
  decisionQueues: [
    {
      id: "blocked-package",
      title: "Shadow NAV pack",
      statusLabel: "Blocked",
      countLabel: "1 item",
      detail: "Shadow NAV pack cannot be delivered until evidence is fixed.",
      tone: "danger",
      badgeVariant: "danger",
      isBlocked: true,
      ownerLabel: "Controller",
      affectedOutputLabel: "Shadow NAV pack",
      nextActionLabel: "Fix delivery evidence",
      primaryActionLabel: "Open evidence",
      primaryActionHref: "/reporting/evidence",
      secondaryActionLabel: null,
      secondaryActionHref: null,
      proofLabel: "Manifest gap",
      proofItems: ["Manifest gap", "Delivery failure"],
      proofPassportStatusLabel: "Blocked",
      proofPassportSummary: "Number Passport for Shadow NAV pack: Source Blocked package queue; Freshness Due 2026-06-26; Reconciliation Route-owned; Approvals Route-owned; Report Usage Shadow NAV pack; Blockers Blocked; Evidence Packet Manifest gap; Audit Trail Open evidence.",
      proofPassportItems: [
        { id: "source", label: "Source", value: "Blocked package queue", detail: "Shadow NAV pack cannot be delivered until evidence is fixed. Owner: Controller.", href: "/reporting/evidence", ariaLabel: "Open source for Shadow NAV pack", badgeVariant: "danger" },
        { id: "freshness", label: "Freshness", value: "Due 2026-06-26", detail: "Due 2026-06-26", href: "/reporting/evidence", ariaLabel: "Open freshness support for Shadow NAV pack", badgeVariant: "danger" },
        { id: "reconciliation", label: "Reconciliation", value: "Route-owned", detail: "Reconciliation state is owned by the Reporting route and shared reporting read model.", href: "/reporting/evidence", ariaLabel: "Open reconciliation support for Shadow NAV pack", badgeVariant: "outline" },
        { id: "approvals", label: "Approvals", value: "Route-owned", detail: "Approval state is owned by the Reporting route and shared reporting read model.", href: "/reporting/evidence", ariaLabel: "Open approval support for Shadow NAV pack", badgeVariant: "outline" },
        { id: "report-usage", label: "Report Usage", value: "Shadow NAV pack", detail: "Reporting output: Shadow NAV pack.", href: "/reporting/evidence", ariaLabel: "Open report usage for Shadow NAV pack", badgeVariant: "danger" },
        { id: "blockers", label: "Blockers", value: "Blocked", detail: "Blocked", href: "/reporting/evidence", ariaLabel: "Open blockers for Shadow NAV pack", badgeVariant: "danger" },
        { id: "evidence-packet", label: "Evidence Packet", value: "Manifest gap", detail: "Manifest gap", href: "/reporting/evidence", ariaLabel: "Open evidence packet for Shadow NAV pack", badgeVariant: "danger" },
        { id: "audit-trail", label: "Audit Trail", value: "Open evidence", detail: "/reporting/evidence", href: "/reporting/evidence", ariaLabel: "Open audit trail for Shadow NAV pack", badgeVariant: "danger" }
      ],
      controlQuestionSummary: "Shadow NAV pack is blocked for Controller.",
      ariaLabel: "Blocked package queue: Shadow NAV pack blocked for Controller until Fix delivery evidence. Proof: Manifest gap, Delivery failure"
    }
  ],
  decisionQueueSummaryLabel: "1 decision queue · 1 blocked · 0 need review",
  cards: [],
  totalFamilies: 0,
  currentCount: 0,
  attentionCount: 0,
  summaryLabel: "No report families",
  isEmpty: false
};
