import {
  redactReportingCredentialText,
  safeReportingHref
} from "@/lib/reporting-link-safety";

describe("reporting link safety", () => {
  it.each([
    "/portal/reporting/package?token=secret",
    "/portal/reporting/package?access_token=secret",
    "https://reports.example.test/package?signature=secret",
    "/portal/reporting/package#access_token=secret",
    "https://operator:secret@reports.example.test/package",
    "javascript:alert(1)"
  ])("rejects unsafe reporting href %s", (href) => {
    expect(safeReportingHref(href)).toBeNull();
  });

  it("accepts operator routes and requires recipient bearers in a fragment", () => {
    expect(safeReportingHref("/api/reporting/artifact?format=xlsx")).toBe(
      "/api/reporting/artifact?format=xlsx"
    );
    expect(safeReportingHref(
      "/portal/reporting/access-grants/grant-1/exchange#token=opaque",
      { requireOpaqueFragment: true }
    )).toBe("/portal/reporting/access-grants/grant-1/exchange#token=opaque");
    expect(safeReportingHref(
      "/portal/reporting/access-grants/grant-1/exchange?token=opaque",
      { requireOpaqueFragment: true }
    )).toBeNull();
    expect(safeReportingHref(
      "https://attacker.example/exchange#token=opaque",
      { requireOpaqueFragment: true }
    )).toBeNull();
    expect(safeReportingHref(
      "//attacker.example/exchange#token=opaque",
      { requireOpaqueFragment: true }
    )).toBeNull();
    expect(safeReportingHref(
      "/portal/reporting/access-grants/grant-1/exchange#token=opaque&artifact=artifact-1",
      { requireOpaqueFragment: true }
    )).toBeNull();
  });

  it("redacts legacy query credentials from retained notification text", () => {
    expect(redactReportingCredentialText(
      "Open /portal/package?token=secret-value&format=pdf"
    )).toBe("Open [reporting credential URL suppressed]");
    expect(redactReportingCredentialText(
      "Open /portal/package?access_token=secret-value"
    )).toBe("Open [reporting credential URL suppressed]");
    expect(redactReportingCredentialText(
      "Open /portal/package#token=secret-value"
    )).toBe("Open [reporting credential URL suppressed]");
  });
});
