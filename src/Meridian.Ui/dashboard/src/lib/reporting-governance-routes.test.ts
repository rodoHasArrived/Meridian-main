import {
  governedReportingRestatementApprovalPath,
  governedReportingRestatementRequestPath,
  governedReportingRunPath,
  governedReportingSeriesHistoryPath,
  governedReportingTransitionPath,
  secureReportingAccessGrantHistoryPath,
  secureReportingAccessGrantIssuePath,
  secureReportingAccessGrantRevokePath,
  secureReportingArtifactDownloadPath,
  secureReportingDeliveryHistoryPath,
  secureReportingDeliveryPath,
  secureReportingDeliveryQueuePath,
  secureReportingTransportCapabilitiesPath
} from "@/lib/reporting-governance-routes";

describe("reporting governance routes", () => {
  it("encodes canonical governed run and mutation identities", () => {
    expect(governedReportingRunPath("run / one")).toBe("/api/fund-structure/reporting/runs/run%20%2F%20one");
    expect(governedReportingTransitionPath("run-1", "validate")).toBe(
      "/api/fund-structure/reporting/runs/run-1/validate"
    );
    expect(governedReportingRestatementRequestPath("run-1")).toBe(
      "/api/fund-structure/reporting/runs/run-1/restatement-requests"
    );
    expect(governedReportingRestatementApprovalPath("request / one")).toBe(
      "/api/fund-structure/reporting/runs/restatement-requests/request%20%2F%20one/approve"
    );
    expect(governedReportingSeriesHistoryPath("series / one")).toBe(
      "/api/fund-structure/reporting/runs/series/series%20%2F%20one"
    );
  });

  it("builds secure distribution routes without placing credentials in a URL", () => {
    expect(secureReportingDeliveryQueuePath()).toBe("/api/fund-structure/reporting/distribution/deliveries");
    expect(secureReportingDeliveryPath("job / one")).toBe(
      "/api/fund-structure/reporting/distribution/deliveries/job%20%2F%20one"
    );
    expect(secureReportingDeliveryHistoryPath("run / one")).toBe(
      "/api/fund-structure/reporting/distribution/packages/run%20%2F%20one/deliveries"
    );
    expect(secureReportingArtifactDownloadPath("run / one", "artifact / one")).toBe(
      "/api/fund-structure/reporting/distribution/packages/run%20%2F%20one/artifacts/artifact%20%2F%20one"
    );
    expect(secureReportingAccessGrantIssuePath()).toBe(
      "/api/fund-structure/reporting/distribution/access-grants"
    );
    expect(secureReportingAccessGrantRevokePath("grant / one")).toBe(
      "/api/fund-structure/reporting/distribution/access-grants/grant%20%2F%20one/revoke"
    );
    expect(secureReportingAccessGrantHistoryPath("run / one")).toBe(
      "/api/fund-structure/reporting/distribution/packages/run%20%2F%20one/access-grants"
    );
    expect(secureReportingTransportCapabilitiesPath()).toBe(
      "/api/fund-structure/reporting/distribution/transports"
    );
  });
});
