import { BookCheck, Landmark, Network, RefreshCcw, Search, ShieldCheck, WalletCards } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { useEffect, useMemo, useState } from "react";
import { AccountingTaskModeStrip } from "@/components/meridian/accounting-task-mode-strip";
import { MetricCard } from "@/components/meridian/metric-card";
import { DenseDataTable, EntitySummary, ToolbarStrip, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { FinancialRecordExplorerShell } from "@/components/meridian/financial-record-explorer";
import { WorkspaceFilterBar } from "@/components/meridian/workspace-primitives";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { LotsTrackerPanel, SecurityDetailsPanel } from "@/components/meridian/security-details-tracker";
import { buildAccountingTaskModes } from "@/lib/accounting-task-modes";
import { AccountingApprovalsWorkstream } from "@/screens/accounting-approvals-workstream";
import { CapitalAccountWorkbenchPanel } from "@/screens/accounting-capital-account-workbench-panel";
import { CloseCommandCenterPanel } from "@/screens/accounting-close-command-center-panel";
import { AccountingCloseReportPackagePanel } from "@/screens/accounting-close-report-package-panel";
import { AccountingConfigurationPanel } from "@/screens/accounting-configuration-panel";
import { AccountingLedgerExplorerPanel } from "@/screens/accounting-ledger-explorer-panel";
import { ManualJournalEntryWorkbenchPanel } from "@/screens/accounting-manual-journal-entry-workbench-panel";
import { OperationalExceptionWorkbenchPanel } from "@/screens/accounting-operational-exception-workbench-panel";
import { AccountingReconciliationCaseworkPanel, CalibrationSummaryPanel, ReconciliationQueueSummaryCard } from "@/screens/accounting-reconciliation-casework-panel";
import {
  CorporateActionsPanel,
  InstrumentPassportPanel,
  ReferenceDataWorkbenchPanel,
  SecurityOpenLotReadModelPanel,
  SecuritySchedulesPanel,
  TradingParametersPanel
} from "@/screens/accounting-security-master-panels";
import { AccountingSystemReconciliationPanel, mergeExternalGlExportPackage } from "@/screens/accounting-system-reconciliation-panel";
import { AccountingWorkflowLaunchPanel } from "@/screens/accounting-workflow-launch-panel";
import {
  certifyAccountingSystemExportPackage,
  createAccountingSystemExportPackage,
  getAccountingSystemExportPackageManifest,
  getAccountingSystemMappingProfiles,
  getAccountingSystemProviders,
  getFinancialOperationsCommandCenter,
  getLatestAccountingSystemImport,
  getLatestAccountingSystemReconciliation,
  getFinancialRecordExplorer,
  getOperationsContinuityWorkflow,
  getOperationsContinuityWorkflows,
  listAccountingSystemExportPackages,
  previewAccountingSystemImport,
  saveFinancialRecordExplorerView
} from "@/lib/api";
import { cn } from "@/lib/utils";
import { cashFlowBadgeClass, cashFlowTextClass, reportingBadgeClass } from "@/screens/accounting-screen.styles";
import { workspaceForPath } from "@/lib/workspace";
import {
  buildAccountingLoadingViewState,
  buildCloseCommandCenterViewState,
  buildAccountingWorkflowLaunchViewState,
  resolveAccountingWorkstream,
  SECURITY_IDENTITY_DETAIL_PANEL_ID,
  useAccountingCloseReportPackageViewModel,
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
  AccountingWorkstream,
  ReconciliationBreakRowViewModel,
  SecuritySearchResultRowViewModel
} from "@/screens/accounting-screen.view-model";
import type {
  AccountingSystemImportDetail,
  AccountingSystemExportPackageRequest,
  ExternalGlExportPackage,
  ExternalGlExportPackageManifest,
  ExternalGlMappingProfile,
  AccountingSystemProvider,
  AccountingSystemReconciliationSummary,
  AccountingWorkspaceResponse,
  FinancialRecordExplorerDto,
  FinancialRecordExplorerSavedViewSaveRequestDto,
  FinancialOperationsCommandCenter,
  MultiAssetCoverageSummary,
  OperationsContinuityWorkflow,
  OperationsContinuityWorkflowSummary
} from "@/types";

interface AccountingScreenProps {
  data: AccountingWorkspaceResponse | null;
  multiAssetCoverage?: MultiAssetCoverageSummary | null;
}

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

const focusCopy: Record<string, { title: string; description: string }> = {
  "close-cockpit": {
    title: "Close Cockpit",
    description: "Blocked close work, owner accountability, retained output, next action, and proof stay first in the Accounting lane."
  },
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
    description: "Manual journal entry drafts, line-level Security Master attribution, GL account picks, balancing validation, and approval submission stay governed."
  },
  "capital-accounts": {
    title: "Capital Account Workbench",
    description: "Investor-level capital account evidence, allocation rules, statement lineage, restatement support, and audit drill-throughs stay governed."
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
  },
  evidence: {
    title: "Accounting evidence",
    description: "Retained close, reconciliation, journal, report, and audit documents remain review-only support for the Accounting lane."
  }
};

function parseCloseWorkflowQuery(search: string): { fundProfileId?: string; fundAccountId?: string; ledgerBookId?: string; periodId?: string; status?: string } {
  const params = new URLSearchParams(search);
  return {
    fundProfileId: normalizeOptionalQueryValue(params.get("fundProfileId")),
    fundAccountId: normalizeOptionalQueryValue(params.get("fundAccountId")),
    ledgerBookId: normalizeOptionalQueryValue(params.get("ledgerBookId")),
    periodId: normalizeOptionalQueryValue(params.get("periodId")),
    status: normalizeOptionalQueryValue(params.get("workflowStatus"))
  };
}

function normalizeOptionalQueryValue(value: string | null): string | undefined {
  const normalized = value?.trim();
  return normalized ? normalized : undefined;
}

function selectCloseWorkflowSummary(
  rows: OperationsContinuityWorkflowSummary[],
  query: { fundProfileId?: string; fundAccountId?: string; ledgerBookId?: string; periodId?: string; status?: string }
): OperationsContinuityWorkflowSummary | null {
  const sorted = [...rows].sort((left, right) => right.updatedAtUtc.localeCompare(left.updatedAtUtc));
  const scopedRows = sorted.filter((row) =>
    matchesOptionalValue(row.fundAccountId, query.fundAccountId) &&
    matchesOptionalValue(row.ledgerBookId ?? null, query.ledgerBookId) &&
    matchesOptionalValue(row.periodId, query.periodId) &&
    matchesOptionalValue(row.status, query.status)
  );

  if ((query.fundProfileId || query.fundAccountId || query.ledgerBookId || query.periodId || query.status) && scopedRows.length === 0) {
    return null;
  }

  return scopedRows[0] ?? sorted[0] ?? null;
}

function matchesOptionalValue(actual: string | null, expected: string | undefined): boolean {
  return expected === undefined || (actual?.localeCompare(expected, undefined, { sensitivity: "accent" }) ?? -1) === 0;
}

function formatApprovalError(err: unknown, fallback: string): string {
  return err instanceof Error ? err.message || fallback : fallback;
}

export function AccountingScreen({ data, multiAssetCoverage }: AccountingScreenProps) {
  const { pathname, search } = useLocation();
  const workstream = resolveAccountingWorkstream(pathname);
  const workspace = workspaceForPath(pathname);
  const closeWorkflowQuery = useMemo(() => parseCloseWorkflowQuery(search), [search]);
  const reconciliation = useAccountingReconciliationViewModel(data, workstream);
  const resolveDialog = useReconciliationResolveDialogViewModel(reconciliation.resolveBreak);
  const selectedReconciliation = reconciliation.selectedReconciliation;
  const selectedReconciliationDetail = reconciliation.detailView;
  const selectedReconciliationOpenBreakLabel = `${selectedReconciliation?.openBreakCount ?? 0} open break${selectedReconciliation?.openBreakCount === 1 ? "" : "s"}`;
  const selectedReconciliationOpenBreakTone = (selectedReconciliation?.openBreakCount ?? 0) === 0 ? "success" : "warning";
  const cashFlow = useAccountingCashFlowViewModel(data?.cashFlow ?? null, pathname, workstream);
  const reporting = useAccountingReportingViewModel(data?.reporting ?? null);
  const configuration = useAccountingConfigurationViewModel();
  const journalEntries = useManualJournalEntryWorkbenchViewModel(workstream === "journal-entries", undefined, search);
  const capitalAccountWorkbench = useCapitalAccountWorkbenchViewModel(workstream === "capital-accounts", search);
  const securityMaster = useSecurityMasterViewModel(workstream === "security-master");
  const [accountingSystemProviders, setAccountingSystemProviders] = useState<AccountingSystemProvider[]>([]);
  const [accountingSystemImport, setAccountingSystemImport] = useState<AccountingSystemImportDetail | null>(null);
  const [accountingSystemReconciliation, setAccountingSystemReconciliation] = useState<AccountingSystemReconciliationSummary | null>(null);
  const [accountingSystemMappingProfiles, setAccountingSystemMappingProfiles] = useState<ExternalGlMappingProfile[]>([]);
  const [accountingSystemExportPackage, setAccountingSystemExportPackage] = useState<ExternalGlExportPackage | null>(null);
  const [accountingSystemExportManifest, setAccountingSystemExportManifest] = useState<ExternalGlExportPackageManifest | null>(null);
  const [accountingSystemExportPackages, setAccountingSystemExportPackages] = useState<ExternalGlExportPackage[]>([]);
  const [accountingSystemExportBusy, setAccountingSystemExportBusy] = useState(false);
  const [accountingSystemCertifyBusy, setAccountingSystemCertifyBusy] = useState(false);
  const [accountingSystemActionMessage, setAccountingSystemActionMessage] = useState<string | null>(null);
  const [accountingSystemActionTone, setAccountingSystemActionTone] = useState<"success" | "warning" | "danger" | null>(null);
  const [accountingSystemLoading, setAccountingSystemLoading] = useState(false);
  const [accountingSystemError, setAccountingSystemError] = useState<string | null>(null);
  const [financialOperationsCommandCenter, setFinancialOperationsCommandCenter] = useState<FinancialOperationsCommandCenter | null>(null);
  const [financialOperationsCommandCenterLoading, setFinancialOperationsCommandCenterLoading] = useState(false);
  const [financialOperationsCommandCenterError, setFinancialOperationsCommandCenterError] = useState<string | null>(null);
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
  const isCloseCockpitWorkstream = workstream === "close-cockpit";
  const showCloseCockpitSections = isCloseCockpitWorkstream;
  const showExternalGlSections = isCloseCockpitWorkstream || workstream === "reconciliation";
  const showPostureSections = isCloseCockpitWorkstream;
  const showMultiAssetCoverageSections = isCloseCockpitWorkstream;
  const showAccountingReportingSections = isCloseCockpitWorkstream || workstream === "evidence" || workstream === "reporting";

  useEffect(() => {
    let cancelled = false;

    if (workstream === "ledger") {
      void getFinancialRecordExplorer("ledger")
        .then((ledgerResult) => {
          if (!cancelled) {
            setLedgerExplorer(ledgerResult);
          }
        })
        .catch(() => {
          if (!cancelled) {
            setLedgerExplorer(null);
          }
        });
    } else if (workstream === "security-master") {
      void getFinancialRecordExplorer("security-instrument")
        .then((securityResult) => {
          if (!cancelled) {
            setSecurityInstrumentExplorer(securityResult);
          }
        })
        .catch(() => {
          if (!cancelled) {
            setSecurityInstrumentExplorer(null);
          }
        });
    }

    return () => {
      cancelled = true;
    };
  }, [workstream]);

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
      const [providers, mappingProfiles] = await Promise.all([
        getAccountingSystemProviders(),
        getAccountingSystemMappingProfiles()
      ]);
      const selectedProviderId = providers.find((provider) => (
        provider.providerId === "quickbooks" && provider.state === "Available"
      ))?.providerId ?? "quickbooks-fixture";
      const [importDetail, reconciliationDetail] = await Promise.all([
        persistPreview
          ? previewAccountingSystemImport({ providerId: selectedProviderId, persistPreview: true })
          : getLatestAccountingSystemImport(),
        getLatestAccountingSystemReconciliation()
      ]);
      const exportPackages = reconciliationDetail
        ? await listAccountingSystemExportPackages({
          providerId: reconciliationDetail.providerId,
          fundProfileId: reconciliationDetail.fundProfileId,
          ledgerBookId: importDetail?.summary.ledgerBookId ?? null
        })
        : [];
      setAccountingSystemProviders(providers);
      setAccountingSystemMappingProfiles(mappingProfiles);
      setAccountingSystemImport(importDetail);
      setAccountingSystemReconciliation(reconciliationDetail);
      setAccountingSystemExportPackages(exportPackages);
      setAccountingSystemExportManifest(null);
    } catch (error) {
      const message = error instanceof Error ? error.message : "Unable to load external GL reconciliation.";
      setAccountingSystemError(message);
    } finally {
      setAccountingSystemLoading(false);
    }
  };

  useEffect(() => {
    if (!showExternalGlSections) {
      return;
    }

    void refreshAccountingSystem(false);
  }, [showExternalGlSections]);

  const createExternalGlExportPackage = async () => {
    if (!accountingSystemReconciliation) {
      setAccountingSystemActionMessage("Load external GL reconciliation before creating an export package.");
      setAccountingSystemActionTone("warning");
      return;
    }

    const selectedMappingProfile = accountingSystemMappingProfiles.find((profile) => (
      profile.providerId === accountingSystemReconciliation.providerId
    )) ?? accountingSystemMappingProfiles[0] ?? null;

    if (!selectedMappingProfile) {
      setAccountingSystemActionMessage("Create or certify an external GL mapping profile before export package preparation.");
      setAccountingSystemActionTone("warning");
      return;
    }

    setAccountingSystemExportBusy(true);
    setAccountingSystemActionMessage(null);
    setAccountingSystemActionTone(null);
    try {
      const evidenceLinks = Array.from(new Set([
        `external-gl-mapping-profile:${selectedMappingProfile.profileId}`,
        `external-gl-reconciliation:${accountingSystemReconciliation.reconciliationId}`,
        ...accountingSystemReconciliation.evidenceReferences,
        ...(accountingSystemImport?.summary.evidenceReferences ?? [])
      ]));
      const request: AccountingSystemExportPackageRequest = {
        actor: "browser-accounting-operator",
        providerId: accountingSystemReconciliation.providerId,
        fundProfileId: accountingSystemReconciliation.fundProfileId,
        ledgerBookId: accountingSystemImport?.summary.ledgerBookId ?? null,
        periodStart: accountingSystemReconciliation.periodStart,
        periodEnd: accountingSystemReconciliation.periodEnd,
        mappingProfileId: selectedMappingProfile.profileId,
        journalEntryIds: [],
        requireBalancedReconciliation: true,
        evidenceLinks,
        correlationId: `browser-external-gl-export:${accountingSystemReconciliation.reconciliationId}`
      };
      const exportPackage = await createAccountingSystemExportPackage(request);
      const manifest = await getAccountingSystemExportPackageManifest(exportPackage.exportPackageId);
      setAccountingSystemExportPackage(exportPackage);
      setAccountingSystemExportPackages((packages) => mergeExternalGlExportPackage(packages, exportPackage));
      setAccountingSystemExportManifest(manifest);
      setAccountingSystemActionMessage(`Export package ${exportPackage.exportPackageId} created as a guarded artifact.`);
      setAccountingSystemActionTone(exportPackage.validationIssues.length > 0 ? "warning" : "success");
    } catch (error) {
      setAccountingSystemActionMessage(formatApprovalError(error, "External GL export package could not be created."));
      setAccountingSystemActionTone("danger");
    } finally {
      setAccountingSystemExportBusy(false);
    }
  };

  const certifyExternalGlExportPackage = async () => {
    if (!accountingSystemExportPackage) {
      setAccountingSystemActionMessage("Create an external GL export package before certification.");
      setAccountingSystemActionTone("warning");
      return;
    }

    setAccountingSystemCertifyBusy(true);
    setAccountingSystemActionMessage(null);
    setAccountingSystemActionTone(null);
    try {
      const evidenceLinks = Array.from(new Set([
        ...accountingSystemExportPackage.evidenceLinks,
        ...(accountingSystemExportPackage.certification?.evidenceLinks ?? []),
        `external-gl-export-certification:${accountingSystemExportPackage.exportPackageId}`
      ]));
      const certified = await certifyAccountingSystemExportPackage({
        exportPackageId: accountingSystemExportPackage.exportPackageId,
        actor: "browser-accounting-controller",
        notes: `Certified external GL export package ${accountingSystemExportPackage.exportPackageId}.`,
        evidenceLinks,
        correlationId: `browser-external-gl-certify:${accountingSystemExportPackage.exportPackageId}`
      });
      const manifest = await getAccountingSystemExportPackageManifest(certified.exportPackageId);
      setAccountingSystemExportPackage(certified);
      setAccountingSystemExportPackages((packages) => mergeExternalGlExportPackage(packages, certified));
      setAccountingSystemExportManifest(manifest);
      setAccountingSystemActionMessage(`Certified external GL export package ${certified.exportPackageId}.`);
      setAccountingSystemActionTone("success");
    } catch (error) {
      setAccountingSystemActionMessage(formatApprovalError(error, "External GL export package could not be certified."));
      setAccountingSystemActionTone("danger");
    } finally {
      setAccountingSystemCertifyBusy(false);
    }
  };

  const refreshCloseWorkflow = async () => {
    if (!data) {
      setCloseWorkflow(null);
      setFinancialOperationsCommandCenter(null);
      return;
    }

    setCloseWorkflowLoading(true);
    setFinancialOperationsCommandCenterLoading(true);
    setCloseWorkflowError(null);
    setFinancialOperationsCommandCenterError(null);
    try {
      const [commandCenter, rows] = await Promise.all([
        getFinancialOperationsCommandCenter(closeWorkflowQuery).catch(err => {
          setFinancialOperationsCommandCenterError(formatApprovalError(err, "Financial Operations command center could not be loaded."));
          return null;
        }),
        getOperationsContinuityWorkflows(closeWorkflowQuery).catch(err => {
          setCloseWorkflowError(formatApprovalError(err, "Close workflow detail could not be loaded."));
          return [];
        })
      ]);
      setFinancialOperationsCommandCenter(commandCenter);
      const selected = selectCloseWorkflowSummary(rows, closeWorkflowQuery);
      if (!selected) {
        setCloseWorkflow(null);
        return;
      }

      const workflow = await getOperationsContinuityWorkflow(selected.workflowId);
      setCloseWorkflow(workflow);
    } catch (error) {
      setCloseWorkflow(null);
      setFinancialOperationsCommandCenter(null);
      setCloseWorkflowError(formatApprovalError(error, "Close workflow detail could not be loaded."));
    } finally {
      setCloseWorkflowLoading(false);
      setFinancialOperationsCommandCenterLoading(false);
    }
  };

  useEffect(() => {
    void refreshCloseWorkflow();
  }, [data, closeWorkflowQuery]);

  const refreshFinancialOperationsCommandCenter = async () => {
    if (!data) {
      setFinancialOperationsCommandCenter(null);
      return;
    }

    setFinancialOperationsCommandCenterLoading(true);
    setFinancialOperationsCommandCenterError(null);
    try {
      const commandCenter = await getFinancialOperationsCommandCenter({
        fundProfileId: closeWorkflowQuery.fundProfileId,
        ledgerBookId: closeWorkflowQuery.ledgerBookId,
        fundAccountId: closeWorkflowQuery.fundAccountId,
        periodId: closeWorkflowQuery.periodId
      });
      setFinancialOperationsCommandCenter(commandCenter);
    } catch (error) {
      setFinancialOperationsCommandCenter(null);
      setFinancialOperationsCommandCenterError(formatApprovalError(error, "Financial Operations command center could not be loaded."));
    } finally {
      setFinancialOperationsCommandCenterLoading(false);
    }
  };

  useEffect(() => {
    void refreshFinancialOperationsCommandCenter();
  }, [data, closeWorkflowQuery]);

  const closeCommandCenter = useMemo(
    () => data ? buildCloseCommandCenterViewState({
      data,
      commandCenter: financialOperationsCommandCenter,
      commandCenterLoading: financialOperationsCommandCenterLoading,
      commandCenterError: financialOperationsCommandCenterError,
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
      financialOperationsCommandCenter,
      financialOperationsCommandCenterError,
      financialOperationsCommandCenterLoading,
      multiAssetCoverage
    ]
  );
  const closeReportPackage = useAccountingCloseReportPackageViewModel(closeWorkflow);
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
  const accountingRecoveryFields = [
    { id: "workstream", label: "Workstream", value: focus.title },
    { id: "queue", label: "Queue", value: `${data.reconciliationQueue.length} runs` },
    { id: "breaks", label: "Breaks", value: `${data.breakQueue.length} open` },
    { id: "close", label: "Close", value: closeCommandCenter?.statusLabel ?? "Loading" },
    {
      id: "external-gl",
      label: "External GL",
      value: accountingSystemReconciliation
        ? `${accountingSystemReconciliation.matchedCount} matched / ${accountingSystemReconciliation.breakCount} breaks`
        : accountingSystemLoading ? "Loading" : "Not loaded"
    }
  ];
  const accountingTaskModes = buildAccountingTaskModes({
    pathname,
    data,
    closeCommandCenter,
    workflowLaunch
  });

  return (
    <div className="space-y-8">
      <section
        id="accounting-overview"
        role="region"
        aria-label={`${workspace.label} workbench context`}
        data-workstream={workstream}
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

      <WorkspaceFilterBar
        label="Accounting recovery navigator"
        searchLabel="Current Accounting route"
        searchValue={`${workspace.label} / ${focus.title}`}
        fields={accountingRecoveryFields}
        actions={
          <div className="flex flex-wrap gap-2" aria-label="Accounting recovery jump targets">
            <Button asChild size="sm" variant="outline">
              <Link to="/accounting#close-command-center">Close center</Link>
            </Button>
            <Button asChild size="sm" variant="outline">
              <Link to="/accounting/reconciliation#external-gl-reconciliation">External GL</Link>
            </Button>
            <Button asChild size="sm" variant="outline">
              <Link to="/accounting#accounting-posture">Posture</Link>
            </Button>
            <Button asChild size="sm" variant="outline">
              <Link to="/accounting/reconciliation">Exceptions</Link>
            </Button>
          </div>
        }
      />

      <AccountingTaskModeStrip modes={accountingTaskModes} />

      {workflowLaunch ? <AccountingWorkflowLaunchPanel view={workflowLaunch} /> : null}

      {showCloseCockpitSections ? (
        <CloseCommandCenterPanel
          view={closeCommandCenter}
          onRefresh={() => void refreshCloseWorkflow()}
        />
      ) : null}

      {showCloseCockpitSections ? (
        <AccountingCloseReportPackagePanel view={closeReportPackage} />
      ) : null}

      {showMultiAssetCoverageSections && multiAssetCoveragePanel ? (
        <Card className="panel-surface" role="region" aria-label="Multi-asset accounting coverage">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <div className="eyebrow-label">Multi-asset coverage</div>
                <CardTitle className="mt-2 text-base">Accounting, reconciliation, and close readiness</CardTitle>
                <CardDescription>
                  Asset-class readiness is supplied by portfolio coverage evidence and rendered without Accounting-local rules.
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

      {showExternalGlSections ? (
        <AccountingSystemReconciliationPanel
          providers={accountingSystemProviders}
          importDetail={accountingSystemImport}
          reconciliation={accountingSystemReconciliation}
          mappingProfiles={accountingSystemMappingProfiles}
          exportPackage={accountingSystemExportPackage}
          exportManifest={accountingSystemExportManifest}
          exportPackages={accountingSystemExportPackages}
          exportBusy={accountingSystemExportBusy}
          certifyBusy={accountingSystemCertifyBusy}
          actionMessage={accountingSystemActionMessage}
          actionTone={accountingSystemActionTone}
          loading={accountingSystemLoading}
          error={accountingSystemError}
          onRefresh={() => void refreshAccountingSystem(true)}
          onCreateExportPackage={() => void createExternalGlExportPackage()}
          onCertifyExportPackage={() => void certifyExternalGlExportPackage()}
        />
      ) : null}

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

      {showPostureSections ? (
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
      ) : null}

      {workstream === "reconciliation" ? (
        <AccountingReconciliationCaseworkPanel
          comparisonView={reconciliation.comparisonView}
          statementRunsView={reconciliation.statementRunsView}
          queuePanelView={reconciliation.queuePanelView}
          selectedStatementRunId={reconciliation.selectedRunId}
          selectedQueueRunId={selectedReconciliation?.runId ?? null}
          selectedDetail={selectedReconciliationDetail}
          detailActions={reconciliation.detailActions}
          onRefreshStatementRuns={reconciliation.refreshStatementRuns}
          onSelectRun={reconciliation.selectRun}
        />
      ) : null}

      {workstream === "ledger" && selectedReconciliation ? (
        <AccountingLedgerExplorerPanel
          selectedReconciliation={selectedReconciliation}
          selectedOpenBreakLabel={selectedReconciliationOpenBreakLabel}
          selectedOpenBreakTone={selectedReconciliationOpenBreakTone}
          trialBalanceView={reconciliation.trialBalanceView}
          transactionLabView={reconciliation.transactionLabView}
          reporting={reporting}
          explorer={ledgerExplorer}
          detailActions={reconciliation.detailActions}
          onSaveView={(request) => saveAccountingExplorerView("ledger", request)}
          onSelectAccountingBasis={reconciliation.selectAccountingBasis}
          onUpdateLedgerAccountFilter={reconciliation.updateLedgerAccountFilter}
          onSelectTrialBalanceRow={reconciliation.selectTrialBalanceRow}
          onRunTransactionLabPreview={reconciliation.runTransactionLabPreview}
        />
      ) : null}

      {showAccountingReportingSections ? (
        <section id="accounting-reporting" className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
          <ReconciliationQueueSummaryCard view={reconciliation.queuePanelView} />

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
      ) : null}

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
              <ReferenceDataWorkbenchPanel
                view={securityMaster.referenceDataWorkbenchView}
                onSelect={securityMaster.selectReferenceDataEndpoint}
              />
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
                <InstrumentPassportPanel view={securityMaster.instrumentPassportView} />
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
                <StatusBanner
                  role="alert"
                  tone="danger"
                  title={reconciliation.errorText}
                  detail={reconciliation.errorDetails.length > 0 ? (
                    <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                      {reconciliation.errorDetails.map((detail) => (
                        <li key={detail}>{detail}</li>
                      ))}
                    </ul>
                  ) : null}
                />
              )}
              {reconciliation.actionErrorText && (
                <StatusBanner
                  role="alert"
                  tone="danger"
                  title={reconciliation.actionErrorText}
                  detail={reconciliation.actionErrorDetails.length > 0 ? (
                    <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                      {reconciliation.actionErrorDetails.map((detail) => (
                        <li key={detail}>{detail}</li>
                      ))}
                    </ul>
                  ) : null}
                />
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
                      <FormRow
                        label={resolveDialog.active.label}
                        labelFor={resolveDialog.active.inputId}
                        hint={resolveDialog.active.helpText}
                      >
                        <Input
                          id={resolveDialog.active.inputId}
                          type="text"
                          required
                          autoFocus
                          aria-describedby={resolveDialog.active.helpId}
                          placeholder={resolveDialog.active.placeholder}
                          value={resolveDialog.active.rationale}
                          onChange={(e) => resolveDialog.updateRationale(e.target.value)}
                        />
                        <span id={resolveDialog.active.helpId} className="sr-only">
                          {resolveDialog.active.helpText}
                        </span>
                      </FormRow>
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
