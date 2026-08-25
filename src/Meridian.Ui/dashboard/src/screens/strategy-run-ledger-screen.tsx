import { useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { FinancialRecordExplorerShell } from "@/components/meridian/financial-record-explorer";
import { AccountingTrialBalanceSelectedDetailPanel, trialBalanceColumns } from "@/components/accounting/TrialBalanceRowDetail";
import { TrialBalanceTable } from "@/components/accounting";
import { DenseDataTable } from "@/components/meridian/ui-kit-primitives";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { getFinancialRecordExplorer, getRunLedgerJournal, getRunTrialBalance } from "@/lib/api";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import { DENSE_VIRTUALIZATION_THRESHOLD } from "@/lib/dense-table-virtualization";
import { cn } from "@/lib/utils";
import {
  buildAccountingLedgerJournalEvidenceViewState,
  buildAccountingTrialBalanceViewState
} from "@/screens/accounting-screen.view-model";
import type { FinancialRecordExplorerDto, LedgerJournalLine, LedgerTrialBalanceLine } from "@/types";

/**
 * The strategy run's ledger — a simulation artifact, not the fund's book.
 *
 * This lives under Strategy on purpose. It used to render inside the Accounting workspace, where
 * a run's simulated trial balance sat under the name an operator reads as the book of record
 * (adversarial-program-review-2026-08-25 §1). The fund's posted journal now owns the Accounting
 * ledger surface; run evidence belongs next to the runs that produced it.
 */
export function StrategyRunLedgerScreen() {
  const [searchParams] = useSearchParams();
  const runId = searchParams.get("runId")?.trim() || null;

  const [explorer, setExplorer] = useState<FinancialRecordExplorerDto | null>(null);
  const [explorerError, setExplorerError] = useState<ApiErrorDisplay | null>(null);
  const [trialBalanceRows, setTrialBalanceRows] = useState<LedgerTrialBalanceLine[]>([]);
  const [trialBalanceLoading, setTrialBalanceLoading] = useState(false);
  const [trialBalanceError, setTrialBalanceError] = useState<ApiErrorDisplay | null>(null);
  const [selectedRowId, setSelectedRowId] = useState<string | null>(null);
  const [journalLines, setJournalLines] = useState<LedgerJournalLine[]>([]);
  const [journalErrorText, setJournalErrorText] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setExplorerError(null);

    getFinancialRecordExplorer("ledger")
      .then((dto) => {
        if (!cancelled) {
          setExplorer(dto);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setExplorer(null);
          setExplorerError(describeApiError(err, "The run ledger explorer failed to load."));
        }
      });

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!runId) {
      setTrialBalanceRows([]);
      setTrialBalanceError(null);
      setTrialBalanceLoading(false);
      setJournalLines([]);
      setJournalErrorText(null);
      return;
    }

    let cancelled = false;
    setTrialBalanceLoading(true);
    setTrialBalanceError(null);
    setJournalErrorText(null);

    getRunTrialBalance(runId)
      .then((rows) => {
        if (!cancelled) {
          setTrialBalanceRows(rows);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setTrialBalanceRows([]);
          setTrialBalanceError(describeApiError(err, "The run trial balance failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setTrialBalanceLoading(false);
        }
      });

    getRunLedgerJournal(runId)
      .then((lines) => {
        if (!cancelled) {
          setJournalLines(lines);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setJournalLines([]);
          setJournalErrorText("Journal lineage could not be loaded for this run.");
        }
      });

    return () => {
      cancelled = true;
    };
  }, [runId]);

  const trialBalanceView = useMemo(
    () => buildAccountingTrialBalanceViewState({
      runId,
      rows: trialBalanceRows,
      selectedRowId,
      loading: trialBalanceLoading,
      error: trialBalanceError
    }),
    [runId, selectedRowId, trialBalanceError, trialBalanceLoading, trialBalanceRows]
  );

  const journalEvidence = useMemo(
    () => buildAccountingLedgerJournalEvidenceViewState({ runId, rows: journalLines }),
    [journalLines, runId]
  );

  return (
    <FinancialRecordExplorerShell
      explorerLabel="Financial Record Explorer"
      title="Strategy Run Ledger Explorer"
      titleId="strategy-run-ledger-explorer-title"
      description="Simulation artifact: this explorer reads a strategy run's ledger, not the fund's posted book. The posted journal lives in Accounting."
      scopeItems={[
        { id: "workspace", label: "Workspace", value: "Strategy" },
        { id: "source", label: "Ledger source", value: "Strategy run (simulation) — not the posted journal" },
        { id: "run-id", label: "Run ID", value: runId ?? "Latest run" }
      ]}
      savedViews={[]}
      summaryItems={[
        { id: "rows", label: "Rows", value: trialBalanceView.filteredRowCountLabel },
        { id: "journal", label: "Journal entries", value: String(journalEvidence.rows.length) }
      ]}
      appliedFilters={[]}
      explorer={explorer}
    >
      <div className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
        <Card aria-labelledby="run-trial-balance-title" className="panel-surface">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-2">
              <CardTitle id="run-trial-balance-title">{trialBalanceView.title}</CardTitle>
              <Badge variant="outline">Source: strategy run</Badge>
            </div>
            <CardDescription>{trialBalanceView.description}</CardDescription>
          </CardHeader>
          <CardContent>
            <span className="sr-only" aria-live="polite">{trialBalanceView.statusAnnouncement}</span>
            {trialBalanceView.hasRows ? (
              <div className="grid gap-3 xl:grid-cols-[minmax(0,1.25fr)_minmax(260px,0.75fr)]">
                {trialBalanceView.rows.length > DENSE_VIRTUALIZATION_THRESHOLD ? (
                  <DenseDataTable
                    columns={trialBalanceColumns}
                    rows={trialBalanceView.rows}
                    getRowId={(line) => line.rowId}
                    getRowAriaLabel={(line) => line.ariaLabel}
                    getRowSelectAriaLabel={(line) => line.selectAriaLabel}
                    getRowAriaControls={(line) => line.detailPanelId}
                    getRowAriaExpanded={(line) => line.isExpanded}
                    selectedRowId={trialBalanceView.selectedRowId}
                    onRowSelect={(line) => setSelectedRowId(line.rowId)}
                    emptyText={trialBalanceView.emptyDetail}
                    ariaLabel={trialBalanceView.tableLabel}
                  />
                ) : (
                  <TrialBalanceTable
                    rows={trialBalanceView.rows}
                    selectedRowId={trialBalanceView.selectedRowId}
                    caption={trialBalanceView.tableLabel}
                    onRowSelect={(line) => setSelectedRowId(line.rowId)}
                  />
                )}
                {trialBalanceView.selectedDetail ? (
                  <AccountingTrialBalanceSelectedDetailPanel
                    panelId={trialBalanceView.detailPanelId}
                    detail={trialBalanceView.selectedDetail}
                  />
                ) : null}
              </div>
            ) : (
              <div
                role={trialBalanceView.state === "error" ? "alert" : "status"}
                className={cn(
                  "rounded-lg border px-4 py-4",
                  trialBalanceView.state === "error"
                    ? "border-danger/35 bg-danger/10 text-danger"
                    : "border-border/70 bg-secondary/25 text-muted-foreground"
                )}
              >
                <div className="text-sm font-semibold text-foreground">{trialBalanceView.emptyTitle}</div>
                <p className="mt-2 text-sm leading-6">
                  {trialBalanceView.errorText
                    ?? trialBalanceView.loadingText
                    ?? (runId ? trialBalanceView.emptyDetail : "Select a run to load its simulated trial balance.")}
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
            {journalErrorText ? (
              <p role="alert" className="rounded-md border border-danger/35 bg-danger/10 px-3 py-2 text-sm text-danger">
                {journalErrorText}
              </p>
            ) : journalEvidence.hasRows ? (
              <ul className="space-y-2" aria-label="Run journal entries">
                {journalEvidence.rows.map((row) => (
                  <li key={row.rowId} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <span className="min-w-0 truncate text-sm font-semibold text-foreground">
                        {row.description || row.journalEntryId}
                      </span>
                      <Badge variant="outline">{row.amountLabel}</Badge>
                    </div>
                    <p className="mt-1 text-xs text-muted-foreground">{row.timestampLabel} - {row.lineCountLabel}</p>
                  </li>
                ))}
              </ul>
            ) : (
              <p role="status" className="rounded-md border border-border/70 bg-secondary/25 px-3 py-2 text-sm text-muted-foreground">
                {journalEvidence.emptyText}
              </p>
            )}
            {explorerError ? (
              <p role="alert" className="mt-3 rounded-md border border-danger/35 bg-danger/10 px-3 py-2 text-sm text-danger">
                {explorerError.summary}
              </p>
            ) : null}
          </CardContent>
        </Card>
      </div>
    </FinancialRecordExplorerShell>
  );
}
