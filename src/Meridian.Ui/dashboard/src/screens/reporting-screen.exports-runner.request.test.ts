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
    restatementAuthorized: false,
    ...overrides
  };
}

describe("buildExportsReportRunRequest restatement authorization", () => {
  it("omits restatement fields for an ordinary run", () => {
    const request = buildExportsReportRunRequest(template(), draft());

    expect(request.allowRestatement).toBeUndefined();
    expect(request.retryReason).toBeNull();
  });

  it("carries the authorization flag and trimmed reason when a restatement is authorized", () => {
    const request = buildExportsReportRunRequest(
      template(),
      draft({ restatementAuthorized: true, retryReason: "  Q2 NAV correction  " })
    );

    expect(request.allowRestatement).toBe(true);
    expect(request.retryReason).toBe("Q2 NAV correction");
  });

  it("does not forward a stale reason when restatement is not authorized", () => {
    const request = buildExportsReportRunRequest(
      template(),
      draft({ restatementAuthorized: false, retryReason: "leftover reason" })
    );

    expect(request.allowRestatement).toBeUndefined();
    expect(request.retryReason).toBeNull();
  });
});
