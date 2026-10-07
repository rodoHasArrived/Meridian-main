import { afterEach, vi } from "vitest";
import { createScenarioRuntime, getScenario, type ApiScenario, type ScenarioId } from "@/scenarios";

type ScenarioOptions = Parameters<typeof createScenarioRuntime>[1];

export type InstalledScenario = ReturnType<typeof createScenarioRuntime> & {
  /** Assert strict request coverage and restore only this installation's changes. */
  restore: () => void;
};

const installations: InstalledScenario[] = [];
const modeKey = "VITE_MERIDIAN_DEV_MODE";

/**
 * Use the same typed responses as URL previews and screenshot capture. Strict
 * coverage is checked after the test, including requests the application caught.
 */
export function installScenario(
  selection: ScenarioId | ApiScenario,
  options: ScenarioOptions = {}
): InstalledScenario {
  const scenario = typeof selection === "string" ? getScenario(selection) : selection;
  const runtime = createScenarioRuntime(scenario, { strict: true, ...options });
  const fetchDescriptor = Object.getOwnPropertyDescriptor(globalThis, "fetch");
  const previousClientMode = import.meta.env[modeKey];
  const hadClientMode = Object.hasOwn(import.meta.env, modeKey);
  const previousProcessMode = process.env[modeKey];
  let restored = false;

  vi.stubGlobal("fetch", runtime.createFetch());
  // A failed scenario must stay failed instead of entering the API client's
  // legacy development fallback and returning unrelated fixture data.
  vi.stubEnv(modeKey, "fixture-only");

  const installed: InstalledScenario = {
    ...runtime,
    restore() {
      if (restored) return;
      restored = true;
      const index = installations.indexOf(installed);
      if (index !== -1) installations.splice(index, 1);
      try {
        runtime.assertNoUnexpectedRequests();
      } finally {
        if (fetchDescriptor) {
          Object.defineProperty(globalThis, "fetch", fetchDescriptor);
        } else {
          Reflect.deleteProperty(globalThis, "fetch");
        }
        if (hadClientMode) {
          Reflect.set(import.meta.env, modeKey, previousClientMode);
        } else {
          Reflect.deleteProperty(import.meta.env, modeKey);
        }
        if (previousProcessMode === undefined) {
          delete process.env[modeKey];
        } else {
          process.env[modeKey] = previousProcessMode;
        }
      }
    }
  };
  installations.push(installed);
  return installed;
}

afterEach(() => {
  const failures: unknown[] = [];
  // Restore nested installations in reverse order, even if coverage fails.
  for (const installed of [...installations].reverse()) {
    try {
      installed.restore();
    } catch (error) {
      failures.push(error);
    }
  }
  if (failures.length === 1) throw failures[0];
  if (failures.length > 1) {
    throw new AggregateError(failures, "Unexpected API requests in strict scenarios.");
  }
});
