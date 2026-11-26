/** Retained term values are text; decimal tokens must never pass through JavaScript Number. */
export type SecurityInstrumentTermState = "Present" | "Missing" | "Null" | "Invalid" | "Ambiguous";

export interface SecurityInstrumentTermField {
  path: string;
  key: string;
  label: string;
  group: string;
  declaredType: string | null;
  jsonType: string;
  state: SecurityInstrumentTermState;
  isDeclared: boolean;
  required: boolean;
  role: string | null;
  valueText: string | null;
  rawJson: string | null;
  unit: string | null;
  unitStatus: "Explicit" | "Unspecified" | "NotApplicable";
  unitPath: string | null;
  validationMessage: string | null;
  children: SecurityInstrumentTermField[];
}

export interface SecurityInstrumentTerms {
  securityId: string;
  assetClass: string;
  currency: string;
  version: number;
  effectiveFrom: string | null;
  effectiveTo: string | null;
  retrievedAtUtc: string;
  status: "Ready" | "ReviewRequired" | "Unsupported";
  fingerprint: string;
  termsFormat: string;
  assetClassTermsVersion: number;
  schema: {
    assetClass: string;
    writeVersion: number;
    readVersion: number;
    lotUnit: string | null;
    cashFlowBehavior: string | null;
    profileId: string | null;
    profileVersion: number | null;
    profileStatus: string | null;
  };
  source: {
    status: "Matched" | "Unavailable" | "VersionMismatch";
    summary: string;
    provenanceJson: string | null;
  };
  commonTermsJson: string | null;
  assetSpecificTermsJson: string | null;
  fields: SecurityInstrumentTermField[];
  diagnostics: string[];
}
