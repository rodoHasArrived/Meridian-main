import { AlertCircle, BookCheck, CheckCircle2, Landmark, Network, Search, ShieldCheck, Table2, TrendingUp, WalletCards } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { MetricCard } from "@/components/meridian/metric-card";
import { DenseDataTable, EntitySummary, ToolbarStrip, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import { workspaceForPath } from "@/lib/workspace";
import {
  buildGovernanceLoadingViewState,
  resolveGovernanceWorkstream,
  useGovernanceCashFlowViewModel,
  useGovernanceReconciliationViewModel,
  useGovernanceReportingViewModel,
  useReconciliationResolveDialogViewModel,
  useSecurityMasterViewModel
} from "@/screens/governance-screen.view-model";
import type {
  CalibrationSummaryViewState,
  CorporateActionRowViewModel,
  TradingParametersViewState
} from "@/screens/governance-screen.view-model";
import type { GovernanceWorkspaceResponse } from "@/types";

interface GovernanceScreenProps {
  data: GovernanceWorkspaceResponse | null;
}

const statusTone: Record<NonNullable<GovernanceWorkspaceResponse["reconciliationQueue"][number]["reconciliationStatus"]>, string> = {
  NotStarted: "text-muted-foreground",
  BreaksOpen: "text-warning",
  SecurityCoverageOpen: "text-warning",
  Resolved: "text-primary",
  Balanced: "text-success"
};

const focusCopy: Record<string, { title: string; description: string }> = {
  ledger: {
    title: "Ledger overview",
    description: "Cash, ledger coverage, and audit-facing balances remain visible from the workstation shell."
  },
  reconciliation: {
    title: "Reconciliation queue",
    description: "Open breaks, timing drift, and balanced runs stay visible without leaving Accounting."
  },
  "security-master": {
    title: "Security coverage",
    description: "Coverage gaps and reference integrity stay tied to reconciliation and reporting readiness."
  },
  reporting: {
    title: "Reporting profiles",
    description: "Report packs, governed exports, and loader artifacts stay tied to accounting evidence."
  }
};

export function GovernanceScreen({ data }: GovernanceScreenProps) {
  const { pathname } = useLocation();
  const workstream = resolveGovernanceWorkstream(pathname);
  const workspace = workspaceForPath(pathname);
  const reconciliation = useGovernanceReconciliationViewModel(data, workstream);
  const resolveDialog = useReconciliationResolveDialogViewModel(reconciliation.resolveBreak);
  const selectedReconciliation = reconciliation.selectedReconciliation;
  const selectedReconciliationDetail = reconciliation.detailView;
  const cashFlow = useGovernanceCashFlowViewModel(data?.cashFlow ?? null, pathname, workstream);
  const reporting = useGovernanceReportingViewModel(data?.reporting ?? null);
  const securityMaster = useSecurityMasterViewModel(workstream === "security-master");
  const identity = securityMaster.identityView;
  const identifierColumns: DenseDataTableColumn<NonNullable<typeof identity>["identifiers"][number]>[] = [
    { id: "kind", label: "Kind", render: (identifier) => <span className="font-mono">{identifier.kind}</span> },
    { id: "value", label: "Value", render: (identifier) => <span className="font-mono text-foreground">{identifier.value}</span> },
    { id: "provider", label: "Provider", render: (identifier) => identifier.providerLabel },
    { id: "state", label: "State", render: (identifier) => <Badge variant={identifier.primaryBadgeVariant}>{identifier.primaryLabel}</Badge> },
    { id: "range", label: "Valid range", render: (identifier) => <span className="font-mono text-muted-foreground">{identifier.validRangeLabel}</span> }
  ];
  const aliasColumns: DenseDataTableColumn<NonNullable<typeof identity>["aliases"][number]>[] = [
    { id: "kind", label: "Kind", render: (alias) => <span className="font-mono">{alias.aliasKind}</span> },
    {
      id: "alias",
      label: "Alias",
      render: (alias) => (
        <div>
          <div className="font-mono text-foreground">{alias.aliasValue}</div>
          <div className="mt-1 text-xs text-muted-foreground">{alias.reasonText}</div>
        </div>
      )
    },
    { id: "provider", label: "Provider", render: (alias) => alias.providerLabel },
    { id: "scope", label: "Scope", render: (alias) => alias.scope },
    { id: "state", label: "State", render: (alias) => <Badge variant={alias.enabledBadgeVariant}>{alias.enabledLabel}</Badge> },
    { id: "range", label: "Valid range", render: (alias) => <span className="font-mono text-muted-foreground">{alias.validRangeLabel}</span> }
  ];

  if (!data) {
    const loading = buildGovernanceLoadingViewState(pathname);
    return (
      <Card
        role={loading.role}
        aria-busy={loading.ariaBusy}
        aria-live={loading.ariaLive}
        aria-labelledby={loading.titleId}
        aria-describedby={loading.detailId}
      >
        <CardHeader>
          <CardTitle id={loading.titleId}>{loading.title}</CardTitle>
          <CardDescription id={loading.detailId}>{loading.detail}</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  const focus = focusCopy[workstream];

  return (
    <div className="space-y-8">
      <section
        role="region"
        aria-label={`${workspace.label} workbench context`}
        className="panel-surface-strong flex flex-wrap items-center justify-between gap-3 px-4 py-4"
      >
        <div className="min-w-0">
          <div className="eyebrow-label">{workspace.label} lane</div>
          <h2 className="mt-2 font-display text-[1.375rem] font-semibold leading-tight text-foreground">
            {focus.title}
          </h2>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">{focus.description}</p>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2">
          <GovernanceChip label="Workstream" value={workstream} />
          <GovernanceChip label="Queue" value={String(data.reconciliationQueue.length)} />
          <GovernanceChip label="Breaks" value={String(data.breakQueue.length)} />
          <GovernanceChip label="Profiles" value={String(data.reporting.profileCount)} />
        </div>
      </section>

      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {data.metrics.map((metric) => (
          <MetricCard key={metric.id} {...metric} />
        ))}
      </section>

      <section className="grid gap-4 xl:grid-cols-[1.2fr_0.8fr]">
        <Card className="panel-surface">
          <CardHeader>
            <div className="eyebrow-label">{workspace.label} Lane</div>
            <CardTitle className="flex items-center gap-2">
              <ShieldCheck className="h-5 w-5 text-primary" />
              {focus.title}
            </CardTitle>
            <CardDescription>{focus.description}</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 md:grid-cols-3">
            <GovernanceHighlight
              icon={BookCheck}
              title="Audit posture"
              description="Reconciliation health and audit readiness stay visible for every run on the queue."
            />
            <GovernanceHighlight
              icon={WalletCards}
              title="Cash flow"
              description="Portfolio cash and ledger cash stay paired so variance review is immediate."
            />
            <GovernanceHighlight
              icon={Landmark}
              title="Reporting"
              description="Export profiles stay close to accounting and reporting workflows instead of living in a separate tool."
            />
          </CardContent>
        </Card>

        <Card className="panel-surface-strong bg-panel-strong text-foreground" role="region" aria-label={cashFlow.ariaLabel}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <div className="eyebrow-label">{cashFlow.eyebrow}</div>
                <CardTitle>{cashFlow.title}</CardTitle>
                <CardDescription className="mt-2 text-muted-foreground">
                  {cashFlow.description}
                </CardDescription>
              </div>
              <span
                aria-label={cashFlow.statusAriaLabel}
                className={cn(
                  "w-fit rounded-sm border px-2.5 py-1 font-mono text-[10px] font-medium uppercase tracking-[0.14em]",
                  cashFlowBadgeClass(cashFlow.statusTone)
                )}
              >
                {cashFlow.statusLabel}
              </span>
            </div>
          </CardHeader>
          <CardContent className="space-y-3 text-sm">
            <span className="sr-only" aria-live="polite">{cashFlow.statusAnnouncement}</span>
            <div aria-label={cashFlow.rowGroupLabel} className="grid gap-3 sm:grid-cols-2">
              {cashFlow.rows.map((row) => (
                <GovernanceValue
                  key={row.id}
                  label={row.label}
                  value={row.value}
                  tone={cashFlowTextClass(row.tone)}
                  ariaLabel={row.ariaLabel}
                />
              ))}
            </div>
          </CardContent>
        </Card>
      </section>

      {workstream === "reconciliation" && selectedReconciliation && selectedReconciliationDetail ? (
        <section className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
          <Card className="panel-surface">
            <CardHeader>
              <CardTitle className="flex items-center gap-2 text-base">
                <BookCheck className="h-4 w-4 text-primary" />
                Reconciliation detail queue
              </CardTitle>
              <CardDescription>Select a run to inspect its active reconciliation detail panel.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              {data.reconciliationQueue.map((item) => (
                <button
                  key={item.runId}
                  type="button"
                  onClick={() => reconciliation.selectRun(item.runId)}
                  className={cn(
                    "w-full rounded-xl border px-4 py-4 text-left transition-colors",
                    item.runId === selectedReconciliation.runId
                      ? "border-primary/50 bg-primary/10"
                      : "border-border/70 bg-secondary/30 hover:bg-secondary/45"
                  )}
                >
                  <div className="flex items-center justify-between gap-3">
                    <div className="font-semibold">{item.strategyName}</div>
                    <div className={cn("font-mono text-xs uppercase tracking-[0.16em]", statusTone[item.reconciliationStatus])}>
                      {item.reconciliationStatus}
                    </div>
                  </div>
                  <div className="mt-2 font-mono text-sm text-muted-foreground">{item.runId}</div>
                  <div className="mt-3 flex items-center justify-between gap-4 text-sm">
                    <span className="text-muted-foreground">{item.status}</span>
                    <span className="font-mono">{item.openBreakCount} open</span>
                  </div>
                </button>
              ))}
            </CardContent>
          </Card>

          <Card className="panel-surface-strong bg-panel-strong text-foreground" role="region" aria-label={selectedReconciliationDetail.ariaLabel}>
            <CardHeader>
              <div className="eyebrow-label">{selectedReconciliationDetail.eyebrow}</div>
              <CardTitle>{selectedReconciliationDetail.title}</CardTitle>
              <CardDescription className="text-muted-foreground">
                {selectedReconciliationDetail.description}
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4 text-sm">
              {selectedReconciliationDetail.fields.map((field) => (
                <GovernanceValue
                  key={field.label}
                  label={field.label}
                  value={field.value}
                  tone={cashFlowTextClass(field.tone)}
                  ariaLabel={field.ariaLabel}
                />
              ))}
              <div
                aria-label={selectedReconciliationDetail.narrativeLabel}
                className="rounded-lg border border-border/70 bg-background/70 p-4 text-muted-foreground"
              >
                {selectedReconciliationDetail.narrative}
              </div>
              {reconciliation.detailActions ? (
                <div className="flex flex-wrap gap-3">
                  <Button asChild variant="secondary">
                    <Link
                      to={reconciliation.detailActions.evidencePacketHref}
                      aria-label={reconciliation.detailActions.evidencePacketAriaLabel}
                    >
                      <Network className="h-4 w-4" />
                      {reconciliation.detailActions.evidencePacketLabel}
                    </Link>
                  </Button>
                  <Button asChild variant="secondary">
                    <a
                      href={reconciliation.detailActions.breakChecklistHref}
                      aria-label={reconciliation.detailActions.breakChecklistAriaLabel}
                    >
                      {reconciliation.detailActions.breakChecklistLabel}
                    </a>
                  </Button>
                  <Button asChild variant="outline" className="border-border/70 bg-transparent text-foreground hover:bg-secondary/60">
                    <a
                      href={reconciliation.detailActions.auditPacketHref}
                      target="_blank"
                      rel="noreferrer"
                      aria-label={reconciliation.detailActions.auditPacketAriaLabel}
                    >
                      {reconciliation.detailActions.auditPacketLabel}
                    </a>
                  </Button>
                </div>
              ) : null}
            </CardContent>
          </Card>
        </section>
      ) : null}

      {workstream === "ledger" && selectedReconciliation ? (
        <section className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
          <Card aria-labelledby="trial-balance-title" aria-describedby="trial-balance-description" className="panel-surface">
            <CardHeader>
              <CardTitle id="trial-balance-title">{reconciliation.trialBalanceView.title}</CardTitle>
              <CardDescription id="trial-balance-description">{reconciliation.trialBalanceView.description}</CardDescription>
            </CardHeader>
            <CardContent>
              <span className="sr-only" aria-live="polite">{reconciliation.trialBalanceView.statusAnnouncement}</span>
              {reconciliation.trialBalanceView.hasRows ? (
                <div className="overflow-x-auto rounded-lg border border-border/70">
                  <table
                    aria-label={reconciliation.trialBalanceView.tableLabel}
                    className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm"
                  >
                    <thead className="bg-secondary/30">
                      <tr>
                        {["Account", "Type", "Balance", "Entries"].map((column) => (
                          <th
                            key={column}
                            scope="col"
                            className={cn("px-3 py-2", column === "Balance" || column === "Entries" ? "text-right" : undefined)}
                          >
                            {column}
                          </th>
                        ))}
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-border/50">
                      {reconciliation.trialBalanceView.rows.map((line) => (
                        <tr key={line.rowId} aria-label={line.ariaLabel} className="hover:bg-secondary/20">
                          <td className="px-3 py-2">{line.accountLabel}</td>
                          <td className="px-3 py-2 font-mono">{line.accountTypeLabel}</td>
                          <td className="px-3 py-2 text-right font-mono">
                            <span
                              className={cn(
                                line.balanceTone === "success"
                                  ? "text-success"
                                  : line.balanceTone === "danger"
                                    ? "text-danger"
                                    : "text-foreground"
                              )}
                            >
                              {line.balanceLabel}
                            </span>
                          </td>
                          <td className="px-3 py-2 text-right font-mono">{line.entryCountLabel}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ) : (
                <div
                  role={reconciliation.trialBalanceView.state === "error" ? "alert" : "status"}
                  className={cn(
                    "rounded-lg border px-4 py-4",
                    reconciliation.trialBalanceView.state === "error"
                      ? "border-danger/35 bg-danger/10 text-danger"
                      : "border-border/70 bg-secondary/25 text-muted-foreground"
                  )}
                >
                  <div className="text-sm font-semibold text-foreground">{reconciliation.trialBalanceView.emptyTitle}</div>
                  <p className="mt-2 text-sm leading-6">
                    {reconciliation.trialBalanceView.errorText ?? reconciliation.trialBalanceView.loadingText ?? reconciliation.trialBalanceView.emptyDetail}
                  </p>
                </div>
              )}
              {reconciliation.trialBalanceView.loadingText && reconciliation.trialBalanceView.hasRows ? (
                <p role="status" className="mt-3 text-sm text-muted-foreground">
                  {reconciliation.trialBalanceView.loadingText}
                </p>
              ) : null}
              {reconciliation.trialBalanceView.errorText && reconciliation.trialBalanceView.hasRows ? (
                <div role="alert" className="mt-3 rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  {reconciliation.trialBalanceView.errorText}
                </div>
              ) : null}
            </CardContent>
          </Card>
          <Card className="panel-surface">
            <CardHeader>
              <CardTitle>Reporting exports</CardTitle>
              <CardDescription>Entry points for report/export handoff using existing export infrastructure.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              <Button asChild>
                <a href={reporting.backendLinks[0].href} target="_blank" rel="noreferrer" aria-label={reporting.backendLinks[0].ariaLabel}>
                  {reporting.backendLinks[0].label}
                </a>
              </Button>
              <Button
                type="button"
                variant="outline"
                disabled={!reporting.exportCanRun}
                aria-label={reporting.exportAriaLabel}
                aria-busy={reporting.exportBusy}
                onClick={() => void reporting.runExport()}
              >
                {reporting.exportButtonLabel}
              </Button>
              <Button asChild variant="outline">
                <a href={reporting.backendLinks[1].href} target="_blank" rel="noreferrer" aria-label={reporting.backendLinks[1].ariaLabel}>
                  {reporting.backendLinks[1].label}
                </a>
              </Button>
              {reporting.exportStatusText ? (
                <p
                  role={reporting.exportStatusRole}
                  className={cn(
                    "rounded-lg border px-3 py-2 text-sm",
                    reporting.exportStatusTone === "success" ? "border-success/30 bg-success/10 text-success" : "",
                    reporting.exportStatusTone === "danger" ? "border-danger/30 bg-danger/10 text-danger" : "",
                    reporting.exportStatusTone === "neutral" ? "border-border/70 bg-secondary/25 text-muted-foreground" : ""
                  )}
                >
                  {reporting.exportStatusText}
                </p>
              ) : null}
            </CardContent>
          </Card>
        </section>
      ) : null}

      <section className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <BookCheck className="h-4 w-4 text-primary" />
              Reconciliation queue
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="overflow-x-auto rounded-xl border border-border/70">
              <table className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm">
                <thead className="bg-secondary/30">
                  <tr>
                    {["Run", "Strategy", "Mode", "Status", "Breaks", "Open", "Reconciliation", "Updated"].map((column) => (
                      <th key={column} className="px-3 py-2 font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                        {column}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/50">
                  {data.reconciliationQueue.map((item) => (
                    <tr key={item.runId} className="bg-background/20">
                      <td className="px-3 py-2 font-mono text-foreground">{item.runId}</td>
                      <td className="px-3 py-2 text-foreground">{item.strategyName}</td>
                      <td className="px-3 py-2 font-mono uppercase text-muted-foreground">{item.mode}</td>
                      <td className="px-3 py-2 text-foreground">{item.status}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{item.breakCount}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{item.openBreakCount}</td>
                      <td className={cn("px-3 py-2 font-mono", statusTone[item.reconciliationStatus])}>{item.reconciliationStatus}</td>
                      <td className="px-3 py-2 text-muted-foreground">{item.lastUpdated}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>

        <Card className="panel-surface">
          <CardHeader>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Landmark className="h-4 w-4 text-primary" />
                  {reporting.title}
                </CardTitle>
                <CardDescription className="mt-2">{reporting.description}</CardDescription>
              </div>
              <span className="w-fit rounded-sm border border-primary/35 bg-primary/10 px-2 py-1 font-mono text-[10px] font-medium uppercase tracking-[0.14em] text-primary">
                {reporting.countLabel}
              </span>
            </div>
          </CardHeader>
          <CardContent className="grid gap-4">
            <div className="min-w-0">
              <div className="mb-2 flex flex-wrap items-center justify-between gap-2 text-xs">
                <span className="font-medium uppercase tracking-[0.14em] text-muted-foreground">{reporting.listLabel}</span>
                <span className="font-mono text-muted-foreground">{reporting.visibleCountLabel}</span>
              </div>
              <div role="list" aria-label={reporting.listLabel} className="space-y-2">
                {reporting.hasRows ? reporting.rows.map((profile) => (
                  <div key={profile.id} role="listitem">
                    <button
                      type="button"
                      aria-pressed={profile.isSelected}
                      aria-controls={reporting.detailId}
                      aria-label={profile.selectAriaLabel}
                      onClick={() => reporting.selectProfile(profile.id)}
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
                          <div className="mt-1 truncate font-mono text-xs text-muted-foreground">{profile.targetLabel}</div>
                        </div>
                        <span className="shrink-0 rounded-sm border border-primary/30 bg-primary/10 px-2 py-0.5 font-mono text-[10px] uppercase tracking-[0.14em] text-primary">
                          {profile.formatLabel}
                        </span>
                      </div>
                      <p className="mt-2 line-clamp-2 text-sm leading-6 text-muted-foreground">{profile.description}</p>
                      <div className="mt-3 flex flex-wrap gap-2">
                        {profile.badges.map((badge) => (
                          <span key={`${profile.id}-${badge.label}`} className={reportingBadgeClass(badge.tone)}>
                            {badge.label}
                          </span>
                        ))}
                      </div>
                    </button>
                  </div>
                )) : (
                  <div role="status" className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning">
                    {reporting.emptyText}
                  </div>
                )}
              </div>
            </div>

            <aside
              id={reporting.detailId}
              aria-live="polite"
              data-testid="reporting-profile-detail"
              className="min-w-0 overflow-hidden rounded-lg border border-border/70 bg-background/35 p-4"
            >
              <div className="eyebrow-label">{reporting.statusTitle}</div>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">{reporting.statusDetail}</p>
              <p className="mt-2 font-mono text-xs text-muted-foreground">{reporting.nextAction}</p>
              {reporting.selectedProfile ? (
                <div id={reporting.selectedProfile.id} className="mt-4 border-t border-border/70 pt-4">
                  <div className="break-words font-semibold text-foreground">{reporting.selectedProfile.title}</div>
                  <div className="mt-1 break-words font-mono text-xs text-muted-foreground">{reporting.selectedProfile.subtitle}</div>
                  <p className="mt-3 break-words text-sm leading-6 text-muted-foreground">{reporting.selectedProfile.description}</p>
                  <dl className="mt-4 grid gap-2">
                    {reporting.selectedProfile.fields.map((field) => (
                      <div key={field.label} className="grid min-w-0 grid-cols-[minmax(0,0.6fr)_minmax(0,1fr)] items-start gap-3 rounded-md border border-border/60 bg-secondary/25 px-3 py-2">
                        <dt className="min-w-0 text-xs text-muted-foreground">{field.label}</dt>
                        <dd className={cn(
                          "min-w-0 break-words text-right font-mono text-xs text-foreground",
                          field.tone === "success" ? "text-success" : field.tone === "warning" ? "text-warning" : field.tone === "muted" ? "text-muted-foreground" : ""
                        )}>
                          {field.value}
                        </dd>
                      </div>
                    ))}
                  </dl>
                </div>
              ) : null}
            </aside>
          </CardContent>
        </Card>
      </section>

      {/* --- Security Master panel (shown when security-master workstream is active) --- */}
      {workstream === "security-master" && (
        <section className="space-y-6">
          {/* Search panel */}
          <Card className="panel-surface">
            <CardHeader>
              <div className="eyebrow-label">Security Master</div>
              <CardTitle className="flex items-center gap-2">
                <Search className="h-5 w-5 text-primary" />
                Security search
              </CardTitle>
              <CardDescription>
                Search by ticker, ISIN, CUSIP, FIGI, or display name. Results show classification and economic definition from the Security Master.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="space-y-2">
                <label htmlFor="security-master-search" className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                  Search securities
                </label>
                <input
                  id="security-master-search"
                  type="text"
                  value={securityMaster.query}
                  onChange={(e) => securityMaster.updateQuery(e.target.value)}
                  placeholder="Search securities…"
                  aria-controls={securityMaster.hasResults ? "security-master-results" : undefined}
                  aria-describedby="security-master-search-help security-master-search-status"
                  aria-invalid={securityMaster.searchErrorText ? true : undefined}
                  className="w-full rounded-lg border border-border/70 bg-secondary/30 px-3 py-2 text-sm text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary/50"
                />
                <p id="security-master-search-help" className="text-xs text-muted-foreground">
                  Search by ticker, ISIN, CUSIP, FIGI, or display name.
                </p>
              </div>

              <span className="sr-only" aria-live="polite">{securityMaster.statusAnnouncement}</span>
              {securityMaster.searchStatusText && (
                <p
                  id="security-master-search-status"
                  role={securityMaster.searching ? "status" : undefined}
                  className="text-sm text-muted-foreground"
                >
                  {securityMaster.searchStatusText}
                </p>
              )}
              {securityMaster.searchErrorText && (
                <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  {securityMaster.searchErrorText}
                </div>
              )}

              {securityMaster.hasResults && (
                <div className="overflow-x-auto rounded-xl border border-border/70">
                  <table id="security-master-results" aria-label={securityMaster.resultsTableLabel} className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm">
                    <caption className="sr-only">{securityMaster.searchStatusText}</caption>
                    <thead className="bg-secondary/30">
                      <tr>
                        {securityMaster.resultColumns.map((col) => (
                          <th key={col.id} className="px-3 py-2 font-semibold uppercase tracking-[0.14em] text-muted-foreground">{col.label}</th>
                        ))}
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-border/50">
                      {securityMaster.resultRows.map((s) => (
                        <tr
                          key={s.rowId}
                          aria-label={s.ariaLabel}
                          className={cn(
                            "bg-background/20 transition-colors hover:bg-secondary/30",
                            s.isSelected ? "bg-primary/10" : ""
                          )}
                        >
                          <td className="px-3 py-2">
                            <button
                              type="button"
                              className="rounded-sm text-left font-semibold text-foreground hover:text-primary focus:outline-none focus:ring-2 focus:ring-primary/40"
                              aria-pressed={s.isSelected}
                              aria-label={s.selectAriaLabel}
                              onClick={() => void securityMaster.selectSecurity(s.securityId)}
                            >
                              {s.displayName}
                            </button>
                          </td>
                          <td className="px-3 py-2 text-muted-foreground">{s.classification.assetClass}</td>
                          <td className="px-3 py-2 font-mono text-muted-foreground">{s.primaryIdentifierLabel}</td>
                          <td className="px-3 py-2 font-mono text-muted-foreground">{s.economicDefinition.currency}</td>
                          <td className={cn("px-3 py-2 font-mono uppercase", s.statusTone === "success" ? "text-success" : "text-warning")}>{s.status}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
              {securityMaster.identityLoading && <p role="status" className="text-sm text-muted-foreground">Loading identity drill-in…</p>}
              {securityMaster.identityErrorText && (
                <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  {securityMaster.identityErrorText}
                </div>
              )}
              {identity && (
                <div className="space-y-4">
                  <EntitySummary
                    eyebrow="Identity drill-in"
                    title={identity.title}
                    subtitle={identity.subtitle}
                    description={identity.description}
                    status={<Badge variant={identity.statusBadgeVariant} dot>{identity.statusLabel}</Badge>}
                    fields={identity.summaryFields}
                    ariaLabel={identity.ariaLabel}
                  />
                  <ToolbarStrip
                    ariaLabel="Security identity detail context"
                    items={[
                      { id: "identifiers", label: "Identifiers", value: String(identity.identifiers.length), active: true },
                      { id: "aliases", label: "Aliases", value: String(identity.aliases.length) },
                      { id: "status", label: "Status", value: identity.statusLabel }
                    ]}
                  />
                  <div>
                    <div className="mb-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{identity.identifiersTitle}</div>
                    {identity.identifiers.length === 0 ? (
                      <p role="status" className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm text-muted-foreground">
                        {identity.identifierEmptyText}
                      </p>
                    ) : (
                      <DenseDataTable
                        columns={identifierColumns}
                        rows={identity.identifiers}
                        getRowId={(identifier) => identifier.rowId}
                        getRowAriaLabel={(identifier) => identifier.ariaLabel}
                        emptyText={identity.identifierEmptyText}
                        ariaLabel={identity.identifiersTableLabel}
                        caption={identity.identifiersTableLabel}
                      />
                    )}
                  </div>

                  <div>
                    <div className="mb-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{identity.aliasesTitle}</div>
                    {identity.aliases.length === 0 ? (
                      <p role="status" className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm text-muted-foreground">
                        {identity.aliasEmptyText}
                      </p>
                    ) : (
                      <DenseDataTable
                        columns={aliasColumns}
                        rows={identity.aliases}
                        getRowId={(alias) => alias.rowId}
                        getRowAriaLabel={(alias) => alias.ariaLabel}
                        emptyText={identity.aliasEmptyText}
                        ariaLabel={identity.aliasesTableLabel}
                        caption={identity.aliasesTableLabel}
                      />
                    )}
                  </div>
                </div>
              )}
            </CardContent>
          </Card>

          {/* Conflicts panel */}
          <Card className="panel-surface">
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <ShieldCheck className="h-5 w-5 text-primary" />
                Identifier conflicts
                {securityMaster.openConflictCount > 0 && (
                  <span className="ml-2 inline-flex items-center rounded-sm border border-warning/35 bg-warning/10 px-2 py-0.5 font-mono text-[10px] font-medium uppercase tracking-[0.12em] text-warning">
                    {securityMaster.conflictCountLabel}
                  </span>
                )}
              </CardTitle>
              <CardDescription>
                Identifier ambiguities detected when multiple providers map the same identifier to different securities.
              </CardDescription>
            </CardHeader>
            <CardContent>
              {securityMaster.conflictsLoading && <p role="status" className="text-sm text-muted-foreground">Loading conflicts…</p>}
              {securityMaster.conflictsErrorText && (
                <div role="alert" className="mb-3 rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  {securityMaster.conflictsErrorText}
                </div>
              )}
              {securityMaster.conflictActionErrorText && (
                <div role="alert" className="mb-3 rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  {securityMaster.conflictActionErrorText}
                </div>
              )}
              {!securityMaster.conflictsLoading && securityMaster.conflicts !== null && !securityMaster.hasConflicts && (
                <p className="text-sm text-muted-foreground">{securityMaster.conflictEmptyText}</p>
              )}
              {securityMaster.hasConflicts && (
                <div className="space-y-3" aria-label={securityMaster.conflictSectionAriaLabel}>
                  {securityMaster.conflictRows.map((conflict) => (
                    <div
                      key={conflict.conflictId}
                      role="group"
                      aria-label={conflict.ariaLabel}
                      className={cn(
                        "rounded-lg border p-4",
                        conflict.statusTone === "warning" ? "border-warning/40 bg-warning/5" : "border-border/60 bg-secondary/20"
                      )}
                    >
                      <div className="flex flex-wrap items-start justify-between gap-3">
                        <div>
                          <div className="flex items-center gap-2">
                            <span className="text-sm font-semibold">{conflict.fieldLabel}</span>
                            <span className={cn("rounded-sm border px-2 py-0.5 font-mono text-[10px] uppercase tracking-[0.12em]", conflict.statusTone === "warning" ? "border-warning/35 bg-warning/10 text-warning" : "border-border/70 bg-secondary text-muted-foreground")}>
                              {conflict.statusLabel}
                            </span>
                          </div>
                          <div className="mt-2 grid gap-1 text-xs text-muted-foreground">
                            <span><span className="font-semibold text-foreground">Provider A:</span> {conflict.providerASummary}</span>
                            <span><span className="font-semibold text-foreground">Provider B:</span> {conflict.providerBSummary}</span>
                            <span className="font-mono text-xs">{conflict.detectedLabel}</span>
                          </div>
                        </div>
                        {conflict.actions.length > 0 && (
                          <div className="flex flex-wrap gap-2">
                            {conflict.actions.map((action) => (
                              <Button
                                key={action.resolution}
                                size="sm"
                                variant={action.variant}
                                disabled={action.disabled}
                                aria-label={action.ariaLabel}
                                onClick={() => void securityMaster.resolveConflict(conflict.conflictId, action.resolution)}
                              >
                                {action.label}
                              </Button>
                            ))}
                          </div>
                        )}
                      </div>
                      {conflict.resolutionStatusText && (
                        <p role="status" className="mt-3 text-xs text-muted-foreground">{conflict.resolutionStatusText}</p>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>

          {/* Corporate actions and trading parameters — shown when a security is selected */}
          {securityMaster.selectedSecurityId && (
            <div className="grid gap-4 xl:grid-cols-2">
              <CorporateActionsPanel
                securityId={securityMaster.selectedSecurityId}
                rows={securityMaster.corporateActionRows}
                loading={securityMaster.corporateActionsLoading}
                errorText={securityMaster.corporateActionsErrorText}
                hasActions={securityMaster.hasCorporateActions}
              />
              <TradingParametersPanel view={securityMaster.tradingParametersView} />
            </div>
          )}
        </section>
      )}

      {workstream === "reconciliation" && (
        <section
          id={reconciliation.detailActions?.breakChecklistTargetId ?? "reconciliation-break-queue"}
          aria-label="Reconciliation break checklist"
          className="space-y-4"
        >
          <Card className="panel-surface">
            <CardHeader>
              <CardTitle>Reconciliation break queue</CardTitle>
              <CardDescription>Review/resolve workflow with assignment and audit metadata.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              <span className="sr-only" aria-live="polite">{reconciliation.statusAnnouncement}</span>
              {reconciliation.loadingText && (
                <p role="status" className="text-sm text-muted-foreground">{reconciliation.loadingText}</p>
              )}
              {reconciliation.errorText && (
                <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  {reconciliation.errorText}
                </div>
              )}
              {reconciliation.actionErrorText && (
                <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  {reconciliation.actionErrorText}
                </div>
              )}
              {!reconciliation.loadingText && !reconciliation.hasBreaks && (
                <p className="rounded-lg border border-border/70 bg-secondary/25 px-3 py-3 text-sm text-muted-foreground">
                  {reconciliation.emptyText}
                </p>
              )}
              {reconciliation.rows.map((item) => (
                <div key={item.breakId} className="rounded-lg border border-border/70 p-3">
                  <div className="flex items-center justify-between gap-3">
                    <div>
                      <div className="font-semibold">{item.strategyName} · {item.category}</div>
                      <div className="text-xs text-muted-foreground">{item.reason}</div>
                    </div>
                    <div className="font-mono text-xs">{item.status}</div>
                  </div>
                  <div className="mt-2 flex flex-wrap gap-2">
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={!item.canAssign}
                      aria-label={item.assignAriaLabel}
                      onClick={() => void reconciliation.assignBreak(item.breakId)}
                    >
                      {item.assignLabel}
                    </Button>
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={!item.canResolve || resolveDialog.isOpenFor(item.breakId)}
                      aria-label={item.resolveAriaLabel}
                      onClick={() => resolveDialog.open(item.breakId, "Resolved")}
                    >
                      {item.resolveLabel}
                    </Button>
                    <Button
                      size="sm"
                      variant="ghost"
                      disabled={!item.canDismiss || resolveDialog.isOpenFor(item.breakId)}
                      aria-label={item.dismissAriaLabel}
                      onClick={() => resolveDialog.open(item.breakId, "Dismissed")}
                    >
                      {item.dismissLabel}
                    </Button>
                  </div>
                  {resolveDialog.active?.breakId === item.breakId && (
                    <form
                      className="mt-3 space-y-2 rounded-lg border border-border/50 bg-secondary/20 p-3"
                      aria-label={resolveDialog.active.formAriaLabel}
                      onSubmit={(e) => {
                        e.preventDefault();
                        void resolveDialog.submit();
                      }}
                    >
                      <label htmlFor={resolveDialog.active.inputId} className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                        {resolveDialog.active.label}
                      </label>
                      <input
                        id={resolveDialog.active.inputId}
                        type="text"
                        required
                        autoFocus
                        aria-describedby={resolveDialog.active.helpId}
                        placeholder={resolveDialog.active.placeholder}
                        value={resolveDialog.active.rationale}
                        onChange={(e) => resolveDialog.updateRationale(e.target.value)}
                        className="w-full rounded-md border border-border bg-background px-3 py-2 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                      />
                      <p id={resolveDialog.active.helpId} className="text-xs text-muted-foreground">
                        {resolveDialog.active.helpText}
                      </p>
                      <div className="flex gap-2">
                        <Button type="submit" size="sm" disabled={resolveDialog.active.isSubmitDisabled} aria-label={resolveDialog.active.submitAriaLabel}>
                          {resolveDialog.active.submitLabel}
                        </Button>
                        <Button type="button" size="sm" variant="ghost" aria-label={resolveDialog.active.cancelAriaLabel} onClick={resolveDialog.close}>
                          {resolveDialog.active.cancelLabel}
                        </Button>
                      </div>
                    </form>
                  )}
                </div>
              ))}
            </CardContent>
          </Card>

          <CalibrationSummaryPanel view={reconciliation.calibrationView} />
        </section>
      )}
    </div>
  );
}

function CalibrationSummaryPanel({ view }: { view: CalibrationSummaryViewState }) {
  const StatusIcon = view.statusIcon === "check" ? CheckCircle2 : AlertCircle;

  return (
          <Card className="panel-surface">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <BookCheck className="h-4 w-4 text-primary" />
          Calibration summary
        </CardTitle>
        <CardDescription>Tolerance profile health across all active reconciliation break routes.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <span className="sr-only" aria-live="polite">{view.statusAnnouncement}</span>
        {view.loadingText && <p role="status" className="text-sm text-muted-foreground">{view.loadingText}</p>}
        {view.errorText && (
          <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
            {view.errorText}
          </div>
        )}
        {!view.loadingText && !view.errorText && (
          <>
            <div className={cn("flex items-center gap-3 rounded-lg border px-4 py-3", view.statusBannerClassName)}>
              <StatusIcon aria-hidden="true" className={cn("size-4 shrink-0", view.statusTextClassName)} />
              <div className="flex-1 min-w-0">
                <span className={cn("text-sm font-semibold", view.statusTextClassName)}>{view.statusLabel}</span>
                {view.summary && <p className="mt-0.5 text-xs text-muted-foreground">{view.summary}</p>}
              </div>
              <span className="shrink-0 font-mono text-xs text-muted-foreground">as of {view.asOfLabel}</span>
            </div>
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
              {view.metricRows.map((metric) => (
                <div
                  key={metric.id}
                  role="group"
                  aria-label={metric.ariaLabel}
                  className="rounded-md border border-border/60 bg-secondary/25 px-3 py-2 text-center"
                >
                  <div className="text-xs text-muted-foreground">{metric.label}</div>
                  <div className={cn("mt-1 font-mono text-lg font-semibold tabular-nums", metric.tone === "warning" ? "text-warning" : "text-foreground")}>
                    {metric.value}
                  </div>
                </div>
              ))}
            </div>
            {view.hasProfiles && (
              <div>
                <div className="mb-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{view.profilesLabel}</div>
                <div className="overflow-x-auto rounded-lg border border-border/60">
                  <table className="min-w-full divide-y divide-border/50 text-left text-xs sm:text-sm">
                    <thead className="bg-secondary/30">
                      <tr>
                        {["Profile", "Route", "Severity", "Open", "Resolved", "Pending sign-off", "Updated"].map((col) => (
                          <th key={col} className="px-3 py-2 font-semibold uppercase tracking-[0.12em] text-muted-foreground">{col}</th>
                        ))}
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-border/40">
                      {view.profileRows.map((row) => (
                        <tr key={row.toleranceProfileId} aria-label={row.ariaLabel} className="hover:bg-secondary/20">
                          <td className="px-3 py-2 font-mono text-foreground">{row.toleranceProfileId}</td>
                          <td className="px-3 py-2 text-muted-foreground">{row.exceptionRoute}</td>
                          <td className="px-3 py-2 font-mono">{row.highestSeverity}</td>
                          <td className={cn("px-3 py-2 font-mono", row.openBreakCount > 0 ? "text-warning" : "text-foreground")}>{row.openBreakCount}</td>
                          <td className="px-3 py-2 font-mono text-foreground">{row.resolvedBreakCount}</td>
                          <td className={cn("px-3 py-2 font-mono", row.pendingSignoffCount > 0 ? "text-warning" : "text-foreground")}>{row.pendingSignoffCount}</td>
                          <td className="px-3 py-2 font-mono text-muted-foreground">{row.lastUpdatedLabel}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}

function CorporateActionsPanel({
  securityId,
  rows,
  loading,
  errorText,
  hasActions
}: {
  securityId: string;
  rows: CorporateActionRowViewModel[];
  loading: boolean;
  errorText: string | null;
  hasActions: boolean;
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Table2 className="h-4 w-4 text-primary" />
          Corporate actions
        </CardTitle>
        <CardDescription>
          Dividends, splits, spin-offs, and other corporate events for <span className="font-mono">{securityId}</span>.
        </CardDescription>
      </CardHeader>
      <CardContent>
        {loading && <p role="status" className="text-sm text-muted-foreground">Loading corporate actions…</p>}
        {errorText && (
          <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
            {errorText}
          </div>
        )}
        {!loading && !errorText && !hasActions && (
          <p className="text-sm text-muted-foreground">No corporate actions recorded for this security.</p>
        )}
        {hasActions && (
          <div className="overflow-x-auto rounded-lg border border-border/60">
            <table aria-label={`Corporate actions for ${securityId}`} className="min-w-full divide-y divide-border/50 text-left text-xs sm:text-sm">
              <thead className="bg-secondary/30">
                <tr>
                  {["Event type", "Ex-date", "Pay date", "Amount"].map((col) => (
                    <th key={col} className="px-3 py-2 font-semibold uppercase tracking-[0.12em] text-muted-foreground">{col}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-border/40">
                {rows.map((row) => (
                  <tr key={row.rowId} aria-label={row.ariaLabel} className="hover:bg-secondary/20">
                    <td className="px-3 py-2 font-semibold text-foreground">{row.eventTypeLabel}</td>
                    <td className="px-3 py-2 font-mono text-muted-foreground">{row.exDateLabel}</td>
                    <td className="px-3 py-2 font-mono text-muted-foreground">{row.payDateLabel}</td>
                    <td className="px-3 py-2 font-mono text-foreground">{row.amountLabel}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function TradingParametersPanel({ view }: { view: TradingParametersViewState }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <TrendingUp className="h-4 w-4 text-primary" />
          Trading parameters
        </CardTitle>
        <CardDescription>
          Lot size, tick size, margin, and circuit-breaker constraints
          {view.securityId ? <> for <span className="font-mono">{view.securityId}</span></> : null}
          {view.asOfLabel !== "—" ? <> as of {view.asOfLabel}</> : null}.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-2">
        {view.loadingText && <p role="status" className="text-sm text-muted-foreground">{view.loadingText}</p>}
        {view.errorText && (
          <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
            {view.errorText}
          </div>
        )}
        {!view.loadingText && !view.errorText && view.fields.length === 0 && (
          <p className="text-sm text-muted-foreground">No trading parameters available for this security.</p>
        )}
        {view.fields.length > 0 && (
          <dl className="grid gap-2">
            {view.fields.map((field) => (
              <div key={field.label} className="grid min-w-0 grid-cols-[minmax(0,0.6fr)_minmax(0,1fr)] items-start gap-3 rounded-md border border-border/60 bg-secondary/25 px-3 py-2">
                <dt className="min-w-0 text-xs text-muted-foreground">{field.label}</dt>
                <dd className={cn(
                  "min-w-0 break-words text-right font-mono text-xs",
                  field.tone === "warning" ? "text-warning" : "text-foreground"
                )}>
                  {field.value}
                </dd>
              </div>
            ))}
          </dl>
        )}
      </CardContent>
    </Card>
  );
}

function GovernanceHighlight({
  icon: Icon,
  title,
  description
}: {
  icon: typeof ShieldCheck;
  title: string;
  description: string;
}) {
  return (
    <div className="workspace-header-card">
      <Icon className="mb-3 h-5 w-5 text-primary" />
      <div className="font-semibold text-foreground">{title}</div>
      <p className="mt-2 text-sm leading-6 text-muted-foreground">{description}</p>
    </div>
  );
}

function GovernanceValue({ label, value, tone, ariaLabel }: { label: string; value: string; tone?: string; ariaLabel?: string }) {
  return (
    <div aria-label={ariaLabel} className="data-grid-surface flex items-center justify-between gap-4 px-3 py-2">
      <span className="text-muted-foreground">{label}</span>
      <span className={cn("font-mono text-foreground", tone)}>{value}</span>
    </div>
  );
}

function GovernanceChip({ label, value }: { label: string; value: string }) {
  return (
    <span className="toolbar-chip">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono capitalize text-foreground">{value}</span>
    </span>
  );
}

function cashFlowTextClass(tone: "default" | "success" | "warning" | "danger") {
  if (tone === "success") return "text-success";
  if (tone === "warning") return "text-warning";
  if (tone === "danger") return "text-danger";
  return "";
}

function cashFlowBadgeClass(tone: "default" | "success" | "warning" | "danger") {
  if (tone === "success") return "border-success/35 bg-success/10 text-success";
  if (tone === "warning") return "border-warning/35 bg-warning/10 text-warning";
  if (tone === "danger") return "border-danger/35 bg-danger/10 text-danger";
  return "border-border/70 bg-secondary text-muted-foreground";
}

function reportingBadgeClass(tone: "primary" | "success" | "warning" | "muted") {
  return cn(
    "rounded-sm border px-2 py-0.5 font-mono text-[10px] uppercase tracking-[0.12em]",
    tone === "primary" ? "border-primary/35 bg-primary/10 text-primary" : "",
    tone === "success" ? "border-success/35 bg-success/10 text-success" : "",
    tone === "warning" ? "border-warning/35 bg-warning/10 text-warning" : "",
    tone === "muted" ? "border-border/70 bg-secondary text-muted-foreground" : ""
  );
}
