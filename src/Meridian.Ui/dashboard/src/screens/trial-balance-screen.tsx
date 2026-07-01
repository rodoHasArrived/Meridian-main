import { useEffect, useMemo, useState } from "react";
import { Search } from "lucide-react";
import { Link, useSearchParams } from "react-router-dom";
import { AccountTree, type AccountNode } from "@/components/accounting/AccountTree";
import { AccountingTrialBalanceSelectedDetailPanel, trialBalanceColumns } from "@/components/accounting/TrialBalanceRowDetail";
import { DenseDataTable } from "@/components/meridian/ui-kit-primitives";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { cn } from "@/lib/utils";
import { WORKSTATION_ROUTE_CATALOG, workstationRouteWithQuery } from "@/lib/workspace";
import { getRunLedgerJournal } from "@/lib/api";
import {
  buildAccountingLedgerJournalEvidenceViewState,
  useAccountingReconciliationViewModel
} from "@/screens/accounting-screen.view-model";
import { buildTrialBalanceAccountTreeNodes, trialBalanceAccountTreeCode } from "@/screens/trial-balance-screen.view-model";
import type { AccountingLedgerJournalEvidenceViewState } from "@/screens/accounting-screen.view-model";
import type { AccountingWorkspaceResponse, LedgerJournalLine } from "@/types";

interface TrialBalanceScreenProps {
  data: AccountingWorkspaceResponse | null;
}

type TrialBalanceViewMode = "table" | "hierarchy";

export function TrialBalanceScreen({ data }: TrialBalanceScreenProps) {
  const [searchParams, setSearchParams] = useSearchParams();
  const [viewMode, setViewMode] = useState<TrialBalanceViewMode>("table");
  const [journalLines, setJournalLines] = useState<LedgerJournalLine[]>([]);
  const [journalLoading, setJournalLoading] = useState(false);
  const reconciliation = useAccountingReconciliationViewModel(data, "ledger");
  const { reconciliationQueue, selectedReconciliation, selectRun } = reconciliation;

  useEffect(() => {
    const requestedRunId = searchParams.get("runId");
    if (requestedRunId && requestedRunId !== selectedReconciliation?.runId) {
      selectRun(requestedRunId);
    }
  }, [searchParams, selectRun, selectedReconciliation?.runId]);

  useEffect(() => {
    if (!selectedReconciliation) {
      return;
    }

    if (searchParams.get("runId") === selectedReconciliation.runId) {
      return;
    }

    const nextParams = new URLSearchParams(searchParams);
    nextParams.set("runId", selectedReconciliation.runId);
    setSearchParams(nextParams, { replace: true });
  }, [searchParams, selectedReconciliation, setSearchParams]);

  useEffect(() => {
    const runId = selectedReconciliation?.runId;
    if (!runId) {
      setJournalLines([]);
      return;
    }

    let cancelled = false;
    setJournalLoading(true);

    getRunLedgerJournal(runId)
      .then((lines) => {
        if (!cancelled) {
          setJournalLines(lines);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setJournalLines([]);
        }
      })
      .finally(() => {
        if (!cancelled) {
          setJournalLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [selectedReconciliation?.runId]);

  const journalEvidence: AccountingLedgerJournalEvidenceViewState = useMemo(
    () => buildAccountingLedgerJournalEvidenceViewState({
      runId: selectedReconciliation?.runId ?? null,
      rows: journalLines
    }),
    [journalLines, selectedReconciliation?.runId]
  );

  const treeNodes: AccountNode[] = useMemo(
    () => buildTrialBalanceAccountTreeNodes(reconciliation.trialBalanceView.rows),
    [reconciliation.trialBalanceView.rows]
  );

  const selectedTreeCode = useMemo(() => {
    const selectedRow = reconciliation.trialBalanceView.rows.find(
      (row) => row.rowId === reconciliation.trialBalanceView.selectedRowId
    );
    return selectedRow ? trialBalanceAccountTreeCode(selectedRow) : undefined;
  }, [reconciliation.trialBalanceView.rows, reconciliation.trialBalanceView.selectedRowId]);

  const relatedSecurities = useMemo(() => {
    const seen = new Map<string, string>();
    for (const row of reconciliation.trialBalanceView.rows) {
      const securityId = row.security?.securityId;
      if (securityId && !seen.has(securityId)) {
        seen.set(securityId, row.security?.displayName.trim() || row.symbol?.trim() || securityId);
      }
    }
    return Array.from(seen.entries()).map(([securityId, label]) => ({ securityId, label }));
  }, [reconciliation.trialBalanceView.rows]);

  if (!data) {
    return (
      <Card
        className="panel-surface"
        role="status"
        aria-busy="true"
        aria-live="polite"
        aria-labelledby="trial-balance-loading-title"
      >
        <CardHeader>
          <CardTitle id="trial-balance-loading-title">Loading Trial Balance</CardTitle>
          <CardDescription>Waiting for accounting workspace data.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  if (reconciliationQueue.length === 0) {
    return (
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Trial Balance</CardTitle>
          <CardDescription>No reconciliation runs are available for this accounting scope.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  return (
    <div className="space-y-4">
      <Card className="panel-surface">
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <CardTitle>Trial Balance</CardTitle>
            <CardDescription>Pick a ledger run to review basis-aware account balances.</CardDescription>
          </div>
          <FormRow label="Run" labelFor="trial-balance-run-select" className="w-full max-w-xs sm:w-64">
            <Select
              id="trial-balance-run-select"
              value={selectedReconciliation?.runId ?? ""}
              onChange={(event) => selectRun(event.target.value)}
            >
              {reconciliationQueue.map((item) => (
                <option key={item.runId} value={item.runId}>
                  {item.strategyName} ({item.runId})
                </option>
              ))}
            </Select>
          </FormRow>
        </CardHeader>
      </Card>

      <Card aria-labelledby="trial-balance-title" aria-describedby="trial-balance-description" className="panel-surface">
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <CardTitle id="trial-balance-title">{reconciliation.trialBalanceView.title}</CardTitle>
            <CardDescription id="trial-balance-description">{reconciliation.trialBalanceView.description}</CardDescription>
          </div>
          <div className="flex gap-2" role="group" aria-label="Trial balance view mode">
            <Button
              type="button"
              size="sm"
              variant={viewMode === "table" ? "default" : "outline"}
              aria-pressed={viewMode === "table"}
              onClick={() => setViewMode("table")}
            >
              Table
            </Button>
            <Button
              type="button"
              size="sm"
              variant={viewMode === "hierarchy" ? "default" : "outline"}
              aria-pressed={viewMode === "hierarchy"}
              onClick={() => setViewMode("hierarchy")}
            >
              Hierarchy
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <span className="sr-only" aria-live="polite">{reconciliation.trialBalanceView.statusAnnouncement}</span>
          <div className="mb-4 flex flex-wrap gap-2" role="group" aria-label="Accounting basis">
            {reconciliation.trialBalanceView.basisOptions.map((option) => (
              <Button
                key={option.id}
                type="button"
                size="sm"
                variant={option.isSelected ? "default" : "outline"}
                aria-pressed={option.isSelected}
                aria-label={`${option.label} basis, ${option.rowCountLabel}. ${option.description}`}
                onClick={() => reconciliation.selectAccountingBasis(option.id)}
              >
                <span>{option.label}</span>
                <span className="ml-2 font-mono text-[10px] opacity-75">{option.rowCount}</span>
              </Button>
            ))}
          </div>
          <div className="mb-4 rounded-md border border-border/70 bg-secondary/15 p-3">
            <FormRow label={reconciliation.trialBalanceView.accountFilterLabel} labelFor="trial-balance-account-filter">
              <div className="flex flex-col gap-2 lg:flex-row lg:items-center">
                <div className="relative min-w-0 flex-1">
                  <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
                  <Input
                    id="trial-balance-account-filter"
                    type="search"
                    value={reconciliation.trialBalanceView.accountFilterValue}
                    onChange={(event) => reconciliation.updateLedgerAccountFilter(event.target.value)}
                    placeholder={reconciliation.trialBalanceView.accountFilterPlaceholder}
                    className="pl-9"
                  />
                </div>
                <span className="font-mono text-xs text-muted-foreground">{reconciliation.trialBalanceView.filteredRowCountLabel}</span>
              </div>
            </FormRow>
          </div>
          {reconciliation.trialBalanceView.hasRows ? (
            viewMode === "table" ? (
              <div className="grid gap-3 xl:grid-cols-[minmax(0,1.25fr)_minmax(260px,0.75fr)]">
                <DenseDataTable
                  columns={trialBalanceColumns}
                  rows={reconciliation.trialBalanceView.rows}
                  getRowId={(line) => line.rowId}
                  getRowAriaLabel={(line) => line.ariaLabel}
                  getRowSelectAriaLabel={(line) => line.selectAriaLabel}
                  getRowAriaControls={(line) => line.detailPanelId}
                  getRowAriaExpanded={(line) => line.isExpanded}
                  selectedRowId={reconciliation.trialBalanceView.selectedRowId}
                  onRowSelect={(line) => reconciliation.selectTrialBalanceRow(line.rowId)}
                  emptyText={reconciliation.trialBalanceView.emptyDetail}
                  ariaLabel={reconciliation.trialBalanceView.tableLabel}
                />
                {reconciliation.trialBalanceView.selectedDetail ? (
                  <AccountingTrialBalanceSelectedDetailPanel
                    panelId={reconciliation.trialBalanceView.detailPanelId}
                    detail={reconciliation.trialBalanceView.selectedDetail}
                  />
                ) : (
                  <aside
                    id={reconciliation.trialBalanceView.detailPanelId}
                    role="region"
                    aria-label={reconciliation.trialBalanceView.detailEmptyAriaLabel}
                    className="row-detail-panel h-fit min-w-0"
                  >
                    <div className="eyebrow-label">Trial-balance detail</div>
                    <h3 className="mt-1 text-sm font-semibold text-foreground">{reconciliation.trialBalanceView.detailEmptyTitle}</h3>
                    <p className="mt-2 text-sm leading-6 text-muted-foreground">{reconciliation.trialBalanceView.detailEmptyText}</p>
                  </aside>
                )}
              </div>
            ) : (
              <AccountTree
                nodes={treeNodes}
                selectedCode={selectedTreeCode}
                onSelect={(node) => {
                  const row = reconciliation.trialBalanceView.rows.find(
                    (candidate) => trialBalanceAccountTreeCode(candidate) === node.code
                  );
                  if (row) {
                    reconciliation.selectTrialBalanceRow(row.rowId);
                  }
                }}
              />
            )
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
        </CardContent>
      </Card>

      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>{journalEvidence.title}</CardTitle>
          <CardDescription>{journalEvidence.description}</CardDescription>
        </CardHeader>
        <CardContent>
          {journalLoading ? (
            <p className="text-sm text-muted-foreground">Loading journal entries for this run.</p>
          ) : journalEvidence.hasRows ? (
            <ul className="space-y-2" aria-label={journalEvidence.title}>
              {journalEvidence.rows.map((row) => (
                <li key={row.journalEntryId} className="rounded-md border border-border/70 bg-secondary/15 px-3 py-2">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <Link
                      className="min-w-0 truncate text-sm font-semibold text-primary underline-offset-2 hover:underline"
                      to={workstationRouteWithQuery("accountingJournalEntryDetail", {
                        journalEntryId: row.journalEntryId,
                        runId: selectedReconciliation?.runId ?? null
                      })}
                    >
                      {row.description || row.journalEntryId}
                    </Link>
                    <Badge variant="outline">{row.amountLabel}</Badge>
                  </div>
                  <p className="mt-1 text-xs text-muted-foreground">{row.timestampLabel} - {row.lineCountLabel}</p>
                </li>
              ))}
            </ul>
          ) : (
            <p className="text-sm text-muted-foreground">{journalEvidence.emptyText}</p>
          )}
        </CardContent>
      </Card>

      {relatedSecurities.length > 0 ? (
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle>Securities in this run</CardTitle>
            <CardDescription>Jump to Asset Detail for a security referenced by this trial balance.</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="flex flex-wrap gap-2">
              {relatedSecurities.map((security) => (
                <Button asChild key={security.securityId} size="sm" variant="outline">
                  <Link
                    to={workstationRouteWithQuery("accountingAssetDetail", { securityId: security.securityId })}
                  >
                    {security.label}
                  </Link>
                </Button>
              ))}
            </div>
          </CardContent>
        </Card>
      ) : null}

      <p className="text-xs text-muted-foreground">
        Looking for the full ledger explorer? <Link className="text-primary underline-offset-2 hover:underline" to={WORKSTATION_ROUTE_CATALOG.accountingLedger}>Open Ledger Explorer</Link>.
      </p>
    </div>
  );
}
