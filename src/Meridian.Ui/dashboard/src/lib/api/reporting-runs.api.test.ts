import { createReportingRunsApi } from "@/lib/api/reporting-runs.api";
import { FUND_STRUCTURE_API_ENDPOINTS } from "@/lib/ui-api-routes.generated";
import type { ReportingRunRequest } from "@/types";

const request = { templateId: "monthly-investor-report" } as ReportingRunRequest;

describe("reporting runs api", () => {
  it("posts governed run requests to the reporting run endpoint", async () => {
    const postJson = vi.fn().mockResolvedValue({ runId: "run-1" });
    const api = createReportingRunsApi(postJson);

    await api.runReportingNow(request);

    expect(postJson).toHaveBeenCalledWith(FUND_STRUCTURE_API_ENDPOINTS.reportingRuns, request, {});
  });

  it("posts readiness requests with the caller's abort signal", async () => {
    const postJson = vi.fn().mockResolvedValue({ ready: true });
    const api = createReportingRunsApi(postJson);
    const controller = new AbortController();

    await api.assessReportingRunReadiness(request, { signal: controller.signal });

    expect(postJson).toHaveBeenCalledWith(
      FUND_STRUCTURE_API_ENDPOINTS.reportingRunReadiness,
      request,
      { signal: controller.signal }
    );
  });
});
