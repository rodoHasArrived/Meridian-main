import { describe, expect, it } from "vitest";
import {
  buildDraftStateMarking,
  buildPublicationRecord,
  buildRestatementChain,
  externalOutputs,
  internalOutputs,
  normalizePublicationOutputKind,
  type PublicationRecordInput
} from "@/lib/reporting-publication";

function record(overrides: Partial<PublicationRecordInput> = {}): PublicationRecordInput {
  return {
    version: "09",
    reportName: "Investment Performance Report",
    periodLabel: "September 2026",
    publishedAtUtc: "2026-10-05T12:32:00Z",
    approvedBy: "Jane Smith",
    dataCutoffUtc: "2026-09-30T23:59:00Z",
    ledgerVersion: "CLOSE-2026-09-v4",
    workflowState: "Published",
    ...overrides
  };
}

describe("buildPublicationRecord", () => {
  it("captures what fixes a publication", () => {
    const built = buildPublicationRecord(record());

    expect(built.version).toBe("09");
    expect(built.approvedBy).toBe("Jane Smith");
    expect(built.ledgerVersion).toBe("CLOSE-2026-09-v4");
    expect(built.isComplete).toBe(true);
    expect(built.isRestatement).toBe(false);
  });

  it("names the fields a defensible record is missing", () => {
    const built = buildPublicationRecord(
      record({ approvedBy: null, ledgerVersion: "  ", dataCutoffUtc: null })
    );

    expect(built.isComplete).toBe(false);
    expect(built.missingFields).toEqual(["Approved by", "Data cutoff", "Ledger version"]);
  });

  it("treats a superseding version as a restatement and flags a missing reason", () => {
    const built = buildPublicationRecord(record({ version: "10", supersedesVersion: "09" }));

    expect(built.isRestatement).toBe(true);
    expect(built.missingFields).toEqual(["Restatement reason"]);
  });

  it("keeps the restatement reason and affected sections", () => {
    const built = buildPublicationRecord(
      record({
        version: "10",
        supersedesVersion: "09",
        restatementReason: "Late corporate action adjustment",
        affectedSections: ["Portfolio Overview", "  ", "Investment Income"]
      })
    );

    expect(built.restatementReason).toBe("Late corporate action adjustment");
    expect(built.affectedSections).toEqual(["Portfolio Overview", "Investment Income"]);
    expect(built.isComplete).toBe(true);
  });
});

describe("publication output distribution", () => {
  it("releases only outputs explicitly marked external", () => {
    const built = buildPublicationRecord(
      record({
        outputs: [
          { kind: "pdf", distributionClass: "External" },
          { kind: "xlsx", distributionClass: "External" },
          { kind: "data", distributionClass: "Internal" }
        ]
      })
    );

    expect(externalOutputs(built).map((output) => output.label)).toEqual(["PDF", "Excel"]);
    expect(internalOutputs(built).map((output) => output.label)).toEqual(["Data package"]);
  });

  it("withholds an output whose audience was never stated", () => {
    const built = buildPublicationRecord(record({ outputs: [{ kind: "pdf" }] }));

    expect(built.outputs[0]?.distributionClass).toBe("Internal");
    expect(built.outputs[0]?.isDistributionClassKnown).toBe(false);
    expect(externalOutputs(built)).toEqual([]);
  });

  it("withholds an output whose audience is an unrecognized value", () => {
    const built = buildPublicationRecord(
      record({ outputs: [{ kind: "pdf", distributionClass: "public-ish" }] })
    );

    expect(built.outputs[0]?.distributionClass).toBe("Internal");
  });

  it("keeps evidence and archive internal even when declared external", () => {
    const built = buildPublicationRecord(
      record({
        outputs: [
          { kind: "evidence", distributionClass: "External" },
          { kind: "archive", distributionClass: "External" }
        ]
      })
    );

    expect(externalOutputs(built)).toEqual([]);
    expect(internalOutputs(built)).toHaveLength(2);
  });

  it("excludes an output the template does not permit", () => {
    const built = buildPublicationRecord(
      record({ outputs: [{ kind: "pptx", distributionClass: "External", isPermitted: false }] })
    );

    expect(externalOutputs(built)).toEqual([]);
    expect(internalOutputs(built)).toHaveLength(1);
  });

  it("keeps an unrecognized output kind with its raw label", () => {
    const built = buildPublicationRecord(record({ outputs: [{ kind: "hologram" }] }));

    expect(built.outputs[0]?.kind).toBeNull();
    expect(built.outputs[0]?.label).toBe("hologram");
  });
});

describe("normalizePublicationOutputKind", () => {
  it("accepts common spellings", () => {
    expect(normalizePublicationOutputKind("PDF/A")).toBe("Pdf");
    expect(normalizePublicationOutputKind("xlsx")).toBe("Excel");
    expect(normalizePublicationOutputKind("data package")).toBe("DataPackage");
  });

  it("returns null rather than guessing", () => {
    expect(normalizePublicationOutputKind("csv")).toBeNull();
  });
});

describe("buildRestatementChain", () => {
  const v09 = buildPublicationRecord(record({ version: "09" }));
  const v10 = buildPublicationRecord(
    record({ version: "10", supersedesVersion: "09", restatementReason: "Late corporate action" })
  );
  const v11 = buildPublicationRecord(
    record({ version: "11", supersedesVersion: "10", restatementReason: "Pricing correction" })
  );

  it("orders versions oldest to newest along the supersede chain", () => {
    const chain = buildRestatementChain([v11, v09, v10]);

    expect(chain.ordered.map((entry) => entry.version)).toEqual(["09", "10", "11"]);
    expect(chain.current?.version).toBe("11");
    expect(chain.superseded.map((entry) => entry.version)).toEqual(["10", "09"]);
    expect(chain.isIntact).toBe(true);
  });

  it("reports a version superseding one that is not in the history", () => {
    const orphan = buildPublicationRecord(
      record({ version: "12", supersedesVersion: "99", restatementReason: "x" })
    );
    const chain = buildRestatementChain([v09, orphan]);

    expect(chain.isIntact).toBe(false);
    expect(chain.issues.map((issue) => issue.kind)).toContain("MultipleRoots");
    expect(chain.severity).toBe("action");
  });

  it("reports two versions superseding the same predecessor", () => {
    const fork = buildPublicationRecord(
      record({ version: "10b", supersedesVersion: "09", restatementReason: "y" })
    );
    const chain = buildRestatementChain([v09, v10, fork]);

    expect(chain.isIntact).toBe(false);
    expect(chain.issues.map((issue) => issue.kind)).toContain("DuplicateSupersede");
  });

  it("reports records the chain never reaches", () => {
    const detached = buildPublicationRecord(
      record({ version: "20", supersedesVersion: "19", restatementReason: "z" })
    );
    const chain = buildRestatementChain([v09, v10, detached]);

    expect(chain.isIntact).toBe(false);
    expect(chain.summary).toContain("not reachable");
  });

  it("does not loop forever on a cyclic chain", () => {
    const a = buildPublicationRecord(record({ version: "A", supersedesVersion: "B", restatementReason: "r" }));
    const b = buildPublicationRecord(record({ version: "B", supersedesVersion: "A", restatementReason: "r" }));
    const chain = buildRestatementChain([a, b]);

    expect(chain.issues.map((issue) => issue.kind)).toContain("Cycle");
    expect(chain.isIntact).toBe(false);
  });

  it("reports an empty history plainly", () => {
    const chain = buildRestatementChain([]);

    expect(chain.current).toBeNull();
    expect(chain.summary).toBe("No publication has been recorded.");
  });

  it("names a restatement as the version in force", () => {
    const chain = buildRestatementChain([v09, v10]);

    expect(chain.summary).toContain("v10 is in force as a restatement");
  });
});

describe("buildDraftStateMarking", () => {
  it("marks a published report as approved for distribution", () => {
    const marking = buildDraftStateMarking({
      workflowState: "Published",
      version: "09",
      periodLabel: "September 2026"
    });

    expect(marking.isApprovedForDistribution).toBe(true);
    expect(marking.stateLabel).toBe("PUBLISHED");
    expect(marking.distributionLine).toBe("September 2026 · Version 09");
  });

  it("marks an approved-but-unpublished report as draft", () => {
    const marking = buildDraftStateMarking({ workflowState: "Approved", version: "06" });

    expect(marking.isApprovedForDistribution).toBe(false);
    expect(marking.stateLabel).toBe("DRAFT");
    expect(marking.distributionLine).toBe("Not approved for distribution");
  });

  it("marks a superseded version as not for distribution", () => {
    const marking = buildDraftStateMarking({ workflowState: "Superseded", version: "09" });

    expect(marking.isApprovedForDistribution).toBe(false);
    expect(marking.stateLabel).toBe("SUPERSEDED");
    expect(marking.severity).toBe("action");
  });

  it("marks an unknown workflow state as draft", () => {
    const marking = buildDraftStateMarking({ workflowState: "something-else" });

    expect(marking.stateLabel).toBe("DRAFT");
    expect(marking.isApprovedForDistribution).toBe(false);
  });
});
