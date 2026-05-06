import { Activity, ClipboardList, FileCheck2, RadioTower, ShieldCheck, TrendingUp } from "lucide-react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import {
  useOperatorReadinessConsoleViewModel,
  type ReadinessConsolePanel,
  type ReadinessConsoleLevel,
  type ReadinessConsoleRow
} from "@/screens/operator-readiness-console.view-model";
import type {
  DataOperationsWorkspaceResponse,
  GovernanceWorkspaceResponse,
  ResearchWorkspaceResponse,
  TradingWorkspaceResponse
} from "@/types";

interface OperatorReadinessConsoleProps {
  research: ResearchWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  dataOperations: DataOperationsWorkspaceResponse | null;
  governance: GovernanceWorkspaceResponse | null;
  reporting: GovernanceWorkspaceResponse | null;
}

const levelBadge: Record<ReadinessConsoleLevel, "success" | "warning" | "danger" | "outline"> = {
  ready: "success",
  review: "warning",
  blocked: "danger",
  neutral: "outline"
};

const levelPanel: Record<ReadinessConsoleLevel, string> = {
  ready: "border-success/35 bg-success/10",
  review: "border-warning/35 bg-warning/10",
  blocked: "border-danger/35 bg-danger/10",
  neutral: "border-border/70 bg-secondary/25"
};

const levelText: Record<ReadinessConsoleLevel, string> = {
  ready: "Ready",
  review: "Review",
  blocked: "Blocked",
  neutral: "Info"
};

const panelIcons: Record<ReadinessConsolePanel["id"], typeof ShieldCheck> = {
  "latest-runs": TrendingUp,
  "active-paper-session": Activity,
  "provider-trust": RadioTower,
  "reconciliation-breaks": ClipboardList,
  "promotion-blockers": ShieldCheck,
  "governance-report-packs": FileCheck2
};

export function OperatorReadinessConsole({
  research,
  trading,
  dataOperations,
  governance,
  reporting
}: OperatorReadinessConsoleProps) {
  const vm = useOperatorReadinessConsoleViewModel({
    research,
    trading,
    dataOperations,
    governance,
    reporting
  });

  return (
    <div className="space-y-6">
      <span className="sr-only" aria-live="polite">{vm.statusAnnouncement}</span>

      <section
        role="region"
        aria-label="Readiness control strip"
        className="panel-surface-strong flex flex-wrap items-center justify-between gap-3 px-4 py-4"
      >
        <div className="min-w-0">
          <div className="eyebrow-label">Trading readiness</div>
          <h2 className="mt-2 font-display text-[1.375rem] font-semibold leading-tight text-foreground">
            {vm.title}
          </h2>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">{vm.subtitle}</p>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2">
          <ConsoleChip label="As of" value={vm.asOf} />
          <ConsoleChip label="Inbox" value={vm.inboxSummary} />
          <span className={cn("rounded-sm border px-2.5 py-1 font-mono text-[10px] font-medium uppercase tracking-[0.14em]", levelPanel[vm.overallLevel])}>
            {vm.overallLabel}
          </span>
        </div>
      </section>

      <section className="grid gap-4 xl:grid-cols-[1.25fr_0.75fr]">
        <Card className={cn("panel-surface-strong border", levelPanel[vm.overallLevel])}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <div className="eyebrow-label">Trading Readiness</div>
                <CardTitle className="mt-2 flex items-center gap-2">
                  <ShieldCheck className="h-5 w-5 text-primary" />
                  {vm.title}
                </CardTitle>
                <CardDescription className="mt-2">{vm.subtitle}</CardDescription>
              </div>
              <Badge variant={levelBadge[vm.overallLevel]}>{vm.overallLabel}</Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            <p className="text-sm leading-6 text-foreground/85">{vm.overallDetail}</p>
            <div className="flex flex-wrap items-center gap-2">
              <ConsoleChip label="Snapshot" value={vm.asOf} />
              <ConsoleChip label="Operator inbox" value={vm.inboxSummary} />
            </div>
            {vm.inboxLoadingLabel ? (
              <p role="status" className="text-sm text-muted-foreground">{vm.inboxLoadingLabel}</p>
            ) : null}
            {vm.inboxErrorText ? (
              <div role="alert" className="rounded-lg border border-warning/35 bg-warning/10 px-3 py-2 text-sm text-warning">
                {vm.inboxErrorText}
              </div>
            ) : null}
            <div className="flex flex-wrap gap-2">
              <Button asChild variant="secondary" size="sm">
                <Link to="/trading">Trading cockpit</Link>
              </Button>
              <Button asChild variant="outline" size="sm">
                <Link to="/accounting/reconciliation">Break queue</Link>
              </Button>
              <Button asChild variant="outline" size="sm">
                <Link to="/reporting">Report packs</Link>
              </Button>
            </div>
          </CardContent>
        </Card>

        <Card aria-labelledby="api-contract-coverage-title" className="panel-surface">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <div className="eyebrow-label">API Contract Coverage</div>
                <CardTitle id="api-contract-coverage-title">Shared sources</CardTitle>
                <CardDescription>Local API payload health for readiness review.</CardDescription>
              </div>
              <ConsoleChip label="Sources" value={String(vm.apiSources.length)} />
            </div>
          </CardHeader>
          <CardContent className="space-y-2" role="list" aria-label={vm.apiSourcesLabel}>
            {vm.apiSources.map((source) => (
              <div
                key={source.id}
                role="listitem"
                aria-label={source.ariaLabel}
                className="rounded-lg border border-border/70 bg-secondary/25 px-3 py-2"
              >
                <div className="flex items-center justify-between gap-3">
                  <span className="text-sm font-semibold">{source.label}</span>
                  <Badge variant={levelBadge[source.level]} aria-label={source.statusAriaLabel}>{source.status}</Badge>
                </div>
                <p className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{source.endpoint}</p>
              </div>
            ))}
          </CardContent>
        </Card>
      </section>

      <section className="grid gap-3 md:grid-cols-2 xl:grid-cols-6" aria-label={vm.metricsLabel}>
        {vm.metrics.map((metric) => (
          <div
            key={metric.id}
            role="group"
            aria-label={metric.ariaLabel}
            className={cn("metric-tile border", levelPanel[metric.level])}
          >
            <div className="flex items-start justify-between gap-2">
              <div>
                <p className="eyebrow-label">{metric.label}</p>
                <p className="mt-2 break-words font-mono text-sm font-semibold text-foreground">{metric.value}</p>
              </div>
              <Badge variant={levelBadge[metric.level]} aria-label={metric.statusAriaLabel}>{levelText[metric.level]}</Badge>
            </div>
            <p className="mt-2 text-xs leading-5 text-foreground/75">{metric.detail}</p>
          </div>
        ))}
      </section>

      <section className="grid gap-4 xl:grid-cols-2">
        {vm.panels.map((panel) => (
          <ConsolePanel key={panel.id} panel={panel} />
        ))}
      </section>

      <Card role="region" aria-label={vm.workItemsRegionLabel} className="panel-surface">
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <div className="eyebrow-label">Operator Inbox</div>
              <CardTitle>Review work items</CardTitle>
              <CardDescription>{vm.workItemsSummary}</CardDescription>
            </div>
            <ConsoleChip label="Visible" value={String(vm.workItems.length)} />
          </div>
        </CardHeader>
        <CardContent>
          {vm.workItemsOverflowText ? (
            <p role="status" className="mb-3 rounded-lg border border-border/70 bg-secondary/25 px-3 py-2 text-sm text-muted-foreground">
              {vm.workItemsOverflowText}
            </p>
          ) : null}
          {vm.workItems.length > 0 ? (
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3" role="list" aria-label={vm.workItemsListLabel}>
              {vm.workItems.map((item) => (
                <div key={item.id} role="listitem">
                  <ReadinessRow row={item} />
                </div>
              ))}
            </div>
          ) : (
            <EmptyConsoleState text="No operator work items returned." />
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function ConsolePanel({ panel }: { panel: ReadinessConsolePanel }) {
  const Icon = panelIcons[panel.id];
  const titleId = `${panel.id}-title`;

  return (
    <Card role="region" aria-label={panel.ariaLabel} className="panel-surface">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <CardTitle id={titleId} className="flex items-center gap-2 text-base">
            <Icon className="h-4 w-4 text-primary" />
            {panel.title}
          </CardTitle>
          <ConsoleChip label="Rows" value={String(panel.rows.length)} />
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        {panel.rows.length > 0 ? (
          <div role="list" aria-label={panel.listLabel} className="space-y-3">
            {panel.rows.map((row) => (
              <div key={row.id} role="listitem">
                <ReadinessRow row={row} />
              </div>
            ))}
          </div>
        ) : (
          <EmptyConsoleState text={panel.emptyText} />
        )}
      </CardContent>
    </Card>
  );
}

function ReadinessRow({ row }: { row: ReadinessConsoleRow }) {
  return (
    <div
      role="group"
      aria-label={row.ariaLabel}
      aria-describedby={row.detailId}
      className={cn("rounded-lg border bg-card px-3 py-3 shadow-[var(--shadow-panel)]", levelPanel[row.level])}
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="text-sm font-semibold text-foreground">{row.label}</div>
          <div className="mt-1 break-words font-mono text-xs text-muted-foreground">{row.meta}</div>
        </div>
        <Badge variant={levelBadge[row.level]} aria-label={row.statusAriaLabel}>{row.value}</Badge>
      </div>
      <p id={row.detailId} className="mt-2 text-xs leading-5 text-foreground/80">{row.detail}</p>
      {row.action ? (
        <div className="mt-3">
          <Button asChild variant={row.action.variant} size="sm">
            <Link to={row.action.route} aria-label={row.action.ariaLabel}>
              {row.action.label}
            </Link>
          </Button>
        </div>
      ) : null}
    </div>
  );
}

function EmptyConsoleState({ text }: { text: string }) {
  return (
    <div role="status" className="rounded-lg border border-dashed border-border/80 bg-secondary/20 px-3 py-4 text-sm text-muted-foreground">
      {text}
    </div>
  );
}

function ConsoleChip({ label, value }: { label: string; value: string }) {
  return (
    <span className="toolbar-chip">
      <span className="text-muted-foreground">{label}</span>
      <span className="min-w-0 break-words font-mono text-foreground">{value}</span>
    </span>
  );
}
