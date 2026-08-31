import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  ACTIVATION_OUTCOME_KEYS,
  primeActivationProgress,
  recordActivationOutcome,
  resetActivationProgressForTests,
  subscribeToActivationProgress
} from "./activation";
import type { FirstRunStatus } from "@/features/first-run/types";

const postJson = vi.hoisted(() => vi.fn());
vi.mock("@/lib/api", () => ({ apiPostJson: postJson }));

function status(dataImported: boolean): FirstRunStatus {
  return {
    isComplete: true,
    goal: "monitor-investments",
    starterKitId: "personal-portfolio",
    dataChoice: "upload",
    workspace: {
      id: "primary",
      name: "Meridian Workspace",
      isSample: false,
      badge: "LOCAL",
      safetyMessage: "Local workspace data.",
      samplePackVersion: ""
    },
    starterKits: [],
    outcomes: [
      { key: "data-imported", label: "Import sample or real data", actionLabel: "Import data", route: "/accounting/statement-import", isComplete: dataImported, completedAtUtc: dataImported ? "2026-08-01T10:00:00Z" : null }
    ],
    recommendedActions: [],
    sampleWorkspace: null
  };
}

describe("recordActivationOutcome", () => {
  beforeEach(() => {
    resetActivationProgressForTests();
    postJson.mockReset();
  });

  afterEach(() => {
    resetActivationProgressForTests();
  });

  it("posts the outcome key to the host and publishes the returned status", async () => {
    const next = status(true);
    postJson.mockResolvedValue(next);
    const seen: FirstRunStatus[] = [];
    subscribeToActivationProgress((value) => seen.push(value));

    await recordActivationOutcome(ACTIVATION_OUTCOME_KEYS.dataImported);

    expect(postJson).toHaveBeenCalledWith(
      "/api/workstation/first-run/outcomes/complete",
      { key: "data-imported" }
    );
    expect(seen).toEqual([next]);
  });

  it("does not re-post an outcome the host already recorded", async () => {
    // Re-posting would overwrite the original completion timestamp with a later one.
    primeActivationProgress(status(true));

    await recordActivationOutcome(ACTIVATION_OUTCOME_KEYS.dataImported);

    expect(postJson).not.toHaveBeenCalled();
  });

  it("posts once when the same outcome is reported twice before the first call settles", async () => {
    postJson.mockResolvedValue(status(true));

    await Promise.all([
      recordActivationOutcome(ACTIVATION_OUTCOME_KEYS.dataImported),
      recordActivationOutcome(ACTIVATION_OUTCOME_KEYS.dataImported)
    ]);

    expect(postJson).toHaveBeenCalledTimes(1);
  });

  it("swallows host failures so the workflow the user finished still succeeds", async () => {
    postJson.mockRejectedValue(new Error("host unavailable"));

    await expect(recordActivationOutcome(ACTIVATION_OUTCOME_KEYS.reportRun)).resolves.toBeUndefined();
  });

  it("retries on a later report after a failed one", async () => {
    postJson.mockRejectedValueOnce(new Error("host unavailable"));
    postJson.mockResolvedValueOnce(status(true));

    await recordActivationOutcome(ACTIVATION_OUTCOME_KEYS.dataImported);
    await recordActivationOutcome(ACTIVATION_OUTCOME_KEYS.dataImported);

    expect(postJson).toHaveBeenCalledTimes(2);
  });

  it("survives a host response that is not a first-run status", async () => {
    // Regression: the returned payload is cached, and a cached shape without `outcomes` used to
    // make the next report throw synchronously into the caller -- turning an export or import that
    // had already succeeded into a reported failure.
    postJson.mockResolvedValue({ jobId: "export-1", success: true });

    await recordActivationOutcome(ACTIVATION_OUTCOME_KEYS.resultSaved);

    expect(() => recordActivationOutcome(ACTIVATION_OUTCOME_KEYS.resultSaved)).not.toThrow();
    await expect(recordActivationOutcome(ACTIVATION_OUTCOME_KEYS.resultSaved)).resolves.toBeUndefined();
  });

  it("resolves instead of propagating when the request layer throws synchronously", async () => {
    postJson.mockImplementation(() => {
      throw new Error("no transport");
    });

    await expect(recordActivationOutcome(ACTIVATION_OUTCOME_KEYS.reportRun)).resolves.toBeUndefined();
  });

  it("stops notifying a subscriber once it unsubscribes", async () => {
    postJson.mockResolvedValue(status(true));
    const listener = vi.fn();
    const unsubscribe = subscribeToActivationProgress(listener);
    unsubscribe();

    await recordActivationOutcome(ACTIVATION_OUTCOME_KEYS.dataImported);

    expect(listener).not.toHaveBeenCalled();
  });
});
