import { describe, expect, it } from "vitest";
import { createApiErrorFromResponseBody, describeApiError, isAbortError, isApiError } from "@/lib/api-errors";

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
        "Meridian service returned 400. Open diagnostics for technical details.",
        "Validation failed",
        "profileId: Profile is required.",
        "approvalReason: Approval reason must cite packet evidence."
      ]
    });
  });

  it("keeps plain-text service errors visible without exposing route paths", () => {
    const error = createApiErrorFromResponseBody("/api/export/analysis", 503, "Export worker unavailable");

    expect(describeApiError(error, "Export failed.")).toEqual({
      summary: "Export worker unavailable",
      details: ["Meridian service returned 503. Open diagnostics for technical details."]
    });
  });

  it("translates raw HTML 404 responses into operator recovery copy", () => {
    const error = createApiErrorFromResponseBody(
      "/api/workstation/reporting",
      404,
      "<!DOCTYPE HTML><html><body><h1>404</h1><p>File not found</p></body></html>"
    );

    expect(describeApiError(error, "Workspace data unavailable.")).toEqual({
      summary: "The requested Meridian data is unavailable.",
      details: ["Meridian service returned 404. Open diagnostics for technical details."]
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
        "Meridian service returned 401. Open diagnostics for technical details.",
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
        "Meridian service returned 403. Open diagnostics for technical details.",
        "Forbidden",
        "The active role cannot read trading readiness."
      ]
    });
  });
});

describe("isAbortError", () => {
  it("recognises the DOMException form browsers reject with", () => {
    expect(isAbortError(new DOMException("The operation was aborted.", "AbortError"))).toBe(true);
  });

  it("recognises the plain Error form jsdom and fetch polyfills reject with", () => {
    const error = new Error("The operation was aborted.");
    error.name = "AbortError";
    expect(isAbortError(error)).toBe(true);
  });

  it("recognises a real AbortController signal rejection", () => {
    const controller = new AbortController();
    controller.abort();

    expect(isAbortError(controller.signal.reason)).toBe(true);
  });

  it("does not treat genuine failures as aborts merely because they mention aborting", () => {
    expect(isAbortError(new Error("Upload aborted by the remote host"))).toBe(false);
    expect(isAbortError(new DOMException("Aborted the transaction", "InvalidStateError"))).toBe(false);
  });

  it("returns false for non-error values", () => {
    expect(isAbortError(null)).toBe(false);
    expect(isAbortError(undefined)).toBe(false);
    expect(isAbortError("AbortError")).toBe(false);
    expect(isAbortError({ name: "AbortError" })).toBe(false);
  });

  it("surfaces the `error` field most workstation endpoints actually return", () => {
    // 395 endpoints return `new { error = "..." }` rather than RFC-7807 problem details. Without
    // this fallback the operator sees only "Request failed (400)" instead of the server's reason.
    const error = createApiErrorFromResponseBody("/api/historical/bars", 400, JSON.stringify({ error: "Symbol is required." }));

    expect(error.detail).toBe("Symbol is required.");
    expect(error.message).toContain("Symbol is required.");
  });

  it("prefers RFC-7807 detail over the legacy error field when both are present", () => {
    const error = createApiErrorFromResponseBody(
      "/api/historical/bars",
      400,
      JSON.stringify({ detail: "Detailed problem statement.", error: "legacy" })
    );

    expect(error.detail).toBe("Detailed problem statement.");
  });
});
