import { useId, useState } from "react";
import { Lock } from "lucide-react";
import { LedgerAmountProofDrawer } from "@/components/meridian/proof-drawer";
import { AmountPassportTrigger } from "@/components/meridian/amount-passport";
import { equalExactDecimals, exactDecimalText, formatExactDecimal, parseExactDecimal } from "@/lib/exact-financial-value";
import { VALUE_STATE_LABELS } from "@/lib/format";
import { cn } from "@/lib/utils";
import type { LedgerAmountSelection } from "@/types/ledger-amount-proof";
import type { AmountPassportReference } from "@/types/amount-passport";

export type FinancialDataCondition = "confirmed" | "provisional" | "estimated" | "stale" | "disputed" | "unavailable" | "not-recorded" | "not-applicable" | "pending" | "restated" | "review-required";

const CONDITION_LABELS: Record<FinancialDataCondition, string> = {
  confirmed: "Confirmed", provisional: "Provisional", estimated: "Estimated", stale: "Stale",
  disputed: "Disputed", unavailable: "Data condition unavailable", "not-recorded": "Not recorded",
  "not-applicable": "Not applicable", pending: "Pending", restated: "Restated", "review-required": "Review required"
};

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
  /**
   * A visible shared notice stating the unavailable data condition for this group of values.
   * Only condition="unavailable" consolidates its visible label; all other conditions stay inline.
   * The caller must render the notice, and each value/proof control remains associated with it.
   */
  conditionContextId?: string;
  /**
   * The viewer is not permitted to read this figure. Renders a lock and "No access" in place of
   * the value; any supplied value is never displayed or offered through a passport.
   */
  restricted?: boolean;
  /** Why access is restricted, shown as the restricted state's explanation. */
  restrictedReason?: string;
  "aria-describedby"?: string;
  className?: string;
}

/**
 * A source value's condition and scoped proof are independent; presence alone proves neither.
 *
 * Every amount renders in exactly one state, each with its own look and accessible text:
 * a value; a confirmed zero ("0.00"); "Not available" (dashed chip: not loaded, not supplied or
 * unparseable — never 0); "n/a" (condition="not-applicable"); or a lock with "No access"
 * (`restricted`). The state is exposed as `data-value-state` for styling and tests.
 */
export function FinancialValue(props: FinancialValueProps) {
  if (props.restricted) return <RestrictedFinancialValue reason={props.restrictedReason} describedBy={props["aria-describedby"]} className={props.className} />;
  return <AccessibleFinancialValue {...props} />;
}

function RestrictedFinancialValue({ reason, describedBy, className }: { reason?: string; describedBy?: string; className?: string }) {
  const detailId = useId();
  const detail = reason?.trim() || "You do not have permission to view this amount.";
  return <span data-value-state="restricted" title={detail} aria-describedby={[describedBy, detailId].filter(Boolean).join(" ")}
    className={cn("inline-flex items-center gap-1 align-top text-xs text-muted-foreground", className)}>
    <Lock aria-hidden="true" className="size-3.5 shrink-0" />
    <span>{VALUE_STATE_LABELS.restricted}</span>
    <span id={detailId} className="sr-only">{detail}</span>
  </span>;
}

function AccessibleFinancialValue({ value, currency, unit, condition = "unavailable", reason, basis, freshness, passport, amountPassport, decimals, signed, absolute = false, conditionContextId, "aria-describedby": describedBy, className }: FinancialValueProps) {
  const [open, setOpen] = useState(false);
  const detailId = useId();
  const parsed = absolute ? parseExactDecimal(value) : null;
  const displayValue = parsed ? exactDecimalText({ ...parsed, coefficient: parsed.coefficient < 0n ? -parsed.coefficient : parsed.coefficient }) : value;
  const formatted = formatExactDecimal(displayValue, { currency, decimals, signed });
  const effectiveCondition = formatted === null && condition === "confirmed" ? "unavailable" : condition;
  const notApplicable = effectiveCondition === "not-applicable";
  // One vocabulary for a missing figure, whether it was never supplied or is unparseable.
  const missingLabel = notApplicable ? CONDITION_LABELS["not-applicable"] : VALUE_STATE_LABELS.unavailable;
  const valueState = formatted === null ? (notApplicable ? "not-applicable" : "unavailable")
    : parseExactDecimal(value)?.coefficient === 0n ? "zero" : "value";
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
  const sharedConditionId = condition === "unavailable" ? conditionContextId?.trim() || undefined : undefined;
  const descriptionIds = [...new Set([
    describedBy, sharedConditionId, sharedConditionId && detail ? detailId : undefined
  ].filter((id): id is string => Boolean(id)).flatMap((id) => id.trim().split(/\s+/)).filter(Boolean))].join(" ") || undefined;
  const content = <span data-value-state={valueState} className={cn("inline-flex max-w-full flex-col items-end gap-0.5 align-top", className)} title={detail || undefined}
    role={sharedConditionId ? "group" : undefined} aria-label={sharedConditionId ? accessibleValue : undefined} aria-describedby={descriptionIds}>
    {formatted !== null ? <span className="font-mono tabular-nums">{text}</span>
      : notApplicable ? <span className="text-muted-foreground"><span aria-hidden="true">{VALUE_STATE_LABELS.notApplicable}</span><span className="sr-only">{text}</span></span>
        : <span className="rounded-sm border border-dashed border-border px-1.5 text-xs leading-5 text-muted-foreground">{text}</span>}
    {effectiveCondition !== "confirmed" && formatted !== null && !sharedConditionId ? <span className="inline-flex items-center gap-1 text-xs text-muted-foreground"><span aria-hidden="true">•</span>{CONDITION_LABELS[effectiveCondition]}</span> : null}
    {formatted === null && detail ? <span id={detailId} className="max-w-64 whitespace-normal text-xs text-muted-foreground">{detail}</span> : null}
    {formatted !== null && detail ? <span id={detailId} className="sr-only">{detail}</span> : null}
    {!passportMatches ? <span className="text-xs text-muted-foreground">Amount passport unavailable</span> : null}
  </span>;
  if (!passportMatches || formatted === null) return content;
  if (amountPassport) {
    const exact = parseExactDecimal(value);
    const reference = exact && amountPassport.exactValue == null ? { ...amountPassport, exactValue: exactDecimalText(exact) } : amountPassport;
    return <AmountPassportTrigger compact selection={{ label: passport?.label ?? "Financial value", reference, displayValue: text,
      currency: amountPassport.currency, unit: amountPassport.unit, basis }}
      ariaLabel={`Open Amount passport for ${passport?.label ?? "financial value"}: ${accessibleValue}`} ariaDescribedBy={descriptionIds}>{content}</AmountPassportTrigger>;
  }
  if (!passport) return content;
  return <>
    <button type="button" className="max-w-full rounded-sm hover:underline hover:decoration-dotted hover:underline-offset-4 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
      aria-label={`Open Amount passport for ${passport.label}: ${accessibleValue}`}
      aria-description={detail || undefined}
      aria-describedby={descriptionIds}
      onClick={(event) => { event.stopPropagation(); setOpen(true); }} onKeyDown={(event) => event.stopPropagation()}>{content}</button>
    <LedgerAmountProofDrawer selection={open ? passport : null} onClose={() => setOpen(false)} />
  </>;
}
