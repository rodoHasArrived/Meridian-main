import { getJson, type ApiRequestOptions } from "@/lib/api";
import { isTaxDecimalText } from "@/lib/ledger-tax-results-format";

export interface PostedComparisonRange {
  periodId: string; label: string; periodStart: string; periodEnd: string;
  rangeStart: string; rangeEnd: string; isPartial: boolean; status: string;
}
export interface PostedComparisonLine {
  accountKey: string; accountName: string; accountType: string; symbol: string | null;
  financialAccountId: string | null;
  currentDebit: string; currentCredit: string; currentBalance: string;
  priorDebit: string; priorCredit: string; priorBalance: string;
  change: string; percentChange: string | null;
  dimensions: Record<string, unknown> | null;
}
export interface PostedPeriodComparison {
  status: "Ready" | "Unavailable" | "Incompatible";
  reason: string | null; generatedAt: string; asOf: string;
  ledgerBookId: string; fundProfileId: string; bookName: string;
  accountingBasis: string; currency: string; accountingPolicyId: string; accountingPolicyVersion: string;
  current: PostedComparisonRange; baseline: PostedComparisonRange | null;
  lines: PostedComparisonLine[]; currentJournalIds: string[]; baselineJournalIds: string[];
  populationHash: string; dimensions: Record<string, unknown> | null;
}
export interface PostedComparisonQuery {
  bookId: string; periodId: string; baselinePeriodId?: string | null; asOf?: string | null;
  accountingBasis?: string;
  dimensions?: Record<string, string>;
}

export async function getPostedPeriodComparison(query: PostedComparisonQuery, options: ApiRequestOptions = {}): Promise<PostedPeriodComparison> {
  const params = new URLSearchParams(query.dimensions);
  if (query.baselinePeriodId) params.set("baselinePeriodId", query.baselinePeriodId);
  if (query.asOf) params.set("asOf", query.asOf);
  if (query.accountingBasis) params.set("accountingBasis", query.accountingBasis);
  const suffix = params.toString();
  const result = await getJson<PostedPeriodComparison>(`/api/ledger/books/${encodeURIComponent(query.bookId)}/periods/${encodeURIComponent(query.periodId)}/comparison${suffix ? `?${suffix}` : ""}`, { ...options, allowDevelopmentFallback: false });
  const equalId = (left: string | undefined, right: string) => typeof left === "string" && left.toLowerCase() === right.toLowerCase();
  if (!result || !equalId(result.ledgerBookId, query.bookId) || !equalId(result.current?.periodId, query.periodId)
    || !["Ready", "Unavailable", "Incompatible"].includes(result.status)
    || (query.baselinePeriodId && result.baseline && !equalId(result.baseline.periodId, query.baselinePeriodId))) {
    throw new Error("The comparison response does not match the selected posted book and periods.");
  }
  if (query.accountingBasis && (typeof result.accountingBasis !== "string" || result.accountingBasis.toLowerCase() !== query.accountingBasis.toLowerCase())) {
    throw new Error("The comparison response does not match the selected accounting basis.");
  }
  if (query.asOf && (!Number.isFinite(Date.parse(query.asOf)) || Date.parse(result.asOf) !== Date.parse(query.asOf))) {
    throw new Error("The comparison response does not match the selected as-of cutoff.");
  }
  if (result.status === "Ready" && (!result.baseline || !result.currency || !result.populationHash || !Array.isArray(result.currentJournalIds) || !Array.isArray(result.baselineJournalIds) || !Array.isArray(result.lines)
    || result.lines.some((line) => ![line.currentDebit, line.currentCredit, line.currentBalance, line.priorDebit, line.priorCredit, line.priorBalance, line.change].every(isTaxDecimalText)
      || (line.percentChange !== null && !isTaxDecimalText(line.percentChange))))) {
    throw new Error("The comparison is missing exact amount or retained population evidence.");
  }
  if (result.status === "Ready" && Object.entries(query.dimensions ?? {}).some(([key, expected]) => {
    if (key === "externalGlDimensionValue") return !query.dimensions?.externalGlDimensionKey;
    const canonicalKey = key === "fundProfileId" ? "fundId" : key === "ledgerBookDimensionId" ? "bookId" : key.startsWith("dimension") && key.length > 9 ? key[9].toLowerCase() + key.slice(10) : key;
    const pairKey = key === "externalGlDimensionKey" ? expected : null;
    const actual = key.startsWith("externalGl.")
      ? (result.dimensions?.externalGlDimensions as Record<string, unknown> | undefined)?.[key.slice(11)]
      : pairKey ? (result.dimensions?.externalGlDimensions as Record<string, unknown> | undefined)?.[pairKey]
      : result.dimensions?.[canonicalKey];
    const expectedValue = pairKey ? query.dimensions?.externalGlDimensionValue : expected;
    return typeof actual !== "string" || typeof expectedValue !== "string" || actual.toLowerCase() !== expectedValue.trim().toLowerCase();
  })) {
    throw new Error("The comparison response does not match the selected dimension scope.");
  }
  return result;
}
