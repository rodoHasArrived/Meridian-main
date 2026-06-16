import { AlertCircle, BookCheck, Briefcase, CheckCircle2, Landmark, Network, Paperclip, RefreshCcw, Search, ShieldCheck, Table2, TrendingUp, UserCheck, WalletCards, X } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { useEffect, useMemo, useState } from "react";
import { MetricCard } from "@/components/meridian/metric-card";
import { DenseDataTable, EntitySummary, ToolbarStrip, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { FinancialRecordExplorerShell } from "@/components/meridian/financial-record-explorer";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { LotsTrackerPanel, SecurityDetailsPanel } from "@/components/meridian/security-details-tracker";
import {
  approveOperationsContinuityWorkflow,
  getAccountingSystemProviders,
  getLatestAccountingSystemImport,
  getLatestAccountingSystemReconciliation,
  getFinancialRecordExplorer,
  getOperationsContinuityWorkflow,
  getOperationsContinuityWorkflows,
  previewAccountingSystemImport,
  rejectOperationsContinuityWorkflow,
  saveFinancialRecordExplorerView
} from "@/lib/api";
import { cn } from "@/lib/utils";
import { WORKSTATION_ROUTE_CATALOG, workspaceForPath } from "@/lib/workspace";
import {
  buildAccountingLoadingViewState,
  buildCloseCommandCenterViewState,
  buildAccountingWorkflowLaunchViewState,
  resolveAccountingWorkstream,
  SECURITY_IDENTITY_DETAIL_PANEL_ID,
  useCapitalAccountWorkbenchViewModel,
  useAccountingConfigurationViewModel,
  useAccountingCashFlowViewModel,
  useManualJournalEntryWorkbenchViewModel,
  useAccountingReconciliationViewModel,
  useAccountingReportingViewModel,
  useReconciliationResolveDialogViewModel,
  useSecurityMasterViewModel
} from "@/screens/accounting-screen.view-model";
import { buildMultiAssetCoveragePanel } from "@/screens/portfolio-screen.view-model";
import type {
  CalibrationProfileRowViewModel,
  CalibrationSummaryViewModel,
  AccountingWorkstream,
  AccountingConfigurationViewModel,
  CapitalAccountWorkbenchViewModel,
  ManualJournalEntryWorkbenchViewModel,
  CorporateActionsViewState,
  CorporateActionRowViewModel,
  ReconciliationBreakRowViewModel,
  ReconciliationQueuePanelViewState,
  ReconciliationQueueRunRowViewModel,
  ReconciliationStatementRunRowViewModel,
  ReconciliationQueueRunTone,
  AccountingTrialBalanceRowViewModel,
  OperationalExceptionWorkbenchViewState,
  SecuritySchedulesViewState,
  SecurityScheduleRowViewModel,
  SecurityOpenLotReadModelViewState,
  SecurityOpenLotRowViewModel,
  SecuritySearchResultRowViewModel,
  CloseCommandCenterViewState,
  AccountingWorkflowLaunchViewState,
  AccountingToolingTone,
  TradingParametersViewState
} from "@/screens/accounting-screen.view-model";
import type {
  AccountingSystemImportDetail,
  AccountingSystemReconciliationEvidencePackage,
  AccountingSystemProvider,
  AccountingSystemReconciliationSummary,
  AccountingWorkspaceResponse,
  FinancialRecordExplorerDto,
  FinancialRecordExplorerSavedViewSaveRequestDto,
  MultiAssetCoverageSummary,
  OperationsApproval,
  OperationsApprovalState,
  OperationsContinuityWorkflow,
  OperationsContinuityWorkflowSummary,
  OperationsTimelineEntry,
  OperationsWorkflowBlocker
} from "@/types";

interface AccountingScreenProps {
  data: AccountingWorkspaceResponse | null;
  multiAssetCoverage?: MultiAssetCoverageSummary | null;
}

const calibrationProfileColumns: DenseDataTableColumn<CalibrationProfileRowViewModel>[] = [
  {
    id: "profile",
    label: "Profile",
    render: (row) => <span className="font-mono text-foreground">{row.toleranceProfileId}</span>
  },
  {
    id: "route",
    label: "Route",
    render: (row) => <span className="text-muted-foreground">{row.exceptionRoute}</span>
  },
  {
    id: "severity",
    label: "Severity",
    render: (row) => <span className={cn("font-mono", calibrationSeverityClass(row.highestSeverity))}>{row.highestSeverity}</span>
  },
  {
    id: "open",
    label: "Open",
    align: "right",
    render: (row) => <span className={cn("font-mono tabular-nums", row.openBreakCount > 0 ? "text-warning" : "text-foreground")}>{row.openBreakCount}</span>
  },
  {
    id: "in-review",
    label: "Review",
    align: "right",
    render: (row) => <span className="font-mono tabular-nums text-foreground">{row.inReviewBreakCount}</span>
  },
  {
    id: "pending-signoff",
    label: "Sign-off",
    align: "right",
    render: (row) => (
      <span className={cn("font-mono tabular-nums", row.pendingSignoffCount > 0 ? "text-warning" : "text-foreground")}>
        {row.pendingSignoffCount}
      </span>
    )
  },
  {
    id: "tolerance",
    label: "Tolerance",
    align: "right",
    render: (row) => <span className="font-mono text-muted-foreground">{row.maxToleranceBandLabel}</span>
  },
  {
    id: "updated",
    label: "Updated",
    render: (row) => <span className="font-mono text-muted-foreground">{row.lastUpdatedLabel}</span>
  }
];

const reconciliationQueueToneClass: Record<ReconciliationQueueRunTone, string> = {
  muted: "text-muted-foreground",
  warning: "text-warning",
  success: "text-success",
  primary: "text-primary"
};

function calibrationSeverityClass(severity: string): string {
  const normalized = severity.trim().toLowerCase();
  if (normalized === "critical") {
    return "text-danger";
  }

  if (normalized === "warning" || normalized === "warn") {
    return "text-warning";
  }

  return "text-foreground";
}

const reconciliationQueueColumns: DenseDataTableColumn<ReconciliationQueueRunRowViewModel>[] = [
  {
    id: "run",
    label: "Run",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.strategyName}</span>
        <span className="mt-1 block font-mono text-[11px] text-muted-foreground">{row.runId}</span>
      </span>
    )
  },
  { id: "mode", label: "Mode", render: (row) => <span className="font-mono uppercase text-muted-foreground">{row.modeLabel}</span> },
  { id: "status", label: "Status", render: (row) => row.runStatusLabel },
  { id: "breaks", label: "Breaks", align: "right", render: (row) => <span className="font-mono">{row.breakCountLabel}</span> },
  { id: "open", label: "Open", align: "right", render: (row) => <span className="font-mono">{row.openBreakLabel}</span> },
  {
    id: "reconciliation",
    label: "Reconciliation",
    render: (row) => (
      <span className={cn("font-mono text-xs uppercase tracking-[0.14em]", reconciliationQueueToneClass[row.reconciliationTone])}>
        {row.reconciliationStatusLabel}
      </span>
    )
  },
  { id: "updated", label: "Updated", render: (row) => <span className="font-mono text-muted-foreground">{row.lastUpdatedLabel}</span> }
];


const reconciliationStatementRunColumns: DenseDataTableColumn<ReconciliationStatementRunRowViewModel>[] = [
  { id: "brokerCustodian", label: "Broker/Custodian", render: (row) => <span title={row.unavailableReason ?? undefined}>{row.brokerCustodianLabel}</span> },
  { id: "account", label: "Account", render: (row) => <span title={row.unavailableReason ?? undefined}>{row.accountLabel}</span> },
  { id: "period", label: "Period", render: (row) => <span className="font-mono text-muted-foreground" title={row.unavailableReason ?? undefined}>{row.periodLabel}</span> },
  { id: "status", label: "Status", render: (row) => <Badge variant={row.statusLabel === "Matched" || row.statusLabel === "Balanced" ? "success" : "warning"}>{row.statusLabel}</Badge> },
  { id: "validation", label: "Validation issues", align: "right", render: (row) => <span className="font-mono">{row.validationIssueCountLabel}</span> },
  { id: "matches", label: "Matches", align: "right", render: (row) => <span className="font-mono">{row.matchCountLabel}</span> },
  { id: "breaks", label: "Breaks", align: "right", render: (row) => <span className="font-mono">{row.breakCountLabel}</span> },
  { id: "cases", label: "Cases", align: "right", render: (row) => <span className="font-mono">{row.caseCountLabel}</span> },
  { id: "imported", label: "Imported", render: (row) => <span className="font-mono text-muted-foreground">{row.importedAtLabel}</span> }
];

const reconciliationBreakColumns: DenseDataTableColumn<ReconciliationBreakRowViewModel>[] = [
  {
    id: "break",
    label: "Break",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.strategyName}</span>
        <span className="mt-1 block font-mono text-[11px] text-muted-foreground">{row.breakId}</span>
      </span>
    )
  },
  { id: "category", label: "Category", render: (row) => <span className="font-mono text-muted-foreground">{row.category}</span> },
  {
    id: "variance",
    label: "Variance",
    align: "right",
    render: (row) => (
      <span
        className={cn(
          "font-mono tabular-nums",
          row.varianceTone === "success" ? "text-success" : row.varianceTone === "danger" ? "text-danger" : "text-foreground"
        )}
      >
        {row.varianceLabel}
      </span>
    )
  },
  { id: "owner", label: "Owner", render: (row) => <span className="font-mono text-muted-foreground">{row.ownerLabel}</span> },
  { id: "updated", label: "Updated", render: (row) => <span className="font-mono text-muted-foreground">{row.lastUpdatedAtLabel}</span> },
  { id: "status", label: "Status", render: (row) => <Badge variant={row.statusBadgeVariant}>{row.status}</Badge> }
];

const trialBalanceColumns: DenseDataTableColumn<AccountingTrialBalanceRowViewModel>[] = [
  {
    id: "account",
    label: "Account",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.accountLabel}</span>
        <span className="mt-1 block font-mono text-[11px] text-muted-foreground">{row.financialAccountId ?? "Unassigned"}</span>
      </span>
    )
  },
  { id: "type", label: "Type", render: (row) => <span className="font-mono text-muted-foreground">{row.accountTypeLabel}</span> },
  { id: "basis", label: "Basis", render: (row) => <Badge variant={row.basisTone}>{row.basisLabel}</Badge> },
  {
    id: "balance",
    label: "Balance",
    align: "right",
    render: (row) => (
      <span
        className={cn(
          "font-mono tabular-nums",
          row.balanceTone === "success" ? "text-success" : row.balanceTone === "danger" ? "text-danger" : "text-foreground"
        )}
      >
        {row.balanceLabel}
      </span>
    )
  },
  { id: "entries", label: "Entries", align: "right", render: (row) => <span className="font-mono tabular-nums">{row.entryCountLabel}</span> }
];

const corporateActionColumns: DenseDataTableColumn<CorporateActionRowViewModel>[] = [
  {
    id: "eventType",
    label: "Event type",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.eventTypeLabel}</span>
        <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{row.corpActId}</span>
      </span>
    )
  },
  { id: "exDate", label: "Ex-date", render: (row) => <span className="font-mono text-muted-foreground">{row.exDateLabel}</span> },
  { id: "payDate", label: "Pay date", render: (row) => <span className="font-mono text-muted-foreground">{row.payDateLabel}</span> },
  { id: "amount", label: "Amount", align: "right", render: (row) => <span className="font-mono tabular-nums text-foreground">{row.amountLabel}</span> }
];

const securityScheduleColumns: DenseDataTableColumn<SecurityScheduleRowViewModel>[] = [
  {
    id: "eventType",
    label: "Event",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.eventTypeLabel}</span>
        <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{row.eventId}</span>
      </span>
    )
  },
  { id: "paymentDate", label: "Payment date", render: (row) => <span className="font-mono text-muted-foreground">{row.paymentDateLabel}</span> },
  { id: "expected", label: "Expected", align: "right", render: (row) => <span className="font-mono tabular-nums text-foreground">{row.expectedAmountLabel}</span> },
  {
    id: "actual",
    label: "Actual",
    align: "right",
    render: (row) => (
      <span className={cn("font-mono tabular-nums", row.actualAmount === null ? "text-muted-foreground" : "text-foreground")}>
        {row.actualAmountLabel}
      </span>
    )
  },
  {
    id: "variance",
    label: "Variance",
    align: "right",
    render: (row) => (
      <span className={cn("font-mono tabular-nums", row.postingStatus === "Variance" ? "text-danger" : "text-muted-foreground")}>
        {row.varianceLabel}
      </span>
    )
  },
  { id: "factor", label: "Factor", align: "right", render: (row) => <span className="font-mono tabular-nums text-muted-foreground">{row.factorLabel}</span> },
  { id: "status", label: "Status", render: (row) => <Badge variant={row.postingStatusTone}>{row.postingStatusLabel}</Badge> }
];

const securityOpenLotColumns: DenseDataTableColumn<SecurityOpenLotRowViewModel>[] = [
  {
    id: "lot",
    label: "Lot",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.lotId}</span>
        <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{row.runId}</span>
      </span>
    )
  },
  { id: "scope", label: "Scope", render: (row) => <span className="text-muted-foreground">{row.scopeLabel}</span> },
  { id: "tradeDate", label: "Trade", render: (row) => <span className="font-mono text-muted-foreground">{row.tradeDateLabel}</span> },
  { id: "quantity", label: "Quantity", align: "right", render: (row) => <span className="font-mono tabular-nums text-foreground">{row.quantityLabel}</span> },
  { id: "face", label: "Face", align: "right", render: (row) => <span className="font-mono tabular-nums text-muted-foreground">{row.faceLabel}</span> },
  { id: "factor", label: "Factor adj.", align: "right", render: (row) => <span className="font-mono tabular-nums text-muted-foreground">{row.factorAdjustedLabel}</span> },
  { id: "cost", label: "Cost", align: "right", render: (row) => <span className="font-mono tabular-nums text-foreground">{row.costBasisLabel}</span> },
  {
    id: "pnl",
    label: "Unrealized",
    align: "right",
    render: (row) => (
      <span className={cn("font-mono tabular-nums", row.unrealizedPnl !== null && row.unrealizedPnl < 0 ? "text-danger" : "text-muted-foreground")}>
        {row.unrealizedPnlLabel}
      </span>
    )
  },
  { id: "status", label: "Status", render: (row) => <Badge variant={row.statusTone}>{row.statusLabel}</Badge> }
];

const focusCopy: Record<string, { title: string; description: string }> = {
  ledger: {
    title: "Ledger overview",
    description: "Cash, ledger coverage, and audit-facing balances remain visible from the workstation shell."
  },
  configure: {
    title: "Configurable accounting setup",
    description: "Books, chart accounts, journal templates, posting rules, validation, and action audit evidence are prepared before activation."
  },
  "journal-entries": {
    title: "Journal entry workbench",
    description: "Manual journal entry drafts, line-level Security Master attribution, GL account picks, balancing validation, and approval submission stay endpoint-backed."
  },
  "capital-accounts": {
    title: "Capital Account Workbench",
    description: "Investor-level capital account evidence, allocation rules, statement lineage, restatement support, and audit drill-throughs stay endpoint-backed."
  },
  reconciliation: {
    title: "Reconciliation queue",
    description: "Open breaks, timing drift, and balanced runs stay visible without leaving Accounting."
  },
  exceptions: {
    title: "Operational exception workbench",
    description: "Break queues, comments, workflow states, audit evidence, and approval handoffs stay together for case resolution."
  },
  "security-master": {
    title: "Security coverage",
    description: "Coverage gaps and reference integrity stay tied to reconciliation and reporting readiness."
  },
  approvals: {
    title: "Approval gate",
    description: "Pending, blocked, and completed close approvals stay grouped with signer, evidence, and audit context before release."
  },
  reporting: {
    title: "Reporting profiles",
    description: "Report packs, governed exports, and loader artifacts stay tied to accounting evidence."
  }
};

const accountingSystemStatusVariant = {
  Matched: "success",
  Variance: "warning",
  MissingExternal: "warning",
  MissingMeridian: "danger",
  ReviewRequired: "warning"
} as const;

const accountingSystemEvidencePackageVariant = {
  Ready: "success",
  ReviewRequired: "warning",
  Missing: "danger"
} as const;

function AccountingSystemReconciliationPanel({
  providers,
  importDetail,
  reconciliation,
  loading,
  error,
  onRefresh
}: {
  providers: AccountingSystemProvider[];
  importDetail: AccountingSystemImportDetail | null;
  reconciliation: AccountingSystemReconciliationSummary | null;
  loading: boolean;
  error: string | null;
  onRefresh: () => void;
}) {
  const activeProvider = providers.find((provider) => provider.providerId === importDetail?.summary.providerId)
    ?? providers.find((provider) => provider.providerId === "quickbooks" && provider.state === "Available")
    ?? providers.find((provider) => provider.providerId === "quickbooks-fixture")
    ?? providers[0]
    ?? null;
  const quickBooksProvider = providers.find((provider) => provider.providerId === "quickbooks");
  const selectedCompanyLabel = activeProvider?.connection?.companyName ?? activeProvider?.connection?.companyId ?? null;
  const rows = reconciliation?.rows.slice(0, 5) ?? [];
  const evidencePackages = reconciliation?.evidencePackages?.slice(0, 4) ?? [];

  return (
    <Card className="panel-surface" role="region" aria-label="External GL reconciliation">
      <CardHeader>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <div className="eyebrow-label">External GL reconciliation</div>
            <CardTitle className="mt-2 flex items-center gap-2 text-base">
              <BookCheck className="h-4 w-4 text-primary" aria-hidden="true" />
              {activeProvider ? `${activeProvider.displayName} evidence` : "External GL evidence"}
            </CardTitle>
            <CardDescription className="mt-2">
              External accounting-system records are imported as read-only evidence against Meridian-owned ledger truth; posting back to the external GL remains disabled.
            </CardDescription>
            {selectedCompanyLabel ? (
              <div className="mt-2 text-xs text-muted-foreground">Selected company: {selectedCompanyLabel}</div>
            ) : null}
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {activeProvider ? <Badge variant="success">{activeProvider.statusLabel}</Badge> : null}
            {quickBooksProvider && quickBooksProvider !== activeProvider ? <Badge variant="outline">{quickBooksProvider.statusLabel}</Badge> : null}
            <Button size="sm" variant="outline" onClick={onRefresh} disabled={loading} busy={loading} busyLabel="Refreshing GL evidence">
              <RefreshCcw className={cn("h-3.5 w-3.5", loading && "animate-spin")} aria-hidden="true" />
              Refresh
            </Button>
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {error ? (
          <div role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger">
            {error}
          </div>
        ) : null}
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <AccountingValue label="Import state" value={importDetail?.summary.state ?? (loading ? "Loading" : "Not loaded")} />
          <AccountingValue label="Trial balance lines" value={String(importDetail?.summary.trialBalanceLineCount ?? 0)} />
          <AccountingValue label="Matched rows" value={String(reconciliation?.matchedCount ?? 0)} />
          <AccountingValue label="Break rows" value={String(reconciliation?.breakCount ?? 0)} />
        </div>
        {reconciliation ? (
          <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_18rem]">
            <div className="overflow-hidden rounded-md border border-border/70">
              <table className="w-full text-left text-sm">
                <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                  <tr>
                    <th className="px-3 py-2 font-medium">Account</th>
                    <th className="px-3 py-2 text-right font-medium">External</th>
                    <th className="px-3 py-2 text-right font-medium">Meridian</th>
                    <th className="px-3 py-2 text-right font-medium">Variance</th>
                    <th className="px-3 py-2 font-medium">Status</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row) => (
                    <tr key={row.rowId} className="border-t border-border/60">
                      <td className="px-3 py-2">
                        <span className="block font-semibold text-foreground">{row.accountName}</span>
                        <span className="block font-mono text-[11px] text-muted-foreground">{row.accountCode}</span>
                      </td>
                      <td className="px-3 py-2 text-right font-mono">{formatGlAmount(row.externalDebit - row.externalCredit, row.currency)}</td>
                      <td className="px-3 py-2 text-right font-mono">{formatGlAmount(row.meridianDebit - row.meridianCredit, row.currency)}</td>
                      <td className="px-3 py-2 text-right font-mono">{formatGlAmount(row.variance, row.currency)}</td>
                      <td className="px-3 py-2">
                        <Badge variant={accountingSystemStatusVariant[row.status]}>{row.status}</Badge>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3 text-sm">
              <div className="font-semibold text-foreground">Posting/export</div>
              <p className="mt-2 text-xs leading-5 text-muted-foreground">{reconciliation.postingDisabledReason}</p>
              {evidencePackages.length > 0 ? (
                <div className="mt-3 space-y-2" aria-label="External GL evidence packages">
                  {evidencePackages.map((evidencePackage) => (
                    <AccountingSystemEvidencePackageRow key={evidencePackage.packageId} evidencePackage={evidencePackage} />
                  ))}
                </div>
              ) : (
                <div className="mt-3 space-y-1" aria-label="External GL evidence references">
                  {reconciliation.evidenceReferences.map((evidence) => (
                    <div key={evidence} className="truncate font-mono text-[11px] text-muted-foreground">{evidence}</div>
                  ))}
                </div>
              )}
            </div>
          </div>
        ) : (
          <p className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm text-muted-foreground">
            External GL reconciliation has not been loaded yet.
          </p>
        )}
      </CardContent>
    </Card>
  );
}

function AccountingSystemEvidencePackageRow({
  evidencePackage
}: {
  evidencePackage: AccountingSystemReconciliationEvidencePackage;
}) {
  const requiredActions = evidencePackage.requiredActions.slice(0, 2);

  return (
    <div className="rounded-md border border-border/60 bg-background/40 px-2.5 py-2">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="truncate text-xs font-semibold text-foreground">{evidencePackage.label}</div>
          <div className="mt-1 text-[11px] text-muted-foreground">{evidencePackage.evidenceReferenceCount} retained evidence ref(s)</div>
        </div>
        <Badge variant={accountingSystemEvidencePackageVariant[evidencePackage.status]}>{evidencePackage.status}</Badge>
      </div>
      {requiredActions.length > 0 ? (
        <ul className="mt-2 list-disc space-y-1 pl-4 text-[11px] leading-4 text-muted-foreground">
          {requiredActions.map((action) => (
            <li key={action}>{action}</li>
          ))}
        </ul>
      ) : evidencePackage.evidenceReferences[0] ? (
        <div className="mt-2 truncate font-mono text-[11px] text-muted-foreground">{evidencePackage.evidenceReferences[0]}</div>
      ) : null}
    </div>
  );
}

function formatGlAmount(value: number, currency: string): string {
  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: currency || "USD",
    maximumFractionDigits: 2
  }).format(value);
}

function AccountingApprovalsWorkstream() {
  const { search } = useLocation();
  const [workflows, setWorkflows] = useState<OperationsContinuityWorkflowSummary[]>([]);
  const [selectedWorkflowId, setSelectedWorkflowId] = useState<string | null>(null);
  const [detail, setDetail] = useState<OperationsContinuityWorkflow | null>(null);
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [action, setAction] = useState<"approve" | "reject" | "request-changes" | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const requestedApprovalId = useMemo(() => new URLSearchParams(search).get("approvalId"), [search]);

  const refreshWorkflows = async () => {
    setLoading(true);
    setError(null);
    try {
      const rows = await getOperationsContinuityWorkflows();
      const sorted = [...rows].sort((left, right) => right.updatedAtUtc.localeCompare(left.updatedAtUtc));
      setWorkflows(sorted);
      setSelectedWorkflowId((current) => current && sorted.some((row) => row.workflowId === current)
        ? current
        : sorted[0]?.workflowId ?? null);
    } catch (err) {
      setError(formatApprovalError(err, "Approval queue could not be loaded."));
      setWorkflows([]);
      setSelectedWorkflowId(null);
      setDetail(null);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void refreshWorkflows();
  }, []);

  useEffect(() => {
    if (!selectedWorkflowId) {
      setDetail(null);
      return;
    }

    let cancelled = false;
    setDetailLoading(true);
    setDetailError(null);
    getOperationsContinuityWorkflow(selectedWorkflowId)
      .then((workflow) => {
        if (!cancelled) {
          setDetail(workflow);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setDetail(null);
          setDetailError(formatApprovalError(err, "Approval detail could not be loaded."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setDetailLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [selectedWorkflowId]);

  const selectedApproval = selectApproval(detail, requestedApprovalId);
  const selectedWorkflow = detail ?? workflows.find((workflow) => workflow.workflowId === selectedWorkflowId) ?? null;
  const approveDisabledReason = buildApprovalActionDisabledReason(selectedWorkflow, selectedApproval, action);
  const rejectDisabledReason = action ? "Wait for the current approval action to finish." : selectedWorkflow ? null : "Select an approval before rejecting.";
  const requestChangesDisabledReason = action ? "Wait for the current approval action to finish." : selectedWorkflow ? null : "Select an approval before requesting changes.";

  const runDecision = async (decision: "approve" | "reject" | "request-changes") => {
    if (!selectedWorkflow) {
      return;
    }

    setAction(decision);
    setActionError(null);
    try {
      const rationale = decision === "approve"
        ? "Approved from Accounting approvals workstream."
        : decision === "request-changes"
          ? "Request changes from Accounting approvals workstream."
          : "Rejected from Accounting approvals workstream.";
      if (decision === "approve") {
        const reportPackId = resolveApprovalReportPackId(selectedWorkflow);
        if (!reportPackId) {
          throw new Error("Approval requires a report pack id from the selected workflow.");
        }

        await approveOperationsContinuityWorkflow(selectedWorkflow.workflowId, {
          expectedVersion: selectedWorkflow.version,
          actor: "browser-operator",
          reviewer: "browser-operator",
          rationale,
          reportPackId
        });
      } else {
        await rejectOperationsContinuityWorkflow(selectedWorkflow.workflowId, {
          expectedVersion: selectedWorkflow.version,
          actor: "browser-operator",
          reviewer: "browser-operator",
          rationale,
          reasonCode: decision === "request-changes" ? "request-changes" : "operator-rejected"
        });
      }

      await refreshWorkflows();
      const refreshedDetail = await getOperationsContinuityWorkflow(selectedWorkflow.workflowId);
      setSelectedWorkflowId(refreshedDetail.workflowId);
      setDetail(refreshedDetail);
    } catch (err) {
      setActionError(formatApprovalError(err, "Approval action failed."));
    } finally {
      setAction(null);
    }
  };

  return (
    <section id="accounting-approvals" className="workspace-section-band" aria-labelledby="accounting-approvals-heading">
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <p className="eyebrow-label">Approvals</p>
          <h3 id="accounting-approvals-heading" className="workspace-section-title">Approval queue and audit gate</h3>
          <p className="workspace-section-summary">Close approvals, missing evidence, signer context, and audit history come from the shared operations-continuity workflow payload.</p>
        </div>
        <Button type="button" size="sm" variant="outline" disabled={loading} busy={loading} busyLabel="Refreshing approvals" onClick={() => void refreshWorkflows()}>
          <RefreshCcw className={cn("h-3.5 w-3.5", loading && "animate-spin")} aria-hidden="true" />
          Refresh
        </Button>
      </div>
      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_26rem]">
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <UserCheck className="h-4 w-4 text-primary" aria-hidden="true" />
              Approval queue
            </CardTitle>
            <CardDescription>Pending, blocked, approved, and rejected close workflows ready for sign-off review.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <ApprovalStatusMessage loading={loading} error={error} empty={!loading && workflows.length === 0} />
            {workflows.length > 0 ? (
              <div className="overflow-hidden rounded-md border border-border/70" role="region" aria-label="Accounting approval queue">
                <table className="w-full text-left text-sm">
                  <caption className="sr-only">Accounting approval queue backed by operations continuity workflows.</caption>
                  <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                    <tr>
                      <th className="px-3 py-2 font-medium">Period</th>
                      <th className="px-3 py-2 font-medium">Status</th>
                      <th className="px-3 py-2 font-medium">Signer</th>
                      <th className="px-3 py-2 font-medium">Evidence</th>
                      <th className="px-3 py-2 font-medium">Updated</th>
                    </tr>
                  </thead>
                  <tbody>
                    {workflows.map((workflow) => {
                      const selected = workflow.workflowId === selectedWorkflowId;
                      return (
                        <tr
                          key={workflow.workflowId}
                          tabIndex={0}
                          aria-selected={selected}
                          className={cn("cursor-pointer border-t border-border/60 hover:bg-secondary/30", selected && "bg-primary/10")}
                          onClick={() => setSelectedWorkflowId(workflow.workflowId)}
                          onKeyDown={(event) => {
                            if (event.key === "Enter" || event.key === " ") {
                              event.preventDefault();
                              setSelectedWorkflowId(workflow.workflowId);
                            }
                          }}
                        >
                          <td className="px-3 py-2">
                            <span className="block font-semibold text-foreground">{workflow.periodId}</span>
                            <span className="block font-mono text-[11px] text-muted-foreground">{workflow.workflowId}</span>
                          </td>
                          <td className="px-3 py-2"><Badge variant={approvalQueueStatusTone(workflow)}>{approvalQueueStatusLabel(workflow)}</Badge></td>
                          <td className="px-3 py-2 text-muted-foreground">{approvalSignerLabel(detail?.workflowId === workflow.workflowId ? detail.approvals : [])}</td>
                          <td className="px-3 py-2 text-muted-foreground">{approvalEvidenceSummary(detail?.workflowId === workflow.workflowId ? detail : null)}</td>
                          <td className="px-3 py-2 font-mono text-muted-foreground">{formatApprovalDate(workflow.updatedAtUtc)}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            ) : null}
          </CardContent>
        </Card>

        <Card className="panel-surface" role="region" aria-label="Selected approval detail">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="text-base">Selected item detail</CardTitle>
                <CardDescription>{selectedWorkflow ? `${selectedWorkflow.periodId} / ${selectedWorkflow.fundAccountId}` : "Select an approval queue row."}</CardDescription>
              </div>
              {selectedApproval ? <Badge variant={approvalStatusTone(selectedApproval.status)}>{selectedApproval.status}</Badge> : null}
            </div>
          </CardHeader>
          <CardContent className="space-y-4 text-sm">
            {detailLoading ? <p role="status" className="text-muted-foreground">Loading selected approval detail...</p> : null}
            {detailError ? <div role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-danger">{detailError}</div> : null}
            {actionError ? <div role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-danger">{actionError}</div> : null}
            {selectedWorkflow ? (
              <>
                <div className="grid gap-2 sm:grid-cols-2">
                  <AccountingValue label="Workflow" value={selectedWorkflow.workflowId} />
                  <AccountingValue label="Approval ID" value={selectedApproval?.approvalId ?? requestedApprovalId ?? "No approval row"} />
                  <AccountingValue label="Required signers" value={approvalSignerLabel(detail?.approvals ?? [])} />
                  <AccountingValue label="Missing evidence" value={missingApprovalEvidenceLabel(detail)} />
                </div>
                <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
                  <div className="text-xs font-semibold uppercase text-muted-foreground">Why blocked</div>
                  <p className="mt-2 leading-6 text-muted-foreground">{approvalBlockedReason(detail)}</p>
                </div>
                <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
                  <div className="text-xs font-semibold uppercase text-muted-foreground">Next action</div>
                  <p className="mt-2 leading-6 text-muted-foreground">{approvalNextAction(detail, selectedWorkflow)}</p>
                </div>
                <div className="flex flex-wrap gap-2">
                  <Button size="sm" disabled={approveDisabledReason !== null} disabledReason={approveDisabledReason} busy={action === "approve"} busyLabel="Approving" onClick={() => void runDecision("approve")}>Approve</Button>
                  <Button size="sm" variant="outline" disabled={rejectDisabledReason !== null} disabledReason={rejectDisabledReason} busy={action === "reject"} busyLabel="Rejecting" onClick={() => void runDecision("reject")}>Reject</Button>
                  <Button size="sm" variant="ghost" disabled={requestChangesDisabledReason !== null} disabledReason={requestChangesDisabledReason} busy={action === "request-changes"} busyLabel="Requesting changes" onClick={() => void runDecision("request-changes")}>Request changes</Button>
                </div>
              </>
            ) : (
              <p className="text-muted-foreground">No approval workflow is selected.</p>
            )}
          </CardContent>
        </Card>
      </div>

      <Card className="panel-surface" role="region" aria-label="Approval audit trail">
        <CardHeader>
          <CardTitle className="text-base">Full audit trail</CardTitle>
          <CardDescription>Hash-linked workflow timeline for the selected approval item.</CardDescription>
        </CardHeader>
        <CardContent>
          {detail?.timeline.length ? (
            <div className="space-y-2">
              {detail.timeline.map((entry) => <ApprovalTimelineRow key={entry.auditId} entry={entry} />)}
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">{detailLoading ? "Loading audit trail..." : "No audit trail rows are available for the selected approval."}</p>
          )}
        </CardContent>
      </Card>
    </section>
  );
}

function ApprovalStatusMessage({ loading, error, empty }: { loading: boolean; error: string | null; empty: boolean }) {
  if (loading) {
    return <p role="status" className="text-sm text-muted-foreground">Loading approval queue...</p>;
  }

  if (error) {
    return <div role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger">{error}</div>;
  }

  if (empty) {
    return <p className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm text-muted-foreground">No approval workflows are available.</p>;
  }

  return null;
}

function ApprovalTimelineRow({ entry }: { entry: OperationsTimelineEntry }) {
  return (
    <div className="rounded-md border border-border/70 px-3 py-2 text-sm">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="font-semibold text-foreground">{splitApprovalWords(entry.eventType)}</span>
        <span className="font-mono text-[11px] text-muted-foreground">{formatApprovalDate(entry.occurredAtUtc)}</span>
      </div>
      <p className="mt-1 text-muted-foreground">{entry.rationale || `${splitApprovalWords(entry.fromState)} -> ${splitApprovalWords(entry.toState)}`}</p>
      <div className="mt-2 flex flex-wrap gap-2 text-[11px] text-muted-foreground">
        <span>Actor: {entry.actor || "unknown"}</span>
        <span>Hash: {entry.currentHash || "pending"}</span>
        <span>Evidence: {entry.references.length}</span>
      </div>
    </div>
  );
}

function selectApproval(workflow: OperationsContinuityWorkflow | null, requestedApprovalId: string | null): OperationsApproval | null {
  if (!workflow) {
    return null;
  }

  if (requestedApprovalId) {
    const requested = workflow.approvals.find((approval) => approval.approvalId === requestedApprovalId);
    if (requested) {
      return requested;
    }
  }

  return [...workflow.approvals].sort((left, right) => {
    const leftTime = left.decidedAtUtc ?? left.submittedAtUtc ?? "";
    const rightTime = right.decidedAtUtc ?? right.submittedAtUtc ?? "";
    return rightTime.localeCompare(leftTime);
  })[0] ?? null;
}

function approvalQueueStatusLabel(workflow: OperationsContinuityWorkflowSummary): string {
  const approvalGate = workflow.gates.find((gate) => gate.gateKey === "Approval") ?? null;
  if (workflow.status === "Blocked" || approvalGate?.status === "Blocked") {
    return "Blocked";
  }

  if (workflow.status === "Closed" || workflow.status === "ReadyForClose" || approvalGate?.status === "Passed") {
    return "Approved";
  }

  return "Pending";
}

function approvalQueueStatusTone(workflow: OperationsContinuityWorkflowSummary): "success" | "warning" | "danger" | "outline" {
  const label = approvalQueueStatusLabel(workflow);
  if (label === "Approved") return "success";
  if (label === "Blocked") return "danger";
  return "warning";
}

function approvalStatusTone(status: OperationsApprovalState): "success" | "warning" | "danger" | "outline" {
  if (status === "Approved") return "success";
  if (status === "Rejected") return "danger";
  if (status === "Pending") return "outline";
  return "warning";
}

function approvalSignerLabel(approvals: OperationsApproval[]): string {
  const signers = approvals
    .flatMap((approval) => [approval.reviewer, approval.operator])
    .map((value) => value?.trim())
    .filter((value): value is string => Boolean(value));
  const unique = [...new Set(signers)];
  return unique.length > 0 ? unique.join(", ") : "Reviewer pending";
}

function approvalEvidenceSummary(workflow: OperationsContinuityWorkflow | null): string {
  if (!workflow) {
    return "Detail pending";
  }

  const count = workflow.evidenceLinks.length
    + workflow.reportPackReadiness.evidenceLinks.length
    + workflow.approvals.reduce((total, approval) => total + approval.evidenceLinks.length, 0);
  return count === 0 ? "No evidence links" : `${count} evidence link${count === 1 ? "" : "s"}`;
}

function missingApprovalEvidenceLabel(workflow: OperationsContinuityWorkflow | null): string {
  if (!workflow) {
    return "Detail pending";
  }

  const missing: string[] = [];
  if (!resolveApprovalReportPackId(workflow)) {
    missing.push("report pack");
  }

  if (!workflow.reportPackReadiness.isReady) {
    missing.push("report readiness");
  }

  const blockerEvidenceMissing = approvalBlockers(workflow).some((blocker) => blocker.evidenceLinks.length === 0);
  if (blockerEvidenceMissing) {
    missing.push("blocker evidence");
  }

  return missing.length === 0 ? "None" : missing.join(", ");
}

function approvalBlockedReason(workflow: OperationsContinuityWorkflow | null): string {
  if (!workflow) {
    return "Approval detail is still loading.";
  }

  const blockers = approvalBlockers(workflow);
  if (blockers.length > 0) {
    return blockers.map((blocker) => blocker.message).join(" ");
  }

  if (!workflow.reportPackReadiness.isReady) {
    return workflow.reportPackReadiness.blockingReason ?? "Report pack readiness is still blocked.";
  }

  return "No approval blockers are surfaced for the selected workflow.";
}

function approvalNextAction(workflow: OperationsContinuityWorkflow | null, summary: OperationsContinuityWorkflowSummary): string {
  const source = workflow ?? summary;
  const approvalAction = source.nextActions.find((action) => action.gate === "Approval") ?? source.nextActions[0] ?? null;
  if (approvalAction) {
    return approvalAction.label;
  }

  if (approvalQueueStatusLabel(summary) === "Approved") {
    return "Approval is complete; continue to evidence production or close package publication.";
  }

  return "Review blockers, evidence, and required signers before taking an approval action.";
}

function approvalBlockers(workflow: OperationsContinuityWorkflow): OperationsWorkflowBlocker[] {
  return [
    ...workflow.blockers,
    ...workflow.gates.flatMap((gate) => gate.gateKey === "Approval" ? gate.blockers : [])
  ];
}

function buildApprovalActionDisabledReason(
  workflow: OperationsContinuityWorkflow | OperationsContinuityWorkflowSummary | null,
  approval: OperationsApproval | null,
  action: string | null
): string | null {
  if (action) {
    return "Wait for the current approval action to finish.";
  }

  if (!workflow) {
    return "Select an approval before approving.";
  }

  if (approval?.status === "Approved") {
    return "This approval is already approved.";
  }

  if ("reportPackReadiness" in workflow && !resolveApprovalReportPackId(workflow)) {
    return "Approval requires a report pack id from the selected workflow.";
  }

  return null;
}

function resolveApprovalReportPackId(workflow: OperationsContinuityWorkflow | OperationsContinuityWorkflowSummary): string | null {
  if ("closePackage" in workflow && workflow.closePackage?.reportPackId) {
    return workflow.closePackage.reportPackId;
  }

  if ("reportPackReadiness" in workflow && workflow.reportPackReadiness.reportPackId) {
    return workflow.reportPackReadiness.reportPackId;
  }

  return null;
}

function formatApprovalDate(value: string | null | undefined): string {
  if (!value) {
    return "Not recorded";
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString("en-US", { timeZone: "UTC" });
}

function splitApprovalWords(value: string): string {
  return value.replace(/([a-z])([A-Z])/g, "$1 $2");
}

function formatApprovalError(err: unknown, fallback: string): string {
  return err instanceof Error ? err.message || fallback : fallback;
}

export function AccountingScreen({ data, multiAssetCoverage }: AccountingScreenProps) {
  const { pathname, search } = useLocation();
  const workstream = resolveAccountingWorkstream(pathname);
  const workspace = workspaceForPath(pathname);
  const reconciliation = useAccountingReconciliationViewModel(data, workstream);
  const resolveDialog = useReconciliationResolveDialogViewModel(reconciliation.resolveBreak);
  const selectedReconciliation = reconciliation.selectedReconciliation;
  const selectedReconciliationDetail = reconciliation.detailView;
  const selectedReconciliationOpenBreakLabel = `${selectedReconciliation?.openBreakCount ?? 0} open break${selectedReconciliation?.openBreakCount === 1 ? "" : "s"}`;
  const selectedReconciliationOpenBreakTone = (selectedReconciliation?.openBreakCount ?? 0) === 0 ? "success" : "warning";
  const cashFlow = useAccountingCashFlowViewModel(data?.cashFlow ?? null, pathname, workstream);
  const reporting = useAccountingReportingViewModel(data?.reporting ?? null);
  const configuration = useAccountingConfigurationViewModel();
  const journalEntries = useManualJournalEntryWorkbenchViewModel(workstream === "journal-entries");
  const capitalAccountWorkbench = useCapitalAccountWorkbenchViewModel(workstream === "capital-accounts", search);
  const securityMaster = useSecurityMasterViewModel(workstream === "security-master");
  const [accountingSystemProviders, setAccountingSystemProviders] = useState<AccountingSystemProvider[]>([]);
  const [accountingSystemImport, setAccountingSystemImport] = useState<AccountingSystemImportDetail | null>(null);
  const [accountingSystemReconciliation, setAccountingSystemReconciliation] = useState<AccountingSystemReconciliationSummary | null>(null);
  const [accountingSystemLoading, setAccountingSystemLoading] = useState(false);
  const [accountingSystemError, setAccountingSystemError] = useState<string | null>(null);
  const [closeWorkflow, setCloseWorkflow] = useState<OperationsContinuityWorkflow | null>(null);
  const [closeWorkflowLoading, setCloseWorkflowLoading] = useState(false);
  const [closeWorkflowError, setCloseWorkflowError] = useState<string | null>(null);
  const [ledgerExplorer, setLedgerExplorer] = useState<FinancialRecordExplorerDto | null>(null);
  const [securityInstrumentExplorer, setSecurityInstrumentExplorer] = useState<FinancialRecordExplorerDto | null>(null);
  const identity = securityMaster.identityView;
  const selectedSecurityEntry = securityMaster.selectedSecurityId
    ? securityMaster.results?.find((entry) => entry.securityId === securityMaster.selectedSecurityId) ?? null
    : null;
  const identifierColumns = useMemo<DenseDataTableColumn<NonNullable<typeof identity>["identifiers"][number]>[]>(() => [
    { id: "kind", label: "Kind", render: (identifier) => <span className="font-mono">{identifier.kind}</span> },
    { id: "value", label: "Value", render: (identifier) => <span className="font-mono text-foreground">{identifier.value}</span> },
    { id: "provider", label: "Provider", render: (identifier) => identifier.providerLabel },
    { id: "state", label: "State", render: (identifier) => <Badge variant={identifier.primaryBadgeVariant}>{identifier.primaryLabel}</Badge> },
    { id: "range", label: "Valid range", render: (identifier) => <span className="font-mono text-muted-foreground">{identifier.validRangeLabel}</span> }
  ], []);
  const aliasColumns = useMemo<DenseDataTableColumn<NonNullable<typeof identity>["aliases"][number]>[]>(() => [
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
  ], []);
  const securityResultColumns = useMemo<DenseDataTableColumn<SecuritySearchResultRowViewModel>[]>(() => [
    {
      id: "name",
      label: "Name",
      render: (row) => (
        <span className="block min-w-0">
          <span className="block font-semibold text-foreground">{row.displayName}</span>
          <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{row.securityId}</span>
        </span>
      )
    },
    { id: "assetClass", label: "Asset Class", render: (row) => row.classification.assetClass },
    { id: "primaryId", label: "Primary ID", render: (row) => <span className="font-mono text-muted-foreground">{row.primaryIdentifierLabel}</span> },
    { id: "currency", label: "Currency", render: (row) => <span className="font-mono text-muted-foreground">{row.economicDefinition.currency}</span> },
    { id: "status", label: "Status", render: (row) => <Badge variant={row.statusTone === "success" ? "success" : "warning"}>{row.status}</Badge> }
  ], []);
  const activeResolveBreak = resolveDialog.active
    ? reconciliation.rows.find((item) => item.breakId === resolveDialog.active?.breakId) ?? null
    : null;

  useEffect(() => {
    let cancelled = false;

    void Promise.allSettled([
      getFinancialRecordExplorer("ledger"),
      getFinancialRecordExplorer("security-instrument")
    ]).then(([ledgerResult, securityResult]) => {
      if (cancelled) {
        return;
      }

      if (ledgerResult.status === "fulfilled") {
        setLedgerExplorer(ledgerResult.value);
      }

      if (securityResult.status === "fulfilled") {
        setSecurityInstrumentExplorer(securityResult.value);
      }
    });

    return () => {
      cancelled = true;
    };
  }, []);

  async function saveAccountingExplorerView(
    explorerId: "ledger" | "security-instrument",
    request: FinancialRecordExplorerSavedViewSaveRequestDto
  ) {
    await saveFinancialRecordExplorerView(explorerId, request);
    const refreshed = await getFinancialRecordExplorer(explorerId);
    if (explorerId === "ledger") {
      setLedgerExplorer(refreshed);
    } else {
      setSecurityInstrumentExplorer(refreshed);
    }
  }
  const reconciliationBreakTableColumns: DenseDataTableColumn<ReconciliationBreakRowViewModel>[] = [
    ...reconciliationBreakColumns,
    {
      id: "actions",
      label: "Actions",
      render: (item) => (
        <div className="panel-action-zone">
          <Button
            size="sm"
            variant="outline"
            disabled={!item.canAssign}
            disabledReason={item.assignDisabledReason}
            aria-label={item.assignAriaLabel}
            onClick={() => {
              reconciliation.selectBreak(item.breakId);
              void reconciliation.assignBreak(item.breakId);
            }}
          >
            {item.assignLabel}
          </Button>
          <Button
            size="sm"
            variant="outline"
            disabled={!item.canResolve || resolveDialog.isOpenFor(item.breakId)}
            disabledReason={resolveDialog.getActionDisabledReason(item.breakId, "resolve", item.resolveDisabledReason)}
            aria-label={item.resolveAriaLabel}
            onClick={() => {
              reconciliation.selectBreak(item.breakId);
              resolveDialog.open(item.breakId, "Resolved");
            }}
          >
            {item.resolveLabel}
          </Button>
          <Button
            size="sm"
            variant="ghost"
            disabled={!item.canDismiss || resolveDialog.isOpenFor(item.breakId)}
            disabledReason={resolveDialog.getActionDisabledReason(item.breakId, "dismiss", item.dismissDisabledReason)}
            aria-label={item.dismissAriaLabel}
            onClick={() => {
              reconciliation.selectBreak(item.breakId);
              resolveDialog.open(item.breakId, "Dismissed");
            }}
          >
            {item.dismissLabel}
          </Button>
        </div>
      )
    }
  ];

  const refreshAccountingSystem = async (persistPreview = false) => {
    setAccountingSystemLoading(true);
    setAccountingSystemError(null);
    try {
      const providers = await getAccountingSystemProviders();
      const selectedProviderId = providers.find((provider) => (
        provider.providerId === "quickbooks" && provider.state === "Available"
      ))?.providerId ?? "quickbooks-fixture";
      const importDetail = persistPreview
        ? await previewAccountingSystemImport({ providerId: selectedProviderId, persistPreview: true })
        : await getLatestAccountingSystemImport();
      const reconciliationDetail = await getLatestAccountingSystemReconciliation();
      setAccountingSystemProviders(providers);
      setAccountingSystemImport(importDetail);
      setAccountingSystemReconciliation(reconciliationDetail);
    } catch (error) {
      const message = error instanceof Error ? error.message : "Unable to load external GL reconciliation.";
      setAccountingSystemError(message);
    } finally {
      setAccountingSystemLoading(false);
    }
  };

  useEffect(() => {
    void refreshAccountingSystem(false);
  }, []);

  const refreshCloseWorkflow = async () => {
    if (!data) {
      setCloseWorkflow(null);
      return;
    }

    setCloseWorkflowLoading(true);
    setCloseWorkflowError(null);
    try {
      const rows = await getOperationsContinuityWorkflows();
      const selected = [...rows].sort((left, right) => right.updatedAtUtc.localeCompare(left.updatedAtUtc))[0] ?? null;
      if (!selected) {
        setCloseWorkflow(null);
        return;
      }

      const workflow = await getOperationsContinuityWorkflow(selected.workflowId);
      setCloseWorkflow(workflow);
    } catch (error) {
      setCloseWorkflow(null);
      setCloseWorkflowError(formatApprovalError(error, "Close workflow detail could not be loaded."));
    } finally {
      setCloseWorkflowLoading(false);
    }
  };

  useEffect(() => {
    void refreshCloseWorkflow();
  }, [data]);

  const closeCommandCenter = useMemo(
    () => data ? buildCloseCommandCenterViewState({
      data,
      workflow: closeWorkflow,
      workflowLoading: closeWorkflowLoading,
      workflowError: closeWorkflowError,
      accountingSystemProviders,
      accountingSystemImport,
      accountingSystemReconciliation,
      multiAssetCoverage
    }) : null,
    [
      accountingSystemImport,
      accountingSystemProviders,
      accountingSystemReconciliation,
      closeWorkflow,
      closeWorkflowError,
      closeWorkflowLoading,
      data,
      multiAssetCoverage
    ]
  );
  const workflowLaunch = useMemo(
    () => data ? buildAccountingWorkflowLaunchViewState({
      data,
      workstream,
      closeCommandCenter
    }) : null,
    [closeCommandCenter, data, workstream]
  );

  if (!data) {
    const loading = buildAccountingLoadingViewState(pathname);
    return (
      <Card
        className="panel-surface-strong"
        role={loading.role}
        aria-busy={loading.ariaBusy}
        aria-live={loading.ariaLive}
        aria-labelledby={loading.titleId}
        aria-describedby={loading.detailId}
      >
        <CardHeader>
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div className="min-w-0">
              <div className="eyebrow-label">{loading.eyebrow}</div>
              <CardTitle id={loading.titleId} className="mt-2 text-base">{loading.title}</CardTitle>
              <CardDescription id={loading.detailId} className="mt-2">{loading.detail}</CardDescription>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <AccountingChip label="Route" value={loading.routeLabel} />
              <AccountingChip label="Workstream" value={loading.workstreamLabel} />
            </div>
          </div>
          <div className="mt-4 grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(18rem,0.38fr)]">
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
              <div className="text-xs font-semibold uppercase text-muted-foreground">{loading.statusItemsLabel}</div>
              <div role="list" className="mt-3 grid gap-2 md:grid-cols-3">
                {loading.statusItems.map((item) => (
                  <div key={item.id} role="listitem" className="rounded-md border border-border/60 bg-background/45 px-3 py-2">
                    <div className="text-sm font-semibold text-foreground">{item.label}</div>
                    <p className="mt-1 text-xs leading-5 text-muted-foreground">{item.detail}</p>
                  </div>
                ))}
              </div>
            </div>
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
              <div className="text-xs font-semibold uppercase text-muted-foreground">{loading.actionsLabel}</div>
              <div className="mt-3 grid gap-2">
                {loading.actions.map((action) => (
                  <Button key={action.id} asChild size="sm" variant="outline" className="h-auto justify-start py-2 text-left">
                    <Link to={action.href} aria-label={action.ariaLabel}>
                      <span className="min-w-0">
                        <span className="block font-semibold">{action.label}</span>
                        <span className="mt-1 block text-xs font-normal leading-5 text-muted-foreground">{action.detail}</span>
                      </span>
                    </Link>
                  </Button>
                ))}
              </div>
            </div>
          </div>
        </CardHeader>
      </Card>
    );
  }

  const focus = focusCopy[workstream];
  const multiAssetCoveragePanel = buildMultiAssetCoveragePanel(multiAssetCoverage);

  return (
    <div className="space-y-8">
      <section
        id="accounting-overview"
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
          <AccountingChip label="Workstream" value={workstream} />
          <AccountingChip label="Queue" value={String(data.reconciliationQueue.length)} />
          <AccountingChip label="Breaks" value={String(data.breakQueue.length)} />
          <AccountingChip label="Profiles" value={String(data.reporting.profileCount)} />
        </div>
      </section>

      {workflowLaunch ? <AccountingWorkflowLaunchPanel view={workflowLaunch} /> : null}

      <CloseCommandCenterPanel
        view={closeCommandCenter}
        onRefresh={() => void refreshCloseWorkflow()}
      />

      {multiAssetCoveragePanel ? (
        <Card className="panel-surface" role="region" aria-label="Multi-asset accounting coverage">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <div className="eyebrow-label">Multi-asset coverage</div>
                <CardTitle className="mt-2 text-base">Accounting, reconciliation, and close readiness</CardTitle>
                <CardDescription>
                  Asset-class readiness is supplied by the shared portfolio coverage endpoint and rendered without Accounting-local rules.
                </CardDescription>
              </div>
              <Badge variant={multiAssetCoveragePanel.statusTone === "default" ? "outline" : multiAssetCoveragePanel.statusTone}>
                {multiAssetCoveragePanel.statusLabel}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            <div className="flex flex-wrap gap-2">
              {multiAssetCoveragePanel.chips.map((chip) => (
                <AccountingChip key={chip.label} label={chip.label} value={chip.value} />
              ))}
            </div>
            <div className="grid gap-2 md:grid-cols-2 xl:grid-cols-4">
              {multiAssetCoveragePanel.rows.slice(0, 8).map((item) => (
                <div key={item.assetClass} className={cn("rounded-md border bg-secondary/20 px-3 py-2", item.statusTone === "danger" ? "border-danger/30" : item.statusTone === "warning" ? "border-warning/30" : "border-border/70")}>
                  <div className="flex items-start justify-between gap-2">
                    <span className="text-sm font-semibold text-foreground">{item.displayName}</span>
                    <Badge variant={item.statusTone === "default" ? "outline" : item.statusTone}>
                      {item.statusLabel}
                    </Badge>
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">
                    {item.readinessDetail}
                  </p>
                  <a href={item.primaryEvidenceRoute} className="mt-2 block truncate text-xs font-medium text-primary hover:underline">
                    {item.evidenceLabel}
                  </a>
                  {item.drillThroughTargets.length > 0 ? (
                    <div className="mt-2 flex flex-wrap gap-1">
                      {item.drillThroughTargets.slice(0, 3).map((target) => (
                        <a
                          key={target.id}
                          href={target.href}
                          aria-label={target.ariaLabel}
                          className="rounded-sm border border-border/60 px-1.5 py-0.5 text-[11px] text-primary hover:border-primary/50 hover:bg-primary/10"
                        >
                          {target.label}
                        </a>
                      ))}
                    </div>
                  ) : null}
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">
                    {item.ledgerLabel}
                  </p>
                </div>
              ))}
            </div>
            <a href={multiAssetCoveragePanel.evidenceRoute} className="text-xs font-medium text-primary hover:underline">
              {multiAssetCoveragePanel.evidenceRouteLabel}
            </a>
          </CardContent>
        </Card>
      ) : null}

      <AccountingSystemReconciliationPanel
        providers={accountingSystemProviders}
        importDetail={accountingSystemImport}
        reconciliation={accountingSystemReconciliation}
        loading={accountingSystemLoading}
        error={accountingSystemError}
        onRefresh={() => void refreshAccountingSystem(true)}
      />

      {workstream === "configure" ? (
        <AccountingConfigurationPanel view={configuration} />
      ) : null}

      {workstream === "journal-entries" ? (
        <ManualJournalEntryWorkbenchPanel view={journalEntries} />
      ) : null}

      {workstream === "capital-accounts" ? (
        <CapitalAccountWorkbenchPanel view={capitalAccountWorkbench} />
      ) : null}

      {workstream === "approvals" ? (
        <AccountingApprovalsWorkstream />
      ) : null}

      {workstream === "exceptions" ? (
        <OperationalExceptionWorkbenchPanel view={reconciliation.exceptionWorkbench} />
      ) : null}

      <section id="accounting-posture" className="workspace-section-band" aria-labelledby="accounting-posture-heading">
        <div className="workspace-section-subheader">
          <div className="min-w-0">
            <p className="eyebrow-label">Posture</p>
            <h3 id="accounting-posture-heading" className="workspace-section-title">Accounting close posture</h3>
            <p className="workspace-section-summary">Close metrics, control center, cash flow, and lane context stay grouped for monitoring.</p>
          </div>
          <a className="workspace-section-jump" href="#accounting-exceptions">Exceptions</a>
        </div>

      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {data.metrics.map((metric) => (
          <MetricCard key={metric.id} {...metric} />
        ))}
      </section>

      {data.controlCenter ? (
        <section className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
          <Card className="panel-surface" role="region" aria-label="Operations control center">
            <CardHeader>
              <CardTitle className="text-base">Operations control center</CardTitle>
              <CardDescription>Aggregate close readiness, reconciliation backlog, approvals, and evidence completeness.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-3 text-sm">
              <div className="grid gap-3 sm:grid-cols-2">
                <AccountingValue label="Close readiness" value={data.controlCenter.closeReadiness} />
                <AccountingValue label="SLA breach count" value={String(data.controlCenter.slaBreachCount)} />
              </div>
              <div className="grid gap-3 sm:grid-cols-2">
                <label className="space-y-1">
                  <span className="text-xs text-muted-foreground">Portfolio filter</span>
                  <select className="w-full rounded border bg-background px-2 py-1 text-sm">
                    {data.controlCenter.portfolioFilterOptions.map((option) => <option key={option}>{option}</option>)}
                  </select>
                </label>
                <label className="space-y-1">
                  <span className="text-xs text-muted-foreground">Account filter</span>
                  <select className="w-full rounded border bg-background px-2 py-1 text-sm">
                    <option>all-accounts</option>
                    {data.controlCenter.accountFilterOptions.map((option) => <option key={option}>{option}</option>)}
                  </select>
                </label>
              </div>
              <div className="grid gap-2 sm:grid-cols-2">
                {data.controlCenter.trendSnapshots.map((snapshot) => (
                  <div key={snapshot.metric} className="rounded border border-border/70 px-2 py-1">
                    <div className="text-xs text-muted-foreground">{snapshot.metric}</div>
                    <div className="font-mono">{snapshot.value} · {snapshot.trend}</div>
                  </div>
                ))}
              </div>
            </CardContent>
          </Card>
          <Card className="panel-surface">
            <CardHeader>
              <CardTitle className="text-base">High-risk alerts</CardTitle>
              <CardDescription>Overdue critical breaks and report approvals requiring immediate action.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-2">
              {data.controlCenter.alerts.map((alert, index) => (
                <div key={`${alert.message}-${index}`} className={cn("rounded border px-2 py-1 text-sm", alert.tone === "danger" ? "border-danger/40 bg-danger/10 text-danger" : "border-warning/40 bg-warning/10 text-warning-foreground")}>
                  {alert.message}
                </div>
              ))}
              <div className="pt-2 text-sm">
                {data.controlCenter.drillLinks.map((link) => (
                  <div key={link.href}><Link className="text-primary underline" to={link.href}>{link.label}</Link></div>
                ))}
              </div>
            </CardContent>
          </Card>
        </section>
      ) : null}

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
            <AccountingHighlight
              icon={BookCheck}
              title="Audit posture"
              description="Reconciliation health and audit readiness stay visible for every run on the queue."
            />
            <AccountingHighlight
              icon={WalletCards}
              title="Cash flow"
              description="Portfolio cash and ledger cash stay paired so variance review is immediate."
            />
            <AccountingHighlight
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
                <AccountingValue
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
      </section>

      {workstream === "reconciliation" ? (
        <section id="accounting-exceptions" className="workspace-section-band" aria-labelledby="accounting-exceptions-heading">
          <div className="workspace-section-subheader">
            <div className="min-w-0">
              <p className="eyebrow-label">Exceptions</p>
              <h3 id="accounting-exceptions-heading" className="workspace-section-title">Reconciliation exceptions and evidence</h3>
              <p className="workspace-section-summary">Statement runs, selected queue detail, and evidence links are grouped for investigation.</p>
            </div>
            <a className="workspace-section-jump" href="#accounting-actions">Actions</a>
          </div>
        <section className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
          <Card className="panel-surface xl:col-span-2">
            <CardHeader>
              <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
                <div>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <Landmark className="h-4 w-4 text-primary" aria-hidden="true" />
                    {reconciliation.statementRunsView.title}
                  </CardTitle>
                  <CardDescription className="mt-2">{reconciliation.statementRunsView.description}</CardDescription>
                </div>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={reconciliation.statementRunsView.loadingText !== null}
                  disabledReason={reconciliation.statementRunsView.loadingText ? "Statement run refresh is already in progress." : null}
                  aria-label={reconciliation.statementRunsView.recoveryActionAriaLabel}
                  onClick={reconciliation.refreshStatementRuns}
                >
                  <RefreshCcw className="h-4 w-4" aria-hidden="true" />
                  {reconciliation.statementRunsView.recoveryActionLabel}
                </Button>
              </div>
            </CardHeader>
            <CardContent className="space-y-4">
              <span className="sr-only" aria-live="polite">{reconciliation.statementRunsView.statusAnnouncement}</span>
              {reconciliation.statementRunsView.loadingText ? (
                <p role="status" className="rounded-lg border border-border/60 bg-secondary/20 px-4 py-3 text-sm text-muted-foreground">
                  {reconciliation.statementRunsView.loadingText}
                </p>
              ) : null}
              {reconciliation.statementRunsView.errorText ? (
                <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  <div>{reconciliation.statementRunsView.errorText}</div>
                  {reconciliation.statementRunsView.errorDetails.length > 0 ? (
                    <ul className="mt-2 list-disc pl-5">
                      {reconciliation.statementRunsView.errorDetails.map((detail) => <li key={detail}>{detail}</li>)}
                    </ul>
                  ) : null}
                </div>
              ) : null}
              <DenseDataTable
                columns={reconciliationStatementRunColumns}
                rows={reconciliation.statementRunsView.rows}
                getRowId={(row) => row.runId}
                getRowAriaLabel={(row) => row.ariaLabel}
                getRowSelectAriaLabel={(row) => row.selectAriaLabel}
                getRowAriaControls={(row) => row.controlsId}
                getRowAriaExpanded={(row) => row.isSelected}
                selectedRowId={reconciliation.selectedRunId}
                onRowSelect={(row) => reconciliation.selectRun(row.runId)}
                emptyText={reconciliation.statementRunsView.emptyText}
                ariaLabel={reconciliation.statementRunsView.tableLabel}
                caption={reconciliation.statementRunsView.tableCaption}
              />
              <div
                id={reconciliation.statementRunsView.detailPanelId}
                role="tablist"
                aria-label="Statement run detail tabs"
                className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4 xl:grid-cols-7"
              >
                {reconciliation.statementRunsView.tabs.map((tab) => (
                  <Button
                    key={tab.id}
                    type="button"
                    variant="outline"
                    size="sm"
                    role="tab"
                    aria-selected={tab.id === "overview" && !tab.disabled}
                    disabled={tab.disabled}
                    disabledReason={tab.disabledReason}
                    aria-label={tab.ariaLabel}
                    className="min-h-16 justify-start whitespace-normal text-left"
                  >
                    <span>
                      <span className="block font-semibold">{tab.label}</span>
                      {tab.badgeLabel ? <span className="mt-1 block font-mono text-[10px] text-muted-foreground">{tab.badgeLabel}</span> : null}
                    </span>
                  </Button>
                ))}
              </div>
              <p className="text-xs text-muted-foreground">
                Matching, tolerance, validation, and case-state decisions remain in the shared reconciliation services; this view only renders endpoint-supplied read models.
              </p>
            </CardContent>
          </Card>

          <Card className="panel-surface">
            <CardHeader>
              <CardTitle className="flex items-center gap-2 text-base">
                <BookCheck className="h-4 w-4 text-primary" />
                {reconciliation.queuePanelView.title}
              </CardTitle>
              <CardDescription>{reconciliation.queuePanelView.description}</CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              {reconciliation.queuePanelView.hasRows ? (
                <DenseDataTable
                  columns={reconciliationQueueColumns}
                  rows={reconciliation.queuePanelView.rows}
                  getRowId={(row) => row.runId}
                  getRowAriaLabel={(row) => row.ariaLabel}
                  getRowSelectAriaLabel={(row) => row.selectAriaLabel}
                  getRowAriaControls={(row) => row.controlsId}
                  getRowAriaExpanded={(row) => row.isExpanded}
                  selectedRowId={selectedReconciliation?.runId ?? null}
                  onRowSelect={(row) => reconciliation.selectRun(row.runId)}
                  emptyText={reconciliation.queuePanelView.emptyText}
                  ariaLabel={reconciliation.queuePanelView.listLabel}
                  caption={reconciliation.queuePanelView.description}
                />
              ) : (
                <div
                  role="status"
                  className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning"
                >
                  {reconciliation.queuePanelView.emptyText}
                </div>
              )}
            </CardContent>
          </Card>

          <Card
            id={reconciliation.queuePanelView.detailPanelId}
            data-selected-source="Selected from reconciliation queue"
            className="row-detail-panel panel-surface-strong bg-panel-strong text-foreground"
            role="region"
            aria-live="polite"
            aria-label={selectedReconciliationDetail?.ariaLabel ?? reconciliation.queuePanelView.detailEmptyAriaLabel}
          >
            <CardHeader>
              <div className="eyebrow-label">{selectedReconciliationDetail?.eyebrow ?? "Reconciliation detail"}</div>
              <CardTitle>{selectedReconciliationDetail?.title ?? reconciliation.queuePanelView.detailEmptyTitle}</CardTitle>
              <CardDescription className="text-muted-foreground">
                {selectedReconciliationDetail?.description ?? reconciliation.queuePanelView.detailEmptyText}
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4 text-sm">
              {selectedReconciliationDetail ? (
                <>
                  {selectedReconciliationDetail.fields.map((field) => (
                    <AccountingValue
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
                    <div className="panel-action-footer">
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
                </>
              ) : (
                <div role="status" className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning">
                  {reconciliation.queuePanelView.detailEmptyText}
                </div>
              )}
            </CardContent>
          </Card>
        </section>
        </section>
      ) : null}

      {workstream === "ledger" && selectedReconciliation ? (
        <FinancialRecordExplorerShell
          explorerLabel="Financial Record Explorer"
          title="Ledger Explorer"
          titleId="accounting-ledger-explorer-title"
          description="Filter accounting ledger records, inspect dense trial-balance rows, and drill into journals, ledger lines, source documents, approvals, reconciliations, report usage, and audit history without leaving the Accounting workspace."
          scopeItems={[
            { id: "workspace", label: "Workspace", value: "Accounting" },
            { id: "record-set", label: "Record set", value: "Journal entries and ledger detail" },
            { id: "run", label: "Reconciliation run", value: selectedReconciliation.strategyName },
            { id: "run-id", label: "Run ID", value: selectedReconciliation.runId }
          ]}
          savedViews={[
            {
              id: "controller-review",
              label: "Controller review",
              detail: "Default ledger explorer view for trial balance, proof drawer, approvals, and report usage.",
              active: true
            },
            {
              id: "exceptions",
              label: "Exceptions",
              detail: "Focuses the ledger grid on unreconciled accounts, blockers, and missing evidence."
            },
            {
              id: "report-usage",
              label: "Report usage",
              detail: "Keeps journal, ledger line, and report export proof paths visible together."
            }
          ]}
          summaryItems={[
            { id: "rows", label: "Rows", value: reconciliation.trialBalanceView.filteredRowCountLabel },
            { id: "basis", label: "Basis", value: reconciliation.trialBalanceView.basisOptions.find((option) => option.isSelected)?.label ?? "Primary" },
            { id: "breaks", label: "Open breaks", value: selectedReconciliationOpenBreakLabel, tone: selectedReconciliationOpenBreakTone },
            { id: "reconciliation", label: "Reconciliation", value: selectedReconciliation.reconciliationStatus, tone: selectedReconciliationOpenBreakTone }
          ]}
          appliedFilters={[
            { id: "account", label: "GL account", value: reconciliation.trialBalanceView.accountFilterValue.trim() || "All accounts" },
            { id: "basis-filter", label: "Accounting basis", value: reconciliation.trialBalanceView.basisOptions.find((option) => option.isSelected)?.label ?? "Primary" },
            { id: "run-filter", label: "Run", value: selectedReconciliation.runId }
          ]}
          actions={[
            {
              id: "evidence",
              label: reconciliation.detailActions?.evidencePacketLabel ?? "Open evidence packet",
              href: reconciliation.detailActions?.evidencePacketHref,
              ariaLabel: reconciliation.detailActions?.evidencePacketAriaLabel
            },
            {
              id: "audit",
              label: reconciliation.detailActions?.auditPacketLabel ?? "Review audit packet",
              href: reconciliation.detailActions?.auditPacketHref,
              ariaLabel: reconciliation.detailActions?.auditPacketAriaLabel
            }
          ]}
          explorer={ledgerExplorer}
          onSaveView={(request) => saveAccountingExplorerView("ledger", request)}
        >
        <div className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
          <Card aria-labelledby="trial-balance-title" aria-describedby="trial-balance-description" className="panel-surface">
            <CardHeader>
              <CardTitle id="trial-balance-title">{reconciliation.trialBalanceView.title}</CardTitle>
              <CardDescription id="trial-balance-description">{reconciliation.trialBalanceView.description}</CardDescription>
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
                <label htmlFor="ledger-account-filter" className="text-xs font-semibold uppercase text-muted-foreground">
                  {reconciliation.trialBalanceView.accountFilterLabel}
                </label>
                <div className="mt-2 flex flex-col gap-2 lg:flex-row lg:items-center">
                  <div className="relative min-w-0 flex-1">
                    <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
                    <input
                      id="ledger-account-filter"
                      type="search"
                      value={reconciliation.trialBalanceView.accountFilterValue}
                      onChange={(event) => reconciliation.updateLedgerAccountFilter(event.target.value)}
                      placeholder={reconciliation.trialBalanceView.accountFilterPlaceholder}
                      className="min-h-10 w-full rounded-md border border-border bg-background py-2 pl-9 pr-3 text-sm text-foreground outline-none transition focus:border-primary focus:ring-2 focus:ring-primary/25"
                    />
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="font-mono text-xs text-muted-foreground">{reconciliation.trialBalanceView.filteredRowCountLabel}</span>
                    {reconciliation.trialBalanceView.accountFilterValue.trim() ? (
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        onClick={() => reconciliation.updateLedgerAccountFilter("")}
                      >
                        {reconciliation.trialBalanceView.clearAccountFilterLabel}
                      </Button>
                    ) : null}
                  </div>
                </div>
                {reconciliation.trialBalanceView.accountFilterOptions.length > 0 ? (
                  <div className="mt-3 flex flex-wrap gap-2" role="group" aria-label="General Ledger account shortcuts">
                    {reconciliation.trialBalanceView.accountFilterOptions.map((option) => (
                      <Button
                        key={option.id}
                        type="button"
                        size="sm"
                        variant={option.isSelected ? "secondary" : "outline"}
                        aria-pressed={option.isSelected}
                        aria-label={`${option.label}, ${option.detail}, ${option.rowCountLabel}`}
                        onClick={() => reconciliation.updateLedgerAccountFilter(option.label)}
                      >
                        <span className="truncate">{option.label}</span>
                        <span className="ml-2 font-mono text-[10px] opacity-75">{option.rowCount}</span>
                      </Button>
                    ))}
                  </div>
                ) : null}
              </div>
              {reconciliation.trialBalanceView.hasRows ? (
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
                    <div id={reconciliation.trialBalanceView.detailPanelId} className="min-w-0">
                      <>
                        <EntitySummary
                          eyebrow={reconciliation.trialBalanceView.selectedDetail.eyebrow}
                          title={reconciliation.trialBalanceView.selectedDetail.title}
                          subtitle={reconciliation.trialBalanceView.selectedDetail.subtitle}
                          description={reconciliation.trialBalanceView.selectedDetail.description}
                          status={<Badge variant={reconciliation.trialBalanceView.selectedDetail.statusVariant} dot>{reconciliation.trialBalanceView.selectedDetail.statusLabel}</Badge>}
                          fields={reconciliation.trialBalanceView.selectedDetail.fields}
                          ariaLabel={reconciliation.trialBalanceView.selectedDetail.ariaLabel}
                        />
                        <div className="mt-3 flex flex-wrap gap-2" aria-label="Trial balance audit drill-through actions">
                          {reconciliation.trialBalanceView.selectedDetail.auditDrillThroughHref ? (
                            <Button asChild size="sm" variant="secondary">
                              <Link to={reconciliation.trialBalanceView.selectedDetail.auditDrillThroughHref}>
                                {reconciliation.trialBalanceView.selectedDetail.auditDrillThroughLabel}
                              </Link>
                            </Button>
                          ) : (
                            <span className="text-xs text-muted-foreground">{reconciliation.trialBalanceView.selectedDetail.auditDrillThroughLabel}</span>
                          )}
                          {reconciliation.trialBalanceView.selectedDetail.approvalDrillThroughHref ? (
                            <Button asChild size="sm" variant="outline">
                              <Link to={reconciliation.trialBalanceView.selectedDetail.approvalDrillThroughHref}>Open approval evidence</Link>
                            </Button>
                          ) : null}
                        </div>
                        <div className="mt-4 rounded-md border border-border/70 bg-background/60 p-3">
                          <h3 className="text-sm font-semibold text-foreground">{reconciliation.trialBalanceView.selectedDetail.ledgerLinesTitle}</h3>
                          <p className="mt-1 text-xs leading-5 text-muted-foreground">{reconciliation.trialBalanceView.selectedDetail.ledgerLinesDescription}</p>
                          {reconciliation.trialBalanceView.selectedDetail.ledgerLines.length > 0 ? (
                            <div className="mt-3 space-y-2" role="list" aria-label={reconciliation.trialBalanceView.selectedDetail.ledgerLinesTitle}>
                              {reconciliation.trialBalanceView.selectedDetail.ledgerLines.map((line) => (
                                <div key={line.rowId} role="listitem" className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2" aria-label={line.ariaLabel}>
                                  <div className="flex items-start justify-between gap-3">
                                    <span className="min-w-0">
                                      <span className="block truncate text-sm font-semibold text-foreground">{line.description}</span>
                                      <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{line.journalEntryId}</span>
                                    </span>
                                    <Badge variant="outline">{line.balanceLabel}</Badge>
                                  </div>
                                  <div className="mt-2 grid grid-cols-2 gap-2 text-[11px] text-muted-foreground">
                                    <span className="font-mono">Debit {line.debitLabel}</span>
                                    <span className="font-mono">Credit {line.creditLabel}</span>
                                  </div>
                                  <div className="mt-2 flex flex-wrap gap-2 text-xs">
                                    {line.evidenceHref ? (
                                      <Link className="text-primary underline-offset-2 hover:underline" to={line.evidenceHref}>
                                        {line.evidenceLabel}
                                      </Link>
                                    ) : (
                                      <span className="text-muted-foreground">{line.evidenceLabel}</span>
                                    )}
                                    {line.approvalHref ? (
                                      <Link className="text-primary underline-offset-2 hover:underline" to={line.approvalHref}>
                                        Approval evidence
                                      </Link>
                                    ) : null}
                                  </div>
                                </div>
                              ))}
                            </div>
                          ) : (
                            <p role="status" className="mt-3 rounded-md border border-border/70 bg-secondary/25 px-3 py-2 text-sm text-muted-foreground">
                              {reconciliation.trialBalanceView.selectedDetail.ledgerLinesEmptyText}
                            </p>
                          )}
                        </div>
                        <div className="mt-4 rounded-md border border-border/70 bg-background/60 p-3">
                          <h3 className="text-sm font-semibold text-foreground">{reconciliation.trialBalanceView.selectedDetail.supportingDocumentsTitle}</h3>
                          {reconciliation.trialBalanceView.selectedDetail.supportingDocuments.length > 0 ? (
                            <div className="mt-3 space-y-2" role="list" aria-label={reconciliation.trialBalanceView.selectedDetail.supportingDocumentsTitle}>
                              {reconciliation.trialBalanceView.selectedDetail.supportingDocuments.map((document) => (
                                <div key={document.id} role="listitem" className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
                                  <div className="text-sm font-semibold text-foreground">
                                    {document.href ? (
                                      document.href.startsWith("/accounting") ? (
                                        <Link className="text-primary underline-offset-2 hover:underline" to={document.href} aria-label={document.ariaLabel}>
                                          {document.label}
                                        </Link>
                                      ) : (
                                        <a className="text-primary underline-offset-2 hover:underline" href={document.href} target="_blank" rel="noreferrer" aria-label={document.ariaLabel}>
                                          {document.label}
                                        </a>
                                      )
                                    ) : (
                                      document.label
                                    )}
                                  </div>
                                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{document.detail}</p>
                                </div>
                              ))}
                            </div>
                          ) : (
                            <p role="status" className="mt-3 rounded-md border border-border/70 bg-secondary/25 px-3 py-2 text-sm text-muted-foreground">
                              {reconciliation.trialBalanceView.selectedDetail.supportingDocumentsEmptyText}
                            </p>
                          )}
                        </div>
                      </>
                    </div>
                  ) : (
                    <aside
                      id={reconciliation.trialBalanceView.detailPanelId}
                      role="region"
                      aria-label={reconciliation.trialBalanceView.detailEmptyAriaLabel}
                      data-selected-source="Selected from trial balance"
                      className="row-detail-panel h-fit min-w-0"
                    >
                      <div className="eyebrow-label">Trial-balance detail</div>
                      <h3 className="mt-1 text-sm font-semibold text-foreground">{reconciliation.trialBalanceView.detailEmptyTitle}</h3>
                      <p className="mt-2 text-sm leading-6 text-muted-foreground">{reconciliation.trialBalanceView.detailEmptyText}</p>
                    </aside>
                  )}
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
                  {reconciliation.trialBalanceView.errorDetails.length > 0 ? (
                    <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                      {reconciliation.trialBalanceView.errorDetails.map((detail) => (
                        <li key={detail}>{detail}</li>
                      ))}
                    </ul>
                  ) : null}
                </div>
              )}
              {reconciliation.trialBalanceView.loadingText && reconciliation.trialBalanceView.hasRows ? (
                <p role="status" className="mt-3 text-sm text-muted-foreground">
                  {reconciliation.trialBalanceView.loadingText}
                </p>
              ) : null}
              {reconciliation.trialBalanceView.errorText && reconciliation.trialBalanceView.hasRows ? (
                <div role="alert" className="mt-3 rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  <div>{reconciliation.trialBalanceView.errorText}</div>
                  {reconciliation.trialBalanceView.errorDetails.length > 0 ? (
                    <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                      {reconciliation.trialBalanceView.errorDetails.map((detail) => (
                        <li key={detail}>{detail}</li>
                      ))}
                    </ul>
                  ) : null}
                </div>
              ) : null}
            </CardContent>
          </Card>
          <Card className="panel-surface">
            <CardHeader>
              <CardTitle>{reconciliation.trialBalanceView.basisBridge.title}</CardTitle>
              <CardDescription>{reconciliation.trialBalanceView.basisBridge.description}</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div role="region" aria-label={reconciliation.trialBalanceView.basisBridge.tableLabel}>
                {reconciliation.trialBalanceView.basisBridge.hasRows ? (
                  <div className="space-y-2">
                    {reconciliation.trialBalanceView.basisBridge.rows.map((row) => (
                      <div key={row.rowId} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2" aria-label={row.ariaLabel}>
                        <div className="flex items-start justify-between gap-3">
                          <span className="min-w-0">
                            <span className="block truncate text-sm font-semibold text-foreground">{row.accountLabel}</span>
                            <span className="mt-1 block text-xs text-muted-foreground">{row.sourceLabel}</span>
                          </span>
                          <Badge variant={row.varianceTone}>{row.varianceLabel}</Badge>
                        </div>
                        <div className="mt-2 grid grid-cols-2 gap-2 text-[11px] text-muted-foreground">
                          <span className="font-mono">Primary {row.primaryBalanceLabel}</span>
                          <span className="font-mono">{row.comparisonBalanceLabel}</span>
                        </div>
                      </div>
                    ))}
                  </div>
                ) : (
                  <p role="status" className="rounded-md border border-border/70 bg-secondary/25 px-3 py-2 text-sm leading-6 text-muted-foreground">
                    {reconciliation.trialBalanceView.basisBridge.emptyText}
                  </p>
                )}
              </div>
              <div className="border-t border-border/70 pt-4">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <h3 className="text-sm font-semibold text-foreground">{reconciliation.transactionLabView.title}</h3>
                    <p className="mt-1 text-sm leading-6 text-muted-foreground">{reconciliation.transactionLabView.description}</p>
                  </div>
                  <Badge variant={reconciliation.transactionLabView.statusTone === "default" ? "outline" : reconciliation.transactionLabView.statusTone} dot>
                    {reconciliation.transactionLabView.requestSummaryLabel}
                  </Badge>
                </div>
                <p
                  role={reconciliation.transactionLabView.statusRole}
                  className={cn(
                    "mt-3 rounded-md border px-3 py-2 text-sm",
                    reconciliation.transactionLabView.statusTone === "default" ? "border-border/70 bg-secondary/25 text-muted-foreground" : "",
                    reconciliation.transactionLabView.statusTone === "success" ? "border-success/30 bg-success/10 text-success" : "",
                    reconciliation.transactionLabView.statusTone === "warning" ? "border-warning/30 bg-warning/10 text-warning" : "",
                    reconciliation.transactionLabView.statusTone === "danger" ? "border-danger/30 bg-danger/10 text-danger" : ""
                  )}
                >
                  {reconciliation.transactionLabView.statusText}
                </p>
                <div className="mt-3 grid grid-cols-2 gap-2 text-xs">
                  <div className="rounded-md border border-border/70 bg-background px-3 py-2">
                    <span className="block text-muted-foreground">Journal</span>
                    <span className="mt-1 block font-mono text-foreground">{reconciliation.transactionLabView.journalLineCountLabel}</span>
                  </div>
                  <div className="rounded-md border border-border/70 bg-background px-3 py-2">
                    <span className="block text-muted-foreground">Ledger impact</span>
                    <span className="mt-1 block font-mono text-foreground">{reconciliation.transactionLabView.ledgerImpactLabel}</span>
                  </div>
                  <div className="rounded-md border border-border/70 bg-background px-3 py-2">
                    <span className="block text-muted-foreground">Reconciliation</span>
                    <span className="mt-1 block font-mono text-foreground">{reconciliation.transactionLabView.reconciliationLabel}</span>
                  </div>
                  <div className="rounded-md border border-border/70 bg-background px-3 py-2">
                    <span className="block text-muted-foreground">Evidence</span>
                    <span className="mt-1 block font-mono text-foreground">{reconciliation.transactionLabView.evidenceLabel}</span>
                  </div>
                </div>
                {reconciliation.transactionLabView.impactRows.length > 0 ? (
                  <div className="mt-3 space-y-2" aria-label="Transaction Lab trial-balance impact">
                    {reconciliation.transactionLabView.impactRows.map((row) => (
                      <div key={row.id} className="flex items-center justify-between gap-3 rounded-md border border-border/70 bg-secondary/20 px-3 py-2 text-sm">
                        <span className="min-w-0 truncate text-foreground">{row.label}</span>
                        <Badge variant={row.tone === "default" ? "outline" : row.tone}>{row.value}</Badge>
                      </div>
                    ))}
                  </div>
                ) : null}
                <Button
                  type="button"
                  variant="outline"
                  className="mt-3 w-full"
                  disabled={!reconciliation.transactionLabView.canPreview}
                  disabledReason={reconciliation.transactionLabView.disabledReason}
                  busy={reconciliation.transactionLabView.busy}
                  busyLabel={reconciliation.transactionLabView.previewButtonLabel}
                  aria-label={reconciliation.transactionLabView.previewButtonAriaLabel}
                  onClick={() => void reconciliation.runTransactionLabPreview()}
                >
                  {reconciliation.transactionLabView.previewButtonLabel}
                </Button>
              </div>
              <div className="border-t border-border/70 pt-4">
                <h3 className="text-sm font-semibold text-foreground">Reporting exports</h3>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">Entry points for report/export handoff using existing export infrastructure.</p>
              </div>
              <Button asChild>
                <a href={reporting.backendLinks[0].href} target="_blank" rel="noreferrer" aria-label={reporting.backendLinks[0].ariaLabel}>
                  {reporting.backendLinks[0].label}
                </a>
              </Button>
              <Button
                type="button"
                variant="outline"
                disabled={!reporting.exportCanRun}
                disabledReason={reporting.exportDisabledReason}
                busy={reporting.exportBusy}
                busyLabel={reporting.exportButtonLabel}
                aria-label={reporting.exportAriaLabel}
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
        </div>
        </FinancialRecordExplorerShell>
      ) : null}

      <section id="accounting-reporting" className={cn("grid gap-4", workstream === "reconciliation" ? "xl:grid-cols-1" : "xl:grid-cols-[1.15fr_0.85fr]")}>
        {workstream !== "reconciliation" ? (
          <ReconciliationQueueSummaryCard view={reconciliation.queuePanelView} />
        ) : null}

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
        <FinancialRecordExplorerShell
          explorerLabel="Financial Record Explorer"
          title="Security & Instrument Explorer"
          titleId="accounting-security-master-explorer-title"
          description="Search Security Master instruments, inspect selected identity detail, and keep identifier conflicts, schedules, open lots, trading controls, and proof drill-through actions attached to the active record."
          scopeItems={[
            { id: "workspace", label: "Workspace", value: "Accounting" },
            { id: "record-set", label: "Record set", value: "Security Master instruments" },
            { id: "selected", label: "Selected instrument", value: securityMaster.pageView.detailSubtitle },
            { id: "status", label: "Record status", value: securityMaster.pageView.detailStatusLabel }
          ]}
          savedViews={[
            {
              id: "instrument-proof",
              label: "Instrument proof",
              detail: "Default Security Master explorer view for search, identity evidence, conflicts, schedules, lots, and controls.",
              active: true
            },
            {
              id: "identifier-conflicts",
              label: "Identifier conflicts",
              detail: "Focuses operator review on provider identifier conflicts and resolution proof."
            },
            {
              id: "lot-schedule-review",
              label: "Lots & schedules",
              detail: "Keeps cash-flow schedules, open lots, trading controls, and audit cues visible for the selected instrument."
            }
          ]}
          summaryItems={securityMaster.pageView.metrics.map((metric) => ({
            id: metric.id,
            label: metric.label,
            value: metric.value,
            tone: metric.tone
          }))}
          appliedFilters={[
            { id: "query", label: "Search", value: securityMaster.query.trim() || "No query" },
            { id: "selection", label: "Security ID", value: securityMaster.selectedSecurityId ?? "No selection" },
            { id: "conflicts", label: "Conflicts", value: securityMaster.conflictsLoading ? "Loading" : securityMaster.conflictCountLabel },
            { id: "detail", label: "Detail coverage", value: securityMaster.pageView.detailSections.map((section) => `${section.label}: ${section.value}`).join(" | ") }
          ]}
          actions={[
            {
              id: "search",
              label: "Open search",
              href: "#security-master-search",
              ariaLabel: "Open Security Master search"
            },
            {
              id: "identity",
              label: identity ? "Open identity proof" : "Identity proof pending",
              href: identity ? `#${identity.panelId}` : null,
              ariaLabel: identity ? "Open selected security identity proof" : undefined
            },
            {
              id: "detail",
              label: securityMaster.selectedSecurityId ? "Open selected record" : "Select a record",
              href: securityMaster.selectedSecurityId ? "#security-detail-page-title" : null,
              ariaLabel: securityMaster.selectedSecurityId ? "Open selected Security Master record detail" : undefined
            }
          ]}
          explorer={securityInstrumentExplorer}
          onSaveView={(request) => saveAccountingExplorerView("security-instrument", request)}
        >
        <section className="space-y-6">
          <section className="panel-surface-strong space-y-4 p-5" aria-label={securityMaster.pageView.ariaLabel}>
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div className="max-w-3xl">
                <div className="eyebrow-label">{securityMaster.pageView.eyebrow}</div>
                <h2 className="mt-2 text-2xl font-semibold tracking-normal text-foreground">{securityMaster.pageView.title}</h2>
                <p className="mt-2 text-sm leading-6 text-muted-foreground">{securityMaster.pageView.description}</p>
              </div>
              <Button variant="outline" size="sm" asChild className="shrink-0">
                <a href="#security-master-search" aria-label="Jump to Security Master search">
                  <Search className="h-3.5 w-3.5" aria-hidden="true" />
                  Search securities
                </a>
              </Button>
            </div>
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              {securityMaster.pageView.metrics.map((metric) => (
                <div key={metric.id} className="rounded-lg border border-border/60 bg-secondary/25 p-4">
                  <div className="text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">{metric.label}</div>
                  <div
                    className={cn(
                      "mt-2 min-w-0 break-words font-mono text-lg font-semibold tabular-nums",
                      metric.tone === "success" ? "text-success" : metric.tone === "warning" ? "text-warning" : "text-foreground"
                    )}
                  >
                    {metric.value}
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{metric.detail}</p>
                </div>
              ))}
            </div>
          </section>

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
                  <div>{securityMaster.searchErrorText}</div>
                  {securityMaster.searchErrorDetails.length > 0 ? (
                    <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                      {securityMaster.searchErrorDetails.map((detail) => (
                        <li key={detail}>{detail}</li>
                      ))}
                    </ul>
                  ) : null}
                </div>
              )}

              {securityMaster.hasResults && (
                <DenseDataTable
                  columns={securityResultColumns}
                  rows={securityMaster.resultRows}
                  getRowId={(row) => row.rowId}
                  getRowAriaLabel={(row) => row.ariaLabel}
                  getRowSelectAriaLabel={(row) => row.selectAriaLabel}
                  getRowAriaControls={(row) => row.detailPanelId}
                  getRowAriaExpanded={(row) => row.isExpanded}
                  onRowSelect={(row) => void securityMaster.selectSecurity(row.securityId)}
                  selectedRowId={securityMaster.selectedSecurityId ? `security-result-${securityMaster.selectedSecurityId}` : null}
                  emptyText={securityMaster.searchStatusText ?? "No Security Master results returned."}
                  ariaLabel={securityMaster.resultsTableLabel}
                  tableId="security-master-results"
                  caption={securityMaster.searchStatusText}
                />
              )}
              <div id={identity?.panelId ?? SECURITY_IDENTITY_DETAIL_PANEL_ID} className="space-y-4">
                {securityMaster.identityLoading && (
                  <p role="status" className="rounded-md border border-[var(--state-pending-bd)] bg-[var(--state-pending-bg)] px-3 py-3 text-sm text-[var(--state-pending-fg)]">
                    Loading identity drill-in…
                  </p>
                )}
                {securityMaster.identityErrorText && (
                  <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                    <div>{securityMaster.identityErrorText}</div>
                    {securityMaster.identityErrorDetails.length > 0 ? (
                      <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                        {securityMaster.identityErrorDetails.map((detail) => (
                          <li key={detail}>{detail}</li>
                        ))}
                      </ul>
                    ) : null}
                  </div>
                )}
                {identity && (
                  <>
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
                  </>
                )}
              </div>
            </CardContent>
          </Card>

          {/* Conflicts panel */}
          <Card className="panel-surface">
            <CardHeader>
              <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div className="min-w-0">
                  <CardTitle className="flex flex-wrap items-center gap-2">
                    <ShieldCheck className="h-5 w-5 text-primary" />
                    Identifier conflicts
                    {securityMaster.openConflictCount > 0 && (
                      <span className="inline-flex items-center rounded-sm border border-warning/35 bg-warning/10 px-2 py-0.5 font-mono text-[10px] font-medium uppercase tracking-[0.12em] text-warning">
                        {securityMaster.conflictCountLabel}
                      </span>
                    )}
                  </CardTitle>
                  <CardDescription className="mt-2">
                    Identifier ambiguities detected when multiple providers map the same identifier to different securities.
                  </CardDescription>
                </div>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  busy={securityMaster.conflictRefreshCommand.busy}
                  busyLabel={securityMaster.conflictRefreshCommand.busyLabel}
                  disabled={securityMaster.conflictRefreshCommand.disabled}
                  disabledReason={securityMaster.conflictRefreshCommand.disabledReason}
                  aria-label={securityMaster.conflictRefreshCommand.ariaLabel}
                  aria-describedby={securityMaster.conflictRefreshCommand.feedbackText ? securityMaster.conflictRefreshCommand.feedbackId : undefined}
                  onClick={() => void securityMaster.refreshConflicts()}
                  className="shrink-0"
                >
                  <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
                  {securityMaster.conflictRefreshCommand.label}
                </Button>
              </div>
            </CardHeader>
            <CardContent>
              {securityMaster.conflictRefreshCommand.feedbackText && (
                <p
                  id={securityMaster.conflictRefreshCommand.feedbackId}
                  role="status"
                  className="mb-3 rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning"
                >
                  {securityMaster.conflictRefreshCommand.feedbackText}
                </p>
              )}
              {securityMaster.conflictsLoading && <p role="status" className="text-sm text-muted-foreground">Loading conflicts…</p>}
              {securityMaster.conflictsErrorText && (
                <div role="alert" className="mb-3 rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  <div>{securityMaster.conflictsErrorText}</div>
                  {securityMaster.conflictsErrorDetails.length > 0 && (
                    <ul className="mt-2 list-disc pl-5">
                      {securityMaster.conflictsErrorDetails.map((detail) => (
                        <li key={detail}>{detail}</li>
                      ))}
                    </ul>
                  )}
                </div>
              )}
              {securityMaster.conflictActionErrorText && (
                <div role="alert" className="mb-3 rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  <div>{securityMaster.conflictActionErrorText}</div>
                  {securityMaster.conflictActionErrorDetails.length > 0 && (
                    <ul className="mt-2 list-disc pl-5">
                      {securityMaster.conflictActionErrorDetails.map((detail) => (
                        <li key={detail}>{detail}</li>
                      ))}
                    </ul>
                  )}
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
                                disabledReason={action.disabledReason}
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

          {securityMaster.selectedSecurityId && (
            <section className="panel-surface-strong space-y-4 p-5" aria-labelledby="security-detail-page-title">
              <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
                <div className="min-w-0">
                  <div className="eyebrow-label">{securityMaster.pageView.detailEyebrow}</div>
                  <h2 id="security-detail-page-title" className="mt-2 text-xl font-semibold tracking-normal text-foreground">
                    {securityMaster.pageView.detailTitle}
                  </h2>
                  <p className="mt-1 break-words font-mono text-xs uppercase tracking-[0.12em] text-muted-foreground">
                    {securityMaster.pageView.detailSubtitle}
                  </p>
                  <p className="mt-2 max-w-4xl text-sm leading-6 text-muted-foreground">
                    {securityMaster.pageView.detailDescription}
                  </p>
                </div>
                <Badge variant={securityMaster.pageView.detailStatusBadgeVariant} dot className="w-fit shrink-0">
                  {securityMaster.pageView.detailStatusLabel}
                </Badge>
              </div>
              <ToolbarStrip
                ariaLabel={securityMaster.pageView.detailToolbarAriaLabel}
                items={securityMaster.pageView.detailSections}
              />
            </section>
          )}

          {/* Schedule workbench, corporate actions, and trading controls — shown when a security is selected */}
          {securityMaster.selectedSecurityId && (
            <>
              <SecuritySchedulesPanel
                view={securityMaster.schedulesView}
                onSelect={securityMaster.selectScheduleEvent}
              />
              <SecurityOpenLotReadModelPanel
                view={securityMaster.openLotReadModelView}
                onSelect={securityMaster.selectOpenLot}
              />
              <div className="grid gap-4 xl:grid-cols-2">
                <CorporateActionsPanel
                  view={securityMaster.corporateActionsView}
                  onSelect={securityMaster.selectCorporateAction}
                />
                <TradingParametersPanel view={securityMaster.tradingParametersView} />
              </div>
            </>
          )}

          {/* Extended security details & lots tracker — shown when a security is selected */}
          {securityMaster.selectedSecurityId && (
            <>
              <SecurityDetailsPanel
                entry={selectedSecurityEntry}
                identity={securityMaster.identity}
                tradingParameters={securityMaster.tradingParameters}
              />
              <LotsTrackerPanel
                securityId={securityMaster.selectedSecurityId}
                currency={selectedSecurityEntry?.economicDefinition.currency ?? null}
              />
            </>
          )}
        </section>
        </FinancialRecordExplorerShell>
      )}

      {(workstream === "reconciliation" || workstream === "exceptions") && (
        <section id="accounting-actions" className="workspace-section-band" aria-labelledby="accounting-actions-heading">
          <div className="workspace-section-subheader">
            <div className="min-w-0">
              <p className="eyebrow-label">Actions</p>
              <h3 id="accounting-actions-heading" className="workspace-section-title">Break resolution actions</h3>
              <p className="workspace-section-summary">Resolve, dismiss, route, and calibration actions use a shared action placement.</p>
            </div>
            <a className="workspace-section-jump" href="#accounting-history">History</a>
          </div>
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
                  <div>{reconciliation.errorText}</div>
                  {reconciliation.errorDetails.length > 0 ? (
                    <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                      {reconciliation.errorDetails.map((detail) => (
                        <li key={detail}>{detail}</li>
                      ))}
                    </ul>
                  ) : null}
                </div>
              )}
              {reconciliation.actionErrorText && (
                <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  <div>{reconciliation.actionErrorText}</div>
                  {reconciliation.actionErrorDetails.length > 0 ? (
                    <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                      {reconciliation.actionErrorDetails.map((detail) => (
                        <li key={detail}>{detail}</li>
                      ))}
                    </ul>
                  ) : null}
                </div>
              )}
              <DenseDataTable
                columns={reconciliationBreakTableColumns}
                rows={reconciliation.rows}
                getRowId={(item) => item.breakId}
                getRowAriaLabel={(item) => item.rowAriaLabel}
                getRowSelectAriaLabel={(item) => item.rowSelectAriaLabel}
                getRowAriaControls={(item) => item.detailPanelId}
                getRowAriaExpanded={(item) => item.isExpanded}
                onRowSelect={(item) => reconciliation.selectBreak(item.breakId)}
                selectedRowId={reconciliation.selectedBreakId}
                emptyText={reconciliation.emptyText}
                ariaLabel={reconciliation.tableLabel}
                caption={reconciliation.tableCaption}
              />
              <div id={reconciliation.detailPanelId} aria-live="polite" data-selected-source="Selected from break queue" className="row-detail-panel">
                {reconciliation.selectedDetail ? (
                  <EntitySummary
                    eyebrow={reconciliation.selectedDetail.eyebrow}
                    title={reconciliation.selectedDetail.title}
                    subtitle={reconciliation.selectedDetail.subtitle}
                    description={reconciliation.selectedDetail.description}
                    ariaLabel={reconciliation.selectedDetail.ariaLabel}
                    fields={reconciliation.selectedDetail.fields}
                    status={<Badge variant={reconciliation.selectedDetail.statusBadgeVariant}>{reconciliation.selectedDetail.statusLabel}</Badge>}
                  />
                ) : (
                  <section
                    role="region"
                    aria-label={reconciliation.detailEmptyAriaLabel}
                    className="rounded-lg border border-border/70 bg-secondary/20 px-4 py-3 text-sm text-muted-foreground"
                  >
                    <div className="eyebrow-label">{reconciliation.detailEmptyTitle}</div>
                    <p className="mt-1">{reconciliation.detailEmptyText}</p>
                  </section>
                )}
              </div>
              {reconciliation.selectedDetail?.analysisText ? (
                <div className="rounded-md border border-border/50 bg-secondary/20 px-3 py-2 text-xs leading-5 text-muted-foreground">
                  <span className="font-medium text-foreground">Analysis: </span>
                  {reconciliation.selectedDetail.analysisText}
                </div>
              ) : null}
              {reconciliation.selectedDetail?.recommendedActionText ? (
                <div className="rounded-md border border-primary/20 bg-primary/5 px-3 py-2 text-xs leading-5">
                  <span className="font-medium text-primary">Recommended: </span>
                  <span className="text-foreground">{reconciliation.selectedDetail.recommendedActionText}</span>
                </div>
              ) : null}
              {reconciliation.selectedDetail?.routingActionHref && reconciliation.selectedDetail.routingActionLabel ? (
                <Button asChild variant="secondary" className="w-fit">
                  <Link
                    to={reconciliation.selectedDetail.routingActionHref}
                    aria-label={reconciliation.selectedDetail.routingActionAriaLabel ?? reconciliation.selectedDetail.routingActionLabel}
                  >
                    <Network className="h-4 w-4" aria-hidden="true" />
                    {reconciliation.selectedDetail.routingActionLabel}
                  </Link>
                </Button>
              ) : null}
              {activeResolveBreak && resolveDialog.active ? (
                    <form
                      className="space-y-2 rounded-lg border border-border/50 bg-secondary/20 p-3"
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
                      <div className="panel-action-zone justify-start">
                        <Button
                          type="submit"
                          size="sm"
                          disabled={resolveDialog.active.isSubmitDisabled}
                          disabledReason={resolveDialog.active.submitDisabledReason}
                          aria-label={resolveDialog.active.submitAriaLabel}
                        >
                          {resolveDialog.active.submitLabel}
                        </Button>
                        <Button type="button" size="sm" variant="ghost" aria-label={resolveDialog.active.cancelAriaLabel} onClick={resolveDialog.close}>
                          {resolveDialog.active.cancelLabel}
                        </Button>
                      </div>
                    </form>
              ) : null}
            </CardContent>
          </Card>

          <CalibrationSummaryPanel view={reconciliation.calibrationView} />
        </section>
        </section>
      )}
    </div>
  );
}

function CalibrationSummaryPanel({ view }: { view: CalibrationSummaryViewModel }) {
  const StatusIcon = view.statusIcon === "check" ? CheckCircle2 : AlertCircle;

  return (
    <Card id="accounting-history" className="panel-surface">
      <CardHeader className="gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <CardTitle className="flex items-center gap-2 text-base">
            <BookCheck className="h-4 w-4 text-primary" />
            Calibration summary
          </CardTitle>
          <CardDescription>Tolerance profile health, break trend, auto-match rate, and T+0 closure rate across active reconciliation routes.</CardDescription>
        </div>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={view.refresh}
          disabled={view.refreshCommand.disabled}
          disabledReason={view.refreshCommand.disabledReason}
          aria-label={view.refreshCommand.ariaLabel}
          className="shrink-0"
        >
          <RefreshCcw className="mr-2 h-3.5 w-3.5" aria-hidden="true" />
          {view.refreshCommand.label}
        </Button>
      </CardHeader>
      <CardContent className="space-y-4">
        <span className="sr-only" aria-live="polite">{view.statusAnnouncement}</span>
        {view.loadingText && <p role="status" className="text-sm text-muted-foreground">{view.loadingText}</p>}
        {view.errorText && (
          <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
            <div>{view.errorText}</div>
            {view.errorDetails.length > 0 ? (
              <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                {view.errorDetails.map((detail) => (
                  <li key={detail}>{detail}</li>
                ))}
              </ul>
            ) : null}
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
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-9">
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
            <div>
              <div className="mb-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{view.profilesLabel}</div>
              {view.hasProfiles ? (
                <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(280px,360px)]">
                  <DenseDataTable
                    columns={calibrationProfileColumns}
                    rows={view.profileRows}
                    getRowId={(row) => row.toleranceProfileId}
                    getRowAriaLabel={(row) => row.ariaLabel}
                    getRowSelectAriaLabel={(row) => row.selectAriaLabel}
                    getRowAriaControls={(row) => row.detailPanelId}
                    getRowAriaExpanded={(row) => row.isSelected}
                    selectedRowId={view.selectedProfileId}
                    onRowSelect={(row) => view.selectProfile(row.toleranceProfileId)}
                    emptyText={view.emptyText}
                    ariaLabel={view.tableAriaLabel}
                  />
                  <div id={view.detailPanelId} aria-live="polite">
                    {view.selectedProfile ? (
                      <EntitySummary
                        eyebrow="Tolerance profile"
                        title={view.selectedProfile.title}
                        subtitle={view.selectedProfile.subtitle}
                        description={view.selectedProfile.description}
                        status={<Badge variant={view.selectedProfile.statusTone} dot>{view.selectedProfile.statusLabel}</Badge>}
                        fields={view.selectedProfile.fields}
                        ariaLabel={view.selectedProfile.ariaLabel}
                      />
                    ) : (
                      <div role="status" className="rounded-lg border border-border/70 bg-secondary/25 px-4 py-3 text-sm text-muted-foreground">
                        Select a tolerance profile to inspect its calibration posture.
                      </div>
                    )}
                  </div>
                </div>
              ) : (
                <div role="status" className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning">
                  {view.emptyText}
                </div>
              )}
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}

function CorporateActionsPanel({
  view,
  onSelect
}: {
  view: CorporateActionsViewState;
  onSelect: (rowId: string) => void;
}) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Table2 className="h-4 w-4 text-primary" />
          Corporate actions
        </CardTitle>
        <CardDescription>
          Dividends, splits, spin-offs, and other corporate events for <span className="font-mono">{view.securityId}</span>.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <span className="sr-only" aria-live="polite">{view.statusAnnouncement}</span>
        {view.loadingText && <p role="status" className="text-sm text-muted-foreground">{view.loadingText}</p>}
        {view.errorText && (
          <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
            <div>{view.errorText}</div>
            {view.errorDetails.length > 0 ? (
              <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                {view.errorDetails.map((detail) => (
                  <li key={detail}>{detail}</li>
                ))}
              </ul>
            ) : null}
          </div>
        )}
        {!view.loadingText && !view.errorText && (
          <div className="grid gap-4 2xl:grid-cols-[minmax(0,1.35fr)_minmax(18rem,0.65fr)]">
            <DenseDataTable
              columns={corporateActionColumns}
              rows={view.rows}
              getRowId={(row) => row.rowId}
              getRowAriaLabel={(row) => row.ariaLabel}
              getRowSelectAriaLabel={(row) => row.selectAriaLabel}
              getRowAriaControls={(row) => row.detailPanelId}
              getRowAriaExpanded={(row) => row.isExpanded}
              onRowSelect={(row) => onSelect(row.rowId)}
              selectedRowId={view.selectedRowId}
              emptyText={view.emptyText}
              ariaLabel={view.tableLabel}
              caption={view.tableCaption}
            />
            <div
              id={view.detailPanelId}
              data-selected-source="Selected from corporate actions"
              className="row-detail-panel h-fit min-w-0"
            >
              {view.selectedDetail ? (
                <EntitySummary
                  eyebrow={view.selectedDetail.eyebrow}
                  title={view.selectedDetail.title}
                  subtitle={view.selectedDetail.subtitle}
                  description={view.selectedDetail.description}
                  ariaLabel={view.selectedDetail.ariaLabel}
                  status={<Badge variant={view.selectedDetail.statusLabel === "Pay date scheduled" ? "success" : "warning"}>{view.selectedDetail.statusLabel}</Badge>}
                  fields={view.selectedDetail.fields.map((field) => ({ label: field.label, value: field.value }))}
                />
              ) : (
                <div role="region" aria-label={view.detailEmptyAriaLabel}>
                  <div className="eyebrow-label">Corporate action detail</div>
                  <h3 className="mt-2 text-sm font-semibold text-foreground">{view.detailEmptyTitle}</h3>
                  <p className="mt-2 text-sm leading-6 text-muted-foreground">{view.detailEmptyText}</p>
                </div>
              )}
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function SecuritySchedulesPanel({
  view,
  onSelect
}: {
  view: SecuritySchedulesViewState;
  onSelect: (rowId: string) => void;
}) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <CardTitle className="flex items-center gap-2 text-base">
              <Table2 className="h-4 w-4 text-primary" aria-hidden="true" />
              {view.title}
            </CardTitle>
            <CardDescription className="mt-2">{view.description}</CardDescription>
          </div>
          <div className="min-w-0 lg:max-w-[28rem]">
            <ToolbarStrip ariaLabel={view.toolbarAriaLabel} items={view.toolbarItems} />
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <span className="sr-only" aria-live="polite">{view.statusAnnouncement}</span>
        {view.loadingText && <p role="status" className="text-sm text-muted-foreground">{view.loadingText}</p>}
        {view.errorText && (
          <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
            <div>{view.errorText}</div>
            {view.errorDetails.length > 0 ? (
              <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                {view.errorDetails.map((detail) => (
                  <li key={detail}>{detail}</li>
                ))}
              </ul>
            ) : null}
          </div>
        )}
        {!view.loadingText && !view.errorText && (
          <div className="grid gap-4 2xl:grid-cols-[minmax(0,1.45fr)_minmax(20rem,0.55fr)]">
          <DenseDataTable
            columns={securityScheduleColumns}
            rows={view.rows}
            getRowId={(row) => row.rowId}
            getRowAriaLabel={(row) => row.ariaLabel}
            getRowSelectAriaLabel={(row) => row.selectAriaLabel}
            getRowAriaControls={(row) => row.detailPanelId}
            getRowAriaExpanded={(row) => row.isExpanded}
            onRowSelect={(row) => onSelect(row.rowId)}
            selectedRowId={view.selectedRowId}
            emptyText={view.emptyText}
            ariaLabel={view.tableLabel}
            caption={view.tableCaption}
          />
          <div id={view.detailPanelId} data-selected-source="Selected from schedule events" className="row-detail-panel h-fit min-w-0">
            {view.selectedDetail ? (
              <EntitySummary
                eyebrow={view.selectedDetail.eyebrow}
                title={view.selectedDetail.title}
                subtitle={view.selectedDetail.subtitle}
                description={view.selectedDetail.description}
                ariaLabel={view.selectedDetail.ariaLabel}
                status={<Badge variant={view.selectedDetail.statusTone}>{view.selectedDetail.statusLabel}</Badge>}
                fields={view.selectedDetail.fields.map((field) => ({ label: field.label, value: field.value }))}
              />
            ) : (
              <div role="region" aria-label={view.detailEmptyAriaLabel}>
                <div className="eyebrow-label">Schedule event detail</div>
                <h3 className="mt-2 text-sm font-semibold text-foreground">{view.detailEmptyTitle}</h3>
                <p className="mt-2 text-sm leading-6 text-muted-foreground">{view.detailEmptyText}</p>
              </div>
            )}
          </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function SecurityOpenLotReadModelPanel({
  view,
  onSelect
}: {
  view: SecurityOpenLotReadModelViewState;
  onSelect: (rowId: string) => void;
}) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <CardTitle className="flex items-center gap-2 text-base">
              <Briefcase className="h-4 w-4 text-primary" aria-hidden="true" />
              {view.title}
            </CardTitle>
            <CardDescription className="mt-2">
              {view.description}
              {view.asOfLabel !== "—" ? <> As of {view.asOfLabel}.</> : null}
            </CardDescription>
          </div>
          <div className="min-w-0 lg:max-w-[28rem]">
            <ToolbarStrip ariaLabel={view.toolbarAriaLabel} items={view.toolbarItems} />
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <span className="sr-only" aria-live="polite">{view.statusAnnouncement}</span>
        {view.loadingText && <p role="status" className="text-sm text-muted-foreground">{view.loadingText}</p>}
        {view.errorText && (
          <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
            <div>{view.errorText}</div>
            {view.errorDetails.length > 0 ? (
              <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                {view.errorDetails.map((detail) => (
                  <li key={detail}>{detail}</li>
                ))}
              </ul>
            ) : null}
          </div>
        )}
        {!view.loadingText && !view.errorText && (
          <>
            <p className="text-sm leading-6 text-muted-foreground">{view.summary}</p>
            <div className="grid gap-4 2xl:grid-cols-[minmax(0,1.45fr)_minmax(20rem,0.55fr)]">
              <DenseDataTable
                columns={securityOpenLotColumns}
                rows={view.rows}
                getRowId={(row) => row.rowId}
                getRowAriaLabel={(row) => row.ariaLabel}
                getRowSelectAriaLabel={(row) => row.selectAriaLabel}
                getRowAriaControls={(row) => row.detailPanelId}
                getRowAriaExpanded={(row) => row.isExpanded}
                onRowSelect={(row) => onSelect(row.rowId)}
                selectedRowId={view.selectedRowId}
                emptyText={view.emptyText}
                ariaLabel={view.tableLabel}
                caption={view.tableCaption}
              />
              <div id={view.detailPanelId} data-selected-source="Selected from open lots" className="row-detail-panel h-fit min-w-0">
                {view.selectedDetail ? (
                  <EntitySummary
                    eyebrow={view.selectedDetail.eyebrow}
                    title={view.selectedDetail.title}
                    subtitle={view.selectedDetail.subtitle}
                    description={view.selectedDetail.description}
                    ariaLabel={view.selectedDetail.ariaLabel}
                    status={<Badge variant={view.selectedDetail.statusTone}>{view.selectedDetail.statusLabel}</Badge>}
                    fields={view.selectedDetail.fields.map((field) => ({ label: field.label, value: field.value }))}
                  />
                ) : (
                  <div role="region" aria-label={view.detailEmptyAriaLabel}>
                    <div className="eyebrow-label">Open lot detail</div>
                    <h3 className="mt-2 text-sm font-semibold text-foreground">{view.detailEmptyTitle}</h3>
                    <p className="mt-2 text-sm leading-6 text-muted-foreground">{view.detailEmptyText}</p>
                  </div>
                )}
              </div>
            </div>
          </>
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
            <div>{view.errorText}</div>
            {view.errorDetails.length > 0 ? (
              <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                {view.errorDetails.map((detail) => (
                  <li key={detail}>{detail}</li>
                ))}
              </ul>
            ) : null}
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

function ReconciliationQueueSummaryCard({ view }: { view: ReconciliationQueuePanelViewState }) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle className="flex items-center gap-2 text-base">
              <BookCheck className="h-4 w-4 text-primary" aria-hidden="true" />
              {view.overviewTitle}
            </CardTitle>
            <CardDescription className="mt-2">{view.overviewDescription}</CardDescription>
          </div>
          <Button asChild variant="outline" size="sm" className="w-fit shrink-0">
            <Link to={view.overviewActionHref} aria-label={view.overviewActionAriaLabel}>
              {view.overviewActionLabel}
            </Link>
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        <DenseDataTable
          columns={reconciliationQueueColumns}
          rows={view.rows}
          getRowId={(row) => row.runId}
          getRowAriaLabel={(row) => row.ariaLabel}
          emptyText={view.emptyText}
          ariaLabel={view.listLabel}
          caption={view.overviewCaption}
        />
      </CardContent>
    </Card>
  );
}

function CloseCommandCenterPanel({
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
              <AccountingValue label="Fund account" value={view.fundAccountLabel} />
              <AccountingValue label="Updated" value={view.updatedLabel} />
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

function OperationalExceptionWorkbenchPanel({ view }: { view: OperationalExceptionWorkbenchViewState }) {
  return (
    <section id="accounting-exceptions" className="workspace-section-band" aria-labelledby="accounting-exceptions-heading">
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <p className="eyebrow-label">Exceptions</p>
          <h3 id="accounting-exceptions-heading" className="workspace-section-title">{view.title}</h3>
          <p className="workspace-section-summary">{view.description}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button asChild size="sm" variant="outline">
            <Link to={view.reconciliationHref}>Reconciliation queue</Link>
          </Button>
          <Button asChild size="sm" variant="outline">
            <Link to={view.approvalsHref}>Approval gate</Link>
          </Button>
        </div>
      </div>

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {view.metricRows.map((metric) => (
          <MetricCard
            key={metric.id}
            id={metric.id}
            label={metric.label}
            value={metric.value}
            delta={metric.detail}
            tone={metric.tone}
          />
        ))}
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_24rem]">
        <Card className="panel-surface" role="region" aria-label="Unified operational exception queue">
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <AlertCircle className="h-4 w-4 text-primary" aria-hidden="true" />
              Case queue
            </CardTitle>
            <CardDescription>Reconciliation exceptions with owner, SLA, comments, and audit evidence counts.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {view.cases.length > 0 ? (
              <div role="list" className="space-y-2" aria-label="Operational exception cases">
                {view.cases.map((item) => (
                  <div key={item.id} role="listitem" aria-label={item.ariaLabel} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div className="min-w-0">
                        <div className="font-semibold text-foreground">{item.title}</div>
                        <div className="mt-1 break-words font-mono text-[11px] text-muted-foreground">{item.subtitle}</div>
                      </div>
                      <Badge variant={item.statusTone}>{item.statusLabel}</Badge>
                    </div>
                    <div className="mt-3 grid gap-2 text-xs text-muted-foreground sm:grid-cols-2 xl:grid-cols-4">
                      <span>Owner: {item.ownerLabel}</span>
                      <span>SLA: {item.slaLabel}</span>
                      <span>{item.commentLabel}</span>
                      <span>{item.auditLabel}</span>
                    </div>
                    <Button asChild size="sm" variant="ghost" className="mt-3">
                      <Link to={item.routeHref}>{item.routeLabel}</Link>
                    </Button>
                  </div>
                ))}
              </div>
            ) : (
              <p role="status" className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm text-muted-foreground">
                {view.emptyText}
              </p>
            )}
          </CardContent>
        </Card>

        <Card className="panel-surface" role="region" aria-label="Exception workflow handoffs">
          <CardHeader>
            <CardTitle className="text-base">Workflow handoffs</CardTitle>
            <CardDescription>Resolution work stays connected to approval, audit, and retained evidence paths.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-2">
            <Button asChild variant="secondary" className="w-full justify-start">
              <Link to={view.reconciliationHref}>Open break queue</Link>
            </Button>
            <Button asChild variant="outline" className="w-full justify-start">
              <Link to={view.approvalsHref}>Review approval blockers</Link>
            </Button>
            <Button asChild variant="outline" className="w-full justify-start">
              <Link to={view.evidenceHref}>Open exception evidence packet</Link>
            </Button>
            <Button asChild variant="outline" className="w-full justify-start">
              <Link to={view.auditHref}>Open audit timeline</Link>
            </Button>
          </CardContent>
        </Card>
      </div>
    </section>
  );
}

function AccountingHighlight({
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

function AccountingWorkflowLaunchPanel({ view }: { view: AccountingWorkflowLaunchViewState }) {
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

function accountingToolingBadgeVariant(tone: AccountingToolingTone): "default" | "outline" | "success" | "warning" | "danger" {
  if (tone === "success" || tone === "warning" || tone === "danger") {
    return tone;
  }

  return "outline";
}

function accountingToolingBorderClass(tone: AccountingToolingTone): string {
  if (tone === "success") {
    return "border-success/30 bg-success/10";
  }

  if (tone === "warning") {
    return "border-warning/35 bg-warning/10";
  }

  if (tone === "danger") {
    return "border-danger/35 bg-danger/10";
  }

  return "border-border/70 bg-secondary/20";
}

function CapitalAccountWorkbenchPanel({ view }: { view: CapitalAccountWorkbenchViewModel }) {
  return (
    <Card className="panel-surface" role="region" aria-labelledby="capital-account-workbench-heading">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <p className="eyebrow-label">Private capital</p>
            <CardTitle id="capital-account-workbench-heading" className="text-base">{view.title}</CardTitle>
            <CardDescription>{view.description}</CardDescription>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant={accountingToolingBadgeVariant(view.statusTone)} dot>{view.statusLabel}</Badge>
            <Button size="sm" variant="outline" disabled={view.loading} busy={view.loading} onClick={() => void view.refresh()}>
              <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
              Refresh
            </Button>
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-5">
        {view.errorText ? (
          <div role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger">
            {view.errorText}
          </div>
        ) : null}

        <div className="flex flex-wrap gap-2">
          <AccountingChip label="Projected" value={view.projectedAtLabel} />
          <AccountingChip label="Route" value={view.workbenchRouteLabel} />
        </div>
        <p className="text-sm leading-6 text-muted-foreground">{view.statusReason}</p>

        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          {view.summaryCards.map((metric) => (
            <MetricCard
              key={metric.id}
              id={metric.id}
              label={metric.label}
              value={metric.value}
              delta={metric.detail}
              tone={metric.tone}
            />
          ))}
        </div>

        {view.investorAccounts.length === 0 && !view.loading ? (
          <div role="status" className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3 text-sm text-muted-foreground">
            {view.emptyText}
          </div>
        ) : null}

        {view.fundEventCommandRows.length > 0 ? (
          <section className="space-y-2" aria-labelledby="capital-account-fund-event-command-heading">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h4 id="capital-account-fund-event-command-heading" className="text-sm font-semibold text-foreground">Fund-event command centers</h4>
              <Badge variant="outline">{view.fundEventCommandRows.length.toLocaleString()} events</Badge>
            </div>
            <div className="overflow-x-auto rounded-md border border-border/70">
              <table className="w-full min-w-[940px] text-sm" aria-label="Capital-account fund-event command centers">
                <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                  <tr>
                    <th className="px-3 py-2 text-left">Fund event</th>
                    <th className="px-3 py-2 text-left">Readiness</th>
                    <th className="px-3 py-2 text-left">Evidence</th>
                    <th className="px-3 py-2 text-left">Routes</th>
                  </tr>
                </thead>
                <tbody>
                  {view.fundEventCommandRows.map((row) => (
                    <tr key={row.id} className="border-t border-border/60 align-top">
                      <td className="px-3 py-2">
                        <div className="font-semibold text-foreground">{row.title}</div>
                        <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{row.subtitle}</div>
                        <div className="mt-1 text-xs text-muted-foreground">{row.memoLabel}</div>
                        <div className="mt-1 font-mono text-xs text-muted-foreground">{row.netActivityLabel}</div>
                      </td>
                      <td className="px-3 py-2">
                        <Badge variant={row.readinessTone} dot>{row.readinessLabel}</Badge>
                        <div className="mt-1 text-xs text-muted-foreground">{row.readinessReasonLabel}</div>
                        <div className="mt-1 text-xs text-muted-foreground">{row.nextActionLabel}</div>
                      </td>
                      <td className="px-3 py-2 text-xs text-muted-foreground">
                        <div>{row.evidenceLabel}</div>
                        <div className="mt-1">{row.subledgerLabel}</div>
                        <div className="mt-1">{row.ledgerImpactLabel}</div>
                        <div className="mt-1">{row.reportOutputLabel}</div>
                      </td>
                      <td className="px-3 py-2">
                        <a
                          className="block break-all font-mono text-[11px] text-primary hover:underline"
                          href={row.commandCenterRouteLabel}
                          target="_blank"
                          rel="noreferrer"
                          aria-label={`Open capital-account fund-event command center for ${row.title}`}
                        >
                          {row.commandCenterRouteLabel}
                        </a>
                        <a
                          className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                          href={row.activityRouteLabel}
                          target="_blank"
                          rel="noreferrer"
                          aria-label={`Open capital-account fund-event activity for ${row.title}`}
                        >
                          {row.activityRouteLabel}
                        </a>
                        <a
                          className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                          href={row.evidenceRouteLabel}
                          target="_blank"
                          rel="noreferrer"
                          aria-label={`Open capital-account fund-event evidence for ${row.title}`}
                        >
                          {row.evidenceRouteLabel}
                        </a>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        ) : null}

        <div className="grid gap-4 xl:grid-cols-[minmax(0,1.15fr)_minmax(0,0.85fr)]">
          <section className="space-y-2" aria-labelledby="capital-account-investor-heading">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h4 id="capital-account-investor-heading" className="text-sm font-semibold text-foreground">Investor capital accounts</h4>
              <Badge variant="outline">{view.investorAccounts.length.toLocaleString()} rows</Badge>
            </div>
            <div className="overflow-x-auto rounded-md border border-border/70">
              <table className="w-full min-w-[880px] text-sm">
                <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                  <tr>
                    <th className="px-3 py-2 text-left">Account</th>
                    <th className="px-3 py-2 text-left">Readiness</th>
                    <th className="px-3 py-2 text-right">Net</th>
                    <th className="px-3 py-2 text-left">Evidence</th>
                    <th className="px-3 py-2 text-left">Cash support</th>
                    <th className="px-3 py-2 text-left">Route</th>
                  </tr>
                </thead>
                <tbody>
                  {view.investorAccounts.map((row) => (
                    <tr key={row.id} className="border-t border-border/60">
                      <td className="px-3 py-2">
                        <div className="font-semibold text-foreground">{row.title}</div>
                        <div className="mt-1 text-xs text-muted-foreground">{row.subtitle}</div>
                        <div className="mt-1 text-xs text-muted-foreground">{row.eventLabel}</div>
                      </td>
                      <td className="px-3 py-2">
                        <Badge variant={accountingToolingBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
                      </td>
                      <td className="px-3 py-2 text-right font-mono tabular-nums">
                        <div className="text-foreground">{row.netActivityLabel}</div>
                        <div className="mt-1 text-xs text-muted-foreground">{row.rollForwardLabel}</div>
                        <div className="mt-1 text-xs text-muted-foreground">{row.activityMixLabel}</div>
                      </td>
                      <td className="px-3 py-2 text-xs text-muted-foreground">{row.evidenceLabel}</td>
                      <td className="px-3 py-2 text-xs">
                        <Badge variant={row.paymentEvidenceTone}>{row.paymentEvidenceLabel}</Badge>
                        <div className="mt-1 text-muted-foreground">{row.paymentEvidenceSummaryLabel}</div>
                        <div className="mt-1 text-muted-foreground">{row.paymentEvidenceRequiredLabel}</div>
                      </td>
                      <td className="px-3 py-2">
                        <a href={row.routeLabel} className="break-all font-mono text-[11px] text-primary hover:underline">
                          {row.routeLabel}
                        </a>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <section className="space-y-2" aria-labelledby="capital-account-allocation-heading">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h4 id="capital-account-allocation-heading" className="text-sm font-semibold text-foreground">Allocation rules</h4>
              <Badge variant="outline">{view.allocationRules.length.toLocaleString()} checks</Badge>
            </div>
            <div className="grid gap-2">
              {view.allocationRules.map((row) => (
                <div key={row.id} className={cn("rounded-md border px-3 py-2 text-sm", accountingToolingBorderClass(row.statusTone))}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="font-semibold text-foreground">{row.label}</div>
                      <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{row.accountLabel}</div>
                    </div>
                    <Badge variant={accountingToolingBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{row.reason}</p>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{row.basis}</p>
                  <div className="mt-2 flex flex-wrap gap-2 text-xs">
                    <AccountingChip label="Evidence" value={row.evidenceLabel} />
                    <AccountingChip label="Required" value={row.requiredLabel} />
                    <AccountingChip label="Policy" value={row.policyLabel} />
                    <AccountingChip label="Effective" value={row.effectiveWindowLabel} />
                    <AccountingChip label="Approval" value={row.approvalLabel} />
                    <AccountingChip label="Inputs" value={row.inputSummaryLabel} />
                    <AccountingChip label="Fund events" value={row.relatedFundEventLabel} />
                  </div>
                  <p className="mt-2 break-all font-mono text-[11px] text-muted-foreground">{row.formulaLabel}</p>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{row.traceLabel}</p>
                  {row.routeLabel !== "No route" ? (
                    <a href={row.routeLabel} className="mt-2 block break-all font-mono text-[11px] text-primary hover:underline">
                      {row.routeLabel}
                    </a>
                  ) : null}
                </div>
              ))}
            </div>
          </section>
        </div>

        <div className="grid gap-4 xl:grid-cols-2">
          <section className="space-y-2" aria-labelledby="capital-account-statement-heading">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h4 id="capital-account-statement-heading" className="text-sm font-semibold text-foreground">Statement lineage</h4>
              <Badge variant="outline">{view.statementLineage.length.toLocaleString()} statements</Badge>
            </div>
            <div className="grid gap-2">
              {view.statementLineage.map((row) => (
                <div key={row.id} className={cn("rounded-md border px-3 py-2 text-sm", accountingToolingBorderClass(row.statusTone))}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="font-semibold text-foreground">{row.title}</div>
                      <div className="mt-1 text-xs text-muted-foreground">{row.subtitle}</div>
                    </div>
                    <Badge variant={accountingToolingBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
                  </div>
                  <div className="mt-2 grid gap-1 text-xs text-muted-foreground">
                    <span>{row.publicationLabel}</span>
                    <span>{row.provenanceLabel}</span>
                    <span>{row.restatementLabel}</span>
                    <span className="break-all font-mono text-[11px]">{row.manifestLabel}</span>
                  </div>
                  {row.changedLineRows.length > 0 ? (
                    <ul className="mt-2 grid gap-1 text-xs text-muted-foreground" aria-label={`${row.title} restatement changed lines`}>
                      {row.changedLineRows.map((line) => (
                        <li key={line.id} className="rounded-sm border border-border/60 px-2 py-1">
                          <span className="break-all font-mono text-[11px]">{line.lineKey}</span>
                          <span className="mx-2">{line.valueLabel}</span>
                          <span>{line.evidenceLabel}</span>
                        </li>
                      ))}
                    </ul>
                  ) : null}
                  <a href={row.routeLabel} className="mt-2 block break-all font-mono text-[11px] text-primary hover:underline">
                    {row.routeLabel}
                  </a>
                </div>
              ))}
            </div>
          </section>

          <section className="space-y-2" aria-labelledby="capital-account-audit-heading">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h4 id="capital-account-audit-heading" className="text-sm font-semibold text-foreground">Audit drill-through</h4>
              <Badge variant="outline">{view.auditDrillThroughs.length.toLocaleString()} targets</Badge>
            </div>
            <div className="grid gap-2">
              {view.auditDrillThroughs.map((row) => (
                <div key={row.id} className={cn("rounded-md border px-3 py-2 text-sm", accountingToolingBorderClass(row.statusTone))}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="font-semibold text-foreground">{row.title}</div>
                      <div className="mt-1 text-xs uppercase text-muted-foreground">{row.kind}</div>
                    </div>
                    <Badge variant={accountingToolingBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{row.summary}</p>
                  <div className="mt-2 flex flex-wrap gap-2 text-xs">
                    <AccountingChip label="Evidence" value={row.evidenceLabel} />
                    <AccountingChip label="Related" value={row.relatedLabel} />
                  </div>
                  {row.routeLabel !== "No route" ? (
                    <a href={row.routeLabel} className="mt-2 block break-all font-mono text-[11px] text-primary hover:underline">
                      {row.routeLabel}
                    </a>
                  ) : null}
                </div>
              ))}
            </div>
          </section>
        </div>

        <div className="grid gap-3 md:grid-cols-2">
          <div className="rounded-md border border-success/30 bg-success/10 px-3 py-3">
            <div className="text-xs font-semibold uppercase text-success">Live in v0.18 slice</div>
            <ul className="mt-2 grid gap-1 text-sm text-foreground">
              {view.liveCapabilities.map((item) => <li key={item}>{item}</li>)}
            </ul>
          </div>
          <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
            <div className="text-xs font-semibold uppercase text-muted-foreground">Still planned</div>
            <ul className="mt-2 grid gap-1 text-sm text-muted-foreground">
              {view.plannedCapabilities.map((item) => <li key={item}>{item}</li>)}
            </ul>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

function ManualJournalPrivateCapitalActivityPanel({ activity }: { activity: ManualJournalEntryWorkbenchViewModel["privateCapitalActivity"] }) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div className="min-w-0">
            <CardTitle className="text-base">{activity.title}</CardTitle>
            <CardDescription>{activity.statusLabel} / {activity.projectedAtLabel}</CardDescription>
          </div>
          <Badge variant={activity.validationIssues.length > 0 ? "warning" : activity.fundEvents.length > 0 ? "success" : "outline"} dot>
            {activity.validationIssues.length > 0 ? "Review" : activity.fundEvents.length > 0 ? "Projected" : "Empty"}
          </Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="grid gap-2 sm:grid-cols-2">
          {activity.summaryCards.map((metric) => (
            <div
              key={metric.id}
              className={cn(
                "rounded-md border px-3 py-2 text-sm",
                metric.tone === "success" ? "border-success/30 bg-success/10 text-success" :
                  metric.tone === "warning" ? "border-warning/30 bg-warning/10 text-warning" :
                    metric.tone === "danger" ? "border-danger/30 bg-danger/10 text-danger" :
                      "border-border/70 bg-secondary/20 text-muted-foreground"
              )}
            >
              <div className="text-[11px] font-semibold uppercase">{metric.label}</div>
              <div className="mt-1 font-mono text-base text-foreground">{metric.value}</div>
              <div className="mt-1 text-xs">{metric.detail}</div>
            </div>
          ))}
        </div>

        {activity.validationIssues.length > 0 ? (
          <div className="space-y-2">
            {activity.validationIssues.map((issue) => (
              <div key={issue.id} className="rounded border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                <div className="font-semibold">{issue.label}</div>
                <div className="mt-1">{issue.message}</div>
                <div className="mt-1 text-xs">{issue.detail}</div>
              </div>
            ))}
          </div>
        ) : null}

        {activity.paymentIntents.length > 0 ? (
          <>
            <div className="overflow-x-auto rounded-md border border-border/70">
              <table className="w-full min-w-[1040px] text-sm" aria-label="Payment intent and cash evidence workflows">
                <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                  <tr>
                    <th className="px-3 py-2 text-left">Payment intent</th>
                    <th className="px-3 py-2 text-left">Status</th>
                    <th className="px-3 py-2 text-left">Expected cash</th>
                    <th className="px-3 py-2 text-left">Approvals</th>
                    <th className="px-3 py-2 text-left">Cash evidence</th>
                    <th className="px-3 py-2 text-left">Reconciliation</th>
                    <th className="px-3 py-2 text-left">Audit</th>
                  </tr>
                </thead>
                <tbody>
                  {activity.paymentIntents.map((intent) => (
                    <tr key={intent.id} className="border-t border-border/60 bg-background/30 align-top">
                      <td className="px-3 py-2">
                        <div className="break-all font-mono text-xs text-foreground">{intent.title}</div>
                        <div className="mt-1 break-all text-[11px] text-muted-foreground">{intent.subtitle}</div>
                        <div className="mt-1 text-[11px] text-muted-foreground">{intent.requestedLabel}</div>
                        {intent.workbenchRouteLabel !== "No workbench route" ? (
                          <a
                            className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                            href={intent.workbenchRouteLabel}
                            target="_blank"
                            rel="noreferrer"
                            aria-label={`Open payment intent workbench route for ${intent.title}`}
                          >
                            {intent.workbenchRouteLabel}
                          </a>
                        ) : null}
                      </td>
                      <td className="px-3 py-2">
                        <Badge variant={intent.statusTone} dot>{intent.statusLabel}</Badge>
                        <div className="mt-1 text-xs text-muted-foreground">{intent.readinessReasonLabel}</div>
                        <div className="mt-1 text-[11px] text-muted-foreground">{intent.executionDeferredLabel}</div>
                      </td>
                      <td className="px-3 py-2 text-xs">
                        <div className="font-mono">{intent.expectedCashLabel}</div>
                        <div className="mt-1 text-[11px] text-muted-foreground">{intent.requestMetadataLabel}</div>
                        <div className="mt-1 text-[11px] text-muted-foreground">{intent.sourceEvidenceLabel}</div>
                      </td>
                      <td className="px-3 py-2 text-xs">{intent.approvalLabel}</td>
                      <td className="px-3 py-2 text-xs">
                        <div>{intent.bankEvidenceLabel}</div>
                        {intent.evidenceRouteLabel !== "No evidence route" ? (
                          <a
                            className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                            href={intent.evidenceRouteLabel}
                            target="_blank"
                            rel="noreferrer"
                            aria-label={`Open payment intent evidence packet for ${intent.title}`}
                          >
                            {intent.evidenceRouteLabel}
                          </a>
                        ) : null}
                      </td>
                      <td className="px-3 py-2 text-xs">{intent.reconciliationLabel}</td>
                      <td className="px-3 py-2 text-xs">{intent.auditLabel}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <section className="space-y-3" aria-label="Payment intent cash evidence drilldowns">
              {activity.paymentIntents.map((intent) => (
                <section
                  key={`${intent.id}-cash-evidence-drilldown`}
                  className="rounded-md border border-border/70 bg-background/30 px-3 py-3"
                  aria-label={`Cash evidence drilldown for ${intent.title}`}
                >
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div>
                      <div className="break-all font-mono text-xs text-foreground">{intent.title}</div>
                      <div className="mt-1 text-xs text-muted-foreground">{intent.expectedCashLabel}</div>
                      <div className="mt-1 text-[11px] text-muted-foreground">{intent.requestMetadataLabel}</div>
                      <div className="mt-1 text-[11px] text-muted-foreground">{intent.sourceEvidenceLabel}</div>
                    </div>
                    <Badge variant={intent.statusTone} dot>{intent.statusLabel}</Badge>
                  </div>
                  <div className="mt-3 grid gap-3 lg:grid-cols-4">
                    <div>
                      <div className="text-[11px] font-semibold uppercase text-muted-foreground">Approval chain</div>
                      {intent.approvalSteps.length > 0 ? (
                        <ol className="mt-2 space-y-2">
                          {intent.approvalSteps.map((step) => (
                            <li key={step.id} className="text-xs">
                              <div className="flex flex-wrap items-center gap-2">
                                <span className="font-semibold text-foreground">{step.sequenceLabel}</span>
                                <Badge variant="outline">{step.statusLabel}</Badge>
                              </div>
                              <div className="mt-1 text-muted-foreground">{step.roleLabel} / {step.actorLabel}</div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{step.decidedLabel}</div>
                              {step.evidenceRouteLabel !== "No approval evidence route" ? (
                                <a
                                  className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                                  href={step.evidenceRouteLabel}
                                  target="_blank"
                                  rel="noreferrer"
                                  aria-label={`Open approval evidence for ${intent.title} ${step.sequenceLabel}`}
                                >
                                  {step.evidenceRouteLabel}
                                </a>
                              ) : null}
                            </li>
                          ))}
                        </ol>
                      ) : (
                        <div className="mt-2 text-xs text-muted-foreground">No approval chain</div>
                      )}
                    </div>

                    <div>
                      <div className="text-[11px] font-semibold uppercase text-muted-foreground">Bank evidence</div>
                      {intent.bankEvidence.length > 0 ? (
                        <div className="mt-2 space-y-2">
                          {intent.bankEvidence.map((item) => (
                            <div key={item.id} className="text-xs">
                              <div className="flex flex-wrap items-center gap-2">
                                <span className="font-semibold text-foreground">{item.title}</span>
                                <Badge variant="outline">{item.statusLabel}</Badge>
                              </div>
                              <div className="mt-1 text-muted-foreground">{item.summaryLabel}</div>
                              <div className="mt-1 font-mono text-[11px] text-muted-foreground">{item.amountLabel} / {item.effectiveDateLabel}</div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{item.referenceLabel}</div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{item.recordedLabel}</div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{item.recorderLabel}</div>
                              {item.evidenceRouteLabel !== "No bank evidence route" ? (
                                <a
                                  className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                                  href={item.evidenceRouteLabel}
                                  target="_blank"
                                  rel="noreferrer"
                                  aria-label={`Open bank evidence for ${intent.title} ${item.title}`}
                                >
                                  {item.evidenceRouteLabel}
                                </a>
                              ) : null}
                            </div>
                          ))}
                        </div>
                      ) : (
                        <div className="mt-2 text-xs text-muted-foreground">No bank evidence</div>
                      )}
                    </div>

                    <div>
                      <div className="text-[11px] font-semibold uppercase text-muted-foreground">Reconciliation</div>
                      {intent.reconciliationLinks.length > 0 ? (
                        <div className="mt-2 space-y-2">
                          {intent.reconciliationLinks.map((link) => (
                            <div key={link.id} className="text-xs">
                              <div className="flex flex-wrap items-center gap-2">
                                <span className="break-all font-mono text-[11px] text-foreground">{link.id}</span>
                                <Badge variant="outline">{link.statusLabel}</Badge>
                              </div>
                              <div className="mt-1 text-muted-foreground">{link.summaryLabel}</div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{link.caseLabel}</div>
                              {link.routeLabel !== "No reconciliation evidence route" ? (
                                <a
                                  className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                                  href={link.routeLabel}
                                  target="_blank"
                                  rel="noreferrer"
                                  aria-label={`Open reconciliation evidence for ${intent.title} ${link.id}`}
                                >
                                  {link.routeLabel}
                                </a>
                              ) : null}
                            </div>
                          ))}
                        </div>
                      ) : (
                        <div className="mt-2 text-xs text-muted-foreground">No reconciliation link</div>
                      )}
                    </div>

                    <div>
                      <div className="text-[11px] font-semibold uppercase text-muted-foreground">Audit trail</div>
                      {intent.auditEvents.length > 0 ? (
                        <div className="mt-2 space-y-2">
                          {intent.auditEvents.map((event) => (
                            <div key={event.id} className="text-xs">
                              <div className="font-semibold text-foreground">{event.actionLabel}</div>
                              <div className="mt-1 text-muted-foreground">{event.summaryLabel}</div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{event.actorLabel} / {event.recordedLabel}</div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{event.evidenceLabel}</div>
                              {event.evidenceRouteLabels.map((route, index) => (
                                <a
                                  key={`${event.id}-${route}`}
                                  className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                                  href={route}
                                  target="_blank"
                                  rel="noreferrer"
                                  aria-label={`Open audit evidence ${index + 1} for ${intent.title} ${event.actionLabel}`}
                                >
                                  {route}
                                </a>
                              ))}
                            </div>
                          ))}
                        </div>
                      ) : (
                        <div className="mt-2 text-xs text-muted-foreground">No audit trail</div>
                      )}
                    </div>
                  </div>
                </section>
              ))}
            </section>
          </>
        ) : null}

        {activity.fundEventLedgerRecords.length > 0 ? (
          <div className="overflow-x-auto rounded-md border border-border/70">
            <table className="w-full min-w-[980px] text-sm" aria-label="Private-capital fund event ledger records">
              <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 text-left">Fund event</th>
                  <th className="px-3 py-2 text-left">Approval</th>
                  <th className="px-3 py-2 text-left">Effective</th>
                  <th className="px-3 py-2 text-left">Net</th>
                  <th className="px-3 py-2 text-left">Subledger</th>
                  <th className="px-3 py-2 text-left">GL impact</th>
                  <th className="px-3 py-2 text-left">Report output</th>
                  <th className="px-3 py-2 text-left">Evidence</th>
                </tr>
              </thead>
              <tbody>
                {activity.fundEventLedgerRecords.map((record) => (
                  <tr key={record.id} className="border-t border-border/60 bg-background/30 align-top">
                    <td className="px-3 py-2">
                      <div className="font-semibold text-foreground">{record.title}</div>
                      <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{record.subtitle}</div>
                      <div className="mt-1 text-xs text-muted-foreground">{record.memoLabel}</div>
                      <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{record.referenceLabel}</div>
                      <div className="mt-2 rounded border border-border/60 bg-secondary/20 px-2 py-1 text-[11px] text-muted-foreground">
                        <div className="flex flex-wrap items-center gap-1">
                          <Badge variant={record.paymentEvidenceTone} dot>{record.paymentEvidenceLabel}</Badge>
                        </div>
                        <div className="mt-1">{record.paymentEvidenceSummaryLabel}</div>
                        <div className="mt-1">{record.paymentEvidenceRequiredLabel}</div>
                      </div>
                      <a
                        className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                        href={record.activityRouteLabel}
                        target="_blank"
                        rel="noreferrer"
                        aria-label={`Open private-capital activity record for ${record.title}`}
                      >
                        {record.activityRouteLabel}
                      </a>
                      <a
                        className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                        href={record.commandCenterRouteLabel}
                        target="_blank"
                        rel="noreferrer"
                        aria-label={`Open fund event command center for ${record.title}`}
                      >
                        {record.commandCenterRouteLabel}
                      </a>
                    </td>
                    <td className="px-3 py-2">
                      <Badge variant={record.statusTone} dot>{record.statusLabel}</Badge>
                      <div className="mt-2">
                        <Badge variant={record.readinessTone} dot>{record.readinessLabel}</Badge>
                      </div>
                      <div className="mt-1 text-xs text-muted-foreground">{record.readinessReasonLabel}</div>
                      <div className="mt-1 text-xs text-muted-foreground">{record.issueLabel}</div>
                      {record.nextActionRouteLabel !== "No next-action route" ? (
                        <a
                          className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                          href={record.nextActionRouteLabel}
                          target="_blank"
                          rel="noreferrer"
                          aria-label={`Open next action for ${record.title}`}
                        >
                          {record.nextActionLabel}
                        </a>
                      ) : (
                        <div className="mt-1 text-[11px] text-muted-foreground">{record.nextActionLabel}</div>
                      )}
                      {record.approvalRouteLabel !== "No approval route" ? (
                        <a
                          className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                          href={record.approvalRouteLabel}
                          target="_blank"
                          rel="noreferrer"
                          aria-label={`Open approval route for ${record.title}`}
                        >
                          {record.approvalRouteLabel}
                        </a>
                      ) : null}
                    </td>
                    <td className="px-3 py-2 font-mono text-xs">{record.effectiveDateLabel}</td>
                    <td className="px-3 py-2 font-mono text-xs">
                      <div>{record.netActivityLabel}</div>
                      <div className="mt-1 text-[11px] text-muted-foreground">{record.grossActivityLabel} gross</div>
                    </td>
                    <td className="px-3 py-2 text-xs">
                      <div>{record.subledgerLabel}</div>
                      <div className="mt-1 text-muted-foreground">{record.capitalAccountRollForwardLabel}</div>
                    </td>
                    <td className="px-3 py-2 text-xs">{record.ledgerImpactLabel}</td>
                    <td className="px-3 py-2 text-xs">
                      <div>{record.reportOutputLabel}</div>
                      <div className="mt-1 text-muted-foreground">{record.reportOutputDetailLabel}</div>
                      <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{record.reportOutputRouteLabel}</div>
                    </td>
                    <td className="px-3 py-2 text-xs">
                      <div>{record.evidenceLabel}</div>
                      {record.evidenceRouteLabel !== "No evidence route" ? (
                        <a
                          className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                          href={record.evidenceRouteLabel}
                          target="_blank"
                          rel="noreferrer"
                          aria-label={`Open evidence packet for ${record.title}`}
                        >
                          {record.evidenceRouteLabel}
                        </a>
                      ) : null}
                      {record.evidenceCategories.length > 0 ? (
                        <div className="mt-2 space-y-1" aria-label={`Evidence readiness categories for ${record.title}`}>
                          <div className="text-[11px] font-semibold uppercase text-muted-foreground">
                            {record.evidenceCategorySummaryLabel}
                          </div>
                          {record.evidenceCategories.map((category) => (
                            <div key={category.id} className="rounded border border-border/60 bg-secondary/20 px-2 py-1">
                              <div className="flex flex-wrap items-center gap-1">
                                <Badge variant={category.tone} dot>{category.label}</Badge>
                                <span className="text-[11px] text-muted-foreground">{category.statusLabel}</span>
                                <span className="font-mono text-[11px] text-muted-foreground">{category.evidenceLabel}</span>
                              </div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{category.summaryLabel}</div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{category.requiredEvidenceLabel}</div>
                            </div>
                          ))}
                        </div>
                      ) : (
                        <div className="mt-2 text-[11px] text-muted-foreground">{record.evidenceCategorySummaryLabel}</div>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}

        {activity.capitalAccounts.length > 0 ? (
          <div className="overflow-x-auto rounded-md border border-border/70">
            <table className="w-full min-w-[760px] text-sm" aria-label="Private-capital capital account activity">
              <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 text-left">Capital account</th>
                  <th className="px-3 py-2 text-left">Net</th>
                  <th className="px-3 py-2 text-left">Calls</th>
                  <th className="px-3 py-2 text-left">Distributions</th>
                  <th className="px-3 py-2 text-left">Other</th>
                  <th className="px-3 py-2 text-left">Last event</th>
                </tr>
              </thead>
              <tbody>
                {activity.capitalAccounts.map((account) => (
                  <tr key={account.id} className="border-t border-border/60 bg-background/30 align-top">
                    <td className="px-3 py-2">
                      <div className="break-all font-mono text-xs text-foreground">{account.title}</div>
                      <div className="mt-1 break-all text-[11px] text-muted-foreground">{account.subtitle}</div>
                    </td>
                    <td className="px-3 py-2 font-mono">{account.netActivityLabel}</td>
                    <td className="px-3 py-2 font-mono">{account.contributionLabel}</td>
                    <td className="px-3 py-2 font-mono">{account.distributionLabel}</td>
                    <td className="px-3 py-2 text-xs">
                      <div>Subscriptions {account.subscriptionLabel}</div>
                      <div>Redemptions {account.redemptionLabel}</div>
                      <div>Fees {account.managementFeeLabel}</div>
                    </td>
                    <td className="px-3 py-2 text-xs">
                      <div>{account.eventCountLabel}</div>
                      <div className="mt-1 text-muted-foreground">{account.lastEventLabel}</div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}

        {activity.capitalAccountSubledgers.length > 0 ? (
          <div className="overflow-x-auto rounded-md border border-border/70">
            <table className="w-full min-w-[980px] text-sm" aria-label="Private-capital capital account subledgers">
              <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 text-left">Capital account</th>
                  <th className="px-3 py-2 text-left">Status</th>
                  <th className="px-3 py-2 text-left">Roll-forward</th>
                  <th className="px-3 py-2 text-left">Activity</th>
                  <th className="px-3 py-2 text-left">Counts</th>
                  <th className="px-3 py-2 text-left">Evidence</th>
                </tr>
              </thead>
              <tbody>
                {activity.capitalAccountSubledgers.map((subledger) => (
                  <tr key={subledger.id} className="border-t border-border/60 bg-background/30 align-top">
                    <td className="px-3 py-2">
                      <div className="break-all font-mono text-xs text-foreground">{subledger.title}</div>
                      <div className="mt-1 break-all text-[11px] text-muted-foreground">{subledger.subtitle}</div>
                      {subledger.activityRouteLabel !== "No subledger route" ? (
                        <a
                          className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                          href={subledger.activityRouteLabel}
                          target="_blank"
                          rel="noreferrer"
                          aria-label={`Open capital-account subledger for ${subledger.title}`}
                        >
                          {subledger.activityRouteLabel}
                        </a>
                      ) : null}
                    </td>
                    <td className="px-3 py-2">
                      <Badge variant={subledger.statusTone} dot>{subledger.statusLabel}</Badge>
                      <div className="mt-1 text-xs text-muted-foreground">{subledger.issueLabel}</div>
                      <div className="mt-1 text-xs text-muted-foreground">{subledger.dateRangeLabel}</div>
                    </td>
                    <td className="px-3 py-2 font-mono text-xs">
                      <div>{subledger.openingLabel} opening</div>
                      <div className="mt-1">{subledger.netActivityLabel} net</div>
                      <div className="mt-1">{subledger.endingLabel} ending</div>
                    </td>
                    <td className="px-3 py-2 text-xs">
                      <div>Calls {subledger.contributionLabel}</div>
                      <div>Distributions {subledger.distributionLabel}</div>
                      <div className="mt-1 text-muted-foreground">{subledger.otherActivityLabel}</div>
                    </td>
                    <td className="px-3 py-2 text-xs">
                      <div>{subledger.eventCountLabel}</div>
                      <div className="mt-1">{subledger.approvalQueueLabel}</div>
                      <div className="mt-1">{subledger.postedEventLabel}</div>
                      <div className="mt-1">{subledger.publishedReportLabel}</div>
                    </td>
                    <td className="px-3 py-2 text-xs">
                      <div>{subledger.evidenceLabel}</div>
                      <div className="mt-2 rounded border border-border/60 bg-secondary/20 px-2 py-1 text-[11px] text-muted-foreground">
                        <div className="flex flex-wrap items-center gap-1">
                          <Badge variant={subledger.paymentEvidenceTone} dot>{subledger.paymentEvidenceLabel}</Badge>
                        </div>
                        <div className="mt-1">{subledger.paymentEvidenceSummaryLabel}</div>
                        <div className="mt-1">{subledger.paymentEvidenceRequiredLabel}</div>
                      </div>
                      {subledger.evidenceCategories.length > 0 ? (
                        <div className="mt-2 space-y-1" aria-label={`Subledger evidence readiness categories for ${subledger.title}`}>
                          <div className="text-[11px] font-semibold uppercase text-muted-foreground">
                            {subledger.evidenceCategorySummaryLabel}
                          </div>
                          {subledger.evidenceCategories.map((category) => (
                            <div key={category.id} className="rounded border border-border/60 bg-secondary/20 px-2 py-1">
                              <div className="flex flex-wrap items-center gap-1">
                                <Badge variant={category.tone} dot>{category.label}</Badge>
                                <span className="text-[11px] text-muted-foreground">{category.statusLabel}</span>
                                <span className="font-mono text-[11px] text-muted-foreground">{category.evidenceLabel}</span>
                              </div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{category.summaryLabel}</div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{category.requiredEvidenceLabel}</div>
                            </div>
                          ))}
                        </div>
                      ) : (
                        <div className="mt-2 text-[11px] text-muted-foreground">{subledger.evidenceCategorySummaryLabel}</div>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}

        {activity.capitalAccountSubledgerEntries.length > 0 ? (
          <div className="overflow-x-auto rounded-md border border-border/70">
            <table className="w-full min-w-[820px] text-sm" aria-label="Private-capital capital account subledger">
              <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 text-left">Event</th>
                  <th className="px-3 py-2 text-left">Status</th>
                  <th className="px-3 py-2 text-left">Effective</th>
                  <th className="px-3 py-2 text-left">Net</th>
                  <th className="px-3 py-2 text-left">Running</th>
                  <th className="px-3 py-2 text-left">Gross</th>
                  <th className="px-3 py-2 text-left">Evidence</th>
                </tr>
              </thead>
              <tbody>
                {activity.capitalAccountSubledgerEntries.map((entry) => (
                  <tr key={entry.id} className="border-t border-border/60 bg-background/30 align-top">
                    <td className="px-3 py-2">
                      <div className="font-semibold text-foreground">{entry.title}</div>
                      <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{entry.subtitle}</div>
                      <div className="mt-1 text-xs text-muted-foreground">{entry.memoLabel}</div>
                    </td>
                    <td className="px-3 py-2">
                      <Badge variant={entry.statusTone} dot>{entry.statusLabel}</Badge>
                      <div className="mt-1 text-xs text-muted-foreground">{entry.issueLabel}</div>
                    </td>
                    <td className="px-3 py-2 font-mono text-xs">{entry.effectiveDateLabel}</td>
                    <td className="px-3 py-2 font-mono text-xs">{entry.netActivityLabel}</td>
                    <td className="px-3 py-2 font-mono text-xs">{entry.runningBalanceLabel}</td>
                    <td className="px-3 py-2 font-mono text-xs">{entry.grossAmountLabel}</td>
                    <td className="px-3 py-2 text-xs">{entry.evidenceLabel}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}

        {activity.ledgerImpacts.length > 0 ? (
          <div className="overflow-x-auto rounded-md border border-border/70">
            <table className="w-full min-w-[820px] text-sm" aria-label="Private-capital ledger impacts">
              <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 text-left">Impact</th>
                  <th className="px-3 py-2 text-left">Readiness</th>
                  <th className="px-3 py-2 text-left">Effective</th>
                  <th className="px-3 py-2 text-left">Debits</th>
                  <th className="px-3 py-2 text-left">Credits</th>
                  <th className="px-3 py-2 text-left">Imbalance</th>
                  <th className="px-3 py-2 text-left">Evidence</th>
                </tr>
              </thead>
              <tbody>
                {activity.ledgerImpacts.map((impact) => (
                  <tr key={impact.id} className="border-t border-border/60 bg-background/30 align-top">
                    <td className="px-3 py-2">
                      <div className="font-semibold text-foreground">{impact.title}</div>
                      <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{impact.subtitle}</div>
                    </td>
                    <td className="px-3 py-2">
                      <Badge variant={impact.readinessTone} dot>{impact.readinessLabel}</Badge>
                      <div className="mt-1 text-xs text-muted-foreground">{impact.issueLabel}</div>
                    </td>
                    <td className="px-3 py-2 font-mono text-xs">{impact.effectiveDateLabel}</td>
                    <td className="px-3 py-2 font-mono text-xs">{impact.debitLabel}</td>
                    <td className="px-3 py-2 font-mono text-xs">{impact.creditLabel}</td>
                    <td className="px-3 py-2 font-mono text-xs">{impact.imbalanceLabel}</td>
                    <td className="px-3 py-2 text-xs">
                      <div>{impact.evidenceLabel}</div>
                      <div className="mt-1 text-muted-foreground">{impact.lineLabel}</div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}

        {activity.reportOutputs.length > 0 ? (
          <div className="overflow-x-auto rounded-md border border-border/70">
            <table className="w-full min-w-[960px] text-sm" aria-label="Private-capital report outputs">
              <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 text-left">Output</th>
                  <th className="px-3 py-2 text-left">Readiness</th>
                  <th className="px-3 py-2 text-left">Effective</th>
                  <th className="px-3 py-2 text-left">Amount</th>
                  <th className="px-3 py-2 text-left">Evidence</th>
                  <th className="px-3 py-2 text-left">Workflow</th>
                  <th className="px-3 py-2 text-left">Route</th>
                </tr>
              </thead>
              <tbody>
                {activity.reportOutputs.map((output) => (
                  <tr key={output.id} className="border-t border-border/60 bg-background/30 align-top">
                    <td className="px-3 py-2">
                      <div className="font-semibold text-foreground">{output.title}</div>
                      <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{output.subtitle}</div>
                    </td>
                    <td className="px-3 py-2">
                      <Badge variant={output.readinessTone} dot>{output.readinessLabel}</Badge>
                      <div className="mt-1 text-xs text-muted-foreground">{output.issueLabel}</div>
                    </td>
                    <td className="px-3 py-2 font-mono text-xs">{output.effectiveDateLabel}</td>
                    <td className="px-3 py-2 font-mono text-xs">{output.amountLabel}</td>
                    <td className="px-3 py-2 text-xs">{output.evidenceLabel}</td>
                    <td className="px-3 py-2 text-xs">
                      <div className="break-all font-mono text-[11px] text-foreground">{output.workflowLabel}</div>
                      <div className="mt-1 text-muted-foreground">{output.publicationLabel}</div>
                      <div className="mt-1 text-muted-foreground">{output.provenanceLabel}</div>
                    </td>
                    <td className="px-3 py-2 break-all font-mono text-[11px] text-muted-foreground">{output.routeLabel}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}

        {activity.fundEvents.length > 0 ? (
          <div className="space-y-2">
            {activity.fundEvents.map((event) => (
              <div key={event.id} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2 text-sm">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0">
                    <div className="font-semibold text-foreground">{event.title}</div>
                    <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{event.subtitle}</div>
                  </div>
                  <Badge variant={event.statusTone} dot>{event.statusLabel}</Badge>
                </div>
                <dl className="mt-2 grid gap-2 text-xs sm:grid-cols-2">
                  <div><dt className="text-muted-foreground">Effective</dt><dd className="font-mono">{event.effectiveDateLabel}</dd></div>
                  <div><dt className="text-muted-foreground">Net</dt><dd className="font-mono">{event.amountLabel}</dd></div>
                  <div><dt className="text-muted-foreground">Gross</dt><dd className="font-mono">{event.grossAmountLabel}</dd></div>
                  <div><dt className="text-muted-foreground">Evidence</dt><dd>{event.evidenceLabel}</dd></div>
                  <div><dt className="text-muted-foreground">Payment</dt><dd className="break-all">{event.paymentLabel}</dd></div>
                  <div><dt className="text-muted-foreground">Validation</dt><dd>{event.validationLabel}</dd></div>
                </dl>
                <div className="mt-2 text-xs text-muted-foreground">{event.memoLabel}</div>
              </div>
            ))}
          </div>
        ) : (
          <p role="status" className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2 text-sm text-muted-foreground">
            {activity.emptyText}
          </p>
        )}
      </CardContent>
    </Card>
  );
}

function ManualJournalLineBadges({ badges }: { badges: ReturnType<ManualJournalEntryWorkbenchViewModel["getLineBadges"]> }) {
  return (
    <div className="mt-2 flex flex-wrap gap-1" aria-label="Line validation badges">
      {badges.map((badge) => (
        <span
          key={badge.id}
          title={badge.message}
          className={cn(
            "inline-flex max-w-full items-center rounded border px-1.5 py-0.5 text-[10px] font-semibold uppercase leading-tight",
            badge.tone === "danger"
              ? "border-danger/30 bg-danger/10 text-danger"
              : badge.tone === "warning"
                ? "border-warning/30 bg-warning/10 text-warning"
                : badge.tone === "success"
                  ? "border-success/30 bg-success/10 text-success"
                  : "border-border/70 bg-secondary/40 text-muted-foreground"
          )}
        >
          {badge.label}
        </span>
      ))}
    </div>
  );
}

function ManualJournalEntryWorkbenchPanel({ view }: { view: ManualJournalEntryWorkbenchViewModel }) {
  const selectedLine = view.draft.lines.find((line) => line.lineId === view.selectedLineId) ?? view.draft.lines[0] ?? null;

  return (
    <section className="workspace-section-band" aria-labelledby="manual-je-heading">
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <p className="eyebrow-label">Manual journal entries</p>
          <h3 id="manual-je-heading" className="workspace-section-title">{view.title}</h3>
          <p className="workspace-section-summary">{view.description}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={view.draft.status === "Submitted" ? "success" : view.draft.status === "NeedsFix" ? "warning" : "outline"} dot>
            {view.statusLabel}
          </Badge>
          <Button size="sm" variant="outline" disabled={view.loading} onClick={() => void view.refresh()}>
            <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
            Refresh
          </Button>
          <Button size="sm" variant="outline" busy={view.validateBusy} onClick={() => void view.validate()}>
            Validate
          </Button>
          <Button size="sm" variant="outline" busy={view.saveBusy} onClick={() => void view.save()}>
            Save draft
          </Button>
          <Button
            size="sm"
            busy={view.submitBusy}
            disabled={!view.canSubmit}
            disabledReason={view.submitDisabledReason}
            onClick={() => void view.submit()}
          >
            Submit approval
          </Button>
        </div>
      </div>

      {view.errorText ? (
        <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
          {view.errorText}
        </div>
      ) : null}

      <div className="grid gap-4 xl:grid-cols-[minmax(15rem,0.32fr)_minmax(0,1fr)]">
        <div className="space-y-4">
          <Card className="panel-surface">
            <CardHeader>
              <CardTitle className="text-base">Draft queue</CardTitle>
              <CardDescription>Saved manual JE drafts and submitted approval records from the shared workbench API.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-2">
              {view.drafts.length > 0 ? view.drafts.map((draft) => (
                <button
                  key={draft.journalEntryId}
                  type="button"
                  className={cn(
                    "w-full rounded-md border px-3 py-2 text-left text-sm transition hover:border-primary/50 hover:bg-primary/10",
                    draft.journalEntryId === view.draft.journalEntryId ? "border-primary/60 bg-primary/10" : "border-border/70 bg-secondary/20"
                  )}
                  onClick={() => view.selectDraft(draft.journalEntryId)}
                >
                  <span className="block font-semibold text-foreground">{draft.memo || "Untitled journal entry"}</span>
                  <span className="mt-1 block text-[11px] text-muted-foreground">{draft.entryType}</span>
                  <span className="mt-1 block font-mono text-[11px] text-muted-foreground">{draft.status} / v{draft.version}</span>
                  <span className="mt-1 block font-mono text-[11px] text-muted-foreground">{draft.accountingDate} / {draft.currency}</span>
                </button>
              )) : (
                <p role="status" className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2 text-sm text-muted-foreground">
                  No saved drafts yet. Save the current entry to add it to the queue.
                </p>
              )}
            </CardContent>
          </Card>

          <ManualJournalPrivateCapitalActivityPanel activity={view.privateCapitalActivity} />
        </div>

        <Card className="panel-surface">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="text-base">Journal entry draft</CardTitle>
                <CardDescription>Header context and line details are validated by the shared ledger API before approval.</CardDescription>
              </div>
              <div className="flex flex-wrap gap-2">
                <AccountingChip label="Totals" value={view.totalsLabel} />
                <AccountingChip label="Balance" value={view.imbalanceLabel} />
                <AccountingChip label="Treasury" value={view.treasuryContextLabel} />
              </div>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-3 md:grid-cols-3">
              <label className="space-y-1 text-sm">
                <span className="text-xs font-semibold uppercase text-muted-foreground">Memo</span>
                <input
                  className="min-h-10 w-full rounded-md border border-border bg-background px-3 py-2 text-sm"
                  value={view.draft.memo}
                  onChange={(event) => view.updateHeader("memo", event.target.value)}
                />
              </label>
              <label className="space-y-1 text-sm">
                <span className="text-xs font-semibold uppercase text-muted-foreground">Fund profile</span>
                <input
                  className="min-h-10 w-full rounded-md border border-border bg-background px-3 py-2 font-mono text-sm"
                  value={view.draft.fundProfileId}
                  onChange={(event) => view.updateHeader("fundProfileId", event.target.value)}
                />
              </label>
              <label className="space-y-1 text-sm">
                <span className="text-xs font-semibold uppercase text-muted-foreground">Currency</span>
                <input
                  className="min-h-10 w-full rounded-md border border-border bg-background px-3 py-2 font-mono text-sm"
                  value={view.draft.currency}
                  onChange={(event) => view.updateHeader("currency", event.target.value.toUpperCase())}
                />
              </label>
              <label className="space-y-1 text-sm">
                <span className="text-xs font-semibold uppercase text-muted-foreground">Entity</span>
                <input className="min-h-10 w-full rounded-md border border-border bg-background px-3 py-2 text-sm" value={view.draft.entityId ?? ""} onChange={(event) => view.updateHeader("entityId", event.target.value)} />
              </label>
              <label className="space-y-1 text-sm">
                <span className="text-xs font-semibold uppercase text-muted-foreground">Fund node</span>
                <input className="min-h-10 w-full rounded-md border border-border bg-background px-3 py-2 text-sm" value={view.draft.fundNodeId ?? ""} onChange={(event) => view.updateHeader("fundNodeId", event.target.value)} />
              </label>
              <label className="space-y-1 text-sm">
                <span className="text-xs font-semibold uppercase text-muted-foreground">Period</span>
                <input className="min-h-10 w-full rounded-md border border-border bg-background px-3 py-2 font-mono text-sm" value={view.draft.periodId ?? ""} onChange={(event) => view.updateHeader("periodId", event.target.value)} />
              </label>
            </div>

            <div className="overflow-x-auto rounded-md border border-border/70">
              <table className="w-full min-w-[920px] text-sm">
                <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                  <tr>
                    <th className="px-3 py-2 text-left">Side</th>
                    <th className="px-3 py-2 text-left">Amount</th>
                    <th className="px-3 py-2 text-left">GL account</th>
                    <th className="px-3 py-2 text-left">Security Master</th>
                    <th className="px-3 py-2 text-left">Description</th>
                  </tr>
                </thead>
                <tbody>
                  {view.draft.lines.map((line) => {
                    const badges = view.getLineBadges(line.lineId);
                    return (
                      <tr key={line.lineId} className={cn("border-t border-border/60 align-top", line.lineId === view.selectedLineId ? "bg-primary/10" : "bg-background/30")}>
                        <td className="px-3 py-2">
                          <select className="min-h-9 w-full rounded border border-border bg-background px-2" value={line.side} onChange={(event) => view.updateLine(line.lineId, { side: event.target.value as typeof line.side })} onFocus={() => view.selectLine(line.lineId)}>
                            <option value="Debit">Debit</option>
                            <option value="Credit">Credit</option>
                          </select>
                          {badges.length > 0 ? <ManualJournalLineBadges badges={badges} /> : null}
                        </td>
                        <td className="px-3 py-2">
                          <input className="min-h-9 w-full rounded border border-border bg-background px-2 font-mono" type="number" value={line.amount} onChange={(event) => view.updateLine(line.lineId, { amount: Number(event.target.value) })} onFocus={() => view.selectLine(line.lineId)} />
                        </td>
                        <td className="px-3 py-2">
                          <select className="min-h-9 w-full rounded border border-border bg-background px-2" value={line.accountPath} onChange={(event) => view.updateLine(line.lineId, { accountPath: event.target.value })} onFocus={() => view.selectLine(line.lineId)}>
                            <option value="">Select GL account</option>
                            {view.accountOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
                          </select>
                        </td>
                        <td className="px-3 py-2">
                          <button
                            type="button"
                            className="min-h-9 w-full rounded border border-border bg-background px-2 text-left text-xs"
                            onClick={() => view.selectLine(line.lineId)}
                          >
                            <span className="block truncate font-semibold text-foreground">{line.securityDisplayName || "Choose security"}</span>
                            <span className="block truncate font-mono text-[11px] text-muted-foreground">{line.securityId || "No Security Master reference"}</span>
                          </button>
                        </td>
                        <td className="px-3 py-2">
                          <input className="min-h-9 w-full rounded border border-border bg-background px-2" value={line.description ?? ""} onChange={(event) => view.updateLine(line.lineId, { description: event.target.value })} onFocus={() => view.selectLine(line.lineId)} />
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>

            <div className="flex flex-wrap gap-2">
              <Button size="sm" variant="outline" onClick={() => view.addLine("Debit")}>Add debit</Button>
              <Button size="sm" variant="outline" onClick={() => view.addLine("Credit")}>Add credit</Button>
            </div>

            <div className="rounded-md border border-border/70 bg-secondary/20 p-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div>
                  <h4 className="text-sm font-semibold text-foreground">Source evidence</h4>
                  <p className="text-xs text-muted-foreground">Attach source support before approval submission.</p>
                </div>
                <Badge variant={(view.draft.evidenceAttachments?.length ?? 0) > 0 ? "success" : "warning"} dot>
                  {(view.draft.evidenceAttachments?.length ?? 0)} attached
                </Badge>
              </div>
              <div className="mt-3 grid gap-3 lg:grid-cols-[minmax(0,0.22fr)_minmax(0,0.34fr)_minmax(0,0.18fr)_minmax(0,0.16fr)_auto]">
                <label className="space-y-1 text-sm">
                  <span className="text-xs font-semibold uppercase text-muted-foreground">Label</span>
                  <input className="min-h-9 w-full rounded border border-border bg-background px-2" value={view.attachmentDraft.displayName} onChange={(event) => view.updateAttachmentDraft({ displayName: event.target.value })} />
                </label>
                <label className="space-y-1 text-sm">
                  <span className="text-xs font-semibold uppercase text-muted-foreground">Route or path</span>
                  <input className="min-h-9 w-full rounded border border-border bg-background px-2 font-mono" value={view.attachmentDraft.uri} onChange={(event) => view.updateAttachmentDraft({ uri: event.target.value })} />
                </label>
                <label className="space-y-1 text-sm">
                  <span className="text-xs font-semibold uppercase text-muted-foreground">Kind</span>
                  <select className="min-h-9 w-full rounded border border-border bg-background px-2" value={view.attachmentDraft.evidenceKind} onChange={(event) => view.updateAttachmentDraft({ evidenceKind: event.target.value })}>
                    <option value="SourceDocument">Source document</option>
                    <option value="ApprovalSupport">Approval support</option>
                    <option value="ReconciliationEvidence">Reconciliation</option>
                    <option value="ValuationSupport">Valuation support</option>
                  </select>
                </label>
                <label className="space-y-1 text-sm">
                  <span className="text-xs font-semibold uppercase text-muted-foreground">Scope</span>
                  <select className="min-h-9 w-full rounded border border-border bg-background px-2" value={view.attachmentDraft.lineId ?? ""} onChange={(event) => view.updateAttachmentDraft({ lineId: event.target.value || null })}>
                    <option value="">Header</option>
                    {view.draft.lines.map((line) => <option key={line.lineId} value={line.lineId}>{line.side} {line.accountPath || line.lineId}</option>)}
                  </select>
                </label>
                <div className="flex items-end">
                  <Button size="sm" variant="outline" onClick={view.addAttachment}>
                    <Paperclip className="h-3.5 w-3.5" aria-hidden="true" />
                    Attach
                  </Button>
                </div>
              </div>
              <div className="mt-3 grid gap-2 md:grid-cols-2">
                {(view.draft.evidenceAttachments ?? []).length > 0 ? (view.draft.evidenceAttachments ?? []).map((attachment) => (
                  <div key={attachment.attachmentId} className="flex items-start justify-between gap-3 rounded border border-border/70 bg-background/50 px-3 py-2 text-sm">
                    <div className="min-w-0">
                      <div className="font-semibold text-foreground">{attachment.displayName}</div>
                      <div className="truncate font-mono text-xs text-muted-foreground">{attachment.uri}</div>
                      <div className="mt-1 text-xs text-muted-foreground">{attachment.evidenceKind} / {attachment.lineId ? `Line ${attachment.lineId}` : "Header"}</div>
                    </div>
                    <Button size="icon" variant="ghost" aria-label={`Remove evidence ${attachment.displayName}`} onClick={() => view.removeAttachment(attachment.attachmentId)}>
                      <X className="h-3.5 w-3.5" aria-hidden="true" />
                    </Button>
                  </div>
                )) : (
                  <p className="rounded border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                    No source evidence is attached yet. Approval submission remains blocked until at least one evidence route or source document is linked.
                  </p>
                )}
              </div>
            </div>

            <div className="grid gap-4 lg:grid-cols-[minmax(0,0.55fr)_minmax(0,0.45fr)]">
              <div className="rounded-md border border-border/70 bg-secondary/20 p-3">
                <h4 className="text-sm font-semibold text-foreground">Validation</h4>
                <div className="mt-3 space-y-2">
                  {view.validationIssues.length > 0 ? view.validationIssues.map((issue) => (
                    <div key={issue.id} className={cn("rounded border px-3 py-2 text-sm", issue.tone === "danger" ? "border-danger/30 bg-danger/10 text-danger" : issue.tone === "warning" ? "border-warning/30 bg-warning/10 text-warning" : "border-border/70 bg-background/50 text-muted-foreground")}>
                      <div className="font-semibold">{issue.label}</div>
                      <div className="mt-1">{issue.message}</div>
                      <div className="mt-1 text-xs">{issue.detail}</div>
                    </div>
                  )) : (
                    <p className="rounded border border-border/70 bg-background/50 px-3 py-2 text-sm text-muted-foreground">
                      No validation issues are attached to this draft. Use Validate before approval submission.
                    </p>
                  )}
                </div>
              </div>
              <div className="rounded-md border border-border/70 bg-secondary/20 p-3">
                <h4 className="text-sm font-semibold text-foreground">Selected line</h4>
                {selectedLine ? (
                  <div className="mt-3 space-y-3 text-sm">
                    <dl className="grid gap-2">
                      <div><dt className="text-xs text-muted-foreground">Line ID</dt><dd className="break-all font-mono">{selectedLine.lineId}</dd></div>
                      <div><dt className="text-xs text-muted-foreground">GL account</dt><dd className="break-all font-mono">{selectedLine.accountPath || "Not selected"}</dd></div>
                      <div><dt className="text-xs text-muted-foreground">Security</dt><dd className="break-all">{selectedLine.securityDisplayName || selectedLine.securityId || "No Security Master reference"}</dd></div>
                    </dl>
                    <div className="rounded border border-border/70 bg-background/50 p-2">
                      <label className="space-y-1 text-sm">
                        <span className="text-xs font-semibold uppercase text-muted-foreground">Security Master picker</span>
                        <div className="flex gap-2">
                          <input
                            className="min-h-9 min-w-0 flex-1 rounded border border-border bg-background px-2"
                            value={view.securitySearchQuery}
                            placeholder="Ticker, ISIN, CUSIP, FIGI, name"
                            onChange={(event) => view.updateSecuritySearchQuery(event.target.value)}
                          />
                          <Button size="icon" variant="outline" busy={view.securitySearchBusy} aria-label="Search Security Master" onClick={() => void view.searchSecurityMaster()}>
                            <Search className="h-3.5 w-3.5" aria-hidden="true" />
                          </Button>
                        </div>
                      </label>
                      <p role="status" className={cn("mt-2 text-xs", view.securitySearchErrorText ? "text-danger" : "text-muted-foreground")}>{view.securitySearchStatusText}</p>
                      <div className="mt-2 space-y-2">
                        {view.securitySearchResults.map((security) => (
                          <button
                            key={security.securityId}
                            type="button"
                            className="w-full rounded border border-border/70 bg-secondary/30 px-3 py-2 text-left hover:border-primary/50 hover:bg-primary/10"
                            onClick={() => view.selectSecurity(selectedLine.lineId, security)}
                          >
                            <span className="block font-semibold text-foreground">{security.displayName}</span>
                            <span className="mt-1 block font-mono text-[11px] text-muted-foreground">{security.securityId} / {security.classification.assetClass} / {security.classification.primaryIdentifierValue}</span>
                          </button>
                        ))}
                      </div>
                      {selectedLine.securityId ? (
                        <Button className="mt-2" size="sm" variant="ghost" onClick={() => view.clearSecurity(selectedLine.lineId)}>
                          <X className="h-3.5 w-3.5" aria-hidden="true" />
                          Clear security
                        </Button>
                      ) : null}
                    </div>
                  </div>
                ) : (
                  <p className="mt-3 text-sm text-muted-foreground">Select a line to inspect attribution.</p>
                )}
              </div>
            </div>
          </CardContent>
        </Card>
      </div>
    </section>
  );
}

function AccountingConfigurationPanel({ view }: { view: AccountingConfigurationViewModel }) {
  return (
    <section className="workspace-section-band" aria-labelledby="accounting-configure-heading">
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <p className="eyebrow-label">Configure</p>
          <h3 id="accounting-configure-heading" className="workspace-section-title">{view.title}</h3>
          <p className="workspace-section-summary">{view.description}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={view.statusTone === "success" ? "success" : view.statusTone === "danger" ? "danger" : view.statusTone === "warning" ? "warning" : "outline"} dot>
            {view.statusLabel}
          </Badge>
          <Button
            size="sm"
            variant="outline"
            disabled={view.loading}
            disabledReason={view.loading ? "Configuration refresh is already in progress." : null}
            onClick={() => void view.refresh()}
          >
            <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
            Refresh
          </Button>
          <Button
            size="sm"
            disabled={!view.canActivate}
            disabledReason={view.activateDisabledReason}
            busy={view.activateBusy}
            busyLabel={view.activateButtonLabel}
            onClick={() => void view.activate()}
          >
            {view.activateButtonLabel}
          </Button>
        </div>
      </div>

      {view.errorText ? (
        <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
          <div className="font-semibold">{view.errorText}</div>
          {view.errorDetails.length > 0 ? (
            <ul className="mt-2 list-disc pl-4">
              {view.errorDetails.map((detail) => <li key={detail}>{detail}</li>)}
            </ul>
          ) : null}
        </div>
      ) : null}

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-5">
        {view.metricRows.map((metric) => (
          <div key={metric.id} className="panel-surface px-4 py-3">
            <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{metric.label}</div>
            <div className={cn("mt-2 font-mono text-xl font-semibold", cashFlowTextClass(metric.tone))}>{metric.value}</div>
            <p className="mt-2 text-xs leading-5 text-muted-foreground">{metric.detail}</p>
          </div>
        ))}
      </div>

      <div className="grid gap-4 xl:grid-cols-[1.1fr_0.9fr]">
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Network className="h-5 w-5 text-primary" />
              Journal templates and preview
            </CardTitle>
            <CardDescription>Preview uses shared accounting configuration endpoints and does not persist journal entries.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap items-center gap-2">
              <Button
                size="sm"
                disabled={!view.canPreview}
                disabledReason={view.previewDisabledReason}
                busy={view.previewBusy}
                busyLabel={view.previewButtonLabel}
                onClick={() => void view.previewFirstTemplate()}
              >
                {view.previewButtonLabel}
              </Button>
              {view.previewStatusText ? <span className="text-sm text-muted-foreground">{view.previewStatusText}</span> : null}
            </div>

            <div className="space-y-2">
              {view.templates.length > 0 ? view.templates.map((template) => (
                <div key={template.id} className="rounded-lg border border-border/70 bg-secondary/25 px-3 py-2">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="font-semibold text-foreground">{template.title}</div>
                      <div className="mt-1 break-words font-mono text-xs text-muted-foreground">{template.subtitle}</div>
                    </div>
                    <Badge variant={template.statusLabel === "Balanced" ? "success" : template.statusLabel === "Archived" ? "outline" : "warning"}>
                      {template.statusLabel}
                    </Badge>
                  </div>
                  <div className="mt-2 flex flex-wrap gap-2 text-xs text-muted-foreground">
                    <span>{template.lineCountLabel}</span>
                    <span>{template.balanceLabel}</span>
                  </div>
                </div>
              )) : (
                <p role="status" className="rounded-lg border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">{view.emptyText}</p>
              )}
            </div>

            {view.preview ? (
              <div className="rounded-lg border border-border/70 bg-background/35 p-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <div className="font-semibold text-foreground">{view.preview.title}</div>
                    <div className="mt-1 font-mono text-xs text-muted-foreground">{view.preview.balanceLabel}</div>
                  </div>
                  <Badge variant={view.preview.statusLabel.startsWith("Balanced") ? "success" : "warning"}>{view.preview.statusLabel}</Badge>
                </div>
                <div className="mt-3 space-y-2">
                  {view.preview.lineRows.map((line) => (
                    <div key={line.id} className="grid gap-2 rounded border border-border/60 px-2 py-2 text-xs sm:grid-cols-[1fr_auto_auto]">
                      <span className="min-w-0 break-words font-mono text-foreground">{line.account}</span>
                      <span className="font-mono text-muted-foreground">{line.side}</span>
                      <span className="font-mono text-foreground">{line.amount}</span>
                    </div>
                  ))}
                </div>
              </div>
            ) : null}
          </CardContent>
        </Card>

        <Card className="panel-surface">
          <CardHeader>
            <CardTitle>Validation and audit trail</CardTitle>
            <CardDescription>Configuration readiness and append-only mutation evidence stay visible before activation.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              {view.validationIssues.length > 0 ? view.validationIssues.map((issue) => (
                <div key={issue.id} className={cn(
                  "rounded-lg border px-3 py-2 text-sm",
                  issue.tone === "danger" ? "border-danger/30 bg-danger/10 text-danger" : "",
                  issue.tone === "warning" ? "border-warning/30 bg-warning/10 text-warning" : "",
                  issue.tone === "default" ? "border-border/70 bg-secondary/25 text-muted-foreground" : ""
                )}>
                  <div className="font-semibold">{issue.label}</div>
                  <div className="mt-1">{issue.message}</div>
                  <div className="mt-1 font-mono text-xs">{issue.detail}</div>
                </div>
              )) : (
                <div className="rounded-lg border border-success/30 bg-success/10 px-3 py-2 text-sm text-success">
                  No critical configuration validation issues.
                </div>
              )}
            </div>

            <div className="space-y-2">
              <div className="eyebrow-label">Recent audit events</div>
              {view.auditTrail.length > 0 ? view.auditTrail.map((event) => (
                <div key={event.id} className="rounded-lg border border-border/70 bg-secondary/25 px-3 py-2 text-sm">
                  <div className="font-semibold text-foreground">{event.title}</div>
                  <div className="mt-1 break-words font-mono text-xs text-muted-foreground">{event.subtitle}</div>
                  <div className="mt-1 font-mono text-xs text-muted-foreground">{event.hashLabel}</div>
                </div>
              )) : (
                <p className="rounded-lg border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">{view.emptyText}</p>
              )}
            </div>
          </CardContent>
        </Card>
      </div>
    </section>
  );
}

function AccountingValue({ label, value, tone, ariaLabel }: { label: string; value: string; tone?: string; ariaLabel?: string }) {
  return (
    <div aria-label={ariaLabel} className="data-grid-surface flex items-center justify-between gap-4 px-3 py-2">
      <span className="text-muted-foreground">{label}</span>
      <span className={cn("font-mono text-foreground", tone)}>{value}</span>
    </div>
  );
}

function AccountingChip({ label, value }: { label: string; value: string }) {
  return (
    <span className="toolbar-chip" role="group" aria-label={`${label} ${value}`}>
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
