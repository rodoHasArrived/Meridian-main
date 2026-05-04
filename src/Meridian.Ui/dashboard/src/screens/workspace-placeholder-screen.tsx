import { ArrowRight, CheckCircle2, Clock3, Sparkles } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Link } from "react-router-dom";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { buildWorkspacePlaceholderViewModel } from "@/screens/workspace-placeholder-screen.view-model";
import type { SessionInfo, SystemOverviewResponse, WorkspaceSummary } from "@/types";

interface WorkspacePlaceholderScreenProps {
  workspace: WorkspaceSummary;
  session: SessionInfo | null;
  overview: SystemOverviewResponse | null;
}

export function WorkspacePlaceholderScreen({ workspace, session, overview }: WorkspacePlaceholderScreenProps) {
  const viewModel = buildWorkspacePlaceholderViewModel({ workspace, session, overview });
  const [primaryAction, ...secondaryActions] = viewModel.actions;

  return (
    <div className="space-y-6">
      <Card role="region" aria-label={viewModel.routeRegionLabel} className="border-border/70 bg-panel-strong">
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div className="space-y-2">
              <div className="eyebrow-label">Workspace route</div>
              <CardTitle className="flex items-center gap-2">
                <CheckCircle2 className="h-5 w-5 text-success" />
                {viewModel.title}
              </CardTitle>
              <CardDescription>{viewModel.description}</CardDescription>
            </div>
            <Badge variant="outline" className="self-start">
              {viewModel.routeStatus}
            </Badge>
          </div>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          {viewModel.statusCells.map((cell) => (
            <StatusCell key={cell.id} label={cell.label} value={cell.value} ariaLabel={cell.ariaLabel} />
          ))}
        </CardContent>
      </Card>

      <div className="grid gap-6 xl:grid-cols-[1.2fr_0.8fr]">
        <Card className="border-border/70" role="region" aria-label={viewModel.pendingRegionLabel}>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <Clock3 className="h-4 w-4 text-warning" />
              {viewModel.pendingTitle}
            </CardTitle>
            <CardDescription>{viewModel.pendingDescription}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            {primaryAction ? (
              <div className="rounded-lg border border-primary/20 bg-primary/5 p-4">
                <div className="eyebrow-label">Recommended next stop</div>
                <Link
                  to={primaryAction.route}
                  aria-label={primaryAction.ariaLabel}
                  aria-describedby={primaryAction.detailId}
                  className="mt-3 flex items-start justify-between gap-3 rounded-md border border-primary/25 bg-background/80 px-4 py-4 text-sm transition-colors hover:bg-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                >
                  <span>
                    <span className="block text-base font-semibold text-foreground">{primaryAction.label}</span>
                    <span id={primaryAction.detailId} className="mt-1 block text-sm leading-6 text-muted-foreground">
                      {primaryAction.detail}
                    </span>
                    <span className="mt-3 inline-flex font-mono text-[11px] text-primary">{primaryAction.routeLabel}</span>
                  </span>
                  <ArrowRight className="mt-1 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
                </Link>
              </div>
            ) : null}

            <section aria-label={viewModel.coverageLabel} className="space-y-3">
              <div>
                <div className="eyebrow-label">What Meridian already covers</div>
                <h3 className="mt-2 text-base font-semibold text-foreground">{viewModel.coverageTitle}</h3>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">{viewModel.coverageDescription}</p>
              </div>
              <ul className="grid gap-3 md:grid-cols-3">
                {viewModel.coverageItems.map((item) => (
                  <li key={item.id} className="rounded-md border border-border/70 bg-secondary/25 px-4 py-3">
                    <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
                      <Sparkles className="h-4 w-4 text-primary" aria-hidden="true" />
                      {item.title}
                    </div>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{item.detail}</p>
                  </li>
                ))}
              </ul>
            </section>

            {secondaryActions.length > 0 ? (
              <nav aria-label={viewModel.actionsLabel} className="grid gap-3 md:grid-cols-2">
                {secondaryActions.map((action) => (
                  <Link
                    key={action.id}
                    to={action.route}
                    aria-label={action.ariaLabel}
                    aria-describedby={action.detailId}
                    className="flex items-start justify-between gap-3 rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm hover:bg-secondary/50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                  >
                    <span>
                      <span className="block font-semibold">{action.label}</span>
                      <span id={action.detailId} className="mt-1 block text-xs leading-5 text-muted-foreground">
                        {action.detail}
                      </span>
                      <span className="mt-2 block font-mono text-[11px] text-muted-foreground">{action.routeLabel}</span>
                    </span>
                    <ArrowRight className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                  </Link>
                ))}
              </nav>
            ) : null}
          </CardContent>
        </Card>

        <Card className="border-border/70" role="region" aria-label={viewModel.telemetryLabel}>
          <CardHeader>
            <div className="eyebrow-label">Route telemetry</div>
            <CardTitle className="text-base">Delivery posture and operator context</CardTitle>
            <CardDescription>
              Keep the reserved route visible in navigation while validating the current operating posture.
            </CardDescription>
          </CardHeader>
          <CardContent className="grid gap-3">
            {viewModel.telemetryCells.map((cell) => (
              <StatusCell key={cell.id} label={cell.label} value={cell.value} ariaLabel={cell.ariaLabel} />
            ))}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

function StatusCell({ label, value, ariaLabel }: { label: string; value: string; ariaLabel: string }) {
  return (
    <div className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3" aria-label={ariaLabel}>
      <div className="eyebrow-label">{label}</div>
      <div className="mt-2 text-sm font-semibold text-foreground">{value}</div>
    </div>
  );
}
