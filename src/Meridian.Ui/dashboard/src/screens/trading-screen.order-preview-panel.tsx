import { usesTrackedCaps } from "@/design-system/label-case";
import { cn } from "@/lib/utils";
import type { OrderPreview, OrderPreviewEffect, OrderPreviewLevel, OrderPreviewWarning } from "./trading-screen.view-model";

const orderPreviewWarningTone: Record<OrderPreviewLevel, string> = {
  info: "border-border/70 bg-secondary/30 text-muted-foreground",
  warning: "border-warning/30 bg-warning/10 text-warning",
  danger: "border-danger/30 bg-danger/10 text-danger"
};

const orderPreviewEffectTone: Record<OrderPreviewEffect, string> = {
  "open-long": "text-success",
  "open-short": "text-warning",
  "add-long": "text-success",
  "add-short": "text-warning",
  "reduce-long": "text-muted-foreground",
  "reduce-short": "text-muted-foreground",
  "close-long": "text-foreground",
  "close-short": "text-foreground",
  "flip-to-short": "text-danger",
  "flip-to-long": "text-danger"
};

export function OrderPreviewPanel({ preview }: { preview: OrderPreview }) {
  const effectTone = preview.effect ? orderPreviewEffectTone[preview.effect] : "text-muted-foreground";
  return (
    <section
      role="region"
      aria-label="Order impact preview"
      aria-live="polite"
      className="rounded-lg border border-border/60 bg-secondary/15 p-3 text-xs"
      data-testid="order-preview"
    >
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <div className="text-xs font-medium text-muted-foreground">
          Order impact preview
        </div>
        <div className={cn("font-semibold", effectTone)} data-testid="order-preview-effect">
          {preview.effectLabel}
        </div>
      </div>

      <p className="sr-only">{preview.ariaSummary}</p>

      <dl className="mt-2 grid gap-2 sm:grid-cols-3">
        <PreviewStat label="Estimated notional" value={preview.notionalLabel} mono />
        <PreviewStat
          label={preview.priceSourceLabel}
          value={preview.referencePriceLabel}
          mono
        />
        <PreviewStat label="Resulting position" value={preview.resultingPositionLabel} />
      </dl>

      <p className="mt-2 text-muted-foreground" data-testid="order-preview-detail">
        {preview.effectDetail}
      </p>

      {preview.riskNote && (
        <p className="mt-1 text-muted-foreground">{preview.riskNote}</p>
      )}

      {preview.warnings.length > 0 && (
        <ul className="mt-2 space-y-1" data-testid="order-preview-warnings">
          {preview.warnings.map((warning) => (
            <OrderPreviewWarningRow key={warning.id} warning={warning} />
          ))}
        </ul>
      )}
    </section>
  );
}

function PreviewStat({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <dt className={cn("text-xs text-muted-foreground", usesTrackedCaps(label) && "type-label--caps")}>{label}</dt>
      <dd className={cn("text-foreground", mono && "font-mono")}>{value}</dd>
    </div>
  );
}

function OrderPreviewWarningRow({ warning }: { warning: OrderPreviewWarning }) {
  return (
    <li
      className={cn("rounded-md border px-2 py-1.5 text-xs", orderPreviewWarningTone[warning.level])}
      role={warning.level === "danger" ? "alert" : undefined}
    >
      {warning.message}
    </li>
  );
}
