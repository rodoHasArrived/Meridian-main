import { describe, expect, it } from "vitest";
import { buildReportingProductionSurfaceViewModel } from "@/screens/reporting-screen.production-surface";

const NOW = "2026-10-04T12:00:00Z";

function runRow(overrides: Record<string, unknown> = {}) {
  return {
    id: "run-1",
    templateId: "tmpl-1",
    family: "Performance",
    status: "Draft",
    asOfDateLabel: "2026-09-30",
    isLatestGenerated: true,
    failureReason: null,
    ...overrides
  } as Parameters<typeof buildReportingProductionSurfaceViewModel>[0][number];
}

function templateRow(overrides: Record<string, unknown> = {}) {
  return {
    id: "tmpl-1",
    name: "Portfolio Performance",
    family: "Performance",
    ...overrides
  } as Parameters<typeof buildReportingProductionSurfaceViewModel>[1][number];
}

describe("reporting production surface view-model", () => {
  it("projects run rows onto the production register", () => {
    const { production } = buildReportingProductionSurfaceViewModel(
      [runRow({ id: "a", status: "Released" })],
      [templateRow()],
      [],
      NOW
    );

    expect(production.register).toHaveLength(1);
    expect(production.register[0]).toMatchObject({
      runId: "a",
      reportName: "Portfolio Performance",
      stateLabel: "Published"
    });
  });

  it("carries a run failure reason through as a blocking reason", () => {
    const { production } = buildReportingProductionSurfaceViewModel(
      [runRow({ status: "Failed", failureReason: "Pricing source unavailable." })],
      [templateRow()],
      [],
      NOW
    );

    expect(production.register[0].blockingReasons).toEqual(["Pricing source unavailable."]);
  });

  it("derives the reporting period from the latest confirmed as-of date", () => {
    const { production, period } = buildReportingProductionSurfaceViewModel(
      [
        runRow({ id: "old", templateId: "t1", asOfDateLabel: "2026-08-31" }),
        runRow({ id: "new", templateId: "t2", asOfDateLabel: "2026-09-30" })
      ],
      [templateRow()],
      [],
      NOW
    );

    expect(period?.periodId).toBe("2026-09-30");
    expect(production.asOfLabel).toBe("Sep 30, 2026");
  });

  it("leaves milestones the read model does not assert unset rather than inventing them", () => {
    const { period } = buildReportingProductionSurfaceViewModel([runRow()], [templateRow()], [], NOW);

    const byKey = Object.fromEntries((period?.milestones ?? []).map((milestone) => [milestone.key, milestone]));
    expect(byKey.periodEnd.date).toBe("2026-09-30");
    expect(byKey.accountingClose.date).toBeNull();
    expect(byKey.reportingCutoff.dateLabel).toBe("Not set");
    expect(byKey.publicationDue.dateLabel).toBe("Not set");
  });

  it("returns no period when no run carries a confirmed as-of date", () => {
    const { period } = buildReportingProductionSurfaceViewModel(
      [runRow({ asOfDateLabel: "As-of date unavailable" })],
      [templateRow()],
      [],
      NOW
    );

    expect(period).toBeNull();
  });

  it("reports the schedule count only when schedules exist", () => {
    const withSchedules = buildReportingProductionSurfaceViewModel(
      [runRow()],
      [templateRow()],
      [{ id: "sched-1" }, { id: "sched-2" }] as Parameters<typeof buildReportingProductionSurfaceViewModel>[2],
      NOW
    );
    const withoutSchedules = buildReportingProductionSurfaceViewModel([runRow()], [templateRow()], [], NOW);

    expect(withSchedules.production.lanes.find((lane) => lane.key === "Schedules")?.count).toBe(2);
    expect(withoutSchedules.production.lanes.find((lane) => lane.key === "Schedules")?.count).toBeNull();
  });

  it("handles an empty workspace without throwing", () => {
    const { production, period } = buildReportingProductionSurfaceViewModel([], [], [], NOW);

    expect(production.isEmpty).toBe(true);
    expect(production.asOfLabel).toBe("As-of unavailable");
    expect(period).toBeNull();
  });
});

describe("period scoping", () => {
  it("keeps only the latest period's runs in the register", () => {
    const { production, period } = buildReportingProductionSurfaceViewModel(
      [
        runRow({ id: "current", templateId: "t1", asOfDateLabel: "2026-09-30" }),
        runRow({ id: "prior", templateId: "t2", asOfDateLabel: "2026-08-31" }),
        runRow({ id: "older", templateId: "t3", asOfDateLabel: "2026-07-31" })
      ],
      [templateRow()],
      [],
      NOW
    );

    expect(production.register.map((row) => row.runId)).toEqual(["current"]);
    expect(period?.periodId).toBe("2026-09-30");
  });

  it("does not let a prior-period run displace the current run for the same template", () => {
    // isLatestGenerated is scoped per run series, so both can claim it.
    const { production } = buildReportingProductionSurfaceViewModel(
      [
        runRow({ id: "prior", templateId: "shared", asOfDateLabel: "2026-08-31", isLatestGenerated: true }),
        runRow({ id: "current", templateId: "shared", asOfDateLabel: "2026-09-30", isLatestGenerated: true })
      ],
      [templateRow({ id: "shared" })],
      [],
      NOW
    );

    expect(production.register).toHaveLength(1);
    expect(production.register[0].runId).toBe("current");
  });

  it("keeps runs that retained no period rather than hiding them", () => {
    const { production } = buildReportingProductionSurfaceViewModel(
      [
        runRow({ id: "current", templateId: "t1", asOfDateLabel: "2026-09-30" }),
        runRow({ id: "undated", templateId: "t2", asOfDateLabel: "As-of date unavailable" })
      ],
      [templateRow()],
      [],
      NOW
    );

    expect(production.register.map((row) => row.runId).sort()).toEqual(["current", "undated"]);
  });

  it("resolves a month period token to its period end for the milestone strip", () => {
    const { production, period } = buildReportingProductionSurfaceViewModel(
      [runRow({ asOfDateLabel: "2026-06" })],
      [templateRow()],
      [],
      NOW
    );

    expect(period?.periodId).toBe("2026-06-30");
    expect(production.asOfLabel).toBe("Jun 2026");
  });

  it("scopes on a non-calendar period token without claiming a milestone strip", () => {
    const { production, period } = buildReportingProductionSurfaceViewModel(
      [
        runRow({ id: "kept", templateId: "t1", asOfDateLabel: "2026-P03" }),
        runRow({ id: "other", templateId: "t2", asOfDateLabel: "2026-P02" })
      ],
      [templateRow()],
      [],
      NOW
    );

    expect(production.register.map((row) => row.runId)).toEqual(["kept"]);
    expect(production.asOfLabel).toBe("2026-P03");
    // No calendar period end, so no milestones are asserted.
    expect(period).toBeNull();
  });

  it("prefers an orderable period over a relative token", () => {
    const { period } = buildReportingProductionSurfaceViewModel(
      [
        runRow({ id: "relative", templateId: "t1", asOfDateLabel: "CurrentMonth" }),
        runRow({ id: "dated", templateId: "t2", asOfDateLabel: "2026-09-30" })
      ],
      [templateRow()],
      [],
      NOW
    );

    expect(period?.periodId).toBe("2026-09-30");
  });
});

describe("daily work reaches the surface", () => {
  it("projects the shared daily-work items into the attention rail", () => {
    const { production } = buildReportingProductionSurfaceViewModel(
      [runRow({ status: "Approved" })],
      [templateRow()],
      [],
      NOW,
      [
        {
          workItemId: "delivery-failure:board",
          kind: "delivery-failure",
          title: "Board portal package failed",
          tone: "danger",
          primaryActionHref: "/reporting/report-packs?recipient=board",
          evidenceGaps: ["Delivery rejection lacks retained portal proof."]
        }
      ]
    );

    const keys = production.attention.map((item) => item.key);
    expect(keys).toContain("blockedWork");
    expect(keys).toContain("evidenceGaps");
  });
});
