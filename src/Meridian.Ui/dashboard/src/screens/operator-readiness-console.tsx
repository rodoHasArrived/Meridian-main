import { Activity, ClipboardList, FileCheck2, RadioTower, ShieldCheck, TrendingUp } from "lucide-react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import {
  useOperatorReadinessConsoleViewModel,
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

export function OperatorReadinessConsole({
  research,
  trading,
  dataOperations,
  governance
}: OperatorReadinessConsoleProps) {
  const vm = useOperatorReadinessConsoleViewModel({
    research,
    trading,
    dataOperations,
    governance
  });

  return (
    <div className="space-y-6">
      <span className="sr-only" aria-live="polite">{vm.statusAnnouncement}</span>

      <section className="grid gap-4 xl:grid-cols-[1.25fr_0.75fr]">
        <Card className={cn("border", levelPanel[vm.overallLevel])}>
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
            <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
              <span className="font-mono">As of {vm.asOf}</span>
              <span aria-hidden="true">/</span>
              <span>{vm.inboxSummary}</span>
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

        <Card>
          <CardHeader>
            <div className="eyebrow-label">API Contract Coverage</div>
            <CardTitle>Shared sources</CardTitle>
            <CardDescription>Local API payload health for readiness review.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-2">
            {vm.apiSources.map((source) => (
              <div key={source.id} className="rounded-lg border border-border/70 bg-secondary/25 px-3 py-2">
                <div className="flex items-center justify-between gap-3">
                  <span className="text-sm font-semibold">{source.label}</span>
                  <Badge variant={levelBadge[source.level]}>{source.status}</Badge>
                </div>
                <p className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{source.endpoint}</p>
              </div>
            ))}
          </CardContent>
        </Card>
      </section>

      <section className="grid gap-3 md:grid-cols-2 xl:grid-cols-6" aria-label="Operator readiness metrics">
        {vm.metrics.map((metric) => (
          <div key={metric.id} className={cn("rounded-lg border px-3 py-3", levelPanel[metric.level])}>
            <div className="flex items-start justify-between gap-2">
              <div>
                <p className="eyebrow-label">{metric.label}</p>
                <p className="mt-2 break-words font-mono text-sm font-semibold text-foreground">{metric.value}</p>
              </div>
              <Badge variant={levelBadge[metric.level]}>{levelText[metric.level]}</Badge>
            </div>
            <p className="mt-2 text-xs leading-5 text-foreground/75">{metric.detail}</p>
          </div>
        ))}
      </section>

      <section className="grid gap-4 xl:grid-cols-2">
        <ConsolePanel icon={TrendingUp} title="Latest runs" emptyText="No Strategy runs loaded." rows={vm.latestRuns} />
        <ConsolePanel icon={Activity} title="Active paper session" emptyText="No active paper session loaded." rows={vm.activeSessionFacts} />
        <ConsolePanel icon={RadioTower} title="Provider trust" emptyText="No provider trust rows loaded." rows={vm.providerTrustRows} />
        <ConsolePanel icon={ClipboardList} title="Reconciliation breaks" emptyText="No open or in-review reconciliation breaks." rows={vm.reconciliationRows} />
        <ConsolePanel icon={ShieldCheck} title="Promotion blockers" emptyText="No promotion blockers surfaced by readiness." rows={vm.promotionRows} />
        <ConsolePanel icon={FileCheck2} title="Governance report packs" emptyText="No reporting readiness payload loaded." rows={vm.reportPackFacts} />
      </section>

      <Card>
        <CardHeader>
          <div className="eyebrow-label">Operator Inbox</div>
          <CardTitle>Review work items</CardTitle>
          <CardDescription>Warning and critical items from the shared operator-inbox contract.</CardDescription>
        </CardHeader>
        <CardContent>
          {vm.workItems.length > 0 ? (
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
              {vm.workItems.map((item) => <ReadinessRow key={item.id} row={item} />)}
            </div>
          ) : (
            <EmptyConsoleState text="No operator work items returned." />
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function ConsolePanel({
  icon: Icon,
  title,
  emptyText,
  rows
}: {
  icon: typeof ShieldCheck;
  title: string;
  emptyText: string;
  rows: ReadinessConsoleRow[];
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Icon className="h-4 w-4 text-primary" />
          {title}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        {rows.length > 0 ? rows.map((row) => <ReadinessRow key={row.id} row={row} />) : <EmptyConsoleState text={emptyText} />}
      </CardContent>
    </Card>
  );
}

function ReadinessRow({ row }: { row: ReadinessConsoleRow }) {
  return (
    <div className={cn("rounded-lg border px-3 py-3", levelPanel[row.level])}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="text-sm font-semibold text-foreground">{row.label}</div>
          <div className="mt-1 break-words font-mono text-xs text-muted-foreground">{row.meta}</div>
        </div>
        <Badge variant={levelBadge[row.level]}>{row.value}</Badge>
      </div>
      <p className="mt-2 text-xs leading-5 text-foreground/80">{row.detail}</p>
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
