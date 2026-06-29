import { describe, expect, it } from "vitest";
import type {
  AppShellDecisionBrief,
  AppShellEvidenceTimelineItem,
  AppShellLinkedContextItem,
  AppShellOperatorFocusItem,
  AppShellWorkflowContinuityViewModel
} from "@/lib/app-shell-workflow-continuity";
import {
  buildDailyControlTowerModel,
  dailyControlTowerBadgeVariant,
  findDailyControlTowerProof
} from "@/lib/daily-control-tower";

describe("daily control tower model", () => {
  it("projects focus rows with status, affected output, next action, and proof", () => {
    const model = buildDailyControlTowerModel(workflowViewModel({
      decisionBrief: decisionBrief({ statusTone: "blocked" }),
      operatorFocusSummary: "2 focus items across workspaces: 1 blocked and 1 review.",
      operatorFocusOverflowLabel: "+1 more focus item",
      operatorFocusItems: [
        operatorFocus({
          id: "focus:settings",
          label: "Brokerage sync failed",
          workspaceLabel: "Settings",
          route: "/settings#alpaca-provider-setup",
          actionLabel: "Fix provider setup",
          tone: "blocked"
        }),
        operatorFocus({
          id: "focus:reporting",
          label: "Report pack approval waiting",
          workspaceLabel: "Reporting",
          route: "/reporting/report-packs",
          actionLabel: "Open report packs",
          tone: "review"
        })
      ],
      evidenceTimelineItems: [
        evidenceItem({
          id: "focus:settings",
          label: "Brokerage sync failed",
          workspaceLabel: "Settings",
          timestampLabel: "2026-05-14 20:00 UTC",
          route: "/settings#alpaca-provider-setup",
          tone: "blocked"
        }),
        evidenceItem({
          id: "evidence:reporting",
          label: "Report pack approval waiting",
          workspaceLabel: "Reporting",
          timestampLabel: "2026-05-14 21:00 UTC",
          route: "/reporting/report-packs",
          tone: "review"
        })
      ]
    }));

    expect(model.decisionBadgeVariant).toBe("danger");
    expect(model.focusSummary).toBe("2 focus items across workspaces: 1 blocked and 1 review.");
    expect(model.focusOverflowLabel).toBe("+1 more focus item");
    expect(model.decisionFacts.map((fact) => [
      fact.id,
      fact.label,
      fact.value,
      fact.href,
      fact.badgeVariant
    ])).toEqual([
      ["status", "Blocked state", "Blocked", null, "danger"],
      ["owner", "Owner", "Settings", "/settings#alpaca-provider-setup", null],
      ["output", "Affected output", "Settings: Brokerage sync failed", "/settings#alpaca-provider-setup", null],
      ["next-action", "Next action", "Fix provider setup", "/settings#alpaca-provider-setup", null],
      ["proof", "Supporting proof", "Brokerage sync failed / 2026-05-14 20:00 UTC", "/settings#alpaca-provider-setup", "danger"]
    ]);
    expect(model.focusRows.map((row) => [
      row.item.label,
      row.statusLabel,
      row.outputLabel,
      row.item.route,
      row.item.actionLabel,
      row.badgeVariant,
      row.proof?.id,
      row.proof?.timestampLabel
    ])).toEqual([
      [
        "Brokerage sync failed",
        "Blocked",
        "Settings: Brokerage sync failed",
        "/settings#alpaca-provider-setup",
        "Fix provider setup",
        "danger",
        "focus:settings",
        "2026-05-14 20:00 UTC"
      ],
      [
        "Report pack approval waiting",
        "Review",
        "Reporting: Report pack approval waiting",
        "/reporting/report-packs",
        "Open report packs",
        "warning",
        "evidence:reporting",
        "2026-05-14 21:00 UTC"
      ]
    ]);
    expect(model.focusRows[0].proofPassportStatusLabel).toBe("Blocked proof");
    expect(model.focusRows[0].proofPassportSummary).toContain("Proof Passport for Settings: Brokerage sync failed");
    expect(model.focusRows[0].proofPassportItems.map((item) => [
      item.id,
      item.label,
      item.value,
      item.href,
      item.badgeVariant
    ])).toEqual([
      ["source", "Source", "Settings", "/settings#alpaca-provider-setup", "danger"],
      ["freshness", "Freshness", "2026-05-14 20:00 UTC", "/settings#alpaca-provider-setup", "danger"],
      ["reconciliation", "Reconciliation", "Route-owned", "/settings#alpaca-provider-setup", "outline"],
      ["approvals", "Approvals", "Route-owned", "/settings#alpaca-provider-setup", "outline"],
      ["report-usage", "Report Usage", "Route-owned", "/settings#alpaca-provider-setup", "outline"],
      ["blockers", "Blockers", "Blocked", "/settings#alpaca-provider-setup", "danger"],
      ["evidence-packet", "Evidence Packet", "Brokerage sync failed / 2026-05-14 20:00 UTC", "/settings#alpaca-provider-setup", "danger"],
      ["audit-trail", "Audit Trail", "2026-05-14 20:00 UTC", "/settings#alpaca-provider-setup", "danger"]
    ]);
    expect(model.focusRows[1].proofPassportItems.find((item) => item.id === "approvals")).toMatchObject({
      value: "Review",
      badgeVariant: "warning"
    });
    expect(model.focusRows[1].proofPassportItems.find((item) => item.id === "report-usage")).toMatchObject({
      value: "Reporting: Report pack approval waiting",
      badgeVariant: "warning"
    });
  });

  it("keeps supporting context caps and tone mapping in the route model", () => {
    const model = buildDailyControlTowerModel(workflowViewModel({
      linkedContextPostureTone: "review",
      linkedContextItems: [1, 2, 3, 4, 5].map((index) => linkedContextItem({ id: `linked-${index}` })),
      evidenceTimelineItems: [1, 2, 3, 4, 5].map((index) => evidenceItem({ id: `evidence-${index}` }))
    }));

    expect(model.linkedContextBadgeVariant).toBe("warning");
    expect(model.linkedContextItems.map((item) => item.id)).toEqual(["linked-1", "linked-2", "linked-3", "linked-4"]);
    expect(model.evidenceTimelineItems.map((item) => item.id)).toEqual(["evidence-1", "evidence-2", "evidence-3", "evidence-4"]);
    expect(dailyControlTowerBadgeVariant("ready")).toBe("success");
    expect(dailyControlTowerBadgeVariant("pending")).toBe("outline");
  });

  it("falls back to the first evidence item when no id or workspace proof matches", () => {
    const proof = findDailyControlTowerProof(
      operatorFocus({ id: "focus:data", workspaceLabel: "Data" }),
      [
        evidenceItem({ id: "evidence:settings", workspaceLabel: "Settings" }),
        evidenceItem({ id: "evidence:reporting", workspaceLabel: "Reporting" })
      ]
    );

    expect(proof?.id).toBe("evidence:settings");
  });
});

function workflowViewModel(overrides: Partial<AppShellWorkflowContinuityViewModel>): AppShellWorkflowContinuityViewModel {
  return {
    decisionBrief: decisionBrief(),
    operatorFocusSummary: "No focus items.",
    operatorFocusEmptyText: "Loaded workspaces have no ranked blockers.",
    operatorFocusOverflowLabel: null,
    operatorFocusItems: [],
    linkedContextLabel: "Linked context",
    linkedContextSummary: "Related workspace context.",
    linkedContextPostureLabel: "Ready",
    linkedContextPostureTone: "ready",
    linkedContextEmptyText: "No linked context.",
    linkedContextItems: [],
    evidenceTimelineLabel: "Evidence timeline",
    evidenceTimelineSummary: "Recent evidence.",
    evidenceTimelineEmptyText: "No evidence.",
    evidenceTimelineOverflowLabel: null,
    evidenceTimelineItems: [],
    ...overrides
  } as AppShellWorkflowContinuityViewModel;
}

function decisionBrief(overrides: Partial<AppShellDecisionBrief> = {}): AppShellDecisionBrief {
  return {
    label: "Decision brief",
    title: "Resolve Brokerage sync failed",
    summary: "Settings is the highest-priority loaded issue.",
    reasonLabel: "Why now",
    reason: "Account sync failed after the last provider heartbeat.",
    statusLabel: "Blocked",
    statusTone: "blocked",
    evidenceLabel: "Latest evidence: Settings 2026-05-14 20:00 UTC",
    actionLabel: "Fix provider setup",
    actionHref: "/settings#alpaca-provider-setup",
    actionAriaLabel: "Settings: Brokerage sync failed. Fix provider setup.",
    ...overrides
  };
}

function operatorFocus(overrides: Partial<AppShellOperatorFocusItem> = {}): AppShellOperatorFocusItem {
  return {
    id: "focus:settings",
    label: "Brokerage sync failed",
    detail: "Account sync failed after the last provider heartbeat.",
    route: "/settings#alpaca-provider-setup",
    workspaceLabel: "Settings",
    actionLabel: "Fix provider setup",
    tone: "blocked",
    ariaLabel: "Settings: Brokerage sync failed. Fix provider setup.",
    ...overrides
  };
}

function evidenceItem(overrides: Partial<AppShellEvidenceTimelineItem> = {}): AppShellEvidenceTimelineItem {
  return {
    id: "evidence:settings",
    label: "Brokerage sync failed",
    detail: "Audit: audit-1.",
    route: "/settings#alpaca-provider-setup",
    workspaceLabel: "Settings",
    timestampLabel: "2026-05-14 20:00 UTC",
    timestampIso: "2026-05-14T20:00:00Z",
    tone: "blocked",
    ariaLabel: "Settings: Brokerage sync failed. Audit: audit-1. Open evidence.",
    ...overrides
  };
}

function linkedContextItem(overrides: Partial<AppShellLinkedContextItem> = {}): AppShellLinkedContextItem {
  return {
    id: "linked-1",
    label: "Trading cockpit",
    detail: "Review execution posture.",
    route: "/trading",
    workspaceLabel: "Trading",
    statusLabel: "Review",
    tone: "review",
    ariaLabel: "Trading: Trading cockpit. Review execution posture.",
    ...overrides
  };
}
