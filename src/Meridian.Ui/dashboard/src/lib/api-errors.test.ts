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
});
