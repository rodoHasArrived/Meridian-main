import type {
  BrokerOptionAssessment, OptionAssessmentBasket, OptionBasketLeg, OptionScreenCandidate, OptionScreenCriteria
} from "@/types/option-visualization.types";

export const DEFAULT_OPTION_CRITERIA: OptionScreenCriteria = {
  underlyingSymbol: "", minDte: 0, maxDte: 365, minDelta: -1, maxDelta: 1, right: "Both",
  minOpenInterest: 0, minVolume: 0, maxSpreadFraction: null, minPremium: null, premiumBasis: "Bid", strikeRange: 20
};
export const EMPTY_OPTION_BASKET: OptionAssessmentBasket = {
  quantity: "1", orderType: "Limit", priceEffect: "Debit", limitPrice: "", currency: "USD", legs: []
};
export const OPTION_LEGS_CSV_HEADER = "legId,instrumentType,symbol,side,positionIntent,ratio,currency,underlyingSymbol,right,expiration,strike,multiplier,exchange,deliverable";

export function candidateLeg(candidate: OptionScreenCandidate, legId: string): OptionBasketLeg {
  return { legId, instrumentType: "Option", symbol: candidate.symbol, side: "Buy", positionIntent: "Open", ratio: "1",
    currency: candidate.currency, underlyingSymbol: candidate.underlyingSymbol, right: candidate.right,
    expiration: candidate.expiration, strike: candidate.strike, multiplier: candidate.multiplier,
    exchange: candidate.exchange, deliverable: "" };
}
export function newEquityLeg(legId: string): OptionBasketLeg {
  return { legId, instrumentType: "Equity", symbol: "", side: "Buy", positionIntent: "Open", ratio: "100",
    currency: "USD", underlyingSymbol: null, right: null, expiration: null, strike: null,
    multiplier: null, exchange: null, deliverable: null };
}
export function emptyAssessment(account: string, basket: OptionAssessmentBasket): BrokerOptionAssessment {
  return { schemaVersion: "1", fundAccountId: account, providerId: "", externalAccountId: "", assessedAt: "",
    brokerReference: "", outcome: "NotReported", rawBrokerStatus: "", basket,
    initialMarginBefore: null, initialMarginChange: null, initialMarginAfter: null,
    maintenanceMarginBefore: null, maintenanceMarginChange: null, maintenanceMarginAfter: null,
    excessLiquidityAfter: null, cashImpact: null, permissionEvidence: null, restrictionEvidence: null, warnings: [],
    marginCurrency: null, cashImpactCurrency: null };
}
// Decimal matching is textual and exact. Floats are used only for screen coordinates, never basket authority.
export function normalizeOptionDecimal(value: string | null): string | null {
  if (value === null) return null;
  const match = /^(-?)(\d+)(?:\.(\d+))?$/.exec(value.trim());
  if (!match) return value;
  const whole = match[2].replace(/^0+(?=\d)/, ""); const fraction = (match[3] ?? "").replace(/0+$/, "");
  return `${match[1] && (whole !== "0" || fraction) ? "-" : ""}${whole}${fraction ? `.${fraction}` : ""}`;
}
export function comparableBasket(basket: OptionAssessmentBasket): string {
  return JSON.stringify({ quantity: normalizeOptionDecimal(basket.quantity), orderType: basket.orderType,
    priceEffect: basket.priceEffect, limitPrice: normalizeOptionDecimal(basket.limitPrice), currency: basket.currency.trim().toUpperCase(),
    legs: basket.legs.map(leg => ({ legId: leg.legId.trim(), instrumentType: leg.instrumentType, symbol: leg.symbol.trim(),
      side: leg.side, positionIntent: leg.positionIntent, right: leg.right, expiration: leg.expiration,
      ratio: normalizeOptionDecimal(leg.ratio), strike: normalizeOptionDecimal(leg.strike), multiplier: normalizeOptionDecimal(leg.multiplier),
      currency: leg.currency.trim().toUpperCase(), exchange: leg.exchange?.trim() || null,
      underlyingSymbol: leg.underlyingSymbol?.trim() || null, deliverable: leg.deliverable?.trim() || null
    })).sort((a, b) => a.legId.localeCompare(b.legId)) });
}

export function optionCoincidentGroups(candidates: OptionScreenCandidate[]) {
  const groups = new Map<string, OptionScreenCandidate[]>();
  for (const candidate of candidates) {
    if (candidate.delta === null || !Number.isFinite(Number(candidate.delta))) continue;
    const key = `${candidate.daysToExpiration}:${normalizeOptionDecimal(candidate.delta)}`;
    const group = groups.get(key); if (group) group.push(candidate); else groups.set(key, [candidate]);
  }
  return [...groups.values()];
}
