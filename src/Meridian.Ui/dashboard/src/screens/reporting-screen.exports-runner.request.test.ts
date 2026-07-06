import { describe, expect, it } from "vitest";
import { buildExportsReportRunRequest } from "@/screens/reporting-screen";
import type { ExportsReportRunDraftState } from "@/screens/reporting-screen.exports-runner";
import type { ReportingTemplateRow } from "@/screens/reporting-screen.view-model";

function template(overrides: Partial<ReportingTemplateRow> = {}): ReportingTemplateRow {
  return {
    templateName: "investor-monthly-statement",
    hasWriterGrids: false,
    ...overrides
  } as ReportingTemplateRow;
}

function draft(overrides: Partial<ExportsReportRunDraftState> = {}): ExportsReportRunDraftState {
  return {
    templateRowId: "template:1",
    asOfDate: "2026-05-04",
    maxRetries: "0",
    requestedBy: "ops",
    datasetSourceId: "",
    retryReason: "",
    restatementTargetRunId: "",
    restatementTemplateId: "",
    restatementJobId: "",
    restatementAsOfDate: "",
    ...overrides
  };
}

describe("buildExportsReportRunRequest restatement authorization", () => {
  it("omits restatement fields for an ordinary run", () => {
    const request = buildExportsReportRunRequest(template(), draft());

    expect(request.allowRestatement).toBeUndefined();
    expect(request.jobId).toBeUndefined();
    expect(request.retryReason).toBeUndefined();
  });

  it("targets the released run's series and carries the trimmed reason when restating", () => {
    const request = buildExportsReportRunRequest(
      template({ templateName: "current-selection" }),
      draft({
        restatementTargetRunId: "adhoc-investor-20260504153000123-20260504",
        restatementTemplateId: "investor-monthly-statement",
        restatementJobId: "adhoc-investor-20260504153000123",
        restatementAsOfDate: "2026-05-04",
        retryReason: "  Q2 NAV correction  "
      })
    );

    expect(request.allowRestatement).toBe(true);
    // Reuses the target run's series identity, not the currently selected template/as-of.
    expect(request.jobId).toBe("adhoc-investor-20260504153000123");
    expect(request.asOfDate).toBe("2026-05-04");
    expect(request.templateId).toBe("investor-monthly-statement");
    expect(request.retryReason).toBe("Q2 NAV correction");
  });

  it("builds a restatement request without a current template selection", () => {
    const request = buildExportsReportRunRequest(
      null,
      draft({
        restatementTargetRunId: "series-a-20260504",
        restatementTemplateId: "series-a-template",
        restatementJobId: "series-a",
        restatementAsOfDate: "2026-05-04",
        retryReason: "correction"
      })
    );

    expect(request.allowRestatement).toBe(true);
    expect(request.jobId).toBe("series-a");
    expect(request.templateId).toBe("series-a-template");
  });

  it("throws for an ordinary run when no template is selected", () => {
    expect(() => buildExportsReportRunRequest(null, draft())).toThrow();
  });
});
