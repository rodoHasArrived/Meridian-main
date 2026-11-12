import { bondDecimalText, DECIMAL_LITERAL } from "@/lib/bond-decimal-format";

interface Preview {
  sampleOriginalFace: string;
  currency: string;
  coupon: string;
  couponRateUnit: string;
  couponCashBasis: string;
  paymentFrequency: string;
  dayCount: string;
  accrualStart: string;
  accrualEnd: string;
  paymentDate: string;
  sampleCouponPayment: string;
  yearFraction: string;
  paymentCalendars: string[];
  businessDayConvention: string;
  settlementDays: string;
  settlementConvention: string;
  settlementCalendars: string[];
  callScheduleCount: number;
  factorHistoryCount: number;
  normalizedTermsJson: string;
}

function readPreview(record: unknown): Preview | null {
  if (typeof record !== "object" || record === null || !("fixedIncomePaymentPreview" in record)) return null;
  const value = record.fixedIncomePaymentPreview;
  if (typeof value !== "object" || value === null || Array.isArray(value)) return null;
  const fields = value as Record<string, unknown>;
  const textFields = ["sampleOriginalFace", "currency", "coupon", "couponRateUnit", "couponCashBasis", "paymentFrequency",
    "dayCount", "accrualStart", "accrualEnd", "paymentDate", "sampleCouponPayment", "yearFraction",
    "businessDayConvention", "settlementDays", "settlementConvention", "normalizedTermsJson"];
  if (!textFields.every((key) => typeof fields[key] === "string")) return null;
  if (!["sampleOriginalFace", "coupon", "sampleCouponPayment", "yearFraction", "settlementDays"]
    .every((key) => DECIMAL_LITERAL.test(fields[key] as string))) return null;
  if (!["paymentCalendars", "settlementCalendars"].every((key) => Array.isArray(fields[key]) &&
    fields[key].every((id: unknown) => typeof id === "string" && id.trim().length > 0))) return null;
  if (!["callScheduleCount", "factorHistoryCount"].every((key) => typeof fields[key] === "number" &&
    Number.isSafeInteger(fields[key]) && fields[key] >= 0)) return null;
  return fields as unknown as Preview;
}

/** Server-generated evidence uses the same schedule, day-count and coupon kernels as analytics. */
export function FixedIncomePaymentPreview({ mappedRecords }: { mappedRecords: unknown[] }) {
  const previews = mappedRecords.map(readPreview).filter((preview): preview is Preview => preview !== null);
  if (previews.length === 0) return null;
  return (
    <section className="mt-3 space-y-2 rounded-sm border border-border/60 bg-background/40 p-3" aria-label="Fixed-income sample payment preview">
      <h4 className="text-xs font-semibold text-foreground">Review conventions · Sample first coupon</h4>
      <p className="text-[11px] text-muted-foreground">Illustrative cash on 100,000 original face, with supplied dated factors. This preview does not approve terms or create a holding.</p>
      {previews.map((preview, index) => (
        <div key={`${preview.paymentDate}-${index}`} className="grid gap-2 border-t border-border/50 pt-2 text-[11px]">
          <dl className="grid gap-x-4 gap-y-1 md:grid-cols-2">
            <div><dt className="text-muted-foreground">Sample coupon cash</dt><dd className="font-mono">{preview.currency} {bondDecimalText(preview.sampleCouponPayment)}</dd></div>
            <div><dt className="text-muted-foreground">Coupon and frequency</dt><dd>{bondDecimalText(preview.coupon)} {preview.couponRateUnit} · {preview.paymentFrequency} · {preview.couponCashBasis}</dd></div>
            <div><dt className="text-muted-foreground">Accrual and payment</dt><dd>{preview.accrualStart} → {preview.accrualEnd} · Pays {preview.paymentDate}</dd></div>
            <div><dt className="text-muted-foreground">Day count</dt><dd>{preview.dayCount} · Fraction {bondDecimalText(preview.yearFraction)}</dd></div>
            <div><dt className="text-muted-foreground">Payment calendars</dt><dd>{preview.paymentCalendars.join(", ") || "Explicitly none"} · {preview.businessDayConvention}</dd></div>
            <div><dt className="text-muted-foreground">Settlement</dt><dd>T+{preview.settlementDays} · {preview.settlementConvention} · {preview.settlementCalendars.join(", ") || "Explicitly no calendar"}</dd></div>
            <div><dt className="text-muted-foreground">Retained schedules</dt><dd>{preview.callScheduleCount} call windows · {preview.factorHistoryCount} dated factors</dd></div>
            <div><dt className="text-muted-foreground">Currency choice</dt><dd>{preview.currency} · Reviewed explicit choice</dd></div>
          </dl>
          <details><summary className="cursor-pointer text-muted-foreground">Normalized terms used for sample</summary><pre className="mt-2 max-h-48 overflow-auto rounded-sm bg-secondary/20 p-2 text-[10px]">{preview.normalizedTermsJson}</pre></details>
        </div>
      ))}
    </section>
  );
}
