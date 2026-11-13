export interface OptionScreenCriteria {
  underlyingSymbol: string; minDte: number; maxDte: number; minDelta: number; maxDelta: number;
  right: "Both" | "Call" | "Put"; minOpenInterest: number; minVolume: number;
  maxSpreadFraction: number | null; minPremium: number | null; premiumBasis: "Bid" | "Ask" | "Mid"; strikeRange: number;
}
export interface OptionScreenCandidate {
  id: string; symbol: string; underlyingSymbol: string; right: "Call" | "Put"; expiration: string;
  daysToExpiration: number; strike: string; delta: string | null; bid: string; ask: string;
  mid: string | null; spreadFraction: string | null; openInterest: number | null; volume: number | null;
  multiplier: string; currency: string; exchange: string; style: string; source: string;
  observedAt: string; passesScreen: boolean; rejectionReasons: string[];
}
export interface OptionScreenResult {
  criteria: OptionScreenCriteria; screenedAt: string; candidates: OptionScreenCandidate[];
  loadedExpirations: string[]; failedExpirations: string[]; omittedExpirations: string[];
  strikeRange: number; coverageNote: string; authorityNote: string;
}
export interface OptionBasketLeg {
  legId: string; instrumentType: "Option" | "Equity"; symbol: string; side: "Buy" | "Sell";
  positionIntent: "Open" | "Close"; ratio: string; currency: string;
  underlyingSymbol: string | null; right: "Call" | "Put" | null; expiration: string | null;
  strike: string | null; multiplier: string | null; exchange: string | null; deliverable: string | null;
}
export interface OptionAssessmentBasket {
  quantity: string; orderType: "Limit" | "Market"; priceEffect: "Debit" | "Credit" | "Even";
  limitPrice: string | null; currency: string; legs: OptionBasketLeg[];
}
export interface BrokerOptionAssessment {
  schemaVersion: "1"; fundAccountId: string; providerId: string; externalAccountId: string;
  assessedAt: string; brokerReference: string; outcome: "Approved" | "Conditional" | "Rejected" | "NotReported";
  rawBrokerStatus: string; basket: OptionAssessmentBasket;
  initialMarginBefore: string | null; initialMarginChange: string | null; initialMarginAfter: string | null;
  maintenanceMarginBefore: string | null; maintenanceMarginChange: string | null; maintenanceMarginAfter: string | null;
  excessLiquidityAfter: string | null; cashImpact: string | null;
  permissionEvidence: string | null; restrictionEvidence: string | null; warnings: string[];
  marginCurrency: string | null; cashImpactCurrency: string | null;
}
export interface OptionAssessmentImportRequest {
  assessment: BrokerOptionAssessment; sourceFileName: string; sourceContentBase64: string;
  sourceContentType: string; legsCsv: string | null; correctsAssessmentId: string | null;
}
export interface OptionAssessmentImportPreview {
  assessment: BrokerOptionAssessment; basketFingerprint: string; previewFingerprint: string;
  sourceSha256: string; sourceBytes: number; correctsAssessmentId: string | null; warnings: string[];
}
export interface RetainedOptionAssessment {
  assessmentId: string; assessment: BrokerOptionAssessment; basketFingerprint: string; contentSha256: string;
  recordedAt: string; recordedBy: string; sourceVaultId: string; sourceDocumentId: string; sourceSha256: string;
  assessmentVaultId: string; assessmentDocumentId: string; correctsAssessmentId: string | null; isSuperseded: boolean;
}
