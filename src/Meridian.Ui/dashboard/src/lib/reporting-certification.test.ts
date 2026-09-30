import { describe, expect, it } from "vitest";
import {
  buildInheritedCertification,
  evaluateDatasetCertification,
  propagateCertificationToBlocks,
  type DatasetCertificationInput
} from "@/lib/reporting-certification";

function dataset(overrides: Partial<DatasetCertificationInput> = {}): DatasetCertificationInput {
  return {
    datasetId: "performance",
    datasetName: "Performance data",
    certifiedBy: "Investment Analytics",
    certifiedAtUtc: "2026-10-02T18:05:00Z",
    periodLabel: "Sep 2026",
    certifiedFingerprint: "perf-v4",
    currentFingerprint: "perf-v4",
    ...overrides
  };
}

describe("evaluateDatasetCertification", () => {
  it("certifies a dataset that still matches what was certified", () => {
    const model = evaluateDatasetCertification(dataset());

    expect(model.state).toBe("Certified");
    expect(model.severity).toBe("ready");
    expect(model.reason).toContain("Investment Analytics");
    expect(model.reason).toContain("Sep 2026");
  });

  it("invalidates the certification when the dataset moves", () => {
    const model = evaluateDatasetCertification(dataset({ currentFingerprint: "perf-v5" }));

    expect(model.state).toBe("Invalidated");
    expect(model.hasMoved).toBe(true);
    expect(model.severity).toBe("action");
    expect(model.label).toBe("Certification invalidated");
  });

  it("reports an uncertified dataset when nobody stands behind it", () => {
    const model = evaluateDatasetCertification(dataset({ certifiedBy: null }));

    expect(model.state).toBe("Uncertified");
    expect(model.severity).toBe("review");
  });

  it("reports a certification untied to the data as unverifiable", () => {
    const model = evaluateDatasetCertification(dataset({ certifiedFingerprint: null }));

    expect(model.state).toBe("Indeterminate");
    expect(model.severity).toBe("action");
  });

  it("certifies when the caller tracks no fingerprint at all", () => {
    const model = evaluateDatasetCertification(
      dataset({ certifiedFingerprint: null, currentFingerprint: null })
    );

    expect(model.state).toBe("Certified");
  });
});

describe("buildInheritedCertification", () => {
  const certified = evaluateDatasetCertification(dataset());
  const invalidated = evaluateDatasetCertification(
    dataset({ datasetId: "ledger", datasetName: "Ledger balances", currentFingerprint: "led-v9" })
  );
  const uncertified = evaluateDatasetCertification(
    dataset({ datasetId: "manual", datasetName: "Manual adjustments", certifiedBy: null })
  );

  it("inherits certification when every dataset is certified", () => {
    const model = buildInheritedCertification([certified]);

    expect(model.state).toBe("Certified");
    expect(model.isFullyCertified).toBe(true);
    expect(model.summary).toBe("All 1 dataset currently certified.");
  });

  it("takes the worst state, never an average", () => {
    const model = buildInheritedCertification([certified, certified, invalidated]);

    expect(model.state).toBe("Invalidated");
    expect(model.isFullyCertified).toBe(false);
    expect(model.invalidatedDatasets.map((entry) => entry.datasetName)).toEqual(["Ledger balances"]);
  });

  it("sorts the worst dataset first", () => {
    const model = buildInheritedCertification([certified, uncertified, invalidated]);

    expect(model.datasets.map((entry) => entry.state)).toEqual([
      "Invalidated",
      "Uncertified",
      "Certified"
    ]);
  });

  it("does not treat a report consuming nothing as certified", () => {
    const model = buildInheritedCertification([]);

    expect(model.state).toBe("Uncertified");
    expect(model.isFullyCertified).toBe(false);
    expect(model.summary).toBe("This report consumes no certified dataset.");
  });

  it("names the datasets that invalidated the report", () => {
    const model = buildInheritedCertification([certified, invalidated]);

    expect(model.summary).toContain("Ledger balances");
    expect(model.summary).toContain("no longer certified");
  });
});

describe("propagateCertificationToBlocks", () => {
  const certified = evaluateDatasetCertification(dataset());
  const invalidated = evaluateDatasetCertification(
    dataset({ datasetId: "ledger", datasetName: "Ledger balances", currentFingerprint: "led-v9" })
  );

  it("certifies a block whose datasets are all certified", () => {
    const [block] = propagateCertificationToBlocks(
      [{ blockId: "b1", blockLabel: "Investment value", datasetIds: ["performance"] }],
      [certified, invalidated]
    );

    expect(block?.state).toBe("Certified");
    expect(block?.offendingDatasetNames).toEqual([]);
  });

  it("invalidates a block that consumes an invalidated dataset", () => {
    const [block] = propagateCertificationToBlocks(
      [{ blockId: "b2", blockLabel: "Net income", datasetIds: ["performance", "ledger"] }],
      [certified, invalidated]
    );

    expect(block?.state).toBe("Invalidated");
    expect(block?.offendingDatasetNames).toEqual(["Ledger balances"]);
    expect(block?.severity).toBe("action");
  });

  it("treats a dataset it cannot see as unverifiable, not certified", () => {
    const [block] = propagateCertificationToBlocks(
      [{ blockId: "b3", blockLabel: "Fee accrual", datasetIds: ["performance", "unknown-set"] }],
      [certified]
    );

    expect(block?.state).toBe("Indeterminate");
    expect(block?.offendingDatasetNames).toEqual(["unknown-set"]);
  });

  it("does not certify a block that names no dataset", () => {
    const [block] = propagateCertificationToBlocks(
      [{ blockId: "b4", blockLabel: "Commentary", datasetIds: [] }],
      [certified]
    );

    expect(block?.state).toBe("Uncertified");
  });

  it("keeps the worst state when a block spans several problem datasets", () => {
    const uncertified = evaluateDatasetCertification(
      dataset({ datasetId: "manual", datasetName: "Manual adjustments", certifiedBy: null })
    );
    const [block] = propagateCertificationToBlocks(
      [{ blockId: "b5", blockLabel: "Total", datasetIds: ["manual", "ledger"] }],
      [uncertified, invalidated]
    );

    expect(block?.state).toBe("Invalidated");
    expect(block?.offendingDatasetNames).toEqual(["Manual adjustments", "Ledger balances"]);
  });
});
