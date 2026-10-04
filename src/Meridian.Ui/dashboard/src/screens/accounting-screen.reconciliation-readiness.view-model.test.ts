import { describe, expect, it } from "vitest";
import {
  buildOpenCaseRow,
  buildQueueAccountRow,
  buildQueueReadinessSummary,
  buildTaxonomyViewModel
} from "@/screens/accounting-screen.reconciliation-readiness.view-model";
import type {
  ReconciliationCaseSummary,
  ReconciliationQueueAccountStatus,
  ReconciliationTaxonomySnapshot
} from "@/types/reconciliation-readiness.types";

function status(overrides: Partial<ReconciliationQueueAccountStatus> = {}): ReconciliationQueueAccountStatus {
  return {
    accountId: "00000000-0000-0000-0000-000000000001",
    accountCode: "FUND-A",
    queueState: "Open",
    unresolvedBreakCount: 3,
    signOffReady: false,
    nextBestAction: "Resolve the three cash breaks raised on 26 Aug.",
    blockerReason: "",
    evidenceLinks: ["evidence/run-42"],
    ...overrides
  };
}

function openCase(overrides: Partial<ReconciliationCaseSummary> = {}): ReconciliationCaseSummary {
  return {
    caseId: "case-1",
    importId: "import-1",
    status: "InReview",
    reason: "Cash balance mismatch",
    confidence: 0.82,
    rationale: "Matched on amount, not on value date.",
    createdAtUtc: "2026-08-24T09:00:00Z",
    assignee: "j.rowe",
    priority: "High",
    slaState: "OnTrack",
    slaDueAtUtc: "2026-08-27T09:00:00Z",
    businessAgeHours: 12.25,
    ageBand: "1-2 days",
    version: 3,
    ...overrides
  };
}

const taxonomy: ReconciliationTaxonomySnapshot = {
  version: 4,
  rootCauses: [
    { code: "TIMING", displayName: "Settlement timing", version: 4, isActive: true },
    { code: "LEGACY_FX", displayName: "Legacy FX handling", version: 2, isActive: false }
  ],
  resolutionCodes: [{ code: "ADJUSTED", displayName: "Ledger adjusted", version: 4, isActive: true }]
};

describe("queue account rows", () => {
  it("presents the server's next action rather than deriving one", () => {
    const row = buildQueueAccountRow(status());

    expect(row.nextBestAction).toBe("Resolve the three cash breaks raised on 26 Aug.");
    expect(row.readinessLabel).toBe("Not ready");
    expect(row.readinessTone).toBe("danger");
  });

  it("says so when the server reported no next action", () => {
    expect(buildQueueAccountRow(status({ nextBestAction: "  " })).nextBestAction)
      .toBe("No next action reported.");
  });

  it("shows a blocker alongside a ready flag instead of resolving the contradiction", () => {
    const row = buildQueueAccountRow(status({ signOffReady: true, blockerReason: "Late adjustment pending" }));

    expect(row.readinessLabel).toBe("Ready");
    expect(row.readinessTone).toBe("warning");
    expect(row.blockerReason).toBe("Late adjustment pending");
  });

  it("treats a ready account with no blocker as clear", () => {
    const row = buildQueueAccountRow(status({ signOffReady: true, unresolvedBreakCount: 0 }));

    expect(row.readinessTone).toBe("success");
    expect(row.blockerReason).toBeNull();
  });

  it("falls back to the account id when no code is supplied", () => {
    expect(buildQueueAccountRow(status({ accountCode: "" })).accountCode)
      .toBe("00000000-0000-0000-0000-000000000001");
  });

  it("counts evidence links rather than implying evidence exists", () => {
    expect(buildQueueAccountRow(status({ evidenceLinks: [] })).evidenceCountLabel).toBe("No evidence linked");
    expect(buildQueueAccountRow(status()).evidenceCountLabel).toBe("1 evidence link");
  });
});

describe("queue readiness summary", () => {
  it("reports nothing rather than zero before the read lands", () => {
    const summary = buildQueueReadinessSummary(null);

    expect(summary.accountsLabel).toBe("—");
    expect(summary.blockedNotice).toBeNull();
  });

  it("totals readiness across accounts and raises a blocked notice only when blocked", () => {
    const summary = buildQueueReadinessSummary([
      status({ signOffReady: true, unresolvedBreakCount: 0 }),
      status({ accountId: "b", blockerReason: "Missing statement" })
    ]);

    expect(summary.accountsLabel).toBe("2");
    expect(summary.readyLabel).toBe("1 of 2");
    expect(summary.blockedLabel).toBe("1");
    expect(summary.unresolvedLabel).toBe("3");
    expect(summary.blockedNotice).toContain("1 account reports a blocker");
  });

  it("stays silent when nothing is blocked", () => {
    expect(buildQueueReadinessSummary([status({ signOffReady: true })]).blockedNotice).toBeNull();
  });
});

describe("open case rows", () => {
  it("resolves casework codes through the taxonomy catalog", () => {
    const row = buildOpenCaseRow(openCase({ rootCauseCode: "TIMING", resolutionCode: "ADJUSTED" }), taxonomy);

    expect(row.rootCauseLabel).toBe("Settlement timing");
    expect(row.resolutionLabel).toBe("Ledger adjusted");
  });

  it("marks a retired catalog entry as retired", () => {
    expect(buildOpenCaseRow(openCase({ rootCauseCode: "LEGACY_FX" }), taxonomy).rootCauseLabel)
      .toBe("Legacy FX handling (retired)");
  });

  it("distinguishes an unset code from one the catalog does not define", () => {
    expect(buildOpenCaseRow(openCase({ rootCauseCode: null }), taxonomy).rootCauseLabel).toBe("Unset");
    expect(buildOpenCaseRow(openCase({ rootCauseCode: "MYSTERY" }), taxonomy).rootCauseLabel)
      .toBe("MYSTERY (not in taxonomy)");
  });

  it("shows the raw code when the taxonomy did not load, without claiming it is unknown", () => {
    expect(buildOpenCaseRow(openCase({ rootCauseCode: "TIMING" }), null).rootCauseLabel).toBe("TIMING");
  });

  it("leads an SLA breach with when it breached", () => {
    const row = buildOpenCaseRow(
      openCase({ slaState: "Breached", slaBreachedAtUtc: "2026-08-25T09:00:00Z" }),
      taxonomy
    );

    expect(row.slaLabel).toBe("Breached — breached 2026-08-25T09:00:00Z");
    expect(row.slaTone).toBe("danger");
  });

  it("keeps an unrecognized SLA state verbatim instead of bucketing it", () => {
    const row = buildOpenCaseRow(openCase({ slaState: "Deferred", slaDueAtUtc: null }), taxonomy);

    expect(row.slaLabel).toBe("Deferred");
    expect(row.slaTone).toBe("default");
  });

  it("names an unassigned case rather than rendering an empty owner", () => {
    expect(buildOpenCaseRow(openCase({ assignee: null }), taxonomy).assignee).toBe("Unassigned");
  });
});

describe("taxonomy view model", () => {
  it("reports not-loaded without implying an empty catalog", () => {
    const view = buildTaxonomyViewModel(null, [openCase()]);

    expect(view.loaded).toBe(false);
    expect(view.versionLabel).toBe("Not loaded");
    expect(view.unknownNotice).toBeNull();
  });

  it("flags codes cited by cases that the catalog does not define", () => {
    const view = buildTaxonomyViewModel(taxonomy, [openCase({ rootCauseCode: "MYSTERY" })]);

    expect(view.unknownCodes).toEqual(["MYSTERY"]);
    expect(view.unknownNotice).toContain("not in taxonomy v4");
  });

  it("checks each slot against its own catalog, matching how a row labels it", () => {
    // TIMING is a root cause, not a resolution code. The row already says
    // "not in taxonomy" for it; the notice must agree.
    const view = buildTaxonomyViewModel(taxonomy, [openCase({ resolutionCode: "TIMING" })]);

    expect(view.unknownCodes).toEqual(["TIMING"]);
    expect(buildOpenCaseRow(openCase({ resolutionCode: "TIMING" }), taxonomy).resolutionLabel)
      .toBe("TIMING (not in taxonomy)");
  });

  it("stays silent when every cited code is in the catalog", () => {
    const view = buildTaxonomyViewModel(taxonomy, [openCase({ rootCauseCode: "TIMING", resolutionCode: "ADJUSTED" })]);

    expect(view.unknownCodes).toEqual([]);
    expect(view.unknownNotice).toBeNull();
    expect(view.versionLabel).toBe("v4");
  });
});
