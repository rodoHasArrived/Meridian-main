import { describe, expect, it } from "vitest";
import { mergeRateRepairPage } from "./security-rate-repair-discovery";
import { candidate, discovery } from "./security-rate-repair.test-fixtures";

const firstPage = () => ({ ...discovery(), scannedCount: 200, nextSkip: 200, isComplete: false });
const nextPage = () => ({ ...discovery(), skip: 200, scannedCount: 2, candidates: [candidate(), candidate({ securityId: "another-record", displayName: "Another CD" })] });
describe("live rate-repair discovery pages", () => {
  it("deduplicates overlapping records while keeping source observations and candidate counts distinct", () => {
    const result = mergeRateRepairPage(firstPage(), nextPage());
    expect(result.discovery.candidates).toHaveLength(2);
    expect(result.discovery.scannedCount).toBe(202);
    expect(result.discovery.isComplete).toBe(true);
    expect(result.changedRecordIds).toEqual([]);
  });
  it("blocks a changed overlap without replacing the originally observed scalar or version", () => {
    const page = nextPage(); page.candidates = [candidate({ version: 8, rawValue: "50.000000000000" })];
    const result = mergeRateRepairPage(firstPage(), page);
    expect(result.discovery.candidates[0]).toMatchObject({ version: 7, rawValue: "0.500000000000", eligible: false, blockerCode: "DiscoveryChanged" });
    expect(result.changedRecordIds).toEqual([candidate().securityId]);
    expect(result.discovery.isComplete).toBe(false);
  });
  it("retains unavailable-projection issues after the scan reaches its terminal page", () => {
    const first = firstPage(); first.issues = [{ securityId: "missing", code: "ProjectionUnavailable", message: "Canonical source unavailable." }];
    const page = nextPage(); page.candidates = [];
    const result = mergeRateRepairPage(first, page);
    expect(result.discovery.issues).toEqual(first.issues);
    expect(result.discovery.isComplete).toBe(false);
    expect(result.discovery.candidates).toHaveLength(1);
  });
  it.each(["skip", "scope", "cursor"])("refuses a mismatched %s", field => {
    const page = nextPage();
    if (field === "skip") page.skip = 400;
    if (field === "scope") page.scopeLabel = "Different authority";
    if (field === "cursor") page.nextSkip = 100;
    expect(() => mergeRateRepairPage(firstPage(), page)).toThrow(/no longer matches/);
  });
  it("uses current server permissions and keeps prior candidates on a terminal empty page", () => {
    const page = { ...nextPage(), scannedCount: 0, candidates: [], actions: [{ command: "Create", enabled: false, disabledReason: "Permission changed." }] };
    const result = mergeRateRepairPage(firstPage(), page);
    expect(result.discovery.candidates).toHaveLength(1);
    expect(result.discovery.actions[0].enabled).toBe(false);
    expect(result.discovery.nextSkip).toBeNull();
  });
});
