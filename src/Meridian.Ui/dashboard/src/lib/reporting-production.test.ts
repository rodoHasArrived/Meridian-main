import { describe, expect, it } from "vitest";
import {
  buildReportingProductionModel,
  REPORTING_LANES,
  type ReportingDailyWorkInput,
  type ReportingProductionRunInput,
  type ReportingProductionTemplateInput
} from "@/lib/reporting-production";

const NOW = "2026-10-04T12:00:00Z";

function run(overrides: Partial<ReportingProductionRunInput> = {}): ReportingProductionRunInput {
  return {
    runId: "run-1",
    templateId: "tmpl-1",
    family: "Performance",
    status: "Draft",
    asOfDate: "2026-09-30",
    ...overrides
  };
}

function template(overrides: Partial<ReportingProductionTemplateInput> = {}): ReportingProductionTemplateInput {
  return {
    templateId: "tmpl-1",
    name: "Portfolio Performance",
    family: "Performance",
    ...overrides
  };
}

describe("reporting lanes", () => {
  it("exposes the seven pipeline lanes in order with numbered ordinals", () => {
    const model = buildReportingProductionModel({ runs: [], evaluationAtUtc: NOW });

    expect(model.lanes.map((lane) => lane.key)).toEqual([...REPORTING_LANES]);
    expect(model.lanes.map((lane) => lane.ordinalLabel)).toEqual(["01", "02", "03", "04", "05", "06", "07"]);
    expect(model.lanes.map((lane) => lane.stage)).toEqual([
      "Plan",
      "Prepare",
      "Prepare",
      "Review",
      "Preserve",
      "Plan",
      "Approve"
    ]);
  });

  it("routes each lane to a real workstation route", () => {
    const model = buildReportingProductionModel({ runs: [], evaluationAtUtc: NOW });
    const byKey = Object.fromEntries(model.lanes.map((lane) => [lane.key, lane.href]));

    expect(byKey.Library).toBe("/reporting/library");
    expect(byKey.Production).toBe("/reporting/run-status");
    expect(byKey.Builder).toBe("/reporting/report-builder");
    expect(byKey.Review).toBe("/reporting/preview");
    expect(byKey.Published).toBe("/reporting/report-packs");
    expect(byKey.Schedules).toBe("/reporting/scheduled");
    expect(byKey.Templates).toBe("/reporting/governance");
  });

  it("counts lane contents and leaves unknown counts blank rather than zero", () => {
    const model = buildReportingProductionModel({
      runs: [
        run({ runId: "a", templateId: "t1", status: "Draft" }),
        run({ runId: "b", templateId: "t2", status: "InReview" }),
        run({ runId: "c", templateId: "t3", status: "Released" })
      ],
      templates: [template({ templateId: "t1" }), template({ templateId: "t2" })],
      evaluationAtUtc: NOW
    });

    const byKey = Object.fromEntries(model.lanes.map((lane) => [lane.key, lane]));
    expect(byKey.Library.count).toBe(2);
    expect(byKey.Production.count).toBe(2);
    expect(byKey.Builder.count).toBe(1);
    expect(byKey.Review.count).toBe(1);
    expect(byKey.Published.count).toBe(1);
    expect(byKey.Schedules.count).toBeNull();
    expect(byKey.Schedules.countLabel).toBe("—");
  });
});

describe("production register", () => {
  it("resolves the report name from the template and falls back to the family", () => {
    const model = buildReportingProductionModel({
      runs: [run({ templateId: "tmpl-1" }), run({ runId: "run-2", templateId: "unknown", family: "Credit" })],
      templates: [template()],
      evaluationAtUtc: NOW
    });

    const names = model.register.map((row) => row.reportName);
    expect(names).toContain("Portfolio Performance");
    expect(names).toContain("Credit");
  });

  it("maps run statuses onto the controlled workflow vocabulary", () => {
    const model = buildReportingProductionModel({
      runs: [
        run({ runId: "a", templateId: "t1", status: "Released" }),
        run({ runId: "b", templateId: "t2", status: "Validated" }),
        run({ runId: "c", templateId: "t3", status: "Failed" })
      ],
      evaluationAtUtc: NOW
    });

    const byId = Object.fromEntries(model.register.map((row) => [row.runId, row]));
    expect(byId.a.stateLabel).toBe("Published");
    expect(byId.b.stateLabel).toBe("Ready for review");
    expect(byId.c.stateLabel).toBe("Blocked");
    expect(byId.c.severity).toBe("blocked");
  });

  it("ranks blocked reports first, then overdue, then lifecycle position", () => {
    const model = buildReportingProductionModel({
      runs: [
        run({ runId: "published", templateId: "t1", status: "Released" }),
        run({ runId: "overdue", templateId: "t2", status: "InReview", dueAtUtc: "2026-10-01T00:00:00Z" }),
        run({ runId: "blocked", templateId: "t3", status: "Failed" }),
        run({ runId: "preparing", templateId: "t4", status: "Draft" })
      ],
      evaluationAtUtc: NOW
    });

    expect(model.register.map((row) => row.runId)).toEqual(["blocked", "overdue", "preparing", "published"]);
  });

  it("marks a past due date overdue but never a published report", () => {
    const model = buildReportingProductionModel({
      runs: [
        run({ runId: "late", templateId: "t1", status: "Preparing", dueAtUtc: "2026-10-01T00:00:00Z" }),
        run({ runId: "done", templateId: "t2", status: "Released", dueAtUtc: "2026-10-01T00:00:00Z" })
      ],
      evaluationAtUtc: NOW
    });

    const byId = Object.fromEntries(model.register.map((row) => [row.runId, row]));
    expect(byId.late.isOverdue).toBe(true);
    expect(byId.late.dueLabel).toBe("Oct 1");
    expect(byId.done.isOverdue).toBe(false);
  });

  it("collapses repeated attempts to the current one per report", () => {
    const model = buildReportingProductionModel({
      runs: [
        run({ runId: "attempt-1", templateId: "tmpl-1", status: "Failed", isLatestGenerated: false }),
        run({ runId: "attempt-2", templateId: "tmpl-1", status: "Released", isLatestGenerated: true })
      ],
      evaluationAtUtc: NOW
    });

    expect(model.register).toHaveLength(1);
    expect(model.register[0].runId).toBe("attempt-2");
  });

  it("labels an unowned report rather than leaving the column blank", () => {
    const model = buildReportingProductionModel({ runs: [run({ owner: "   " })], evaluationAtUtc: NOW });
    expect(model.register[0].owner).toBe("Unassigned");
  });

  it("reports an unusable as-of date instead of formatting a sentinel", () => {
    const model = buildReportingProductionModel({
      runs: [run({ asOfDate: "as-of-date-unavailable" })],
      evaluationAtUtc: NOW
    });

    expect(model.register[0].asOfLabel).toBe("As-of unavailable");
  });

  it("classifies reports by family", () => {
    const model = buildReportingProductionModel({
      runs: [
        run({ runId: "a", templateId: "t1", family: "Ledger" }),
        run({ runId: "b", templateId: "t2", family: "Statutory" }),
        run({ runId: "c", templateId: "t3", family: "Performance" })
      ],
      evaluationAtUtc: NOW
    });

    const byId = Object.fromEntries(model.register.map((row) => [row.runId, row.reportClass]));
    expect(byId).toEqual({ a: "Accounting", b: "Regulatory", c: "Portfolio" });
  });
});

describe("production headline and attention", () => {
  it("summarises ready, review and blocked counts", () => {
    const model = buildReportingProductionModel({
      runs: [
        run({ runId: "a", templateId: "t1", status: "Approved" }),
        run({ runId: "b", templateId: "t2", status: "Released" }),
        run({ runId: "c", templateId: "t3", status: "InReview" }),
        run({ runId: "d", templateId: "t4", status: "Failed" })
      ],
      evaluationAtUtc: NOW
    });

    expect(model.readyCount).toBe(2);
    expect(model.reviewCount).toBe(1);
    expect(model.blockedCount).toBe(1);
    expect(model.headlineLabel).toBe("2 / 4 ready  ·  1 review  ·  1 blocked");
  });

  it("omits empty categories from the headline", () => {
    const model = buildReportingProductionModel({
      runs: [run({ status: "Approved" })],
      evaluationAtUtc: NOW
    });

    expect(model.headlineLabel).toBe("1 / 1 ready");
  });

  it("builds attention items from register state and external signals", () => {
    const model = buildReportingProductionModel({
      runs: [
        run({ runId: "a", templateId: "t1", status: "Failed" }),
        run({ runId: "b", templateId: "t2", status: "Rejected" }),
        run({ runId: "c", templateId: "t3", status: "AwaitingApproval" })
      ],
      signals: { staleSourceCount: 4, openCommentCount: 3 },
      evaluationAtUtc: NOW
    });

    expect(model.attention.map((item) => [item.key, item.label])).toEqual([
      ["blocked", "1 blocked"],
      ["staleSources", "4 stale sources"],
      ["changesRequested", "1 changes requested"],
      ["comments", "3 comments"],
      ["approvals", "1 approval due"]
    ]);
  });

  it("omits attention items with nothing to report", () => {
    const model = buildReportingProductionModel({
      runs: [run({ status: "Approved" })],
      signals: { staleSourceCount: 0, openCommentCount: 0 },
      evaluationAtUtc: NOW
    });

    expect(model.attention).toEqual([]);
  });

  it("lists at most five recently published reports", () => {
    const model = buildReportingProductionModel({
      runs: Array.from({ length: 7 }, (_, index) =>
        run({ runId: `pub-${index}`, templateId: `t-${index}`, status: "Released" })),
      evaluationAtUtc: NOW
    });

    expect(model.publishedCount).toBe(7);
    expect(model.recentlyPublished).toHaveLength(5);
  });

  it("reports an empty period without inventing counts", () => {
    const model = buildReportingProductionModel({ runs: [], evaluationAtUtc: NOW });

    expect(model.isEmpty).toBe(true);
    expect(model.headlineLabel).toBe("No reports in production");
    expect(model.attention).toEqual([]);
    expect(model.periodLabel).toBe("Current period");
  });
});

describe("period confirmation guard", () => {
  it("does not present a terminal state as ready when no period was retained", () => {
    const model = buildReportingProductionModel({
      runs: [
        run({ runId: "undated", templateId: "t1", status: "Released", asOfDate: "as-of-date-unavailable" }),
        run({ runId: "dated", templateId: "t2", status: "Released", asOfDate: "2026-09-30" })
      ],
      evaluationAtUtc: NOW
    });

    const byId = Object.fromEntries(model.register.map((row) => [row.runId, row]));
    expect(byId.undated.requiresPeriodConfirmation).toBe(true);
    expect(byId.undated.stateLabel).toBe("Period confirmation required");
    expect(byId.undated.workflowState).not.toBe("Published");
    expect(byId.dated.requiresPeriodConfirmation).toBe(false);

    // Only the dated output is canonical.
    expect(model.recentlyPublished.map((row) => row.runId)).toEqual(["dated"]);
    expect(model.publishedCount).toBe(1);
  });

  it("leaves non-terminal undated runs alone", () => {
    const model = buildReportingProductionModel({
      runs: [run({ status: "Draft", asOfDate: "as-of-date-unavailable" })],
      evaluationAtUtc: NOW
    });

    expect(model.register[0].requiresPeriodConfirmation).toBe(false);
    expect(model.register[0].stateLabel).toBe("Preparing");
  });
});

describe("governed report identity and periods", () => {
  it("resolves a versioned template identity against the unversioned template key", () => {
    const model = buildReportingProductionModel({
      runs: [run({ templateId: "board-pack:v1", family: "GovernedReportPack" })],
      templates: [template({ templateId: "board-pack", name: "Board Pack", family: "GovernedReportPack" })],
      evaluationAtUtc: NOW
    });

    expect(model.register[0].reportName).toBe("Board Pack");
  });

  it("presents governed period tokens instead of discarding them", () => {
    const model = buildReportingProductionModel({
      runs: [
        run({ runId: "month", templateId: "t1", asOfDate: "2026-06" }),
        run({ runId: "periodNumber", templateId: "t2", asOfDate: "2026-P03" }),
        run({ runId: "relative", templateId: "t3", asOfDate: "CurrentMonth" }),
        run({ runId: "iso", templateId: "t4", asOfDate: "2026-09-30" }),
        run({ runId: "sentinel", templateId: "t5", asOfDate: "as-of-date-unavailable" })
      ],
      evaluationAtUtc: NOW
    });

    const byId = Object.fromEntries(model.register.map((row) => [row.runId, row.asOfLabel]));
    expect(byId.month).toBe("Jun 2026");
    expect(byId.periodNumber).toBe("2026-P03");
    expect(byId.relative).toBe("CurrentMonth");
    expect(byId.iso).toBe("Sep 30, 2026");
    expect(byId.sentinel).toBe("As-of unavailable");
  });
});

describe("terminal states and lane counts", () => {
  it("keeps restatements out of active production and counts them as published", () => {
    const model = buildReportingProductionModel({
      runs: [
        run({ runId: "restated", templateId: "t1", status: "Restated" }),
        run({ runId: "preparing", templateId: "t2", status: "Draft" })
      ],
      evaluationAtUtc: NOW
    });

    const byKey = Object.fromEntries(model.lanes.map((lane) => [lane.key, lane.count]));
    expect(byKey.Production).toBe(1);
    expect(byKey.Published).toBe(1);
    expect(model.publishedCount).toBe(1);
  });

  it("treats an archived pack as retained history, not active preparation", () => {
    const model = buildReportingProductionModel({
      runs: [run({ runId: "archived", templateId: "t1", status: "Archived" })],
      evaluationAtUtc: NOW
    });

    expect(model.register[0].stateLabel).toBe("Archived");
    const byKey = Object.fromEntries(model.lanes.map((lane) => [lane.key, lane.count]));
    expect(byKey.Production).toBe(0);
    expect(byKey.Builder).toBe(0);
    expect(byKey.Published).toBe(0);
  });

  it("orders recently published by publication recency rather than report name", () => {
    const model = buildReportingProductionModel({
      runs: [
        run({ runId: "alpha", templateId: "t1", status: "Released", reportName: "Alpha", publishedAtUtc: "2026-10-01T00:00:00Z" }),
        run({ runId: "zulu", templateId: "t2", status: "Released", reportName: "Zulu", publishedAtUtc: "2026-10-03T00:00:00Z" }),
        run({ runId: "mike", templateId: "t3", status: "Released", reportName: "Mike", publishedAtUtc: "2026-10-02T00:00:00Z" })
      ],
      evaluationAtUtc: NOW
    });

    expect(model.recentlyPublished.map((row) => row.runId)).toEqual(["zulu", "mike", "alpha"]);
    // The triage register still ranks alphabetically within a lifecycle position.
    expect(model.register.map((row) => row.reportName)).toEqual(["Alpha", "Mike", "Zulu"]);
  });

  it("falls back to the source ordering when no publication timestamp is retained", () => {
    const model = buildReportingProductionModel({
      runs: [
        run({ runId: "newest", templateId: "t1", status: "Released", reportName: "Zulu" }),
        run({ runId: "oldest", templateId: "t2", status: "Released", reportName: "Alpha" })
      ],
      evaluationAtUtc: NOW
    });

    // The shared services return update-ordered history, newest first.
    expect(model.recentlyPublished.map((row) => row.runId)).toEqual(["newest", "oldest"]);
  });
});

describe("daily work in the attention rail", () => {
  function work(overrides: Partial<ReportingDailyWorkInput> = {}): ReportingDailyWorkInput {
    return {
      workItemId: "w1",
      kind: "delivery-failure",
      title: "Board portal package failed",
      tone: "info",
      ...overrides
    };
  }

  it("surfaces blocked packages, evidence gaps and overdue work from the shared projection", () => {
    const model = buildReportingProductionModel({
      runs: [run({ status: "Approved" })],
      signals: {
        dailyWork: [
          work({ workItemId: "blocked", tone: "danger", primaryActionHref: "/reporting/report-packs?recipient=board" }),
          work({ workItemId: "gap", tone: "info", evidenceGaps: ["Delivery rejection lacks retained portal proof."] }),
          work({ workItemId: "late", tone: "info", dueAtUtc: "2026-10-01T00:00:00Z" })
        ]
      },
      evaluationAtUtc: NOW
    });

    const byKey = Object.fromEntries(model.attention.map((item) => [item.key, item]));
    expect(byKey.blockedWork).toMatchObject({ label: "1 blocked package", severity: "blocked" });
    expect(byKey.blockedWork.href).toBe("/reporting/report-packs?recipient=board");
    expect(byKey.evidenceGaps).toMatchObject({ label: "1 evidence gap", severity: "action" });
    expect(byKey.overdueWork).toMatchObject({ label: "1 work item past due", severity: "blocked" });
  });

  it("no longer claims nothing needs attention while the server holds urgent work", () => {
    const model = buildReportingProductionModel({
      runs: [run({ status: "Approved" })],
      signals: { dailyWork: [work({ tone: "danger" })] },
      evaluationAtUtc: NOW
    });

    expect(model.attention).not.toEqual([]);
  });

  it("stays quiet when the projection carries no urgent work", () => {
    const model = buildReportingProductionModel({
      runs: [run({ status: "Approved" })],
      signals: { dailyWork: [work({ tone: "success" })] },
      evaluationAtUtc: NOW
    });

    expect(model.attention).toEqual([]);
  });
});
