/**
 * Reconciliation queue readiness for Accounting → Reconciliation.
 *
 * The break queue answers "what is broken"; these three routes answer "can this
 * account be signed off, and what is holding it up" — and none of them had a
 * caller. `queue-status` carries the server's own next-best-action and blocker
 * per account, `cases` carries open casework with its SLA state, and
 * `break-queue/taxonomy` is the catalog those casework codes come from.
 */

import { useCallback, useEffect, useMemo, useState } from "react";
import { ClipboardCheck, RefreshCcw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { cn } from "@/lib/utils";
import {
  getReconciliationOpenCases,
  getReconciliationQueueStatus,
  getReconciliationTaxonomy
} from "@/lib/api/reconciliation-readiness.api";
import {
  buildOpenCaseRow,
  buildQueueAccountRow,
  buildQueueReadinessSummary,
  buildTaxonomyViewModel,
  type OpenCaseRowViewModel,
  type QueueAccountRowViewModel,
  type ReadinessTone
} from "@/screens/accounting-screen.reconciliation-readiness.view-model";
import type {
  ReconciliationCaseSummary,
  ReconciliationQueueAccountStatus,
  ReconciliationTaxonomySnapshot
} from "@/types/reconciliation-readiness.types";

export function ReconciliationReadinessPanel() {
  const [statuses, setStatuses] = useState<ReconciliationQueueAccountStatus[] | null>(null);
  const [cases, setCases] = useState<ReconciliationCaseSummary[] | null>(null);
  const [taxonomy, setTaxonomy] = useState<ReconciliationTaxonomySnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [errors, setErrors] = useState<string[]>([]);

  const refresh = useCallback(async () => {
    setLoading(true);
    const [statusResult, caseResult, taxonomyResult] = await Promise.allSettled([
      getReconciliationQueueStatus(),
      getReconciliationOpenCases(),
      getReconciliationTaxonomy()
    ]);

    setStatuses(statusResult.status === "fulfilled" ? statusResult.value : null);
    setCases(caseResult.status === "fulfilled" ? caseResult.value : null);
    setTaxonomy(taxonomyResult.status === "fulfilled" ? taxonomyResult.value : null);

    // Listed individually inside one banner: the taxonomy failing degrades code
    // labels, while the queue failing removes the readiness verdict entirely, and
    // one merged message would not tell the operator which of those happened.
    setErrors([
      statusResult.status === "rejected" ? `Queue status: ${errorMessage(statusResult.reason)}` : null,
      caseResult.status === "rejected" ? `Open cases: ${errorMessage(caseResult.reason)}` : null,
      taxonomyResult.status === "rejected" ? `Taxonomy: ${errorMessage(taxonomyResult.reason)}` : null
    ].filter((entry): entry is string => entry !== null));
    setLoading(false);
  }, []);

  useEffect(() => { void refresh(); }, [refresh]);

  const accountRows = useMemo(() => (statuses ?? []).map(buildQueueAccountRow), [statuses]);
  const summary = useMemo(() => buildQueueReadinessSummary(statuses), [statuses]);
  const caseRows = useMemo(
    () => (cases ?? []).map((entry) => buildOpenCaseRow(entry, taxonomy)),
    [cases, taxonomy]
  );
  const taxonomyView = useMemo(() => buildTaxonomyViewModel(taxonomy, cases), [cases, taxonomy]);

  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <div className="eyebrow-label">Reconciliation</div>
            <CardTitle className="flex items-center gap-2">
              <ClipboardCheck className="h-5 w-5 text-primary" />
              Queue readiness
            </CardTitle>
            <CardDescription>
              Per-account sign-off readiness and open casework, with the server's own next action and blocker.
            </CardDescription>
          </div>
          <Button size="sm" variant="outline" onClick={() => void refresh()} disabled={loading}>
            <RefreshCcw className="mr-2 h-4 w-4" />
            Refresh
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <dl className="flex flex-wrap items-baseline gap-x-6 gap-y-1.5 text-xs">
          <SummaryStat label="Accounts" value={summary.accountsLabel} />
          <SummaryStat label="Sign-off ready" value={summary.readyLabel} />
          <SummaryStat label="Blocked" value={summary.blockedLabel} />
          <SummaryStat label="Unresolved breaks" value={summary.unresolvedLabel} />
          <SummaryStat label="Taxonomy" value={taxonomyView.versionLabel} />
        </dl>

        {errors.length > 0 ? (
          <StatusBanner
            role="alert"
            tone={statuses === null ? "danger" : "warning"}
            title={statuses === null
              ? "Queue readiness unavailable"
              : "Queue readiness loaded with gaps"}
            detail={(
              <ul className="mt-2 list-disc pl-5">
                {errors.map((error) => <li key={error}>{error}</li>)}
              </ul>
            )}
          />
        ) : null}
        {summary.blockedNotice ? (
          <StatusBanner role="status" tone="warning" title="Sign-off blocked" detail={summary.blockedNotice} />
        ) : null}
        {taxonomyView.unknownNotice ? (
          <StatusBanner role="status" tone="warning" title="Casework codes outside the taxonomy" detail={taxonomyView.unknownNotice} />
        ) : null}

        <DenseDataTable
          columns={accountColumns}
          rows={accountRows}
          getRowId={(row) => row.accountId}
          getRowAriaLabel={(row) => row.ariaLabel}
          emptyText={loading ? "Loading queue status…" : "No accounts reported a reconciliation queue state."}
          ariaLabel="Reconciliation queue status by account"
          caption="Sign-off readiness, unresolved break count, and the next action reported for each account."
        />

        <div className="space-y-2">
          <div className="flex items-center gap-2 text-xs text-muted-foreground">
            <span>Open casework</span>
            <Badge variant="outline">{caseRows.length}</Badge>
            {taxonomyView.loaded ? (
              <span>
                {taxonomyView.rootCauseCount} root causes, {taxonomyView.resolutionCount} resolution codes
              </span>
            ) : (
              <span>Codes shown as recorded; the taxonomy did not load.</span>
            )}
          </div>
          <DenseDataTable
            columns={caseColumns}
            rows={caseRows}
            getRowId={(row) => row.caseId}
            getRowAriaLabel={(row) => row.ariaLabel}
            emptyText={loading ? "Loading open cases…" : "No open reconciliation cases."}
            ariaLabel="Open reconciliation cases"
            caption="Open casework with SLA state, ownership, and the root-cause and resolution codes recorded so far."
          />
        </div>
      </CardContent>
    </Card>
  );
}

function SummaryStat({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex min-w-0 items-baseline gap-2">
      <dt className="whitespace-nowrap uppercase tracking-[0.08em] text-muted-foreground">{label}</dt>
      <dd className="whitespace-nowrap font-mono text-sm font-semibold text-foreground">{value}</dd>
    </div>
  );
}

const accountColumns: DenseDataTableColumn<QueueAccountRowViewModel>[] = [
  {
    id: "account",
    label: "Account",
    render: (row) => (
      <div className="space-y-1">
        <div className="font-mono text-foreground">{row.accountCode}</div>
        <div className="text-xs text-muted-foreground">{row.queueState}</div>
      </div>
    )
  },
  {
    id: "readiness",
    label: "Sign-off",
    render: (row) => <span className={cn("font-mono", toneClass(row.readinessTone))}>{row.readinessLabel}</span>
  },
  {
    id: "unresolved",
    label: "Unresolved",
    align: "right",
    render: (row) => (
      <span className={cn("font-mono tabular-nums", row.unresolvedBreakCount > 0 ? "text-warning" : "text-foreground")}>
        {row.unresolvedBreakCount}
      </span>
    )
  },
  {
    id: "next",
    label: "Next action",
    render: (row) => (
      <div className="space-y-1">
        <div className="text-foreground">{row.nextBestAction}</div>
        {row.blockerReason ? <div className="text-xs text-danger">Blocked: {row.blockerReason}</div> : null}
      </div>
    )
  },
  {
    id: "evidence",
    label: "Evidence",
    render: (row) => <span className="text-muted-foreground">{row.evidenceCountLabel}</span>
  }
];

const caseColumns: DenseDataTableColumn<OpenCaseRowViewModel>[] = [
  {
    id: "case",
    label: "Case",
    render: (row) => (
      <div className="space-y-1">
        <div className="font-mono text-foreground">{row.caseId}</div>
        <div className="text-xs text-muted-foreground">{row.reason}</div>
      </div>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => (
      <div className="space-y-1">
        <div className="font-mono text-foreground">{row.status}</div>
        <div className="text-xs text-muted-foreground">{row.priority}</div>
      </div>
    )
  },
  {
    id: "sla",
    label: "SLA",
    render: (row) => (
      <div className="space-y-1">
        <div className={cn("font-mono", toneClass(row.slaTone))}>{row.slaLabel}</div>
        <div className="text-xs text-muted-foreground">{row.ageLabel}</div>
      </div>
    )
  },
  {
    id: "codes",
    label: "Root cause / resolution",
    render: (row) => (
      <div className="space-y-1">
        <div className="text-foreground">{row.rootCauseLabel}</div>
        <div className="text-xs text-muted-foreground">{row.resolutionLabel}</div>
      </div>
    )
  },
  {
    id: "owner",
    label: "Owner",
    render: (row) => (
      <div className="space-y-1">
        <div className="text-foreground">{row.assignee}</div>
        <div className="font-mono text-xs text-muted-foreground">confidence {row.confidenceLabel}</div>
      </div>
    )
  }
];

function toneClass(tone: ReadinessTone): string {
  if (tone === "danger") {
    return "text-destructive";
  }

  if (tone === "warning") {
    return "text-warning";
  }

  return tone === "success" ? "text-success" : "text-foreground";
}

function errorMessage(reason: unknown): string {
  return reason instanceof Error ? reason.message : "Request failed.";
}
