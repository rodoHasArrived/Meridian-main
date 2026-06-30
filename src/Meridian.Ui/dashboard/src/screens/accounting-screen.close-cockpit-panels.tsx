import { AlertCircle, BookCheck, CheckCircle2, Landmark, LockKeyhole, Network, Paperclip, RefreshCcw, ShieldCheck, Table2, UserCheck, WalletCards, X } from "lucide-react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import { accountingToolingBadgeVariant, accountingToolingBorderClass } from "@/screens/accounting-screen.styles";
import { AccountingChip } from "@/screens/accounting-screen.workbench-context";
import type {
  AccountingCloseReportPackageViewModel,
  AccountingCloseWorkflowActionId,
  AccountingWorkflowLaunchViewState,
  CloseCommandCenterViewState
} from "@/screens/accounting-screen.view-model";

const accountingWorkflowStepIcons: Record<AccountingWorkflowLaunchViewState["steps"][number]["id"], typeof ShieldCheck> = {
  ledger: Table2,
  configure: Landmark,
  "journal-entries": BookCheck,
  "capital-accounts": WalletCards,
  reconciliation: Network,
  exceptions: AlertCircle,
  "security-master": ShieldCheck,
  approvals: UserCheck,
  reporting: Paperclip
};

const accountingWorkflowActionIcons: Record<string, typeof ShieldCheck> = {
  reconcile: Network,
  "journal-entry": BookCheck,
  approvals: UserCheck,
  evidence: Paperclip
};

export function AccountingWorkflowLaunchPanel({ view }: { view: AccountingWorkflowLaunchViewState }) {
  return (
    <section className="workspace-section-band" aria-labelledby="accounting-workflow-heading">
      <span className="sr-only" aria-live="polite">{view.liveRegionText}</span>
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <p className="eyebrow-label">Workflow</p>
          <h3 id="accounting-workflow-heading" className="workspace-section-title">{view.title}</h3>
          <p className="workspace-section-summary">{view.description}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={accountingToolingBadgeVariant(view.statusTone)} dot>{view.statusLabel}</Badge>
          <AccountingChip label="Active" value={view.activeLabel} />
        </div>
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_19rem]" role="region" aria-label={view.ariaLabel}>
        <div className="grid gap-2 md:grid-cols-2 xl:grid-cols-4">
          {view.steps.map((step) => {
            const Icon = accountingWorkflowStepIcons[step.id];
            return (
              <Link
                key={step.id}
                to={step.href}
                aria-label={step.ariaLabel}
                aria-current={step.isActive ? "page" : undefined}
                className={cn(
                  "group rounded-md border px-3 py-3 transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                  accountingToolingBorderClass(step.tone),
                  step.isActive && "border-primary/60 bg-primary/10"
                )}
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <Icon className={cn("h-4 w-4", step.isActive ? "text-primary" : "text-muted-foreground group-hover:text-primary")} aria-hidden="true" />
                      <span className="font-semibold text-foreground">{step.label}</span>
                    </div>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{step.caption}</p>
                  </div>
                  <Badge variant={accountingToolingBadgeVariant(step.tone)}>{step.statusLabel}</Badge>
                </div>
                <div className="mt-3 flex items-center justify-between gap-2 border-t border-border/60 pt-2 text-xs">
                  <span className="text-muted-foreground">{step.metricLabel}</span>
                  <span className="font-mono text-foreground">{step.metricValue}</span>
                </div>
              </Link>
            );
          })}
        </div>

        <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-3">
          <div className="text-xs font-semibold uppercase text-muted-foreground">Operator actions</div>
          <div className="mt-3 grid gap-2">
            {view.actionRows.map((action) => {
              const Icon = accountingWorkflowActionIcons[action.id] ?? ShieldCheck;
              return (
                <Button key={action.id} asChild variant={action.tone === "warning" || action.tone === "danger" ? "default" : "outline"} size="sm" className="h-auto justify-start py-2 text-left">
                  <Link to={action.href} aria-label={action.ariaLabel}>
                    <Icon className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                    <span className="min-w-0">
                      <span className="block font-semibold">{action.label}</span>
                      <span className="mt-1 block text-xs font-normal leading-5 text-muted-foreground">{action.detail}</span>
                    </span>
                  </Link>
                </Button>
              );
            })}
          </div>
        </div>
      </div>
    </section>
  );
}

export function CloseCommandCenterPanel({
  view,
  onRefresh
}: {
  view: CloseCommandCenterViewState;
  onRefresh: () => void;
}) {
  return (
    <section id="close-command-center" className="workspace-section-band" aria-labelledby="close-command-center-heading">
      <span className="sr-only" aria-live="polite">{view.liveRegionText}</span>
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <p className="eyebrow-label">Controller close</p>
          <h3 id="close-command-center-heading" className="workspace-section-title">{view.title}</h3>
          <p className="workspace-section-summary">{view.description}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={accountingToolingBadgeVariant(view.statusTone)} dot>{view.statusLabel}</Badge>
          <Button type="button" size="sm" variant="outline" disabled={view.status === "loading"} busy={view.status === "loading"} busyLabel="Refreshing close command center" onClick={onRefresh}>
            <RefreshCcw className={cn("h-3.5 w-3.5", view.status === "loading" && "animate-spin")} aria-hidden="true" />
            Refresh
          </Button>
        </div>
      </div>

      <Card className={cn("panel-surface", accountingToolingBorderClass(view.statusTone))} role="region" aria-label={view.ariaLabel}>
        <CardHeader>
          <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(18rem,0.35fr)]">
            <div className="min-w-0">
              <CardTitle className="flex items-center gap-2 text-base">
                {view.statusTone === "success" ? (
                  <CheckCircle2 className="h-4 w-4 text-success" aria-hidden="true" />
                ) : (
                  <AlertCircle className={cn("h-4 w-4", view.statusTone === "danger" ? "text-danger" : "text-warning")} aria-hidden="true" />
                )}
                {view.periodLabel}
              </CardTitle>
              <CardDescription className="mt-2">{view.summary}</CardDescription>
            </div>
            <div className="grid gap-2 text-sm">
              <CloseCommandCenterValue label="Fund account" value={view.fundAccountLabel} />
              <CloseCommandCenterValue label="Updated" value={view.updatedLabel} />
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {view.loadingText ? <p role="status" className="text-sm text-muted-foreground">{view.loadingText}</p> : null}
          {view.errorText ? (
            <div role="alert" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
              {view.errorText}
            </div>
          ) : null}

          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            {view.metricRows.map((metric) => {
              const body = (
                <div className={cn("h-full rounded-md border bg-secondary/20 px-3 py-3", accountingToolingBorderClass(metric.tone))}>
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0 text-xs font-semibold uppercase text-muted-foreground">{metric.label}</div>
                    <Badge variant={accountingToolingBadgeVariant(metric.tone)}>{metric.value}</Badge>
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{metric.detail}</p>
                </div>
              );

              return metric.href ? (
                <Link key={metric.id} to={metric.href} className="block focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40" aria-label={`Open ${metric.label} detail`}>
                  {body}
                </Link>
              ) : (
                <div key={metric.id}>{body}</div>
              );
            })}
          </div>

          <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_20rem]">
            <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-3">
              <div className="text-xs font-semibold uppercase text-muted-foreground">Blocking and at-risk items</div>
              {view.blockerRows.length > 0 ? (
                <div role="list" className="mt-3 space-y-2" aria-label="Close command center blockers">
                  {view.blockerRows.map((item) => (
                    <div key={item.id} role="listitem" className={cn("rounded-md border bg-background/45 px-3 py-2", accountingToolingBorderClass(item.tone))}>
                      <div className="flex flex-wrap items-start justify-between gap-2">
                        <span className="font-semibold text-foreground">{item.label}</span>
                        <Badge variant={accountingToolingBadgeVariant(item.tone)}>{item.tone === "danger" ? "Blocker" : "Review"}</Badge>
                      </div>
                      <p className="mt-1 text-sm leading-6 text-muted-foreground">{item.detail}</p>
                      <div className="mt-3 grid gap-2 text-xs sm:grid-cols-2">
                        <CloseCommandCenterValue label="Status" value={item.statusLabel} />
                        <CloseCommandCenterValue label="Impact" value={item.impactLabel} />
                        {item.ownerLabel ? <CloseCommandCenterValue label="Owner" value={item.ownerLabel} /> : null}
                        {item.dueLabel ? <CloseCommandCenterValue label="Due" value={item.dueLabel} /> : null}
                        <CloseCommandCenterValue label="Evidence" value={item.evidenceLabel} />
                        <div className="data-grid-surface px-3 py-2 sm:col-span-2">
                          <span className="text-muted-foreground">Action</span>
                          <p className="mt-1 break-words text-foreground">{item.actionLabel}</p>
                        </div>
                      </div>
                      {item.href ? <Link to={item.href} className="mt-2 inline-block text-xs font-medium text-primary hover:underline">Open evidence</Link> : null}
                    </div>
                  ))}
                </div>
              ) : (
                <p className="mt-3 text-sm text-muted-foreground">No blocking or at-risk close items are surfaced.</p>
              )}
            </div>
            <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-3">
              <div className="text-xs font-semibold uppercase text-muted-foreground">Close actions</div>
              <div className="mt-3 grid gap-2">
                {view.actionRows.map((action) => (
                  <Button key={action.id} asChild variant={action.tone === "success" ? "outline" : "default"} size="sm">
                    <Link to={action.href} aria-label={action.ariaLabel}>{action.label}</Link>
                  </Button>
                ))}
              </div>
            </div>
          </div>
        </CardContent>
      </Card>
    </section>
  );
}

export function AccountingCloseReportPackagePanel({ view }: { view: AccountingCloseReportPackageViewModel }) {
  const runCloseWorkflowAction = (actionId: AccountingCloseWorkflowActionId | null) => {
    if (!actionId) {
      return;
    }

    switch (actionId) {
      case "configure-close-plan":
        void view.configureClosePlan();
        return;
      case "sign-off-task":
        void view.signOffNextTask();
        return;
      case "request-late-adjustment":
        void view.createLateAdjustment();
        return;
      case "review-evidence": {
        const row = view.evidenceReviewRows.find((item) => item.issueCode && !item.reviewDisabledReason);
        if (row) {
          void view.reviewCloseEvidence(row.rowId);
        }
        return;
      }
      case "build-package":
        void view.buildReportPackage();
        return;
      case "certify-package":
        void view.certifyPackage();
        return;
      case "inspect-export":
        void view.inspectSelectedPackageExport();
        return;
      case "lock-period":
        void view.lockClosePeriod();
        return;
    }
  };

  return (
    <section id="accounting-close-report-package" className="workspace-section-band" aria-labelledby="accounting-close-report-package-heading">
      <span className="sr-only" aria-live="polite">{view.liveRegionText}</span>
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <p className="eyebrow-label">Close package</p>
          <h3 id="accounting-close-report-package-heading" className="workspace-section-title">{view.title}</h3>
          <p className="workspace-section-summary">{view.description}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={accountingToolingBadgeVariant(view.statusTone)} dot>{view.statusLabel}</Badge>
          <Button type="button" size="sm" variant="outline" disabled={view.loading} busy={view.loading} busyLabel="Refreshing close package" onClick={() => void view.refresh()}>
            <RefreshCcw className={cn("h-3.5 w-3.5", view.loading && "animate-spin")} aria-hidden="true" />
            Refresh
          </Button>
          <Button
            type="button"
            size="sm"
            disabled={Boolean(view.buildDisabledReason)}
            disabledReason={view.buildDisabledReason ?? undefined}
            busy={view.buildBusy}
            busyLabel="Building accounting report package"
            onClick={() => void view.buildReportPackage()}
          >
            <Paperclip className="h-3.5 w-3.5" aria-hidden="true" />
            {view.buildButtonLabel}
          </Button>
          <Button
            type="button"
            size="sm"
            variant="secondary"
            disabled={Boolean(view.certifyDisabledReason)}
            disabledReason={view.certifyDisabledReason ?? undefined}
            busy={view.certifyBusy}
            busyLabel="Certifying accounting report package"
            onClick={() => void view.certifyPackage()}
          >
            <BookCheck className="h-3.5 w-3.5" aria-hidden="true" />
            {view.certifyButtonLabel}
          </Button>
          <Button
            type="button"
            size="sm"
            variant="outline"
            disabled={Boolean(view.exportManifestDisabledReason)}
            disabledReason={view.exportManifestDisabledReason ?? undefined}
            busy={view.exportManifestBusy}
            busyLabel="Loading report export manifest"
            onClick={() => void view.inspectSelectedPackageExport()}
          >
            <Paperclip className="h-3.5 w-3.5" aria-hidden="true" />
            {view.exportManifestButtonLabel}
          </Button>
          <Button
            type="button"
            size="sm"
            variant="outline"
            disabled={Boolean(view.configureClosePlanDisabledReason)}
            disabledReason={view.configureClosePlanDisabledReason ?? undefined}
            busy={view.configureClosePlanBusy}
            busyLabel="Retaining close setup"
            onClick={() => void view.configureClosePlan()}
          >
            <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
            {view.configureClosePlanButtonLabel}
          </Button>
          <Button
            type="button"
            size="sm"
            variant="outline"
            disabled={Boolean(view.lockClosePeriodDisabledReason)}
            disabledReason={view.lockClosePeriodDisabledReason ?? undefined}
            busy={view.lockClosePeriodBusy}
            busyLabel="Locking close period"
            onClick={() => void view.lockClosePeriod()}
          >
            <LockKeyhole className="h-3.5 w-3.5" aria-hidden="true" />
            {view.lockClosePeriodButtonLabel}
          </Button>
          <Button
            type="button"
            size="sm"
            variant="outline"
            disabled={Boolean(view.signOffDisabledReason)}
            disabledReason={view.signOffDisabledReason ?? undefined}
            busy={view.signOffBusy}
            busyLabel="Signing off close checklist task"
            onClick={() => void view.signOffNextTask()}
          >
            <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
            {view.signOffButtonLabel}
          </Button>
        </div>
      </div>

      <Card className={cn("panel-surface", accountingToolingBorderClass(view.statusTone))} role="region" aria-label={view.ariaLabel}>
        <CardHeader>
          <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(18rem,0.35fr)]">
            <div className="min-w-0">
              <CardTitle className="flex items-center gap-2 text-base">
                <Paperclip className="h-4 w-4 text-primary" aria-hidden="true" />
                {view.periodLabel}
              </CardTitle>
              <CardDescription className="mt-2">{view.materialityLabel}</CardDescription>
            </div>
            <div className="grid gap-2 text-sm">
              <CloseCommandCenterValue label="Fund" value={view.fundLabel} />
              <CloseCommandCenterValue label="Period lock" value={view.lockLabel} />
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {view.loadingText ? <p role="status" className="text-sm text-muted-foreground">{view.loadingText}</p> : null}
          {view.errorText ? (
            <div role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
              {view.errorText}
            </div>
          ) : null}
          {view.buildStatusText ? (
            <div
              role={view.buildStatusTone === "danger" ? "alert" : "status"}
              className={cn(
                "rounded-md border px-3 py-2 text-sm",
                view.buildStatusTone === "success" && "border-success/30 bg-success/10 text-success",
                view.buildStatusTone === "danger" && "border-danger/30 bg-danger/10 text-danger",
                view.buildStatusTone === "neutral" && "border-border/70 bg-secondary/20 text-muted-foreground"
              )}
            >
              {view.buildStatusText}
            </div>
          ) : null}
          {view.certifyStatusText ? (
            <div
              role={view.certifyStatusTone === "danger" ? "alert" : "status"}
              className={cn(
                "rounded-md border px-3 py-2 text-sm",
                view.certifyStatusTone === "success" && "border-success/30 bg-success/10 text-success",
                view.certifyStatusTone === "danger" && "border-danger/30 bg-danger/10 text-danger",
                view.certifyStatusTone === "neutral" && "border-border/70 bg-secondary/20 text-muted-foreground"
              )}
            >
              {view.certifyStatusText}
            </div>
          ) : null}
          {view.signOffStatusText ? (
            <div
              role={view.signOffStatusTone === "danger" ? "alert" : "status"}
              className={cn(
                "rounded-md border px-3 py-2 text-sm",
                view.signOffStatusTone === "success" && "border-success/30 bg-success/10 text-success",
                view.signOffStatusTone === "danger" && "border-danger/30 bg-danger/10 text-danger",
                view.signOffStatusTone === "neutral" && "border-border/70 bg-secondary/20 text-muted-foreground"
              )}
            >
              {view.signOffStatusText}
            </div>
          ) : null}
          {view.configureClosePlanStatusText ? (
            <div
              role={view.configureClosePlanStatusTone === "danger" ? "alert" : "status"}
              className={cn(
                "rounded-md border px-3 py-2 text-sm",
                view.configureClosePlanStatusTone === "success" && "border-success/30 bg-success/10 text-success",
                view.configureClosePlanStatusTone === "danger" && "border-danger/30 bg-danger/10 text-danger",
                view.configureClosePlanStatusTone === "neutral" && "border-border/70 bg-secondary/20 text-muted-foreground"
              )}
            >
              {view.configureClosePlanStatusText}
            </div>
          ) : null}
          {view.lockClosePeriodStatusText ? (
            <div
              role={view.lockClosePeriodStatusTone === "danger" ? "alert" : "status"}
              className={cn(
                "rounded-md border px-3 py-2 text-sm",
                view.lockClosePeriodStatusTone === "success" && "border-success/30 bg-success/10 text-success",
                view.lockClosePeriodStatusTone === "danger" && "border-danger/30 bg-danger/10 text-danger",
                view.lockClosePeriodStatusTone === "neutral" && "border-border/70 bg-secondary/20 text-muted-foreground"
              )}
            >
              {view.lockClosePeriodStatusText}
            </div>
          ) : null}
          {view.createLateAdjustmentStatusText ? (
            <div
              role={view.createLateAdjustmentStatusTone === "danger" ? "alert" : "status"}
              className={cn(
                "rounded-md border px-3 py-2 text-sm",
                view.createLateAdjustmentStatusTone === "success" && "border-success/30 bg-success/10 text-success",
                view.createLateAdjustmentStatusTone === "danger" && "border-danger/30 bg-danger/10 text-danger",
                view.createLateAdjustmentStatusTone === "neutral" && "border-border/70 bg-secondary/20 text-muted-foreground"
              )}
            >
              {view.createLateAdjustmentStatusText}
            </div>
          ) : null}
          {view.reviewLateAdjustmentStatusText ? (
            <div
              role={view.reviewLateAdjustmentStatusTone === "danger" ? "alert" : "status"}
              className={cn(
                "rounded-md border px-3 py-2 text-sm",
                view.reviewLateAdjustmentStatusTone === "success" && "border-success/30 bg-success/10 text-success",
                view.reviewLateAdjustmentStatusTone === "danger" && "border-danger/30 bg-danger/10 text-danger",
                view.reviewLateAdjustmentStatusTone === "neutral" && "border-border/70 bg-secondary/20 text-muted-foreground"
              )}
            >
              {view.reviewLateAdjustmentStatusText}
            </div>
          ) : null}
          {view.reviewCloseEvidenceStatusText ? (
            <div
              role={view.reviewCloseEvidenceStatusTone === "danger" ? "alert" : "status"}
              className={cn(
                "rounded-md border px-3 py-2 text-sm",
                view.reviewCloseEvidenceStatusTone === "success" && "border-success/30 bg-success/10 text-success",
                view.reviewCloseEvidenceStatusTone === "danger" && "border-danger/30 bg-danger/10 text-danger",
                view.reviewCloseEvidenceStatusTone === "neutral" && "border-border/70 bg-secondary/20 text-muted-foreground"
              )}
            >
              {view.reviewCloseEvidenceStatusText}
            </div>
          ) : null}
          {view.exportManifestStatusText ? (
            <div
              role={view.exportManifestStatusTone === "danger" ? "alert" : "status"}
              className={cn(
                "rounded-md border px-3 py-2 text-sm",
                view.exportManifestStatusTone === "success" && "border-success/30 bg-success/10 text-success",
                view.exportManifestStatusTone === "danger" && "border-danger/30 bg-danger/10 text-danger",
                view.exportManifestStatusTone === "neutral" && "border-border/70 bg-secondary/20 text-muted-foreground"
              )}
            >
              {view.exportManifestStatusText}
            </div>
          ) : null}

          <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-3" aria-label="End-to-end close workflow controls">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="text-xs font-semibold uppercase text-muted-foreground">Close workflow control</div>
              <Badge variant="outline">{view.closeWorkflowSteps.length} steps</Badge>
            </div>
            <div role="list" className="mt-3 grid gap-2 lg:grid-cols-2 xl:grid-cols-4">
              {view.closeWorkflowSteps.map((step) => (
                <div
                  key={step.id}
                  role="listitem"
                  className={cn("grid min-h-[12rem] gap-3 rounded-md border bg-background/45 px-3 py-3", accountingToolingBorderClass(step.tone))}
                >
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <span className="font-semibold text-foreground">{step.label}</span>
                    <Badge variant={accountingToolingBadgeVariant(step.tone)}>{step.statusLabel}</Badge>
                  </div>
                  <div className="grid content-start gap-1 text-xs leading-5 text-muted-foreground">
                    <span>{step.detail}</span>
                    <span className="font-mono text-[0.72rem] text-foreground">{step.evidenceLabel}</span>
                  </div>
                  {step.actionId ? (
                    <Button
                      type="button"
                      size="sm"
                      variant={step.tone === "danger" ? "default" : "outline"}
                      className="mt-auto justify-self-start"
                      disabled={Boolean(step.disabledReason)}
                      disabledReason={step.disabledReason ?? undefined}
                      onClick={() => runCloseWorkflowAction(step.actionId)}
                    >
                      {step.actionLabel}
                    </Button>
                  ) : null}
                </div>
              ))}
            </div>
          </div>

          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            {view.metrics.map((metric) => (
              <div key={metric.id} className={cn("rounded-md border bg-secondary/20 px-3 py-3", accountingToolingBorderClass(metric.tone))}>
                <div className="flex items-start justify-between gap-2">
                  <div className="text-xs font-semibold uppercase text-muted-foreground">{metric.label}</div>
                  <Badge variant={accountingToolingBadgeVariant(metric.tone)}>{metric.value}</Badge>
                </div>
                <p className="mt-2 text-xs leading-5 text-muted-foreground">{metric.detail}</p>
              </div>
            ))}
          </div>

          <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-3" aria-label="Close operating coverage">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="text-xs font-semibold uppercase text-muted-foreground">Operating coverage</div>
              <Badge variant={view.operatingCoverageRows.length > 0 ? "outline" : "warning"}>
                {view.operatingCoverageRows.length > 0 ? `${view.operatingCoverageRows.length} controls` : "Not supplied"}
              </Badge>
            </div>
            {view.operatingCoverageRows.length > 0 ? (
              <div role="list" className="mt-3 grid gap-2 md:grid-cols-2 xl:grid-cols-3">
                {view.operatingCoverageRows.map((row) => (
                  <div key={row.controlId} role="listitem" className={cn("rounded-md border bg-background/45 px-3 py-2", accountingToolingBorderClass(row.statusTone))}>
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <span className="font-semibold text-foreground">{row.label}</span>
                      <Badge variant={accountingToolingBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
                    </div>
                    <div className="mt-2 grid gap-1 text-xs text-muted-foreground">
                      <span>{row.evidenceLabel}; {row.blockerLabel}</span>
                      <span>{row.requiredAction}</span>
                    </div>
                    {row.issueLabels.length > 0 ? (
                      <ul className="mt-2 space-y-1 text-xs text-warning">
                        {row.issueLabels.map((issue) => (
                          <li key={`${row.controlId}-${issue}`}>{issue}</li>
                        ))}
                      </ul>
                    ) : null}
                  </div>
                ))}
              </div>
            ) : (
              <p className="mt-3 text-sm text-muted-foreground">
                Shared close operating coverage was not returned with this close plan.
              </p>
            )}
          </div>

          <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-3" aria-label="Close sign-off administration">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="text-xs font-semibold uppercase text-muted-foreground">Task sign-off</div>
              <Badge variant={view.signOffDisabledReason ? "outline" : view.closeSignOffDraft.decision === "Rejected" ? "danger" : "success"}>
                {view.closeSignOffDraft.decision}
              </Badge>
            </div>
            <div className="mt-3 grid gap-3 xl:grid-cols-[minmax(0,1.15fr)_minmax(0,0.85fr)]">
              <div className="space-y-2">
                <div className="text-xs font-semibold uppercase text-muted-foreground">Ready tasks</div>
                {view.closeSignOffTaskOptions.length > 0 ? (
                  <div role="list" className="grid gap-2 md:grid-cols-2" aria-label="Close task sign-off selector">
                    {view.closeSignOffTaskOptions.map((task) => (
                      <button
                        key={task.taskId}
                        type="button"
                        role="listitem"
                        aria-label={task.selectAriaLabel}
                        aria-pressed={task.selected}
                        className={cn(
                          "rounded-md border px-3 py-2 text-left transition hover:border-primary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                          task.selected ? "border-primary/50 bg-primary/10" : "border-border/70 bg-background/45"
                        )}
                        onClick={() => view.selectCloseSignOffTask(task.taskId)}
                      >
                        <div className="flex flex-wrap items-start justify-between gap-2">
                          <span className="font-semibold text-foreground">{task.displayName}</span>
                          <Badge variant={accountingToolingBadgeVariant(task.statusTone)}>{task.statusLabel}</Badge>
                        </div>
                        <div className="mt-2 grid gap-1 text-xs text-muted-foreground">
                          <span>{task.taskId}</span>
                          <span>Owner: {task.ownerLabel}</span>
                          <span>{task.signOffLabel}</span>
                        </div>
                      </button>
                    ))}
                  </div>
                ) : (
                  <p className="rounded-md border border-border/70 bg-background/45 px-3 py-2 text-sm text-muted-foreground">
                    No close checklist task is ready for sign-off.
                  </p>
                )}
              </div>
              <div className="grid gap-3 rounded-md border border-border/70 bg-background/45 px-3 py-3">
                <div className="grid gap-2 md:grid-cols-[minmax(0,0.6fr)_minmax(0,0.4fr)]">
                  <label className="space-y-1 text-sm">
                    <span className="text-xs font-semibold uppercase text-muted-foreground">Task id</span>
                    <Input
                      value={view.closeSignOffDraft.taskId}
                      onChange={(event) => view.updateCloseSignOffDraft({ taskId: event.target.value })}
                    />
                  </label>
                  <label className="space-y-1 text-sm">
                    <span className="text-xs font-semibold uppercase text-muted-foreground">Role</span>
                    <Input
                      value={view.closeSignOffDraft.role}
                      onChange={(event) => view.updateCloseSignOffDraft({ role: event.target.value })}
                    />
                  </label>
                </div>
                <div className="space-y-2" aria-label="Close task sign-off role candidates">
                  <div className="text-xs font-semibold uppercase text-muted-foreground">Role candidates</div>
                  {view.closeSignOffRoleOptions.length > 0 ? (
                    <div role="list" className="grid gap-2 md:grid-cols-2">
                      {view.closeSignOffRoleOptions.map((role) => (
                        <button
                          key={role.role}
                          type="button"
                          role="listitem"
                          aria-label={role.selectAriaLabel}
                          aria-pressed={role.selected}
                          className={cn(
                            "rounded-md border px-3 py-2 text-left transition hover:border-primary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                            role.selected ? "border-primary/50 bg-primary/10" : "border-border/70 bg-secondary/10"
                          )}
                          onClick={() => view.selectCloseSignOffRole(role.role)}
                        >
                          <div className="flex flex-wrap items-start justify-between gap-2">
                            <span className="font-semibold text-foreground">{role.label}</span>
                            <Badge variant={role.selected ? "success" : "outline"}>{role.sourceLabel}</Badge>
                          </div>
                        </button>
                      ))}
                    </div>
                  ) : (
                    <p className="rounded-md border border-border/70 bg-secondary/10 px-3 py-2 text-sm text-muted-foreground">
                      Select a ready checklist task before choosing a signer role.
                    </p>
                  )}
                </div>
                <div className="space-y-2" aria-label="Close task sign-off decision">
                  <div className="text-xs font-semibold uppercase text-muted-foreground">Decision</div>
                  <div role="list" className="grid gap-2 sm:grid-cols-2">
                    {view.closeSignOffDecisionOptions.map((decision) => (
                      <button
                        key={decision.decision}
                        type="button"
                        role="listitem"
                        aria-label={decision.selectAriaLabel}
                        aria-pressed={decision.selected}
                        className={cn(
                          "rounded-md border px-3 py-2 text-left font-semibold transition hover:border-primary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                          decision.selected ? "border-primary/50 bg-primary/10 text-foreground" : "border-border/70 bg-secondary/10 text-muted-foreground"
                        )}
                        onClick={() => view.selectCloseSignOffDecision(decision.decision)}
                      >
                        {decision.label}
                      </button>
                    ))}
                  </div>
                </div>
                <label className="space-y-1 text-sm">
                  <span className="text-xs font-semibold uppercase text-muted-foreground">Notes</span>
                  <Input
                    value={view.closeSignOffDraft.notes}
                    onChange={(event) => view.updateCloseSignOffDraft({ notes: event.target.value })}
                  />
                </label>
              </div>
            </div>
          </div>

          <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-3" aria-label="Close setup editor">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="text-xs font-semibold uppercase text-muted-foreground">Close setup</div>
              <Badge variant={view.configureClosePlanDisabledReason ? "outline" : "success"}>{view.configureClosePlanButtonLabel}</Badge>
            </div>
            <div className="mt-3 grid gap-3 xl:grid-cols-[minmax(0,0.85fr)_minmax(0,1.15fr)]">
              <div className="grid gap-2 rounded-md border border-border/70 bg-background/45 px-3 py-3">
                <div className="text-xs font-semibold uppercase text-muted-foreground">Materiality</div>
                <div className="grid gap-2 md:grid-cols-3">
                  <label className="space-y-1 text-sm">
                    <span className="text-xs font-semibold uppercase text-muted-foreground">Amount threshold</span>
                    <Input
                      inputMode="decimal"
                      value={view.closeSetupDraft.amountThreshold}
                      onChange={(event) => view.updateCloseSetupDraft({ amountThreshold: event.target.value })}
                    />
                  </label>
                  <label className="space-y-1 text-sm">
                    <span className="text-xs font-semibold uppercase text-muted-foreground">Percent threshold</span>
                    <Input
                      inputMode="decimal"
                      value={view.closeSetupDraft.percentThreshold}
                      onChange={(event) => view.updateCloseSetupDraft({ percentThreshold: event.target.value })}
                    />
                  </label>
                  <label className="space-y-1 text-sm">
                    <span className="text-xs font-semibold uppercase text-muted-foreground">Currency</span>
                    <Input
                      value={view.closeSetupDraft.currency}
                      onChange={(event) => view.updateCloseSetupDraft({ currency: event.target.value })}
                    />
                  </label>
                </div>
                <div className="grid gap-2 md:grid-cols-[minmax(0,1fr)_auto] md:items-end">
                  <label className="space-y-1 text-sm">
                    <span className="text-xs font-semibold uppercase text-muted-foreground">Review role</span>
                    <Input
                      value={view.closeSetupDraft.reviewRole}
                      onChange={(event) => view.updateCloseSetupDraft({ reviewRole: event.target.value })}
                    />
                  </label>
                  <Checkbox
                    label="Approval required"
                    checked={view.closeSetupDraft.requiresLateAdjustmentApproval}
                    onCheckedChange={(checked) => view.updateCloseSetupDraft({ requiresLateAdjustmentApproval: checked })}
                  />
                </div>
              </div>
              <div className="grid gap-2 rounded-md border border-border/70 bg-background/45 px-3 py-3">
                <div className="text-xs font-semibold uppercase text-muted-foreground">Checklist task</div>
                {view.closeSetupTaskOptions.length > 0 ? (
                  <div role="list" className="grid gap-2 md:grid-cols-2" aria-label="Close setup task catalog">
                    {view.closeSetupTaskOptions.map((task) => (
                      <button
                        key={task.taskId}
                        type="button"
                        role="listitem"
                        aria-label={task.selectAriaLabel}
                        aria-pressed={task.selected}
                        className={cn(
                          "rounded-md border px-3 py-2 text-left transition hover:border-primary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                          task.selected ? "border-primary/50 bg-primary/10" : "border-border/70 bg-secondary/10"
                        )}
                        onClick={() => view.selectCloseSetupTask(task.taskId)}
                      >
                        <div className="flex flex-wrap items-start justify-between gap-2">
                          <span className="font-semibold text-foreground">{task.displayName}</span>
                          <Badge variant={accountingToolingBadgeVariant(task.statusTone)}>{task.statusLabel}</Badge>
                        </div>
                        <div className="mt-2 grid gap-1 text-xs text-muted-foreground">
                          <span>{task.taskId}</span>
                          <span>Owner: {task.ownerLabel}; Due: {task.dueDateLabel}</span>
                          <span>{task.dependencyLabel}; {task.signOffLabel}</span>
                        </div>
                      </button>
                    ))}
                  </div>
                ) : (
                  <p className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                    No retained close checklist tasks are available for setup.
                  </p>
                )}
                <div className="grid gap-2 md:grid-cols-[minmax(0,0.45fr)_minmax(0,0.55fr)]">
                  <label className="space-y-1 text-sm">
                    <span className="text-xs font-semibold uppercase text-muted-foreground">Task id</span>
                    <Input
                      value={view.closeSetupDraft.taskId}
                      onChange={(event) => view.updateCloseSetupDraft({ taskId: event.target.value })}
                    />
                  </label>
                  <label className="space-y-1 text-sm">
                    <span className="text-xs font-semibold uppercase text-muted-foreground">Display name</span>
                    <Input
                      value={view.closeSetupDraft.taskDisplayName}
                      onChange={(event) => view.updateCloseSetupDraft({ taskDisplayName: event.target.value })}
                    />
                  </label>
                </div>
                <div className="grid gap-2 md:grid-cols-[minmax(0,0.3fr)_minmax(0,0.25fr)_minmax(0,0.2fr)_minmax(0,0.25fr)]">
                  <label className="space-y-1 text-sm">
                    <span className="text-xs font-semibold uppercase text-muted-foreground">Owner</span>
                    <Input
                      value={view.closeSetupDraft.taskOwner}
                      onChange={(event) => view.updateCloseSetupDraft({ taskOwner: event.target.value })}
                    />
                  </label>
                  <label className="space-y-1 text-sm">
                    <span className="text-xs font-semibold uppercase text-muted-foreground">Due date</span>
                    <Input
                      value={view.closeSetupDraft.taskDueDate}
                      onChange={(event) => view.updateCloseSetupDraft({ taskDueDate: event.target.value })}
                    />
                  </label>
                  <label className="space-y-1 text-sm">
                    <span className="text-xs font-semibold uppercase text-muted-foreground">Approvals</span>
                    <Input
                      inputMode="numeric"
                      value={view.closeSetupDraft.taskRequiredApprovalCount}
                      onChange={(event) => view.updateCloseSetupDraft({ taskRequiredApprovalCount: event.target.value })}
                    />
                  </label>
                  <label className="space-y-1 text-sm">
                    <span className="text-xs font-semibold uppercase text-muted-foreground">Approval role</span>
                    <Input
                      value={view.closeSetupDraft.taskRequiredApprovalRole}
                      onChange={(event) => view.updateCloseSetupDraft({ taskRequiredApprovalRole: event.target.value })}
                    />
                  </label>
                </div>
                <div className="space-y-2" aria-label="Close setup sign-off role candidates">
                  <div className="text-xs font-semibold uppercase text-muted-foreground">Sign-off role candidates</div>
                  {view.closeSetupSignOffRoleOptions.length > 0 ? (
                    <div role="list" className="grid gap-2 md:grid-cols-2">
                      {view.closeSetupSignOffRoleOptions.map((role) => (
                        <button
                          key={role.role}
                          type="button"
                          role="listitem"
                          aria-label={role.selectAriaLabel}
                          aria-pressed={role.selected}
                          className={cn(
                            "rounded-md border px-3 py-2 text-left transition hover:border-primary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                            role.selected ? "border-primary/50 bg-primary/10" : "border-border/70 bg-secondary/10"
                          )}
                          onClick={() => view.selectCloseSetupSignOffRole(role.role)}
                        >
                          <div className="flex flex-wrap items-start justify-between gap-2">
                            <span className="font-semibold text-foreground">{role.label}</span>
                            <Badge variant={role.selected ? "success" : "outline"}>{role.sourceLabel}</Badge>
                          </div>
                        </button>
                      ))}
                    </div>
                  ) : (
                    <p className="rounded-md border border-border/70 bg-secondary/10 px-3 py-2 text-sm text-muted-foreground">
                      Select a retained checklist task before choosing sign-off roles.
                    </p>
                  )}
                </div>
                <label className="space-y-1 text-sm">
                  <span className="text-xs font-semibold uppercase text-muted-foreground">Required evidence</span>
                  <Input
                    value={view.closeSetupDraft.taskRequiredEvidence}
                    onChange={(event) => view.updateCloseSetupDraft({ taskRequiredEvidence: event.target.value })}
                  />
                </label>
                <label className="space-y-1 text-sm">
                  <span className="text-xs font-semibold uppercase text-muted-foreground">Sign-off matrix</span>
                  <textarea
                    className="min-h-[88px] w-full rounded-md border border-input bg-background px-3 py-2 text-sm shadow-sm transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                    value={view.closeSetupDraft.taskSignOffRequirements}
                    onChange={(event) => view.updateCloseSetupDraft({ taskSignOffRequirements: event.target.value })}
                  />
                </label>
                <div className="space-y-2" aria-label="Close setup dependency candidates">
                  <div className="text-xs font-semibold uppercase text-muted-foreground">Dependency candidates</div>
                  {view.closeSetupDependencyOptions.length > 0 ? (
                    <div role="list" className="grid gap-2 md:grid-cols-2">
                      {view.closeSetupDependencyOptions.map((dependency) => (
                        <button
                          key={dependency.taskId}
                          type="button"
                          role="listitem"
                          aria-label={dependency.toggleAriaLabel}
                          aria-pressed={dependency.checked}
                          className={cn(
                            "rounded-md border px-3 py-2 text-left transition hover:border-primary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                            dependency.checked ? "border-primary/50 bg-primary/10" : "border-border/70 bg-secondary/10"
                          )}
                          onClick={() => view.toggleCloseSetupDependency(dependency.taskId)}
                        >
                          <div className="flex flex-wrap items-start justify-between gap-2">
                            <span className="font-semibold text-foreground">{dependency.displayName}</span>
                            <Badge variant={accountingToolingBadgeVariant(dependency.statusTone)}>{dependency.statusLabel}</Badge>
                          </div>
                          <div className="mt-2 grid gap-1 text-xs text-muted-foreground">
                            <span>{dependency.taskId}</span>
                            <span>Owner: {dependency.ownerLabel}; Due: {dependency.dueDateLabel}</span>
                          </div>
                        </button>
                      ))}
                    </div>
                  ) : (
                    <p className="rounded-md border border-border/70 bg-secondary/10 px-3 py-2 text-sm text-muted-foreground">
                      Select a retained checklist task with available predecessors before adding dependency links.
                    </p>
                  )}
                </div>
                <label className="space-y-1 text-sm">
                  <span className="text-xs font-semibold uppercase text-muted-foreground">Depends on task ids</span>
                  <Input
                    value={view.closeSetupDraft.taskDependsOnTaskIds}
                    onChange={(event) => view.updateCloseSetupDraft({ taskDependsOnTaskIds: event.target.value })}
                  />
                </label>
                <label className="space-y-1 text-sm">
                  <span className="text-xs font-semibold uppercase text-muted-foreground">Dependency reason</span>
                  <Input
                    value={view.closeSetupDraft.taskDependencyReason}
                    onChange={(event) => view.updateCloseSetupDraft({ taskDependencyReason: event.target.value })}
                  />
                </label>
              </div>
            </div>
          </div>

          <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-3" aria-label="Accounting report certification safeguards">
            <div className="text-xs font-semibold uppercase text-muted-foreground">Certification safeguards</div>
            <div role="list" className="mt-3 grid gap-2 md:grid-cols-2 xl:grid-cols-3">
              {view.certificationSafeguards.map((safeguard) => (
                <div key={safeguard.id} role="listitem" className={cn("rounded-md border bg-background/45 px-3 py-2", accountingToolingBorderClass(safeguard.tone))}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <span className="font-semibold text-foreground">{safeguard.label}</span>
                    <Badge variant={accountingToolingBadgeVariant(safeguard.tone)}>{safeguard.value}</Badge>
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{safeguard.detail}</p>
                </div>
              ))}
            </div>
          </div>

          <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-3">
            <div className="text-xs font-semibold uppercase text-muted-foreground">Close calendar</div>
            {view.closeCalendar.length > 0 ? (
              <div role="list" className="mt-3 grid gap-2 lg:grid-cols-2 xl:grid-cols-3" aria-label="Close calendar milestones">
                {view.closeCalendar.map((milestone) => (
                  <div key={milestone.milestoneId} role="listitem" className={cn("rounded-md border bg-background/45 px-3 py-2", accountingToolingBorderClass(milestone.statusTone))}>
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <span className="font-semibold text-foreground">{milestone.displayName}</span>
                      <Badge variant={accountingToolingBadgeVariant(milestone.statusTone)}>{milestone.statusLabel}</Badge>
                    </div>
                    <div className="mt-2 grid gap-1 text-xs text-muted-foreground">
                      <span>Due: {milestone.dueDateLabel}</span>
                      <span>Owner: {milestone.ownerLabel}</span>
                      <span>{milestone.dependencyLabel}; {milestone.signOffLabel}</span>
                      <span>{milestone.evidenceLabel}; {milestone.lockedLabel}</span>
                      {milestone.blockerLabel ? <span className="text-warning">{milestone.blockerLabel}</span> : null}
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <p className="mt-3 text-sm text-muted-foreground">Close calendar milestones are not loaded.</p>
            )}
          </div>

          <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_24rem]">
            <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-3">
              <div className="text-xs font-semibold uppercase text-muted-foreground">Close checklist</div>
              {view.tasks.length > 0 ? (
                <div role="list" className="mt-3 space-y-2" aria-label="Close package checklist tasks">
                  {view.tasks.map((task) => (
                    <div key={task.taskId} role="listitem" className={cn("rounded-md border bg-background/45 px-3 py-2", accountingToolingBorderClass(task.statusTone))}>
                      <div className="flex flex-wrap items-start justify-between gap-2">
                        <span className="font-semibold text-foreground">{task.displayName}</span>
                        <Badge variant={accountingToolingBadgeVariant(task.statusTone)}>{task.statusLabel}</Badge>
                      </div>
                      <div className="mt-2 grid gap-2 text-xs text-muted-foreground md:grid-cols-2 xl:grid-cols-4">
                        <span>Owner: {task.ownerLabel}</span>
                        <span>Due: {task.dueDateLabel}</span>
                        <span>{task.dependencyLabel}</span>
                        <span>{task.signOffLabel}</span>
                      </div>
                      <div className="mt-2 text-xs text-muted-foreground">{task.signOffRequirementLabel}</div>
                        <div className="mt-2 flex flex-wrap gap-2 text-xs text-muted-foreground">
                          <span>{task.evidenceLabel}</span>
                          {task.blockerLabel ? <span className="text-warning">{task.blockerLabel}</span> : null}
                        </div>
                        {task.signOffDetailLabel ? (
                          <div className="mt-2 text-xs text-muted-foreground">{task.signOffDetailLabel}</div>
                        ) : null}
                      </div>
                    ))}
                  </div>
              ) : (
                <p className="mt-3 text-sm text-muted-foreground">Close checklist detail is not loaded.</p>
              )}
              <div className="mt-4 grid gap-3 lg:grid-cols-2">
                <div className="rounded-md border border-border/70 bg-background/45 px-3 py-3" aria-label="Close dependency graph">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="text-xs font-semibold uppercase text-muted-foreground">Dependency graph</div>
                    <Badge variant={view.dependencyGraphRows.length > 0 ? "warning" : "outline"}>{view.dependencyGraphRows.length}</Badge>
                  </div>
                  {view.dependencyGraphRows.length > 0 ? (
                    <div role="list" className="mt-3 space-y-2">
                      {view.dependencyGraphRows.map((dependency) => (
                        <div key={dependency.dependencyId} role="listitem" className={cn("rounded-md border px-3 py-2", accountingToolingBorderClass(dependency.statusTone))}>
                          <div className="flex flex-wrap items-start justify-between gap-2">
                            <span className="font-semibold text-foreground">{dependency.taskLabel}</span>
                            <Badge variant={accountingToolingBadgeVariant(dependency.statusTone)}>{dependency.statusLabel}</Badge>
                          </div>
                          <div className="mt-2 grid gap-1 text-xs text-muted-foreground">
                            <span>Depends on: {dependency.predecessorLabel}</span>
                            <span>{dependency.reason}</span>
                            {dependency.blockerLabel ? <span className="text-warning">{dependency.blockerLabel}</span> : null}
                          </div>
                        </div>
                      ))}
                    </div>
                  ) : (
                    <p className="mt-3 text-sm text-muted-foreground">No close-task dependencies are retained for this plan.</p>
                  )}
                </div>

                <div className="rounded-md border border-border/70 bg-background/45 px-3 py-3" aria-label="Close sign-off matrix">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="text-xs font-semibold uppercase text-muted-foreground">Sign-off matrix</div>
                    <Badge variant={view.signOffMatrixRows.length > 0 ? "success" : "outline"}>{view.signOffMatrixRows.length}</Badge>
                  </div>
                  {view.signOffMatrixRows.length > 0 ? (
                    <div role="list" className="mt-3 space-y-2">
                      {view.signOffMatrixRows.map((row) => (
                        <div key={row.rowId} role="listitem" className={cn("rounded-md border px-3 py-2", accountingToolingBorderClass(row.statusTone))}>
                          <div className="flex flex-wrap items-start justify-between gap-2">
                            <span className="font-semibold text-foreground">{row.roleLabel}</span>
                            <Badge variant={accountingToolingBadgeVariant(row.statusTone)}>{row.approvedLabel}</Badge>
                          </div>
                          <div className="mt-2 grid gap-1 text-xs text-muted-foreground">
                            <span>{row.taskLabel}</span>
                            <span>{row.statusLabel}</span>
                            <span>{row.evidenceRequirementLabel}</span>
                            {row.latestSignOffLabel ? <span>{row.latestSignOffLabel}</span> : null}
                          </div>
                        </div>
                      ))}
                    </div>
                  ) : (
                    <p className="mt-3 text-sm text-muted-foreground">No sign-off matrix rows are retained for this plan.</p>
                  )}
                </div>
              </div>
            </div>

            <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-3">
              <div className="text-xs font-semibold uppercase text-muted-foreground">Package history</div>
              {view.packageRows.length > 0 ? (
                <div role="list" className="mt-3 space-y-2" aria-label="Accounting report package history">
                  {view.packageRows.map((item) => (
                    <button
                      key={item.packageId}
                      type="button"
                      className={cn(
                        "w-full rounded-md border bg-background/45 px-3 py-2 text-left transition hover:border-primary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                        item.selected ? "border-primary/50" : "border-border/70"
                      )}
                      aria-pressed={item.selected}
                      onClick={() => view.selectPackage(item.packageId)}
                    >
                      <div className="flex flex-wrap items-start justify-between gap-2">
                        <span className="break-all font-mono text-xs font-semibold text-foreground">{item.packageId}</span>
                        <Badge variant={accountingToolingBadgeVariant(item.certificationTone)}>{item.certificationLabel}</Badge>
                      </div>
                      <div className="mt-2 grid gap-1 text-xs text-muted-foreground">
                        <span>Period {item.periodLabel}</span>
                        <span>NAV {item.navLabel}</span>
                        <span>{item.investorStatementLabel}</span>
                        <span>Realized gain/loss {item.realizedGainLossLabel}</span>
                        <span>{item.restatementLabel}</span>
                        <span>{item.exportArtifactLabel}</span>
                        <span>{item.evidenceLabel}; {item.validationLabel}</span>
                      </div>
                    </button>
                  ))}
                  {view.exportManifest ? (
                    <div className="rounded-md border border-border/70 bg-background/45 px-3 py-2" aria-label="Accounting report export manifest">
                      <div className="flex flex-wrap items-start justify-between gap-2">
                        <span className="font-semibold text-foreground">{view.exportManifest.displayName}</span>
                        <Badge variant="outline">{view.exportManifest.certificationLabel}</Badge>
                      </div>
                      <div className="mt-2 grid gap-1 text-xs text-muted-foreground">
                        <span className="break-all font-mono">{view.exportManifest.artifactId}</span>
                        <span>{view.exportManifest.formatLabel}</span>
                        <span>{view.exportManifest.fileName}</span>
                        <span>Generated {view.exportManifest.generatedLabel}</span>
                        <span>{view.exportManifest.evidenceLabel}; {view.exportManifest.postingLabel}</span>
                        <span className="break-all font-mono">Hash {view.exportManifest.hashLabel}</span>
                        <span className="break-all font-mono">{view.exportManifest.routeLabel}</span>
                      </div>
                    </div>
                  ) : null}
                </div>
              ) : (
                <p className="mt-3 text-sm text-muted-foreground">No accounting report packages have been built for the selected close workflow.</p>
              )}
            </div>
          </div>

          <div className="grid gap-4 lg:grid-cols-2">
            <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="text-xs font-semibold uppercase text-muted-foreground">Late adjustments</div>
                <Badge variant={view.lateAdjustments.length > 0 ? "warning" : "outline"}>{view.lateAdjustments.length}</Badge>
              </div>
              <div className="mt-3 grid gap-2 rounded-md border border-border/70 bg-background/45 px-3 py-3">
                <div className="grid gap-2 md:grid-cols-[minmax(0,0.34fr)_minmax(0,0.18fr)_minmax(0,0.16fr)]">
                  <label className="space-y-1 text-sm">
                    <span className="text-xs font-semibold uppercase text-muted-foreground">Journal entry</span>
                    <Input
                      value={view.lateAdjustmentDraft.journalEntryId}
                      onChange={(event) => view.updateLateAdjustmentDraft({ journalEntryId: event.target.value })}
                    />
                  </label>
                  <label className="space-y-1 text-sm">
                    <span className="text-xs font-semibold uppercase text-muted-foreground">Amount</span>
                    <Input
                      inputMode="decimal"
                      value={view.lateAdjustmentDraft.amount}
                      onChange={(event) => view.updateLateAdjustmentDraft({ amount: event.target.value })}
                    />
                  </label>
                  <label className="space-y-1 text-sm">
                    <span className="text-xs font-semibold uppercase text-muted-foreground">Currency</span>
                    <Input
                      value={view.lateAdjustmentDraft.currency}
                      onChange={(event) => view.updateLateAdjustmentDraft({ currency: event.target.value })}
                    />
                  </label>
                </div>
                <label className="space-y-1 text-sm">
                  <span className="text-xs font-semibold uppercase text-muted-foreground">Reason</span>
                  <Input
                    value={view.lateAdjustmentDraft.reason}
                    onChange={(event) => view.updateLateAdjustmentDraft({ reason: event.target.value })}
                  />
                </label>
                <div className="flex justify-end">
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    disabled={Boolean(view.createLateAdjustmentDisabledReason)}
                    disabledReason={view.createLateAdjustmentDisabledReason ?? undefined}
                    busy={view.createLateAdjustmentBusy}
                    busyLabel="Requesting late adjustment"
                    onClick={() => void view.createLateAdjustment()}
                  >
                    <Paperclip className="h-3.5 w-3.5" aria-hidden="true" />
                    Request late adjustment
                  </Button>
                </div>
              </div>
              {view.lateAdjustments.length > 0 ? (
                <div role="list" className="mt-3 space-y-2" aria-label="Late adjustment requests">
                  {view.lateAdjustments.map((adjustment) => (
                    <div key={adjustment.requestId} role="listitem" className="rounded-md border border-border/70 bg-background/45 px-3 py-2">
                      <div className="flex flex-wrap items-start justify-between gap-2">
                        <span className="font-mono text-xs font-semibold text-foreground">{adjustment.journalEntryId}</span>
                        <Badge variant="outline">{adjustment.statusLabel}</Badge>
                      </div>
                      <p className="mt-2 text-sm leading-6 text-muted-foreground">{adjustment.reason}</p>
                      <div className="mt-2 grid gap-1 text-xs text-muted-foreground">
                        <span>{adjustment.amountLabel}</span>
                        <Badge variant={accountingToolingBadgeVariant(adjustment.materialityTone)} className="w-fit">{adjustment.materialityLabel}</Badge>
                        <span>{adjustment.requestedByLabel}</span>
                        {adjustment.decisionLabel ? <span>{adjustment.decisionLabel}</span> : null}
                        <span>{adjustment.evidenceLabel}</span>
                      </div>
                      <div className="mt-3 flex flex-wrap gap-2">
                        <Button
                          type="button"
                          size="sm"
                          variant="secondary"
                          disabled={Boolean(adjustment.reviewDisabledReason) || view.reviewLateAdjustmentBusy}
                          disabledReason={adjustment.reviewDisabledReason ?? undefined}
                          busy={view.reviewLateAdjustmentBusy}
                          busyLabel="Reviewing late adjustment"
                          onClick={() => void view.reviewLateAdjustment(adjustment.requestId, "Approved")}
                        >
                          <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                          Approve
                        </Button>
                        <Button
                          type="button"
                          size="sm"
                          variant="outline"
                          disabled={Boolean(adjustment.reviewDisabledReason) || view.reviewLateAdjustmentBusy}
                          disabledReason={adjustment.reviewDisabledReason ?? undefined}
                          onClick={() => void view.reviewLateAdjustment(adjustment.requestId, "Rejected")}
                        >
                          <X className="h-3.5 w-3.5" aria-hidden="true" />
                          Reject
                        </Button>
                      </div>
                    </div>
                  ))}
                </div>
              ) : (
                <p className="mt-3 text-sm text-muted-foreground">No late adjustments are registered for this close plan.</p>
              )}
            </div>

            <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="text-xs font-semibold uppercase text-muted-foreground">Evidence and blockers</div>
                <Badge variant={view.evidenceReviewRows.length > 0 ? "outline" : "success"}>{view.evidenceReviewRows.length}</Badge>
              </div>
              {view.evidenceReviewRows.length > 0 ? (
                <div role="list" className="mt-3 space-y-2" aria-label="Close evidence and blocker review rows">
                  {view.evidenceReviewRows.map((row) => (
                    <div key={row.rowId} role="listitem" className={cn("rounded-md border bg-background/45 px-3 py-2", accountingToolingBorderClass(row.statusTone))}>
                      <div className="flex flex-wrap items-start justify-between gap-2">
                        <span className="font-semibold text-foreground">{row.label}</span>
                        <Badge variant={accountingToolingBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
                      </div>
                      <div className="mt-2 grid gap-1 text-xs text-muted-foreground">
                        <span>{row.categoryLabel}</span>
                        <span>{row.evidenceLabel}</span>
                        <span>{row.detailLabel}</span>
                        {row.latestReviewLabel ? <span>{row.latestReviewLabel}</span> : null}
                      </div>
                      {row.issueCode ? (
                        <div className="mt-3 flex justify-end">
                          <Button
                            type="button"
                            size="sm"
                            variant="outline"
                            disabled={Boolean(row.reviewDisabledReason) || view.reviewCloseEvidenceBusy}
                            disabledReason={row.reviewDisabledReason ?? undefined}
                            busy={view.reviewCloseEvidenceBusy}
                            busyLabel="Retaining evidence review"
                            onClick={() => void view.reviewCloseEvidence(row.rowId)}
                          >
                            <BookCheck className="h-3.5 w-3.5" aria-hidden="true" />
                            Retain review
                          </Button>
                        </div>
                      ) : null}
                    </div>
                  ))}
                </div>
              ) : (
                <p className="mt-3 text-sm text-muted-foreground">No retained close evidence or blocker rows are attached.</p>
              )}
            </div>

            <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-3">
              <div className="text-xs font-semibold uppercase text-muted-foreground">Validation</div>
              {view.validationIssues.length > 0 ? (
                <div role="list" className="mt-3 space-y-2" aria-label="Close report package validation issues">
                  {view.validationIssues.map((issue) => (
                    <div key={issue.id} role="listitem" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm">
                      <div className="font-semibold text-warning">{issue.label}</div>
                      <p className="mt-1 leading-6 text-muted-foreground">{issue.message}</p>
                      {issue.detail ? <div className="mt-1 font-mono text-xs text-muted-foreground">{issue.detail}</div> : null}
                    </div>
                  ))}
                </div>
              ) : (
                <p className="mt-3 text-sm text-muted-foreground">No close/report validation issues are attached.</p>
              )}
            </div>
          </div>
        </CardContent>
      </Card>
    </section>
  );
}


function CloseCommandCenterValue({ label, value }: { label: string; value: string }) {
  return (
    <div className="data-grid-surface flex items-center justify-between gap-4 px-3 py-2">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono text-foreground">{value}</span>
    </div>
  );
}
