import { describe, expect, it } from "vitest";

import { isWriteSelectableAssetProfile } from "./settings-profile-backed-security";
import type { SecurityAssetProfileDefinition, SecurityAssetProfileStatus } from "@/types";

function profileWith(
  status: SecurityAssetProfileStatus,
  effectiveFrom: string,
  effectiveTo: string | null
): SecurityAssetProfileDefinition {
  return {
    profileId: "custom-private-credit",
    version: 1,
    name: "Custom Private Credit",
    category: "PrivateCredit",
    subType: null,
    status,
    fields: [],
    identifierPreferences: [],
    lifecycleStates: [],
    accountingImpactHints: [],
    dateOrderRules: [],
    effectiveFrom,
    effectiveTo,
    approvedBy: "controller",
    approvedAtUtc: "2026-05-29T00:00:00Z",
    changeReason: "test",
    approvalReference: "AP-001"
  };
}

describe("isWriteSelectableAssetProfile", () => {
  const today = new Date("2026-08-13T12:00:00Z");

  it("keeps Approved versions selectable regardless of effective window", () => {
    expect(isWriteSelectableAssetProfile(profileWith("Approved", "2026-09-01", null), today)).toBe(true);
  });

  it("keeps a Superseded version selectable while its effective window still covers today", () => {
    // Governance marks the predecessor Superseded the moment a future-dated replacement is
    // approved; until that replacement activates, the predecessor is the only version
    // write-time validation accepts, so the creation form must keep offering it.
    expect(isWriteSelectableAssetProfile(profileWith("Superseded", "2026-05-29", "2026-08-31"), today)).toBe(true);
    expect(isWriteSelectableAssetProfile(profileWith("Superseded", "2026-05-29", null), today)).toBe(true);
  });

  it("hides a Superseded version whose effective window has closed", () => {
    expect(isWriteSelectableAssetProfile(profileWith("Superseded", "2026-05-29", "2026-08-12"), today)).toBe(false);
  });

  it("hides a Superseded version whose effective window has not opened", () => {
    expect(isWriteSelectableAssetProfile(profileWith("Superseded", "2026-08-14", null), today)).toBe(false);
  });

  it("hides Draft and Retired versions", () => {
    expect(isWriteSelectableAssetProfile(profileWith("Draft", "2026-05-29", null), today)).toBe(false);
    expect(isWriteSelectableAssetProfile(profileWith("Retired", "2026-05-29", null), today)).toBe(false);
  });
});
