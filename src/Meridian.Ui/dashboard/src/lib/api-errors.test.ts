import { describe, expect, it } from "vitest";
import {
  ApiResponseFormatError,
  createApiErrorFromResponseBody,
  describeApiError,
  isAbortError,
  isApiError,
  presentApiError
} from "@/lib/api-errors";

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
    // The message is what most call sites render, so it carries the reason and never the route.
    expect(error.message).toBe(
      "One or more validation errors occurred. profileId: Profile is required.; approvalReason: Approval reason must cite packet evidence."
    );
    expect(error.path).toBe("/api/export/analysis");
    expect(error.status).toBe(400);
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

describe("operator-facing error messages", () => {
  it("never puts the request path or status code in the message", () => {
    const cases = [
      createApiErrorFromResponseBody("/api/ledger/journal-automation/recurring/occurrences?fundProfileId=default-fund", 500, ""),
      createApiErrorFromResponseBody("/api/risk/escalations", 404, ""),
      createApiErrorFromResponseBody("/api/workstation/session", 401, ""),
      createApiErrorFromResponseBody("/api/execution/orders/submit", 400, ""),
      createApiErrorFromResponseBody("/api/workstation/reporting", 404, "<!DOCTYPE html><html><body>File not found</body></html>")
    ];

    for (const error of cases) {
      expect(error.message).not.toMatch(/\/api\//);
      expect(error.message).not.toMatch(/\(\d{3}\)/);
      expect(error.message).not.toMatch(/<html|doctype/i);
      expect(error.message.length).toBeGreaterThan(10);
    }
    expect(cases[0].message).toBe("Meridian could not complete this request. Try again.");
    expect(cases[1].message).toBe("The requested Meridian data is unavailable.");
    expect(cases[3].message).toBe("Meridian did not accept this request.");
  });

  it("keeps the reason the service gave as the message", () => {
    expect(createApiErrorFromResponseBody("/api/promotion/evaluate/run-1", 409, JSON.stringify({ error: "Promotion gate still has open blockers." })).message)
      .toBe("Promotion gate still has open blockers.");
  });
});

describe("presentApiError", () => {
  it("separates the plain sentence from the request facts and offers retry for a server fault", () => {
    const error = createApiErrorFromResponseBody(
      "/api/ledger/journal-automation/recurring/occurrences?fundProfileId=default-fund",
      503,
      "",
      "corr-123"
    );

    expect(presentApiError(error, "The recurring queue could not be loaded.")).toEqual({
      summary: "The recurring queue could not be loaded.",
      details: [],
      retryable: true,
      technicalDetails: [
        { label: "Status", value: "503" },
        { label: "Request", value: "/api/ledger/journal-automation/recurring/occurrences?fundProfileId=default-fund" },
        { label: "Correlation ID", value: "corr-123" }
      ]
    });
  });

  it("does not offer retry for a request the service rejected, and lists what to fix", () => {
    const presentation = presentApiError(
      createApiErrorFromResponseBody("/api/export/analysis", 422, JSON.stringify({
        title: "Validation failed",
        detail: "One or more validation errors occurred.",
        traceId: "00-abc-def-00",
        errors: { profileId: ["Profile is required."] }
      })),
      "Export failed."
    );

    expect(presentation.summary).toBe("One or more validation errors occurred.");
    expect(presentation.details).toEqual(["Validation failed", "profileId: Profile is required."]);
    expect(presentation.retryable).toBe(false);
    expect(presentation.technicalDetails).toContainEqual({ label: "Correlation ID", value: "00-abc-def-00" });
  });

  it.each([
    [401, "Session expired or Meridian sign-in is required.", false],
    [403, "Permission denied for this Meridian role.", false],
    [404, "The requested Meridian data is unavailable.", false],
    [409, "This record changed since it was loaded. Refresh and try again.", false],
    [429, "Too many requests were sent at once. Wait a moment and try again.", true],
    [504, "Meridian took too long to respond. Try again.", true]
  ])("maps a bare %i to a plain sentence", (status, summary, retryable) => {
    const presentation = presentApiError(createApiErrorFromResponseBody("/api/x", status, ""), "The data could not be loaded.");
    expect(presentation.summary).toBe(summary);
    expect(presentation.retryable).toBe(retryable);
  });

  it("hides a raw HTML body behind the fallback", () => {
    const presentation = presentApiError(
      createApiErrorFromResponseBody("/api/workstation/reporting", 502, "<html><body>Bad gateway</body></html>"),
      "Reporting could not be loaded."
    );
    expect(presentation.summary).toBe("Reporting could not be loaded.");
    expect(presentation.details).toEqual([]);
  });

  it("presents an unreadable response and a dropped connection as retryable", () => {
    const unreadable = presentApiError(
      new ApiResponseFormatError("/api/workstation/data", new SyntaxError("Unexpected token < in JSON")),
      "Data could not be loaded."
    );
    expect(unreadable.summary).toBe("Meridian returned a response the workstation could not read. Try again.");
    expect(unreadable.retryable).toBe(true);
    expect(unreadable.technicalDetails).toEqual([
      { label: "Request", value: "/api/workstation/data" },
      { label: "Parser", value: "Unexpected token < in JSON" }
    ]);

    expect(presentApiError(new TypeError("Failed to fetch"), "Data could not be loaded.")).toEqual({
      summary: "Failed to fetch",
      details: [],
      retryable: true,
      technicalDetails: []
    });
    expect(presentApiError(null, "Data could not be loaded.").summary).toBe("Data could not be loaded.");
  });
});
