import { adaptGovernedReportingRunResponse } from "@/lib/reporting-governance-api";
import type { ReportingRunParameters } from "@/types";

const normalizedParameters: ReportingRunParameters = {
  scope: {
    fundProfileId: "fund-alpha",
    entityScopeKind: "Portfolio",
    entityId: null,
    portfolioId: "portfolio-credit",
    investorId: null,
    dimensions: {
      strategyId: "private-credit",
      externalGlDimensions: { Department: "Private Credit" }
    }
  },
  periodId: "2026-Q2",
  asOfDate: "2026-06-30",
  ledgerBook: { ledgerBookId: null, ledgerBookCode: "Primary GL" },
  accountingBasis: "Gaap",
  presentationCurrency: "USD",
  consolidationLevel: "Portfolio",
  outputFormat: "Pdf",
  finality: "Draft",
  includeSupportingSchedules: true,
  includeEvidenceAppendix: true,
  templateParameters: {}
};

const canonicalAccess = {
  policyId: "policy-1",
  policyVersion: "2",
  mode: "Restricted",
  ownerPrincipalId: "maker-1",
  allowOwnerAccess: true,
  principals: [
    { kind: "User", principalId: "maker-1" },
    { kind: "Group", principalId: "reviewers" },
    { kind: "Company", principalId: "company-1" }
  ],
  policyHash: "policy-hash"
};

describe("reporting governance API compatibility boundary", () => {
  it("returns the canonical required normalized parameters and versioned action projection", () => {
    const result = adaptGovernedReportingRunResponse({
      runId: "run-1",
      version: 7,
      access: canonicalAccess,
      snapshot: { parametersCanonicalJson: null },
      normalizedParameters,
      actionAvailability: [{
        action: "validate",
        isAllowed: true,
        blockedReason: null,
        expectedVersion: 7
      }]
    });

    expect(result.normalizedParameters.scope.dimensions).toEqual(
      normalizedParameters.scope.dimensions
    );
    expect(result.actionAvailability).toEqual([{
      action: "validate",
      isAllowed: true,
      blockedReason: null,
      expectedVersion: 7
    }]);
    expect(result.access).toEqual(canonicalAccess);
  });

  it("adapts certified legacy parameters and allowed-action aliases only at the API boundary", () => {
    const result = adaptGovernedReportingRunResponse({
      runId: "run-legacy",
      version: 3,
      access: canonicalAccess,
      snapshot: { parametersCanonicalJson: JSON.stringify(normalizedParameters) },
      allowedActions: ["submit"]
    });

    expect(result.normalizedParameters).toEqual(normalizedParameters);
    expect(result.actionAvailability).toEqual([{
      action: "submit",
      isAllowed: true,
      blockedReason: null,
      expectedVersion: 3
    }]);
    expect(result).not.toHaveProperty("allowedActions");
    expect(result).not.toHaveProperty("parameters");
  });

  it("rejects a response without a canonical or certified normalized parameter projection", () => {
    expect(() => adaptGovernedReportingRunResponse({
      runId: "run-malformed",
      version: 1,
      access: canonicalAccess,
      snapshot: { parametersCanonicalJson: "{}" },
      actionAvailability: []
    })).toThrow(/normalizedParameters/);
  });

  it("rejects flattened or untyped access scope instead of inferring principal kinds", () => {
    expect(() => adaptGovernedReportingRunResponse({
      runId: "run-flat-access",
      version: 1,
      access: {
        ...canonicalAccess,
        principals: undefined,
        principalIds: ["maker-1", "reviewers"]
      },
      snapshot: { parametersCanonicalJson: null },
      normalizedParameters,
      actionAvailability: []
    })).toThrow(/typed principals/);

    expect(() => adaptGovernedReportingRunResponse({
      runId: "run-untyped-access",
      version: 1,
      access: {
        ...canonicalAccess,
        principals: [{ principalId: "maker-1" }]
      },
      snapshot: { parametersCanonicalJson: null },
      normalizedParameters,
      actionAvailability: []
    })).toThrow(/principal kind/);
  });
});
