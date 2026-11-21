import { describe, expect, it } from "vitest";
import { resolveDevFixture } from "../dev-fixtures";
import type { FamilyOfficeOverview } from "@/types/family-office.types";
import type { AuditTrailExplorerResult } from "@/types/execution-audit.types";

describe("explicit analytical demonstration fixtures", () => {
  it("filters legal history without relabelling financial snapshot dates", () => {
    const history = resolveDevFixture<FamilyOfficeOverview>("/api/workstation/family-office/overview?asOf=2024-06-01")!;
    expect(history.ownershipGraph.asOfDate).toBe("2024-06-01");
    expect(history.ownershipGraph.edges.map((edge) => edge.edgeId)).toEqual(["edge-trust"]);
    expect(history.asOfDate).toBe("2026-05-29");
    expect(history.capitalCommitments[0].asOfDate).toBe("2026-05-29");
    const current = resolveDevFixture<FamilyOfficeOverview>("/api/workstation/family-office/overview")!;
    expect(current.ownershipGraph.edges).toHaveLength(4);
    expect(resolveDevFixture("/api/workstation/family-office/overview?asOf=2026-02-30")).toBeUndefined();
  });

  it("keeps order trace identity and time filters exact, with honest truncation", () => {
    const exact = resolveDevFixture<AuditTrailExplorerResult>("/api/execution/audit/search?orderId=PO-1&limit=2")!;
    expect(exact.totalMatched).toBe(4);
    expect(exact.returned).toBe(2);
    expect(exact.entries.every((entry) => entry.orderId === "PO-1")).toBe(true);
    const unrelated = resolveDevFixture<AuditTrailExplorerResult>("/api/execution/audit/search?orderId=PO")!;
    expect(unrelated.entries).toEqual([]);
    const fill = resolveDevFixture<AuditTrailExplorerResult>("/api/execution/audit/search?orderId=PO-1&fromUtc=2026-05-29T11%3A00%3A03Z&toUtc=2026-05-29T11%3A00%3A06Z")!;
    expect(fill.entries.map((entry) => entry.auditId)).toEqual(["synthetic-fill-1"]);
    expect(fill.entries[0].metadata?.timestampBasis).toBe("execution-report-event");
  });

  it("does not fabricate fixtures for unsupported authoritative reads", () => {
    expect(resolveDevFixture("/api/ledger/unknown-book/private-capital")).toBeUndefined();
    expect(resolveDevFixture("/api/workstation/unknown-analytical-surface")).toBeUndefined();
  });
});
