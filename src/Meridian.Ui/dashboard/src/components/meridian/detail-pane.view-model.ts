// Rules for the key-value list inside a DetailPane. View models already hand screens
// preformatted `{ label, value, tone }` rows; this module decides how each value is typeset
// (numbers and identifiers in the data face, dates on one line, prose in the body face) and
// which rows belong behind "Technical details" instead of in the reading path.
import { isOpaqueIdentifier } from "@/lib/identifiers";

export type DetailFieldTone = "default" | "success" | "warning" | "danger" | "muted";

/**
 * How a value is typeset:
 *  - `text`: a phrase or status; body face, wraps at word boundaries only.
 *  - `number`: a figure; data face, one line.
 *  - `date`: a date, time or age; one line. Numeric forms ("2026-06-30", "09:42:00 ET") use the
 *    data face, written-out ones ("Jun 30, 2026") the body face.
 *  - `code`: an identifier an operator reads ("PO-1", "brk-2026-0042"); data face.
 *  - `id`: an opaque identifier (GUID, digest); kept out of the reading path.
 */
export type DetailFieldKind = "text" | "number" | "date" | "code" | "id";

export interface DetailField {
  label: string;
  value: string;
  tone?: DetailFieldTone;
  /** Overrides the kind inferred from the value. */
  kind?: DetailFieldKind;
  /** Supporting sentence under the value. */
  detail?: string;
  /** Forces the row into the "Technical details" disclosure (opaque identifiers go there anyway). */
  technical?: boolean;
}

export interface ResolvedDetailField extends DetailField {
  kind: DetailFieldKind;
  tone: DetailFieldTone;
}

// "1,250", "-$4,118.22", "(1,234.00)", "42.5%", "+3.1 bps", "1.2M", "1,234.56 USD".
const NUMERIC = /^[+\-−(]?\s*(?:[A-Z]{3}\s)?[$€£¥]?\s*\d[\d,]*(?:\.\d+)?\s*(?:%|bps?|x|[KMB])?(?:\s[A-Z]{3})?\)?$/;
const ISO_DATE = /^\d{4}-\d{2}-\d{2}(?:[T ]\d{2}:\d{2}(?::\d{2})?(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?)?$/;
const CLOCK_TIME = /^\d{1,2}:\d{2}(?::\d{2})?(?:\s?(?:AM|PM))?(?:\s[A-Z]{2,4})?$/i;
const MONTH_DATE = /^(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.? \d{1,2}(?:, \d{4})?(?:,? \d{1,2}:\d{2}(?::\d{2})?(?:\s?(?:AM|PM))?(?:\s[A-Z]{2,4})?)?$/;
const RELATIVE_AGE = /^\d+\s?(?:s|m|h|d|w|mo|y)\s?ago$/i;
// Content hashes and digests: no operator reads these, but support does copy them.
const LONG_HEX = /^(?:sha(?:1|256|512)[:-])?[0-9a-f]{32,}$/i;
// A single token that carries a digit: "PO-1", "brk-2026-0042", "ACC_1001", "je-cash-1".
const READABLE_CODE = /^(?=[^\s]*\d)[A-Za-z0-9][A-Za-z0-9._:/-]{3,}$/;

/** Infers how a preformatted value should be typeset. */
export function inferDetailFieldKind(value: string): DetailFieldKind {
  const text = value.trim();
  if (isOpaqueIdentifier(text) || LONG_HEX.test(text)) {
    return "id";
  }
  if (ISO_DATE.test(text) || CLOCK_TIME.test(text) || MONTH_DATE.test(text) || RELATIVE_AGE.test(text)) {
    return "date";
  }
  if (NUMERIC.test(text)) {
    return "number";
  }
  return READABLE_CODE.test(text) ? "code" : "text";
}

/** Whether a value of this kind is set in the data (monospace) face rather than the body face. */
export function usesDataFace(kind: DetailFieldKind, value: string): boolean {
  return kind === "number" || kind === "code" || kind === "id" || (kind === "date" && /^\d/.test(value.trim()));
}

/**
 * Whether a preformatted value is a figure or identifier ("$1,250.00", "12.5%", "brk-2026-0042",
 * "2026-06-30") that keeps the data face; phrases, statuses and written-out dates read in the
 * body face.
 */
export function isDataFaceValue(value: string): boolean {
  const text = value.trim();
  return text.length > 0 && usesDataFace(inferDetailFieldKind(text), text);
}

export interface PartitionedDetailFields {
  /** Rows in the pane's reading path. */
  visible: ResolvedDetailField[];
  /** Opaque identifiers and rows marked `technical`, shown behind a disclosure with a copy action. */
  technical: ResolvedDetailField[];
}

function normalizeLabel(label: string): string {
  return label.trim().toLowerCase();
}

/**
 * Splits a pane's rows into the reading path and the technical disclosure.
 *
 * `shownInRow` names the columns the selected table row already shows; those rows are dropped,
 * because a detail pane exists for what the row cannot show. Rows with an empty value are dropped
 * too. Opaque identifiers never stay in the reading path.
 */
export function partitionDetailFields(
  fields: readonly DetailField[],
  options: { shownInRow?: readonly string[] } = {}
): PartitionedDetailFields {
  const alreadyShown = new Set((options.shownInRow ?? []).map(normalizeLabel));
  const visible: ResolvedDetailField[] = [];
  const technical: ResolvedDetailField[] = [];

  for (const field of fields) {
    if (!field.value.trim()) {
      continue;
    }

    const resolved: ResolvedDetailField = {
      ...field,
      kind: field.kind ?? inferDetailFieldKind(field.value),
      tone: field.tone ?? "default"
    };
    if (resolved.technical || resolved.kind === "id") {
      technical.push(resolved);
    } else if (!alreadyShown.has(normalizeLabel(field.label))) {
      visible.push(resolved);
    }
  }

  return { visible, technical };
}
