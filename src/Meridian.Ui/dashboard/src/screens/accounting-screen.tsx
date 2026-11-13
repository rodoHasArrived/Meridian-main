import { BookCheck, Landmark, Network, RefreshCcw, Search, ShieldCheck, WalletCards } from "lucide-react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import "@/styles/accounting-screen.css";
import { selectCurrentDailyValuationSchedule, useAccountingCloseSources, type CloseWorkflowQuery } from "./accounting-screen.close-sources";
import { useValuationMarkPreview, ValuationMarkPreviewPanel } from "./accounting-screen.mark-preview";
import { formatCount } from "@/lib/format";
import { StatStrip } from "@/components/meridian/stat-strip";
import { CloseChecklistProgressChart, ReconciliationAgingChart } from "@/components/meridian/workstation-evidence-charts";
import { CloseChecklistHistoryChart } from "@/components/meridian/close-checklist-history-chart";
import { DenseDataTable, EntitySummary, ToolbarStrip, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { FinancialRecordExplorerShell } from "@/components/meridian/financial-record-explorer";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { PrepareNextPeriodPanel } from "@/screens/accounting-screen.prepare-next-period";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { TabPanel, Tabs } from "@/components/ui/tabs";
import { LotsTrackerPanel, SecurityDetailsPanel } from "@/components/meridian/security-details-tracker";
import { SecurityCurveWorkbench } from "@/components/meridian/security-curve-workbench";
import { SecurityCurveValuationPanel } from "@/components/meridian/security-curve-valuation-panel";
import { BondDiscoveryPanel } from "@/components/meridian/bond-discovery-panel";
import { BondAnalyticsPanel } from "@/components/meridian/bond-analytics-panel";
import { BondWorkflowPanel } from "@/components/meridian/bond-workflow-panel";
import { BondIncomePanel } from "@/components/meridian/bond-income-panel";
import { BondMigrationPreviewPanel } from "@/components/meridian/bond-migration-preview-panel";
import { SecurityRatesIngestionPanel } from "@/components/meridian/security-rates-ingestion-panel";
import { BondObservedFixingPanel } from "@/components/meridian/bond-observed-fixing-panel";
import type { BondObservedFixingReceipt } from "@/lib/api/bond-observed-fixings.api";
import { BondReferenceLadderPanel } from "@/components/meridian/bond-reference-ladder-panel";
import { CoveragePassportDrillIn } from "@/components/meridian/coverage-passport-drill-in";
import { ReconciliationComparisonPanel } from "@/components/accounting";
import { TieOutPanel } from "@/components/accounting/TieOutPanel";
import { approveAndPostDailyValuationBatch, certifyAccountingSystemExportPackage, configureDailyValuationSchedule, createAccountingSystemExportPackage, getAccountingSystemExportPackageManifest, getAccountingSystemMappingProfiles, getAccountingSystemProviders, getLatestAccountingSystemImport, getLatestAccountingSystemReconciliation, getFinancialRecordExplorer, listAccountingSystemExportPackages, previewAccountingSystemImport, runDueDailyValuationSchedules, saveFinancialRecordExplorerView } from "@/lib/api";
import { cn } from "@/lib/utils";
import { cashFlowTextClass } from "@/screens/accounting-screen.styles";
import { StatusBadge, toneToSeverity } from "@/design-system/status";
import { workspaceForPath } from "@/lib/workspace";
import { CapitalAccountWorkbenchPanel } from "@/screens/accounting-screen.capital-account-workbench-panel";
import { AccountingPostedLedgerSection } from "@/screens/accounting-screen.posted-ledger-panel";
import { CorporateActionsPanel } from "@/screens/accounting-screen.corporate-actions-panel";
import { AccountingCloseReportPackagePanel, AccountingWorkflowLaunchPanel, CloseCommandCenterPanel } from "@/screens/accounting-screen.close-cockpit-panels";
import { SecuritySchedulesPanel } from "@/screens/accounting-screen.security-master-panels";
import { CalibrationSummaryPanel } from "@/screens/accounting-screen.calibration-panel";
import { InstrumentPassportPanel, ReferenceDataWorkbenchPanel, SecurityOpenLotReadModelPanel } from "@/screens/accounting-screen.security-master-detail-panels";
import { AccountingChip, AccountingWorkbenchContext } from "@/screens/accounting-screen.workbench-context";
import { OperationalExceptionWorkbenchPanel, ReconciliationQueueSummaryCard, TradingParametersPanel, reconciliationQueueColumns } from "@/screens/accounting-screen.operations-panels";
import { BankingPaymentApprovalsPanel } from "@/screens/accounting-screen.payment-approvals";
import { AuditChainIntegrityPanel } from "@/screens/accounting-screen.audit-integrity";
import { BondCutoverConsolePanel } from "@/screens/accounting-screen.bond-cutover";
import { ReconciliationReadinessPanel } from "@/screens/accounting-screen.reconciliation-readiness";
import { AccountingOnboardingPanel } from "@/screens/accounting-screen.onboarding-panel";
import { ReconciliationWorkbench, ReconciliationStatementRuns } from "./accounting-screen.reconciliation-workbench";
import { useReconciliationViewState } from "./reconciliation-view-state";
import { AccountOversightPanel } from "@/screens/accounting-screen.account-oversight";
import { buildAccountingSectionVisibility, buildAccountingTaskMode, resolveAccountingWorkstream } from "@/screens/accounting-screen.task-mode-view-model";
import { buildAccountingLoadingViewState, buildCloseCommandCenterViewState, buildAccountingWorkflowLaunchViewState, SECURITY_IDENTITY_DETAIL_PANEL_ID, useAccountingCloseReportPackageViewModel, useCapitalAccountWorkbenchViewModel, useAccountingConfigurationViewModel, useAccountingCashFlowViewModel, useManualJournalEntryWorkbenchViewModel, useAccountingReconciliationViewModel, useAccountingReportingViewModel, useReconciliationResolveDialogViewModel, useSecurityMasterViewModel } from "@/screens/accounting-screen.view-model";
import { buildMultiAssetCoveragePanel } from "@/screens/portfolio-screen.view-model";
import type { CloseCommandCenterViewState, SecuritySearchResultRowViewModel } from "@/screens/accounting-screen.view-model";
import type { AccountingSystemImportDetail, AccountingSystemExportPackageRequest, ExternalGlExportPackage, ExternalGlExportPackageManifest, ExternalGlMappingProfile, AccountingSystemProvider, AccountingSystemReconciliationSummary, AccountingWorkspaceResponse, FinancialRecordExplorerDto, FinancialRecordExplorerSavedViewSaveRequestDto, MultiAssetCoverageSummary, SessionInfo } from "@/types";
import { formatApprovalError } from "@/screens/accounting-screen.approvals";
import { eyebrowClassName, usesTrackedCaps } from "@/design-system/label-case";
import { applyCrossScreenCounts, applyOpenBreakDelta, selectCrossScreenCounts } from "@/lib/cross-screen-counts";
import { BREAK_QUEUE_VISIT_SURFACE, sinceLastVisitDelta, summarizeSinceLastVisit, useSinceLastVisit } from "@/lib/since-last-visit";
import { useSharedCrossScreenCounts } from "@/lib/cross-screen-counts-context";
import { IntercompanyAgreementsPanel } from "@/screens/accounting-screen.intercompany-agreements";
import { AccountingSystemReconciliationPanel, mergeExternalGlExportPackage } from "@/screens/accounting-screen.external-gl-panel";
import { AccountingCaseWorkbench } from "@/screens/accounting-screen.case-workbench";
import { AccountingConfigurationPanel } from "@/screens/accounting-screen.configuration-panel";
import { ManualJournalEntryWorkbenchPanel } from "@/screens/accounting-screen.manual-journal-panel";
import { AccountingApprovalsWorkstream } from "@/screens/accounting-screen.approvals-workstream";
import { AccountingValue } from "@/screens/accounting-screen.accounting-value";
import { buildSecurityMasterDrillInSearch, isSecurityMasterDrillInTabId, resolveExplorerSecurityId, resolveSecurityMasterDrillInTab } from "@/screens/accounting-screen.security-master-navigation";

interface AccountingScreenProps {
  data: AccountingWorkspaceResponse | null;
  multiAssetCoverage?: MultiAssetCoverageSummary | null;
  session?: SessionInfo | null;
}

function parseCloseWorkflowQuery(search: string): CloseWorkflowQuery {
  const params = new URLSearchParams(search);
  return {
    fundProfileId: normalizeOptionalQueryValue(params.get("fundProfileId")),
    entityId: normalizeOptionalQueryValue(params.get("entityId")),
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

export function AccountingScreen({ data, multiAssetCoverage, session = null }: AccountingScreenProps) {
  const [curvesOpen, setCurvesOpen] = useState(false);
  const [bondDiscoveryOpen, setBondDiscoveryOpen] = useState(false);
  const { pathname, search, hash } = useLocation();
  const sharedCounts = useSharedCrossScreenCounts();
  // The same baseline the break queue marks rows against (C3), read without recording a visit:
  // only opening the queue itself advances it, not any other Accounting route.
  const breakQueueVisit = useSinceLastVisit(BREAK_QUEUE_VISIT_SURFACE, undefined, { record: false });
  const navigate = useNavigate();
  const operatorIdentity = session?.displayName.trim() || null;
  const workstream = resolveAccountingWorkstream(pathname);
  const taskMode = buildAccountingTaskMode(pathname);
  const sectionVisibility = buildAccountingSectionVisibility(taskMode, hash);
  const workspace = workspaceForPath(pathname);
  const closeWorkflowQuery = useMemo(() => parseCloseWorkflowQuery(search), [search]);
  const [accountingSystemReconciliation, setAccountingSystemReconciliation] = useState<AccountingSystemReconciliationSummary | null>(null);
  const reconciliationViewState = useReconciliationViewState(workstream === "reconciliation" || workstream === "exceptions");
  const reconciliation = useAccountingReconciliationViewModel(data, workstream, undefined, accountingSystemReconciliation, operatorIdentity, workstream === "reconciliation" || workstream === "exceptions" ? reconciliationViewState : undefined);
  const resolveDialog = useReconciliationResolveDialogViewModel(reconciliation.resolveBreak);
  const selectedReconciliation = reconciliation.selectedReconciliation;
  const selectedReconciliationDetail = reconciliation.detailView;
  const cashFlow = useAccountingCashFlowViewModel(data?.cashFlow ?? null, pathname, workstream);
  const reporting = useAccountingReportingViewModel(data?.reporting ?? null);
  const configuration = useAccountingConfigurationViewModel(undefined, sectionVisibility.showConfiguration);
  const journalEntries = useManualJournalEntryWorkbenchViewModel(workstream === "journal-entries", undefined, search, session?.displayName.trim() ? JSON.stringify([session.displayName, session.role, session.environment]) : null);
  const capitalAccountWorkbench = useCapitalAccountWorkbenchViewModel(workstream === "capital-accounts", search);
  const securityMaster = useSecurityMasterViewModel(workstream === "security-master");
  const selectSecurityById = securityMaster.selectSecurity;
  const requestedSecurityId = new URLSearchParams(search).get("securityId")?.trim() || null;
  const selectSecurityWithRoute = useCallback((securityId: string, replace = false) => {
    const next = new URLSearchParams(search);
    if (requestedSecurityId && requestedSecurityId !== securityId) next.delete("bondAction");
    next.set("securityId", securityId);
    if (requestedSecurityId === securityId) void selectSecurityById(securityId);
    else navigate({ pathname, search: `?${next}`, hash }, { replace });
  }, [hash, navigate, pathname, requestedSecurityId, search, selectSecurityById]);
  const [observedFixingSelection, setObservedFixingSelection] = useState<{ securityId: string; receipts: BondObservedFixingReceipt[] } | null>(null);
  const observedFixingReceiptIds = useMemo(() => observedFixingSelection?.securityId === securityMaster.selectedSecurityId
    ? observedFixingSelection.receipts.map(receipt => receipt.receiptId) : [], [observedFixingSelection, securityMaster.selectedSecurityId]);
  const [ladderReturnAvailable, setLadderReturnAvailable] = useState(false);
  const ladderScroll = useRef<Array<{ element: Element; top: number; left: number }>>([]);
  const returnToLadder = () => {
    const selectedRow = ladderScroll.current[0]?.element;
    // The shell and local restoration must target the same row, regardless of effect timing.
    const returnHash = selectedRow instanceof HTMLElement && selectedRow.id ? `#${encodeURIComponent(selectedRow.id)}` : "#bond-ladders";
    navigate({ pathname, search: buildSecurityMasterDrillInSearch(search, "reference"), hash: returnHash },
      { replace: true, preventScrollReset: true });
    requestAnimationFrame(() => {
      for (const position of ladderScroll.current)
        position.element.scrollTo({ top: position.top, left: position.left, behavior: "instant" });
      if (selectedRow instanceof HTMLElement && selectedRow.isConnected)
        selectedRow.focus({ preventScroll: true });
    });
  };
  const selectSecurityFromExplorer = useCallback((recordId: string) => {
    const securityId = resolveExplorerSecurityId(recordId);
    if (!securityId) return;
    // The explorer selects its default row on mount. Synchronize that initial selection
    // without adding a Back destination that would immediately select and push it again.
    selectSecurityWithRoute(securityId, requestedSecurityId === null);
  }, [requestedSecurityId, selectSecurityWithRoute]);
  useEffect(() => {
    if (workstream === "security-master" && requestedSecurityId) {
      void selectSecurityById(requestedSecurityId);
    }
  }, [workstream, requestedSecurityId, selectSecurityById]);
  const [accountingSystemProviders, setAccountingSystemProviders] = useState<AccountingSystemProvider[]>([]);
  const [accountingSystemImport, setAccountingSystemImport] = useState<AccountingSystemImportDetail | null>(null);
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
  const {
    financialOperationsCommandCenter, financialOperationsCommandCenterLoading, financialOperationsCommandCenterError,
    privateCapitalCloseCockpit, dailyValuationSchedules, closeWorkflow, closeWorkflowLoading, closeWorkflowError,
    refreshCloseWorkflow,
  } = useAccountingCloseSources(closeWorkflowQuery, Boolean(data)
    && (sectionVisibility.showCloseCockpitLanding || sectionVisibility.showWorkflowDetails));
  const [activeDailyValuationCommand, setActiveDailyValuationCommand] = useState<NonNullable<CloseCommandCenterViewState["actionRows"][number]["command"]> | null>(null);
  const [dailyValuationBatchStatusText, setDailyValuationBatchStatusText] = useState<string | null>(null);
  const [securityInstrumentExplorer, setSecurityInstrumentExplorer] = useState<FinancialRecordExplorerDto | null>(null);
  const securityInstrumentExplorerView = useMemo(() => {
    if (!securityInstrumentExplorer) {
      return null;
    }

    const openConflictCount = securityMaster.openConflictCount;
    const conflictTone = openConflictCount > 0 ? "Warning" as const : "Success" as const;
    const coveragePosture = securityMaster.pageView.coveragePosture;
    const coverageTone = coveragePosture.tone === "success"
      ? "Success" as const
      : coveragePosture.tone === "warning"
        ? "Warning" as const
        : "Default" as const;
    const hasCoverageSummary = securityInstrumentExplorer.summaryItems.some((item) => item.label.toLowerCase().includes("coverage"));
    const hasConflictSummary = securityInstrumentExplorer.summaryItems.some((item) => item.label.toLowerCase().includes("conflict"));
    const hasConflictFilter = securityInstrumentExplorer.filters.some((filter) => filter.label.toLowerCase().includes("conflict"));
    return {
      ...securityInstrumentExplorer,
      sourceState: /fixture|no-host/i.test(securityInstrumentExplorer.sourceState)
        ? "Demo data: Security Master instrument coverage is loaded for review."
        : securityInstrumentExplorer.sourceState,
      scopeItems: securityInstrumentExplorer.scopeItems.map((item) => (
        item.label.toLowerCase() === "source"
          ? { ...item, value: "Security Master" }
          : item
      )),
      summaryItems: [
        ...securityInstrumentExplorer.summaryItems.map((item) => (
          item.label.toLowerCase().includes("coverage")
            ? { ...item, value: coveragePosture.label, detail: coveragePosture.detail, tone: coverageTone }
            : item.label.toLowerCase().includes("conflict")
            ? { ...item, value: formatCount(openConflictCount), tone: conflictTone }
            : item
        )),
        ...(hasCoverageSummary ? [] : [{
          label: "Coverage",
          value: coveragePosture.label,
          detail: coveragePosture.detail,
          tone: coverageTone
        }]),
        ...(hasConflictSummary ? [] : [{
          label: "Open conflicts",
          value: formatCount(openConflictCount),
          detail: openConflictCount > 0 ? "Identifier conflicts require operator review." : "No open identifier conflicts.",
          tone: conflictTone
        }])
      ],
      filters: [
        ...securityInstrumentExplorer.filters.map((filter) => (
          filter.label.toLowerCase().includes("conflict")
            ? { ...filter, value: `${openConflictCount} open`, tone: conflictTone }
            : filter
        )),
        ...(hasConflictFilter ? [] : [{
          filterId: "identifier-conflicts",
          label: "Conflicts",
          value: `${openConflictCount} open`,
          operator: "equals",
          tone: conflictTone
        }])
      ]
    };
  }, [securityInstrumentExplorer, securityMaster.openConflictCount, securityMaster.pageView.coveragePosture]);
  const identity = securityMaster.identityView;
  const identityPrimaryFields = identity?.summaryFields.filter((field) => field.label === "Asset class" || field.label === "Effective") ?? [];
  const identityTechnicalFields = identity?.summaryFields.filter((field) => !identityPrimaryFields.includes(field)) ?? [];
  const selectedSecurityEntry = securityMaster.selectedEntry;
  const securityMasterActiveTab = resolveSecurityMasterDrillInTab(search);
  const bondAction = new URLSearchParams(search).get("bondAction");
  const focusedBondAction = useRef<string | null>(null);
  useEffect(() => {
    if (securityMasterActiveTab !== "evidence" || !bondAction || !["analytics", "migration"].includes(bondAction)) {
      focusedBondAction.current = null;
      return;
    }
    if (!selectedSecurityEntry) return;
    const focusKey = `${selectedSecurityEntry.securityId.toLowerCase()}:${bondAction}`;
    if (focusedBondAction.current === focusKey) return;
    const target = document.getElementById(`bond-${bondAction}-action`);
    if (target) {
      const explorerHeader = target.closest(".workspace-section-band")?.querySelector(".workspace-section-subheader");
      target.style.scrollMarginTop = `${(explorerHeader?.getBoundingClientRect().height ?? 0) + 16}px`;
      target.focus({ preventScroll: true });
      target.scrollIntoView?.({ block: "start" });
      focusedBondAction.current = focusKey;
    }
  }, [bondAction, securityMasterActiveTab, selectedSecurityEntry]);
  const securityMasterDrillInTabs = useMemo(() => [
    {
      id: "identity",
      label: "Identity",
      count: identity ? String(identity.identifiers.length + identity.aliases.length) : undefined,
      panelId: "security-master-identity-tab-panel"
    },
    {
      id: "reference",
      label: "Reference data",
      count: securityMaster.referenceDataWorkbenchView.rows.length > 0 ? String(securityMaster.referenceDataWorkbenchView.rows.length) : undefined,
      panelId: "security-master-reference-tab-panel"
    },
    {
      id: "schedules",
      label: "Schedules",
      count: String(securityMaster.schedulesView.rows.length + securityMaster.corporateActionsView.rows.length),
      panelId: "security-master-schedules-tab-panel"
    },
    {
      id: "lots",
      label: "Lots",
      count: securityMaster.openLotReadModelView.rows.length > 0 ? String(securityMaster.openLotReadModelView.rows.length) : undefined,
      panelId: "security-master-lots-tab-panel"
    },
    {
      id: "passport",
      label: "Passport & controls",
      count: securityMaster.instrumentPassportView.statusLabel,
      panelId: "security-master-passport-tab-panel"
    },
    {
      id: "evidence",
      label: "Evidence",
      count: selectedSecurityEntry ? "Ready" : undefined,
      panelId: "security-master-evidence-tab-panel"
    }
  ], [
    identity,
    securityMaster.corporateActionsView.rows.length,
    securityMaster.instrumentPassportView.statusLabel,
    securityMaster.openLotReadModelView.rows.length,
    securityMaster.referenceDataWorkbenchView.rows.length,
    securityMaster.schedulesView.rows.length,
    selectedSecurityEntry
  ]);
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

  useEffect(() => {
    if (!sectionVisibility.showSecurityMaster) {
      return;
    }

    let cancelled = false;
    void getFinancialRecordExplorer("security-instrument").then((explorer) => {
      if (!cancelled) {
        setSecurityInstrumentExplorer(explorer);
      }
    }).catch(() => {
      if (!cancelled) {
        setSecurityInstrumentExplorer(null);
      }
    });

    return () => {
      cancelled = true;
    };
  }, [sectionVisibility.showSecurityMaster]);

  // Only the security-instrument explorer remains in Accounting; the ledger explorer moved
  // to the Strategy workspace with the run-scoped ledger it serves.
  async function saveAccountingExplorerView(
    explorerId: "security-instrument",
    request: FinancialRecordExplorerSavedViewSaveRequestDto
  ) {
    await saveFinancialRecordExplorerView(explorerId, request);
    setSecurityInstrumentExplorer(await getFinancialRecordExplorer(explorerId));
  }
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
    if (!sectionVisibility.showExternalGl) {
      return;
    }

    void refreshAccountingSystem(false);
  }, [sectionVisibility.showExternalGl]);

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
      setAccountingSystemActionMessage("Guarded export package created. Its identifier is available in technical details.");
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
      setAccountingSystemActionMessage("External GL export package certified.");
      setAccountingSystemActionTone("success");
    } catch (error) {
      setAccountingSystemActionMessage(formatApprovalError(error, "External GL export package could not be certified."));
      setAccountingSystemActionTone("danger");
    } finally {
      setAccountingSystemCertifyBusy(false);
    }
  };

  const effectiveDailyValuationStatus = privateCapitalCloseCockpit?.dailyValuationStatus
    ?? financialOperationsCommandCenter?.privateCapitalCloseCockpit?.dailyValuationStatus
    ?? null;
  const currentDailyValuationSchedule = useMemo(
    () => selectCurrentDailyValuationSchedule(dailyValuationSchedules, effectiveDailyValuationStatus, closeWorkflowQuery),
    [closeWorkflowQuery, dailyValuationSchedules, effectiveDailyValuationStatus]
  );

  const valuationMarkPreview = useValuationMarkPreview(currentDailyValuationSchedule);
  const runCloseCommand = async (
    command: NonNullable<CloseCommandCenterViewState["actionRows"][number]["command"]>
  ) => {
    const status = effectiveDailyValuationStatus;
    if ((command === "configure-daily-valuation-schedule" || command === "run-due-daily-valuation-schedules") && !valuationMarkPreview.isCurrent) {
      setDailyValuationBatchStatusText("Preview mark impact for this schedule before configuring or running valuation.");
      return;
    }
    const hasRetainedBatch = Boolean(status?.batchCorrelationId) && (status?.journalEntryIds.length ?? 0) > 0;
    if (command === "configure-daily-valuation-schedule" && !currentDailyValuationSchedule) {
      setDailyValuationBatchStatusText("No server-retained daily valuation schedule is loaded for this close scope.");
      return;
    }
    if (command === "configure-daily-valuation-schedule" &&
        (status?.state === "Running" || status?.state === "DraftReady" ||
          (status?.state === "Blocked" && hasRetainedBatch))) {
      setDailyValuationBatchStatusText("Complete or correct the retained daily valuation batch before reconfiguring its schedule.");
      return;
    }
    if (command === "run-due-daily-valuation-schedules" &&
        (!currentDailyValuationSchedule || !status?.isConfigured || !status.isEnabled || status.state !== "Scheduled")) {
      setDailyValuationBatchStatusText("The current close scope does not have an enabled Scheduled daily valuation configuration.");
      return;
    }
    if ((command === "approve-daily-valuation-batch" && status?.state !== "DraftReady") ||
        (command === "retry-daily-valuation-batch" && (status?.state !== "Blocked" || !hasRetainedBatch)) ||
        ((command === "approve-daily-valuation-batch" || command === "retry-daily-valuation-batch") &&
          (!status?.scheduleId || !status.fundProfileId || status.journalEntryIds.length === 0))) {
      setDailyValuationBatchStatusText("No retained daily valuation batch is available for this command.");
      return;
    }

    setActiveDailyValuationCommand(command);
    setDailyValuationBatchStatusText(null);
    try {
      if (command === "configure-daily-valuation-schedule") {
        const configured = await configureDailyValuationSchedule({
          ...currentDailyValuationSchedule!,
          actor: "close-cockpit-operator"
        });
        setDailyValuationBatchStatusText(
          `Configured daily valuation schedule ${configured.scheduleId} for ${configured.nextRunAtUtc}.`
        );
        await refreshCloseWorkflow();
        return;
      }

      if (command === "run-due-daily-valuation-schedules") {
        const result = await runDueDailyValuationSchedules();
        const currentRun = result.runs.find((run) => run.scheduleId === currentDailyValuationSchedule!.scheduleId);
        setDailyValuationBatchStatusText(currentRun
          ? `Daily valuation schedule ${currentRun.scheduleId} finished in ${currentRun.state}: ${currentRun.summary}`
          : result.runs.length > 0
            ? `Ran ${result.runs.length} due daily valuation schedule(s); the current schedule was not due.`
            : "No daily valuation schedules were due for the current tenant scope.");
        await refreshCloseWorkflow();
        return;
      }

      const isRetry = command === "retry-daily-valuation-batch";
      const result = await approveAndPostDailyValuationBatch({
        scheduleId: status!.scheduleId!,
        fundProfileId: status!.fundProfileId!,
        actor: "close-cockpit-operator",
        notes: isRetry
          ? "Retried the incomplete retained daily valuation batch from the controller close cockpit."
          : "Approved the complete retained daily valuation batch from the controller close cockpit.",
        evidenceLinks: status!.evidenceLinks
          .map((link) => link.route)
          .filter((route): route is string => Boolean(route)),
        tenantId: status!.tenantId ?? null,
        companyId: status!.companyId ?? null
      });
      setDailyValuationBatchStatusText(result.isComplete
        ? `${isRetry ? "Retried and posted" : "Posted"} all ${result.postedJournalEntryIds.length} daily valuation drafts in batch ${result.batchCorrelationId}.`
        : `Daily valuation batch ${isRetry ? "retry " : ""}remains blocked: ${result.blockers.join(" ")}`);
      await refreshCloseWorkflow();
    } catch (error) {
      const fallback = command === "configure-daily-valuation-schedule"
        ? "Daily valuation schedule could not be configured."
        : command === "run-due-daily-valuation-schedules"
          ? "Due daily valuation schedules could not be run."
          : command === "retry-daily-valuation-batch"
            ? "Daily valuation batch correction and retry could not complete."
            : "Daily valuation batch could not be approved and posted.";
      setDailyValuationBatchStatusText(formatApprovalError(error, fallback));
    } finally {
      setActiveDailyValuationCommand(null);
    }
  };

  const closeCommandCenter = useMemo(
    () => data ? buildCloseCommandCenterViewState({
      data,
      commandCenter: financialOperationsCommandCenter
        ? {
          ...financialOperationsCommandCenter,
          privateCapitalCloseCockpit: privateCapitalCloseCockpit
            ?? financialOperationsCommandCenter.privateCapitalCloseCockpit
        }
        : null,
      commandCenterLoading: financialOperationsCommandCenterLoading,
      commandCenterError: financialOperationsCommandCenterError,
      workflow: closeWorkflow,
      workflowLoading: closeWorkflowLoading,
      workflowError: closeWorkflowError,
      accountingSystemProviders,
      accountingSystemImport,
      accountingSystemReconciliation,
      multiAssetCoverage,
      currentDailyValuationSchedule
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
      multiAssetCoverage,
      privateCapitalCloseCockpit,
      currentDailyValuationSchedule
    ]
  );
  const closeReportPackage = useAccountingCloseReportPackageViewModel(closeWorkflow, undefined, financialOperationsCommandCenter, closeWorkflowQuery);
  const workflowLaunch = useMemo(
    () => data ? buildAccountingWorkflowLaunchViewState({
      data,
      workstream,
      closeCommandCenter,
      taskMode
    }) : null,
    [closeCommandCenter, data, taskMode, workstream]
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
              <div className={eyebrowClassName(loading.eyebrow)}>{loading.eyebrow}</div>
              <CardTitle id={loading.titleId} className="mt-2 text-base">{loading.title}</CardTitle>
              <CardDescription id={loading.detailId} className="mt-2">{loading.detail}</CardDescription>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <AccountingChip label="Route" value={loading.routeLabel} />
              <AccountingChip label="Task mode" value={loading.workstreamLabel} />
            </div>
          </div>
          <div className="mt-4 grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(18rem,0.38fr)]">
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
              <div className={cn("text-xs font-semibold text-muted-foreground", usesTrackedCaps(loading.statusItemsLabel) && "uppercase")}>{loading.statusItemsLabel}</div>
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
              <div className={cn("text-xs font-semibold text-muted-foreground", usesTrackedCaps(loading.actionsLabel) && "uppercase")}>{loading.actionsLabel}</div>
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

  const focus = taskMode;
  const multiAssetCoveragePanel = buildMultiAssetCoveragePanel(multiAssetCoverage);

  return (
    <div className="space-y-5">
      {/* Route heading first ("where am I?"); workspace-wide metrics follow. */}
      <AccountingWorkbenchContext
        workspace={workspace}
        workstream={workstream}
        taskMode={taskMode}
      />

      <StatStrip
        metrics={applyOpenBreakDelta(
          applyCrossScreenCounts(data.metrics, selectCrossScreenCounts({ shared: sharedCounts, breakQueue: data.breakQueue, openBreakCount: data.workspace?.openBreaks })),
          sinceLastVisitDelta(summarizeSinceLastVisit(data.breakQueue ?? [], breakQueueVisit.baseline))
        )}
        label="Accounting headline metrics"
        source="Accounting workspace"
        figureSource="accounting"
      />
      {currentDailyValuationSchedule && (sectionVisibility.showCloseCockpitLanding || sectionVisibility.showWorkflowDetails) ? <ValuationMarkPreviewPanel preview={valuationMarkPreview} /> : null}

      {workstream === "reconciliation" ? (
        <>
          <ReconciliationWorkbench reconciliation={reconciliation} resolveDialog={resolveDialog} controls={reconciliationViewState} selectionNotice={reconciliation.selectionNotice}
            runSelector={<ReconciliationStatementRuns reconciliation={reconciliation} initiallyOpen={hash === "#statement-runs"} />}
          />
          <TechnicalDetails label="Break aging"><ReconciliationAgingChart breaks={reconciliation.sourceRows} /></TechnicalDetails>
          <AccountOversightPanel />
          <TechnicalDetails id="accounting-history" label="Calibration and history"><CalibrationSummaryPanel view={reconciliation.calibrationView} /></TechnicalDetails>
        </>
      ) : null}

      {/* On the landing the queue leads and preparation follows it (below); elsewhere it stays here. */}
      {sectionVisibility.showWorkflowDetails && !sectionVisibility.showCloseCockpitLanding ? <PrepareNextPeriodPanel initialWorkflowId={closeWorkflow?.workflowId} /> : null}

      {sectionVisibility.showCloseCockpitLanding ? (
        <>
          {closeWorkflow?.closeChecklist ? <TechnicalDetails label="Close task progress"><CloseChecklistProgressChart tasks={closeWorkflow.closeChecklist} /></TechnicalDetails> : null}
          <AccountingCaseWorkbench
            breakRows={reconciliation.rows}
            selectedBreakId={reconciliation.selectedBreakId}
            selectedBreakDetail={reconciliation.selectedDetail}
            closeCommandCenter={closeCommandCenter}
            detailActions={reconciliation.detailActions}
            onSelectBreak={reconciliation.selectBreak}
            onAssignBreak={(breakId) => {
              reconciliation.selectBreak(breakId);
              void reconciliation.assignBreak(breakId);
            }}
            onResolveBreak={(breakId) => {
              reconciliation.selectBreak(breakId);
              resolveDialog.open(breakId, "Resolved");
            }}
            onDismissBreak={(breakId) => {
              reconciliation.selectBreak(breakId);
              resolveDialog.open(breakId, "Dismissed");
            }}
          />
          <PrepareNextPeriodPanel initialWorkflowId={closeWorkflow?.workflowId} />
          {closeWorkflow ? <CloseChecklistHistoryChart workflowId={closeWorkflow.workflowId} workflowVersion={closeWorkflow.version} /> : null}
          <TechnicalDetails label="System details" className="panel-surface">
            <div className="space-y-4">
              {workflowLaunch ? <AccountingWorkflowLaunchPanel view={workflowLaunch} /> : null}
              {closeCommandCenter ? (
                <CloseCommandCenterPanel
                  view={closeCommandCenter}
                  onRefresh={() => void refreshCloseWorkflow()}
                  onCommand={(command) => void runCloseCommand(command)}
                  activeCommand={activeDailyValuationCommand}
                  commandStatusText={dailyValuationBatchStatusText}
                />
              ) : null}
              <AccountingCloseReportPackagePanel view={closeReportPackage} />
            </div>
          </TechnicalDetails>
        </>
      ) : (
        <>
      {sectionVisibility.showWorkflowDetails && workflowLaunch ? <AccountingWorkflowLaunchPanel view={workflowLaunch} /> : null}

      {sectionVisibility.showWorkflowDetails && closeCommandCenter ? (
        <CloseCommandCenterPanel
          view={closeCommandCenter}
          onRefresh={() => void refreshCloseWorkflow()}
          onCommand={(command) => void runCloseCommand(command)}
          activeCommand={activeDailyValuationCommand}
          commandStatusText={dailyValuationBatchStatusText}
        />
      ) : null}

      {sectionVisibility.showWorkflowDetails ? <AccountingCloseReportPackagePanel view={closeReportPackage} /> : null}

      {sectionVisibility.showMultiAssetCoverage && multiAssetCoveragePanel ? (
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
                  <CoveragePassportDrillIn assetClass={item.assetClass} />
                </div>
              ))}
            </div>
            <a href={multiAssetCoveragePanel.evidenceRoute} className="text-xs font-medium text-primary hover:underline">
              {multiAssetCoveragePanel.evidenceRouteLabel}
            </a>
          </CardContent>
        </Card>
      ) : null}

      {sectionVisibility.showExternalGl ? (
      <AccountingOnboardingPanel />
      ) : null}

      {sectionVisibility.showExternalGl ? (
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

      {sectionVisibility.showConfiguration ? (
        <AccountingConfigurationPanel view={configuration} />
      ) : null}

      {sectionVisibility.showConfiguration ? (
        <IntercompanyAgreementsPanel key={JSON.stringify([session?.displayName, session?.role, session?.environment, search])} initialEntityId={closeWorkflowQuery.entityId ?? ""} initialAgreementId={new URLSearchParams(search).get("agreementId")} initialLedgerBookId={new URLSearchParams(search).get("ledgerBookId") ?? undefined} />
      ) : null}

      {sectionVisibility.showConfiguration ? (
        <AuditChainIntegrityPanel />
      ) : null}

      {sectionVisibility.showConfiguration ? (
        <BondCutoverConsolePanel />
      ) : null}

      {sectionVisibility.showJournalEntries ? (
        <ManualJournalEntryWorkbenchPanel key={JSON.stringify([session?.displayName, session?.role, session?.environment])} view={journalEntries} search={search} />
      ) : null}

      {sectionVisibility.showCapitalAccounts ? (
        <CapitalAccountWorkbenchPanel view={capitalAccountWorkbench} />
      ) : null}

      {sectionVisibility.showApprovals ? (
        <AccountingApprovalsWorkstream />
      ) : null}

      {sectionVisibility.showApprovals ? (
        <BankingPaymentApprovalsPanel entityId={closeWorkflowQuery.entityId} />
      ) : null}

      {sectionVisibility.showExceptionWorkbench ? (
        <OperationalExceptionWorkbenchPanel view={reconciliation.exceptionWorkbench} />
      ) : null}

      {sectionVisibility.showPosture ? (
      <section id="accounting-posture" className="workspace-section-band" aria-labelledby="accounting-posture-heading">
        <div className="workspace-section-subheader">
          <div className="min-w-0">
            <p className="eyebrow-label">Posture</p>
            <h3 id="accounting-posture-heading" className="workspace-section-title">Accounting close posture</h3>
            <p className="workspace-section-summary">Close metrics, control center, cash flow, and workspace context stay grouped for monitoring.</p>
          </div>
          <a className="workspace-section-jump" href="#accounting-exceptions">Exceptions</a>
        </div>

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
              <div
                className="grid gap-3 sm:grid-cols-2"
                role="group"
                aria-label="Operations control center scope"
              >
                <AccountingValue
                  label="Portfolio coverage"
                  value={data.controlCenter.portfolioFilterOptions.join(", ") || "All portfolios"}
                />
                <AccountingValue
                  label="Account coverage"
                  value={data.controlCenter.accountFilterOptions.join(", ") || "All accounts"}
                />
              </div>
              <p className="text-xs text-muted-foreground">
                Control-center measures are aggregate service results. Meridian does not present a selector until the service supplies per-scope measures.
              </p>
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
            <div className="eyebrow-label">{workspace.label}</div>
            <CardTitle className="flex items-center gap-2">
              <ShieldCheck className="h-5 w-5 text-primary" />
              {focus.label}
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
                <div className={eyebrowClassName(cashFlow.eyebrow)}>{cashFlow.eyebrow}</div>
                <CardTitle>{cashFlow.title}</CardTitle>
                <CardDescription className="mt-2 text-muted-foreground">
                  {cashFlow.description}
                </CardDescription>
              </div>
              <StatusBadge
                aria-label={cashFlow.statusAriaLabel}
                // Cash-flow "default" was the neutral chip, not the primary one.
                status={cashFlow.statusTone === "default" ? "info" : toneToSeverity(cashFlow.statusTone)}
                dot={false}
                label={cashFlow.statusLabel}
              />
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

      {sectionVisibility.showReconciliation ? (
        <TechnicalDetails label="Readiness, source comparison, and run evidence" open={hash === "#accounting-exceptions"}>
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
          <div className="xl:col-span-2">
            <ReconciliationReadinessPanel />
          </div>

          <div className="space-y-3 xl:col-span-2">
            <ReconciliationComparisonPanel view={reconciliation.comparisonView} />
            {/* Proof of the summary above, built from the same numbers and the same balanced rule.
                Its heading stays at the panel default (h3), level with the sibling cards in this band. */}
            {reconciliation.comparisonView.tieOut ? (
              <TieOutPanel {...reconciliation.comparisonView.tieOut} />
            ) : null}
          </div>



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
                <StatusBanner
                  role="status"
                  tone="warning"
                  title="Reconciliation queue empty"
                  detail={reconciliation.queuePanelView.emptyText}
                />
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
        </TechnicalDetails>
      ) : null}

      {sectionVisibility.showLedgerExplorer ? <AccountingPostedLedgerSection workstream={workstream} /> : null}

      {/*
        The strategy-run ledger explorer moved to the Strategy workspace
        (/strategy/run-ledger). A run's simulated ledger is a run artifact; keeping it here
        put it under the name operators read as the fund's book of record. The Accounting
        ledger surface above now renders the posted journal. Reporting exports and Transaction
        Lab stayed — both are accounting work, not run evidence, and only shared the removed
        container. Transaction Lab in particular previews the accounting effect of a proposed
        transaction, and this is its only entry point anywhere in the workstation.
      */}
      {sectionVisibility.showLedgerExplorer ? (
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle>{reconciliation.transactionLabView.title}</CardTitle>
            <CardDescription>{reconciliation.transactionLabView.description}</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="flex items-start justify-between gap-3">
              <p
                role={reconciliation.transactionLabView.statusRole}
                className={cn(
                  "flex-1 rounded-md border px-3 py-2 text-sm",
                  reconciliation.transactionLabView.statusTone === "default" ? "border-border/70 bg-secondary/25 text-muted-foreground" : "",
                  reconciliation.transactionLabView.statusTone === "success" ? "border-success/30 bg-success/10 text-success" : "",
                  reconciliation.transactionLabView.statusTone === "warning" ? "border-warning/30 bg-warning/10 text-warning" : "",
                  reconciliation.transactionLabView.statusTone === "danger" ? "border-danger/30 bg-danger/10 text-danger" : ""
                )}
              >
                {reconciliation.transactionLabView.statusText}
              </p>
              <Badge variant={reconciliation.transactionLabView.statusTone === "default" ? "outline" : reconciliation.transactionLabView.statusTone} dot>
                {reconciliation.transactionLabView.requestSummaryLabel}
              </Badge>
            </div>
            <div className="mt-3 grid grid-cols-2 gap-2 text-xs">
              <div className="rounded-md border border-border/70 bg-background px-3 py-2">
                <span className="block text-muted-foreground">Expected projection</span>
                <span className="mt-1 block font-mono text-foreground">{reconciliation.transactionLabView.journalLineCountLabel}</span>
              </div>
              <div className="rounded-md border border-border/70 bg-background px-3 py-2">
                <span className="block text-muted-foreground">Projected accounting effect</span>
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
              <div className="mt-3 space-y-2" aria-label="Transaction Lab projected trial-balance effect">
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
          </CardContent>
        </Card>
      ) : null}

      {sectionVisibility.showLedgerExplorer ? (
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle>Reporting exports</CardTitle>
            <CardDescription>Entry points for report and export handoff using existing export infrastructure.</CardDescription>
          </CardHeader>
          <CardContent className="flex flex-wrap items-center gap-2">
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
                  "w-full rounded-lg border px-3 py-2 text-sm",
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
      ) : null}

      {sectionVisibility.showReporting ? (
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
              <StatusBadge status="review" dot={false} label={reporting.countLabel} />
            </div>
          </CardHeader>
          <CardContent className="grid gap-4">
            <div className="min-w-0">
              <div className="mb-2 flex flex-wrap items-center justify-between gap-2 text-xs">
                <span className={cn("font-medium text-muted-foreground", usesTrackedCaps(reporting.listLabel) && "uppercase tracking-[0.14em]")}>{reporting.listLabel}</span>
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
                        "w-full rounded-lg border px-4 py-3 text-left transition-colors focus:outline-hidden focus:ring-2 focus:ring-primary/40",
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
                        <StatusBadge className="shrink-0" status="review" dot={false} label={profile.formatLabel} />
                      </div>
                      <p className="mt-2 line-clamp-2 text-sm leading-6 text-muted-foreground">{profile.description}</p>
                      <div className="mt-3 flex flex-wrap gap-2">
                        {profile.badges.map((badge) => (
                          <StatusBadge key={`${profile.id}-${badge.label}`} status={toneToSeverity(badge.tone)} dot={false} label={badge.label} />
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

            <div
              role="region"
              aria-label="Selected reporting profile detail"
              id={reporting.detailId}
              aria-live="polite"
              data-testid="reporting-profile-detail"
              className="min-w-0 overflow-hidden rounded-lg border border-border/70 bg-background/35 p-4"
            >
              <div className={eyebrowClassName(reporting.statusTitle)}>{reporting.statusTitle}</div>
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
            </div>
          </CardContent>
        </Card>
      </section>
      ) : null}

      {/* --- Security Master panel (shown when security-master workstream is active) --- */}
      {sectionVisibility.showSecurityMaster && (
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
          explorer={securityInstrumentExplorerView}
          activeRecordId={requestedSecurityId
            ? securityInstrumentExplorer?.rows.find((row) => resolveExplorerSecurityId(row.recordId)?.toLowerCase() === requestedSecurityId.toLowerCase())?.recordId ?? `security-instrument:${requestedSecurityId}`
            : undefined}
          onSaveView={(request) => saveAccountingExplorerView("security-instrument", request)}
          onSelectRecord={selectSecurityFromExplorer}
        >
        <section className="space-y-6">
          <div id="bond-ladders" className="scroll-mt-6">
            <BondReferenceLadderPanel onSelectSecurity={(securityId) => {
              ladderScroll.current = [];
              let element: Element | null = document.activeElement;
              while (element) {
                ladderScroll.current.push({ element, top: element.scrollTop, left: element.scrollLeft });
                element = element.parentElement;
              }
              setLadderReturnAvailable(true);
              const next = new URLSearchParams(search);
              next.set("securityId", securityId);
              next.set("tab", "evidence");
              next.delete("bondAction");
              if (requestedSecurityId === securityId) void selectSecurityById(securityId);
              navigate({ pathname, search: `?${next}`, hash: "#security-detail-page-title" }, { preventScrollReset: true });
            }} />
          </div>
          <section className="panel-surface-strong space-y-4 p-5" aria-label={securityMaster.pageView.ariaLabel}>
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div className="max-w-3xl">
                <div className={eyebrowClassName(securityMaster.pageView.eyebrow)}>{securityMaster.pageView.eyebrow}</div>
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
                  <div className={cn("text-[11px] font-semibold text-muted-foreground", usesTrackedCaps(metric.label) && "uppercase tracking-[0.14em]")}>{metric.label}</div>
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

          <div className="grid gap-4" aria-label="Security Master market inputs and discovery">
            <details className="rounded border border-border p-4" onToggle={(event) => setCurvesOpen(event.currentTarget.open)}>
              <summary className="cursor-pointer font-semibold">Curves: definitions, pillars and historical versions</summary>
              {curvesOpen ? <div className="mt-4"><SecurityCurveWorkbench key={JSON.stringify([search, session?.displayName, session?.role, session?.environment])} /></div> : null}
            </details>
            <details className="rounded border border-border p-4" onToggle={(event) => setBondDiscoveryOpen(event.currentTarget.open)}>
              <summary className="cursor-pointer font-semibold">Discover provider bonds and stage candidate imports</summary>
              {bondDiscoveryOpen ? <div className="mt-4"><BondDiscoveryPanel key={JSON.stringify([search, session?.displayName, session?.role, session?.environment])} /></div> : null}
            </details>
          </div>

          <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(24rem,0.8fr)]">
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
                <Input
                  id="security-master-search"
                  type="text"
                  value={securityMaster.query}
                  onChange={(e) => securityMaster.updateQuery(e.target.value)}
                  placeholder="Search securities…"
                  aria-controls={securityMaster.hasResults ? "security-master-results" : undefined}
                  aria-describedby="security-master-search-help security-master-search-status"
                  aria-invalid={securityMaster.searchErrorText ? true : undefined}
                  className="w-full text-sm"
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
                  onRowSelect={(row) => selectSecurityWithRoute(row.securityId)}
                  selectedRowId={securityMaster.selectedSecurityId ? `security-result-${securityMaster.selectedSecurityId}` : null}
                  emptyText={securityMaster.searchStatusText ?? "No Security Master results returned."}
                  ariaLabel={securityMaster.resultsTableLabel}
                  tableId="security-master-results"
                  caption={securityMaster.searchStatusText}
                />
              )}
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
                      <StatusBadge status="action" dot={false} label={securityMaster.conflictCountLabel} />
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
                            <StatusBadge status={conflict.statusTone === "warning" ? "action" : "info"} dot={false} label={conflict.statusLabel} />
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
          </div>

          {securityMaster.selectedSecurityId && (
            <section className="panel-surface-strong space-y-4 p-5" aria-labelledby="security-detail-page-title">
              <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
                <div className="min-w-0">
                  <div className={eyebrowClassName(securityMaster.pageView.detailEyebrow)}>{securityMaster.pageView.detailEyebrow}</div>
                  <h2 id="security-detail-page-title" className="mt-2 text-xl font-semibold tracking-normal text-foreground">
                    {securityMaster.pageView.detailTitle}
                  </h2>
                  <p className={cn("mt-1 break-words text-xs text-muted-foreground", usesTrackedCaps(securityMaster.pageView.detailSubtitle) && "font-mono uppercase tracking-[0.12em]")}>
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
              <Tabs
                aria-label="Security Master selected record drill-ins"
                tabs={securityMasterDrillInTabs}
                value={securityMasterActiveTab}
                onValueChange={(value) => {
                  if (!isSecurityMasterDrillInTabId(value)) {
                    return;
                  }

                  navigate({
                    pathname,
                    search: buildSecurityMasterDrillInSearch(search, value),
                    hash
                  }, { replace: true });
                }}
              >
                <TabPanel>
                  <div id={identity?.panelId ?? SECURITY_IDENTITY_DETAIL_PANEL_ID} className="space-y-4">
                    {securityMaster.identityLoading && (
                      <p role="status" className="rounded-md border border-[var(--state-pending-bd)] bg-[var(--state-pending-bg)] px-3 py-3 text-sm text-[var(--state-pending-fg)]">
                        Loading identity drill-in...
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
                        fields={identityPrimaryFields}
                        ariaLabel={identity.ariaLabel}
                      />
                      {identityTechnicalFields.length > 0 ? (
                        <TechnicalDetails
                          label="Identity record references"
                          description="Stable Security Master identity and version references for audit and support use."
                        >
                          <div className="grid gap-2 sm:grid-cols-2">
                            {identityTechnicalFields.map((field) => (
                              <AccountingValue key={field.label} label={field.label} value={field.value} />
                            ))}
                          </div>
                        </TechnicalDetails>
                      ) : null}
                      <ToolbarStrip
                        ariaLabel="Security identity detail context"
                        items={[
                          { id: "identifiers", label: "Identifiers", value: String(identity.identifiers.length), active: true },
                          { id: "aliases", label: "Aliases", value: String(identity.aliases.length) },
                          { id: "status", label: "Status", value: identity.statusLabel }
                        ]}
                      />
                      <div>
                        <div className={cn("mb-2 text-xs font-semibold text-muted-foreground", usesTrackedCaps(identity.identifiersTitle) && "uppercase tracking-[0.14em]")}>{identity.identifiersTitle}</div>
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
                        <div className={cn("mb-2 text-xs font-semibold text-muted-foreground", usesTrackedCaps(identity.aliasesTitle) && "uppercase tracking-[0.14em]")}>{identity.aliasesTitle}</div>
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
                </TabPanel>
                <TabPanel>
                  <div className="space-y-4">
                    <ReferenceDataWorkbenchPanel
                      view={securityMaster.referenceDataWorkbenchView}
                      onSelect={securityMaster.selectReferenceDataEndpoint}
                    />
                  </div>
                </TabPanel>
                <TabPanel>
                  <div className="space-y-4">
                    <SecuritySchedulesPanel
                      view={securityMaster.schedulesView}
                      onSelect={securityMaster.selectScheduleEvent}
                    />
                    <CorporateActionsPanel
                      view={securityMaster.corporateActionsView}
                      onSelect={securityMaster.selectCorporateAction}
                    />
                  </div>
                </TabPanel>
                <TabPanel>
                  <div className="space-y-4">
                    <SecurityOpenLotReadModelPanel
                      view={securityMaster.openLotReadModelView}
                      onSelect={securityMaster.selectOpenLot}
                    />
                    <LotsTrackerPanel
                      securityId={securityMaster.selectedSecurityId}
                      currency={selectedSecurityEntry?.economicDefinition.currency ?? null}
                    />
                  </div>
                </TabPanel>
                <TabPanel>
                  <div className="grid gap-4 xl:grid-cols-2">
                    <InstrumentPassportPanel view={securityMaster.instrumentPassportView} />
                    <TradingParametersPanel view={securityMaster.tradingParametersView} />
                  </div>
                </TabPanel>
                <TabPanel>
                  <div className="flex flex-col gap-4">
                    {ladderReturnAvailable ? <Button type="button" variant="outline" size="sm" className="w-fit" onClick={returnToLadder}>Return to bond ladder</Button> : null}
                    {securityMaster.detailLoading ? <p role="status">Loading authoritative security terms…</p> : null}
                    {securityMaster.detailErrorText ? <StatusBanner tone="danger" title="Security terms unavailable" detail={securityMaster.detailErrorText} role="alert" /> : null}
                    <SecurityDetailsPanel
                      entry={selectedSecurityEntry}
                      identity={securityMaster.identity}
                      tradingParameters={securityMaster.tradingParameters}
                    />
                    {selectedSecurityEntry && ["bond", "fixedincome"].includes(
                      selectedSecurityEntry.classification.assetClass.toLowerCase().replace(/[\s_-]/g, "")
                    ) ? (
                      <>
                        <div id="bond-analytics-action" tabIndex={-1} aria-label="Bond analytics" className="scroll-mt-6 focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary">
                        <div id="bond-calculation-context" tabIndex={-1}>
                          <BondAnalyticsPanel key={securityMaster.selectedSecurityId} securityId={securityMaster.selectedSecurityId}
                            observedFixingReceiptIds={observedFixingReceiptIds}
                            onClearObservedFixings={() => setObservedFixingSelection(null)}
                            onReviewConventions={() => {
                              const panel = document.getElementById("bond-conventions-review");
                              panel?.focus({ preventScroll: true });
                              panel?.scrollIntoView({ block: "nearest" });
                            }}
                            onSelectFixing={() => {
                              const panel = document.getElementById("bond-observed-fixings");
                              panel?.focus({ preventScroll: true });
                              panel?.scrollIntoView({ block: "nearest" });
                            }}
                            onSelectForwardCurve={() => {
                              const panel = document.getElementById("bond-rates-fixings");
                              panel?.focus({ preventScroll: true });
                              panel?.scrollIntoView({ block: "nearest" });
                            }} />
                        </div>
                        </div>
                        <SecurityCurveValuationPanel key={`curve-valuation-${securityMaster.selectedSecurityId}`} securityId={securityMaster.selectedSecurityId} />
                        <BondWorkflowPanel securityId={securityMaster.selectedSecurityId} />
                        <BondIncomePanel key={`income-${securityMaster.selectedSecurityId}`} />
                        <section id="bond-conventions-review" tabIndex={-1} aria-label="Bond convention review">
                          <Button type="button" variant="outline" size="sm" onClick={() => {
                            const panel = document.getElementById("bond-calculation-context");
                            panel?.focus({ preventScroll: true });
                            panel?.scrollIntoView({ block: "nearest" });
                          }}>Return to calculation</Button>
                        <div id="bond-migration-action" tabIndex={-1} aria-label="Bond convention migration" className="scroll-mt-6 focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary">
                        <BondMigrationPreviewPanel
                          key={`migration-${securityMaster.selectedSecurityId}`}
                          initiallyOpen={bondAction === "migration"}
                          securityId={securityMaster.selectedSecurityId}
                          expectedVersion={securityMaster.identity?.securityId.toLowerCase() === securityMaster.selectedSecurityId.toLowerCase()
                            ? securityMaster.identity.version
                            : null}
                        />
                        </div>
                        </section>
                        <div>
                          <Button type="button" variant="outline" size="sm" onClick={() => {
                            const panel = document.getElementById("bond-calculation-context");
                            panel?.focus({ preventScroll: true }); panel?.scrollIntoView({ block: "nearest" });
                          }}>Return to calculation from observed fixings</Button>
                          <BondObservedFixingPanel key={`observed-fixings-${securityMaster.selectedSecurityId}`}
                            securityId={securityMaster.selectedSecurityId} securityVersion={securityMaster.identity?.securityId === securityMaster.selectedSecurityId ? securityMaster.identity.version : null}
                            selectedReceiptIds={observedFixingReceiptIds}
                            onSelect={(receipt) => setObservedFixingSelection(current => ({ securityId: receipt.fixing.securityId,
                              receipts: [...(current?.securityId === receipt.fixing.securityId ? current.receipts.filter(prior => prior.fixing.resetDate !== receipt.fixing.resetDate) : []), receipt] }))}
                            onRemove={(receiptId) => setObservedFixingSelection(current => current ? { ...current, receipts: current.receipts.filter(receipt => receipt.receiptId !== receiptId) } : null)} />
                        </div>
                        <div>
                          <Button type="button" variant="outline" size="sm" onClick={() => {
                            const panel = document.getElementById("bond-calculation-context");
                            panel?.focus({ preventScroll: true });
                            panel?.scrollIntoView({ block: "nearest" });
                          }}>Return to calculation from fixings</Button>
                          <SecurityRatesIngestionPanel key={`rates-${securityMaster.selectedSecurityId}`} securityId={securityMaster.selectedSecurityId} />
                        </div>
                      </>
                    ) : null}
                  </div>
                </TabPanel>
              </Tabs>
            </section>
          )}
        </section>
        </FinancialRecordExplorerShell>
      )}

      {sectionVisibility.showReconciliationActions && workstream !== "reconciliation" ? (
        <>
          <ReconciliationWorkbench reconciliation={reconciliation} resolveDialog={resolveDialog} controls={reconciliationViewState} selectionNotice={reconciliation.selectionNotice} runSelector={<ReconciliationStatementRuns reconciliation={reconciliation} initiallyOpen={hash === "#statement-runs"} />} />
          <TechnicalDetails id="accounting-history" label="Calibration and history"><CalibrationSummaryPanel view={reconciliation.calibrationView} compact={workstream === "exceptions"} /></TechnicalDetails>
        </>
      ) : null}
        </>
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
