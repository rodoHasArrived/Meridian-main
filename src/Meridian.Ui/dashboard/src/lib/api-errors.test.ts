import { describe, expect, it } from "vitest";
import { createApiErrorFromResponseBody, describeApiError, isApiError } from "@/lib/api-errors";

describe("api-errors", () => {
  it("parses validation errors into structured operator-facing details", () => {
    const error = createApiErrorFromResponseBody(
      "/api/export/analysis",
      400,
      JSON.stringify({
        title: "Validation failed",
        detail: "One or more validation errors occurred.",
        errors: {
          profileId: ["Profile is required."],
          approvalReason: ["Approval reason must cite packet evidence."]
        }
      })
    );

    expect(isApiError(error)).toBe(true);
    expect(error.message).toBe(
      "Request failed for /api/export/analysis (400) - One or more validation errors occurred. profileId: Profile is required.; approvalReason: Approval reason must cite packet evidence."
    );
    expect(error.validationIssues).toEqual([
      {
        field: "profileId",
        label: "profileId",
        messages: ["Profile is required."]
      },
      {
        field: "approvalReason",
        label: "approvalReason",
        messages: ["Approval reason must cite packet evidence."]
      }
    ]);

    expect(describeApiError(error, "Export failed.")).toEqual({
      summary: "One or more validation errors occurred.",
      details: [
        "Endpoint returned 400 for /api/export/analysis.",
        "Validation failed",
        "profileId: Profile is required.",
        "approvalReason: Approval reason must cite packet evidence."
      ]
    });
  });

  it("keeps plain-text backend errors visible", () => {
    const error = createApiErrorFromResponseBody("/api/export/analysis", 503, "Export worker unavailable");

    expect(describeApiError(error, "Export failed.")).toEqual({
      summary: "Export worker unavailable",
      details: ["Endpoint returned 503 for /api/export/analysis."]
    });
  });

  it("maps unauthenticated responses to a session recovery summary", () => {
    const error = createApiErrorFromResponseBody(
      "/api/workstation/session",
      401,
      JSON.stringify({
        title: "Unauthorized",
        detail: "The workstation session token expired."
      })
    );

    expect(describeApiError(error, "Session request failed.")).toEqual({
      summary: "Session expired or Meridian sign-in is required.",
      details: [
        "Endpoint returned 401 for /api/workstation/session.",
        "Unauthorized",
        "The workstation session token expired."
      ]
    });
  });

  it("maps forbidden responses to a role permission summary", () => {
    const error = createApiErrorFromResponseBody(
      "/api/workstation/trading",
      403,
      JSON.stringify({
        title: "Forbidden",
        detail: "The active role cannot read trading readiness."
      })
    );

    expect(describeApiError(error, "Trading request failed.")).toEqual({
      summary: "Permission denied for this Meridian role.",
      details: [
        "Endpoint returned 403 for /api/workstation/trading.",
        "Forbidden",
        "The active role cannot read trading readiness."
      ]
    });
  });
});
