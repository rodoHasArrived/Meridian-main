import { describe, expect, it } from "vitest";

import { buildProfileFieldPayload, isWriteSelectableAssetProfile } from "./settings-profile-backed-security";
import type {
  SecurityAssetProfileDefinition,
  SecurityAssetProfileFieldDefinition,
  SecurityAssetProfileStatus
} from "@/types";

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

  it("keeps a currently effective Approved version selectable", () => {
    expect(isWriteSelectableAssetProfile(profileWith("Approved", "2026-05-29", null), today)).toBe(true);
    expect(isWriteSelectableAssetProfile(profileWith("Approved", "2026-08-13", null), today)).toBe(true);
  });

  it("hides an Approved version whose effective window has not opened", () => {
    // A freshly approved replacement with a future effectiveFrom cannot back a write today -
    // write-time governance rejects it - so the form must not advertise it yet.
    expect(isWriteSelectableAssetProfile(profileWith("Approved", "2026-09-01", null), today)).toBe(false);
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

function numericField(
  key: string,
  fieldType: "Decimal" | "Integer"
): SecurityAssetProfileFieldDefinition {
  return {
    key,
    label: key,
    fieldType,
    isRequired: true,
    allowedValues: [],
    description: null,
    minValue: null,
    maxValue: null,
    isProjected: false,
    isSearchable: false
  };
}

describe("buildProfileFieldPayload numeric precision", () => {
  it("accepts values that round-trip exactly through Number", () => {
    const fields = [numericField("commitment", "Decimal"), numericField("vintage", "Integer")];
    const { payload, invalidFields } = buildProfileFieldPayload(fields, {
      commitment: "1000000.50",
      vintage: "2024"
    });

    expect(invalidFields).toEqual([]);
    expect(payload.commitment).toBe(1000000.5);
    expect(payload.vintage).toBe(2024);
  });

  it("rejects a decimal the IEEE double would silently round", () => {
    // The server contract is .NET decimal: submitting 123456789.123456789 through a JS Number
    // would persist 123456789.12345679 - different economics than the operator entered.
    const fields = [numericField("commitment", "Decimal")];
    const { payload, invalidFields } = buildProfileFieldPayload(fields, {
      commitment: "123456789.123456789"
    });

    expect(payload).not.toHaveProperty("commitment");
    expect(invalidFields).toHaveLength(1);
    expect(invalidFields[0]).toContain("commitment");
  });

  it("rejects an integer beyond exact double precision", () => {
    // 9007199254740993 parses to 9007199254740992 - still an integer, silently off by one.
    const fields = [numericField("originalFace", "Integer")];
    const { payload, invalidFields } = buildProfileFieldPayload(fields, {
      originalFace: "9007199254740993"
    });

    expect(payload).not.toHaveProperty("originalFace");
    expect(invalidFields).toHaveLength(1);
  });

  it("accepts small exact decimals that serialize in exponent form", () => {
    // String(1e-7) is "1e-7", but the double holds exactly 0.0000001 and .NET decimal parses the
    // serialized exponent form back exactly - the comparison is numeric fidelity, not spelling.
    const fields = [numericField("rate", "Decimal")];
    const { payload, invalidFields } = buildProfileFieldPayload(fields, {
      rate: "0.0000001"
    });

    expect(invalidFields).toEqual([]);
    expect(payload.rate).toBe(1e-7);
  });

  it("accepts harmless textual normalization like trailing fractional zeros", () => {
    const fields = [numericField("ownershipPercent", "Decimal")];
    const { payload, invalidFields } = buildProfileFieldPayload(fields, {
      ownershipPercent: "12.50"
    });

    expect(invalidFields).toEqual([]);
    expect(payload.ownershipPercent).toBe(12.5);
  });
});
