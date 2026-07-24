import { AlertTriangle, Network, RotateCcw, Settings, ShieldCheck } from "lucide-react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { SeverityBadge } from "@/components/operations";
import { cn } from "@/lib/utils";
import {
  formatReadinessStatusValue,
  mapReadinessStatusLevel,
  type AcceptanceLevel,
  type TradingReadinessSummaryRow
} from "@/screens/trading-screen.readiness-summary";
import type {
  TradingReadinessState,
  TradingReadinessWarningRow,
  TradingReadinessWorkItemRow
} from "@/screens/trading-screen.view-model";
import type { TradingAcceptanceGate } from "@/types";

export interface CockpitAcceptanceItem {
  label: string;
  value: string;
  detail: string;
  level: AcceptanceLevel;
}

export const acceptanceTone: Record<AcceptanceLevel, string> = {
  ready: "border-success/30 bg-success/10 text-success",
  review: "border-warning/30 bg-warning/10 text-warning",
  atRisk: "border-danger/30 bg-danger/10 text-danger"
};

const acceptanceLabel: Record<AcceptanceLevel, string> = {
  ready: "Ready",
  review: "Review",
  atRisk: "At risk"
};

const acceptanceStatus: Record<AcceptanceLevel, string> = {
  ready: "ready",
  review: "review",
  atRisk: "blocked"
};

const workItemTone: Record<string, string> = {
  Info: "border-border/70 bg-secondary/25 text-muted-foreground",
  Success: "border-success/30 bg-success/10 text-success",
  Warning: "border-warning/30 bg-warning/10 text-warning",
  Critical: "border-danger/30 bg-danger/10 text-danger"
};

export function mapAcceptanceGate(gate: TradingAcceptanceGate): CockpitAcceptanceItem {
  return {
    label: gate.label,
    value: formatReadinessStatusValue(gate.status),
    detail: gate.detail,
    level: mapReadinessStatusLevel(gate.status)
  };
}

export function AcceptanceStatusCard({
  items,
  readinessVm
}: {
  items: CockpitAcceptanceItem[];
  readinessVm: TradingReadinessState & { refresh: () => Promise<void> };
}) {
  const readyCount = items.filter((item) => item.level === "ready").length;
  const totalCount = items.length;
  const hasAtRisk = items.some((item) => item.level === "atRisk");
  const overallLevel: AcceptanceLevel = readyCount === totalCount ? "ready" : hasAtRisk ? "atRisk" : "review";

  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <div className="eyebrow-label">Operator Acceptance</div>
            <CardTitle className="flex items-center gap-2 text-base">
              <ShieldCheck className="h-4 w-4 text-primary" />
              Paper cockpit readiness
            </CardTitle>
            <CardDescription>
              Session, replay, audit, and promotion signals for the current paper workflow.
            </CardDescription>
          </div>
          <div className="panel-action-zone">
            <Button asChild size="sm" variant="secondary">
              <Link to="/trading/readiness">Open console</Link>
            </Button>
            <Button asChild size="sm" variant="outline">
              <Link to={readinessVm.evidenceAction.href} aria-label={readinessVm.evidenceAction.ariaLabel}>
                <Network className="h-4 w-4" />
                {readinessVm.evidenceAction.label}
              </Link>
            </Button>
            <Button
              size="sm"
              variant="outline"
              onClick={() => { void readinessVm.refresh(); }}
              disabled={readinessVm.refreshDisabled}
              disabledReason={readinessVm.refreshDisabledReason}
              busy={readinessVm.refreshing}
              busyLabel={readinessVm.refreshBusyLabel}
              aria-label={readinessVm.refreshAriaLabel}
            >
              <RotateCcw className={cn("h-4 w-4", readinessVm.refreshing && "animate-spin")} />
              {readinessVm.refreshButtonLabel}
            </Button>
            <SeverityBadge status={acceptanceStatus[overallLevel]} label={`${readyCount}/${totalCount} ready`} />
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="sr-only" aria-live="polite">{readinessVm.statusAnnouncement}</div>
        {readinessVm.summaryRows.length > 0 && (
          <div className="grid gap-2 md:grid-cols-4" aria-label={readinessVm.summaryLabel}>
            {readinessVm.summaryRows.map((row) => (
              <ReadinessSummaryPill key={row.id} row={row} />
            ))}
          </div>
        )}
        {readinessVm.errorText && (
          <div role="alert" className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning">
            {readinessVm.errorText}
          </div>
        )}
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          {items.map((item) => (
            <AcceptanceRow key={item.label} item={item} />
          ))}
        </div>
        {readinessVm.hasOperatorAttention && (
          <OperatorWorkItemList
            summaryText={readinessVm.workItemSummaryText}
            listLabel={readinessVm.workItemListLabel}
            primaryKind={readinessVm.primaryWorkItemKind}
            workItems={readinessVm.visibleWorkItems}
            workItemOverflowLabel={readinessVm.workItemOverflowLabel}
            warnings={readinessVm.visibleWarnings}
            warningOverflowLabel={readinessVm.warningOverflowLabel}
          />
        )}
      </CardContent>
    </Card>
  );
}

function ReadinessSummaryPill({ row }: { row: TradingReadinessSummaryRow }) {
  return (
    <div className={cn("data-grid-surface border border-l-[3px] px-3 py-2", acceptanceTone[row.level])} aria-label={row.ariaLabel}>
      <p className="text-xs font-semibold uppercase tracking-[0.14em] opacity-80">{row.label}</p>
      <p className="mt-1 break-words font-mono text-xs font-semibold text-foreground">{row.label}: {row.value}</p>
    </div>
  );
}

function AcceptanceRow({ item }: { item: CockpitAcceptanceItem }) {
  return (
    <div className={cn("data-grid-surface border border-l-[3px] px-4 py-3", acceptanceTone[item.level])}>
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-xs font-semibold uppercase tracking-[0.14em] opacity-80">{item.label}</p>
          <p className="mt-1 font-mono text-sm font-semibold">{item.value}</p>
        </div>
        <SeverityBadge status={acceptanceStatus[item.level]} label={acceptanceLabel[item.level]} />
      </div>
      <p className="mt-2 text-xs leading-5 text-foreground/80">{item.detail}</p>
    </div>
  );
}

function OperatorWorkItemList({
  summaryText,
  listLabel,
  primaryKind,
  workItems,
  workItemOverflowLabel,
  warnings,
  warningOverflowLabel
}: {
  summaryText: string;
  listLabel: string;
  primaryKind: string | null;
  workItems: TradingReadinessWorkItemRow[];
  workItemOverflowLabel: string | null;
  warnings: TradingReadinessWarningRow[];
  warningOverflowLabel: string | null;
}) {
  return (
    <div className="panel-surface p-4" role="region" aria-label={listLabel}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Operator work items</p>
          <p className="mt-1 text-sm text-muted-foreground">{summaryText}</p>
        </div>
        {primaryKind && (
          <span className="rounded-sm border border-border/70 px-3 py-1 font-mono text-[10px] font-medium uppercase tracking-[0.14em] text-muted-foreground">
            {primaryKind}
          </span>
        )}
      </div>

      {workItems.length > 0 && (
        <ul className="mt-3 grid gap-2 md:grid-cols-2">
          {workItems.map((item) => (
            <li
              key={item.workItemId}
              aria-label={item.ariaLabel}
              className={cn("rounded-lg border px-3 py-2 text-sm", workItemTone[item.tone] ?? workItemTone.Info)}
            >
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="font-semibold text-foreground">{item.label}</span>
                <SeverityBadge status={item.tone} label={item.tone} />
              </div>
              <p className="mt-1 text-xs leading-5 text-foreground/80">{item.detail}</p>
              {item.metadataText && (
                <p className="mt-2 font-mono text-[11px] text-foreground/70">{item.metadataText}</p>
              )}
              {item.action && (
                <div className="mt-3">
                  <Button asChild size="sm" variant="outline" className="bg-background/40">
                    <Link to={item.action.href} aria-label={item.action.ariaLabel}>
                      <Settings className="h-3.5 w-3.5" aria-hidden="true" />
                      <span>{item.action.label}</span>
                    </Link>
                  </Button>
                  <span className="sr-only">{item.action.detail}</span>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
      {workItemOverflowLabel && (
        <p className="mt-3 font-mono text-[11px] text-muted-foreground">{workItemOverflowLabel}</p>
      )}

      {warnings.length > 0 && (
        <ul className="mt-3 space-y-1 text-xs text-warning">
          {warnings.map((warning) => (
            <li key={warning.id} aria-label={warning.ariaLabel} className="flex gap-2">
              <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
              <span>{warning.text}</span>
            </li>
          ))}
        </ul>
      )}
      {warningOverflowLabel && (
        <p className="mt-3 font-mono text-[11px] text-warning/90">{warningOverflowLabel}</p>
      )}
    </div>
  );
}
