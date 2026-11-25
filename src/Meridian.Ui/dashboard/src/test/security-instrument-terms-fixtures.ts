import type { SecurityInstrumentTermField, SecurityInstrumentTerms } from "@/types/security-instrument-terms";

export function instrumentTermField(overrides: Partial<SecurityInstrumentTermField> = {}): SecurityInstrumentTermField {
  return {
    path: "/assetSpecificTerms/couponRate",
    key: "couponRate",
    label: "Coupon rate",
    group: "Asset-specific terms",
    declaredType: "Decimal",
    jsonType: "Number",
    state: "Present",
    isDeclared: true,
    required: true,
    role: "Economic",
    valueText: "5.12345678901234567890",
    rawJson: "5.12345678901234567890",
    unit: "Percent",
    unitStatus: "Explicit",
    unitPath: "/assetSpecificTerms/couponRateUnit",
    validationMessage: null,
    children: [],
    ...overrides
  };
}

export function instrumentTermsFixture(overrides: Partial<SecurityInstrumentTerms> = {}): SecurityInstrumentTerms {
  return {
    securityId: "sec-bond",
    assetClass: "Bond",
    currency: "USD",
    version: 7,
    effectiveFrom: "2026-01-01",
    effectiveTo: null,
    retrievedAtUtc: "2026-11-21T10:00:00Z",
    status: "ReviewRequired",
    fingerprint: "a".repeat(64),
    termsFormat: "AssetSpecificTerms/v2",
    assetClassTermsVersion: 2,
    schema: {
      assetClass: "Bond",
      writeVersion: 2,
      readVersion: 2,
      lotUnit: "FaceAmount",
      cashFlowBehavior: "Contractual",
      profileId: null,
      profileVersion: null,
      profileStatus: null
    },
    source: {
      status: "Matched",
      summary: "Retained provider record for economic-definition v7.",
      provenanceJson: '{"sourceSystem":"Reference vendor","version":7}'
    },
    commonTermsJson: '{"currency":"USD"}',
    assetSpecificTermsJson:
      '{"couponRate":5.12345678901234567890,"couponSchedule":[{"cashAmount":9007199254740993.123456789}]}',
    fields: [
      instrumentTermField(),
      instrumentTermField({
        path: "/assetSpecificTerms/maturityDate",
        key: "maturityDate",
        label: "Maturity date",
        declaredType: "Date",
        jsonType: "String",
        valueText: "2040-06-30",
        rawJson: '"2040-06-30"',
        unit: null,
        unitStatus: "NotApplicable",
        unitPath: null
      }),
      instrumentTermField({
        path: "/assetSpecificTerms/paymentFrequency",
        key: "paymentFrequency",
        label: "Payment frequency",
        declaredType: "String",
        jsonType: "Undefined",
        state: "Missing",
        valueText: null,
        rawJson: null,
        unit: null,
        unitStatus: "NotApplicable",
        unitPath: null
      }),
      instrumentTermField({
        path: "/assetSpecificTerms/redemptionAmount",
        key: "redemptionAmount",
        label: "Redemption amount",
        jsonType: "Null",
        state: "Null",
        valueText: null,
        rawJson: "null",
        required: false,
        unit: null,
        unitStatus: "Unspecified",
        unitPath: null
      }),
      instrumentTermField({
        path: "/assetSpecificTerms/invalidRate",
        key: "invalidRate",
        label: "Invalid rate",
        jsonType: "String",
        state: "Invalid",
        valueText: "many",
        rawJson: '"many"',
        unit: null,
        unitStatus: "Unspecified",
        unitPath: null,
        validationMessage: "The retained value does not match the declared Decimal type."
      }),
      instrumentTermField({
        path: "/assetSpecificTerms/vendorFlag",
        key: "vendorFlag",
        label: "Vendor flag",
        declaredType: null,
        jsonType: "Boolean",
        isDeclared: false,
        required: false,
        role: "Operational",
        valueText: "false",
        rawJson: "false",
        unit: null,
        unitStatus: "NotApplicable",
        unitPath: null
      }),
      instrumentTermField({
        path: "/assetSpecificTerms/couponSchedule",
        key: "couponSchedule",
        label: "Coupon schedule",
        declaredType: "Array",
        jsonType: "Array",
        valueText: null,
        rawJson: '[{"cashAmount":9007199254740993.123456789}]',
        unit: null,
        unitStatus: "NotApplicable",
        unitPath: null,
        children: [
          instrumentTermField({
            path: "/assetSpecificTerms/couponSchedule/0",
            key: "0",
            label: "Payment 1",
            declaredType: "Object",
            jsonType: "Object",
            valueText: null,
            rawJson: '{"cashAmount":9007199254740993.123456789}',
            unit: null,
            unitStatus: "NotApplicable",
            unitPath: null,
            children: [
              instrumentTermField({
                path: "/assetSpecificTerms/couponSchedule/0/cashAmount",
                key: "cashAmount",
                label: "Cash amount",
                valueText: "9007199254740993.123456789",
                rawJson: "9007199254740993.123456789",
                unit: "USD",
                unitPath: "/commonTerms/currency"
              })
            ]
          })
        ]
      })
    ],
    diagnostics: [],
    ...overrides
  };
}
