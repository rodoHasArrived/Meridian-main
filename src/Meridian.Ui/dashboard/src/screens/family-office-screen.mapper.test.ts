import { describe, expect, it } from "vitest";
import { mapFamilyOfficeOverview } from "@/screens/family-office-screen.mapper";
import {
  FAMILY_OFFICE_FIXTURE_IDS,
  buildFamilyOfficeOverviewFixture
} from "@/screens/family-office-screen.mapper.test-fixture";
import { buildFamilyOfficeScreenViewModel } from "@/screens/family-office-screen.view-model";

describe("mapFamilyOfficeOverview", () => {
  it("keeps the not-connected state when the server reports no family structure", () => {
    expect(mapFamilyOfficeOverview(null)).toBeNull();
    expect(mapFamilyOfficeOverview(buildFamilyOfficeOverviewFixture({ entities: [] }))).toBeNull();
  });

  it("takes consolidated figures from the server balance sheet rather than re-adding the parts", () => {
    const structure = mapFamilyOfficeOverview(buildFamilyOfficeOverviewFixture());
    const root = structure?.entities.find((entity) => entity.entityId === FAMILY_OFFICE_FIXTURE_IDS.root);

    // Account equity plus private marks sums to 79M; the balance sheet reports 85M net worth
    // because it also carries holdings the account list does not. The server number wins.
    expect(root?.netWorth).toBe(85_000_000);
    expect(root?.cash).toBe(9_500_000);
    expect(root?.liabilities).toBe(7_000_000);
    expect(root?.unfundedCommitments).toBe(4_250_000);
    expect(root?.reconciliationBreakCount).toBe(3);
  });

  it("rolls child accounts, marks, and commitments up to the entity that owns them", () => {
    const structure = mapFamilyOfficeOverview(buildFamilyOfficeOverviewFixture());
    const trust = structure?.entities.find((entity) => entity.entityId === FAMILY_OFFICE_FIXTURE_IDS.trust);
    const llc = structure?.entities.find((entity) => entity.entityId === FAMILY_OFFICE_FIXTURE_IDS.llc);

    expect(trust?.cash).toBe(4_500_000);
    expect(trust?.privateAssetValue).toBe(26_000_000);
    expect(trust?.netWorth).toBe(57_000_000);
    expect(trust?.unfundedCommitments).toBe(4_250_000);
    expect(trust?.reconciliationBreakCount).toBe(0);

    expect(llc?.cash).toBe(5_000_000);
    expect(llc?.privateAssetValue).toBe(0);
    expect(llc?.reconciliationBreakCount).toBe(1);
  });

  it("reports a missing ownership percentage as unmapped instead of zero", () => {
    const structure = mapFamilyOfficeOverview(buildFamilyOfficeOverviewFixture());
    const llc = structure?.entities.find((entity) => entity.entityId === FAMILY_OFFICE_FIXTURE_IDS.llc);
    expect(llc?.ownershipPercent).toBeNull();

    const vm = buildFamilyOfficeScreenViewModel(FAMILY_OFFICE_FIXTURE_IDS.llc, structure);
    expect(vm.ownershipGraph.nodes.find((node) => node.id === FAMILY_OFFICE_FIXTURE_IDS.llc)?.percentage)
      .toBe("Unmapped");
  });

  it("escalates a private mark without complete valuation evidence", () => {
    const structure = mapFamilyOfficeOverview(buildFamilyOfficeOverviewFixture());

    expect(structure?.privateAssets).toEqual([
      expect.objectContaining({ assetId: "private-fund-vii", valuationStatus: "missing-evidence" })
    ]);
    expect(structure?.entities.find((entity) => entity.entityId === FAMILY_OFFICE_FIXTURE_IDS.trust)
      ?.staleValuationWarningCount).toBe(1);
  });

  it("rolls the public asset register up into asset-class exposures, largest first", () => {
    const structure = mapFamilyOfficeOverview(buildFamilyOfficeOverviewFixture());

    expect(structure?.assetClassExposures).toEqual([
      { assetClass: "Public equity", value: 30_000_000 },
      { assetClass: "Fixed income", value: 20_000_000 }
    ]);
  });

  it("synthesizes a consolidated root when the graph exposes no family-office entity", () => {
    const overview = buildFamilyOfficeOverviewFixture();
    const structure = mapFamilyOfficeOverview({
      ...overview,
      entities: overview.entities.filter((entity) => entity.entityId !== FAMILY_OFFICE_FIXTURE_IDS.root)
    });
    const root = structure?.entities.find((entity) => entity.entityId === FAMILY_OFFICE_FIXTURE_IDS.root);

    expect(root).toMatchObject({ displayName: "Ridgeline Family Office", netWorth: 85_000_000, liabilities: 7_000_000 });
    expect(structure?.entities).toHaveLength(3);
    // Counted once from the asset register — summing per-entity counts would double
    // count, because each entity's count already includes its descendants.
    expect(root?.staleValuationWarningCount).toBe(1);
  });

  it("terminates on a cyclic ownership link instead of recursing forever", () => {
    const overview = buildFamilyOfficeOverviewFixture();
    const structure = mapFamilyOfficeOverview({
      ...overview,
      entities: overview.entities.map((entity) =>
        entity.entityId === FAMILY_OFFICE_FIXTURE_IDS.root
          ? { ...entity, parentEntityId: FAMILY_OFFICE_FIXTURE_IDS.trust }
          : entity)
    });

    expect(structure?.entities).toHaveLength(3);
  });
});
