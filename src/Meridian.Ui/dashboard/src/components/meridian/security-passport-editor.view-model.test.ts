import { describe, expect, it } from "vitest";
import {
  attachPendingDrafts,
  buildConflictRows,
  classifyConflictResolution,
  toPassportConflicts,
  buildPassportActions,
  buildPassportStateBadge,
  buildTrustPosture,
  buildWorkbenchErrorBanner,
  validateConflictOverride,
  type PassportLifecycleAction
} from "@/components/meridian/security-passport-editor.view-model";

function actionMap(actions: ReturnType<typeof buildPassportActions>): Record<PassportLifecycleAction, boolean> {
  return actions.reduce(
    (acc, a) => {
      acc[a.action] = a.enabled;
      return acc;
    },
    {} as Record<PassportLifecycleAction, boolean>
  );
}

describe("passport lifecycle actions", () => {
  it("enables save-draft only when there is a pending edit", () => {
    expect(actionMap(buildPassportActions({ state: null, hasPendingEdit: false, isBusy: false })))
      .toMatchObject({ "save-draft": false });
    expect(actionMap(buildPassportActions({ state: null, hasPendingEdit: true, isBusy: false })))
      .toMatchObject({ "save-draft": true });
  });

  it("gates submit/approve/publish on the matching lifecycle state", () => {
    expect(actionMap(buildPassportActions({ state: "Draft", hasPendingEdit: false, isBusy: false })))
      .toMatchObject({ submit: true, approve: false, publish: false });
    expect(actionMap(buildPassportActions({ state: "Submitted", hasPendingEdit: false, isBusy: false })))
      .toMatchObject({ submit: false, approve: true, publish: false });
    expect(actionMap(buildPassportActions({ state: "Approved", hasPendingEdit: false, isBusy: false })))
      .toMatchObject({ submit: false, approve: false, publish: true });
  });

  it("disables everything while busy, with a reason", () => {
    const actions = buildPassportActions({ state: "Approved", hasPendingEdit: true, isBusy: true });
    expect(actions.every((a) => !a.enabled)).toBe(true);
    expect(actions.every((a) => a.disabledReason && a.disabledReason.length > 0)).toBe(true);
  });

  it("explains why publish is disabled before approval", () => {
    const publish = buildPassportActions({ state: "Draft", hasPendingEdit: false, isBusy: false })
      .find((a) => a.action === "publish");
    expect(publish?.enabled).toBe(false);
    expect(publish?.disabledReason).toContain("approved");
  });
});

describe("passport state badge", () => {
  it("maps each lifecycle state to a distinct tone", () => {
    expect(buildPassportStateBadge("Draft").tone).toBe("draft");
    expect(buildPassportStateBadge("Submitted").tone).toBe("submitted");
    expect(buildPassportStateBadge("Approved").tone).toBe("approved");
    expect(buildPassportStateBadge("Published").tone).toBe("published");
    expect(buildPassportStateBadge("Rejected").tone).toBe("rejected");
    expect(buildPassportStateBadge(null).label).toBe("No draft");
  });
});

describe("workbench error banner", () => {
  it("offers a non-destructive reload on a version conflict", () => {
    const banner = buildWorkbenchErrorBanner({ kind: "version-conflict", currentVersion: 7 });
    expect(banner.tone).toBe("error");
    expect(banner.reloadToVersion).toBe(7);
    expect(banner.message).toContain("7");
  });

  it("prompts for a workflow on workflow-required", () => {
    const banner = buildWorkbenchErrorBanner({ kind: "workflow-required" });
    expect(banner.tone).toBe("warning");
    expect(banner.title).toContain("workflow");
  });

  it("surfaces the server message for unprocessable edits", () => {
    const banner = buildWorkbenchErrorBanner({ kind: "unprocessable", message: "Justification is required." });
    expect(banner.message).toBe("Justification is required.");
  });
});

describe("conflict rows", () => {
  const conflicts = [
    { conflictId: "c1", fieldLabel: "Coupon", policyWinnerSource: "golden-copy", challengerSource: "vendor-a", severity: "High" },
    { conflictId: "c2", fieldLabel: "Maturity", policyWinnerSource: "golden-copy", challengerSource: "vendor-b", severity: "Low", isResolved: true }
  ];

  it("classifies severity and disables resolved rows", () => {
    const rows = buildConflictRows(conflicts, { isBusy: false });
    expect(rows[0].severityTone).toBe("high");
    expect(rows[0].canAct).toBe(true);
    expect(rows[1].canAct).toBe(false);
  });

  it("disables all rows while busy", () => {
    const rows = buildConflictRows(conflicts, { isBusy: true });
    expect(rows.every((r) => !r.canAct)).toBe(true);
  });
});

describe("conflict override validation", () => {
  it("requires a reason", () => {
    const result = validateConflictOverride({ chosenWinnerSource: "golden-copy", policyWinnerSource: "golden-copy", reason: "", acknowledgeDeviation: false });
    expect(result.valid).toBe(false);
    expect(result.reason).toContain("reason");
  });

  it("requires acknowledgement when deviating from the policy winner", () => {
    const result = validateConflictOverride({ chosenWinnerSource: "vendor-a", policyWinnerSource: "golden-copy", reason: "Vendor is correct.", acknowledgeDeviation: false });
    expect(result.valid).toBe(false);
    expect(result.reason).toContain("deviation");
  });

  it("accepts an acknowledged deviation with a reason", () => {
    const result = validateConflictOverride({ chosenWinnerSource: "vendor-a", policyWinnerSource: "golden-copy", reason: "Vendor is correct.", acknowledgeDeviation: true });
    expect(result.valid).toBe(true);
  });

  it("accepts the policy winner with a reason and no acknowledgement", () => {
    const result = validateConflictOverride({ chosenWinnerSource: "golden-copy", policyWinnerSource: "golden-copy", reason: "Confirmed.", acknowledgeDeviation: false });
    expect(result.valid).toBe(true);
  });
});

describe("trust posture", () => {
  it("classifies postures into blocked/review/trusted", () => {
    expect(buildTrustPosture("Blocked").tone).toBe("blocked");
    expect(buildTrustPosture("Trusted").tone).toBe("trusted");
    expect(buildTrustPosture("anything-else").tone).toBe("review");
    expect(buildTrustPosture(null).tone).toBe("review");
  });
});

describe("conflict resolution outcome (review D3 \"Use B\")", () => {
  it("treats a result without a pending revision as resolved", () => {
    expect(classifyConflictResolution({ chosenWinnerSource: "golden-copy", newVersion: 8 })).toEqual({ kind: "resolved", newVersion: 8 });
    expect(classifyConflictResolution({ chosenWinnerSource: "golden-copy", newVersion: 8, pendingRevisionId: "  " })).toEqual({
      kind: "resolved",
      newVersion: 8
    });
  });

  it("adopts the staged draft and explains that the conflict resolves on publish", () => {
    const outcome = classifyConflictResolution({ chosenWinnerSource: "vendor-b", newVersion: 7, pendingRevisionId: "rev-9" });
    expect(outcome).toMatchObject({ kind: "draft-staged", newVersion: 7, pendingRevisionId: "rev-9" });
    if (outcome.kind !== "draft-staged") throw new Error("expected a staged draft");
    expect(outcome.banner.tone).toBe("info");
    expect(outcome.banner.message).toBe(
      "Staged draft rev-9 adopting vendor-b's value. The conflict resolves to that source when the draft is approved and published."
    );
  });

  it("keeps a conflict awaiting its staged draft from being resolved again", () => {
    const [row] = buildConflictRows(
      [{ conflictId: "k1", fieldLabel: "Coupon", policyWinnerSource: "golden-copy", challengerSource: "vendor-b", pendingRevisionId: "rev-9" }],
      { isBusy: false }
    );
    expect(row.canAct).toBe(false);
    expect(row.pendingDraftLabel).toBe("Awaiting draft rev-9 — resolves when it is published");
  });
});

describe("conflict list from the trust snapshot", () => {
  const assessment = (conflictId: string, status: "Open" | "Resolved" | "Dismissed", recommendedWinner: string) =>
    ({
      conflict: {
        conflictId,
        securityId: "sec",
        conflictKind: "FieldValue",
        fieldPath: "EconomicDefinition.Coupon",
        providerA: "golden-copy",
        valueA: "5.00",
        providerB: "vendor-b",
        valueB: "5.25",
        detectedAt: "2026-11-24T00:00:00Z",
        status
      },
      currentWinningValue: "5.00",
      challengerValue: "5.25",
      currentWinningSource: "golden-copy",
      challengerSource: "vendor-b",
      recommendation: "PreserveWinner",
      recommendedResolution: "Keep",
      recommendedWinner,
      impactSeverity: "High",
      impactSummary: "",
      impactDetail: "",
      isBulkEligible: false
    }) as const;

  it("lists open conflicts with the recommended winner as the policy winner", () => {
    const conflicts = toPassportConflicts([
      assessment("k1", "Open", "golden-copy"),
      assessment("k2", "Open", "vendor-b"),
      assessment("k3", "Resolved", "golden-copy")
    ]);
    expect(conflicts).toEqual([
      { conflictId: "k1", fieldLabel: "EconomicDefinition.Coupon", policyWinnerSource: "golden-copy", challengerSource: "vendor-b", severity: "High" },
      { conflictId: "k2", fieldLabel: "EconomicDefinition.Coupon", policyWinnerSource: "vendor-b", challengerSource: "golden-copy", severity: "High" }
    ]);
    expect(toPassportConflicts(undefined)).toEqual([]);
  });

  it("links each conflict to the pending Use B draft the server reports for it", () => {
    const base = { securityId: "sec", actor: "alice", createdAt: "", updatedAt: "", fieldPath: null, fieldValue: null, justification: null,
      fundProfileId: null, baseVersion: null, ageHours: 1 };
    const linked = attachPendingDrafts(
      [
        { conflictId: "k1", fieldLabel: "Coupon", policyWinnerSource: "a", challengerSource: "b" },
        { conflictId: "k2", fieldLabel: "Maturity", policyWinnerSource: "a", challengerSource: "b" }
      ],
      [
        { ...base, revisionId: "rev-1", state: "Submitted", conflictId: "k1", conflictCandidateSource: "b" },
        { ...base, revisionId: "rev-ordinary", state: "Draft" },
        { ...base, revisionId: "rev-old", state: "Rejected", conflictId: "k2" }
      ]
    );
    expect(linked[0]).toMatchObject({ pendingRevisionId: "rev-1", pendingRevisionState: "Submitted" });
    expect(linked[1].pendingRevisionId).toBeUndefined();

    const [row] = buildConflictRows([linked[0]], { isBusy: false });
    expect(row).toMatchObject({ canAct: false, pendingRevisionId: "rev-1", pendingRevisionState: "Submitted" });
  });
});
