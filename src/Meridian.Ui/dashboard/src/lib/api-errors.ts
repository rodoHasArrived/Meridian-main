import { EXECUTION_API_ENDPOINTS } from "@/lib/workstation-endpoints";

export interface ApiValidationIssue {
  field: string;
  label: string;
  messages: string[];
}

export interface ApiErrorDisplay {
  summary: string;
  details: string[];
}

export class ApiError extends Error {
  readonly path: string;
  readonly status: number;
  readonly title: string | null;
  readonly detail: string | null;
  readonly validationIssues: ApiValidationIssue[];
  readonly responseBody: string | null;
  /** Server trace or correlation identifier, when the response carried one. Support quotes it; operators do not read it. */
  readonly correlationId: string | null;

  constructor(input: {
    path: string;
    status: number;
    title?: string | null;
    detail?: string | null;
    validationIssues?: ApiValidationIssue[];
    responseBody?: string | null;
    correlationId?: string | null;
  }) {
    const validationDetail = formatValidationIssues(input.validationIssues ?? []);
    const detail = input.detail?.trim() || null;
    // `message` is what most call sites render (`error instanceof Error ? error.message : ...`),
    // so it has to be operator copy: the reason the service gave, or a plain sentence for the
    // status. The request path and status stay on the error for the technical-details disclosure.
    super(buildOperatorMessage(input.status, detail, validationDetail));
    this.name = "ApiError";
    this.path = input.path;
    this.status = input.status;
    this.title = input.title?.trim() || null;
    this.detail = detail;
    this.validationIssues = input.validationIssues ?? [];
    this.responseBody = input.responseBody?.trim() || null;
    this.correlationId = input.correlationId?.trim() || null;
  }
}

/**
 * A successful response whose body could not be read as JSON. The message is operator copy; the
 * request path and the parser's complaint stay on the error for the technical-details disclosure.
 */
export class ApiResponseFormatError extends Error {
  readonly path: string;
  readonly parserDetail: string | null;

  constructor(path: string, cause: unknown) {
    super("Meridian returned a response the workstation could not read. Try again.", { cause });
    this.name = "ApiResponseFormatError";
    this.path = path;
    this.parserDetail = cause instanceof Error && cause.message.trim() ? cause.message.trim() : null;
  }
}

export function createApiErrorFromResponseBody(
  path: string,
  status: number,
  body: string,
  correlationId: string | null = null
): ApiError {
  const trimmed = body.trim();
  if (!trimmed) {
    return new ApiError({ path, status, correlationId });
  }

  try {
    const parsed = JSON.parse(trimmed) as unknown;
    if (isRecord(parsed)) {
      return new ApiError({
        path,
        status,
        title: readString(parsed.title),
        // `error` is the shape most workstation endpoints still return (`new { error = "..." }`)
        // rather than RFC-7807 problem details. Without it the operator sees only
        // the generic status sentence instead of the reason the server actually gave.
        detail:
          readString(parsed.detail) ??
          readString(parsed.message) ??
          readString(parsed.error) ??
          readString(parsed.title),
        validationIssues: parseValidationIssues(parsed.errors),
        responseBody: trimmed,
        // ASP.NET problem details carry `traceId`; a response header wins when the caller read one.
        correlationId: correlationId ?? readString(parsed.correlationId) ?? readString(parsed.traceId)
      });
    }
  } catch {
    // Plain-text error bodies are still useful.
  }

  return new ApiError({
    path,
    status,
    detail: trimmed,
    responseBody: trimmed,
    correlationId
  });
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

export function readOrderSubmissionRefusal(error: unknown): string | null {
  if (!isApiError(error) || error.path !== EXECUTION_API_ENDPOINTS.ordersSubmit) {
    return null;
  }

  // The submit route reserves these responses for validation, authorization, rate limits,
  // and confirmed rejections. Timeouts and unqualified server errors carry no such proof.
  const refusalStatus = [400, 401, 403, 404, 405, 413, 415, 422, 429].includes(error.status);
  if (!refusalStatus && error.status !== 503) {
    return null;
  }

  let body: { type?: unknown; success?: unknown; errorMessage?: unknown; requiresOrderReconciliation?: unknown } | null = null;
  try {
    body = error.responseBody ? JSON.parse(error.responseBody) : null;
  } catch {
    // A definitive response can have a plain-text or empty body.
  }
  const unavailableBeforeDispatch = error.status === 503
    && body?.type === "https://meridian.io/errors/execution-pre-dispatch-unavailable";
  if ((!refusalStatus && !unavailableBeforeDispatch) || body?.requiresOrderReconciliation === true) {
    return null;
  }

  return body?.success === false && typeof body.errorMessage === "string" && body.errorMessage.trim()
    ? body.errorMessage.trim()
    : error.message;
}

/**
 * True when a rejected request was aborted rather than genuinely failing.
 *
 * Callers use this to stay silent on teardown/navigation instead of showing the
 * operator an error banner. Both arms are required: `AbortController.abort()`
 * rejects with a `DOMException` in browsers, but the same abort surfaces as a
 * plain `Error` named "AbortError" under jsdom and some fetch polyfills, so a
 * `DOMException`-only check misreports those as real failures.
 *
 * Matches on `name` only — never on message text, which would swallow genuine
 * errors that merely mention aborting.
 */
export function isAbortError(error: unknown): boolean {
  return error instanceof DOMException
    ? error.name === "AbortError"
    : error instanceof Error && error.name === "AbortError";
}

/** Request facts for the collapsed "Technical details" disclosure. Never part of the default reading path. */
export interface ApiErrorTechnicalDetail {
  label: string;
  value: string;
}

export interface ApiErrorPresentation {
  /** One plain sentence: what did not happen. */
  summary: string;
  /** Supporting operator copy: validation messages and the service's own explanation. */
  details: string[];
  /** Whether repeating the same request can succeed without the operator changing anything first. */
  retryable: boolean;
  /** Status, request path and correlation identifier, for support. */
  technicalDetails: ApiErrorTechnicalDetail[];
}

/**
 * Turns a failed request into what an operator needs: a plain sentence, whether Retry is worth
 * offering, and the request facts kept apart for a collapsed technical-details disclosure.
 *
 * `fallback` names the task that failed ("The recurring queue could not be loaded.") and is used
 * when the service gave no usable reason of its own.
 */
export function presentApiError(error: unknown, fallback: string): ApiErrorPresentation {
  if (!isApiError(error)) {
    const display = describeApiError(error, fallback);
    const technicalDetails: ApiErrorTechnicalDetail[] = [];
    if (error instanceof ApiResponseFormatError) {
      technicalDetails.push({ label: "Request", value: error.path });
      if (error.parserDetail) {
        technicalDetails.push({ label: "Parser", value: error.parserDetail });
      }
    }
    return {
      summary: display.summary,
      details: display.details.filter((detail) => !isDiagnosticsPointer(detail)),
      // A non-HTTP failure is a dropped connection, a timeout or a malformed response: all transient.
      retryable: true,
      technicalDetails
    };
  }

  const rawBody = hasRawTechnicalBody(error);
  const summary = error.status === 401 || error.status === 403 || rawBody || !error.detail
    ? summarizeStatus(error.status, fallback)
    : error.detail;
  const details: string[] = [];
  if (!rawBody) {
    if (error.title && error.title !== summary && error.title !== error.detail) {
      details.push(error.title);
    }
    for (const issue of error.validationIssues) {
      for (const message of issue.messages) {
        details.push(`${issue.label}: ${message}`);
      }
    }
  }

  const technicalDetails: ApiErrorTechnicalDetail[] = [
    { label: "Status", value: String(error.status) },
    { label: "Request", value: error.path }
  ];
  if (error.correlationId) {
    technicalDetails.push({ label: "Correlation ID", value: error.correlationId });
  }

  return {
    summary,
    details,
    retryable: isRetryableStatus(error.status),
    technicalDetails
  };
}

/** A plain sentence for an HTTP status, for the cases where the service gave no usable reason. */
export function summarizeStatus(status: number, fallback: string): string {
  if (status === 401) {
    return "Session expired or Meridian sign-in is required.";
  }
  if (status === 403) {
    return "Permission denied for this Meridian role.";
  }
  if (status === 404) {
    return "The requested Meridian data is unavailable.";
  }
  if (status === 408 || status === 504) {
    return "Meridian took too long to respond. Try again.";
  }
  if (status === 409) {
    return "This record changed since it was loaded. Refresh and try again.";
  }
  if (status === 429) {
    return "Too many requests were sent at once. Wait a moment and try again.";
  }
  // A server fault or an unexplained rejection says nothing the task-specific fallback does not.
  return fallback;
}

/** Retrying helps when the service was busy, slow or briefly down; it does not fix a rejected request. */
function isRetryableStatus(status: number): boolean {
  return status === 408 || status === 429 || status >= 500;
}

function isDiagnosticsPointer(detail: string): boolean {
  return /open diagnostics/i.test(detail);
}

function buildOperatorMessage(status: number, detail: string | null, validationDetail: string): string {
  if (detail && !looksLikeRawTechnicalResponse(detail)) {
    return validationDetail ? `${detail} ${validationDetail}` : detail;
  }
  if (validationDetail) {
    return validationDetail;
  }
  return summarizeStatus(
    status,
    status >= 500 ? "Meridian could not complete this request. Try again." : "Meridian did not accept this request."
  );
}

export function describeApiError(error: unknown, fallback: string): ApiErrorDisplay {
  if (!isApiError(error)) {
    if (error instanceof Error && error.message.trim()) {
      if (looksLikeRawTechnicalResponse(error.message)) {
        return {
          summary: fallback,
          details: ["Open diagnostics for the technical response."]
        };
      }

      return {
        summary: error.message,
        details: []
      };
    }

    return {
      summary: fallback,
      details: []
    };
  }

  if (hasRawTechnicalBody(error)) {
    return {
      summary: summarizeTechnicalApiError(error, fallback),
      details: [buildDiagnosticsDetail(error.status)]
    };
  }

  const summary = summarizeApiError(error, fallback);
  const details: string[] = [buildDiagnosticsDetail(error.status)];

  if (error.title && error.detail && error.title !== error.detail) {
    details.push(error.title);
  } else if (error.title && error.title !== summary) {
    details.push(error.title);
  }

  if (error.detail && error.detail !== summary && error.detail !== error.title) {
    details.push(error.detail);
  }

  for (const issue of error.validationIssues) {
    for (const message of issue.messages) {
      details.push(`${issue.label}: ${message}`);
    }
  }

  if (details.length === 1 && error.responseBody && !looksLikeDuplicate(summary, error.responseBody)) {
    details.push(error.responseBody);
  }

  return {
    summary,
    details
  };
}

function buildDiagnosticsDetail(status: number): string {
  return `Meridian service returned ${status}. Open diagnostics for technical details.`;
}

function summarizeApiError(error: ApiError, fallback: string): string {
  if (error.status === 401) {
    return "Session expired or Meridian sign-in is required.";
  }

  if (error.status === 403) {
    return "Permission denied for this Meridian role.";
  }

  return error.detail ?? error.title ?? fallback;
}

function summarizeTechnicalApiError(error: ApiError, fallback: string): string {
  if (error.status === 404) {
    return "The requested Meridian data is unavailable.";
  }

  if (error.status >= 500) {
    return "Meridian could not load this data. Try again or open diagnostics.";
  }

  return fallback;
}

function hasRawTechnicalBody(error: ApiError): boolean {
  return looksLikeRawTechnicalResponse(error.responseBody)
    || looksLikeRawTechnicalResponse(error.detail)
    || looksLikeRawTechnicalResponse(error.title);
}

function looksLikeRawTechnicalResponse(value: string | null | undefined): boolean {
  if (!value) {
    return false;
  }

  const trimmed = value.trim();
  return /<!doctype\s+html/i.test(trimmed)
    || /<html(?:\s|>)/i.test(trimmed)
    || /\bfile not found\b/i.test(trimmed)
    || /^404(?:\s|$|:|-)/i.test(trimmed)
    || /\bhttp\s+error\s+404\b/i.test(trimmed);
}

function parseValidationIssues(value: unknown): ApiValidationIssue[] {
  if (!isRecord(value)) {
    return [];
  }

  return Object.entries(value)
    .map(([field, messages]) => ({
      field,
      label: field.trim() || "request",
      messages: normalizeValidationMessages(messages)
    }))
    .filter((issue) => issue.messages.length > 0);
}

function normalizeValidationMessages(value: unknown): string[] {
  if (Array.isArray(value)) {
    return value.map(readString).filter((message): message is string => message !== null);
  }

  const message = readString(value);
  return message ? [message] : [];
}

function formatValidationIssues(issues: ApiValidationIssue[]): string {
  return issues
    .flatMap((issue) => issue.messages.map((message) => `${issue.label}: ${message}`))
    .join("; ");
}

function looksLikeDuplicate(summary: string, body: string): boolean {
  return summary.trim() === body.trim();
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function readString(value: unknown): string | null {
  return typeof value === "string" && value.trim().length > 0 ? value.trim() : null;
}
