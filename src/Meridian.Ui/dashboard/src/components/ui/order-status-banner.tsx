import { CheckCircle, PauseCircle } from "lucide-react";

import { CardContent } from "@/components/ui/card";

export interface OrderStatusBannerProps {
  /** Confirmation text for an order the broker accepted. */
  successText: string | null;
  /**
   * Text for an order parked awaiting governed risk approval. Nothing routed, but a live
   * queue entry can still execute it — so this is deliberately not styled as a failure.
   */
  parkedText: string | null;
}

/**
 * Post-submission banner for the order ticket. Parked takes precedence over submitted:
 * only one of the two can be live at a time, and a park is the more recent outcome.
 */
export function OrderStatusBanner({ successText, parkedText }: OrderStatusBannerProps) {
  if (!successText && !parkedText) {
    return null;
  }

  const parked = parkedText !== null;
  return (
    <CardContent className="border-b border-border/60 pb-4">
      <div
        role="status"
        className={parked
          ? "rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning flex items-center gap-2"
          : "rounded-lg border border-success/30 bg-success/10 px-4 py-3 text-sm text-success flex items-center gap-2"}
      >
        {parked ? <PauseCircle className="h-4 w-4 shrink-0" /> : <CheckCircle className="h-4 w-4 shrink-0" />}
        {parkedText ?? successText}
      </div>
    </CardContent>
  );
}
