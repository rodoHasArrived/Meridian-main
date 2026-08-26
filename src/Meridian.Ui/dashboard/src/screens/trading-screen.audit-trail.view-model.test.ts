import { describe, expect, it } from "vitest";
import {
  auditLedgerLabel,
  auditLedgerTone,
  auditOutcomeTone,
  buildAuditTrailPanelViewModel,
  buildAuditTrailRow
} from "@/screens/trading-screen.audit-trail.view-model";
import type { AuditTrailTimelineEntry } from "@/types/execution-audit.types";

function entry(overrides: Partial<AuditTrailTimelineEntry> = {}): AuditTrailTimelineEntry {
  return {
    auditId: "audit-1",
    occurredAt: "2026-05-29T14:03:11Z",
    objectKind: "Order",
    objectId: "ord-991",
    category: "Execution",
    action: "Submit",
    outcome: "Accepted",
    actor: "trader@example.com",
    runId: "run-7",
    symbol: "SPY",
    correlationId: "corr-42",
    reason: null,
    scope: null,
    message: null,
    metadata: null,
    relatedObjectIds: null,
    evidenceRoute: "/reporting/evidence/audit-1",
    actionLedgerSource: "execution",
    actionLedgerSequence: 118,
    previousActionHash: "aaa",
    currentActionHash: "bbb",
    actionLedgerStatus: "Verified",
    ...overrides
  };
}

describe("buildAuditTrailRow", () => {
  it("summarizes an entry with its actor, outcome, and ledger position", () => {
    expect(buildAuditTrailRow(entry())).toMatchObject({
      objectLabel: "Order ord-991",
      actionLabel: "Execution · Submit",
      outcomeTone: "success",
      actor: "trader@example.com",
      context: "SPY · run-7 · corr-42",
      ledgerLabel: "Verified #118",
      ledgerTone: "success"
    });
  });

  it("attributes an unattributed action to the system rather than leaving it blank", () => {
    expect(buildAuditTrailRow(entry({ actor: null })).actor).toBe("System");
  });

  it("falls back to the message when no correlating identifiers are present", () => {
    const row = buildAuditTrailRow(
      entry({ symbol: null, runId: null, correlationId: null, reason: null, message: "Circuit breaker tripped" })
    );
    expect(row.context).toBe("Circuit breaker tripped");
  });
});

describe("audit outcome and ledger tones", () => {
  it("escalates rejected and failed outcomes", () => {
    expect(auditOutcomeTone("Rejected")).toBe("danger");
    expect(auditOutcomeTone("failed")).toBe("danger");
    expect(auditOutcomeTone("Partial")).toBe("warning");
    expect(auditOutcomeTone("Accepted")).toBe("success");
  });

  it("reports an entry with no hash as unchained instead of implying it is verified", () => {
    const unchained = entry({ currentActionHash: null, actionLedgerStatus: null, actionLedgerSequence: null });
    expect(auditLedgerLabel(unchained)).toBe("Unchained");
    expect(auditLedgerTone(unchained)).toBe("warning");
  });

  it("escalates a broken hash chain to danger", () => {
    expect(auditLedgerTone(entry({ actionLedgerStatus: "Broken" }))).toBe("danger");
  });

  it("says the sequence is missing rather than printing a bare hash position", () => {
    expect(auditLedgerLabel(entry({ actionLedgerSequence: null }))).toBe("Verified no sequence");
  });
});

describe("buildAuditTrailPanelViewModel", () => {
  it("reports truncation so a partial trail is never read as the whole trail", () => {
    const vm = buildAuditTrailPanelViewModel(
      { asOf: "2026-05-29T14:05:00Z", totalMatched: 412, returned: 50, entries: [entry()] },
      50
    );

    expect(vm.countLabel).toBe("50 of 412");
    expect(vm.truncated).toBe(true);
    expect(vm.truncationNotice).toContain("50 most recent of 412 matches");
  });

  it("does not claim truncation when everything matched is returned", () => {
    const vm = buildAuditTrailPanelViewModel(
      { asOf: "2026-05-29T14:05:00Z", totalMatched: 1, returned: 1, entries: [entry()] },
      50
    );

    expect(vm.truncated).toBe(false);
    expect(vm.truncationNotice).toBeNull();
  });

  it("distinguishes an unloaded panel from an empty result set", () => {
    expect(buildAuditTrailPanelViewModel(null, 50)).toMatchObject({
      countLabel: "—",
      asOfLabel: "Not loaded",
      emptyState: "Audit trail has not loaded."
    });
    expect(buildAuditTrailPanelViewModel(
      { asOf: "2026-05-29T14:05:00Z", totalMatched: 0, returned: 0, entries: [] },
      50
    ).emptyState).toBe("No audit entries match these filters.");
  });
});
