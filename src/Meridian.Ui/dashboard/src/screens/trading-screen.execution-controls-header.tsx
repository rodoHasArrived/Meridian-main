import { OctagonX } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { StatusBadge } from "@/design-system/status";
import type { ExecutionEvidenceViewModel, TradingConfirmAction } from "@/screens/trading-screen.view-model";
import { usesTrackedCaps } from "@/design-system/label-case";

/**
 * Title row of the execution controls snapshot: the breaker state badge, the kill switch beside
 * it (destructive, always confirmed), and the refresh control. The breaker button is absent rather than disabled when no snapshot has
 * loaded - an execution control the operator cannot act on should not look like one they can.
 */
export function ExecutionControlsHeader({
  executionEvidence,
  onConfirm
}: {
  executionEvidence: ExecutionEvidenceViewModel;
  onConfirm: (action: TradingConfirmAction) => void;
}) {
  const panel = executionEvidence.controlsPanel;

  return (
    <div className="mb-2 flex items-center justify-between gap-3">
      <p className={cn("text-xs font-semibold text-muted-foreground", usesTrackedCaps(panel?.title ?? "Execution controls snapshot") && "uppercase tracking-[0.14em]")}>
        {panel?.title ?? "Execution controls snapshot"}
      </p>
      <div className="panel-action-zone">
        {/* State sits beside the control that changes it, so the two are read together. */}
        <StatusBadge
          status={!panel ? "info" : panel.statusTone === "danger" ? "blocked" : "ready"}
          label={panel?.statusLabel ?? "Snapshot unavailable"}
        />
        {panel && (
          <Button
            size="sm"
            variant={panel.breakerAction.kind === "open-circuit-breaker" ? "destructive" : "outline"}
            onClick={() => onConfirm(panel.breakerAction)}
            disabled={panel.breakerActionDisabled}
            disabledReason={panel.breakerActionDisabledReason}
            aria-label={panel.breakerActionAriaLabel}
            title={panel.breakerActionAriaLabel}
          >
            <OctagonX className="mr-2 h-4 w-4" />
            {panel.breakerActionLabel}
          </Button>
        )}
        {executionEvidence.errorText ? <Button
          size="sm"
          variant="outline"
          onClick={() => { void executionEvidence.refresh(); }}
          disabled={executionEvidence.refreshDisabled}
          disabledReason={executionEvidence.refreshDisabledReason}
          busy={executionEvidence.loading}
          busyLabel={executionEvidence.refreshBusyLabel}
          aria-label={executionEvidence.refreshAriaLabel}
        >
          {executionEvidence.refreshButtonLabel}
        </Button> : null}
      </div>
    </div>
  );
}
