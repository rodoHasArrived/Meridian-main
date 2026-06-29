import { describe, expect, it } from "vitest";
import { buildReportingHubModel, type ReportingHubDailyWorkInput } from "@/lib/reporting-hub";
import {
  buildReportingTaskModes,
  resolveReportingRouteFocusMode,
  type ReportingTaskModeInput
} from "@/lib/reporting-task-modes";

describe("reporting task modes", () => {
  it("resolves the active reporting mode from the route and hash", () => {
    expect(activeModeId({ pathname: "/reporting", hash: "" })).toBe("cockpit");
    expect(activeModeId({ pathname: "/reporting/run-status", hash: "" })).toBe("run-status");
    expect(activeModeId({ pathname: "/reporting", hash: "#run-status" })).toBe("run-status");
    expect(activeModeId({ pathname: "/reporting/report-builder", hash: "" })).toBe("report-builder");
    expect(activeModeId({ pathname: "/reporting/report-packs", hash: "" })).toBe("report-builder");
    expect(activeModeId({ pathname: "/reporting/governance", hash: "" })).toBe("governance");
    expect(activeModeId({ pathname: "/reporting/report-packs", hash: "#reporting-governance" })).toBe("governance");
    expect(activeModeId({ pathname: "/reporting/delivery-evidence", hash: "" })).toBe("delivery-evidence");
    expect(activeModeId({ pathname: "/reporting/evidence", hash: "" })).toBe("delivery-evidence");
    expect(activeModeId({ pathname: "/reporting/exports", hash: "" })).toBe("exports");
  });

  it("keeps canonical reporting routes focused while legacy aliases stay broad", () => {
    expect(resolveReportingRouteFocusMode({ pathname: "/reporting", hash: "" })).toBe("cockpit");
    expect(resolveReportingRouteFocusMode({ pathname: "/reporting", hash: "#run-status" })).toBe("run-status");
    expect(resolveReportingRouteFocusMode({ pathname: "/reporting/report-builder", hash: "" })).toBe("report-builder");
    expect(resolveReportingRouteFocusMode({ pathname: "/reporting/run-status", hash: "" })).toBe("run-status");
    expect(resolveReportingRouteFocusMode({ pathname: "/reporting/delivery-evidence", hash: "" })).toBe("delivery-evidence");
    expect(resolveReportingRouteFocusMode({ pathname: "/reporting/exports", hash: "" })).toBe("exports");
    expect(resolveReportingRouteFocusMode({ pathname: "/reporting/governance", hash: "" })).toBe("governance");
    expect(resolveReportingRouteFocusMode({ pathname: "/reporting/report-packs", hash: "" })).toBe("legacy-broad");
    expect(resolveReportingRouteFocusMode({
      pathname: "/reporting/report-packs/11111111-1111-1111-1111-111111111111/packages/pkg-board-1",
      hash: ""
    })).toBe("legacy-broad");
    expect(resolveReportingRouteFocusMode({ pathname: "/reporting/report-packs", hash: "#reporting-governance" })).toBe("legacy-broad");
    expect(resolveReportingRouteFocusMode({ pathname: "/reporting/evidence", hash: "" })).toBe("legacy-broad");
  });

  it("keeps daily reporting queue guidance on the focused mode model", () => {
    const modes = buildReportingTaskModes(baseInput({
      dailyWork: [
        dailyWork({
          workItemId: "delivery-failure:board",
          kind: "delivery-failure",
          title: "Delivery failure: Board portal",
          tone: "danger"
        }),
        dailyWork({
          workItemId: "approval-needed:monthly",
          kind: "approval-needed",
          title: "Approval needed: Monthly pack",
          tone: "warning"
        }),
        dailyWork({
          workItemId: "readiness-warning:evidence",
          kind: "readiness-warning",
          title: "Evidence readiness warning",
          tone: "warning"
        })
      ]
    }));

    expect(mode(modes, "cockpit")).toMatchObject({
      statusLabel: "Blocked",
      badgeVariant: "danger",
      recommended: true,
      countLabel: "3 daily items · 1 blocked · 2 need review"
    });
    expect(mode(modes, "run-status")).toMatchObject({ recommended: true });
    expect(mode(modes, "delivery-evidence")).toMatchObject({
      statusLabel: "Evidence review",
      badgeVariant: "warning",
      recommended: true
    });
    expect(mode(modes, "governance")).toMatchObject({ recommended: true });
    expect(mode(modes, "exports")).toMatchObject({ recommended: true });
  });
});

function activeModeId(route: Pick<ReportingTaskModeInput, "pathname" | "hash">) {
  return buildReportingTaskModes(baseInput(route)).find((item) => item.active)?.id;
}

function baseInput(overrides: Partial<ReportingTaskModeInput> & { dailyWork?: ReportingHubDailyWorkInput[] } = {}): ReportingTaskModeInput {
  const { dailyWork = [], ...inputOverrides } = overrides;
  return {
    pathname: "/reporting",
    hash: "",
    hubModel: buildReportingHubModel([], [], dailyWork),
    templateCount: 3,
    runCount: 2,
    writerGridCount: 1,
    deliveryPlanCount: 4,
    structuredExportCount: 5,
    workflowCount: 6,
    accessSummary: "Scoped",
    ...inputOverrides
  };
}

function dailyWork(overrides: Partial<ReportingHubDailyWorkInput>): ReportingHubDailyWorkInput {
  return {
    workItemId: "daily-work",
    kind: "approval-needed",
    title: "Daily work",
    statusLabel: "Needs review",
    detail: "Operator review is required.",
    tone: "warning",
    owner: "controller",
    dueAtUtc: null,
    primaryActionLabel: "Review",
    primaryActionHref: "/reporting",
    ...overrides
  };
}

function mode<T extends { id: string }>(modes: T[], id: T["id"]): T {
  const found = modes.find((item) => item.id === id);
  if (!found) {
    throw new Error(`Expected reporting mode ${id}`);
  }

  return found;
}
