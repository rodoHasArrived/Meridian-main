/**
 * Pure form rules for proposing a governed historical identifier window from the passport editor.
 *
 * Imports stamp identifier validity with the ingest time, so an identifier the security carried
 * before ingest is staged here as a Draft that an independent reviewer approves. These checks mirror
 * the server's proposal invariants so the operator sees why a draft would be refused; the server
 * re-validates every field and remains the authority.
 */

import type { ProposeHistoricalIdentifierWindowInput } from "@/lib/api/security-master-workbench.api";

/** Identifier kinds an operator can propose a window for (SecurityIdentifierKind, minus Unknown). */
export const HISTORICAL_WINDOW_IDENTIFIER_KINDS = [
  "Ticker",
  "Isin",
  "Cusip",
  "Sedol",
  "Figi",
  "OccOptionSymbol",
  "ProviderSymbol",
  "InternalCode",
  "Lei",
  "PermId",
  "Bbgid",
  "Wkn",
  "Valoren",
  "PermTicker",
  "Ric",
  "Cik",
  "LoanX",
  "Clip",
  "ListingId",
  "Uti",
  "Upi",
  "MarkitRedEntity",
  "MarkitRedPair"
] as const;

const IDENTIFIER_KIND_LABELS: Record<(typeof HISTORICAL_WINDOW_IDENTIFIER_KINDS)[number], string> = {
  Ticker: "Ticker",
  Isin: "ISIN",
  Cusip: "CUSIP",
  Sedol: "SEDOL",
  Figi: "FIGI",
  OccOptionSymbol: "OCC option symbol",
  ProviderSymbol: "Provider symbol",
  InternalCode: "Internal code",
  Lei: "LEI",
  PermId: "PermID",
  Bbgid: "BBGID",
  Wkn: "WKN",
  Valoren: "Valoren",
  PermTicker: "Perm ticker",
  Ric: "RIC",
  Cik: "CIK",
  LoanX: "LoanX ID",
  Clip: "CLIP",
  ListingId: "Listing ID",
  Uti: "UTI",
  Upi: "UPI",
  MarkitRedEntity: "Markit RED entity",
  MarkitRedPair: "Markit RED pair"
};

/** Operator-facing label for an identifier kind; the request still sends the enum value. */
export function historicalWindowKindLabel(kind: string): string {
  return (IDENTIFIER_KIND_LABELS as Record<string, string>)[kind] ?? kind;
}

export interface HistoricalWindowDraft {
  kind: string;
  value: string;
  provider: string;
  /** Date-only `YYYY-MM-DD` from an `<input type="date">`. */
  validFrom: string;
  /** Date-only `YYYY-MM-DD`, or empty for an open-ended window. */
  validTo: string;
  evidenceReference: string;
  justification: string;
}

export const EMPTY_HISTORICAL_WINDOW_DRAFT: HistoricalWindowDraft = {
  kind: "Ticker",
  value: "",
  provider: "",
  validFrom: "",
  validTo: "",
  evidenceReference: "",
  justification: ""
};

export interface HistoricalWindowValidation {
  valid: boolean;
  /** The first unmet rule, phrased for the operator; null when valid. */
  reason: string | null;
}

/** True when the kind needs a provider namespace (the server refuses a ProviderSymbol without one). */
export function historicalWindowRequiresProvider(kind: string): boolean {
  return kind === "ProviderSymbol";
}

/**
 * Validates a draft against the proposal rules. `today` is a `YYYY-MM-DD` date in UTC; the window
 * must start before it.
 */
export function validateHistoricalWindowDraft(draft: HistoricalWindowDraft, today: string): HistoricalWindowValidation {
  const fail = (reason: string): HistoricalWindowValidation => ({ valid: false, reason });

  if (!(HISTORICAL_WINDOW_IDENTIFIER_KINDS as readonly string[]).includes(draft.kind)) {
    return fail("Choose an identifier kind.");
  }
  if (draft.value.trim().length === 0) {
    return fail("Enter the identifier value.");
  }
  if (historicalWindowRequiresProvider(draft.kind) && draft.provider.trim().length === 0) {
    return fail("A provider symbol window must name its provider.");
  }
  if (draft.validFrom.length === 0) {
    return fail("Enter the date the identifier became valid.");
  }
  if (draft.validFrom >= today) {
    return fail("A historical window must start before today.");
  }
  if (draft.validTo.length > 0 && draft.validTo <= draft.validFrom) {
    return fail("The end date must be after the start date.");
  }
  if (draft.evidenceReference.trim().length === 0) {
    return fail("Cite the Evidence Vault reference that supports this window.");
  }
  if (draft.justification.trim().length === 0) {
    return fail("Explain why this identifier history is correct.");
  }
  return { valid: true, reason: null };
}

/** Pins a date-only value to UTC midnight so the server never shifts it across the operator's timezone. */
function toUtcInstant(date: string): string {
  return `${date}T00:00:00Z`;
}

/** Builds the request body for a draft that has passed {@link validateHistoricalWindowDraft}. */
export function buildHistoricalWindowInput(
  draft: HistoricalWindowDraft,
  expectedVersion: number,
  fundProfileId: string | null
): ProposeHistoricalIdentifierWindowInput {
  return {
    expectedVersion,
    kind: draft.kind,
    value: draft.value.trim(),
    provider: draft.provider.trim().length > 0 ? draft.provider.trim() : null,
    validFrom: toUtcInstant(draft.validFrom),
    validTo: draft.validTo.length > 0 ? toUtcInstant(draft.validTo) : null,
    evidenceReference: draft.evidenceReference.trim(),
    justification: draft.justification.trim(),
    fundProfileId
  };
}

/** Today's date as `YYYY-MM-DD` in UTC, matching how the request pins dates. */
export function utcToday(now: Date = new Date()): string {
  return now.toISOString().slice(0, 10);
}
