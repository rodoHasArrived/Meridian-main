// Display labels for wire values.
//
// Contracts carry PascalCase enum names ("PartiallyFilled", "ControlEvaluated"). Those are
// identifiers, not operator copy: every rendered status, action or event name goes through
// `formatEnumLabel` (or a domain map built on it) so the reading path shows words. The raw value
// stays available for exports, filters and the technical-details disclosure.

const ACRONYM = /^[A-Z0-9]{2,}$/;

/**
 * Sentence-case words for an enum-style identifier: `"PartiallyFilled"` → `"Partially filled"`,
 * `"review_required"` → `"Review required"`, `"FIXSessionDown"` → `"FIX session down"`.
 * Text that already contains spaces is operator copy and is returned unchanged.
 */
export function formatEnumLabel(value: string | null | undefined, fallback = "—"): string {
  const trimmed = value?.trim();
  if (!trimmed) {
    return fallback;
  }
  if (/\s/.test(trimmed)) {
    return trimmed;
  }
  const words = trimmed
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .split(/[\s_-]+/)
    .filter(Boolean);
  if (words.length === 0) {
    return fallback;
  }
  return words
    .map((word, index) => {
      if (ACRONYM.test(word)) return word;
      const lower = word.toLowerCase();
      return index === 0 ? lower.charAt(0).toUpperCase() + lower.slice(1) : lower;
    })
    .join(" ");
}

/** Labels for execution audit actions and outcomes whose plain-words form is not the split identifier. */
const EXECUTION_EVENT_LABELS: Readonly<Record<string, string>> = {
  ControlEvaluated: "Control check run",
  OrderSubmitted: "Order submitted",
  PartialFill: "Partial fill",
  PartiallyFilled: "Partly filled",
  PendingApproval: "Waiting for approval",
  ManualOverride: "Manual override",
  ApprovalGranted: "Approval granted",
  WalRetained: "Retained in write-ahead log"
};

/** An execution audit action, outcome or ledger status as operator copy. */
export function executionEventLabel(value: string | null | undefined, fallback = "—"): string {
  const trimmed = value?.trim();
  if (!trimmed) {
    return fallback;
  }
  return EXECUTION_EVENT_LABELS[trimmed] ?? formatEnumLabel(trimmed, fallback);
}

/**
 * A sentence from a service that embeds enum identifiers, with each one spelled out:
 * `"SymbolConcentration: NVDA 26.40% (state Observe)."` → `"Symbol concentration: NVDA 26.40%
 * (state Observe)."`. Tickers, numbers and ordinary words are left as they are.
 */
export function humanizeIdentifiersInText(text: string | null | undefined, fallback = "—"): string {
  const trimmed = text?.trim();
  if (!trimmed) {
    return fallback;
  }
  return trimmed.replace(/\b[A-Z][a-z0-9]+(?:[A-Z][a-z0-9]+)+\b/g, (token, offset: number) => {
    const label = formatEnumLabel(token);
    const startsSentence = offset === 0 || /[.!?]\s+$/.test(trimmed.slice(0, offset));
    return startsSentence ? label : label.charAt(0).toLowerCase() + label.slice(1);
  });
}

/** True when rendered text still carries a multi-word PascalCase identifier such as "PartiallyFilled". */
export function containsPascalCaseIdentifier(text: string): boolean {
  return /\b[A-Z][a-z0-9]+(?:[A-Z][a-z0-9]+)+\b/.test(text);
}
