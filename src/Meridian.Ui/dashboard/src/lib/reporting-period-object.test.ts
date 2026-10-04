import { describe, expect, it } from "vitest";
import {
  buildReportingPeriodModel,
  type ReportingPeriodInput
} from "@/lib/reporting-period-object";

function period(overrides: Partial<ReportingPeriodInput> = {}): ReportingPeriodInput {
  return {
    periodId: "2026-09",
    periodEnd: "2026-09-30",
    valuationDate: "2026-09-30",
    accountingClose: "2026-10-02",
    reportingCutoff: "2026-10-03",
    publicationDue: "2026-10-05",
    ...overrides
  };
}

describe("reporting period milestones", () => {
  it("orders the five governed milestones and labels them", () => {
    const model = buildReportingPeriodModel(period(), "2026-10-01");

    expect(model.milestones.map((milestone) => milestone.key)).toEqual([
      "periodEnd",
      "valuationDate",
      "accountingClose",
      "reportingCutoff",
      "publicationDue"
    ]);
    expect(model.milestones.map((milestone) => milestone.label)).toEqual([
      "Period end",
      "Valuation date",
      "Accounting close",
      "Reporting cutoff",
      "Publication"
    ]);
  });

  it("splits reached from upcoming milestones and counts the days between", () => {
    const model = buildReportingPeriodModel(period(), "2026-10-01");

    const byKey = Object.fromEntries(model.milestones.map((milestone) => [milestone.key, milestone]));
    expect(byKey.periodEnd.isReached).toBe(true);
    expect(byKey.periodEnd.daysRemaining).toBe(-1);
    expect(byKey.accountingClose.isUpcoming).toBe(true);
    expect(byKey.accountingClose.daysRemaining).toBe(1);
    expect(byKey.publicationDue.daysRemaining).toBe(4);
    expect(model.nextMilestone?.key).toBe("accountingClose");
  });

  it("defaults the valuation date to the period end when unset", () => {
    const model = buildReportingPeriodModel(
      period({ valuationDate: null }),
      "2026-10-01"
    );

    const valuation = model.milestones.find((milestone) => milestone.key === "valuationDate");
    expect(valuation?.date).toBe("2026-09-30");
  });

  it("reports unset milestones without inventing a date", () => {
    const model = buildReportingPeriodModel(
      period({ publicationDue: null }),
      "2026-10-01"
    );

    const publication = model.milestones.find((milestone) => milestone.key === "publicationDue");
    expect(publication?.date).toBeNull();
    expect(publication?.dateLabel).toBe("Not set");
    expect(publication?.daysRemaining).toBeNull();
  });
});

describe("reporting period status", () => {
  it("derives Scheduled before the period ends", () => {
    const model = buildReportingPeriodModel(period(), "2026-09-15");
    expect(model.status).toBe("Scheduled");
    expect(model.statusSeverity).toBe("info");
  });

  it("derives InProduction after period end but before cutoff", () => {
    const model = buildReportingPeriodModel(period(), "2026-10-01");
    expect(model.status).toBe("InProduction");
    expect(model.statusLabel).toBe("In production");
  });

  it("derives CutoffPassed once the reporting cutoff has passed", () => {
    const model = buildReportingPeriodModel(period(), "2026-10-04");
    expect(model.status).toBe("CutoffPassed");
    expect(model.statusSeverity).toBe("action");
  });

  it("derives Published once the publication date is reached", () => {
    const model = buildReportingPeriodModel(period(), "2026-10-06");
    expect(model.status).toBe("Published");
    expect(model.statusSeverity).toBe("ready");
  });

  it("honours a declared status over the derived one", () => {
    const model = buildReportingPeriodModel(period({ status: "Archived" }), "2026-10-01");
    expect(model.status).toBe("Archived");
  });

  it("escalates to Reopened even when the source declares Published", () => {
    const model = buildReportingPeriodModel(
      period({
        status: "Published",
        close: { periodLabel: "September 2026", status: "Reopened", closedAtUtc: "2026-10-02T18:42:00Z" }
      }),
      "2026-10-06"
    );

    expect(model.status).toBe("Reopened");
    expect(model.statusSeverity).toBe("blocked");
  });

  it("does not report overdue milestones once the period is published", () => {
    const model = buildReportingPeriodModel(period({ status: "Published" }), "2026-10-10");
    expect(model.overdueMilestones).toEqual([]);
  });

  it("flags passed production milestones as overdue while still in production", () => {
    const model = buildReportingPeriodModel(period(), "2026-10-04");
    expect(model.overdueMilestones.map((milestone) => milestone.key)).toEqual([
      "accountingClose",
      "reportingCutoff"
    ]);
  });
});

describe("reporting period reproducibility", () => {
  it("is reproducible only when every governed snapshot is bound", () => {
    const model = buildReportingPeriodModel(
      period({
        snapshots: {
          portfolioSnapshotId: "PORT-SEP26",
          benchmarkSnapshotId: "BM-SEP26",
          fxSnapshotId: "FX-SEP26",
          pricingCutoffUtc: "2026-09-30T21:00:00Z",
          ledgerVersion: "CLOSE-SEP26-v4"
        }
      }),
      "2026-10-01"
    );

    expect(model.isReproducible).toBe(true);
    expect(model.missingSnapshotLabels).toEqual([]);
  });

  it("names each unbound snapshot", () => {
    const model = buildReportingPeriodModel(
      period({ snapshots: { portfolioSnapshotId: "PORT-SEP26", ledgerVersion: "  " } }),
      "2026-10-01"
    );

    expect(model.isReproducible).toBe(false);
    expect(model.missingSnapshotLabels).toEqual([
      "Benchmark snapshot",
      "FX snapshot",
      "Pricing cutoff",
      "Ledger version"
    ]);
    expect(model.summaryLabel).toContain("4 snapshots unbound");
  });
});

describe("accounting close integration", () => {
  it("requires re-freeze review when the period was reopened", () => {
    const model = buildReportingPeriodModel(
      period({
        close: { periodLabel: "September 2026", status: "Closed", reopenedAtUtc: "2026-10-04T12:00:00Z" }
      }),
      "2026-10-05"
    );

    expect(model.requiresRefreezeReview).toBe(true);
    expect(model.refreezeReason).toContain("reopened");
  });

  it("requires re-freeze review when the ledger version moved after freeze", () => {
    const model = buildReportingPeriodModel(
      period({
        snapshots: { ledgerVersion: "CLOSE-SEP26-v3" },
        close: { periodLabel: "September 2026", status: "Closed", ledgerVersion: "CLOSE-SEP26-v4" }
      }),
      "2026-10-05"
    );

    expect(model.requiresRefreezeReview).toBe(true);
    expect(model.refreezeReason).toBe("Ledger moved from CLOSE-SEP26-v3 to CLOSE-SEP26-v4 after report freeze.");
  });

  it("requires re-freeze review when the close landed after the report freeze", () => {
    const model = buildReportingPeriodModel(
      period({
        frozenAtUtc: "2026-10-01T09:00:00Z",
        close: { periodLabel: "September 2026", status: "Closed", closedAtUtc: "2026-10-02T18:42:00Z" }
      }),
      "2026-10-05"
    );

    expect(model.requiresRefreezeReview).toBe(true);
    expect(model.refreezeReason).toContain("closed after reports were frozen");
  });

  it("stays quiet when the ledger version still matches the frozen basis", () => {
    const model = buildReportingPeriodModel(
      period({
        frozenAtUtc: "2026-10-03T09:00:00Z",
        snapshots: { ledgerVersion: "CLOSE-SEP26-v4" },
        close: {
          periodLabel: "September 2026",
          status: "Closed",
          ledgerVersion: "CLOSE-SEP26-v4",
          closedAtUtc: "2026-10-02T18:42:00Z"
        }
      }),
      "2026-10-05"
    );

    expect(model.requiresRefreezeReview).toBe(false);
    expect(model.refreezeReason).toBeNull();
    expect(model.closeStatusLabel).toBe("Closed");
  });

  it("reports an unlinked close rather than assuming one", () => {
    const model = buildReportingPeriodModel(period(), "2026-10-01");
    expect(model.close).toBeNull();
    expect(model.closeStatusLabel).toBe("Not linked");
    expect(model.requiresRefreezeReview).toBe(false);
  });
});
