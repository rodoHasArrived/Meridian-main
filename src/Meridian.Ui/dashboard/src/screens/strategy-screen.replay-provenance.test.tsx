import { render, screen, within } from "@testing-library/react";
import { StrategyReplayProvenance } from "./strategy-screen.replay-provenance";
import type { StrategyRunReplayProvenance } from "@/types";

const provenance: StrategyRunReplayProvenance = { sourceMix: [], revisions: [], completedInputHashSha256: null };

it("keeps absent legacy assessments distinct from assessed inputs without warnings", () => {
  const { rerender } = render(<StrategyReplayProvenance evidence={provenance} receipt={null} loading={false} />);
  const assessment = screen.getByRole("region", { name: "Replay data-quality assessment" });
  expect(assessment).toHaveTextContent("status remain unknown");
  rerender(<StrategyReplayProvenance evidence={{ ...provenance, dataQualityWarnings: [] }} receipt={null} loading={false} />);
  expect(assessment).toHaveTextContent("No source-quality warnings were reported");
  expect(assessment).not.toHaveTextContent("status remain unknown");
});

it("preserves warning kinds, affected fills and the retained assessment scope", () => {
  render(<StrategyReplayProvenance evidence={{ ...provenance, dataQualityWarnings: [
    { kind: "Repaired", message: "An acquisition used repaired prices.", affectedFillCount: 2, evidence: [{
      providerId: "synthetic-provider", eventReference: "bar:2026-11-20", observedAt: "2026-11-20T00:00:00Z",
      datasetId: "dataset-1", backfillRevisionId: "revision-1", contentHash: "hash-1", repaired: true, gapFilled: null,
      repairReceiptId: "repair-1", qualityScore: 0.7, qualityThreshold: 0.8, qualityAssessedAt: "2026-11-20T12:00:00Z",
      assessmentFrom: "2026-11-01", assessmentTo: "2026-11-20"
    }] },
    { kind: "Unknown", message: "Some source assessments were not retained.", affectedFillCount: 1, evidence: [] }
  ] }} receipt={null} loading={false} />);
  const assessment = screen.getByRole("region", { name: "Replay data-quality assessment" });
  expect(within(assessment).getByText("Repaired data")).toBeInTheDocument();
  expect(within(assessment).getByText("Unknown source quality")).toBeInTheDocument();
  expect(assessment).toHaveTextContent("Affected fills: 2");
  expect(assessment).toHaveTextContent("Assessment range: 2026-11-01 to 2026-11-20");
  expect(assessment).toHaveTextContent("Gap-filled: Unknown");
  expect(assessment).toHaveTextContent("Retained threshold: 0.8");
  expect(assessment).toHaveTextContent("repair-1");
});
