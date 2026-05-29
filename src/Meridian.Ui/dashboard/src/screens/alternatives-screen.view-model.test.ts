import { describe, expect, it } from "vitest";
import { buildAlternativesScreenViewModel } from "@/screens/alternatives-screen.view-model";

const requiredWarnings = [
  "manual valuation",
  "statement imported",
  "unreconciled",
  "missing source document",
  "stale NAV"
];

describe("buildAlternativesScreenViewModel", () => {
  it("keeps alternatives route metadata and fixture/import policy in the view model seam", () => {
    const vm = buildAlternativesScreenViewModel();

    expect(vm.route).toMatchObject({
      path: "/portfolio/alternatives",
      workspaceLabel: "Portfolio",
      label: "Alternatives",
      disabledReason: null
    });
    expect(vm.route.dataSourcePolicy).toContain("fixtures or statement imports");
    expect(vm.statusChips.map((chip) => chip.value)).toContain("/portfolio/alternatives");
    expect(vm.privateAssets.map((asset) => asset.source)).toEqual(expect.arrayContaining(["fixture", "import"]));
  });

  it("surfaces explicit operator warning labels across private asset records", () => {
    const vm = buildAlternativesScreenViewModel("harbor-re-dev");
    const labels = new Set(vm.privateAssets.flatMap((asset) => asset.warnings.map((warning) => warning.label)));

    expect(vm.explicitWarnings).toEqual(requiredWarnings);
    for (const label of requiredWarnings) {
      expect(labels.has(label as (typeof vm.explicitWarnings)[number]) || vm.explicitWarnings.includes(label as (typeof vm.explicitWarnings)[number])).toBe(true);
    }
    expect(vm.privateAssets.every((asset) => asset.selectAriaLabel.includes(asset.name))).toBe(true);
    expect(vm.selectedAsset.id).toBe("harbor-re-dev");
    expect(vm.selectedDetail.valuationHistory.some((point) => point.warningLabel === "stale NAV")).toBe(true);
    expect(vm.selectedDetail.documents.some((document) => document.warningLabel === "missing source document")).toBe(true);
  });

  it("falls back to the first private asset when selection is missing", () => {
    const vm = buildAlternativesScreenViewModel("missing");

    expect(vm.selectedAsset.id).toBe("aurora-growth-v");
    expect(vm.selectedDetail.commitmentSummary.map((item) => item.label)).toEqual(["Committed", "Called", "Unfunded"]);
    expect(vm.detailDrawerDescription).toContain("Capital activity");
  });
});
