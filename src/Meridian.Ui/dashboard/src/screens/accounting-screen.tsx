import { AlertCircle, BookCheck, Briefcase, CheckCircle2, Landmark, Network, RefreshCcw, Search, ShieldCheck, Table2, TrendingUp, WalletCards } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { useEffect, useMemo, useState } from "react";
import { MetricCard } from "@/components/meridian/metric-card";
import { DenseDataTable, EntitySummary, ToolbarStrip, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { WorkspaceFilterBar, WorkspaceTabStrip } from "@/components/meridian/workspace-primitives";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { LotsTrackerPanel, SecurityDetailsPanel } from "@/components/meridian/security-details-tracker";
import { getAccountingSystemProviders, getLatestAccountingSystemImport, getLatestAccountingSystemReconciliation, previewAccountingSystemImport } from "@/lib/api";
import { cn } from "@/lib/utils";
import { WORKSTATION_ROUTE_CATALOG, workspaceForPath } from "@/lib/workspace";
import {
  buildAccountingLoadingViewState,
  resolveAccountingWorkstream,
  SECURITY_IDENTITY_DETAIL_PANEL_ID,
  useAccountingConfigurationViewModel,
  useAccountingCashFlowViewModel,
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
  CorporateActionsViewState,
  CorporateActionRowViewModel,
  ReconciliationBreakRowViewModel,
  ReconciliationQueuePanelViewState,
  ReconciliationQueueRunRowViewModel,
  ReconciliationStatementRunRowViewModel,
  ReconciliationQueueRunTone,
  AccountingTrialBalanceRowViewModel,
  SecuritySchedulesViewState,
  SecurityScheduleRowViewModel,
  SecurityOpenLotReadModelViewState,
  SecurityOpenLotRowViewModel,
  SecuritySearchResultRowViewModel,
  TradingParametersViewState
} from "@/screens/accounting-screen.view-model";
import type { AccountingSystemImportDetail, AccountingSystemProvider, AccountingSystemReconciliationSummary, AccountingWorkspaceResponse, MultiAssetCoverageSummary } from "@/types";

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

type FinancialOperationsStepId =
  | "receive-activity"
  | "match-records"
  | "resolve-exceptions"
  | "approve-results"
  | "produce-evidence";

interface FinancialOperationsStep {
  id: FinancialOperationsStepId;
  label: string;
  description: string;
  href: string;
}

const financialOperationsSteps: FinancialOperationsStep[] = [
  {
    id: "receive-activity",
    label: "Receive Activity",
    description: "Source activity, ledger context, security coverage, and account records arrive in Accounting.",
    href: WORKSTATION_ROUTE_CATALOG.accountingLedger
  },
  {
    id: "match-records",
    label: "Match Records",
    description: "Ledger, cash, security, and position records are matched through reconciliation runs.",
    href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation
  },
  {
    id: "resolve-exceptions",
    label: "Resolve Exceptions",
    description: "Open breaks, comments, evidence, and sign-off blockers stay visible for resolution.",
    href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation
  },
  {
    id: "approve-results",
    label: "Approve Results",
    description: "Close readiness, approvals, and retained audit decisions are captured before release.",
    href: WORKSTATION_ROUTE_CATALOG.accountingApprovals
  },
  {
    id: "produce-evidence",
    label: "Produce Evidence",
    description: "Approved close evidence is packaged into retained audit packets and report-ready outputs.",
    href: WORKSTATION_ROUTE_CATALOG.reportingEvidence
  }
];

const accountingSystemStatusVariant = {
  Matched: "success",
  Variance: "warning",
  MissingExternal: "warning",
  MissingMeridian: "danger",
  ReviewRequired: "warning"
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
  const activeProvider = providers.find((provider) => provider.providerId === "quickbooks-fixture") ?? providers[0] ?? null;
  const plannedProvider = providers.find((provider) => provider.providerId === "quickbooks");
  const rows = reconciliation?.rows.slice(0, 5) ?? [];

  return (
    <Card className="panel-surface" role="region" aria-label="External GL reconciliation">
      <CardHeader>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <div className="eyebrow-label">External GL reconciliation</div>
            <CardTitle className="mt-2 flex items-center gap-2 text-base">
              <BookCheck className="h-4 w-4 text-primary" aria-hidden="true" />
              QuickBooks fixture evidence
            </CardTitle>
            <CardDescription className="mt-2">
              External accounting-system records are imported as evidence for comparison with Meridian ledger records; posting remains disabled.
            </CardDescription>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {activeProvider ? <Badge variant="success">{activeProvider.statusLabel}</Badge> : null}
            {plannedProvider ? <Badge variant="outline">{plannedProvider.statusLabel}</Badge> : null}
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
              <div className="mt-3 space-y-1">
                {reconciliation.evidenceReferences.map((evidence) => (
                  <div key={evidence} className="truncate font-mono text-[11px] text-muted-foreground">{evidence}</div>
                ))}
              </div>
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

function formatGlAmount(value: number, currency: string): string {
  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: currency || "USD",
    maximumFractionDigits: 2
  }).format(value);
}

export function AccountingScreen({ data, multiAssetCoverage }: AccountingScreenProps) {
  const { pathname } = useLocation();
  const workstream = resolveAccountingWorkstream(pathname);
  const workspace = workspaceForPath(pathname);
  const reconciliation = useAccountingReconciliationViewModel(data, workstream);
  const resolveDialog = useReconciliationResolveDialogViewModel(reconciliation.resolveBreak);
  const selectedReconciliation = reconciliation.selectedReconciliation;
  const selectedReconciliationDetail = reconciliation.detailView;
  const cashFlow = useAccountingCashFlowViewModel(data?.cashFlow ?? null, pathname, workstream);
  const reporting = useAccountingReportingViewModel(data?.reporting ?? null);
  const configuration = useAccountingConfigurationViewModel();
  const securityMaster = useSecurityMasterViewModel(workstream === "security-master");
  const [accountingSystemProviders, setAccountingSystemProviders] = useState<AccountingSystemProvider[]>([]);
  const [accountingSystemImport, setAccountingSystemImport] = useState<AccountingSystemImportDetail | null>(null);
  const [accountingSystemReconciliation, setAccountingSystemReconciliation] = useState<AccountingSystemReconciliationSummary | null>(null);
  const [accountingSystemLoading, setAccountingSystemLoading] = useState(false);
  const [accountingSystemError, setAccountingSystemError] = useState<string | null>(null);
  const [activeTaskView, setActiveTaskView] = useState<AccountingTaskViewId>(() => (
    typeof window === "undefined" ? "overview" : resolveAccountingTaskViewId(window.location.hash)
  ));
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

  useEffect(() => {
    const updateActiveTaskView = () => {
      setActiveTaskView(resolveAccountingTaskViewId(window.location.hash));
    };

    updateActiveTaskView();
    window.addEventListener("hashchange", updateActiveTaskView);
    return () => window.removeEventListener("hashchange", updateActiveTaskView);
  }, []);

  const refreshAccountingSystem = async (persistPreview = false) => {
    setAccountingSystemLoading(true);
    setAccountingSystemError(null);
    try {
      const providers = await getAccountingSystemProviders();
      const importDetail = persistPreview
        ? await previewAccountingSystemImport({ providerId: "quickbooks-fixture", persistPreview: true })
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

  if (!data) {
    const loading = buildAccountingLoadingViewState(pathname);
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
  const activeFinancialOperationsStep = resolveActiveFinancialOperationsStep(pathname);
  const multiAssetCoveragePanel = buildMultiAssetCoveragePanel(multiAssetCoverage);
  const accountingTaskFields = [
    { id: "workstream", label: "Workstream", value: workstream },
    { id: "queue", label: "Queue", value: String(data.reconciliationQueue.length) },
    { id: "breaks", label: "Breaks", value: String(data.breakQueue.length) },
    { id: "profiles", label: "Profiles", value: String(data.reporting.profileCount) }
  ];
  const visibleAccountingTaskViews = getAccountingTaskViewsForWorkstream(workstream);
  const accountingTaskTabs = visibleAccountingTaskViews.map((view) => ({
    id: view.id,
    label: view.label,
    selected: activeTaskView === view.id,
    panelId: view.sectionId,
    href: view.href
  }));
  const accountingTaskMapCards = buildAccountingTaskMapCards({
    workstream,
    breakCount: data.breakQueue.length,
    runCount: data.reconciliationQueue.length,
    profileCount: data.reporting.profileCount,
    hasSelectedReconciliation: Boolean(selectedReconciliation),
    configurationStatusLabel: configuration.statusLabel
  });

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

      <section
        role="region"
        aria-label="Financial Operations workflow"
        className="panel-surface px-4 py-4"
      >
        <div className="flex flex-col gap-3 xl:flex-row xl:items-start xl:justify-between">
          <div className="min-w-0">
            <div className="eyebrow-label">Financial Operations Flow</div>
            <h3 className="mt-1 text-base font-semibold text-foreground">
              {"Receive Activity -> Match Records -> Resolve Exceptions -> Approve Results -> Produce Evidence"}
            </h3>
          </div>
          <nav className="workflow-continuity-steps xl:max-w-[64rem]" aria-label="Financial Operations steps">
            {financialOperationsSteps.map((step) => {
              const active = step.id === activeFinancialOperationsStep;
              return (
                <Link
                  key={step.id}
                  to={step.href}
                  aria-current={active ? "step" : undefined}
                  aria-label={active
                    ? `${step.label}, current financial operations step`
                    : `Open ${step.label}, financial operations step`}
                  className={cn(
                    "workflow-continuity-step focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                    active ? "workflow-continuity-step-active workflow-continuity-step-review" : "workflow-continuity-step-pending"
                  )}
                >
                  <span className="workflow-continuity-step-label">{step.label}</span>
                  <span className="workflow-continuity-step-description">{step.description}</span>
                  <span className="workflow-continuity-step-status">{active ? "Current" : "Available"}</span>
                </Link>
              );
            })}
          </nav>
        </div>
      </section>

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

      <WorkspaceFilterBar
        label="Accounting task navigator"
        searchLabel="Accounting tasks"
        searchValue={accountingTaskViews.find((view) => view.id === activeTaskView)?.label ?? "Overview"}
        fields={accountingTaskFields}
        actions={
          <WorkspaceTabStrip
            label="Accounting sub-task screens"
            tabs={accountingTaskTabs}
          />
        }
      />
      <AccountingTaskMap
        label="Accounting page organization"
        cards={accountingTaskMapCards}
      />

      {workstream === "configure" ? (
        <AccountingConfigurationPanel view={configuration} />
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
        <section className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
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
        </section>
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
      )}

      {workstream === "reconciliation" && (
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

function resolveActiveFinancialOperationsStep(pathname: string): FinancialOperationsStepId {
  if (pathname.startsWith(WORKSTATION_ROUTE_CATALOG.reporting)) {
    return "produce-evidence";
  }

  if (pathname.startsWith(WORKSTATION_ROUTE_CATALOG.accountingApprovals)) {
    return "approve-results";
  }

  if (pathname.startsWith(WORKSTATION_ROUTE_CATALOG.accountingReconciliation)) {
    return "match-records";
  }

  return "receive-activity";
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

type AccountingTaskViewId =
  | "overview"
  | "posture"
  | "exceptions"
  | "ledger"
  | "reporting"
  | "security-master"
  | "actions"
  | "history"
  | "configure";

interface AccountingTaskView {
  id: AccountingTaskViewId;
  label: string;
  href: string;
  sectionId: string;
}

type AccountingTaskMapTone = "primary" | "supporting" | "muted";

interface AccountingTaskMapCard {
  id: string;
  label: string;
  value: string;
  detail: string;
  href: string;
  tone: AccountingTaskMapTone;
}

const accountingTaskViews: AccountingTaskView[] = [
  { id: "overview", label: "Overview", href: "#accounting-overview", sectionId: "accounting-overview" },
  { id: "posture", label: "Close Posture", href: "#accounting-posture", sectionId: "accounting-posture" },
  { id: "exceptions", label: "Exceptions", href: "#accounting-exceptions", sectionId: "accounting-exceptions" },
  { id: "ledger", label: "Ledger Review", href: "#trial-balance-title", sectionId: "trial-balance-title" },
  { id: "reporting", label: "Reports", href: "#accounting-reporting", sectionId: "accounting-reporting" },
  { id: "security-master", label: "Security Master", href: "#security-master-search", sectionId: "security-master-search" },
  { id: "actions", label: "Actions", href: "#accounting-actions", sectionId: "accounting-actions" },
  { id: "history", label: "History", href: "#accounting-history", sectionId: "accounting-history" },
  { id: "configure", label: "Configure", href: "#accounting-configure-heading", sectionId: "accounting-configure-heading" }
];

const accountingTaskViewsByWorkstream: Record<AccountingWorkstream, AccountingTaskViewId[]> = {
  ledger: ["overview", "posture", "ledger", "reporting", "history"],
  reconciliation: ["overview", "posture", "exceptions", "actions", "history", "reporting"],
  reporting: ["overview", "reporting", "posture", "ledger"],
  "security-master": ["overview", "security-master", "posture", "reporting"],
  configure: ["overview", "configure", "posture", "reporting"]
};

function resolveAccountingTaskViewId(hash: string): AccountingTaskViewId {
  const normalizedHash = hash.replace(/^#/, "");
  return accountingTaskViews.find((view) => view.sectionId === normalizedHash)?.id ?? "overview";
}

function getAccountingTaskViewsForWorkstream(workstream: AccountingWorkstream): AccountingTaskView[] {
  const ids = accountingTaskViewsByWorkstream[workstream];
  return ids
    .map((id) => accountingTaskViews.find((view) => view.id === id))
    .filter((view): view is AccountingTaskView => Boolean(view));
}

function buildAccountingTaskMapCards({
  workstream,
  breakCount,
  runCount,
  profileCount,
  hasSelectedReconciliation,
  configurationStatusLabel
}: {
  workstream: AccountingWorkstream;
  breakCount: number;
  runCount: number;
  profileCount: number;
  hasSelectedReconciliation: boolean;
  configurationStatusLabel: string;
}): AccountingTaskMapCard[] {
  return [
    {
      id: "close",
      label: "Close Posture",
      value: `${runCount} runs`,
      detail: "Metrics, cash-flow evidence, and operations control center.",
      href: "#accounting-posture",
      tone: workstream === "ledger" || workstream === "reporting" ? "primary" : "supporting"
    },
    {
      id: "exceptions",
      label: "Exceptions",
      value: `${breakCount} breaks`,
      detail: "Statement runs, open breaks, detail evidence, and resolution queue.",
      href: workstream === "reconciliation" ? "#accounting-exceptions" : WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
      tone: workstream === "reconciliation" ? "primary" : "supporting"
    },
    {
      id: "ledger",
      label: "Ledger Review",
      value: hasSelectedReconciliation ? "Selected run" : "No run",
      detail: "Trial balance, basis bridge, transaction preview, and ledger impact.",
      href: workstream === "ledger" ? "#trial-balance-title" : WORKSTATION_ROUTE_CATALOG.accountingLedger,
      tone: workstream === "ledger" ? "primary" : "supporting"
    },
    {
      id: "reports",
      label: "Reports",
      value: `${profileCount} profiles`,
      detail: "Report profiles, export readiness, and retained evidence handoff.",
      href: "#accounting-reporting",
      tone: workstream === "reporting" ? "primary" : "supporting"
    },
    {
      id: "security-master",
      label: "Security Master",
      value: workstream === "security-master" ? "Active" : "Available",
      detail: "Search, identity, conflicts, schedules, lots, and trading parameters.",
      href: workstream === "security-master" ? "#security-master-search" : WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster,
      tone: workstream === "security-master" ? "primary" : "supporting"
    },
    {
      id: "configure",
      label: "Configure",
      value: configurationStatusLabel,
      detail: "Basis, mapping, validation, and audit readiness before activation.",
      href: workstream === "configure" ? "#accounting-configure-heading" : WORKSTATION_ROUTE_CATALOG.accountingConfigure,
      tone: workstream === "configure" ? "primary" : "muted"
    }
  ];
}

function AccountingTaskMap({ label, cards }: { label: string; cards: AccountingTaskMapCard[] }) {
  return (
    <section aria-label={label} className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
      {cards.map((card) => {
        const content = (
          <>
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <div className="text-sm font-semibold text-foreground">{card.label}</div>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">{card.detail}</p>
              </div>
              <span
                className={cn(
                  "shrink-0 rounded-sm border px-2 py-1 font-mono text-[10px] uppercase tracking-[0.14em]",
                  card.tone === "primary" ? "border-primary/35 bg-primary/10 text-primary" : "",
                  card.tone === "supporting" ? "border-border/70 bg-secondary/30 text-muted-foreground" : "",
                  card.tone === "muted" ? "border-border/60 bg-background text-muted-foreground" : ""
                )}
              >
                {card.value}
              </span>
            </div>
          </>
        );
        const className = cn(
          "block rounded-lg border px-4 py-3 text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
          card.tone === "primary" ? "border-primary/40 bg-primary/10 hover:bg-primary/15" : "",
          card.tone === "supporting" ? "border-border/70 bg-secondary/25 hover:bg-secondary/40" : "",
          card.tone === "muted" ? "border-border/60 bg-background/70 hover:bg-secondary/20" : ""
        );

        if (card.href.startsWith("#")) {
          return (
            <a key={card.id} href={card.href} className={className}>
              {content}
            </a>
          );
        }

        return (
          <Link key={card.id} to={card.href} className={className}>
            {content}
          </Link>
        );
      })}
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
