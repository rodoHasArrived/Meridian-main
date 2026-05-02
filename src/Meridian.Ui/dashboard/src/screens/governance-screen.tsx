import { AlertCircle, BookCheck, CheckCircle2, Landmark, Search, ShieldCheck, Table2, TrendingUp, WalletCards } from "lucide-react";
import { useLocation } from "react-router-dom";
import { MetricCard } from "@/components/meridian/metric-card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import { workspaceForPath } from "@/lib/workspace";
import {
  buildReconciliationNarrative,
  resolveGovernanceWorkstream,
  useGovernanceCashFlowViewModel,
  useGovernanceReconciliationViewModel,
  useGovernanceReportingViewModel,
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
    description: "Open breaks, timing drift, and balanced runs stay visible without leaving governance."
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
  const selectedReconciliation = reconciliation.selectedReconciliation;
  const cashFlow = useGovernanceCashFlowViewModel(data?.cashFlow ?? null, pathname, workstream);
  const reporting = useGovernanceReportingViewModel(data?.reporting ?? null);
  const securityMaster = useSecurityMasterViewModel(workstream === "security-master");
  const identity = securityMaster.identityView;

  if (!data) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Loading Governance</CardTitle>
          <CardDescription>Waiting for reconciliation, cash-flow, and reporting summaries from the workstation bootstrap payload.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  const focus = focusCopy[workstream];

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

        <Card className="bg-panel-strong text-slate-50" role="region" aria-label={cashFlow.ariaLabel}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <div className="eyebrow-label">{cashFlow.eyebrow}</div>
                <CardTitle>{cashFlow.title}</CardTitle>
                <CardDescription className="mt-2 text-slate-300">
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

      {workstream === "reconciliation" && selectedReconciliation ? (
        <section className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
          <Card>
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

          <Card className="bg-panel-strong text-slate-50">
            <CardHeader>
              <div className="eyebrow-label">Reconciliation Detail</div>
              <CardTitle>{selectedReconciliation.strategyName}</CardTitle>
              <CardDescription className="text-slate-300">
                {selectedReconciliation.runId} is currently {selectedReconciliation.reconciliationStatus}.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4 text-sm">
              <GovernanceValue label="Mode" value={selectedReconciliation.mode.toUpperCase()} />
              <GovernanceValue label="Run status" value={selectedReconciliation.status} />
              <GovernanceValue label="Break count" value={String(selectedReconciliation.breakCount)} />
              <GovernanceValue label="Open breaks" value={String(selectedReconciliation.openBreakCount)} tone={selectedReconciliation.openBreakCount === 0 ? "text-success" : "text-warning"} />
              <GovernanceValue label="Last updated" value={selectedReconciliation.lastUpdated} />
              <div className="rounded-lg border border-border/70 bg-background/70 p-4 text-slate-200">
                {buildReconciliationNarrative(selectedReconciliation)}
              </div>
              <div className="flex gap-3">
                <Button variant="secondary">Open break checklist</Button>
                <Button variant="outline" className="border-border/70 bg-transparent text-foreground hover:bg-secondary/60">
                  Review audit packet
                </Button>
              </div>
            </CardContent>
          </Card>
        </section>
      ) : null}

      {workstream === "ledger" && selectedReconciliation ? (
        <section className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
          <Card aria-labelledby="trial-balance-title" aria-describedby="trial-balance-description">
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
          <Card>
            <CardHeader>
              <CardTitle>Reporting exports</CardTitle>
              <CardDescription>Entry points for report/export handoff using existing export infrastructure.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              <Button asChild><a href="/api/export/preview" target="_blank" rel="noreferrer">Preview report payload</a></Button>
              <Button asChild variant="outline"><a href="/api/export/analysis" target="_blank" rel="noreferrer">Run reporting export</a></Button>
              <Button asChild variant="outline"><a href="/api/export/formats" target="_blank" rel="noreferrer">List export formats</a></Button>
            </CardContent>
          </Card>
        </section>
      ) : null}

      <section className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
        <Card>
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

        <Card>
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
          <Card>
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

              {securityMaster.results && securityMaster.results.length > 0 && (
                <div className="overflow-x-auto rounded-xl border border-border/70">
                  <table id="security-master-results" aria-label="Security search results" className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm">
                    <caption className="sr-only">{securityMaster.searchStatusText}</caption>
                    <thead className="bg-secondary/30">
                      <tr>
                        {["Name", "Asset Class", "Primary ID", "Currency", "Status"].map((col) => (
                          <th key={col} className="px-3 py-2 font-semibold uppercase tracking-[0.14em] text-muted-foreground">{col}</th>
                        ))}
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-border/50">
                      {securityMaster.results.map((s) => (
                        <tr
                          key={s.securityId}
                          className={cn(
                            "bg-background/20 transition-colors hover:bg-secondary/30",
                            securityMaster.selectedSecurityId === s.securityId ? "bg-primary/10" : ""
                          )}
                        >
                          <td className="px-3 py-2">
                            <button
                              type="button"
                              className="rounded-sm text-left font-semibold text-foreground hover:text-primary focus:outline-none focus:ring-2 focus:ring-primary/40"
                              aria-pressed={securityMaster.selectedSecurityId === s.securityId}
                              aria-label={`Open identity drill-in for ${s.displayName}`}
                              onClick={() => void securityMaster.selectSecurity(s.securityId)}
                            >
                              {s.displayName}
                            </button>
                          </td>
                          <td className="px-3 py-2 text-muted-foreground">{s.classification.assetClass}</td>
                          <td className="px-3 py-2 font-mono text-muted-foreground">
                            {s.classification.primaryIdentifierKind ? `${s.classification.primaryIdentifierKind}: ${s.classification.primaryIdentifierValue}` : "—"}
                          </td>
                          <td className="px-3 py-2 font-mono text-muted-foreground">{s.economicDefinition.currency}</td>
                          <td className={cn("px-3 py-2 font-mono uppercase", s.status === "Active" ? "text-success" : "text-warning")}>{s.status}</td>
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
                <div
                  role="region"
                  aria-label={identity.ariaLabel}
                  className="space-y-4 rounded-lg border border-border/70 bg-secondary/20 p-4"
                >
                  <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                    <div>
                      <h4 className="font-semibold text-foreground">{identity.title}</h4>
                      <p className="mt-1 font-mono text-xs text-muted-foreground">{identity.subtitle}</p>
                      <p className="mt-2 text-sm text-muted-foreground">{identity.description}</p>
                    </div>
                    <Badge variant={identity.statusBadgeVariant} dot>{identity.statusLabel}</Badge>
                  </div>

                  <dl className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
                    {identity.summaryFields.map((field) => (
                      <div key={field.label} className="rounded-md border border-border/60 bg-background/40 px-3 py-2">
                        <dt className="text-xs text-muted-foreground">{field.label}</dt>
                        <dd className="mt-1 break-words font-mono text-xs text-foreground">{field.value}</dd>
                      </div>
                    ))}
                  </dl>

                  <div>
                    <div className="mb-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{identity.identifiersTitle}</div>
                    {identity.identifiers.length === 0 ? (
                      <p role="status" className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm text-muted-foreground">
                        {identity.identifierEmptyText}
                      </p>
                    ) : (
                      <div className="overflow-x-auto rounded-lg border border-border/60">
                        <table aria-label={identity.identifiersTableLabel} className="min-w-full divide-y divide-border/50 text-left text-xs sm:text-sm">
                          <caption className="sr-only">{identity.identifiersTableLabel}</caption>
                          <thead className="bg-secondary/30">
                            <tr>{["Kind", "Value", "Provider", "State", "Valid range"].map((col) => <th key={col} className="px-3 py-2">{col}</th>)}</tr>
                          </thead>
                          <tbody className="divide-y divide-border/40">
                            {identity.identifiers.map((identifier) => (
                              <tr key={identifier.rowId} aria-label={identifier.ariaLabel}>
                                <td className="px-3 py-2 font-mono">{identifier.kind}</td>
                                <td className="px-3 py-2 font-mono text-foreground">{identifier.value}</td>
                                <td className="px-3 py-2">{identifier.providerLabel}</td>
                                <td className="px-3 py-2">
                                  <Badge variant={identifier.primaryBadgeVariant}>{identifier.primaryLabel}</Badge>
                                </td>
                                <td className="px-3 py-2 font-mono text-muted-foreground">{identifier.validRangeLabel}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    )}
                  </div>

                  <div>
                    <div className="mb-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{identity.aliasesTitle}</div>
                    {identity.aliases.length === 0 ? (
                      <p role="status" className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm text-muted-foreground">
                        {identity.aliasEmptyText}
                      </p>
                    ) : (
                      <div className="overflow-x-auto rounded-lg border border-border/60">
                        <table aria-label={identity.aliasesTableLabel} className="min-w-full divide-y divide-border/50 text-left text-xs sm:text-sm">
                          <caption className="sr-only">{identity.aliasesTableLabel}</caption>
                          <thead className="bg-secondary/30">
                            <tr>{["Kind", "Alias", "Provider", "Scope", "State", "Valid range"].map((col) => <th key={col} className="px-3 py-2">{col}</th>)}</tr>
                          </thead>
                          <tbody className="divide-y divide-border/40">
                            {identity.aliases.map((alias) => (
                              <tr key={alias.rowId} aria-label={alias.ariaLabel}>
                                <td className="px-3 py-2 font-mono">{alias.aliasKind}</td>
                                <td className="px-3 py-2">
                                  <div className="font-mono text-foreground">{alias.aliasValue}</div>
                                  <div className="mt-1 text-xs text-muted-foreground">{alias.reasonText}</div>
                                </td>
                                <td className="px-3 py-2">{alias.providerLabel}</td>
                                <td className="px-3 py-2">{alias.scope}</td>
                                <td className="px-3 py-2">
                                  <Badge variant={alias.enabledBadgeVariant}>{alias.enabledLabel}</Badge>
                                </td>
                                <td className="px-3 py-2 font-mono text-muted-foreground">{alias.validRangeLabel}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    )}
                  </div>
                </div>
              )}
            </CardContent>
          </Card>

          {/* Conflicts panel */}
          <Card>
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
        <section className="space-y-4">
          <Card>
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
                      disabled={!item.canResolve}
                      aria-label={item.resolveAriaLabel}
                      onClick={() => void reconciliation.resolveBreak(item.breakId, "Resolved")}
                    >
                      {item.resolveLabel}
                    </Button>
                    <Button
                      size="sm"
                      variant="ghost"
                      disabled={!item.canDismiss}
                      aria-label={item.dismissAriaLabel}
                      onClick={() => void reconciliation.resolveBreak(item.breakId, "Dismissed")}
                    >
                      {item.dismissLabel}
                    </Button>
                  </div>
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
  const StatusIcon = view.statusTone === "success" ? CheckCircle2 : view.statusTone === "danger" ? AlertCircle : AlertCircle;
  const statusClass = view.statusTone === "success" ? "text-success" : view.statusTone === "danger" ? "text-danger" : "text-warning";
  const bannerClass = view.statusTone === "success" ? "border-success/30 bg-success/5" : view.statusTone === "danger" ? "border-danger/30 bg-danger/5" : "border-warning/30 bg-warning/5";

  return (
    <Card>
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
            <div className={cn("flex items-center gap-3 rounded-lg border px-4 py-3", bannerClass)}>
              <StatusIcon aria-hidden="true" className={cn("size-4 shrink-0", statusClass)} />
              <div className="flex-1 min-w-0">
                <span className={cn("text-sm font-semibold", statusClass)}>{view.statusLabel}</span>
                {view.summary && <p className="mt-0.5 text-xs text-muted-foreground">{view.summary}</p>}
              </div>
              <span className="shrink-0 font-mono text-xs text-muted-foreground">as of {view.asOfLabel}</span>
            </div>
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
              {[
                { label: "Total breaks", value: view.totalBreakCount },
                { label: "Open", value: view.openBreakCount, warn: view.openBreakCount > 0 },
                { label: "Critical open", value: view.criticalOpenBreakCount, warn: view.criticalOpenBreakCount > 0 },
                { label: "Pending sign-off", value: view.pendingSignoffCount, warn: view.pendingSignoffCount > 0 },
                { label: "Signed off", value: view.signedOffCount },
                { label: "Missing metadata", value: view.missingMetadataCount, warn: view.missingMetadataCount > 0 }
              ].map(({ label, value, warn }) => (
                <div key={label} className="rounded-md border border-border/60 bg-secondary/25 px-3 py-2 text-center">
                  <div className="text-xs text-muted-foreground">{label}</div>
                  <div className={cn("mt-1 font-mono text-lg font-semibold tabular-nums", warn ? "text-warning" : "text-foreground")}>
                    {value}
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
    <div className="rounded-xl border border-border/70 bg-secondary/35 p-4">
      <Icon className="mb-3 h-5 w-5 text-primary" />
      <div className="font-semibold">{title}</div>
      <p className="mt-2 text-sm leading-6 text-muted-foreground">{description}</p>
    </div>
  );
}

function GovernanceValue({ label, value, tone, ariaLabel }: { label: string; value: string; tone?: string; ariaLabel?: string }) {
  return (
    <div aria-label={ariaLabel} className="flex items-center justify-between gap-4 rounded-lg border border-border/70 bg-secondary/40 px-3 py-2">
      <span className="text-slate-300">{label}</span>
      <span className={cn("font-mono text-slate-50", tone)}>{value}</span>
    </div>
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
