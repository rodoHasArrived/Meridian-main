/**
 * Statement run detail tabs for Accounting → Reconciliation.
 *
 * The tab strip above the statement-run table was built with counts lifted from
 * the run summary, but every panel body rendered only the tab's own
 * description: Validation described validation issues without listing any, and
 * Breaks & Cases described breaks without showing one. The three per-run routes
 * were mapped the whole time and never called.
 *
 * This component owns the tab bodies. Validation and Breaks & Cases render the
 * server's rows; the remaining tabs keep their existing descriptive banner,
 * because no route on this run supplies their contents.
 */

import { useCallback, useEffect, useMemo, useState } from "react";
import { RefreshCcw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { StatusBanner } from "@/components/ui/status-banner";
import { TabPanel, Tabs } from "@/components/ui/tabs";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { cn } from "@/lib/utils";
import {
  getStatementRunBreaks,
  getStatementRunValidation,
  reconcileStatementRun
} from "@/lib/api/statement-run-detail.api";
import {
  buildStatementRunBreaksViewModel,
  buildStatementRunReconcileAction,
  buildStatementRunValidationViewModel,
  type StatementRunBreakRowViewModel,
  type StatementRunDetailTone,
  type StatementRunValidationRowViewModel
} from "@/screens/accounting-screen.statement-run-detail.view-model";
import type {
  StatementRunBreak,
  StatementRunReconcileAcknowledgement,
  StatementRunValidation
} from "@/types/statement-run-detail.types";
import type { ReconciliationRunDetailTabViewModel } from "@/screens/accounting-screen.view-model";

export interface StatementRunDetailTabsProps {
  panelId: string;
  runId: string | null;
  tabs: ReconciliationRunDetailTabViewModel[];
  /** Called after a reconcile pass so the surrounding run list can refresh. */
  onRunReconciled?: (runId: string) => void;
}

export function StatementRunDetailTabs({ panelId, runId, tabs, onRunReconciled }: StatementRunDetailTabsProps) {
  const [validation, setValidation] = useState<StatementRunValidation | null>(null);
  const [breaks, setBreaks] = useState<StatementRunBreak[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reconcileInFlight, setReconcileInFlight] = useState(false);
  const [forbidden, setForbidden] = useState(false);
  const [acknowledgement, setAcknowledgement] = useState<StatementRunReconcileAcknowledgement | null>(null);

  const refresh = useCallback(async () => {
    if (!runId) {
      setValidation(null);
      setBreaks(null);
      setError(null);
      return;
    }

    setLoading(true);
    setError(null);
    const [validationResult, breaksResult] = await Promise.allSettled([
      getStatementRunValidation(runId),
      getStatementRunBreaks(runId)
    ]);

    // Each half is reported on its own: a run can validate cleanly and still
    // fail to return breaks, and collapsing both into one error would hide that.
    setValidation(validationResult.status === "fulfilled" ? validationResult.value : null);
    setBreaks(breaksResult.status === "fulfilled" ? breaksResult.value : null);

    const failures = [
      validationResult.status === "rejected" ? `Validation: ${errorMessage(validationResult.reason)}` : null,
      breaksResult.status === "rejected" ? `Breaks: ${errorMessage(breaksResult.reason)}` : null
    ].filter((entry): entry is string => entry !== null);
    setError(failures.length > 0 ? failures.join(" ") : null);
    setLoading(false);
  }, [runId]);

  useEffect(() => {
    setAcknowledgement(null);
    setForbidden(false);
    void refresh();
  }, [refresh]);

  const validationView = useMemo(() => buildStatementRunValidationViewModel(validation), [validation]);
  const breaksView = useMemo(() => buildStatementRunBreaksViewModel(breaks), [breaks]);
  const reconcileAction = useMemo(
    () => buildStatementRunReconcileAction({
      runId,
      forbidden,
      inFlight: reconcileInFlight,
      blockedByValidation: validationView.blocked,
      lastAcknowledgement: acknowledgement
    }),
    [acknowledgement, forbidden, reconcileInFlight, runId, validationView.blocked]
  );

  async function runReconcile() {
    if (!runId || !reconcileAction.enabled) {
      return;
    }

    setReconcileInFlight(true);
    setError(null);
    try {
      const result = await reconcileStatementRun(runId);
      setAcknowledgement(result);
      await refresh();
      onRunReconciled?.(runId);
    } catch (reason) {
      if (isForbidden(reason)) {
        setForbidden(true);
      }
      setError(errorMessage(reason));
    } finally {
      setReconcileInFlight(false);
    }
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2 text-xs text-muted-foreground">
          <span>Validation issues</span>
          <Badge variant={validationView.blocked ? "danger" : "outline"}>{validationView.countLabel}</Badge>
          <span>Breaks</span>
          <Badge variant={breaksView.breachedCount > 0 ? "danger" : "outline"}>{breaksView.countLabel}</Badge>
        </div>
        <div className="flex items-center gap-2">
          <Button
            size="sm"
            variant="outline"
            onClick={() => void refresh()}
            disabled={!runId || loading}
            aria-label="Refresh statement run validation and breaks."
          >
            <RefreshCcw className="mr-2 h-4 w-4" />
            Refresh
          </Button>
          <Button
            size="sm"
            onClick={() => void runReconcile()}
            disabled={!reconcileAction.enabled}
            title={reconcileAction.disabledReason ?? undefined}
            aria-label={reconcileAction.ariaLabel}
          >
            {reconcileAction.label}
          </Button>
        </div>
      </div>

      {reconcileAction.disabledReason && runId ? (
        <p className="text-xs text-muted-foreground">{reconcileAction.disabledReason}</p>
      ) : null}
      {reconcileAction.lastOutcome ? (
        <StatusBanner role="status" tone="info" title="Matching complete" detail={reconcileAction.lastOutcome} />
      ) : null}
      {error ? <StatusBanner role="alert" tone="danger" title="Statement run detail unavailable" detail={error} /> : null}

      <Tabs
        id={panelId}
        aria-label="Statement run detail tabs"
        tabs={tabs.map((tab) => ({
          ariaLabel: tab.ariaLabel,
          count: resolveTabCount(tab, validationView, breaksView),
          disabled: tab.disabled,
          id: tab.id,
          label: tab.label
        }))}
      >
        {tabs.map((tab) => (
          <TabPanel key={tab.id}>
            {renderTabBody(tab, validationView, breaksView, loading)}
          </TabPanel>
        ))}
      </Tabs>
    </div>
  );
}

function renderTabBody(
  tab: ReconciliationRunDetailTabViewModel,
  validationView: ReturnType<typeof buildStatementRunValidationViewModel>,
  breaksView: ReturnType<typeof buildStatementRunBreaksViewModel>,
  loading: boolean
) {
  if (tab.disabled) {
    return (
      <StatusBanner role="status" tone="warning" title={tab.label} detail={tab.disabledReason ?? tab.description} />
    );
  }

  if (tab.id === "validation") {
    return (
      <div className="space-y-3">
        {validationView.blockedNotice ? (
          <StatusBanner role="alert" tone="danger" title="Run blocked" detail={validationView.blockedNotice} />
        ) : null}
        <DenseDataTable
          columns={validationColumns}
          rows={validationView.rows}
          getRowId={(row) => row.issueId}
          getRowAriaLabel={(row) => row.ariaLabel}
          emptyText={loading ? "Loading validation issues…" : validationView.emptyState}
          ariaLabel="Statement run validation issues"
          caption={tab.description}
        />
      </div>
    );
  }

  if (tab.id === "breaks-cases") {
    return (
      <DenseDataTable
        columns={breakColumns}
        rows={breaksView.rows}
        getRowId={(row) => row.breakId}
        getRowAriaLabel={(row) => row.ariaLabel}
        emptyText={loading ? "Loading breaks…" : breaksView.emptyState}
        ariaLabel="Statement run breaks"
        caption={tab.description}
      />
    );
  }

  return <StatusBanner role="status" tone="info" title={tab.label} detail={tab.description} />;
}

/**
 * Once a route has answered, its tab shows the served count; until then the tab
 * keeps the count the run summary supplied. Replacing a real summary count with
 * a placeholder while the fetch is in flight would lose information the screen
 * already had.
 */
function resolveTabCount(
  tab: ReconciliationRunDetailTabViewModel,
  validationView: ReturnType<typeof buildStatementRunValidationViewModel>,
  breaksView: ReturnType<typeof buildStatementRunBreaksViewModel>
): string | null {
  if (tab.id === "validation") {
    return validationView.loaded ? validationView.countLabel : tab.badgeLabel;
  }

  if (tab.id === "breaks-cases") {
    return breaksView.loaded ? breaksView.countLabel : tab.badgeLabel;
  }

  return tab.badgeLabel;
}

const validationColumns: DenseDataTableColumn<StatementRunValidationRowViewModel>[] = [
  {
    id: "severity",
    label: "Severity",
    render: (row) => <span className={cn("font-mono", toneClass(row.severityTone))}>{row.severityLabel}</span>
  },
  {
    id: "code",
    label: "Code",
    render: (row) => <span className="font-mono text-foreground">{row.code}</span>
  },
  {
    id: "message",
    label: "Issue",
    render: (row) => (
      <div className="space-y-1">
        <div className="text-foreground">{row.message}</div>
        {row.recommendedAction ? (
          <div className="text-xs text-muted-foreground">Recommended: {row.recommendedAction}</div>
        ) : null}
      </div>
    )
  },
  {
    id: "source",
    label: "Source",
    render: (row) => (
      <div className="space-y-1">
        <div className="font-mono text-muted-foreground">{row.sourceLabel}</div>
        {row.rawValue ? <div className="font-mono text-xs text-muted-foreground">{row.rawValue}</div> : null}
      </div>
    )
  }
];

const breakColumns: DenseDataTableColumn<StatementRunBreakRowViewModel>[] = [
  {
    id: "type",
    label: "Break",
    render: (row) => (
      <div className="space-y-1">
        <div className="text-foreground">{row.typeLabel}</div>
        <div className="text-xs text-muted-foreground">{row.category}</div>
      </div>
    )
  },
  {
    id: "reference",
    label: "Source reference",
    render: (row) => <span className="font-mono text-muted-foreground">{row.sourceReference}</span>
  },
  {
    id: "delta",
    label: "Delta",
    align: "right",
    render: (row) => (
      <span className={cn("font-mono tabular-nums", toneClass(row.toleranceTone))}>{row.deltaLabel}</span>
    )
  },
  {
    id: "tolerance",
    label: "Tolerance",
    align: "right",
    render: (row) => (
      <span className="font-mono tabular-nums text-muted-foreground" title={row.toleranceNote}>
        {row.toleranceLabel}
      </span>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => <span className="font-mono text-foreground">{row.status}</span>
  },
  {
    id: "created",
    label: "Raised",
    render: (row) => <span className="font-mono text-muted-foreground">{row.createdAtUtc}</span>
  }
];

function toneClass(tone: StatementRunDetailTone): string {
  if (tone === "danger") {
    return "text-destructive";
  }

  if (tone === "warning") {
    return "text-warning";
  }

  return tone === "success" ? "text-success" : "text-foreground";
}

function isForbidden(reason: unknown): boolean {
  return typeof reason === "object"
    && reason !== null
    && "status" in reason
    && (reason as { status?: unknown }).status === 403;
}

function errorMessage(reason: unknown): string {
  return reason instanceof Error ? reason.message : "Request failed.";
}
