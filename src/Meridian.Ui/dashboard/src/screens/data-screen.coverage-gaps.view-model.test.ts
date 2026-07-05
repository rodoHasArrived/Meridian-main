import { describe, expect, it } from "vitest";
import { buildCoverageGapsModel } from "./data-screen.coverage-gaps.view-model";
import type { SecurityMasterQualityReport, SecurityMasterQualityViolation } from "@/types";

function violation(overrides: Partial<SecurityMasterQualityViolation> = {}): SecurityMasterQualityViolation {
  return {
    ruleId: "RC001",
    ruleName: "Unmastered Active Symbol",
    category: "RefreshControl",
    securityId: "9c2f6f6e-0000-0000-0000-000000000001",
    fieldPath: "symbol.GME",
    message: "Symbol 'GME' is active in configured-streaming, canonical-symbol-registry but has no resolvable security-master record.",
    severity: "Error",
    detectedAt: "2026-07-05T12:00:00Z",
    ...overrides
  };
}

function report(violations: SecurityMasterQualityViolation[]): SecurityMasterQualityReport {
  return {
    runAt: "2026-07-05T12:00:00Z",
    securitiesScanned: 10,
    violationCount: violations.length,
    violations
  };
}

describe("buildCoverageGapsModel", () => {
  it("keeps only RC001 violations, extracts the symbol and reporting sources", () => {
    const model = buildCoverageGapsModel(
      report([
        violation(),
        violation({
          ruleId: "MA001",
          ruleName: "Missing Maturity Date",
          fieldPath: "assetSpecificTerms.maturity",
          message: "Bond securities require a maturity date."
        })
      ])
    );

    const row = model.rows[0];
    expect(model.rows).toHaveLength(1);
    expect(row.symbol).toBe("GME");
    expect(row.sourcesLabel).toBe("configured-streaming, canonical-symbol-registry");
    expect(model.summary).toContain("1 active symbol");
  });

  it("sorts gaps alphabetically and reports full coverage when none remain", () => {
    const populated = buildCoverageGapsModel(
      report([
        violation({ fieldPath: "symbol.ZZZQ", message: "Symbol 'ZZZQ' is active in ledger-positions but has no resolvable security-master record." }),
        violation({ fieldPath: "symbol.AMC", message: "Symbol 'AMC' is active in configured-streaming but has no resolvable security-master record." })
      ])
    );
    const empty = buildCoverageGapsModel(report([]));

    expect(populated.rows.map((row) => row.symbol)).toEqual(["AMC", "ZZZQ"]);
    expect(empty.rows).toHaveLength(0);
    expect(empty.summary).toContain("Every active symbol");
  });
});
