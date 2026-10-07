import { resolveDevFixture } from "../lib/dev-fixtures";
import { cloneFixture } from "../lib/dev-fixtures/fixture-resolver";
import { accountingScenarios } from "./accounting";
import type { ApiScenario, ScenarioResponse } from "./types";

export type { ApiScenario, ScenarioResponse, ScenarioRoute, ScenarioProblem } from "./types";
export { accountingPayload } from "./accounting";

export const scenarios = accountingScenarios;
export type ScenarioId = keyof typeof scenarios;
export const scenarioHeader = "x-meridian-scenario";

export function getScenario(id: string): ApiScenario {
  if (!Object.prototype.hasOwnProperty.call(scenarios, id)) {
    throw new Error(`Unknown Meridian scenario "${id}". Available scenarios: ${Object.keys(scenarios).join(", ")}.`);
  }
  return scenarios[id as ScenarioId];
}

export function scenarioPreviewUrl(scenario: ApiScenario): string {
  const url = new URL(`/workstation${scenario.previewPath}`, "http://meridian.local");
  url.searchParams.set("scenario", scenario.id);
  return `${url.pathname}${url.search}`;
}

/** Explicit overrides first; known read-only development fixtures supply shell dependencies. */
export function resolveScenarioResponse(scenario: ApiScenario, input: string, method = "GET"): ScenarioResponse | undefined {
  const url = new URL(input, "http://meridian.local");
  const normalizedMethod = method.toUpperCase();
  const route = scenario.responses.find((candidate) => {
    const expected = new URL(candidate.path, "http://meridian.local");
    return (candidate.method === normalizedMethod || (normalizedMethod === "HEAD" && candidate.method === "GET")) && expected.pathname === url.pathname &&
      (!expected.search || expected.search === url.search);
  });
  const headers = {
    "Content-Type": "application/json; charset=utf-8",
    "Cache-Control": "no-store",
    "x-meridian-dev-fixture": "true",
    [scenarioHeader]: scenario.id
  };
  if (route) {
    return { ...cloneFixture(route.response), headers: { ...headers, ...route.response.headers } };
  }
  // Never let a mutation accidentally match a GET fixture. Dynamic fixture patterns
  // are anchored by the existing resolver; unknown siblings cannot prefix-match.
  if (normalizedMethod !== "GET" && normalizedMethod !== "HEAD") return undefined;
  const body = resolveDevFixture<unknown>(`${url.pathname}${url.search}`);
  return body === undefined ? undefined : { status: 200, body, headers };
}

export function unexpectedScenarioRequest(scenario: ApiScenario, input: string, method: string): string {
  return `Unexpected API request in scenario "${scenario.id}": ${method.toUpperCase()} ${input}. Add an explicit scenario response for this request.`;
}

/** Consumers assert the recorded violations even when application code catches fetch errors. */
export function createScenarioRuntime(scenario: ApiScenario, { strict = false }: { strict?: boolean } = {}) {
  const unexpectedRequests: string[] = [];
  const resolve = (input: string, method = "GET") => {
    const response = resolveScenarioResponse(scenario, input, method);
    if (!response && strict) unexpectedRequests.push(unexpectedScenarioRequest(scenario, input, method));
    return response;
  };
  return {
    resolve,
    unexpectedRequests,
    assertNoUnexpectedRequests() {
      if (unexpectedRequests.length) throw new Error(unexpectedRequests.join("\n"));
    },
    createFetch(): typeof fetch {
      return async (input, init) => {
        const request = typeof input === "string" || input instanceof URL ? undefined : input;
        const url = request?.url ?? String(input);
        const method = init?.method ?? request?.method ?? "GET";
        const signal = init?.signal ?? request?.signal;
        signal?.throwIfAborted();
        const response = resolve(url, method);
        if (!response) {
          const detail = unexpectedScenarioRequest(scenario, url, method);
          if (strict) throw new Error(detail);
          return new Response(JSON.stringify({ status: 501, title: "Scenario response unavailable", detail }), {
            status: 501,
            headers: { [scenarioHeader]: scenario.id, "Content-Type": "application/json" }
          });
        }
        await waitForScenarioDelay(response.delayMs ?? 0, signal);
        return new Response(method.toUpperCase() === "HEAD" || response.status === 204 ? null : JSON.stringify(response.body), {
          status: response.status,
          headers: response.headers
        });
      };
    }
  };
}

export function waitForScenarioDelay(delayMs: number, signal?: AbortSignal | null): Promise<void> {
  signal?.throwIfAborted();
  if (delayMs <= 0) return Promise.resolve();
  return new Promise((resolve, reject) => {
    const finish = () => {
      signal?.removeEventListener("abort", abort);
      resolve();
    };
    const timer = setTimeout(finish, delayMs);
    const abort = () => {
      clearTimeout(timer);
      signal?.removeEventListener("abort", abort);
      reject(signal?.reason ?? new DOMException("The request was aborted.", "AbortError"));
    };
    signal?.addEventListener("abort", abort, { once: true });
  });
}
