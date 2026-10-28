export type ScenarioMethod = "GET" | "HEAD" | "POST" | "PUT" | "PATCH" | "DELETE";

/** A response is never implicitly successful. Every scenario declares its HTTP status. */
export interface ScenarioResponse<T = unknown> {
  status: number;
  body: T;
  delayMs?: number;
  headers?: Record<string, string>;
}

export interface ScenarioRoute<T = unknown> {
  method: ScenarioMethod;
  path: string;
  response: ScenarioResponse<T>;
}

export interface ApiScenario {
  id: string;
  previewPath: string;
  responses: readonly ScenarioRoute[];
  waitForTexts: readonly string[];
  waitForAbsentTexts?: readonly string[];
}

export interface ScenarioProblem {
  status: number;
  title: string;
  detail: string;
}
