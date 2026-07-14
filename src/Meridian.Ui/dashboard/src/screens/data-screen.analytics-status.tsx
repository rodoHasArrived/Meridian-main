import { Link } from "react-router-dom";
import { RefreshCcw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";

export interface DataAnalyticsPanelStatus {
  id: string;
  label: string;
  error: string | null;
  loading: boolean;
  refresh: () => Promise<void>;
}

export interface DataAnalyticsDegradedViewModel {
  affected: { id: string; label: string }[];
  affectedIds: Set<string>;
  title: string;
  detail: string;
  affectsLabel: string;
  retryLabel: string;
  retryBusyLabel: string;
  retryAriaLabel: string;
  diagnosticsLabel: string;
  diagnosticsHref: string;
  diagnosticsAriaLabel: string;
  refreshing: boolean;
  retryAll: () => Promise<void>;
}

/**
 * One upstream condition renders one state: when two or more of the Data
 * analytics reads fail together, the overview surfaces a single degraded
 * panel with the next productive action instead of a stack of per-panel
 * alarms. A single failing panel keeps its own inline state.
 */
export function buildDataAnalyticsDegradedViewModel(
  panels: DataAnalyticsPanelStatus[]
): DataAnalyticsDegradedViewModel | null {
  const affected = panels.filter((panel) => panel.error !== null);
  if (affected.length < 2) {
    return null;
  }

  return {
    affected: affected.map(({ id, label }) => ({ id, label })),
    affectedIds: new Set(affected.map((panel) => panel.id)),
    title: "Data analytics services degraded",
    detail:
      "The analytics reads behind these panels are unavailable, so their posture is paused. Provider streaming and backfills are unaffected.",
    affectsLabel: affected.map((panel) => panel.label).join(" · "),
    retryLabel: "Retry all",
    retryBusyLabel: "Retrying…",
    retryAriaLabel: `Retry ${affected.length} unavailable analytics panels`,
    diagnosticsLabel: "Open diagnostics",
    diagnosticsHref: WORKSTATION_ROUTE_CATALOG.settingsDiagnostics,
    diagnosticsAriaLabel: "Open workstation diagnostics in Settings",
    refreshing: affected.some((panel) => panel.loading),
    retryAll: async () => {
      await Promise.all(affected.map((panel) => panel.refresh()));
    }
  };
}

export function DataAnalyticsDegradedRegion({ vm }: { vm: DataAnalyticsDegradedViewModel }) {
  return (
    <section
      aria-labelledby="data-analytics-degraded-title"
      className="workspace-region"
      role="status"
    >
      <Card>
        <CardContent className="space-y-3 pt-6">
          <StatusBanner
            tone="warning"
            title={<span id="data-analytics-degraded-title">{vm.title}</span>}
            detail={vm.detail}
          />
          <p className="text-xs text-muted-foreground">
            <span className="font-medium uppercase tracking-[0.08em]">Affects</span>{" "}
            <span className="font-mono">{vm.affectsLabel}</span>
          </p>
          <div className="flex flex-wrap items-center gap-2">
            <Button
              type="button"
              size="sm"
              onClick={() => void vm.retryAll()}
              disabled={vm.refreshing}
              aria-label={vm.retryAriaLabel}
            >
              <RefreshCcw className="h-4 w-4" aria-hidden="true" />
              <span className="ml-1.5">{vm.refreshing ? vm.retryBusyLabel : vm.retryLabel}</span>
            </Button>
            <Button asChild variant="outline" size="sm">
              <Link to={vm.diagnosticsHref} aria-label={vm.diagnosticsAriaLabel}>
                {vm.diagnosticsLabel}
              </Link>
            </Button>
          </div>
        </CardContent>
      </Card>
    </section>
  );
}
