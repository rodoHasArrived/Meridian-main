import { describe, expect, it } from "vitest";
import type { SecurityAssetClassDefinition, SecurityAssetTermFieldDefinition } from "@/lib/api/security-master-schema.api";
import {
  buildSchemaFieldGroups,
  buildSchemaValueEditor,
  validateSchemaFieldValue
} from "./security-passport-schema-fields.view-model";

function field(overrides: Partial<SecurityAssetTermFieldDefinition>): SecurityAssetTermFieldDefinition {
  return {
    key: "coupon",
    fieldPath: "assetSpecificTerms.coupon",
    label: "Coupon",
    type: "Decimal",
    editor: "Decimal",
    group: "Rates and conventions",
    order: 0,
    required: false,
    scalarEditable: true,
    choices: [],
    aliases: [],
    ...overrides
  };
}

const bond: SecurityAssetClassDefinition = {
  assetClass: "Bond",
  termsVersion: 1,
  lotUnit: "FaceValue",
  cashFlowBehavior: "ContractualSchedule",
  fields: [
    field({ key: "maturity", fieldPath: "assetSpecificTerms.maturity", label: "Maturity", editor: "Date", group: "Dates", order: 1, required: true }),
    field({ key: "coupon", order: 2 }),
    field({ key: "dayCount", fieldPath: "assetSpecificTerms.dayCount", label: "Day count", editor: "Convention", order: 3, choices: ["ACT/360", "30/360"] }),
    field({ key: "callSchedule", fieldPath: "assetSpecificTerms.callSchedule", label: "Call schedule", editor: "Json", group: "Schedules and structure", order: 4, scalarEditable: false })
  ]
};

describe("security passport schema fields", () => {
  it("groups scalar-editable terms in registry order and leaves blocks to the term-block route", () => {
    const groups = buildSchemaFieldGroups(bond);

    expect(groups.map((group) => group.group)).toEqual(["Dates", "Rates and conventions"]);
    expect(groups[1].fields.map((f) => f.key)).toEqual(["coupon", "dayCount"]);
    expect(groups.flatMap((g) => g.fields).some((f) => f.key === "callSchedule")).toBe(false);
    expect(buildSchemaFieldGroups(null)).toEqual([]);
  });

  it("picks a value control per editor kind", () => {
    expect(buildSchemaValueEditor(bond.fields[2])).toEqual({ input: "select", choices: ["ACT/360", "30/360"] });
    expect(buildSchemaValueEditor(field({ editor: "Toggle" })).choices).toEqual(["true", "false"]);
    expect(buildSchemaValueEditor(bond.fields[0]).input).toBe("date");
    expect(buildSchemaValueEditor(field({ editor: "Integer" })).step).toBe("1");
    expect(buildSchemaValueEditor(null).input).toBe("text");
  });

  it("pre-validates required values, numbers, dates and closed vocabularies", () => {
    expect(validateSchemaFieldValue(bond.fields[0], "")).toBe("Maturity is required.");
    expect(validateSchemaFieldValue(bond.fields[0], "2031-01-15")).toBeNull();
    expect(validateSchemaFieldValue(bond.fields[1], "abc")).toBe("Coupon must be a number.");
    expect(validateSchemaFieldValue(field({ editor: "Integer", label: "Lots" }), "1.5")).toBe("Lots must be a whole number.");
    expect(validateSchemaFieldValue(field({ editor: "Choice", label: "Kind", choices: ["A", "B"] }), "C")).toContain("one of: A, B");
    expect(validateSchemaFieldValue(null, "anything")).toBeNull();
  });
});
