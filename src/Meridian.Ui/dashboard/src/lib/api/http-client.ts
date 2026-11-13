// Shared HTTP transport for the workstation API client: JSON/blob/form requests, mutation headers,
// error mapping, and development-fixture fallback. Re-exported publicly through @/lib/api.
import { ApiResponseFormatError, createApiErrorFromResponseBody } from "@/lib/api-errors";

export const developmentFixtureHeader = "x-meridian-dev-fixture";

export const developmentScenarioHeader = "x-meridian-scenario";

const csrfCookieName = "mdc-csrf";

const csrfHeaderName = "X-CSRF-Token";

export interface ApiRequestOptions {
  signal?: AbortSignal;
  /** Disable development fixtures for authoritative or security-sensitive read paths. */
  allowDevelopmentFallback?: boolean;
}

let developmentFixtureUsage = false;

export function resetDevelopmentFixtureUsage() {
  developmentFixtureUsage = false;
}

export function hasDevelopmentFixtureUsage() {
  return developmentFixtureUsage;
}

export async function getJson<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
  const response = await fetch(path, {
    signal: options.signal,
    headers: {
      Accept: "application/json"
    }
  });

  if (!response.ok) {
    const fixture = options.allowDevelopmentFallback === false || response.headers?.has?.(developmentScenarioHeader)
      ? undefined
      : await getDevelopmentFallback<T>(path, response.status);
    if (fixture !== undefined) {
      markDevelopmentFixtureUsage();
      return fixture;
    }

    throw await buildApiError(path, response);
  }

  if (response.headers?.get?.(developmentFixtureHeader) === "true") {
    markDevelopmentFixtureUsage();
  }

  return readJsonResponse<T>(path, response);
}

const developmentFallbackStatuses = new Set([404, 500, 502, 503, 504]);

async function getDevelopmentFallback<T>(path: string, status: number): Promise<T | undefined> {
  if (!import.meta.env.DEV || import.meta.env.VITE_MERIDIAN_DEV_MODE || !developmentFallbackStatuses.has(status)) {
    return undefined;
  }

  const { resolveDevFixture } = await import("@/lib/dev-fixtures");
  const fixture = resolveDevFixture<T>(path);
  if (fixture !== undefined) {
    // Keep this loud: without it a developer can work for hours against a broken endpoint
    // while fixtures silently stand in for real backend failures.
    console.warn(
      `[dev-fixture] Backend request failed with ${status} for ${path}; serving development fixture data instead. ` +
        "Responses on this screen do not reflect the live backend."
    );
  }

  return fixture;
}

export function markDevelopmentFixtureUsage() {
  developmentFixtureUsage = true;
}

export async function postJson<T>(path: string, body?: unknown, options: ApiRequestOptions = {}): Promise<T> {
  const response = await fetch(path, {
    method: "POST",
    signal: options.signal,
    headers: buildMutationHeaders(),
    body: body !== undefined ? JSON.stringify(body) : undefined
  });

  if (!response.ok) {
    throw await buildApiError(path, response);
  }

  return readJsonResponse<T>(path, response);
}

export async function postFormData<T>(path: string, formData: FormData, options: ApiRequestOptions = {}): Promise<T> {
  const response = await fetch(path, {
    method: "POST",
    signal: options.signal,
    headers: buildMultipartMutationHeaders(),
    body: formData
  });

  if (!response.ok) {
    throw await buildApiError(path, response);
  }

  return readJsonResponse<T>(path, response);
}

export function apiGetJson<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
  return getJson<T>(path, options);
}

/** Retain authoritative artifact bytes without JSON number conversion or fixture fallback. */
export async function apiGetBlob(path: string, options: ApiRequestOptions = {}): Promise<Blob> {
  const response = await fetch(path, { signal: options.signal, headers: { Accept: "application/json" } });
  if (!response.ok) {
    throw await buildApiError(path, response);
  }
  return response.blob();
}

export function apiPostJson<T>(path: string, body?: unknown, options: ApiRequestOptions = {}): Promise<T> {
  return postJson<T>(path, body, options);
}

export function apiPutJson<T>(path: string, body?: unknown, options: ApiRequestOptions = {}): Promise<T> {
  return putJson<T>(path, body, options);
}

export async function putJson<T>(path: string, body?: unknown, options: ApiRequestOptions = {}): Promise<T> {
  const response = await fetch(path, {
    method: "PUT",
    signal: options.signal,
    headers: buildMutationHeaders(),
    body: body !== undefined ? JSON.stringify(body) : undefined
  });

  if (!response.ok) {
    throw await buildApiError(path, response);
  }

  return readJsonResponse<T>(path, response);
}

export async function patchJson<T>(path: string, body?: unknown, options: ApiRequestOptions = {}): Promise<T> {
  const response = await fetch(path, {
    method: "PATCH",
    signal: options.signal,
    headers: buildMutationHeaders(),
    body: body !== undefined ? JSON.stringify(body) : undefined
  });

  if (!response.ok) {
    throw await buildApiError(path, response);
  }

  return readJsonResponse<T>(path, response);
}

export async function deleteJson<T>(path: string, options: ApiRequestOptions = {}, body?: unknown): Promise<T> {
  const response = await fetch(path, {
    method: "DELETE",
    signal: options.signal,
    headers: buildMutationHeaders(),
    body: body !== undefined ? JSON.stringify(body) : undefined
  });

  if (!response.ok) {
    throw await buildApiError(path, response);
  }

  return readJsonResponse<T>(path, response);
}

function buildMutationHeaders(): Record<string, string> {
  const headers: Record<string, string> = {
    Accept: "application/json",
    "Content-Type": "application/json"
  };

  const csrfToken = readCookie(csrfCookieName);
  if (csrfToken) {
    headers[csrfHeaderName] = csrfToken;
  }

  return headers;
}

function buildMultipartMutationHeaders(): Record<string, string> {
  const headers: Record<string, string> = {
    Accept: "application/json"
  };

  const csrfToken = readCookie(csrfCookieName);
  if (csrfToken) {
    headers[csrfHeaderName] = csrfToken;
  }

  return headers;
}

function readCookie(name: string): string | undefined {
  if (typeof document === "undefined" || !document.cookie) {
    return undefined;
  }

  const prefix = `${name}=`;
  const cookie = document.cookie
    .split(";")
    .map((part) => part.trim())
    .find((part) => part.startsWith(prefix));
  if (!cookie) {
    return undefined;
  }

  return decodeURIComponent(cookie.slice(prefix.length));
}

async function readJsonResponse<T>(path: string, response: Response): Promise<T> {
  if (response.status === 204 || response.status === 205) {
    return null as T;
  }

  const textFallback = typeof response.clone === "function" ? response.clone() : response;
  try {
    if (typeof response.json === "function") {
      return await response.json() as T;
    }
  } catch (error) {
    const text = await readResponseSuccessBody(textFallback);
    if (!text.trim()) {
      return null as T;
    }

    try {
      return JSON.parse(text) as T;
    } catch {
      // Deliberately reports the enclosing catch's `error` (the response.json() failure),
      // not the fallback JSON.parse failure — the original error is the actionable one.
      throw new ApiResponseFormatError(path, error);
    }
  }

  const text = await readResponseSuccessBody(response);
  if (!text.trim()) {
    return null as T;
  }

  try {
    return JSON.parse(text) as T;
  } catch (error) {
    throw new ApiResponseFormatError(path, error);
  }
}

async function readResponseSuccessBody(response: Response): Promise<string> {
  if (typeof response.text !== "function") {
    return "";
  }

  try {
    return await response.text();
  } catch {
    return "";
  }
}

async function buildApiError(path: string, response: Response) {
  return createApiErrorFromResponseBody(
    path,
    response.status,
    await readResponseErrorBody(response),
    readCorrelationId(response)
  );
}

/** The correlation identifier a response carries for support, when the host or a proxy set one. */
function readCorrelationId(response: Response): string | null {
  // Test doubles and some polyfills hand back a response without a Headers object.
  if (typeof response.headers?.get !== "function") {
    return null;
  }

  return response.headers.get("x-correlation-id") ?? response.headers.get("x-request-id");
}

async function readResponseErrorBody(response: Response): Promise<string> {
  try {
    return await response.text();
  } catch {
    return "";
  }
}

export async function getDevelopmentSearchFallback(query: string, take: number, activeOnly: boolean) {
  if (!import.meta.env.DEV || import.meta.env.VITE_MERIDIAN_DEV_MODE) {
    return undefined;
  }

  const { searchDevSecurityMasterEntries } = await import("@/lib/dev-fixtures");
  return searchDevSecurityMasterEntries(query, take, activeOnly);
}
