import { FileText, Landmark, Network } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
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
  const { pathname } = useLocation();
  const vm = useReportingScreenViewModel(data?.reporting ?? null, undefined, pathname);

  if (!data) {
    return (
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>{vm.loadingTitle}</CardTitle>
          <CardDescription>{vm.loadingDetail}</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  return (
    <div className="space-y-8">
      <section
        role="region"
        aria-label="Reporting workbench context"
        className="panel-surface-strong flex flex-wrap items-center justify-between gap-3 px-4 py-4"
      >
        <div className="min-w-0">
          <div className="eyebrow-label">Reporting lane</div>
          <h2 className="mt-2 font-display text-[1.375rem] font-semibold leading-tight text-foreground">
            Governed export workbench
          </h2>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">
            Report packs, export routes, and review evidence stay in one cockpit so governed output can be
            checked before it leaves Reporting.
          </p>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2">
          <Button asChild variant="outline" size="sm">
            <Link to="/reporting/evidence?subjectKind=report-pack&subjectId=current">
              <Network className="h-4 w-4" aria-hidden="true" />
              Evidence
            </Link>
          </Button>
          {vm.workbenchChips.map((chip) => (
            <ReportingChip key={chip.label} label={chip.label} value={chip.value} />
          ))}
        </div>
      </section>

      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {data.metrics.map((metric) => (
          <MetricCard key={metric.id} {...metric} />
        ))}
      </section>

      {vm.workflowTaskPanel ? (
        <section
          role="region"
          aria-label={vm.workflowTaskPanel.regionLabel}
          className="panel-surface-strong grid gap-4 px-4 py-4 xl:grid-cols-[1.05fr_0.95fr]"
        >
          <div className="min-w-0 space-y-4">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <div className="eyebrow-label">{vm.workflowTaskPanel.eyebrow}</div>
                <h3 className="mt-2 text-lg font-semibold text-foreground">{vm.workflowTaskPanel.title}</h3>
                <p className="mt-2 max-w-3xl text-sm leading-6 text-muted-foreground">
                  {vm.workflowTaskPanel.description}
                </p>
              </div>
              <Badge variant={vm.workflowTaskPanel.statusVariant}>
                {vm.workflowTaskPanel.statusLabel}
              </Badge>
            </div>
            <div className="flex flex-wrap gap-2">
              {vm.workflowTaskPanel.chips.map((chip) => (
                <ReportingChip key={chip.label} label={chip.label} value={chip.value} />
              ))}
            </div>
            <div
              role="status"
              aria-label="Selected report-pack profile"
              className="rounded-md border border-primary/25 bg-primary/10 px-3 py-2 text-sm leading-6 text-primary"
            >
              {vm.workflowTaskPanel.selectedSummary}
            </div>
            <div className="grid gap-3 md:grid-cols-2">
              <div>
                <div className="eyebrow-label">Targets</div>
                <div role="list" aria-label={vm.workflowTaskPanel.targetsLabel} className="mt-2 grid gap-2">
                  {vm.workflowTaskPanel.targets.length > 0 ? (
                    vm.workflowTaskPanel.targets.map((target) => (
                      <div
                        key={target.id}
                        role="listitem"
                        aria-label={target.ariaLabel}
                        className="rounded-md border border-border/70 bg-secondary/25 px-3 py-2"
                      >
                        <span className="font-mono text-sm text-foreground">{target.label}</span>
                      </div>
                    ))
                  ) : (
                    <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                      No report-pack targets loaded.
                    </p>
                  )}
                </div>
              </div>
              <div>
                <div className="eyebrow-label">Backend</div>
                <div aria-label={vm.workflowTaskPanel.backendLinksLabel} className="mt-2 grid gap-2">
                  {vm.workflowTaskPanel.backendLinks.map((link) => (
                    <a
                      key={link.id}
                      href={link.href}
                      target="_blank"
                      rel="noreferrer"
                      aria-label={link.ariaLabel}
                      className="flex min-w-0 items-center gap-2 rounded-md border border-border/70 bg-secondary/25 px-3 py-2 text-sm hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40"
                    >
                      <Badge variant="outline">{link.method}</Badge>
                      <span className="min-w-0">
                        <span className="block font-semibold text-foreground">{link.label}</span>
                        <span className="block break-all font-mono text-[11px] text-muted-foreground">{link.href}</span>
                      </span>
                    </a>
                  ))}
                </div>
              </div>
            </div>
          </div>
          <div>
            <div className="eyebrow-label">Approval profile</div>
            <div role="list" aria-label={vm.workflowTaskPanel.profileListLabel} className="mt-3 grid gap-2">
              {vm.workflowTaskPanel.profiles.map((profile) => (
                <button
                  key={profile.id}
                  type="button"
                  aria-pressed={profile.isSelected}
                  aria-label={profile.selectAriaLabel}
                  onClick={() => vm.selectProfile(profile.id)}
                  className={cn(
                    "rounded-md border px-3 py-3 text-left transition-colors focus:outline-none focus:ring-2 focus:ring-primary/40",
                    profile.isSelected
                      ? "border-primary/45 bg-primary/10"
                      : "border-border/70 bg-secondary/25 hover:bg-secondary/45"
                  )}
                >
                  <span className="flex items-start justify-between gap-3">
                    <span>
                      <span className="block font-semibold text-foreground">{profile.name}</span>
                      <span className="mt-1 block text-xs text-muted-foreground">{profile.summary}</span>
                    </span>
                    <Badge variant={profile.readinessVariant}>{profile.readinessLabel}</Badge>
                  </span>
                </button>
              ))}
            </div>
          </div>
        </section>
      ) : null}

      <section className="grid gap-4 xl:grid-cols-[1.2fr_0.8fr]">
        <Card className="panel-surface">
          <CardHeader>
            <div className="eyebrow-label">Reporting Lane</div>
            <CardTitle className="flex items-center gap-2">
              <FileText className="h-5 w-5 text-primary" />
              Evidence and distribution
            </CardTitle>
            <CardDescription>
              Export profiles stay tied to accounting evidence, loader posture, and governed delivery targets.
            </CardDescription>
          </CardHeader>
          <CardContent className="grid gap-3 md:grid-cols-3">
            <ReportingHighlight
              title="Profile wiring"
              description="Each queue row keeps profile ID, format, target tool, and loader posture visible before export."
            />
            <ReportingHighlight
              title="Pack routing"
              description="Report-pack targets map the same evidence to board, audit, and compliance distribution lanes."
            />
            <ReportingHighlight
              title="Review posture"
              description="Dictionary and loader evidence remain attached so governed output can be reviewed without context switching."
            />
          </CardContent>
        </Card>

        <Card className="panel-surface-strong text-slate-50">
          <CardHeader>
            <div className="eyebrow-label">Pack targets</div>
            <CardTitle>Report-pack targets</CardTitle>
            <CardDescription className="text-slate-300">{vm.packTargetsSummary}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3 text-sm text-slate-200">
            <div className="flex flex-wrap gap-2">
              {vm.packTargetChips.map((chip) => (
                <ReportingChip key={chip.label} label={chip.label} value={chip.value} />
              ))}
            </div>
            {vm.hasPackTargets ? (
              <div
                role="list"
                aria-label={vm.packTargetsListLabel}
                className="data-grid-surface space-y-2 border-0 bg-background/40 p-3"
              >
                {vm.packTargets.map((target) => (
                  <div
                    key={target.id}
                    role="listitem"
                    aria-label={target.ariaLabel}
                    className="flex items-center justify-between gap-3 rounded-md border border-border/70 bg-background/20 px-3 py-2"
                  >
                    <span className="inline-flex items-center gap-2">
                      <FileText className="h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
                      <span className="font-mono capitalize text-foreground">{target.label}</span>
                    </span>
                    <Badge variant="outline">Target</Badge>
                  </div>
                ))}
              </div>
            ) : (
              <p role="status" aria-label="No report-pack targets" className="text-slate-300">Configure report-pack targets in the governance policy.</p>
            )}
          </CardContent>
        </Card>
      </section>

      <section className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
        <Card className="panel-surface">
          <CardHeader>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Landmark className="h-4 w-4 text-primary" />
                  Governed export queue
                </CardTitle>
                <CardDescription className="mt-2">{vm.description}</CardDescription>
              </div>
              <Badge variant="outline">{vm.countLabel}</Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap items-center gap-2">
              {vm.queueChips.map((chip) => (
                <ReportingChip key={chip.label} label={chip.label} value={chip.value} />
              ))}
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
                       <div className="flex flex-col gap-3">
                         <div className="flex items-start justify-between gap-3">
                           <div className="min-w-0">
                             <div className="font-semibold text-foreground">{profile.name}</div>
                             <div className="mt-1 font-mono text-xs text-muted-foreground">{profile.id}</div>
                           </div>
                           <div className="flex flex-wrap justify-end gap-2">
                             <Badge variant="outline">{profile.formatLabel}</Badge>
                             {profile.badges.map((badge) => (
                               <Badge key={badge.label} variant={badge.variant}>
                                 {badge.label}
                               </Badge>
                             ))}
                           </div>
                         </div>
                         <div className="grid gap-2 sm:grid-cols-3">
                           <ReportingEvidenceField label="Profile ID" value={profile.id} />
                           <ReportingEvidenceField label="Target" value={profile.targetLabel} />
                           <ReportingEvidenceField label="Format" value={profile.formatLabel} />
                         </div>
                         <p className="text-sm leading-6 text-muted-foreground">
                           {profile.description}
                         </p>
                       </div>
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
          role="complementary"
          aria-label={vm.statusTitle}
          aria-live="polite"
          className="panel-surface h-fit min-w-0 overflow-hidden p-4"
        >
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              <div className="eyebrow-label">Selected profile inspector</div>
              <h3 className="mt-2 text-base font-semibold text-foreground">
                {vm.selectedProfile?.title ?? vm.statusTitle}
              </h3>
              <p className="mt-1 font-mono text-xs text-muted-foreground">
                {vm.selectedProfile ? `${vm.selectedProfile.id} · ${vm.selectedProfile.subtitle}` : vm.nextAction}
              </p>
            </div>
            <Badge variant={vm.selectedProfile ? "default" : "outline"}>
              {vm.selectedProfile ? "Selected" : "Waiting"}
            </Badge>
          </div>
          <p className="mt-3 text-sm leading-6 text-muted-foreground">{vm.statusDetail}</p>
          {vm.exportStatus ? (
            <div
              role="status"
              aria-label={vm.exportStatus.ariaLabel}
              className={cn("mt-3 space-y-3 rounded-md border px-3 py-2 text-sm leading-6", vm.exportStatus.className)}
            >
              <p>{vm.exportStatus.text}</p>
              {vm.exportStatus.fields.length > 0 ? (
                <dl className="grid gap-2 sm:grid-cols-2">
                  {vm.exportStatus.fields.map((field) => (
                    <div
                      key={field.label}
                      className="rounded-sm border border-border/60 bg-background/25 px-2.5 py-2"
                    >
                      <dt className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
                        {field.label}
                      </dt>
                      <dd className={cn("mt-1 break-words font-mono text-xs", field.className)}>
                        {field.value}
                      </dd>
                    </div>
                  ))}
                </dl>
              ) : null}
              {vm.exportStatus.warnings.length > 0 ? (
                <ul className="space-y-1 rounded-sm border border-warning/30 bg-warning/10 px-2.5 py-2 text-xs text-warning">
                  {vm.exportStatus.warnings.map((warning) => (
                    <li key={warning}>{warning}</li>
                  ))}
                </ul>
              ) : null}
              {vm.exportStatus.artifacts.length > 0 ? (
                <dl
                  aria-label="Export artifacts"
                  className="space-y-1 rounded-sm border border-border/60 bg-background/25 px-2.5 py-2"
                >
                  {vm.exportStatus.artifacts.map((artifact) => (
                    <div key={`${artifact.label}-${artifact.value}`} className="grid gap-1">
                      <dt className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
                        {artifact.label}
                      </dt>
                      <dd className={cn("break-words font-mono text-xs", artifact.className)}>
                        {artifact.value}
                      </dd>
                    </div>
                  ))}
                </dl>
              ) : null}
            </div>
          ) : null}
          <p className="mt-3 font-mono text-xs text-muted-foreground">{vm.nextAction}</p>
          {vm.selectedProfile ? (
            <div className="mt-4 space-y-3 border-t border-border/70 pt-4">
              <p className="text-sm leading-6 text-muted-foreground">{vm.selectedProfile.description}</p>
              <div
                role="status"
                aria-label={`${vm.selectedProfile.title} readiness`}
                className="rounded-md border border-primary/25 bg-primary/10 px-3 py-2 text-sm leading-6 text-primary"
              >
                {vm.selectedProfile.readinessSummary}
              </div>
              <dl className="grid gap-2">
                {vm.selectedProfile.fields.map((field) => (
                  <div
                    key={field.label}
                    className="grid grid-cols-[minmax(0,0.6fr)_minmax(0,1fr)] items-start gap-3 rounded-md border border-border/60 bg-secondary/25 px-3 py-2"
                  >
                    <dt className="text-xs text-muted-foreground">{field.label}</dt>
                    <dd className={cn("text-right font-mono text-xs", field.className)}>
                      {field.value}
                    </dd>
                  </div>
                ))}
              </dl>
              <div className="grid gap-2 pt-2" role="list" aria-label={`${vm.selectedProfile.title} export actions`}>
                {vm.selectedProfile.actions.map((action) => (
                  <div
                    key={action.id}
                    role="listitem"
                    className="rounded-md border border-border/60 bg-secondary/20 px-3 py-2"
                  >
                    <div className="flex flex-wrap items-center gap-2">
                      <Button
                        asChild={action.method === "GET" && !action.isDisabled}
                        disabled={action.isDisabled}
                        busy={action.isRunning}
                        busyLabel={action.isRunning ? "Running export…" : null}
                        size="sm"
                        variant={action.variant}
                        aria-label={action.ariaLabel}
                        aria-describedby={action.describedById}
                        title={action.disabledReason ?? undefined}
                        onClick={
                          action.method === "POST"
                            ? () => void vm.runExport(action.profileId, vm.selectedProfile!.title)
                            : undefined
                        }
                      >
                        {action.isDisabled ? (
                          action.label
                        ) : action.method === "POST" ? (
                          action.label
                        ) : (
                          <a href={action.href} target="_blank" rel="noreferrer" aria-label={action.ariaLabel}>
                            {action.label}
                          </a>
                        )}
                      </Button>
                      {action.disabledReason ? (
                        <Badge variant="warning">Disabled</Badge>
                      ) : action.isRunning ? (
                        <Badge variant="warning">Running</Badge>
                      ) : (
                        <Badge variant="outline">{action.method}</Badge>
                      )}
                    </div>
                    <p id={action.describedById} className="mt-2 text-xs leading-5 text-muted-foreground">
                      {action.disabledReason ?? action.statusText}
                    </p>
                  </div>
                ))}
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
    <div className="rounded-lg border border-border/70 bg-secondary/35 p-4">
      <div className="font-semibold">{title}</div>
      <p className="mt-2 text-sm leading-6 text-muted-foreground">{description}</p>
    </div>
  );
}

function ReportingChip({ label, value }: { label: string; value: string }) {
  return (
    <div className="toolbar-chip" aria-label={`${label} ${value}`}>
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono text-foreground">{value}</span>
    </div>
  );
}

function ReportingEvidenceField({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border border-border/60 bg-background/20 px-3 py-2">
      <div className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{label}</div>
      <div className="mt-1 font-mono text-xs text-foreground">{value}</div>
    </div>
  );
}

