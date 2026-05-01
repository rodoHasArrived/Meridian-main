import { FileText, Landmark } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { MetricCard } from "@/components/meridian/metric-card";
import { cn } from "@/lib/utils";
import { useReportingScreenViewModel } from "@/screens/reporting-screen.view-model";
import type { GovernanceWorkspaceResponse } from "@/types";

interface ReportingScreenProps {
  data: GovernanceWorkspaceResponse | null;
}

export function ReportingScreen({ data }: ReportingScreenProps) {
  const vm = useReportingScreenViewModel(data?.reporting ?? null);

  if (!data) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Loading Reporting</CardTitle>
          <CardDescription>
            Waiting for governed report-pack and export-profile data.
          </CardDescription>
        </CardHeader>
      </Card>
    );
  }

  return (
    <div className="space-y-8">
      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {data.metrics.map((metric) => (
          <MetricCard key={metric.id} {...metric} />
        ))}
      </section>

      <section className="grid gap-4 xl:grid-cols-[1.2fr_0.8fr]">
        <Card>
          <CardHeader>
            <div className="eyebrow-label">Reporting Lane</div>
            <CardTitle className="flex items-center gap-2">
              <FileText className="h-5 w-5 text-primary" />
              Governed report packs
            </CardTitle>
            <CardDescription>
              Export profiles tied to accounting evidence, ledger outputs, and report-pack targets.
            </CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 md:grid-cols-3">
            <ReportingHighlight
              title="Export profiles"
              description="Profiles determine format, target tool, loader scripts, and data dictionaries for each governed export."
            />
            <ReportingHighlight
              title="Report-pack targets"
              description="Targets map export profiles to board, audit, and compliance outputs from the same accounting evidence."
            />
            <ReportingHighlight
              title="Approval posture"
              description="Governed outputs track approval and version state so restated reports remain auditable."
            />
          </CardContent>
        </Card>

        <Card className="bg-panel-strong text-slate-50">
          <CardHeader>
            <div className="eyebrow-label">Pack targets</div>
            <CardTitle>Report-pack targets</CardTitle>
            <CardDescription className="text-slate-300">{vm.packTargetsSummary}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-2 text-sm text-slate-200">
            {vm.hasPackTargets ? (
              vm.packTargets.map((target) => (
                <div
                  key={target}
                  className="flex items-center gap-2 rounded-lg border border-border/70 bg-background/20 px-3 py-2"
                >
                  <FileText className="h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
                  <span className="font-mono capitalize">{target}</span>
                </div>
              ))
            ) : (
              <p className="text-slate-300">Configure report-pack targets in the governance policy.</p>
            )}
          </CardContent>
        </Card>
      </section>

      <section className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
        <Card>
          <CardHeader>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Landmark className="h-4 w-4 text-primary" />
                  {vm.title}
                </CardTitle>
                <CardDescription className="mt-2">{vm.description}</CardDescription>
              </div>
              <span className="w-fit rounded-sm border border-primary/35 bg-primary/10 px-2 py-1 font-mono text-[10px] font-medium uppercase tracking-[0.14em] text-primary">
                {vm.countLabel}
              </span>
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            <div className="flex items-center justify-between text-xs">
              <span className="font-medium uppercase tracking-[0.14em] text-muted-foreground">
                {vm.listLabel}
              </span>
              <span className="font-mono text-muted-foreground">{vm.visibleCountLabel}</span>
            </div>
            <div role="list" aria-label={vm.listLabel} className="space-y-2">
              {vm.hasRows ? (
                vm.rows.map((profile) => (
                  <div key={profile.id} role="listitem">
                    <button
                      type="button"
                      aria-pressed={profile.isSelected}
                      aria-controls={vm.detailId}
                      aria-label={profile.selectAriaLabel}
                      onClick={() => vm.selectProfile(profile.id)}
                      className={cn(
                        "w-full rounded-lg border px-4 py-3 text-left transition-colors focus:outline-none focus:ring-2 focus:ring-primary/40",
                        profile.isSelected
                          ? "border-primary/45 bg-primary/10"
                          : "border-border/70 bg-secondary/30 hover:bg-secondary/45"
                      )}
                    >
                      <div className="flex items-start justify-between gap-3">
                        <div className="min-w-0">
                          <div className="font-semibold text-foreground">{profile.name}</div>
                          <div className="mt-1 truncate font-mono text-xs text-muted-foreground">
                            {profile.targetLabel}
                          </div>
                        </div>
                        <span className="shrink-0 rounded-sm border border-primary/30 bg-primary/10 px-2 py-0.5 font-mono text-[10px] uppercase tracking-[0.14em] text-primary">
                          {profile.formatLabel}
                        </span>
                      </div>
                      <p className="mt-2 line-clamp-2 text-sm leading-6 text-muted-foreground">
                        {profile.description}
                      </p>
                      {profile.badges.length > 0 && (
                        <div className="mt-3 flex flex-wrap gap-2">
                          {profile.badges.map((badge) => (
                            <span key={badge.label} className={badgeClass(badge.tone)}>
                              {badge.label}
                            </span>
                          ))}
                        </div>
                      )}
                    </button>
                  </div>
                ))
              ) : (
                <div
                  role="status"
                  className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning"
                >
                  {vm.emptyText}
                </div>
              )}
            </div>
          </CardContent>
        </Card>

        <aside
          id={vm.detailId}
          aria-live="polite"
          className="h-fit min-w-0 overflow-hidden rounded-lg border border-border/70 bg-background/35 p-4"
        >
          <div className="eyebrow-label">{vm.statusTitle}</div>
          <p className="mt-2 text-sm leading-6 text-muted-foreground">{vm.statusDetail}</p>
          <p className="mt-2 font-mono text-xs text-muted-foreground">{vm.nextAction}</p>
          {vm.selectedProfile ? (
            <div className="mt-4 space-y-3 border-t border-border/70 pt-4">
              <div className="font-semibold text-foreground">{vm.selectedProfile.title}</div>
              <div className="font-mono text-xs text-muted-foreground">{vm.selectedProfile.subtitle}</div>
              <p className="text-sm leading-6 text-muted-foreground">{vm.selectedProfile.description}</p>
              <dl className="grid gap-2">
                {vm.selectedProfile.fields.map((field) => (
                  <div
                    key={field.label}
                    className="grid grid-cols-[minmax(0,0.6fr)_minmax(0,1fr)] items-start gap-3 rounded-md border border-border/60 bg-secondary/25 px-3 py-2"
                  >
                    <dt className="text-xs text-muted-foreground">{field.label}</dt>
                    <dd className={cn("text-right font-mono text-xs", fieldToneClass(field.tone))}>
                      {field.value}
                    </dd>
                  </div>
                ))}
              </dl>
              <div className="flex gap-3 pt-2">
                <Button asChild size="sm">
                  <a href="/api/export/preview" target="_blank" rel="noreferrer">
                    Preview payload
                  </a>
                </Button>
                <Button asChild size="sm" variant="outline">
                  <a href="/api/export/analysis" target="_blank" rel="noreferrer">
                    Run export
                  </a>
                </Button>
              </div>
            </div>
          ) : null}
        </aside>
      </section>
    </div>
  );
}

function ReportingHighlight({ title, description }: { title: string; description: string }) {
  return (
    <div className="rounded-xl border border-border/70 bg-secondary/35 p-4">
      <div className="font-semibold">{title}</div>
      <p className="mt-2 text-sm leading-6 text-muted-foreground">{description}</p>
    </div>
  );
}

function badgeClass(tone: "primary" | "success" | "warning" | "muted") {
  return cn(
    "rounded-sm border px-2 py-0.5 font-mono text-[10px] uppercase tracking-[0.12em]",
    tone === "primary" && "border-primary/35 bg-primary/10 text-primary",
    tone === "success" && "border-success/35 bg-success/10 text-success",
    tone === "warning" && "border-warning/35 bg-warning/10 text-warning",
    tone === "muted" && "border-border/70 bg-secondary text-muted-foreground"
  );
}

function fieldToneClass(tone: "default" | "success" | "warning" | "muted") {
  if (tone === "success") return "text-success";
  if (tone === "warning") return "text-warning";
  if (tone === "muted") return "text-muted-foreground";
  return "text-foreground";
}
