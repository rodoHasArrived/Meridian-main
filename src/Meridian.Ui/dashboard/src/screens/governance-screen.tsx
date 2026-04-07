import { BookCheck, Landmark, Search, ShieldCheck, WalletCards } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { useLocation, useSearchParams } from "react-router-dom";
import { MetricCard } from "@/components/meridian/metric-card";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import {
  getReconciliationBreakQueue,
  getSecurityDetail,
  getSecurityEconomicDefinition,
  getRunTrialBalance,
  getSecurityConflicts,
  getSecurityHistory,
  getSecurityIdentity,
  resolveReconciliationBreak,
  resolveSecurityConflict,
  reviewReconciliationBreak,
  searchSecurities
} from "@/lib/api";
import { cn } from "@/lib/utils";
import type {
  GovernanceLatestReconciliation,
  GovernanceWorkspaceResponse,
  ReconciliationBreakQueueItem,
  ReconciliationSecurityCoverageIssue,
  ResolveConflictRequest,
  SecurityCoverageReference,
  SecurityEconomicDefinitionSummary,
  SecurityIdentityDrillIn,
  SecurityMasterConflict,
  SecurityMasterEntry,
  SecurityMasterHistoryEvent
} from "@/types";

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
  }
};

export function GovernanceScreen({ data }: GovernanceScreenProps) {
  const { pathname } = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();
  const workstream = useMemo(() => {
    if (pathname.includes("/reconciliation")) {
      return "reconciliation";
    }

    if (pathname.includes("/security-master")) {
      return "security-master";
    }

    return "ledger";
  }, [pathname]);
  const securityIdParam = searchParams.get("securityId");
  const securityQueryParam = searchParams.get("query")?.trim() ?? "";
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);
  const selectedReconciliation = data?.reconciliationQueue.find((item) => item.runId === selectedRunId) ?? data?.reconciliationQueue[0] ?? null;

  useEffect(() => {
    if (!data || selectedRunId || data.reconciliationQueue.length === 0) {
      return;
    }

    setSelectedRunId(data.reconciliationQueue[0].runId);
  }, [data, selectedRunId]);

  // --- Security Master search state ---
  const [securityQuery, setSecurityQuery] = useState("");
  const [securityResults, setSecurityResults] = useState<SecurityMasterEntry[] | null>(null);
  const [securitySearching, setSecuritySearching] = useState(false);
  const securitySearchRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [selectedSecurityId, setSelectedSecurityId] = useState<string | null>(securityIdParam);
  const [securityDetail, setSecurityDetail] = useState<SecurityMasterEntry | null>(null);
  const [securityIdentity, setSecurityIdentity] = useState<SecurityIdentityDrillIn | null>(null);
  const [securityHistory, setSecurityHistory] = useState<SecurityMasterHistoryEvent[] | null>(null);
  const [securityEconomicDefinition, setSecurityEconomicDefinition] = useState<SecurityEconomicDefinitionSummary | null>(null);
  const [securityDrillInLoading, setSecurityDrillInLoading] = useState(false);
  const [securityDrillInError, setSecurityDrillInError] = useState<string | null>(null);

  // --- Security Master conflicts state ---
  const [conflicts, setConflicts] = useState<SecurityMasterConflict[] | null>(null);
  const [conflictsLoading, setConflictsLoading] = useState(false);
  const [conflictResolvingId, setConflictResolvingId] = useState<string | null>(null);
  const [breakQueue, setBreakQueue] = useState<ReconciliationBreakQueueItem[]>([]);
  const [breakActionId, setBreakActionId] = useState<string | null>(null);
  const [trialBalance, setTrialBalance] = useState<Array<{ accountName: string; accountType: string; balance: number; entryCount: number }>>([]);

  function setSecurityRouteState(next: { query?: string | null; securityId?: string | null }) {
    const updated = new URLSearchParams(searchParams);

    if (next.query !== undefined) {
      if (next.query && next.query.trim()) {
        updated.set("query", next.query.trim());
      } else {
        updated.delete("query");
      }
    }

    if (next.securityId !== undefined) {
      if (next.securityId) {
        updated.set("securityId", next.securityId);
      } else {
        updated.delete("securityId");
      }
    }

    setSearchParams(updated, { replace: true });
  }

  async function runSecuritySearch(query: string) {
    const trimmed = query.trim();
    if (!trimmed) {
      setSecurityResults(null);
      return;
    }

    setSecuritySearching(true);
    try {
      const results = await searchSecurities(trimmed);
      setSecurityResults(results);
    } catch {
      setSecurityResults([]);
    } finally {
      setSecuritySearching(false);
    }
  }

  async function loadSecurityDrillIn(securityId: string) {
    setSelectedSecurityId(securityId);
    setSecurityDrillInLoading(true);
    setSecurityDrillInError(null);

    try {
      const [detailResult, identityResult, historyResult, economicDefinitionResult] = await Promise.allSettled([
        getSecurityDetail(securityId),
        getSecurityIdentity(securityId),
        getSecurityHistory(securityId),
        getSecurityEconomicDefinition(securityId)
      ]);

      if (detailResult.status === "rejected") {
        throw detailResult.reason instanceof Error ? detailResult.reason : new Error("Failed to load Security Master detail.");
      }

      setSecurityDetail(detailResult.value);
      setSecurityIdentity(identityResult.status === "fulfilled" ? identityResult.value : null);
      setSecurityHistory(historyResult.status === "fulfilled" ? historyResult.value : []);
      setSecurityEconomicDefinition(economicDefinitionResult.status === "fulfilled" ? economicDefinitionResult.value : null);
    } catch (err) {
      setSecurityDetail(null);
      setSecurityIdentity(null);
      setSecurityHistory(null);
      setSecurityEconomicDefinition(null);
      setSecurityDrillInError(err instanceof Error ? err.message : "Failed to load Security Master drill-in.");
    } finally {
      setSecurityDrillInLoading(false);
    }
  }

  function handleSelectSecurity(securityId: string, query?: string | null) {
    if (query && query.trim()) {
      setSecurityQuery(query);
    }

    setSecurityRouteState({ query: query ?? securityQueryParam, securityId });
  }

  function handleReviewReferenceSelect(reference: SecurityCoverageReference) {
    if (reference.securityId) {
      handleSelectSecurity(reference.securityId, reference.symbol);
      return;
    }

    setSelectedSecurityId(null);
    setSecurityDetail(null);
    setSecurityIdentity(null);
    setSecurityHistory(null);
    setSecurityEconomicDefinition(null);
    setSecurityDrillInError(null);
    setSecurityQuery(reference.symbol);
    setSecurityResults(null);
    setSecurityRouteState({ query: reference.symbol, securityId: null });
    void runSecuritySearch(reference.symbol);
  }

  useEffect(() => {
    if (workstream !== "security-master") return;
    setConflictsLoading(true);
    getSecurityConflicts()
      .then(setConflicts)
      .catch(() => setConflicts([]))
      .finally(() => setConflictsLoading(false));
  }, [workstream]);

  useEffect(() => {
    if (workstream !== "reconciliation") return;
    getReconciliationBreakQueue().then(setBreakQueue).catch(() => setBreakQueue(data?.breakQueue ?? []));
  }, [workstream, data?.breakQueue]);

  useEffect(() => {
    if (!selectedReconciliation || workstream !== "ledger") return;
    getRunTrialBalance(selectedReconciliation.runId)
      .then((rows) => setTrialBalance(rows))
      .catch(() => setTrialBalance([]));
  }, [selectedReconciliation, workstream]);

  useEffect(() => {
    if (workstream !== "security-master" || !securityQueryParam) {
      return;
    }

    setSecurityQuery((current) => current === securityQueryParam ? current : securityQueryParam);
    void runSecuritySearch(securityQueryParam);
  }, [securityQueryParam, workstream]);

  useEffect(() => {
    if (workstream !== "security-master") {
      return;
    }

    if (!securityIdParam) {
      setSelectedSecurityId(null);
      setSecurityDetail(null);
      setSecurityIdentity(null);
      setSecurityHistory(null);
      setSecurityEconomicDefinition(null);
      setSecurityDrillInError(null);
      return;
    }

    void loadSecurityDrillIn(securityIdParam);
  }, [securityIdParam, workstream]);

  function handleSecurityQueryChange(q: string) {
    setSecurityQuery(q);
    if (securitySearchRef.current) clearTimeout(securitySearchRef.current);
    if (!q.trim()) {
      setSecurityResults(null);
      setSecurityRouteState({ query: null, securityId: selectedSecurityId });
      return;
    }
    securitySearchRef.current = setTimeout(() => {
      setSecurityRouteState({ query: q.trim(), securityId: selectedSecurityId });
    }, 350);
  }

  async function handleResolveConflict(conflictId: string, resolution: ResolveConflictRequest["resolution"]) {
    setConflictResolvingId(conflictId);
    try {
      await resolveSecurityConflict({ conflictId, resolution, resolvedBy: "operator" });
      try {
        const refreshed = await getSecurityConflicts();
        setConflicts(refreshed);
      } catch {
        setConflicts((prev) => prev ?? []);
      }
    } finally {
      setConflictResolvingId(null);
    }
  }

  async function handleAssignBreak(breakId: string) {
    setBreakActionId(breakId);
    try {
      const updated = await reviewReconciliationBreak({ breakId, assignedTo: "ops.gov", reviewedBy: "ops.gov" });
      setBreakQueue((prev) => prev.map((item) => (item.breakId === breakId ? updated : item)));
    } finally {
      setBreakActionId(null);
    }
  }

  async function handleResolveBreak(breakId: string, status: "Resolved" | "Dismissed") {
    setBreakActionId(breakId);
    try {
      const updated = await resolveReconciliationBreak({ breakId, status, resolvedBy: "ops.gov", resolutionNote: "Reviewed in governance panel." });
      setBreakQueue((prev) => prev.map((item) => (item.breakId === breakId ? updated : item)));
    } finally {
      setBreakActionId(null);
    }
  }

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
  const selectedCoverage = selectedReconciliation?.securityCoverage ?? null;
  const reviewReferences = selectedCoverage?.reviewReferences ?? [];
  const latestCoverageIssues = selectedReconciliation?.latestReconciliation?.securityCoverageIssues ?? [];
  const openConflictCount = conflicts?.filter((c) => c.status === "Open").length ?? 0;

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
            <div className="eyebrow-label">Governance Lane</div>
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
              description="Export profiles stay close to governance workflows instead of living in a separate tool."
            />
          </CardContent>
        </Card>

        <Card className="bg-panel-strong text-slate-50">
          <CardHeader>
            <div className="eyebrow-label">Cash Flow</div>
            <CardTitle>{data.cashFlow.summary}</CardTitle>
            <CardDescription className="text-slate-300">
              Route focus at <code className="rounded bg-white/10 px-1 py-0.5">{pathname}</code> reuses the same governance summary payload.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-3 text-sm">
            <GovernanceValue label="Portfolio cash" value={formatCurrency(data.cashFlow.totalCash)} />
            <GovernanceValue label="Ledger cash" value={formatCurrency(data.cashFlow.totalLedgerCash)} />
            <GovernanceValue label="Net variance" value={formatCurrency(data.cashFlow.netVariance)} tone={data.cashFlow.netVariance === 0 ? "text-success" : "text-warning"} />
            <GovernanceValue label="Financing" value={formatCurrency(data.cashFlow.totalFinancing)} />
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
                  onClick={() => setSelectedRunId(item.runId)}
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
              <div className="rounded-xl bg-white/10 p-4 text-slate-200">
                {buildReconciliationNarrative(selectedReconciliation)}
              </div>
              <div className="flex gap-3">
                <Button variant="secondary">Open break checklist</Button>
                <Button variant="outline" className="border-white/20 bg-transparent text-slate-50 hover:bg-white/10">
                  Review audit packet
                </Button>
              </div>
            </CardContent>
          </Card>
        </section>
      ) : null}

      {workstream === "ledger" && selectedReconciliation ? (
        <section className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
          <Card>
            <CardHeader>
              <CardTitle>Multi-ledger trial balance</CardTitle>
              <CardDescription>Baseline ledger balances for {selectedReconciliation.runId} grouped by account type.</CardDescription>
            </CardHeader>
            <CardContent>
              <div className="overflow-x-auto rounded-xl border border-border/70">
                <table className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm">
                  <thead className="bg-secondary/30">
                    <tr>{["Account", "Type", "Balance", "Entries"].map((c) => <th key={c} className="px-3 py-2">{c}</th>)}</tr>
                  </thead>
                  <tbody className="divide-y divide-border/50">
                    {trialBalance.map((line) => (
                      <tr key={`${line.accountName}-${line.accountType}`}>
                        <td className="px-3 py-2">{line.accountName}</td>
                        <td className="px-3 py-2 font-mono">{line.accountType}</td>
                        <td className="px-3 py-2 font-mono">{formatCurrency(line.balance)}</td>
                        <td className="px-3 py-2 font-mono">{line.entryCount}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>Governance exports</CardTitle>
              <CardDescription>Entry points for report/export handoff using existing export infrastructure.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              <Button asChild><a href="/api/export/preview" target="_blank" rel="noreferrer">Preview report payload</a></Button>
              <Button asChild variant="outline"><a href="/api/export/analysis" target="_blank" rel="noreferrer">Run governance export</a></Button>
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
            <CardTitle className="flex items-center gap-2 text-base">
              <Landmark className="h-4 w-4 text-primary" />
              Reporting profiles
            </CardTitle>
            <CardDescription>{data.reporting.summary}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {data.reporting.profiles.slice(0, 4).map((profile) => (
              <div key={profile.id} className="rounded-xl border border-border/70 bg-secondary/30 p-4">
                <div className="flex items-center justify-between gap-3">
                  <div className="font-semibold">{profile.name}</div>
                  <div className="font-mono text-xs uppercase tracking-[0.16em] text-primary">{profile.format}</div>
                </div>
                <div className="mt-2 text-sm text-muted-foreground">{profile.description}</div>
                <div className="mt-3 flex items-center justify-between gap-4 text-sm">
                  <span className="text-muted-foreground">Target</span>
                  <span className="font-mono">{profile.targetTool}</span>
                </div>
              </div>
            ))}
          </CardContent>
        </Card>
      </section>

      {workstream === "security-master" && (
        <section className="space-y-6">
          <section className="grid gap-4 xl:grid-cols-[0.95fr_1.05fr]">
            <Card>
              <CardHeader>
                <div className="eyebrow-label">Coverage Review</div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <ShieldCheck className="h-4 w-4 text-primary" />
                  Security coverage queue
                </CardTitle>
                <CardDescription>
                  Reconciliation runs with partial or unresolved Security Master coverage stay linked to the same authoritative review queue.
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-3">
                {data.reconciliationQueue
                  .filter((item) => item.securityCoverage.hasIssues)
                  .map((item) => (
                    <button
                      key={item.runId}
                      type="button"
                      onClick={() => setSelectedRunId(item.runId)}
                      className={cn(
                        "w-full rounded-xl border px-4 py-4 text-left transition-colors",
                        item.runId === selectedReconciliation?.runId
                          ? "border-primary/50 bg-primary/10"
                          : "border-border/70 bg-secondary/30 hover:bg-secondary/45"
                      )}
                    >
                      <div className="flex items-center justify-between gap-3">
                        <div className="font-semibold">{item.strategyName}</div>
                        <div className={cn("rounded-full px-2 py-0.5 text-[11px] font-mono uppercase", coverageTone(item.securityCoverage.tone))}>
                          {item.securityCoverage.reviewReferences.length} review
                        </div>
                      </div>
                      <div className="mt-2 flex flex-wrap gap-3 text-xs text-muted-foreground">
                        <span className="font-mono">{item.runId}</span>
                        <span>{item.reconciliationStatus}</span>
                      </div>
                      <p className="mt-3 text-sm text-muted-foreground">{item.securityCoverage.summary}</p>
                    </button>
                  ))}
                {data.reconciliationQueue.every((item) => !item.securityCoverage.hasIssues) && (
                  <p className="text-sm text-muted-foreground">No security coverage review items are currently open.</p>
                )}
              </CardContent>
            </Card>

            <Card className="bg-panel-strong text-slate-50">
              <CardHeader>
                <div className="eyebrow-label">Selected Run</div>
                <CardTitle>{selectedReconciliation?.strategyName ?? "Security coverage review"}</CardTitle>
                <CardDescription className="text-slate-300">
                  {selectedCoverage
                    ? selectedCoverage.summary
                    : "Select a reconciliation run to inspect review references and drill into authoritative instrument details."}
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-4 text-sm">
                {selectedCoverage ? (
                  <>
                    <div className="grid gap-3 sm:grid-cols-3">
                      <GovernanceValue label="Portfolio partial" value={String(selectedCoverage.portfolioPartial)} />
                      <GovernanceValue label="Portfolio missing" value={String(selectedCoverage.portfolioMissing)} />
                      <GovernanceValue label="Ledger review" value={String(selectedCoverage.ledgerPartial + selectedCoverage.ledgerMissing)} />
                    </div>

                    <div className="space-y-3 rounded-xl bg-white/10 p-4">
                      <div className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-300">Review references</div>
                      {reviewReferences.length === 0 ? (
                        <p className="text-sm text-slate-200">No partial or unresolved references remain on this run.</p>
                      ) : (
                        reviewReferences.slice(0, 6).map((reference) => (
                          <div key={`${reference.source}-${reference.symbol}-${reference.accountName ?? "none"}-${reference.coverageStatus}`} className="rounded-lg border border-white/10 bg-black/10 p-3">
                            <div className="flex flex-wrap items-center justify-between gap-3">
                              <div>
                                <div className="flex items-center gap-2">
                                  <span className="font-semibold text-slate-50">{reference.displayName}</span>
                                  <span className={cn("rounded-full px-2 py-0.5 text-[11px] font-mono uppercase", coverageTone(reference.coverageStatus === "Resolved" ? "success" : "warning"))}>
                                    {reference.coverageStatus}
                                  </span>
                                </div>
                                <div className="mt-1 flex flex-wrap gap-3 text-xs text-slate-300">
                                  <span>{reference.source}</span>
                                  <span className="font-mono">{reference.symbol}</span>
                                  {reference.accountName ? <span>{reference.accountName}</span> : null}
                                  {reference.assetClass ? <span>{reference.assetClass}</span> : null}
                                  {reference.subType ? <span>{reference.subType}</span> : null}
                                  {reference.currency ? <span>{reference.currency}</span> : null}
                                </div>
                              </div>
                              <div className="flex flex-wrap gap-2">
                                <Button size="sm" variant="secondary" onClick={() => handleReviewReferenceSelect(reference)}>
                                  {reference.securityId ? "Load drill-in" : "Search symbol"}
                                </Button>
                                <Button asChild size="sm" variant="outline" className="border-white/20 bg-transparent text-slate-50 hover:bg-white/10">
                                  <a href={buildSecurityMasterHref(reference)}>{reference.securityId ? "Open deep link" : "Open search deep link"}</a>
                                </Button>
                              </div>
                            </div>
                            <p className="mt-2 text-xs text-slate-200">
                              {reference.coverageReason ?? "Coverage review is required before downstream reporting can rely on this mapping."}
                            </p>
                            {(reference.matchedIdentifierKind || reference.matchedProvider) && (
                              <div className="mt-2 flex flex-wrap gap-3 text-[11px] text-slate-300">
                                {reference.matchedIdentifierKind && reference.matchedIdentifierValue ? (
                                  <span className="font-mono">
                                    {reference.matchedIdentifierKind}: {reference.matchedIdentifierValue}
                                  </span>
                                ) : null}
                                {reference.matchedProvider ? <span>{reference.matchedProvider}</span> : null}
                              </div>
                            )}
                          </div>
                        ))
                      )}
                    </div>

                    <div className="space-y-3 rounded-xl bg-white/10 p-4">
                      <div className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-300">Latest reconciliation issues</div>
                      {latestCoverageIssues.length === 0 ? (
                        <p className="text-sm text-slate-200">No reconciliation-side security coverage issues were returned for this run.</p>
                      ) : (
                        latestCoverageIssues.slice(0, 5).map((issue) => (
                          <CoverageIssueRow key={`${issue.source}-${issue.symbol}-${issue.accountName ?? "none"}`} issue={issue} />
                        ))
                      )}
                    </div>
                  </>
                ) : (
                  <div className="rounded-xl bg-white/10 p-4 text-slate-200">
                    Security coverage drill-ins appear here when a governance run is selected.
                  </div>
                )}
              </CardContent>
            </Card>
          </section>

          <section className="grid gap-4 xl:grid-cols-[1.05fr_0.95fr]">
            <Card>
              <CardHeader>
                <div className="eyebrow-label">Security Master</div>
                <CardTitle className="flex items-center gap-2">
                  <Search className="h-5 w-5 text-primary" />
                  Security search
                </CardTitle>
                <CardDescription>
                  Search by ticker, ISIN, CUSIP, FIGI, or display name. Search results and governance coverage refs both feed the same security drill-in.
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <input
                  type="text"
                  value={securityQuery}
                  onChange={(e) => handleSecurityQueryChange(e.target.value)}
                  placeholder="Search securities…"
                  className="w-full rounded-lg border border-border/70 bg-secondary/30 px-3 py-2 text-sm text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary/50"
                />
                {securitySearching && <p className="text-sm text-muted-foreground">Searching…</p>}
                {securityResults !== null && !securitySearching && securityResults.length === 0 && (
                  <p className="text-sm text-muted-foreground">No securities found for &ldquo;{securityQuery}&rdquo;.</p>
                )}
                {securityResults && securityResults.length > 0 && (
                  <div className="overflow-x-auto rounded-xl border border-border/70">
                    <table className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm">
                      <thead className="bg-secondary/30">
                        <tr>
                          {["Name", "Asset Class", "Primary ID", "Currency", "Status", ""].map((col) => (
                            <th key={col} className="px-3 py-2 font-semibold uppercase tracking-[0.14em] text-muted-foreground">{col}</th>
                          ))}
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-border/50">
                        {securityResults.map((s) => (
                          <tr key={s.securityId} className={cn("bg-background/20", selectedSecurityId === s.securityId ? "bg-primary/10" : undefined)}>
                            <td className="px-3 py-2 font-semibold text-foreground">{s.displayName}</td>
                            <td className="px-3 py-2 text-muted-foreground">{s.classification.assetClass}</td>
                            <td className="px-3 py-2 font-mono text-muted-foreground">
                              {s.classification.primaryIdentifierKind ? `${s.classification.primaryIdentifierKind}: ${s.classification.primaryIdentifierValue}` : "—"}
                            </td>
                            <td className="px-3 py-2 font-mono text-muted-foreground">{s.economicDefinition.currency}</td>
                            <td className={cn("px-3 py-2 font-mono uppercase", securityStatusTone(s.status))}>{s.status}</td>
                            <td className="px-3 py-2 text-right">
                              <Button size="sm" variant="outline" onClick={() => handleSelectSecurity(s.securityId, securityQuery)}>
                                Open
                              </Button>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <div className="eyebrow-label">Security Drill-In</div>
                <CardTitle>{securityDetail?.displayName ?? "Select a security"}</CardTitle>
                <CardDescription>
                  Detail, identity, history, and economic-definition routes are merged here so operators can review one authoritative record at a time.
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                {securityDrillInLoading && <p className="text-sm text-muted-foreground">Loading security drill-in…</p>}
                {!securityDrillInLoading && securityDrillInError && (
                  <div className="rounded-xl border border-destructive/30 bg-destructive/10 p-4 text-sm text-destructive">
                    {securityDrillInError}
                  </div>
                )}
                {!securityDrillInLoading && !securityDrillInError && !securityDetail && (
                  <div className="rounded-xl border border-dashed border-border/70 bg-secondary/20 p-4 text-sm text-muted-foreground">
                    Use search results or review references to open a Security Master record.
                  </div>
                )}
                {!securityDrillInLoading && !securityDrillInError && securityDetail && (
                  <>
                    <div className="grid gap-3 sm:grid-cols-2">
                      <StatBlock label="Status" value={securityDetail.status} />
                      <StatBlock label="Asset class" value={securityDetail.classification.assetClass} />
                      <StatBlock label="Subtype" value={securityDetail.classification.subType ?? "—"} />
                      <StatBlock label="Currency" value={securityEconomicDefinition?.currency ?? securityDetail.economicDefinition.currency} />
                    </div>

                    <div className="rounded-xl border border-border/70 bg-secondary/20 p-4">
                      <div className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">Identity</div>
                      <div className="mt-3 space-y-2 text-sm">
                        {(securityIdentity?.identifiers ?? []).slice(0, 6).map((identifier) => (
                          <div key={`${identifier.kind}-${identifier.value}`} className="flex items-center justify-between gap-3 rounded-lg border border-border/60 bg-background/50 px-3 py-2">
                            <span className="font-mono">{identifier.kind}: {identifier.value}</span>
                            <span className="text-xs text-muted-foreground">{identifier.provider ?? (identifier.isPrimary ? "Primary" : "Secondary")}</span>
                          </div>
                        ))}
                        {(securityIdentity?.identifiers.length ?? 0) === 0 && (
                          <p className="text-muted-foreground">No identifier detail was returned for this security.</p>
                        )}
                        {securityIdentity?.aliases && securityIdentity.aliases.length > 0 && (
                          <div className="pt-2 text-xs text-muted-foreground">
                            Aliases: {securityIdentity.aliases.map((alias) => `${alias.provider}:${alias.value}`).join(" • ")}
                          </div>
                        )}
                      </div>
                    </div>

                    <div className="rounded-xl border border-border/70 bg-secondary/20 p-4">
                      <div className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">Economic definition</div>
                      <div className="mt-3 grid gap-3 sm:grid-cols-2 text-sm">
                        <StatBlock label="Version" value={String(securityEconomicDefinition?.version ?? securityDetail.economicDefinition.version)} />
                        <StatBlock label="Issuer type" value={securityEconomicDefinition?.issuerType ?? "—"} />
                        <StatBlock label="Asset family" value={securityEconomicDefinition?.assetFamily ?? "—"} />
                        <StatBlock label="Effective window" value={formatEffectiveWindow(securityEconomicDefinition ?? securityDetail.economicDefinition)} />
                      </div>
                    </div>

                    <div className="rounded-xl border border-border/70 bg-secondary/20 p-4">
                      <div className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">History</div>
                      <div className="mt-3 space-y-2 text-sm">
                        {(securityHistory ?? []).slice(0, 4).map((event) => (
                          <div key={`${event.globalSequence}-${event.streamVersion}`} className="rounded-lg border border-border/60 bg-background/50 px-3 py-2">
                            <div className="flex items-center justify-between gap-3">
                              <span className="font-semibold">{event.eventType}</span>
                              <span className="text-xs text-muted-foreground">{new Date(event.eventTimestamp).toLocaleString()}</span>
                            </div>
                            <div className="mt-1 text-xs text-muted-foreground">v{event.streamVersion} · actor {event.actor}</div>
                          </div>
                        ))}
                        {(securityHistory?.length ?? 0) === 0 && (
                          <p className="text-muted-foreground">No workstation history events were returned for this security.</p>
                        )}
                      </div>
                    </div>
                  </>
                )}
              </CardContent>
            </Card>
          </section>

          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <ShieldCheck className="h-5 w-5 text-primary" />
                Identifier conflicts
                {openConflictCount > 0 && (
                  <span className="ml-2 inline-flex items-center rounded-full bg-warning/20 px-2 py-0.5 text-xs font-semibold text-warning">
                    {openConflictCount} open
                  </span>
                )}
              </CardTitle>
              <CardDescription>
                Identifier ambiguities detected when multiple providers map the same identifier to different securities.
              </CardDescription>
            </CardHeader>
            <CardContent>
              {conflictsLoading && <p className="text-sm text-muted-foreground">Loading conflicts…</p>}
              {!conflictsLoading && conflicts !== null && conflicts.length === 0 && (
                <p className="text-sm text-muted-foreground">No identifier conflicts detected.</p>
              )}
              {conflicts && conflicts.length > 0 && (
                <div className="space-y-3">
                  {conflicts.map((conflict) => (
                    <div
                      key={conflict.conflictId}
                      className={cn(
                        "rounded-xl border p-4",
                        conflict.status === "Open" ? "border-warning/40 bg-warning/5" : "border-border/60 bg-secondary/20"
                      )}
                    >
                      <div className="flex flex-wrap items-start justify-between gap-3">
                        <div>
                          <div className="flex items-center gap-2">
                            <span className="font-semibold text-sm">{conflict.fieldPath}</span>
                            <span className={cn("rounded-full px-2 py-0.5 text-xs font-mono uppercase tracking-wide", conflict.status === "Open" ? "bg-warning/20 text-warning" : "bg-secondary text-muted-foreground")}>
                              {conflict.status}
                            </span>
                          </div>
                          <div className="mt-2 grid gap-1 text-xs text-muted-foreground">
                            <span><span className="font-semibold text-foreground">Provider A:</span> {conflict.providerA} → security {conflict.valueA.substring(0, 8)}…</span>
                            <span><span className="font-semibold text-foreground">Provider B:</span> {conflict.providerB} → security {conflict.valueB.substring(0, 8)}…</span>
                            <span className="font-mono text-xs">Detected {new Date(conflict.detectedAt).toLocaleDateString()}</span>
                          </div>
                        </div>
                        {conflict.status === "Open" && (
                          <div className="flex flex-wrap gap-2">
                            <Button
                              size="sm"
                              variant="outline"
                              disabled={conflictResolvingId === conflict.conflictId}
                              onClick={() => void handleResolveConflict(conflict.conflictId, "AcceptA")}
                            >
                              Accept A
                            </Button>
                            <Button
                              size="sm"
                              variant="outline"
                              disabled={conflictResolvingId === conflict.conflictId}
                              onClick={() => void handleResolveConflict(conflict.conflictId, "AcceptB")}
                            >
                              Accept B
                            </Button>
                            <Button
                              size="sm"
                              variant="ghost"
                              disabled={conflictResolvingId === conflict.conflictId}
                              onClick={() => void handleResolveConflict(conflict.conflictId, "Dismiss")}
                            >
                              Dismiss
                            </Button>
                          </div>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>
        </section>
      )}

      {workstream === "reconciliation" && (
        <section>
          <Card>
            <CardHeader>
              <CardTitle>Reconciliation break queue</CardTitle>
              <CardDescription>Review/resolve workflow with assignment and audit metadata.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              {breakQueue.map((item) => (
                <div key={item.breakId} className="rounded-lg border border-border/70 p-3">
                  <div className="flex items-center justify-between gap-3">
                    <div>
                      <div className="font-semibold">{item.strategyName} · {item.category}</div>
                      <div className="text-xs text-muted-foreground">{item.reason}</div>
                    </div>
                    <div className="font-mono text-xs">{item.status}</div>
                  </div>
                  <div className="mt-2 flex flex-wrap gap-2">
                    <Button size="sm" variant="outline" disabled={breakActionId === item.breakId || item.status !== "Open"} onClick={() => void handleAssignBreak(item.breakId)}>Assign</Button>
                    <Button size="sm" variant="outline" disabled={breakActionId === item.breakId || item.status === "Resolved"} onClick={() => void handleResolveBreak(item.breakId, "Resolved")}>Resolve</Button>
                    <Button size="sm" variant="ghost" disabled={breakActionId === item.breakId || item.status === "Dismissed"} onClick={() => void handleResolveBreak(item.breakId, "Dismissed")}>Dismiss</Button>
                  </div>
                </div>
              ))}
            </CardContent>
          </Card>
        </section>
      )}
    </div>
  );
}

function StatBlock({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-border/70 bg-secondary/20 p-3">
      <div className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">{label}</div>
      <div className="mt-2 font-mono text-sm text-foreground">{value}</div>
    </div>
  );
}

function CoverageIssueRow({ issue }: { issue: ReconciliationSecurityCoverageIssue }) {
  return (
    <div className="rounded-lg border border-white/10 bg-black/10 p-3">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-semibold text-slate-50">{issue.displayName ?? issue.symbol}</span>
        <span className={cn("rounded-full px-2 py-0.5 text-[11px] font-mono uppercase", coverageTone(issue.coverageStatus === "Resolved" ? "success" : "warning"))}>
          {issue.coverageStatus}
        </span>
      </div>
      <div className="mt-1 flex flex-wrap gap-3 text-xs text-slate-300">
        <span>{issue.source}</span>
        <span className="font-mono">{issue.symbol}</span>
        {issue.accountName ? <span>{issue.accountName}</span> : null}
        {issue.assetClass ? <span>{issue.assetClass}</span> : null}
        {issue.currency ? <span>{issue.currency}</span> : null}
      </div>
      <p className="mt-2 text-xs text-slate-200">{issue.coverageReason ?? issue.reason}</p>
    </div>
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

function GovernanceValue({ label, value, tone }: { label: string; value: string; tone?: string }) {
  return (
    <div className="flex items-center justify-between gap-4 rounded-lg bg-white/10 px-3 py-2">
      <span className="text-slate-300">{label}</span>
      <span className={cn("font-mono text-slate-50", tone)}>{value}</span>
    </div>
  );
}

function formatCurrency(value: number) {
  const prefix = value >= 0 ? "$" : "-$";
  return `${prefix}${Math.abs(value).toLocaleString(undefined, { maximumFractionDigits: 2 })}`;
}

function coverageTone(tone: "default" | "success" | "warning" | "danger") {
  if (tone === "success") {
    return "bg-success/20 text-success";
  }

  if (tone === "warning") {
    return "bg-warning/20 text-warning";
  }

  if (tone === "danger") {
    return "bg-destructive/20 text-destructive";
  }

  return "bg-secondary text-muted-foreground";
}

function securityStatusTone(status: string) {
  return status === "Active" ? "text-success" : "text-warning";
}

function buildSecurityMasterHref(reference: SecurityCoverageReference) {
  if (reference.securityId) {
    return `/governance/security-master?securityId=${encodeURIComponent(reference.securityId)}&query=${encodeURIComponent(reference.symbol)}`;
  }

  return `/governance/security-master?query=${encodeURIComponent(reference.symbol)}`;
}

function formatEffectiveWindow(economicDefinition: Pick<SecurityEconomicDefinitionSummary, "effectiveFrom" | "effectiveTo">) {
  const start = economicDefinition.effectiveFrom ? new Date(economicDefinition.effectiveFrom).toLocaleDateString() : "Open";
  const end = economicDefinition.effectiveTo ? new Date(economicDefinition.effectiveTo).toLocaleDateString() : "Current";
  return `${start} → ${end}`;
}

function buildReconciliationNarrative(item: GovernanceWorkspaceResponse["reconciliationQueue"][number]) {
  if (item.reconciliationStatus === "Balanced") {
    return "This run is currently balanced. Audit review should focus on evidence completeness and timing freshness rather than open break remediation.";
  }

  if (item.reconciliationStatus === "SecurityCoverageOpen") {
    return "Break counts are secondary here. The main task is resolving Security Master coverage so downstream ledger and reporting workflows are trustworthy.";
  }

  if (item.reconciliationStatus === "Resolved") {
    return "Historical breaks have been worked through, but the run still needs operator review before it can be treated as fully balanced.";
  }

  if (item.reconciliationStatus === "NotStarted") {
    return "No reconciliation pass has been recorded yet. This run should be queued behind currently active governance review work.";
  }

  return "Open reconciliation breaks remain on this run. Prioritize amount mismatches, timing drift, and unresolved references before moving on.";
}
