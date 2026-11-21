import { createContext, useContext, useId, useState, type ReactNode } from "react";
import { LedgerAmountProofDrawer } from "@/components/meridian/proof-drawer";
import { AmountPassportTrigger } from "@/components/meridian/amount-passport";
import { equalExactDecimals, exactDecimalText, formatExactDecimal, parseExactDecimal } from "@/lib/exact-financial-value";
import { cn } from "@/lib/utils";
import type { LedgerAmountSelection } from "@/types/ledger-amount-proof";
import type { AmountPassportReference } from "@/types/amount-passport";

export type FinancialDataCondition = "confirmed" | "provisional" | "estimated" | "stale" | "disputed" | "unavailable" | "not-recorded" | "not-applicable" | "pending" | "restated" | "review-required";

const CONDITION_LABELS: Record<FinancialDataCondition, string> = {
  confirmed: "Confirmed", provisional: "Provisional", estimated: "Estimated", stale: "Stale",
  disputed: "Disputed", unavailable: "Data condition unavailable", "not-recorded": "Not recorded",
  "not-applicable": "Not applicable", pending: "Pending", restated: "Restated", "review-required": "Review required"
};

const UnavailableConditionDescription = createContext<string | null>(null);

/** Explain a shared missing condition once without changing any value's own status or proof. */
export function FinancialValueConditionGroup({ children, description, active = true }: { children: ReactNode; description: string; active?: boolean }) {
  const descriptionId = useId();
  if (!active) return <>{children}</>;
  return <UnavailableConditionDescription.Provider value={descriptionId}>
    <div className="min-w-0">
      <p id={descriptionId} className="mb-3 text-xs text-muted-foreground">Data condition unavailable: {description}</p>
      {children}
    </div>
  </UnavailableConditionDescription.Provider>;
}

export interface FinancialValueProps {
  value: string | number | null | undefined;
  currency?: string;
  unit?: string;
  condition?: FinancialDataCondition;
  reason?: string;
  basis?: string;
  freshness?: string;
  passport?: LedgerAmountSelection | null;
  /** Existing scope/revision-addressed passport supplied by the read owner. */
  amountPassport?: AmountPassportReference | null;
  decimals?: number;
  signed?: boolean;
  /** Debit/credit columns may show a signed net balance on its applicable side. Proof keeps the signed operand. */
  absolute?: boolean;
  className?: string;
}

/** A source value's condition and scoped proof are independent; presence alone proves neither. */
export function FinancialValue({ value, currency, unit, condition = "unavailable", reason, basis, freshness, passport, amountPassport, decimals, signed, absolute = false, className }: FinancialValueProps) {
  const [open, setOpen] = useState(false);
  const conditionDescription = useContext(UnavailableConditionDescription);
  const parsed = absolute ? parseExactDecimal(value) : null;
  const displayValue = parsed ? exactDecimalText({ ...parsed, coefficient: parsed.coefficient < 0n ? -parsed.coefficient : parsed.coefficient }) : value;
  const formatted = formatExactDecimal(displayValue, { currency, decimals, signed });
  const effectiveCondition = formatted === null && condition === "confirmed" ? "unavailable" : condition;
  const missingLabel = effectiveCondition === "not-applicable" ? "Not applicable" : value == null ? "Not recorded" : "Unavailable";
  const passportMatches = amountPassport
    ? (amountPassport.currency ?? undefined) === currency && (amountPassport.exactValue == null || equalExactDecimals(value, amountPassport.exactValue))
    : !passport || (passport.currency === currency && equalExactDecimals(value, passport.amount));
  const explanation = !passportMatches ? "Amount passport unavailable: its amount or currency does not match this value." : reason
    ?? (formatted === null ? "The source has not supplied a usable value."
      : effectiveCondition === "unavailable" ? "The source has not supplied a data condition for this value." : undefined);
  const detail = [explanation, basis, freshness].filter(Boolean).join(" · ");
  const text = formatted === null ? missingLabel : `${formatted}${currency || unit ? ` ${currency ?? unit}` : ""}`;
  const accessibleValue = effectiveCondition === "confirmed" || formatted === null
    ? text : `${text} · ${CONDITION_LABELS[effectiveCondition]}`;
  const groupedUnavailable = effectiveCondition === "unavailable" && conditionDescription !== null;
  const content = <span className={cn("inline-flex max-w-full flex-col items-end gap-0.5 align-top", className)} title={detail || undefined}
    role={groupedUnavailable ? "group" : undefined} aria-label={groupedUnavailable ? accessibleValue : undefined}
    aria-describedby={groupedUnavailable ? conditionDescription : undefined}>
    <span className="font-mono tabular-nums">{text}</span>
    {effectiveCondition !== "confirmed" && formatted !== null && !groupedUnavailable ? <span className="inline-flex items-center gap-1 text-xs text-muted-foreground"><span aria-hidden="true">•</span>{CONDITION_LABELS[effectiveCondition]}</span> : null}
    {formatted === null && detail ? <span className="max-w-64 whitespace-normal text-xs text-muted-foreground">{detail}</span> : null}
    {formatted !== null && detail ? <span className="sr-only">{detail}</span> : null}
    {!passportMatches ? <span className="text-xs text-muted-foreground">Amount passport unavailable</span> : null}
  </span>;
  if (!passportMatches || formatted === null) return content;
  if (amountPassport) {
    const exact = parseExactDecimal(value);
    const reference = exact && amountPassport.exactValue == null ? { ...amountPassport, exactValue: exactDecimalText(exact) } : amountPassport;
    return <AmountPassportTrigger compact selection={{ label: passport?.label ?? "Financial value", reference, displayValue: text,
      currency: amountPassport.currency, unit: amountPassport.unit, basis }}
      ariaLabel={`Open Amount passport for ${passport?.label ?? "financial value"}: ${accessibleValue}`}>{content}</AmountPassportTrigger>;
  }
  if (!passport) return content;
  return <>
    <button type="button" className="max-w-full rounded-sm hover:underline hover:decoration-dotted hover:underline-offset-4 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
      aria-label={`Open Amount passport for ${passport.label}: ${accessibleValue}`}
      aria-description={detail || undefined}
      onClick={(event) => { event.stopPropagation(); setOpen(true); }} onKeyDown={(event) => event.stopPropagation()}>{content}</button>
    <LedgerAmountProofDrawer selection={open ? passport : null} onClose={() => setOpen(false)} />
  </>;
}
