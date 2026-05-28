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

  constructor(input: {
    path: string;
    status: number;
    title?: string | null;
    detail?: string | null;
    validationIssues?: ApiValidationIssue[];
    responseBody?: string | null;
  }) {
    const validationDetail = formatValidationIssues(input.validationIssues ?? []);
    const detail = input.detail?.trim() || null;
    const message = `Request failed for ${input.path} (${input.status})${buildMessageSuffix(detail, validationDetail)}`;
    super(message);
    this.name = "ApiError";
    this.path = input.path;
    this.status = input.status;
    this.title = input.title?.trim() || null;
    this.detail = detail;
    this.validationIssues = input.validationIssues ?? [];
    this.responseBody = input.responseBody?.trim() || null;
  }
}

export function createApiErrorFromResponseBody(path: string, status: number, body: string): ApiError {
  const trimmed = body.trim();
  if (!trimmed) {
    return new ApiError({ path, status });
  }

  try {
    const parsed = JSON.parse(trimmed) as unknown;
    if (isRecord(parsed)) {
      return new ApiError({
        path,
        status,
        title: readString(parsed.title),
        detail: readString(parsed.detail) ?? readString(parsed.message) ?? readString(parsed.title),
        validationIssues: parseValidationIssues(parsed.errors),
        responseBody: trimmed
      });
    }
  } catch {
    // Plain-text error bodies are still useful.
  }

  return new ApiError({
    path,
    status,
    detail: trimmed,
    responseBody: trimmed
  });
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

export function describeApiError(error: unknown, fallback: string): ApiErrorDisplay {
  if (!isApiError(error)) {
    if (error instanceof Error && error.message.trim()) {
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

  const summary = summarizeApiError(error, fallback);
  const details: string[] = [`Endpoint returned ${error.status} for ${error.path}.`];

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

function summarizeApiError(error: ApiError, fallback: string): string {
  if (error.status === 401) {
    return "Session expired or Meridian sign-in is required.";
  }

  if (error.status === 403) {
    return "Permission denied for this Meridian role.";
  }

  return error.detail ?? error.title ?? fallback;
}

function buildMessageSuffix(detail: string | null, validationDetail: string): string {
  if (detail && validationDetail) {
    return ` - ${detail} ${validationDetail}`;
  }

  if (validationDetail) {
    return ` - ${validationDetail}`;
  }

  if (detail) {
    return ` - ${detail}`;
  }

  return "";
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
