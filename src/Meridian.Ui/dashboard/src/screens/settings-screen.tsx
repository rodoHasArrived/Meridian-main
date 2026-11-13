import { ExecutionAlertSettingsPanel } from "./settings-screen.execution-alerts";
import { ProviderSetupPanel } from "@/components/data/provider-setup-panel";
import { LifecycleControlPanel } from "@/components/meridian/lifecycle-control-panel";
import { StatStrip } from "@/components/meridian/stat-strip";
import { listScopedAccessAssignments } from "@/lib/api";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import { selectSharedProviderHealth } from "@/lib/cross-screen-counts";
import { useSharedCrossScreenCounts } from "@/lib/cross-screen-counts-context";
import { resolveFigureEvidence, useWorkspaceFigureContext } from "@/lib/workspace-figure-context";
import { SettingsMutationConfirmDialog, type SettingsMutationConfirmation } from "@/screens/settings-mutation-confirm-dialog";
import { resolveSettingsRouteState, SETTINGS_ROUTE_VIEW_COPY, type SettingsRouteViewId } from "@/screens/settings-route-state";
import { buildApprovalPolicyRuleDraft, buildCloseCalendarItemDraft, buildLedgerMappingAssignmentDraft, buildRolePermissionProfileDraft, settingsRoleDisplayLabel } from "@/screens/settings-screen.operations-form-options";
import { buildSettingsScreenViewModel, useAlpacaConnectionFormViewModel, useRobinhoodConnectionViewModel, useSettingsRecentEventsSelectionViewModel } from "@/screens/settings-screen.view-model";
import { SettingsTaskChooser } from "@/screens/settings-task-chooser";
import type { AccountingWorkspaceResponse, BrokerageConnectionStatus, DataWorkspaceResponse, FeatureCapabilitySettingsResponse, LedgerMappingWorkbench, OperationsApprovalPolicyMatrix, OperationsCloseCalendar, PortfolioWorkspaceResponse, ProviderConnectionRow, ProviderRoutingBinding, ProviderRoutingConnection, ProviderRoutingTrustSnapshot, ReportingWorkspaceResponse, RolePermissionCatalog, SecurityAssetProfileDefinition, SessionInfo, StrategyWorkspaceResponse, SystemOverviewResponse, TradingWorkspaceResponse, UserAccessAssignment, WorkspaceKey } from "@/types";
import { useEffect, useMemo, useState } from "react";
import { useLocation } from "react-router-dom";
import { buildProfileFieldValueState, createProfileBackedSecurityState, isWriteSelectableAssetProfile, type ProfileBackedSecurityState } from "./settings-profile-backed-security";
import type { ProviderOpenApiImportState } from "./settings-provider-integration";
import { createProviderOpenApiImportState, providerRuntimeStateKey } from "./settings-provider-integration";
import { SettingsAccessPostureSection, SettingsAppearanceSection } from "@/screens/settings-screen.profile-sections";
import { SettingsAlpacaSection, SettingsRobinhoodSection } from "@/screens/settings-screen.brokerage-sections";
import { SettingsDiagnosticsSection } from "@/screens/settings-screen.diagnostics-section";
import { SettingsBackendCapabilitySection, SettingsRuntimeCapabilitiesSection } from "@/screens/settings-screen.capability-sections";
import { createAssetProfileDraftState, type ApprovalPolicyRuleState, type AssetProfileDraftState, type CloseCalendarItemState, type LedgerMappingAssignmentState, type RolePermissionProfileState, type ScopedAccessAssignmentState } from "@/screens/settings-screen.section-state";
import { SettingsScopedAccessSection, useSettingsScopedAccessSectionState } from "@/screens/settings-screen.scoped-access-section";
import { SettingsOperationsSection, useSettingsOperationsSectionState } from "@/screens/settings-screen.operations-section";
import { SettingsAssetProfileSection, useSettingsAssetProfileSectionState } from "@/screens/settings-screen.asset-profile-section";
import { SettingsProviderSection, useSettingsProviderSectionState } from "@/screens/settings-screen.provider-section";

interface SettingsScreenProps {
  fundAccountId?: string | null;
  session: SessionInfo | null;
  overview: SystemOverviewResponse | null;
  strategy?: StrategyWorkspaceResponse | null;
  trading?: TradingWorkspaceResponse | null;
  portfolio?: PortfolioWorkspaceResponse | null;
  data?: DataWorkspaceResponse | null;
  accounting?: AccountingWorkspaceResponse | null;
  reporting?: ReportingWorkspaceResponse | null;
  brokerageConnection?: BrokerageConnectionStatus | null;
  robinhoodConnection?: BrokerageConnectionStatus | null;
  providerConnections?: ProviderConnectionRow[] | null;
  providerRoutingConnections?: ProviderRoutingConnection[] | null;
  providerRoutingBindings?: ProviderRoutingBinding[] | null;
  providerRoutingTrustSnapshots?: ProviderRoutingTrustSnapshot[] | null;
  featureCapabilities?: FeatureCapabilitySettingsResponse | null;
  rolePermissionCatalog?: RolePermissionCatalog | null;
  securityAssetProfiles?: SecurityAssetProfileDefinition[] | null;
  ledgerMappingWorkbench?: LedgerMappingWorkbench | null;
  operationsApprovalPolicyMatrix?: OperationsApprovalPolicyMatrix | null;
  operationsCloseCalendar?: OperationsCloseCalendar | null;
  providerRoutingRefreshing?: boolean;
  onFeatureCapabilityToggle?: (capabilityKey: string, isEnabled: boolean) => Promise<void> | void;
  onRefresh?: () => Promise<void> | void;
  onProviderRoutingRefresh?: () => Promise<void> | void;
  loading?: boolean;
  error?: string | null;
  workspaceErrors?: Partial<Record<WorkspaceKey, string>>;
}

// Storage-health headline metric tone — mirrors the view-model's storage tone mapping so the
// MetricCard accent matches the "Storage health" system item.
function settingsStorageHealthTone(
  health: SystemOverviewResponse["storageHealth"] | undefined
): "default" | "success" | "warning" | "danger" {
  if (health === "Healthy") return "success";
  if (health === "Warning") return "warning";
  if (health === "Critical") return "danger";
  return "default";
}

function inferSettingsTaskView({
  overview,
  strategy,
  trading,
  portfolio,
  data,
  accounting,
  reporting,
  brokerageConnection,
  providerConnections,
  providerRoutingConnections,
  providerRoutingBindings,
  providerRoutingTrustSnapshots,
  featureCapabilities,
  rolePermissionCatalog,
  securityAssetProfiles,
  ledgerMappingWorkbench,
  operationsApprovalPolicyMatrix,
  operationsCloseCalendar,
  error,
  workspaceErrors
}: Pick<
  SettingsScreenProps,
  | "overview"
  | "strategy"
  | "trading"
  | "portfolio"
  | "data"
  | "accounting"
  | "reporting"
  | "brokerageConnection"
  | "providerConnections"
  | "providerRoutingConnections"
  | "providerRoutingBindings"
  | "providerRoutingTrustSnapshots"
  | "featureCapabilities"
  | "rolePermissionCatalog"
  | "securityAssetProfiles"
  | "ledgerMappingWorkbench"
  | "operationsApprovalPolicyMatrix"
  | "operationsCloseCalendar"
  | "error"
  | "workspaceErrors"
>): SettingsRouteViewId {
  if (providerConnections || providerRoutingConnections || providerRoutingBindings || providerRoutingTrustSnapshots || brokerageConnection) {
    return "providers";
  }

  if (securityAssetProfiles) {
    return "operations";
  }

  if (featureCapabilities) {
    return "diagnostics-advanced";
  }

  if (rolePermissionCatalog || ledgerMappingWorkbench || operationsApprovalPolicyMatrix || operationsCloseCalendar) {
    return "operations";
  }

  if (
    error
    || overview === null
    || strategy
    || trading
    || portfolio
    || data
    || accounting
    || reporting
    || Object.keys(workspaceErrors ?? {}).length > 0
  ) {
    return "diagnostics";
  }

  return "access";
}

export function SettingsScreen({
  fundAccountId,
  session,
  overview,
  strategy = null,
  trading = null,
  portfolio = null,
  data = null,
  accounting = null,
  reporting = null,
  brokerageConnection = null,
  robinhoodConnection = null,
  providerConnections = null,
  providerRoutingConnections = null,
  providerRoutingBindings = null,
  providerRoutingTrustSnapshots = null,
  featureCapabilities = null,
  rolePermissionCatalog = null,
  securityAssetProfiles = null,
  ledgerMappingWorkbench = null,
  operationsApprovalPolicyMatrix = null,
  operationsCloseCalendar = null,
  providerRoutingRefreshing = false,
  onFeatureCapabilityToggle,
  onRefresh,
  onProviderRoutingRefresh,
  loading = false,
  error = null,
  workspaceErrors = {}
}: SettingsScreenProps) {
  const location = useLocation();
  const sharedCounts = useSharedCrossScreenCounts();
  const figureContext = useWorkspaceFigureContext();
  const providerHealth = selectSharedProviderHealth(sharedCounts, data?.providers);
  // Provider health is not an overview figure: it comes from the shared cross-screen counts, or
  // from the Data rows when those counts are unavailable. It states that payload's evidence, so a
  // failed counts reload shows as stale here even when the overview reloaded.
  const providerHealthFromSharedCounts = Boolean(sharedCounts?.providers);
  const providerHealthEvidence = resolveFigureEvidence(figureContext, providerHealthFromSharedCounts ? "crossScreenCounts" : "data");
  const vm = buildSettingsScreenViewModel({
    session,
    overview,
    crossScreenCounts: sharedCounts,
    strategy,
    trading,
    portfolio,
    data,
    accounting,
    reporting,
    brokerageConnection,
    robinhoodConnection,
    providerConnections,
    providerRoutingConnections,
    providerRoutingBindings,
    providerRoutingTrustSnapshots,
    featureCapabilities,
    rolePermissionCatalog,
    securityAssetProfiles,
    ledgerMappingWorkbench,
    operationsApprovalPolicyMatrix,
    operationsCloseCalendar,
    providerRoutingRefreshing,
    loading,
    error,
    workspaceErrors
  });
  const alpacaForm = useAlpacaConnectionFormViewModel({
    onRefresh,
    canClear: vm.alpacaConnectionPanel.canClear
  });
  const robinhoodForm = useRobinhoodConnectionViewModel({
    onRefresh,
    canConnect: vm.robinhoodConnectionPanel.canConnect && vm.robinhoodConnectionPanel.isConfigured,
    canDisconnect: vm.robinhoodConnectionPanel.canDisconnect
  });
  // The status endpoint returns authorizationUrl only on the connect response, so prefer the
  // URL the form retained over the (post-refresh null) panel value for the manual fallback link.
  const robinhoodAuthorizationUrl = robinhoodForm.authorizationUrl ?? vm.robinhoodConnectionPanel.authorizationUrl;
  const recentEventsVm = useSettingsRecentEventsSelectionViewModel(vm.recentEventsSection);
  const inferredTaskView = useMemo(() => inferSettingsTaskView({
    overview,
    strategy,
    trading,
    portfolio,
    data,
    accounting,
    reporting,
    brokerageConnection,
    providerConnections,
    providerRoutingConnections,
    providerRoutingBindings,
    providerRoutingTrustSnapshots,
    featureCapabilities,
    rolePermissionCatalog,
    securityAssetProfiles,
    ledgerMappingWorkbench,
    operationsApprovalPolicyMatrix,
    operationsCloseCalendar,
    error,
    workspaceErrors
  }), [
    accounting,
    brokerageConnection,
    data,
    error,
    featureCapabilities,
    ledgerMappingWorkbench,
    operationsApprovalPolicyMatrix,
    operationsCloseCalendar,
    overview,
    portfolio,
    providerConnections,
    providerRoutingBindings,
    providerRoutingConnections,
    providerRoutingTrustSnapshots,
    reporting,
    rolePermissionCatalog,
    securityAssetProfiles,
    strategy,
    trading,
    workspaceErrors
  ]);
  const resolvedRouteState = resolveSettingsRouteState(location.pathname, location.hash);
  const activeTaskView = resolvedRouteState?.view ?? inferredTaskView;
  const activeProviderId = resolvedRouteState?.providerId
    ?? (activeTaskView === "provider-setup" || activeTaskView === "provider-advanced"
      ? brokerageConnection?.providerId ?? providerConnections?.[0]?.providerId ?? null
      : null);
  const legacyEmbeddedSettingsView = resolvedRouteState === null;
  const [providerOpenApiImportState, setProviderOpenApiImportState] = useState<Record<string, ProviderOpenApiImportState>>({});
  const [mutationConfirmation, setMutationConfirmation] = useState<SettingsMutationConfirmation | null>(null);
  const [mutationConfirmationBusy, setMutationConfirmationBusy] = useState(false);
  const [mutationConfirmationError, setMutationConfirmationError] = useState<ApiErrorDisplay | null>(null);
  const confirmSettingsMutation = async () => {
    if (!mutationConfirmation || mutationConfirmationBusy) {
      return;
    }

    setMutationConfirmationError(null);
    setMutationConfirmationBusy(true);
    try {
      await mutationConfirmation.run();
      setMutationConfirmation(null);
    } catch (error) {
      setMutationConfirmationError(describeApiError(error, "Settings change failed."));
    } finally {
      setMutationConfirmationBusy(false);
    }
  };
  const ledgerMappingDraft = useMemo(
    () => buildLedgerMappingAssignmentDraft(ledgerMappingWorkbench),
    [ledgerMappingWorkbench]
  );
  const roleProfileDraft = useMemo(
    () => buildRolePermissionProfileDraft(rolePermissionCatalog),
    [rolePermissionCatalog]
  );
  const scopedAccessRoleOptions = useMemo(
    () => buildScopedAccessRoleOptions(rolePermissionCatalog),
    [rolePermissionCatalog]
  );
  const scopedAccessRoleProfileOptions = useMemo(
    () => buildScopedAccessRoleProfileOptions(rolePermissionCatalog),
    [rolePermissionCatalog]
  );
  const approvalPolicyDraft = useMemo(
    () => buildApprovalPolicyRuleDraft(operationsApprovalPolicyMatrix),
    [operationsApprovalPolicyMatrix]
  );
  const closeCalendarDraft = useMemo(
    () => buildCloseCalendarItemDraft(operationsCloseCalendar),
    [operationsCloseCalendar]
  );
  const approvedAssetProfiles = useMemo(
    () => (securityAssetProfiles ?? []).filter((profile) => isWriteSelectableAssetProfile(profile)),
    [securityAssetProfiles]
  );
  const firstApprovedAssetProfile = approvedAssetProfiles[0] ?? null;
  const ledgerMappingDraftSignature = `${ledgerMappingDraft.accountOptions.map((option) => option.value).join("|")}::${ledgerMappingDraft.ledgerGroupOptions.map((option) => option.value).join("|")}`;
  const roleProfileDraftSignature = `${roleProfileDraft.baseRoleOptions.map((option) => option.value).join("|")}::${roleProfileDraft.permissionOptions.map((option) => option.value).join("|")}`;
  const scopedAccessCatalogSignature = `${scopedAccessRoleOptions.map((option) => option.value).join("|")}::${scopedAccessRoleProfileOptions.map((option) => option.value).join("|")}::${roleProfileDraft.permissionOptions.map((option) => option.value).join("|")}`;
  const approvalPolicyDraftSignature = approvalPolicyDraft.rows.map((row) => [
    row.policyKey,
    row.requiredPermission,
    row.submitterRole,
    row.reviewerRole,
    row.requiredDistinctApprovals,
    row.requiresIndependentReviewer,
    row.requiresReportPack,
    row.requiresChecklistControlApprovals,
    row.evidenceRequirement,
    row.severity
  ].join(":")).join("|");
  const closeCalendarDraftSignature = closeCalendarDraft.items.map((item) => [
    item.workflowId,
    item.nextDueTaskId ?? "",
    item.nextDueDate ?? "",
    item.nextDueOwner ?? "",
    item.periodId,
    item.version
  ].join(":")).join("|");
  const assetProfileSignature = approvedAssetProfiles.map((profile) => `${profile.profileId}:${profile.version}`).join("|");
  const [ledgerMappingAssignment, setLedgerMappingAssignment] = useState<LedgerMappingAssignmentState>(() => ({
    accountId: "",
    ledgerGroupId: "",
    rationale: "Assign ledger group for governed posting, reconciliation, reporting, and close control.",
    busy: false,
    message: null,
    details: [],
    tone: "default"
  }));
  const [rolePermissionProfile, setRolePermissionProfile] = useState<RolePermissionProfileState>(() => ({
    profileName: "",
    displayName: "",
    description: "",
    baseRole: "",
    permissionNames: [],
    rationale: "Create a scoped authority profile for governed fund operations.",
    busy: false,
    message: null,
    details: [],
    tone: "default"
  }));
  const [scopedAccessAssignments, setScopedAccessAssignments] = useState<UserAccessAssignment[]>([]);
  const [scopedAccess, setScopedAccess] = useState<ScopedAccessAssignmentState>(() => ({
    principalId: "",
    principalKind: "User",
    scopeKind: "Fund",
    scopeId: "",
    role: "",
    roleProfileName: "",
    permissionNames: [],
    effectiveFrom: "",
    effectiveTo: "",
    approvalLimitAmount: "",
    approvalLimitCurrency: "USD",
    segregationOfDutiesRule: "",
    rationale: "Grant scoped authority with audit evidence for governed fund operations.",
    includeRevoked: false,
    loading: Boolean(rolePermissionCatalog),
    busy: false,
    revokeBusyId: null,
    message: null,
    details: [],
    tone: "default"
  }));
  const [approvalPolicyRule, setApprovalPolicyRule] = useState<ApprovalPolicyRuleState>(() => ({
    policyKey: "",
    requiredPermission: "",
    submitterRole: "",
    reviewerRole: "",
    requiredDistinctApprovals: 1,
    requiresIndependentReviewer: true,
    requiresReportPack: true,
    requiresChecklistControlApprovals: true,
    evidenceRequirement: "",
    severity: "Critical",
    rationale: "Update approval policy for governed fund-operations close control.",
    busy: false,
    message: null,
    details: [],
    tone: "default"
  }));
  const [closeCalendarItem, setCloseCalendarItem] = useState<CloseCalendarItemState>(() => ({
    workflowId: "",
    taskId: "",
    dueDate: "",
    owner: "",
    rationale: "Update account close calendar ownership and due-date accounting.",
    busy: false,
    message: null,
    details: [],
    tone: "default"
  }));
  const [assetProfileDraft, setAssetProfileDraft] = useState<AssetProfileDraftState>(() => (
    createAssetProfileDraftState(firstApprovedAssetProfile)
  ));
  const [profileBackedSecurity, setProfileBackedSecurity] = useState<ProfileBackedSecurityState>(() => (
    createProfileBackedSecurityState(firstApprovedAssetProfile)
  ));
  const allProviderRows = useMemo(
    () => vm.providerConnectionCenter.groups.flatMap((group) => group.rows),
    [vm.providerConnectionCenter.groups]
  );
  const providerRowIdsSignature = useMemo(
    () => allProviderRows.map((row) => row.providerId).sort().join("|"),
    [allProviderRows]
  );
  const routeCopy = SETTINGS_ROUTE_VIEW_COPY[activeTaskView];
  const settingsStatMetrics = [
    {
      id: "providers-healthy",
      label: "Providers healthy",
      value: providerHealth ? `${providerHealth.healthy} / ${providerHealth.total}` : "—",
      delta: "",
      tone: !providerHealth
        ? "default" as const
        : providerHealth.total > 0 && providerHealth.healthy === providerHealth.total ? "success" as const : "warning" as const,
      ...(providerHealth
        ? {
            provenance: {
              source: providerHealthFromSharedCounts ? "Cross-screen counts" : "Data workspace",
              // Outside the shell the counts payload's own as-of time is the only one it carries.
              asOf: providerHealthEvidence.asOf ?? (providerHealthFromSharedCounts ? sharedCounts?.asOf ?? null : null)
            },
            ...(providerHealthEvidence.freshness ? { freshness: providerHealthEvidence.freshness } : {})
          }
        : {})
    },
    { id: "active-runs", label: "Active runs", value: overview ? String(overview.activeRuns) : "—", delta: "", tone: "default" as const },
    { id: "open-positions", label: "Open positions", value: overview ? String(overview.openPositions) : "—", delta: "", tone: "default" as const },
    { id: "storage-health", label: "Storage health", value: overview?.storageHealth ?? "—", delta: "", tone: settingsStorageHealthTone(overview?.storageHealth) }
  ];
  const showChooserSection = activeTaskView === "chooser";
  const showPreferencesSection = activeTaskView === "preferences";
  const showAccessSection = activeTaskView === "access";
  const showOperationsSection = activeTaskView === "operations";
  const showAssetProfileSection = activeTaskView === "operations";
  const showProviderSection = activeTaskView === "providers" || activeTaskView === "provider-setup" || activeTaskView === "provider-advanced";
  const showProviderSetupControls = activeTaskView === "provider-setup" || legacyEmbeddedSettingsView;
  const showDataProviderModulesSection = showProviderSetupControls && (activeProviderId === "data" || legacyEmbeddedSettingsView);
  const showAlpacaSection = showProviderSetupControls && (activeProviderId === "alpaca" || legacyEmbeddedSettingsView);
  const showRobinhoodSection = showProviderSetupControls && (activeProviderId === "robinhood" || legacyEmbeddedSettingsView);
  const showDiagnosticsSection = activeTaskView === "diagnostics" || activeTaskView === "diagnostics-advanced";
  const showAdvancedDiagnosticsSection = activeTaskView === "diagnostics-advanced";
  const showRuntimeSection = showAdvancedDiagnosticsSection;
  const showBackendCapabilitySection = showAdvancedDiagnosticsSection;
  const visibleDiagnosticLinks = showAdvancedDiagnosticsSection ? vm.diagnosticLinks : vm.diagnosticExceptionLinks;

  useEffect(() => {
    setLedgerMappingAssignment((current) => ({
      ...current,
      accountId: ledgerMappingDraft.accountOptions.some((option) => option.value === current.accountId)
        ? current.accountId
        : ledgerMappingDraft.accountOptions[0]?.value ?? "",
      ledgerGroupId: ledgerMappingDraft.ledgerGroupOptions.some((option) => option.value === current.ledgerGroupId)
        ? current.ledgerGroupId
        : ledgerMappingDraft.ledgerGroupOptions[0]?.value ?? "",
      message: null,
      details: [],
      tone: "default"
    }));
  }, [ledgerMappingDraftSignature]);

  useEffect(() => {
    setRolePermissionProfile((current) => {
      const currentBaseRole = roleProfileDraft.baseRoleOptions.some((option) => option.value === current.baseRole)
        ? current.baseRole
        : roleProfileDraft.defaultBaseRole;
      const validPermissions = new Set(roleProfileDraft.permissionOptions.map((option) => option.value));
      const retainedPermissions = current.permissionNames.filter((permission) => validPermissions.has(permission));
      return {
        ...current,
        baseRole: currentBaseRole,
        permissionNames: retainedPermissions.length > 0 ? retainedPermissions : roleProfileDraft.defaultPermissionNames,
        message: null,
        details: [],
        tone: "default"
      };
    });
  }, [roleProfileDraftSignature]);

  useEffect(() => {
    setScopedAccess((current) => {
      const validRoles = new Set(scopedAccessRoleOptions.map((option) => option.value));
      const role = validRoles.has(current.role)
        ? current.role
        : scopedAccessRoleOptions[0]?.value ?? "";
      const validProfiles = new Set(scopedAccessRoleProfileOptions.map((option) => option.value));
      const roleProfileName = validProfiles.has(current.roleProfileName)
        ? current.roleProfileName
        : scopedAccessRoleProfileOptions[0]?.value ?? "";
      const validPermissions = new Set(roleProfileDraft.permissionOptions.map((option) => option.value));
      const retainedPermissions = current.permissionNames.filter((permission) => validPermissions.has(permission));
      const selectedRole = rolePermissionCatalog?.roles.find((entry) => entry.role === role);
      const defaultPermissions = selectedRole?.permissions ?? roleProfileDraft.defaultPermissionNames;
      return {
        ...current,
        role,
        roleProfileName,
        permissionNames: retainedPermissions.length > 0 ? retainedPermissions : defaultPermissions,
        message: null,
        details: [],
        tone: "default"
      };
    });
  }, [scopedAccessCatalogSignature]);

  useEffect(() => {
    if (!rolePermissionCatalog) {
      return;
    }

    let cancelled = false;
    setScopedAccess((current) => current.loading ? current : { ...current, loading: true });

    listScopedAccessAssignments({ includeRevoked: scopedAccess.includeRevoked })
      .then((assignments) => {
        if (cancelled) {
          return;
        }

        setScopedAccessAssignments(assignments);
        setScopedAccess((current) => ({
          ...current,
          loading: false,
          message: assignments.length === 0 && !current.message
            ? "No scoped access assignments loaded."
            : current.message,
          details: assignments.length === 0 && !current.message ? [] : current.details,
          tone: assignments.length === 0 && !current.message ? "default" : current.tone
        }));
      })
      .catch((error) => {
        if (cancelled) {
          return;
        }

        const display = describeApiError(error, "Scoped access assignments could not be loaded.");
        setScopedAccess((current) => ({
          ...current,
          loading: false,
          message: display.summary,
          details: display.details,
          tone: "danger"
        }));
      });

    return () => {
      cancelled = true;
    };
  }, [rolePermissionCatalog, scopedAccess.includeRevoked]);

  useEffect(() => {
    setApprovalPolicyRule((current) => {
      const selected = approvalPolicyDraft.rows.find((row) => row.policyKey === current.policyKey) ??
        approvalPolicyDraft.rows[0];
      if (!selected) {
        return {
          ...current,
          policyKey: "",
          message: null,
          details: [],
          tone: "default"
        };
      }

      return {
        ...current,
        policyKey: selected.policyKey,
        requiredPermission: selected.requiredPermission,
        submitterRole: selected.submitterRole,
        reviewerRole: selected.reviewerRole,
        requiredDistinctApprovals: selected.requiredDistinctApprovals,
        requiresIndependentReviewer: selected.requiresIndependentReviewer,
        requiresReportPack: selected.requiresReportPack,
        requiresChecklistControlApprovals: selected.requiresChecklistControlApprovals,
        evidenceRequirement: selected.evidenceRequirement,
        severity: selected.severity,
        message: null,
        details: [],
        tone: "default"
      };
    });
  }, [approvalPolicyDraftSignature]);

  useEffect(() => {
    setCloseCalendarItem((current) => {
      const selected = closeCalendarDraft.items.find((item) => item.workflowId === current.workflowId) ??
        closeCalendarDraft.items[0];
      if (!selected) {
        return {
          ...current,
          workflowId: "",
          taskId: "",
          dueDate: "",
          owner: "",
          message: null,
          details: [],
          tone: "default"
        };
      }

      return {
        ...current,
        workflowId: selected.workflowId,
        taskId: selected.nextDueTaskId ?? "",
        dueDate: selected.nextDueDate ?? "",
        owner: selected.nextDueOwner ?? "",
        message: null,
        details: [],
        tone: "default"
      };
    });
  }, [closeCalendarDraftSignature]);

  useEffect(() => {
    setAssetProfileDraft((current) => {
      const selected = approvedAssetProfiles.find((profile) => profile.profileId === current.starterProfileId) ??
        firstApprovedAssetProfile;
      if (!selected) {
        return createAssetProfileDraftState(null);
      }

      if (current.starterProfileId === selected.profileId && current.profileId.trim()) {
        return current;
      }

      return createAssetProfileDraftState(selected);
    });

    setProfileBackedSecurity((current) => {
      const selected = approvedAssetProfiles.find((profile) => profile.profileId === current.profileId) ??
        firstApprovedAssetProfile;
      if (!selected) {
        return createProfileBackedSecurityState(null);
      }

      return {
        ...current,
        profileId: selected.profileId,
        fieldValues: buildProfileFieldValueState(selected, current.fieldValues),
        message: null,
        details: [],
        tone: "default"
      };
    });
  }, [assetProfileSignature, firstApprovedAssetProfile, approvedAssetProfiles]);

  useEffect(() => {
    if (allProviderRows.length === 0) {
      return;
    }

    setProviderOpenApiImportState((current) => {
      let changed = false;
      const next = { ...current };
      for (const row of allProviderRows) {
        const stateKey = providerRuntimeStateKey(row);
        if (next[stateKey]) {
          continue;
        }
        next[stateKey] = createProviderOpenApiImportState(row);
        changed = true;
      }
      return changed ? next : current;
    });
  }, [providerRowIdsSignature]);

  const { revokeScopedAccess, scopedAccessActiveCount, scopedAccessRevokedCount, selectScopedAccessRole, submitScopedAccessAssignment, toggleScopedAccessPermission } = useSettingsScopedAccessSectionState({ onRefresh, rolePermissionCatalog, scopedAccess, scopedAccessAssignments, session, setMutationConfirmation, setScopedAccess, setScopedAccessAssignments });
  const { selectApprovalPolicyRule, selectCloseCalendarItem, submitApprovalPolicyRule, submitCloseCalendarItem, submitLedgerMappingAssignment, submitRolePermissionProfile, toggleRoleProfilePermission } = useSettingsOperationsSectionState({ approvalPolicyDraft, approvalPolicyRule, closeCalendarDraft, closeCalendarItem, ledgerMappingAssignment, onRefresh, rolePermissionProfile, roleProfileDraft, session, setApprovalPolicyRule, setCloseCalendarItem, setLedgerMappingAssignment, setRolePermissionProfile });
  const { additionalRequiredIdentifierPreferences, approveAssetProfileDraft, loadAssetProfileLineage, rollbackAssetProfile, selectAssetProfileStarter, selectProfileBackedSecurityProfile, selectedDraftStarterProfile, selectedProfileBackedSecurityProfile, submitAssetProfileDraft, submitProfileBackedSecurity, updateProfileBackedSecurityField } = useSettingsAssetProfileSectionState({ approvedAssetProfiles, assetProfileDraft, firstApprovedAssetProfile, onRefresh, profileBackedSecurity, session, setAssetProfileDraft, setProfileBackedSecurity, vm });
  const { activeProviderDisplayName, createProviderRuntimeHandoff, inlineProviderManagementEnabled, loadProviderRuntimeEvidence, providerCapabilityFilter, providerHealthFilter, providerRuntimeState, providerSearch, providerSort, providerVerificationFilter, replayProviderRuntimeQuarantine, resolveProviderRuntimeQuarantineRecord, runProviderRuntimeDueSync, setProviderCapabilityFilter, setProviderHealthFilter, setProviderSearch, setProviderSort, setProviderVerificationFilter, showProviderAdvancedControls, showProviderFilters, submitProviderOpenApiImport, updateProviderOpenApiImportState, visibleProviderGroups } = useSettingsProviderSectionState({ activeProviderId, activeTaskView, allProviderRows, featureCapabilities, legacyEmbeddedSettingsView, providerOpenApiImportState, session, setProviderOpenApiImportState, vm });

  return (
    <div className="space-y-5">
      {/* Route heading first ("where am I?"); workspace-wide metrics follow. */}
      <section
        id="settings-overview"
        role="region"
        aria-label="Settings workbench context"
        className="flex flex-wrap items-end justify-between gap-3"
      >
        <div className="min-w-0">
          <h2 className="font-display text-lg font-semibold leading-tight text-foreground">
            {routeCopy.title}
          </h2>
          <p className="mt-0.5 max-w-3xl text-xs leading-5 text-muted-foreground">
            {routeCopy.description}
          </p>
        </div>
      </section>

      <StatStrip metrics={settingsStatMetrics} label="Operator control posture metrics" source="System overview" figureSource="overview" />

      {showChooserSection ? <SettingsTaskChooser viewModel={vm} /> : null}
      {showPreferencesSection ? <ExecutionAlertSettingsPanel accountId={fundAccountId} /> : null}

      {showAccessSection ? (
      <SettingsAccessPostureSection vm={vm} overview={overview} providerHealth={providerHealth} />
      ) : null}

      {showPreferencesSection ? (
      <SettingsAppearanceSection />
      ) : null}

      {showAccessSection ? (
      <SettingsScopedAccessSection scopedAccessActiveCount={scopedAccessActiveCount} scopedAccessRevokedCount={scopedAccessRevokedCount} scopedAccess={scopedAccess} setScopedAccess={setScopedAccess} scopedAccessAssignments={scopedAccessAssignments} setMutationConfirmation={setMutationConfirmation} revokeScopedAccess={revokeScopedAccess} submitScopedAccessAssignment={submitScopedAccessAssignment} roleProfileDraft={roleProfileDraft} selectScopedAccessRole={selectScopedAccessRole} scopedAccessRoleOptions={scopedAccessRoleOptions} scopedAccessRoleProfileOptions={scopedAccessRoleProfileOptions} toggleScopedAccessPermission={toggleScopedAccessPermission} />
      ) : null}

      {showOperationsSection ? (
      <SettingsOperationsSection vm={vm} submitLedgerMappingAssignment={submitLedgerMappingAssignment} ledgerMappingDraft={ledgerMappingDraft} ledgerMappingAssignment={ledgerMappingAssignment} setLedgerMappingAssignment={setLedgerMappingAssignment} submitRolePermissionProfile={submitRolePermissionProfile} roleProfileDraft={roleProfileDraft} rolePermissionProfile={rolePermissionProfile} setRolePermissionProfile={setRolePermissionProfile} rolePermissionCatalog={rolePermissionCatalog} toggleRoleProfilePermission={toggleRoleProfilePermission} submitApprovalPolicyRule={submitApprovalPolicyRule} approvalPolicyDraft={approvalPolicyDraft} approvalPolicyRule={approvalPolicyRule} selectApprovalPolicyRule={selectApprovalPolicyRule} setApprovalPolicyRule={setApprovalPolicyRule} submitCloseCalendarItem={submitCloseCalendarItem} closeCalendarDraft={closeCalendarDraft} closeCalendarItem={closeCalendarItem} selectCloseCalendarItem={selectCloseCalendarItem} setCloseCalendarItem={setCloseCalendarItem} />
      ) : null}

      {showAssetProfileSection ? (
      <SettingsAssetProfileSection vm={vm} submitAssetProfileDraft={submitAssetProfileDraft} selectedDraftStarterProfile={selectedDraftStarterProfile} assetProfileDraft={assetProfileDraft} selectAssetProfileStarter={selectAssetProfileStarter} approvedAssetProfiles={approvedAssetProfiles} setAssetProfileDraft={setAssetProfileDraft} approveAssetProfileDraft={approveAssetProfileDraft} loadAssetProfileLineage={loadAssetProfileLineage} setMutationConfirmation={setMutationConfirmation} rollbackAssetProfile={rollbackAssetProfile} submitProfileBackedSecurity={submitProfileBackedSecurity} selectedProfileBackedSecurityProfile={selectedProfileBackedSecurityProfile} profileBackedSecurity={profileBackedSecurity} selectProfileBackedSecurityProfile={selectProfileBackedSecurityProfile} setProfileBackedSecurity={setProfileBackedSecurity} additionalRequiredIdentifierPreferences={additionalRequiredIdentifierPreferences} updateProfileBackedSecurityField={updateProfileBackedSecurityField} />
      ) : null}

      {showProviderSection ? (
      <SettingsProviderSection vm={vm} showProviderSetupControls={showProviderSetupControls} inlineProviderManagementEnabled={inlineProviderManagementEnabled} showProviderAdvancedControls={showProviderAdvancedControls} onProviderRoutingRefresh={onProviderRoutingRefresh} showProviderFilters={showProviderFilters} providerSearch={providerSearch} setProviderSearch={setProviderSearch} providerCapabilityFilter={providerCapabilityFilter} setProviderCapabilityFilter={setProviderCapabilityFilter} providerHealthFilter={providerHealthFilter} setProviderHealthFilter={setProviderHealthFilter} providerVerificationFilter={providerVerificationFilter} setProviderVerificationFilter={setProviderVerificationFilter} providerSort={providerSort} setProviderSort={setProviderSort} activeProviderDisplayName={activeProviderDisplayName} visibleProviderGroups={visibleProviderGroups} session={session} onRefresh={onRefresh} activeTaskView={activeTaskView} providerRuntimeState={providerRuntimeState} providerOpenApiImportState={providerOpenApiImportState} updateProviderOpenApiImportState={updateProviderOpenApiImportState} submitProviderOpenApiImport={submitProviderOpenApiImport} loadProviderRuntimeEvidence={loadProviderRuntimeEvidence} setMutationConfirmation={setMutationConfirmation} runProviderRuntimeDueSync={runProviderRuntimeDueSync} createProviderRuntimeHandoff={createProviderRuntimeHandoff} replayProviderRuntimeQuarantine={replayProviderRuntimeQuarantine} resolveProviderRuntimeQuarantineRecord={resolveProviderRuntimeQuarantineRecord} />
      ) : null}

      {showDataProviderModulesSection ? (
      <section id="data-provider-modules" className="scroll-mt-6">
        <ProviderSetupPanel />
      </section>
      ) : null}

      {showAlpacaSection ? (
      <SettingsAlpacaSection vm={vm} alpacaForm={alpacaForm} />
      ) : null}

      {showRobinhoodSection ? (
      <SettingsRobinhoodSection vm={vm} robinhoodAuthorizationUrl={robinhoodAuthorizationUrl} robinhoodForm={robinhoodForm} />
      ) : null}

      {showDiagnosticsSection ? (
      <SettingsDiagnosticsSection vm={vm} overview={overview} recentEventsVm={recentEventsVm} showAdvancedDiagnosticsSection={showAdvancedDiagnosticsSection} visibleDiagnosticLinks={visibleDiagnosticLinks} />
      ) : null}
      {showRuntimeSection ? <LifecycleControlPanel /> : null}
      {showRuntimeSection ? (
      <SettingsRuntimeCapabilitiesSection vm={vm} onFeatureCapabilityToggle={onFeatureCapabilityToggle} setMutationConfirmation={setMutationConfirmation} />
      ) : null}

      {showBackendCapabilitySection ? (
      <SettingsBackendCapabilitySection vm={vm} />
      ) : null}
      <SettingsMutationConfirmDialog
        confirmation={mutationConfirmation}
        busy={mutationConfirmationBusy}
        error={mutationConfirmationError}
        onCancel={() => {
          if (!mutationConfirmationBusy) {
            setMutationConfirmation(null);
            setMutationConfirmationError(null);
          }
        }}
        onConfirm={() => void confirmSettingsMutation()}
      />
    </div>
  );
}

function buildScopedAccessRoleOptions(catalog: RolePermissionCatalog | null): Array<{ value: string; label: string }> {
  return (catalog?.roles ?? []).map((role) => ({
    value: role.role,
    label: settingsRoleDisplayLabel(role.displayName, role.role)
  }));
}

function buildScopedAccessRoleProfileOptions(catalog: RolePermissionCatalog | null): Array<{ value: string; label: string }> {
  const profileOptions = (catalog?.roles ?? [])
    .filter((role) => !role.isBuiltIn)
    .map((role) => ({
      value: role.role,
      label: settingsRoleDisplayLabel(role.displayName, role.role)
    }));

  return [
    { value: "", label: "No role profile" },
    ...profileOptions
  ];
}
