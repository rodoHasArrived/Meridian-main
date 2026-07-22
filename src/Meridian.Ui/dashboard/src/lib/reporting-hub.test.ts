import { describe, expect, it } from "vitest";
import {
  buildReportingHubModel,
  type ReportingHubRunInput,
  type ReportingHubTemplateInput
} from "@/lib/reporting-hub";

function run(overrides: Partial<ReportingHubRunInput>): ReportingHubRunInput {
  return {
    templateId: "tmpl",
    family: "Performance",
    status: "Released",
    asOfDateLabel: "2026-06-30",
    runIdLabel: "run-1",
    drilldownLinks: [],
    ...overrides
  };
}

function template(overrides: Partial<ReportingHubTemplateInput>): ReportingHubTemplateInput {
  return {
    templateName: "perf",
    name: "Performance Report",
    family: "Performance",
    canRunOnDemand: true,
    ...overrides
  };
}

describe("reporting hub model", () => {
  it("groups runs by family, picks the most recent run, and surfaces approved as-of", () => {
    const model = buildReportingHubModel(
      [
        run({ family: "Performance", status: "Draft", asOfDateLabel: "2026-06-30", runIdLabel: "perf-jun" }),
        run({ family: "Performance", status: "Released", asOfDateLabel: "2026-05-31", runIdLabel: "perf-may" }),
        run({ family: "Holdings", status: "Approved", asOfDateLabel: "2026-06-30", runIdLabel: "hold-jun" })
      ],
      [
        template({ family: "Performance", templateName: "perf" }),
        template({ family: "Holdings", templateName: "holdings", name: "Holdings Report" })
      ]
    );

    expect(model.cards.map((card) => card.family)).toEqual(["Holdings", "Performance"]);

    const performance = model.cards.find((card) => card.family === "Performance")!;
    // Latest run is the June draft; readiness reflects that, but the approved line points at May.
    expect(performance.latestRunId).toBe("perf-jun");
    expect(performance.readiness).toBe("Draft");
    expect(performance.needsAttention).toBe(true);
    expect(performance.approvedAsOfLabel).toBe("Approved as of May 31, 2026");
    expect(performance.runCount).toBe(2);

    const holdings = model.cards.find((card) => card.family === "Holdings")!;
    expect(holdings.readiness).toBe("Approved");
    expect(holdings.isCurrent).toBe(true);
    expect(holdings.approvedAsOfLabel).toBe("Approved as of Jun 30, 2026");
  });

  it("resolves the open link from the latest run's first browser-navigable drilldown", () => {
    const model = buildReportingHubModel(
      [
        run({
          family: "Performance",
          status: "Released",
          asOfDateLabel: "2026-06-30",
          drilldownLinks: [
            { href: "/api/raw", label: "Manifest", isBrowserNavigable: false },
            { href: "/reporting/runs/perf-jun", label: "Run", isBrowserNavigable: true }
          ]
        })
      ],
      [template({ family: "Performance" })]
    );

    const performance = model.cards[0];
    expect(performance.openHref).toBe("/reporting/runs/perf-jun");
    expect(performance.openLabel).toBe("Open latest output");
  });

  it("prefers explicit latest generated and approved rerun markers over same-date sorting", () => {
    const model = buildReportingHubModel(
      [
        run({ family: "Performance", status: "Approved", runIdLabel: "perf-20260630", isLatestApproved: true, runAttemptOrdinal: 1 }),
        run({ family: "Performance", status: "Draft", runIdLabel: "perf-20260630-v2", isLatestGenerated: true, runAttemptOrdinal: 2 })
      ],
      [template({ family: "Performance" })]
    );

    const performance = model.cards[0];
    expect(performance.latestRunId).toBe("perf-20260630-v2");
    expect(performance.approvedAsOfLabel).toBe("Approved as of Jun 30, 2026");
    expect(performance.readiness).toBe("Draft");
  });

  it("represents families that have templates but no runs", () => {
    const model = buildReportingHubModel([], [template({ family: "Audit", templateName: "audit-pack" })]);

    const audit = model.cards[0];
    expect(audit.readiness).toBe("NoRuns");
    expect(audit.runCount).toBe(0);
    expect(audit.openHref).toBeNull();
    expect(audit.nextActionHref).toBe("/reporting/run?templateId=audit-pack");
    expect(audit.nextActionLabel).toBe("Run Audit");
    expect(audit.latestAsOfLabel).toBe("—");
    expect(audit.approvedAsOfLabel).toBe("No approved output yet");
    expect(audit.needsAttention).toBe(true);
  });

  it("humanizes backend family keys and gives every family one next action", () => {
    const model = buildReportingHubModel([], [template({
      id: "investor-monthly:1.0",
      family: "InvestorStatement",
      templateName: "investor-monthly"
    })]);

    expect(model.cards[0]).toMatchObject({
      familyKey: "InvestorStatement",
      family: "Investor Statement",
      nextActionLabel: "Run Investor Statement",
      nextActionHref: "/reporting/run?templateId=investor-monthly%3A1.0"
    });
    expect(model.dailyWorkSummaryLabel).toBe("No urgent work queued · 1 report family needs setup or review");
  });

  it("routes non-runnable families to setup instead of inferring run authority", () => {
    const model = buildReportingHubModel([], [template({
      family: "CustomReport",
      templateName: "draft-custom",
      canRunOnDemand: false
    })]);

    expect(model.cards[0]).toMatchObject({
      family: "Custom Report",
      nextActionLabel: "Set up Custom Report",
      nextActionHref: "/reporting/report-builder?family=CustomReport"
    });
  });

  it("labels a failed run as review work even when it has a navigable detail link", () => {
    const model = buildReportingHubModel([
      run({
        status: "Failed",
        drilldownLinks: [{ href: "/reporting/runs/perf-failed", label: "Run", isBrowserNavigable: true }]
      })
    ], [template({})]);

    expect(model.cards[0]).toMatchObject({
      nextActionLabel: "Review latest run",
      nextActionHref: "/reporting/runs/perf-failed"
    });
  });

  it.each([
    ["Published", "Released", "Published"],
    ["AwaitingApproval", "InReview", "Awaiting approval"]
  ] as const)("presents backend %s runs without falling back to no-runs", (status, readiness, statusLabel) => {
    const model = buildReportingHubModel([run({ status })], [template({})]);

    expect(model.cards[0]).toMatchObject({ runCount: 1, readiness, statusLabel });
    expect(model.cards[0]?.statusLabel).not.toBe("No runs yet");
  });

  it("requires period confirmation instead of presenting a published run as current without an as-of date", () => {
    const model = buildReportingHubModel([
      run({
        family: "InvestorStatement",
        status: "Published",
        asOfDateLabel: "As-of date unavailable",
        runIdLabel: "investor-monthly-missing-period"
      })
    ], [template({ family: "InvestorStatement", templateName: "investor-monthly" })]);

    expect(model.cards[0]).toMatchObject({
      family: "Investor Statement",
      readiness: "InReview",
      statusLabel: "Period confirmation required",
      approvedAsOfLabel: "Approved output needs period confirmation",
      latestAsOfLabel: "No period confirmed",
      isCurrent: false,
      needsAttention: true,
      nextActionLabel: "Review latest run"
    });
    expect(model.currentCount).toBe(0);
    expect(model.attentionCount).toBe(1);
  });

  it("keeps a dated retained run ahead of a missing-period run in the same family", () => {
    const model = buildReportingHubModel([
      run({
        family: "InvestorStatement",
        status: "Published",
        asOfDateLabel: "As-of date unavailable",
        runIdLabel: "investor-monthly-missing-period",
        isLatestGenerated: true
      }),
      run({
        family: "InvestorStatement",
        status: "Released",
        asOfDateLabel: "2026-06-30",
        runIdLabel: "investor-monthly-june"
      })
    ], [template({ family: "InvestorStatement", templateName: "investor-monthly" })]);

    expect(model.cards[0]).toMatchObject({
      latestRunId: "investor-monthly-june",
      readiness: "Released",
      latestAsOfLabel: "Jun 30, 2026",
      needsAttention: false
    });
  });

  it("summarizes current vs. attention counts", () => {
    const model = buildReportingHubModel(
      [
        run({ family: "Performance", status: "Released" }),
        run({ family: "Holdings", status: "Failed" })
      ],
      [
        template({ family: "Performance" }),
        template({ family: "Holdings", templateName: "holdings" })
      ]
    );

    expect(model.totalFamilies).toBe(2);
    expect(model.currentCount).toBe(1);
    expect(model.attentionCount).toBe(1);
    expect(model.summaryLabel).toBe("1 of 2 families current · 1 need attention");
  });

  it("surfaces daily reporting work ahead of family launch cards", () => {
    const model = buildReportingHubModel(
      [run({ family: "Performance", status: "Released" })],
      [template({ family: "Performance" })],
      [
        {
          workItemId: "delivery-failure:1",
          kind: "delivery-failure",
          title: "Delivery failure: Board portal",
          statusLabel: "Failed",
          detail: "Secure portal package rejected.",
          tone: "danger",
          owner: "fund-controller",
          dueAtUtc: "2026-06-30T15:00:00Z",
          primaryActionLabel: "Review delivery",
          primaryActionHref: "/reporting?deliveryAttempt=1",
          evidenceGaps: ["Delivery failure has no retained evidence link."],
          context: ["board", "Secure portal"]
        }
      ]
    );

    expect(model.isEmpty).toBe(false);
    expect(model.dailyWorkSummaryLabel).toBe("1 daily item · 1 blocked · 0 need review");
    expect(model.dailyWork[0]).toMatchObject({
      kindLabel: "Delivery failure",
      badgeVariant: "danger",
      blockedLabel: "Failed",
      owner: "fund-controller",
      affectedOutputLabel: "board",
      nextActionLabel: "Review delivery",
      proofLabel: "1 evidence gap",
      dueLabel: "Due Jun 30, 2026",
      evidenceGaps: ["Delivery failure has no retained evidence link."],
      primaryActionHref: "/reporting?deliveryAttempt=1"
    });
    expect(model.cards[0]?.family).toBe("Performance");
  });

  it("is empty when nothing is configured", () => {
    const model = buildReportingHubModel([], []);
    expect(model.isEmpty).toBe(true);
    expect(model.summaryLabel).toBe("No report families are configured yet.");
  });
});
