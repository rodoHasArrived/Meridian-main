import { describe, expect, it } from "vitest";
import { buildAuditIntegrityViewModel } from "@/screens/accounting-screen.audit-integrity.view-model";
import type { AccountingAuditIntegrityReportDto, AuditChainIntegrityResultDto } from "@/types";

function chain(overrides: Partial<AuditChainIntegrityResultDto> = {}): AuditChainIntegrityResultDto {
  return {
    chain: "accounting-actions",
    status: "verified",
    linksChecked: 12,
    unprotectedPreChainCount: 0,
    failureKind: null,
    detail: null,
    failedSequence: null,
    ...overrides
  };
}

function report(chains: AuditChainIntegrityResultDto[], allVerified: boolean): AccountingAuditIntegrityReportDto {
  return { verifiedAtUtc: "2026-10-18T09:00:00Z", allVerified, chains };
}

describe("buildAuditIntegrityViewModel", () => {
  it("passes only when every configured chain verified", () => {
    const view = buildAuditIntegrityViewModel(report([
      chain(),
      chain({ chain: "ledger-events", status: "not-configured", linksChecked: 0, detail: "No durable ledger journal store is composed." })
    ], true));

    expect(view.tone).toBe("success");
    expect(view.verdict).toContain("1 of 2");
    expect(view.rows[1].statusLabel).toBe("Not kept in this deployment");
    expect(view.rows[1].coverage).toContain("No durable ledger journal store");
  });

  it("reports a broken chain with its classification and position", () => {
    const view = buildAuditIntegrityViewModel(report([
      chain({ status: "broken", linksChecked: 1, failureKind: "EventMutated", failedSequence: 7, detail: "Digest mismatch." })
    ], false));

    expect(view.tone).toBe("danger");
    expect(view.title).toBe("Audit chain integrity failure");
    expect(view.verdict).toContain("Accounting actions");
    expect(view.rows[0].failure).toBe("EventMutated at sequence 7 — Digest mismatch.");
  });

  it("never treats an unreadable store as verified or as tampering", () => {
    const view = buildAuditIntegrityViewModel(report([
      chain(),
      chain({ chain: "ledger-events", status: "unavailable", linksChecked: 0, failureKind: "NpgsqlException", detail: "The audit store could not be read; its chain was not verified." })
    ], false));

    expect(view.tone).toBe("warning");
    expect(view.title).toBe("Audit chains partly checked");
    expect(view.rows[1].failure).toBeNull();
  });

  it("does not call a run partly checked when no chain verified", () => {
    const view = buildAuditIntegrityViewModel(report([
      chain({ status: "unavailable", linksChecked: 0, failureKind: "IOException", detail: "The audit store could not be read; its chain was not verified." }),
      chain({ chain: "ledger-events", status: "not-configured", linksChecked: 0 })
    ], false));

    expect(view.tone).toBe("warning");
    expect(view.title).toBe("No audit chain verified");
    expect(view.verdict).toContain("could not be read");
  });

  it("does not certify a chain that has not begun chaining", () => {
    const view = buildAuditIntegrityViewModel(report([
      chain({ status: "not-started", linksChecked: 0, unprotectedPreChainCount: 4, detail: "Chaining has not begun in this store, so no record is protected yet." }),
      chain({ chain: "ledger-events", status: "verified", linksChecked: 9 })
    ], false));

    expect(view.tone).toBe("warning");
    expect(view.title).toBe("Audit chains partly checked");
    expect(view.verdict).toBe("Accounting actions has not begun chaining, so none of its records are protected yet.");
    expect(view.rows[0].statusLabel).toBe("Nothing chained yet");
    expect(view.rows[0].coverage).toBe("0 chained entries checked; 4 earlier records predate chaining and are not covered");
  });

  it("does not claim integrity when no chain is configured", () => {
    const view = buildAuditIntegrityViewModel(report([
      chain({ status: "not-configured", linksChecked: 0 }),
      chain({ chain: "ledger-events", status: "not-configured", linksChecked: 0 })
    ], false));

    expect(view.tone).toBe("warning");
    expect(view.title).toBe("No audit chain verified");
  });

  it("states how much pre-chain history the chain cannot vouch for", () => {
    const view = buildAuditIntegrityViewModel(report([chain({ linksChecked: 1, unprotectedPreChainCount: 3 })], true));

    expect(view.rows[0].coverage).toBe("1 chained entry checked; 3 earlier records predate chaining and are not covered");
  });
});
