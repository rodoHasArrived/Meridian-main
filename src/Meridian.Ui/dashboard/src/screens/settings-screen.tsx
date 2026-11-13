import { ExecutionAlertSettingsPanel } from "./settings-screen.execution-alerts";
import { ProviderSetupPanel } from "@/components/data/provider-setup-panel";
import { LifecycleControlPanel } from "@/components/meridian/lifecycle-control-panel";
import { StatStrip } from "@/components/meridian/stat-strip";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { SeverityBadge } from "@/components/operations";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle
} from "@/components/ui/card";
import { Checkbox, Toggle } from "@/components/ui/checkbox";
import { DensityToggle } from "@/components/ui/density-toggle";
import { FieldSupportText, joinDescribedByIds } from "@/components/ui/field-support";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { ThemeToggle } from "@/components/ui/theme-toggle";
import { eyebrowClassName, usesTrackedCaps } from "@/design-system/label-case";
import {
  approveSecurityAssetProfile,
  assignLedgerMapping,
  createProviderIntegrationReconciliationHandoff,
  createRolePermissionProfile,
  createScopedAccessAssignment,
  createSecurityMasterEntry,
  draftSecurityAssetProfile,
  getProviderIntegrationConnectionMonitor,
  getProviderIntegrationConnectionSyncPlan,
  getProviderIntegrationConnectionSyncRuns,
  getProviderIntegrationIdentityResolution,
  getProviderIntegrationPromotionReadiness,
  getProviderIntegrationQuarantineReview,
  getProviderIntegrationReconciliationHandoffHistory,
  getProviderIntegrationStagingReview,
  getSecurityAssetProfileLineage,
  importProviderIntegrationOpenApi,
  listScopedAccessAssignments,
  replayProviderIntegrationQuarantineRecords,
  resolveProviderIntegrationQuarantineRecord,
  revokeScopedAccessAssignment,
  rollbackSecurityAssetProfile,
  runDueProviderIntegrationSync,
  upsertOperationsApprovalPolicyRule,
  upsertOperationsCloseCalendarItem
} from "@/lib/api";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import { selectSharedProviderHealth } from "@/lib/cross-screen-counts";
import { useSharedCrossScreenCounts } from "@/lib/cross-screen-counts-context";
import { formatNumber as formatNumberAmount, formatUtcDate } from "@/lib/format";
import { utcToday } from "@/lib/local-date";
import { badgeVariantToOperatorSeverity } from "@/lib/shared-tone-mappings";
import { cn } from "@/lib/utils";
import { settingsProviderAdvancedRoute, settingsProviderSetupRoute, WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { resolveFigureEvidence, useWorkspaceFigureContext } from "@/lib/workspace-figure-context";
import { SettingsMutationConfirmDialog, type SettingsMutationConfirmation } from "@/screens/settings-mutation-confirm-dialog";
import { resolveSettingsRouteState, SETTINGS_ROUTE_VIEW_COPY, type SettingsRouteViewId } from "@/screens/settings-route-state";
import {
  buildApprovalPolicyPermissionOptions,
  buildApprovalPolicyRuleDraft,
  buildCloseCalendarItemDraft,
  buildLedgerMappingAssignmentDraft,
  buildRolePermissionProfileDraft,
  humanizeSettingsIdentifier,
  settingsRoleDisplayLabel
} from "@/screens/settings-screen.operations-form-options";
import {
  buildSettingsScreenViewModel,
  useAlpacaConnectionFormViewModel,
  useRobinhoodConnectionViewModel,
  useSettingsRecentEventsSelectionViewModel,
  type SettingsAlpacaCredentialFieldState,
  type SettingsProfileAuthenticationStep,
  type SettingsProviderConnectionRow,
  type SettingsRecentEventDetail,
  type SettingsRecentEventTableRow
} from "@/screens/settings-screen.view-model";
import { SettingsTaskChooser } from "@/screens/settings-task-chooser";
import type {
  AccessPrincipalKind,
  AccessScopeKind,
  AccountingWorkspaceResponse,
  BrokerageConnectionStatus,
  DataWorkspaceResponse,
  FeatureCapabilitySettingsResponse,
  LedgerMappingWorkbench,
  OperationsApprovalPolicyMatrix,
  OperationsCloseCalendar,
  PortfolioWorkspaceResponse,
  ProviderConnectionRow,
  ProviderIntegrationQuarantinedRecord,
  ProviderIntegrationQuarantineResolutionAction,
  ProviderRoutingBinding,
  ProviderRoutingConnection,
  ProviderRoutingTrustSnapshot,
  ReportingWorkspaceResponse,
  RolePermissionCatalog,
  SecurityAssetProfileDefinition,
  SecurityAssetProfileFieldDefinition,
  SecurityAssetProfileGovernanceResult,
  SecurityAssetProfileLineage,
  SessionInfo,
  StrategyWorkspaceResponse,
  SystemOverviewResponse,
  TradingWorkspaceResponse,
  UserAccessAssignment,
  WorkspaceKey
} from "@/types";
import {
  Activity,
  ArrowRight,
  ExternalLink,
  GitBranch,
  KeyRound,
  LoaderCircle,
  MonitorCheck,
  RefreshCcw,
  Save,
  Search,
  ShieldCheck,
  Trash2,
  User
} from "lucide-react";
import {
  useEffect,
  useMemo,
  useState,
  type FormEvent,
  type ReactNode
} from "react";
import { Link, useLocation } from "react-router-dom";
import { OwnedProviderCredentialPanel } from "./settings-owned-provider-credentials";
import {
  buildProfileFieldPayload,
  buildProfileFieldValueState,
  createProfileBackedSecurityState,
  isWriteSelectableAssetProfile,
  missingRequiredIdentifierDetails,
  type ProfileBackedSecurityState
} from "./settings-profile-backed-security";
import type { ProviderOpenApiImportState, ProviderRuntimeEvidenceState, ProviderRuntimePhase } from "./settings-provider-integration";
import {
  createProviderOpenApiImportState,
  diagnosticToneClass,
  FilterSelect,
  itemToneClass,
  parseProviderOpenApiCapabilities,
  ProviderIntegrationRuntimePanel,
  providerRuntimeErrorDetail,
  providerRuntimeHandoffEvidenceId,
  providerRuntimeLatestQuarantineDecision,
  providerRuntimeQuarantineActionLabel,
  providerRuntimeQuarantineActionNote,
  providerRuntimeReplaySyncRunId,
  providerRuntimeStateKey,
  providerRuntimeValue,
  SettingsFieldRow
} from "./settings-provider-integration";

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

interface LedgerMappingAssignmentState {
  accountId: string;
  ledgerGroupId: string;
  rationale: string;
  busy: boolean;
  message: string | null;
  details: string[];
  tone: "default" | "success" | "danger";
}

interface RolePermissionProfileState {
  profileName: string;
  displayName: string;
  description: string;
  baseRole: string;
  permissionNames: string[];
  rationale: string;
  busy: boolean;
  message: string | null;
  details: string[];
  tone: "default" | "success" | "danger" | "warning";
}

interface ScopedAccessAssignmentState {
  principalId: string;
  principalKind: AccessPrincipalKind;
  scopeKind: AccessScopeKind;
  scopeId: string;
  role: string;
  roleProfileName: string;
  permissionNames: string[];
  effectiveFrom: string;
  effectiveTo: string;
  approvalLimitAmount: string;
  approvalLimitCurrency: string;
  segregationOfDutiesRule: string;
  rationale: string;
  includeRevoked: boolean;
  loading: boolean;
  busy: boolean;
  revokeBusyId: string | null;
  message: string | null;
  details: string[];
  tone: "default" | "success" | "danger" | "warning";
}

interface ApprovalPolicyRuleState {
  policyKey: string;
  requiredPermission: string;
  submitterRole: string;
  reviewerRole: string;
  requiredDistinctApprovals: number;
  requiresIndependentReviewer: boolean;
  requiresReportPack: boolean;
  requiresChecklistControlApprovals: boolean;
  evidenceRequirement: string;
  severity: string;
  rationale: string;
  busy: boolean;
  message: string | null;
  details: string[];
  tone: "default" | "success" | "danger" | "warning";
}

interface CloseCalendarItemState {
  workflowId: string;
  taskId: string;
  dueDate: string;
  owner: string;
  rationale: string;
  busy: boolean;
  message: string | null;
  details: string[];
  tone: "default" | "success" | "danger" | "warning";
}

interface AssetProfileDraftState {
  starterProfileId: string;
  profileId: string;
  name: string;
  category: string;
  subType: string;
  rationale: string;
  busyAction: "draft" | "approve" | "lineage" | "rollback" | null;
  rollbackTargetVersion: number;
  lineage: SecurityAssetProfileLineage | null;
  result: SecurityAssetProfileGovernanceResult | null;
  message: string | null;
  details: string[];
  tone: "default" | "success" | "danger" | "warning";
}
const emptyProviderRuntimeEvidenceState: ProviderRuntimeEvidenceState = {
  phase: "idle",
  message: null,
  details: [],
  monitor: null,
  syncRuns: null,
  syncPlan: null,
  staging: null,
  identity: null,
  promotion: null,
  handoff: null,
  quarantine: null
};

const systemToneClass = {
  default: "border-border/70",
  success: "border-success/30",
  warning: "border-warning/30",
  danger: "border-danger/30"
} as const;

const eventToneClass = {
  default: "border-border/70 bg-secondary/25",
  warning: "border-warning/35 bg-warning/10",
  danger: "border-danger/35 bg-danger/10"
} as const;

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

const requirementToneClass = {
  success: "border-success/30 bg-success/10 text-success",
  warning: "border-warning/35 bg-warning/10 text-warning",
  muted: "border-border/70 bg-secondary/25 text-muted-foreground"
} as const;

const environmentOptionClass = {
  paper: {
    selected: "border-paper/40 bg-paper/10 text-[var(--state-paper-fg)]",
    idle: "border-border/70 bg-secondary/25 text-foreground hover:border-paper/35 hover:bg-paper/10",
    badge: "border-paper/30 bg-paper/10 text-[var(--state-paper-fg)]"
  },
  live: {
    selected: "border-live-env/40 bg-live-env/10 text-live-env",
    idle: "border-border/70 bg-secondary/25 text-foreground hover:border-live-env/35 hover:bg-live-env/10",
    badge: "border-live-env/35 bg-live-env/10 text-[var(--state-live-fg)]"
  }
} as const;

function AlpacaCredentialField({
  field,
  value,
  onValueChange,
  leadingIcon
}: {
  field: SettingsAlpacaCredentialFieldState;
  value: string;
  onValueChange: (value: string) => void;
  leadingIcon: ReactNode;
}) {
  const disabledReasonId = `${field.id}-disabled-reason`;
  return (
    <label htmlFor={field.id} className="grid gap-1 text-xs font-medium text-muted-foreground">
      {field.label}
      <Input
        id={field.id}
        type={field.type}
        value={value}
        onChange={(event) => onValueChange(event.target.value)}
        autoComplete={field.autoComplete}
        placeholder={field.placeholder}
        leadingIcon={leadingIcon}
        disabled={field.disabled}
        error={field.error}
        aria-describedby={joinDescribedByIds(field.describedBy, field.disabledReason ? disabledReasonId : undefined)}
      />
      <FieldSupportText
        helpId={field.helpId}
        helpText={field.helpText}
        helpClassName={cn("text-[11px] leading-4", field.error ? "text-danger" : "text-muted-foreground")}
        disabledReason={field.disabledReason}
        disabledReasonId={field.disabledReason ? disabledReasonId : undefined}
        disabledReasonClassName="text-[11px] leading-4"
      />
    </label>
  );
}

const setupStepToneClass = {
  success: "border-success/30 bg-success/10",
  warning: "border-warning/35 bg-warning/10",
  danger: "border-danger/35 bg-danger/10",
  muted: "border-border/70 bg-secondary/25"
} as const;

const recentEventColumns: DenseDataTableColumn<SettingsRecentEventTableRow>[] = [
  {
    id: "status",
    label: "Status",
    render: (event) => <Badge variant={event.badgeVariant}>{event.statusCode}</Badge>
  },
  {
    id: "message",
    label: "Message",
    className: "min-w-[14rem]",
    render: (event) => <span className="text-foreground">{event.message}</span>
  },
  {
    id: "source",
    label: "Source",
    className: "font-mono text-muted-foreground",
    render: (event) => event.source
  },
  {
    id: "timestamp",
    label: "Timestamp",
    className: "font-mono text-muted-foreground",
    render: (event) => event.timestamp
  }
];

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
  const [providerSearch, setProviderSearch] = useState("");
  const [providerCapabilityFilter, setProviderCapabilityFilter] = useState<"all" | "brokerage" | "data" | "accounting">("all");
  const [providerHealthFilter, setProviderHealthFilter] = useState<"all" | "healthy" | "warning" | "blocked">("all");
  const [providerVerificationFilter, setProviderVerificationFilter] = useState<"all" | "verified" | "unverified">("all");
  const [providerSort, setProviderSort] = useState<"risk" | "name">("risk");
  const [providerRuntimeState, setProviderRuntimeState] = useState<Record<string, ProviderRuntimeEvidenceState>>({});
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
  const scopedAccessActiveCount = scopedAccessAssignments.filter((assignment) => !assignment.revokedAtUtc).length;
  const scopedAccessRevokedCount = scopedAccessAssignments.length - scopedAccessActiveCount;
  const providerInlineFlag = featureCapabilities?.capabilities.find((capability) => (
    capability.capabilityKey === "desktop.settings.provider-connection-center-inline-management"
  ));
  const inlineProviderManagementEnabled = providerInlineFlag?.isEnabled ?? true;
  const allProviderRows = useMemo(
    () => vm.providerConnectionCenter.groups.flatMap((group) => group.rows),
    [vm.providerConnectionCenter.groups]
  );
  const activeProviderDisplayName = activeProviderId
    ? allProviderRows.find((row) => row.providerId.toLowerCase() === activeProviderId.toLowerCase())?.displayName
      ?? humanizeSettingsIdentifier(activeProviderId)
    : null;
  const providerRowIdsSignature = useMemo(
    () => allProviderRows.map((row) => row.providerId).sort().join("|"),
    [allProviderRows]
  );
  const providerRiskScores = useMemo(
    () => Object.fromEntries(allProviderRows.map((row) => [row.providerId, providerRiskScore(row)])),
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
  const showProviderFilters = activeTaskView === "providers" || legacyEmbeddedSettingsView;
  const showProviderSetupControls = activeTaskView === "provider-setup" || legacyEmbeddedSettingsView;
  const showProviderAdvancedControls = activeTaskView === "provider-advanced" || legacyEmbeddedSettingsView;
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

  const filteredProviderGroups = useMemo(() => {
    const search = providerSearch.trim().toLowerCase();
    return vm.providerConnectionCenter.groups.map((group) => {
      const rows = group.rows
        .filter((row) => filterProviderRow(row, search, providerCapabilityFilter, providerHealthFilter, providerVerificationFilter))
        .sort((left, right) => {
          if (providerSort === "name") {
            return left.displayName.localeCompare(right.displayName);
          }
          const riskScore = (providerRiskScores[right.providerId] ?? 0) - (providerRiskScores[left.providerId] ?? 0);
          return riskScore !== 0 ? riskScore : left.displayName.localeCompare(right.displayName);
        });
      return {
        ...group,
        rows
      };
    });
  }, [
    providerCapabilityFilter,
    providerHealthFilter,
    providerRiskScores,
    providerSearch,
    providerSort,
    providerVerificationFilter,
    vm.providerConnectionCenter.groups
  ]);
  const visibleProviderGroups = useMemo(() => {
    if (!activeProviderId || activeTaskView === "providers" || legacyEmbeddedSettingsView) {
      return filteredProviderGroups;
    }

    return filteredProviderGroups
      .map((group) => ({
        ...group,
        rows: group.rows.filter((row) => row.providerId.toLowerCase() === activeProviderId.toLowerCase())
      }))
      .filter((group) => group.rows.length > 0);
  }, [activeProviderId, activeTaskView, filteredProviderGroups, legacyEmbeddedSettingsView]);

  const loadProviderRuntimeEvidence = async (row: SettingsProviderConnectionRow) => {
    const stateKey = providerRuntimeStateKey(row);
    const connectionId = row.integrationConnectionId;

    setProviderRuntimeState((current) => ({
      ...current,
      [stateKey]: {
        ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
        phase: "loading",
        message: null,
        details: []
      }
    }));

    const evaluatedAt = new Date().toISOString();
    const [
      monitorResult,
      syncRunsResult,
      syncPlanResult,
      stagingResult,
      identityResult,
      promotionResult,
      handoffResult,
      quarantineResult
    ] = await Promise.allSettled([
      getProviderIntegrationConnectionMonitor(connectionId, 5),
      getProviderIntegrationConnectionSyncRuns(connectionId, 5),
      getProviderIntegrationConnectionSyncPlan(connectionId, evaluatedAt),
      getProviderIntegrationStagingReview(connectionId, 5),
      getProviderIntegrationIdentityResolution(connectionId, 5),
      getProviderIntegrationPromotionReadiness(connectionId, 5),
      getProviderIntegrationReconciliationHandoffHistory(connectionId),
      getProviderIntegrationQuarantineReview(connectionId, 5)
    ]);

    const details = [
      providerRuntimeErrorDetail(monitorResult, "Connection monitor"),
      providerRuntimeErrorDetail(syncRunsResult, "Sync-run history"),
      providerRuntimeErrorDetail(syncPlanResult, "Sync plan"),
      providerRuntimeErrorDetail(stagingResult, "Staging review"),
      providerRuntimeErrorDetail(identityResult, "Identity resolution"),
      providerRuntimeErrorDetail(promotionResult, "Promotion readiness"),
      providerRuntimeErrorDetail(handoffResult, "Reconciliation handoff history"),
      providerRuntimeErrorDetail(quarantineResult, "Quarantine review")
    ].filter((detail): detail is string => Boolean(detail));
    const failedCount = details.length;
    const phase: ProviderRuntimePhase = failedCount === 8 ? "error" : "loaded";
    const warningSuffix = failedCount === 1 ? "warning" : "warnings";

    setProviderRuntimeState((current) => ({
      ...current,
      [stateKey]: {
        phase,
        message: failedCount === 0
          ? "Provider integration runtime evidence loaded."
          : failedCount === 8
            ? "Provider integration runtime evidence unavailable."
            : `Provider integration runtime loaded with ${failedCount} ${warningSuffix}.`,
        details,
        monitor: providerRuntimeValue(monitorResult),
        syncRuns: providerRuntimeValue(syncRunsResult),
        syncPlan: providerRuntimeValue(syncPlanResult),
        staging: providerRuntimeValue(stagingResult),
        identity: providerRuntimeValue(identityResult),
        promotion: providerRuntimeValue(promotionResult),
        handoff: providerRuntimeValue(handoffResult),
        quarantine: providerRuntimeValue(quarantineResult)
      }
    }));
  };

  const updateProviderOpenApiImportState = (
    row: SettingsProviderConnectionRow,
    updater: (state: ProviderOpenApiImportState) => ProviderOpenApiImportState
  ) => {
    const stateKey = providerRuntimeStateKey(row);
    setProviderOpenApiImportState((current) => ({
      ...current,
      [stateKey]: updater(current[stateKey] ?? createProviderOpenApiImportState(row))
    }));
  };

  const submitProviderOpenApiImport = async (row: SettingsProviderConnectionRow) => {
    const stateKey = providerRuntimeStateKey(row);
    const formState = providerOpenApiImportState[stateKey] ?? createProviderOpenApiImportState(row);
    const importedAt = new Date();
    const capabilities = parseProviderOpenApiCapabilities(formState.capabilities);

    if (!formState.manifestId.trim() || !formState.openApiDocumentJson.trim() || capabilities.length === 0) {
      updateProviderOpenApiImportState(row, (state) => ({
        ...state,
        message: "Manifest id, capability list, and OpenAPI JSON are required before importing a draft.",
        details: [],
        tone: "warning"
      }));
      return;
    }

    updateProviderOpenApiImportState(row, (state) => ({
      ...state,
      busy: true,
      message: "Importing OpenAPI draft manifest.",
      details: [],
      tone: "default"
    }));

    try {
      const result = await importProviderIntegrationOpenApi({
        manifestId: formState.manifestId.trim(),
        providerId: row.providerId,
        displayName: formState.displayName.trim() || row.displayName,
        environment: formState.environment.trim() || "paper",
        authType: formState.authType,
        tokenUrl: formState.tokenUrl.trim() || null,
        scopes: formState.scopes.split(",").map((scope) => scope.trim()).filter(Boolean),
        capabilities,
        openApiDocumentJson: formState.openApiDocumentJson,
        importedBy: session?.displayName ?? "settings-operator",
        importedAt: importedAt.toISOString(),
        expectedManifestReference: formState.result?.manifestReference?.manifestId === formState.manifestId.trim()
          ? formState.result.manifestReference
          : undefined,
        changeReason: formState.changeReason.trim() || "Imported from the Settings Provider Connection Center."
      });

      updateProviderOpenApiImportState(row, (state) => ({
        ...state,
        busy: false,
        result,
        message: result.message ?? `OpenAPI draft imported for ${result.manifest.manifestId}.`,
        details: [
          `${result.manifest.endpoints.length} endpoints seeded.`,
          `${result.readiness.requiredEvidence.length} readiness evidence requirements.`,
          ...result.issues.map((issue) => issue.message)
        ],
        tone: result.readiness.isReady && result.issues.length === 0 ? "success" : "warning"
      }));
    } catch (error) {
      const display = describeApiError(error, "Provider integration OpenAPI import failed.");
      updateProviderOpenApiImportState(row, (state) => ({
        ...state,
        busy: false,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  const runProviderRuntimeDueSync = async (row: SettingsProviderConnectionRow) => {
    const stateKey = providerRuntimeStateKey(row);
    const requestedAt = new Date();

    setProviderRuntimeState((current) => ({
      ...current,
      [stateKey]: {
        ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
        phase: "loading",
        message: "Provider integration due-sync is running.",
        details: []
      }
    }));

    try {
      const result = await runDueProviderIntegrationSync(row.integrationConnectionId, {
        connectionId: row.integrationConnectionId,
        requestedAt: requestedAt.toISOString(),
        requestedBy: session?.displayName ?? "settings-operator",
        maxPages: 2,
        pathParametersByCapability: {},
        queryParametersByCapability: {}
      });

      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: `Due-sync completed: ${result.startedCount} started / ${result.skippedCount} skipped.`,
          details: result.items.flatMap((item) => item.issues.map((issue) => issue.message))
        }
      }));
      await loadProviderRuntimeEvidence(row);
    } catch (error) {
      const display = describeApiError(error, "Provider integration due-sync failed.");
      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: display.summary,
          details: display.details
        }
      }));
    }
  };

  const createProviderRuntimeHandoff = async (row: SettingsProviderConnectionRow) => {
    const stateKey = providerRuntimeStateKey(row);
    const currentState = providerRuntimeState[stateKey];
    const readyRows = currentState?.promotion?.rows.filter((promotionRow) => promotionRow.status === "ReadyForReconciliation") ?? [];
    const alreadyHandedOff = new Set((currentState?.handoff?.records ?? []).map((record) => record.stagingRecordId));
    const stagingRecordIds = readyRows
      .map((promotionRow) => promotionRow.stagingRecordId)
      .filter((stagingRecordId) => !alreadyHandedOff.has(stagingRecordId));
    const requestedAt = new Date();

    if (stagingRecordIds.length === 0) {
      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: "No promotion-ready staging rows are available for reconciliation handoff.",
          details: []
        }
      }));
      return;
    }

    setProviderRuntimeState((current) => ({
      ...current,
      [stateKey]: {
        ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
        phase: "loading",
        message: "Creating provider integration reconciliation handoff.",
        details: []
      }
    }));

    try {
      const result = await createProviderIntegrationReconciliationHandoff({
        connectionId: row.integrationConnectionId,
        stagingRecordIds,
        requestedBy: session?.displayName ?? "settings-operator",
        requestedAt: requestedAt.toISOString(),
        approvalEvidenceId: providerRuntimeHandoffEvidenceId(row.integrationConnectionId, requestedAt),
        note: "Approved from the Settings Provider Connection Center promotion readiness panel.",
        recentRunLimit: 5
      });

      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: `Reconciliation handoff ${result.accepted ? "created" : "reviewed"}: ${result.acceptedRecordCount} accepted / ${result.duplicateRecordCount} duplicate.`,
          details: result.issues.map((issue) => issue.message)
        }
      }));
      await loadProviderRuntimeEvidence(row);
    } catch (error) {
      const display = describeApiError(error, "Provider integration reconciliation handoff failed.");
      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: display.summary,
          details: display.details
        }
      }));
    }
  };

  const replayProviderRuntimeQuarantine = async (row: SettingsProviderConnectionRow) => {
    const stateKey = providerRuntimeStateKey(row);
    const currentState = providerRuntimeState[stateKey];
    const replayEligibleRecords = currentState?.quarantine?.records.filter((record) =>
      !providerRuntimeLatestQuarantineDecision(currentState.quarantine?.decisions, record)
    ) ?? [];
    const replaySeed = replayEligibleRecords[0] ?? null;
    const manifestId = currentState?.monitor?.manifestId ?? null;
    if (!currentState || !replaySeed || !manifestId) {
      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: "Quarantine replay is unavailable until runtime evidence includes monitor data and undecided quarantine records.",
          details: [],
        }
      }));
      return;
    }

    const quarantineRecordIds = replayEligibleRecords
      .filter((record) => record.syncRunId === replaySeed.syncRunId && record.capability === replaySeed.capability)
      .map((record) => record.quarantineRecordId);
    const requestedAt = new Date();

    setProviderRuntimeState((current) => ({
      ...current,
      [stateKey]: {
        ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
        phase: "loading",
        message: "Provider integration quarantine replay is running.",
        details: []
      }
    }));

    try {
      const replayResult = await replayProviderIntegrationQuarantineRecords({
        replaySyncRunId: providerRuntimeReplaySyncRunId(row.integrationConnectionId, requestedAt),
        sourceSyncRunId: replaySeed.syncRunId,
        manifestId,
        connectionId: row.integrationConnectionId,
        capability: replaySeed.capability,
        quarantineRecordIds,
        requestedBy: session?.displayName ?? "settings-operator",
        requestedAt: requestedAt.toISOString(),
        mode: "Original"
      });

      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: `Quarantine replay completed: ${replayResult.recordsAccepted} accepted / ${replayResult.recordsRequarantined} requarantined.`,
          details: replayResult.issues.map((issue) => issue.message)
        }
      }));
      await loadProviderRuntimeEvidence(row);
    } catch (error) {
      const display = describeApiError(error, "Provider integration quarantine replay failed.");
      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: display.summary,
          details: display.details
        }
      }));
    }
  };

  const resolveProviderRuntimeQuarantineRecord = async (
    row: SettingsProviderConnectionRow,
    record: ProviderIntegrationQuarantinedRecord,
    action: ProviderIntegrationQuarantineResolutionAction
  ) => {
    const stateKey = providerRuntimeStateKey(row);
    const reviewedAt = new Date();
    const actionLabel = providerRuntimeQuarantineActionLabel(action);

    setProviderRuntimeState((current) => ({
      ...current,
      [stateKey]: {
        ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
        phase: "loading",
        message: `Recording ${actionLabel.toLowerCase()} decision for ${record.quarantineRecordId}.`,
        details: []
      }
    }));

    try {
      const result = await resolveProviderIntegrationQuarantineRecord({
        connectionId: row.integrationConnectionId,
        syncRunId: record.syncRunId,
        quarantineRecordId: record.quarantineRecordId,
        action,
        reviewedBy: session?.displayName ?? "settings-operator",
        reviewedAt: reviewedAt.toISOString(),
        note: providerRuntimeQuarantineActionNote(action)
      });

      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: result.message ?? `${actionLabel} decision recorded for ${record.quarantineRecordId}.`,
          details: []
        }
      }));
      await loadProviderRuntimeEvidence(row);
    } catch (error) {
      const display = describeApiError(error, `Provider integration quarantine ${actionLabel.toLowerCase()} decision failed.`);
      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: display.summary,
          details: display.details
        }
      }));
    }
  };

  const submitLedgerMappingAssignment = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!ledgerMappingAssignment.accountId || !ledgerMappingAssignment.ledgerGroupId) {
      setLedgerMappingAssignment((current) => ({
        ...current,
        message: "Choose an account and ledger group before saving.",
        details: [],
        tone: "danger"
      }));
      return;
    }

    setLedgerMappingAssignment((current) => ({
      ...current,
      busy: true,
      message: null,
      details: [],
      tone: "default"
    }));

    try {
      const result = await assignLedgerMapping({
        accountId: ledgerMappingAssignment.accountId,
        ledgerGroupId: ledgerMappingAssignment.ledgerGroupId,
        requestedBy: session?.displayName ?? "settings-screen",
        rationale: ledgerMappingAssignment.rationale,
        correlationId: `settings-ledger-map-${Date.now()}`
      });
      await onRefresh?.();
      setLedgerMappingAssignment((current) => ({
        ...current,
        busy: false,
        message: `Ledger mapping saved for ${result.account.accountCode}.`,
        details: [
          `Audit ${result.auditEvent.auditId}`,
          `Correlation ${result.auditEvent.correlationId}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Ledger mapping assignment failed.");
      setLedgerMappingAssignment((current) => ({
        ...current,
        busy: false,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  const toggleRoleProfilePermission = (permissionName: string, checked: boolean) => {
    setRolePermissionProfile((current) => ({
      ...current,
      permissionNames: checked
        ? Array.from(new Set([...current.permissionNames, permissionName]))
        : current.permissionNames.filter((name) => name !== permissionName),
      message: null,
      details: [],
      tone: "default"
    }));
  };

  const submitRolePermissionProfile = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const profileName = rolePermissionProfile.profileName.trim();
    const displayName = rolePermissionProfile.displayName.trim() || profileName;
    if (!roleProfileDraft.canSave || !profileName || !displayName || rolePermissionProfile.permissionNames.length === 0) {
      setRolePermissionProfile((current) => ({
        ...current,
        message: "Profile name, display name, base role, and at least one permission are required.",
        details: roleProfileDraft.disabledReason ? [roleProfileDraft.disabledReason] : [],
        tone: "warning"
      }));
      return;
    }

    setRolePermissionProfile((current) => ({
      ...current,
      busy: true,
      message: null,
      details: [],
      tone: "default"
    }));

    try {
      const result = await createRolePermissionProfile({
        profileName,
        displayName,
        description: rolePermissionProfile.description.trim() || null,
        baseRole: rolePermissionProfile.baseRole,
        permissionNames: rolePermissionProfile.permissionNames,
        requestedBy: session?.displayName ?? "settings-screen",
        rationale: rolePermissionProfile.rationale,
        correlationId: `settings-role-profile-${Date.now()}`
      });
      await onRefresh?.();
      setRolePermissionProfile((current) => ({
        ...current,
        busy: false,
        message: `Role profile saved for ${result.profile.displayName}.`,
        details: [
          `Audit ${result.auditEvent.auditId}`,
          `Correlation ${result.auditEvent.correlationId}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Role profile save failed.");
      setRolePermissionProfile((current) => ({
        ...current,
        busy: false,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  const selectScopedAccessRole = (role: string) => {
    const selectedRole = rolePermissionCatalog?.roles.find((entry) => entry.role === role);
    setScopedAccess((current) => ({
      ...current,
      role,
      permissionNames: selectedRole?.permissions ?? current.permissionNames,
      message: null,
      details: [],
      tone: "default"
    }));
  };

  const toggleScopedAccessPermission = (permissionName: string, checked: boolean) => {
    setScopedAccess((current) => ({
      ...current,
      permissionNames: checked
        ? Array.from(new Set([...current.permissionNames, permissionName]))
        : current.permissionNames.filter((name) => name !== permissionName),
      message: null,
      details: [],
      tone: "default"
    }));
  };

  const submitScopedAccessAssignment = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const principalId = scopedAccess.principalId.trim();
    const scopeId = scopedAccess.scopeKind === "Global" ? null : scopedAccess.scopeId.trim();
    const rationale = scopedAccess.rationale.trim();
    const approvalLimitText = scopedAccess.approvalLimitAmount.trim();
    let approvalLimitAmount: number | null = null;
    const approvalLimitCurrency = scopedAccess.approvalLimitCurrency.trim().toUpperCase();
    const segregationOfDutiesRule = scopedAccess.segregationOfDutiesRule.trim();
    if (!principalId || !scopedAccess.role || scopedAccess.permissionNames.length === 0 || !rationale || (!scopeId && scopedAccess.scopeKind !== "Global")) {
      setScopedAccess((current) => ({
        ...current,
        message: "Principal, scope, role, at least one permission, and rationale are required.",
        details: scopedAccess.scopeKind !== "Global" && !scopeId ? ["Scoped grants require a concrete scope ID."] : [],
        tone: "warning"
      }));
      return;
    }
    if (approvalLimitText) {
      const parsedApprovalLimitAmount = Number(approvalLimitText);
      if (!Number.isFinite(parsedApprovalLimitAmount) || parsedApprovalLimitAmount <= 0) {
        setScopedAccess((current) => ({
          ...current,
          message: "Approval limit must be greater than zero.",
          details: ["Use a positive numeric amount or leave the approval limit blank."],
          tone: "warning"
        }));
        return;
      }

      approvalLimitAmount = parsedApprovalLimitAmount;
    }
    if (approvalLimitAmount !== null && !/^[A-Z]{3}$/.test(approvalLimitCurrency)) {
      setScopedAccess((current) => ({
        ...current,
        message: "Approval limit currency must be a three-letter code.",
        details: ["Use an ISO-style currency code such as USD."],
        tone: "warning"
      }));
      return;
    }

    setMutationConfirmation({
      id: `grant-access-${principalId}`,
      title: "Confirm scoped access grant",
      description: `Grant ${scopedAccess.role} authority to ${principalId} for ${scopedAccess.scopeKind === "Global" ? "the global scope" : `${scopedAccess.scopeKind} ${scopeId}`} with ${scopedAccess.permissionNames.length} permission${scopedAccess.permissionNames.length === 1 ? "" : "s"}. Meridian will retain the server-owned audit record.`,
      confirmLabel: "Confirm grant",
      confirmAriaLabel: `Confirm scoped access grant for ${principalId}`,
      run: async () => {
        setScopedAccess((current) => ({
          ...current,
          busy: true,
          message: null,
          details: [],
          tone: "default"
        }));

        try {
          const result = await createScopedAccessAssignment({
        principalId,
        principalKind: scopedAccess.principalKind,
        scopeKind: scopedAccess.scopeKind,
        scopeId,
        role: scopedAccess.role,
        roleProfileName: scopedAccess.roleProfileName || null,
        permissionNames: scopedAccess.permissionNames,
        effectiveFrom: toScopedAccessDateTime(scopedAccess.effectiveFrom),
        effectiveTo: toScopedAccessDateTime(scopedAccess.effectiveTo),
        requestedBy: session?.displayName ?? "settings-screen",
        rationale,
        approvalLimitAmount,
        approvalLimitCurrency: approvalLimitAmount === null ? null : approvalLimitCurrency,
        segregationOfDutiesRule: segregationOfDutiesRule || null,
        correlationId: `settings-scoped-access-${Date.now()}`
      });
          setScopedAccessAssignments((current) => upsertScopedAccessAssignment(current, result.assignment));
          await onRefresh?.();
          setScopedAccess((current) => ({
            ...current,
            principalId: "",
            scopeId: current.scopeKind === "Global" ? "" : current.scopeId,
            approvalLimitAmount: "",
            approvalLimitCurrency: current.approvalLimitCurrency || "USD",
            segregationOfDutiesRule: "",
            busy: false,
            message: `Scoped access granted for ${result.assignment.principalId}.`,
            details: [
              `Audit ${result.auditEvent.auditId}`,
              `Correlation ${result.auditEvent.correlationId}`,
              `Version ${result.assignment.version}`
            ],
            tone: "success"
          }));
        } catch (error) {
          const display = describeApiError(error, "Scoped access grant failed.");
          setScopedAccess((current) => ({
            ...current,
            busy: false,
            message: display.summary,
            details: display.details,
            tone: "danger"
          }));
        }
      }
    });
  };

  const revokeScopedAccess = async (assignment: UserAccessAssignment) => {
    if (assignment.revokedAtUtc) {
      return;
    }

    setScopedAccess((current) => ({
      ...current,
      revokeBusyId: assignment.assignmentId,
      message: null,
      details: [],
      tone: "default"
    }));

    try {
      const result = await revokeScopedAccessAssignment({
        assignmentId: assignment.assignmentId,
        expectedVersion: assignment.version,
        requestedBy: session?.displayName ?? "settings-screen",
        rationale: `Revoke scoped authority for ${assignment.principalId}.`,
        correlationId: `settings-scoped-access-revoke-${Date.now()}`
      });
      setScopedAccessAssignments((current) => upsertScopedAccessAssignment(current, result.assignment).filter((entry) => (
        scopedAccess.includeRevoked || !entry.revokedAtUtc
      )));
      await onRefresh?.();
      setScopedAccess((current) => ({
        ...current,
        revokeBusyId: null,
        message: `Scoped access revoked for ${result.assignment.principalId}.`,
        details: [
          `Audit ${result.auditEvent.auditId}`,
          `Correlation ${result.auditEvent.correlationId}`,
          `Version ${result.assignment.version}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Scoped access revoke failed.");
      setScopedAccess((current) => ({
        ...current,
        revokeBusyId: null,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  const selectApprovalPolicyRule = (policyKey: string) => {
    const selected = approvalPolicyDraft.rows.find((row) => row.policyKey === policyKey);
    if (!selected) {
      return;
    }

    setApprovalPolicyRule((current) => ({
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
    }));
  };

  const submitApprovalPolicyRule = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const selected = approvalPolicyDraft.rows.find((row) => row.policyKey === approvalPolicyRule.policyKey);
    if (!approvalPolicyDraft.canSave || !selected) {
      setApprovalPolicyRule((current) => ({
        ...current,
        message: "Choose an approval policy rule before saving.",
        details: approvalPolicyDraft.disabledReason ? [approvalPolicyDraft.disabledReason] : [],
        tone: "warning"
      }));
      return;
    }

    if (approvalPolicyRule.requiredDistinctApprovals < 1) {
      setApprovalPolicyRule((current) => ({
        ...current,
        message: "Required approvals must be at least 1.",
        details: [],
        tone: "warning"
      }));
      return;
    }

    setApprovalPolicyRule((current) => ({
      ...current,
      busy: true,
      message: null,
      details: [],
      tone: "default"
    }));

    try {
      const result = await upsertOperationsApprovalPolicyRule({
        ...selected,
        requiredPermission: approvalPolicyRule.requiredPermission,
        submitterRole: approvalPolicyRule.submitterRole,
        reviewerRole: approvalPolicyRule.reviewerRole,
        requiredDistinctApprovals: approvalPolicyRule.requiredDistinctApprovals,
        requiresIndependentReviewer: approvalPolicyRule.requiresIndependentReviewer,
        requiresReportPack: approvalPolicyRule.requiresReportPack,
        requiresChecklistControlApprovals: approvalPolicyRule.requiresChecklistControlApprovals,
        evidenceRequirement: approvalPolicyRule.evidenceRequirement,
        severity: approvalPolicyRule.severity,
        requestedBy: session?.displayName ?? "settings-screen",
        rationale: approvalPolicyRule.rationale,
        correlationId: `settings-approval-policy-${Date.now()}`
      });
      await onRefresh?.();
      setApprovalPolicyRule((current) => ({
        ...current,
        busy: false,
        message: `Approval policy saved for ${result.rule.action}.`,
        details: [
          `Audit ${result.auditEvent.auditId}`,
          `Correlation ${result.auditEvent.correlationId}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Approval policy save failed.");
      setApprovalPolicyRule((current) => ({
        ...current,
        busy: false,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  const selectCloseCalendarItem = (workflowId: string) => {
    const selected = closeCalendarDraft.items.find((item) => item.workflowId === workflowId);
    if (!selected) {
      return;
    }

    setCloseCalendarItem((current) => ({
      ...current,
      workflowId: selected.workflowId,
      taskId: selected.nextDueTaskId ?? "",
      dueDate: selected.nextDueDate ?? "",
      owner: selected.nextDueOwner ?? "",
      message: null,
      details: [],
      tone: "default"
    }));
  };

  const submitCloseCalendarItem = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const selected = closeCalendarDraft.items.find((item) => item.workflowId === closeCalendarItem.workflowId);
    if (!closeCalendarDraft.canSave || !selected) {
      setCloseCalendarItem((current) => ({
        ...current,
        message: "Choose an account close workflow before saving.",
        details: closeCalendarDraft.disabledReason ? [closeCalendarDraft.disabledReason] : [],
        tone: "warning"
      }));
      return;
    }

    if (!closeCalendarItem.taskId.trim() || !closeCalendarItem.dueDate || !closeCalendarItem.owner.trim()) {
      setCloseCalendarItem((current) => ({
        ...current,
        message: "Task, due date, owner, and rationale are required.",
        details: [],
        tone: "warning"
      }));
      return;
    }

    setCloseCalendarItem((current) => ({
      ...current,
      busy: true,
      message: null,
      details: [],
      tone: "default"
    }));

    try {
      const result = await upsertOperationsCloseCalendarItem({
        workflowId: selected.workflowId,
        taskId: closeCalendarItem.taskId,
        dueDate: closeCalendarItem.dueDate,
        owner: closeCalendarItem.owner,
        requestedBy: session?.displayName ?? "settings-screen",
        rationale: closeCalendarItem.rationale,
        correlationId: `settings-close-calendar-${Date.now()}`
      });
      await onRefresh?.();
      setCloseCalendarItem((current) => ({
        ...current,
        busy: false,
        message: `Close calendar saved for ${result.item.periodId}.`,
        details: [
          `Audit ${result.auditEvent.auditId}`,
          `Correlation ${result.auditEvent.correlationId}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Close calendar save failed.");
      setCloseCalendarItem((current) => ({
        ...current,
        busy: false,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  const selectedDraftStarterProfile = approvedAssetProfiles.find((profile) => profile.profileId === assetProfileDraft.starterProfileId) ??
    firstApprovedAssetProfile;
  const selectedProfileBackedSecurityProfile = approvedAssetProfiles.find((profile) => profile.profileId === profileBackedSecurity.profileId) ??
    firstApprovedAssetProfile;
  // The write seam enforces the profile's REQUIRED identifier preferences at creation, so the form
  // must collect every required kind — an InternalCode-only form makes a profile requiring CUSIP
  // fail SM_CUSTOM_PROFILE_IDENTIFIER_COVERAGE_MISSING on every submission. InternalCode keeps its
  // dedicated primary-identifier input; the remaining required kinds render below.
  const additionalRequiredIdentifierPreferences = (selectedProfileBackedSecurityProfile?.identifierPreferences ?? [])
    .filter((preference) => preference.isRequiredForClose && preference.kind !== "InternalCode");

  const selectAssetProfileStarter = (profileId: string) => {
    const selected = approvedAssetProfiles.find((profile) => profile.profileId === profileId);
    setAssetProfileDraft(createAssetProfileDraftState(selected ?? null));
  };

  const selectProfileBackedSecurityProfile = (profileId: string) => {
    const selected = approvedAssetProfiles.find((profile) => profile.profileId === profileId);
    setProfileBackedSecurity((current) => ({
      ...createProfileBackedSecurityState(selected ?? null),
      displayName: current.displayName,
      internalCode: current.internalCode,
      currency: current.currency || "USD"
    }));
  };

  const updateProfileBackedSecurityField = (fieldKey: string, value: string) => {
    setProfileBackedSecurity((current) => ({
      ...current,
      fieldValues: {
        ...current.fieldValues,
        [fieldKey]: value
      },
      message: null,
      details: [],
      tone: "default"
    }));
  };

  const submitAssetProfileDraft = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!selectedDraftStarterProfile) {
      setAssetProfileDraft((current) => ({
        ...current,
        message: "Choose an approved starter profile before drafting.",
        details: vm.assetProfileGovernancePanel.createDisabledReason ? [vm.assetProfileGovernancePanel.createDisabledReason] : [],
        tone: "warning"
      }));
      return;
    }

    const profileId = normalizeAssetProfileId(assetProfileDraft.profileId);
    const name = assetProfileDraft.name.trim();
    const category = assetProfileDraft.category.trim();
    const rationale = assetProfileDraft.rationale.trim();
    if (!profileId || !name || !category || !rationale) {
      setAssetProfileDraft((current) => ({
        ...current,
        message: "Profile id, name, category, and rationale are required.",
        details: [],
        tone: "warning"
      }));
      return;
    }

    setAssetProfileDraft((current) => ({ ...current, busyAction: "draft", message: null, details: [], tone: "default" }));
    try {
      const result = await draftSecurityAssetProfile({
        profileId,
        name,
        category,
        subType: assetProfileDraft.subType.trim() || null,
        fields: selectedDraftStarterProfile.fields,
        identifierPreferences: selectedDraftStarterProfile.identifierPreferences,
        lifecycleStates: selectedDraftStarterProfile.lifecycleStates,
        accountingImpactHints: selectedDraftStarterProfile.accountingImpactHints,
        dateOrderRules: selectedDraftStarterProfile.dateOrderRules,
        requestedBy: session?.displayName ?? "settings-screen",
        rationale,
        correlationId: `settings-asset-profile-draft-${Date.now()}`
      });
      await onRefresh?.();
      setAssetProfileDraft((current) => ({
        ...current,
        profileId,
        name,
        category,
        busyAction: null,
        rollbackTargetVersion: result.profile.version,
        lineage: result.lineage,
        result,
        message: `Draft saved for ${result.profile.name} v${result.profile.version}.`,
        details: [
          `Audit ${result.auditEvent.auditId}`,
          `Correlation ${result.auditEvent.correlationId}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Asset profile draft failed.");
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  const approveAssetProfileDraft = async () => {
    const profileId = normalizeAssetProfileId(assetProfileDraft.profileId);
    const version = assetProfileDraft.result?.profile.version ?? assetProfileDraft.rollbackTargetVersion;
    if (!profileId || version < 1) {
      setAssetProfileDraft((current) => ({
        ...current,
        message: "Draft a profile version before approval.",
        details: [],
        tone: "warning"
      }));
      return;
    }

    setAssetProfileDraft((current) => ({ ...current, busyAction: "approve", message: null, details: [], tone: "default" }));
    try {
      const result = await approveSecurityAssetProfile({
        profileId,
        version,
        effectiveFrom: todayDateOnly(),
        approvalReference: `settings:${profileId}:v${version}`,
        requestedBy: session?.displayName ?? "settings-screen",
        rationale: assetProfileDraft.rationale.trim() || "Approve asset profile for governed Security Master creation.",
        correlationId: `settings-asset-profile-approve-${Date.now()}`
      });
      await onRefresh?.();
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        lineage: result.lineage,
        result,
        message: `Approved ${result.profile.name} v${result.profile.version}.`,
        details: [
          `Audit ${result.auditEvent.auditId}`,
          `Correlation ${result.auditEvent.correlationId}`,
          `Reference ${result.auditEvent.approvalReference ?? "n/a"}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Asset profile approval failed.");
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  const loadAssetProfileLineage = async () => {
    const profileId = assetProfileDraft.starterProfileId || assetProfileDraft.profileId;
    if (!profileId) {
      return;
    }

    setAssetProfileDraft((current) => ({ ...current, busyAction: "lineage", message: null, details: [], tone: "default" }));
    try {
      const lineage = await getSecurityAssetProfileLineage(profileId);
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        lineage,
        rollbackTargetVersion: lineage.versions[0]?.version ?? current.rollbackTargetVersion,
        message: `Loaded ${lineage.versions.length} version${lineage.versions.length === 1 ? "" : "s"} for ${lineage.profileId}.`,
        details: lineage.auditEvents.slice(-3).map((event) => `Audit ${event.auditId}: ${event.eventType}`),
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Asset profile lineage load failed.");
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  const rollbackAssetProfile = async () => {
    const profileId = assetProfileDraft.starterProfileId || assetProfileDraft.profileId;
    if (!profileId || assetProfileDraft.rollbackTargetVersion < 1) {
      setAssetProfileDraft((current) => ({
        ...current,
        message: "Choose a profile and target version before rollback.",
        details: [],
        tone: "warning"
      }));
      return;
    }

    setAssetProfileDraft((current) => ({ ...current, busyAction: "rollback", message: null, details: [], tone: "default" }));
    try {
      const result = await rollbackSecurityAssetProfile({
        profileId,
        targetVersion: assetProfileDraft.rollbackTargetVersion,
        effectiveFrom: todayDateOnly(),
        approvalReference: `settings:${profileId}:rollback:${assetProfileDraft.rollbackTargetVersion}`,
        requestedBy: session?.displayName ?? "settings-screen",
        rationale: assetProfileDraft.rationale.trim() || "Rollback asset profile to an approved prior version.",
        correlationId: `settings-asset-profile-rollback-${Date.now()}`
      });
      await onRefresh?.();
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        lineage: result.lineage,
        result,
        message: `Rolled back ${result.profile.name} into v${result.profile.version}.`,
        details: [
          `Audit ${result.auditEvent.auditId}`,
          `Correlation ${result.auditEvent.correlationId}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Asset profile rollback failed.");
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  const submitProfileBackedSecurity = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const selected = selectedProfileBackedSecurityProfile;
    if (!selected) {
      setProfileBackedSecurity((current) => ({
        ...current,
        message: "Choose an approved asset profile before creating a security.",
        details: vm.assetProfileGovernancePanel.createDisabledReason ? [vm.assetProfileGovernancePanel.createDisabledReason] : [],
        tone: "warning"
      }));
      return;
    }

    const missingFields = selected.fields
      .filter((field) => field.isRequired && !profileBackedSecurity.fieldValues[field.key]?.trim())
      .map((field) => field.label);
    const requiredIdentifierPreferences = selected.identifierPreferences
      .filter((preference) => preference.isRequiredForClose && preference.kind !== "InternalCode");
    // Includes the ProviderSymbol provider namespace: the write-side command validation rejects
    // that kind with a blank provider, so the form must catch it up front.
    const missingIdentifiers = missingRequiredIdentifierDetails(requiredIdentifierPreferences, profileBackedSecurity);
    if (!profileBackedSecurity.displayName.trim()
      || !profileBackedSecurity.internalCode.trim()
      || missingFields.length > 0
      || missingIdentifiers.length > 0) {
      setProfileBackedSecurity((current) => ({
        ...current,
        message: "Display name, internal code, required identifiers, and required profile fields are required.",
        details: [...missingIdentifiers, ...missingFields],
        tone: "warning"
      }));
      return;
    }

    const { payload: profileFields, invalidFields } = buildProfileFieldPayload(selected.fields, profileBackedSecurity.fieldValues);
    if (invalidFields.length > 0) {
      setProfileBackedSecurity((current) => ({
        ...current,
        message: "Correct the profile field values before creating the security.",
        details: invalidFields,
        tone: "warning"
      }));
      return;
    }

    setProfileBackedSecurity((current) => ({ ...current, busy: true, message: null, details: [], tone: "default" }));
    try {
      const securityId = createBrowserGuid();
      const effectiveFrom = new Date().toISOString();
      const result = await createSecurityMasterEntry({
        securityId,
        assetClass: "CustomAsset",
        commonTerms: {
          displayName: profileBackedSecurity.displayName.trim(),
          currency: profileBackedSecurity.currency.trim().toUpperCase() || "USD"
        },
        assetSpecificTerms: {
          schemaVersion: 3,
          category: selected.category,
          subType: selected.subType,
          customProfileId: selected.profileId,
          profileVersion: selected.version,
          profileFields,
          profileApproval: {
            approvedBy: selected.approvedBy,
            approvedAtUtc: selected.approvedAtUtc,
            // The write seam verifies this reference against the catalog's own recorded approval
            // reference when one exists, so the governed value must be copied verbatim; the
            // legacy fabricated form remains only for catalog versions predating the field
            // (where the seam has no fact to compare).
            approvalReference: selected.approvalReference ?? `profile:${selected.profileId}:v${selected.version}`
          },
          evidenceLinks: []
        },
        identifiers: [
          {
            kind: "InternalCode",
            value: profileBackedSecurity.internalCode.trim(),
            isPrimary: true,
            validFrom: effectiveFrom
          },
          ...requiredIdentifierPreferences.map((preference) => ({
            kind: preference.kind,
            value: profileBackedSecurity.identifierValues[preference.kind]!.trim(),
            isPrimary: false,
            validFrom: effectiveFrom,
            ...(preference.kind === "ProviderSymbol"
              ? { provider: profileBackedSecurity.identifierProviders[preference.kind]!.trim() }
              : {})
          }))
        ],
        effectiveFrom,
        sourceSystem: "Meridian.Settings.AssetProfiles",
        updatedBy: session?.displayName ?? "settings-screen",
        sourceRecordId: `asset-profile:${selected.profileId}:v${selected.version}`,
        reason: profileBackedSecurity.rationale.trim()
      });
      await onRefresh?.();
      setProfileBackedSecurity((current) => ({
        ...current,
        busy: false,
        message: `Security created for ${result.displayName}.`,
        details: [
          `Security ${result.securityId}`,
          `Profile ${selected.profileId} v${selected.version}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Profile-backed security creation failed.");
      setProfileBackedSecurity((current) => ({
        ...current,
        busy: false,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

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
      <section className="grid gap-4 xl:grid-cols-[0.95fr_1.05fr]">
        <Card
          id="profile-authentication"
          role="region"
          aria-label={vm.profileAuthenticationPanel.regionLabel}
          className={cn("panel-surface scroll-mt-6 border", diagnosticToneClass[vm.profileAuthenticationPanel.statusTone])}
        >
          <CardHeader>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <div className="eyebrow-label eyebrow-label--sentence">Profile and authentication</div>
                <CardTitle className="mt-2 flex items-center gap-2">
                  <User className="h-5 w-5 text-primary" />
                  {vm.profileAuthenticationPanel.title}
                </CardTitle>
                <CardDescription className="mt-2">{vm.profileAuthenticationPanel.summary}</CardDescription>
              </div>
              <SeverityBadge
                status={badgeVariantToOperatorSeverity(vm.profileAuthenticationPanel.statusTone)}
                label={vm.profileAuthenticationPanel.statusLabel}
              />
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-4 lg:grid-cols-[minmax(0,0.78fr)_minmax(0,1fr)]">
              <div className="rounded-md border border-border/70 bg-background/35 px-4 py-4">
                <div className="flex items-start gap-3">
                  <div
                    aria-hidden="true"
                    className="grid h-12 w-12 shrink-0 place-items-center rounded-md border border-primary/35 font-mono text-sm font-semibold text-primary"
                  >
                    {vm.profileAuthenticationPanel.avatarInitials}
                  </div>
                  <div className="min-w-0">
                    <div className="break-words text-sm font-semibold text-foreground">
                      {vm.profileAuthenticationPanel.operatorName}
                    </div>
                    <div className="mt-1 break-words text-xs text-muted-foreground">
                      {vm.profileAuthenticationPanel.roleLabel}
                    </div>
                    <div className="mt-3 flex flex-wrap gap-2">
                      <SettingsChip label="Mode" value={vm.profileAuthenticationPanel.environmentLabel} />
                      <SettingsChip label="Workspace" value={vm.profileAuthenticationPanel.workspaceLabel} />
                    </div>
                  </div>
                </div>
                <dl className="mt-4 grid gap-2">
                  <SettingsFieldRow label="Command trail" value={vm.profileAuthenticationPanel.commandCountLabel} tone="muted" />
                  <SettingsFieldRow
                    label="Authority"
                    value={vm.profileAuthenticationPanel.authorityLabel}
                    tone={vm.profileAuthenticationPanel.statusTone === "danger" ? "danger" : vm.profileAuthenticationPanel.statusTone === "warning" ? "warning" : "default"}
                  />
                </dl>
                <p className="mt-3 text-xs leading-5 text-muted-foreground">
                  {vm.profileAuthenticationPanel.authorityDetail}
                </p>
              </div>

              <div className="grid gap-3">
                <dl className="grid gap-2" aria-label="Profile authentication facts">
                  {vm.profileAuthenticationPanel.facts.map((fact) => (
                    <SettingsFieldRow key={fact.id} label={fact.label} value={fact.value} tone={fact.tone} />
                  ))}
                </dl>
                {vm.profileAuthenticationPanel.notice ? (
                  <StatusBanner
                    role={vm.profileAuthenticationPanel.notice.role}
                    tone={settingsBannerTone(vm.profileAuthenticationPanel.notice.tone)}
                    title={vm.profileAuthenticationPanel.notice.title}
                    detail={vm.profileAuthenticationPanel.notice.detail}
                  />
                ) : null}
              </div>
            </div>

            <div className="grid gap-2">
              <h3 className={cn("text-xs font-semibold text-muted-foreground", usesTrackedCaps(vm.profileAuthenticationPanel.stepsTitle) && "uppercase")}>
                {vm.profileAuthenticationPanel.stepsTitle}
              </h3>
              <div role="list" aria-label={vm.profileAuthenticationPanel.stepsAriaLabel} className="grid gap-2">
                {vm.profileAuthenticationPanel.steps.map((step) => (
                  <ProfileAuthenticationStepRow key={step.id} step={step} />
                ))}
              </div>
            </div>
          </CardContent>
        </Card>

        <Card className={cn("panel-surface border", systemToneClass[vm.systemTone])}>
          <CardHeader>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <div className="eyebrow-label">System posture</div>
                <CardTitle className="mt-2 flex items-center gap-2">
                  <MonitorCheck className="h-5 w-5 text-primary" />
                  {vm.systemTitle}
                </CardTitle>
                <CardDescription className="mt-2">{vm.systemSummary}</CardDescription>
              </div>
              <SeverityBadge
                status={badgeVariantToOperatorSeverity(vm.systemTone)}
                label={overview?.systemStatus ?? "Unavailable"}
              />
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap gap-2">
              <SettingsChip label="Providers healthy" value={providerHealth ? `${providerHealth.healthy} / ${providerHealth.total}` : "—"} />
              <SettingsChip label="Runs" value={overview ? String(overview.activeRuns) : "—"} />
              <SettingsChip label="Positions" value={overview ? String(overview.openPositions) : "—"} />
              <SettingsChip label="Storage" value={overview?.storageHealth ?? "—"} />
            </div>
            {vm.hasOverview ? (
              <dl className="grid gap-2">
                {vm.systemItems.map((item) => (
                  <SettingsFieldRow key={item.label} label={item.label} value={item.value} tone={item.tone} />
                ))}
              </dl>
            ) : (
              <p className="rounded-md border border-border/70 bg-secondary/25 px-4 py-4 text-center text-sm text-muted-foreground">
                System overview is unavailable. Check the API connection.
              </p>
            )}
          </CardContent>
        </Card>
      </section>
      ) : null}

      {showPreferencesSection ? (
      <Card
        id="settings-appearance"
        role="region"
        aria-label="Appearance preferences"
        className="panel-surface scroll-mt-6 border border-border/70"
      >
        <CardHeader>
          <div className="eyebrow-label">Operator preferences</div>
          <CardTitle className="mt-2 flex items-center gap-2 text-base">
            <MonitorCheck className="h-4 w-4 text-primary" />
            Appearance
          </CardTitle>
          <CardDescription className="mt-2">
            Choose the workstation theme and screen density for this browser.
          </CardDescription>
        </CardHeader>
        <CardContent className="grid gap-4 md:grid-cols-2">
          <div className="grid gap-2">
            <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Theme</div>
            <ThemeToggle fullWidth />
          </div>
          <div className="grid gap-2">
            <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Density</div>
            <DensityToggle persist="meridian.workstation.density.v1" fullWidth />
          </div>
        </CardContent>
      </Card>
      ) : null}

      {showAccessSection ? (
      <Card
        id="scoped-access-control"
        role="region"
        aria-label="Scoped access assignment console"
        className="panel-surface scroll-mt-6 border border-border/70"
      >
        <CardHeader>
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div>
              <div className="eyebrow-label">Scoped authority</div>
              <CardTitle className="mt-2 flex items-center gap-2 text-base">
                <ShieldCheck className="h-4 w-4 text-primary" />
                Scoped access assignments
              </CardTitle>
              <CardDescription className="mt-2">
                Grant and revoke principal authority by role, permission, scope, version, rationale, and audit event.
              </CardDescription>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <SettingsChip label="Active" value={String(scopedAccessActiveCount)} />
              <SettingsChip label="Revoked" value={String(scopedAccessRevokedCount)} />
              <Badge variant={scopedAccess.loading ? "warning" : scopedAccess.tone === "danger" ? "danger" : "outline"}>
                {scopedAccess.loading ? "Loading" : scopedAccess.includeRevoked ? "Revoked included" : "Active only"}
              </Badge>
            </div>
          </div>
        </CardHeader>
        <CardContent className="grid gap-4">
          <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
            <Checkbox
              checked={scopedAccess.includeRevoked}
              onCheckedChange={(checked) => setScopedAccess((current) => ({
                  ...current,
                  includeRevoked: checked,
                  loading: true,
                  message: null,
                  details: [],
                  tone: "default"
                }))}
              className="w-fit rounded-md border border-border/60 bg-secondary/20 px-3 py-2 text-xs"
              label="Include revoked assignments"
            />
            <div className="text-xs leading-5 text-muted-foreground">
              Revocations submit the assignment version currently shown in this console.
            </div>
          </div>

          {scopedAccessAssignments.length > 0 ? (
            <div role="list" aria-label="Scoped access assignments" className="grid gap-2">
              {scopedAccessAssignments.map((assignment) => (
                <div
                  key={assignment.assignmentId}
                  role="listitem"
                  className={cn(
                    "grid gap-3 rounded-md border px-3 py-3",
                    assignment.revokedAtUtc ? diagnosticToneClass.warning : diagnosticToneClass.default
                  )}
                >
                  <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2">
                        <h3 className="break-words text-sm font-semibold text-foreground">
                          {assignment.principalId}
                        </h3>
                        <Badge variant={assignment.revokedAtUtc ? "warning" : "success"}>
                          {assignment.revokedAtUtc ? "Revoked" : "Active"}
                        </Badge>
                      </div>
                      <p className="mt-1 text-xs leading-5 text-muted-foreground">
                        {assignment.principalKind} · {formatScopedAccessScope(assignment)} · {formatScopedAccessWindow(assignment)}
                      </p>
                    </div>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() => setMutationConfirmation({
                        id: `revoke-access-${assignment.assignmentId}`,
                        title: "Confirm scoped access revocation",
                        description: `Revoke ${assignment.role} authority for ${assignment.principalId}. Existing audit evidence remains retained and the current assignment version will be checked by the server.`,
                        confirmLabel: "Confirm revoke",
                        confirmAriaLabel: `Confirm scoped access revocation for ${assignment.principalId}`,
                        destructive: true,
                        run: () => revokeScopedAccess(assignment)
                      })}
                      disabled={Boolean(assignment.revokedAtUtc) || scopedAccess.revokeBusyId !== null}
                      busy={scopedAccess.revokeBusyId === assignment.assignmentId}
                      busyLabel="Revoking access"
                      aria-label={`Revoke scoped access for ${assignment.principalId}`}
                    >
                      <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                      Revoke
                    </Button>
                  </div>
                  <dl className="grid gap-2 sm:grid-cols-2 xl:grid-cols-6">
                    <SettingsFieldRow label="Role" value={assignment.roleProfileName ?? humanizeSettingsIdentifier(assignment.role)} tone="default" />
                    <SettingsFieldRow label="Version" value={String(assignment.version)} tone="muted" />
                    <SettingsFieldRow label="Audit" value={assignment.lastAuditId ?? "Pending"} tone={assignment.lastAuditId ? "default" : "warning"} />
                    <SettingsFieldRow label="Correlation" value={assignment.correlationId} tone="muted" />
                    <SettingsFieldRow
                      label="Approval limit"
                      value={formatScopedAccessApprovalLimit(assignment)}
                      tone={assignment.approvalLimitAmount === null || assignment.approvalLimitAmount === undefined ? "muted" : "default"}
                    />
                    <SettingsFieldRow
                      label="SoD rule"
                      value={assignment.segregationOfDutiesRule || "Not specified"}
                      tone={assignment.segregationOfDutiesRule ? "default" : "muted"}
                    />
                  </dl>
                  <div className="flex flex-wrap gap-2" aria-label={`Permissions for ${assignment.principalId}`}>
                    {assignment.permissionNames.map((permission) => (
                      <Badge key={`${assignment.assignmentId}-${permission}`} variant="outline">
                        {humanizeSettingsIdentifier(permission)}
                      </Badge>
                    ))}
                  </div>
                  {assignment.revocationReason ? (
                    <p className="text-xs leading-5 text-muted-foreground">
                      Revoked by {assignment.revokedBy ?? "unknown"}: {assignment.revocationReason}
                    </p>
                  ) : null}
                </div>
              ))}
            </div>
          ) : (
            <p className="rounded-md border border-border/70 bg-secondary/25 px-4 py-4 text-center text-sm text-muted-foreground">
              No scoped access assignments are loaded for the selected filter.
            </p>
          )}

          <form
            className="grid gap-3 rounded-md border border-border/70 bg-background/35 px-3 py-3"
            onSubmit={submitScopedAccessAssignment}
            aria-label="Grant scoped access assignment"
          >
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div className="min-w-0">
                <h3 className="text-sm font-semibold text-foreground">Grant scoped access</h3>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  Bind a user or group to one scope with explicit permissions, effective dates, and a rationale.
                </p>
              </div>
              <Badge variant={roleProfileDraft.canSave ? "warning" : "outline"}>
                {roleProfileDraft.canSave ? "Catalog ready" : roleProfileDraft.statusLabel}
              </Badge>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(0,0.7fr)_minmax(0,0.8fr)_minmax(0,1fr)]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Principal ID
                <Input
                  value={scopedAccess.principalId}
                  onChange={(event) => setScopedAccess((current) => ({ ...current, principalId: event.target.value, message: null }))}
                  placeholder="fund-controller"
                  disabled={scopedAccess.busy}
                  aria-label="Scoped access principal id"
                />
              </label>
              <FilterSelect
                label="Principal kind"
                value={scopedAccess.principalKind}
                onChange={(value) => setScopedAccess((current) => ({
                  ...current,
                  principalKind: value as AccessPrincipalKind,
                  message: null
                }))}
                options={scopedAccessPrincipalKindOptions}
                disabled={scopedAccess.busy}
              />
              <FilterSelect
                label="Scope kind"
                value={scopedAccess.scopeKind}
                onChange={(value) => setScopedAccess((current) => ({
                  ...current,
                  scopeKind: value as AccessScopeKind,
                  scopeId: value === "Global" ? "" : current.scopeId,
                  message: null
                }))}
                options={scopedAccessScopeKindOptions}
                disabled={scopedAccess.busy}
              />
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Scope ID
                <Input
                  value={scopedAccess.scopeId}
                  onChange={(event) => setScopedAccess((current) => ({ ...current, scopeId: event.target.value, message: null }))}
                  placeholder={scopedAccess.scopeKind === "Global" ? "Global scope" : "fund-2026-direct-lending"}
                  disabled={scopedAccess.busy || scopedAccess.scopeKind === "Global"}
                  aria-label="Scoped access scope id"
                />
              </label>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,0.8fr)_minmax(0,0.8fr)_minmax(0,0.65fr)_minmax(0,0.65fr)]">
              <FilterSelect
                label="Role"
                value={scopedAccess.role}
                onChange={selectScopedAccessRole}
                options={scopedAccessRoleOptions}
                disabled={scopedAccess.busy || scopedAccessRoleOptions.length === 0}
              />
              <FilterSelect
                label="Role profile"
                value={scopedAccess.roleProfileName}
                onChange={(value) => setScopedAccess((current) => ({
                  ...current,
                  roleProfileName: value,
                  message: null
                }))}
                options={scopedAccessRoleProfileOptions}
                disabled={scopedAccess.busy || scopedAccessRoleProfileOptions.length === 0}
              />
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Effective from
                <Input
                  type="date"
                  value={scopedAccess.effectiveFrom}
                  onChange={(event) => setScopedAccess((current) => ({ ...current, effectiveFrom: event.target.value, message: null }))}
                  disabled={scopedAccess.busy}
                  aria-label="Scoped access effective from"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Effective to
                <Input
                  type="date"
                  value={scopedAccess.effectiveTo}
                  onChange={(event) => setScopedAccess((current) => ({ ...current, effectiveTo: event.target.value, message: null }))}
                  disabled={scopedAccess.busy}
                  aria-label="Scoped access effective to"
                />
              </label>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,0.55fr)_minmax(0,0.35fr)_minmax(0,1.1fr)]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Approval limit
                <Input
                  type="number"
                  min="0"
                  step="0.01"
                  value={scopedAccess.approvalLimitAmount}
                  onChange={(event) => setScopedAccess((current) => ({ ...current, approvalLimitAmount: event.target.value, message: null }))}
                  placeholder="100000"
                  disabled={scopedAccess.busy}
                  aria-label="Scoped access approval limit amount"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Currency
                <Input
                  value={scopedAccess.approvalLimitCurrency}
                  onChange={(event) => setScopedAccess((current) => ({ ...current, approvalLimitCurrency: event.target.value.toUpperCase(), message: null }))}
                  placeholder="USD"
                  disabled={scopedAccess.busy}
                  maxLength={3}
                  aria-label="Scoped access approval limit currency"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Segregation rule
                <Input
                  value={scopedAccess.segregationOfDutiesRule}
                  onChange={(event) => setScopedAccess((current) => ({ ...current, segregationOfDutiesRule: event.target.value, message: null }))}
                  placeholder="Requester cannot approve own payment request"
                  disabled={scopedAccess.busy}
                  aria-label="Scoped access segregation of duties rule"
                />
              </label>
            </div>
            <fieldset
              className="grid gap-2 rounded-md border border-border/60 bg-secondary/15 px-3 py-3"
              disabled={scopedAccess.busy || roleProfileDraft.permissionOptions.length === 0}
            >
              <legend className="px-1 text-xs font-semibold text-foreground">Scoped permissions</legend>
              <div className="grid max-h-48 gap-2 overflow-auto pr-1 sm:grid-cols-2 xl:grid-cols-3">
                {roleProfileDraft.permissionOptions.map((permission) => (
                  <Checkbox
                    key={`scoped-access-${permission.value}`}
                    checked={scopedAccess.permissionNames.includes(permission.value)}
                    onCheckedChange={(checked) => toggleScopedAccessPermission(permission.value, checked)}
                    className="min-w-0 rounded-sm border border-border/60 bg-background/40 px-2 py-2 text-xs"
                    label={
                      <span className="min-w-0">
                      <span className="block font-medium text-foreground">{permission.label}</span>
                      <span className="block break-words text-[11px] leading-4">{permission.group}</span>
                    </span>
                    }
                  />
                ))}
              </div>
            </fieldset>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_auto]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Rationale
                <Input
                  value={scopedAccess.rationale}
                  onChange={(event) => setScopedAccess((current) => ({ ...current, rationale: event.target.value, message: null }))}
                  placeholder="Reason for the scoped authority grant"
                  disabled={scopedAccess.busy}
                  aria-label="Scoped access rationale"
                />
              </label>
              <div className="flex items-end">
                <Button
                  type="submit"
                  size="sm"
                  disabled={scopedAccess.busy || scopedAccessRoleOptions.length === 0 || roleProfileDraft.permissionOptions.length === 0}
                  busy={scopedAccess.busy}
                  busyLabel="Granting access"
                  disabledReason={scopedAccessRoleOptions.length === 0 ? "Role catalog data has not loaded." : null}
                >
                  <Save className="h-3.5 w-3.5" aria-hidden="true" />
                  Grant access
                </Button>
              </div>
            </div>
            {scopedAccess.message ? (
              <StatusBanner
                role={scopedAccess.tone === "danger" ? "alert" : "status"}
                tone={settingsBannerTone(scopedAccess.tone)}
                title={scopedAccess.message}
                detail={scopedAccess.details.length > 0 ? (
                  <ul className="list-disc space-y-1 pl-5">
                    {scopedAccess.details.map((detail) => <li key={detail}>{detail}</li>)}
                  </ul>
                ) : null}
              />
            ) : null}
          </form>
        </CardContent>
      </Card>
      ) : null}

      {showOperationsSection ? (
      <Card id="fund-operations-control-center" className="panel-surface scroll-mt-6 border border-border/70">
        <CardHeader>
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div>
              <div className="eyebrow-label">Configurable operations</div>
              <CardTitle className="mt-2 flex items-center gap-2 text-base">
                <ShieldCheck className="h-4 w-4 text-primary" />
                {vm.operationsControlCenter.title}
              </CardTitle>
              <CardDescription className="mt-2">{vm.operationsControlCenter.summary}</CardDescription>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <SettingsChip label="Loaded" value={vm.operationsControlCenter.loadedCountLabel} />
              <SettingsChip label="Review" value={vm.operationsControlCenter.reviewCountLabel} />
              <SeverityBadge
                status={badgeVariantToOperatorSeverity(vm.operationsControlCenter.statusVariant)}
                label={vm.operationsControlCenter.statusLabel}
              />
            </div>
          </div>
        </CardHeader>
        <CardContent className="grid gap-4">
          <div className="grid gap-3 xl:grid-cols-4" role="list" aria-label={vm.operationsControlCenter.listLabel}>
            {vm.operationsControlCenter.cards.map((card) => (
              <div
                key={card.id}
                role="listitem"
                className={cn(
                  "grid gap-3 rounded-md border px-3 py-3",
                  diagnosticToneClass[capabilityTone(card.statusVariant === "default" ? "outline" : card.statusVariant)]
                )}
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <h3 className="text-sm font-semibold text-foreground">{card.title}</h3>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{card.description}</p>
                  </div>
                  <SeverityBadge
                    status={badgeVariantToOperatorSeverity(card.statusVariant)}
                    label={card.statusLabel}
                    className="shrink-0"
                  />
                </div>
                <dl className="grid gap-2 sm:grid-cols-2 xl:grid-cols-1 2xl:grid-cols-2">
                  {card.metrics.map((metric) => (
                    <SettingsFieldRow key={`${card.id}-${metric.label}`} label={metric.label} value={metric.value} tone={metric.tone} />
                  ))}
                </dl>
                <p className="text-xs leading-5 text-foreground/75">{card.detail}</p>
                <div className="mt-auto flex flex-wrap gap-2">
                  {!card.routeHref.startsWith("/api/") && (
                    <Button asChild variant="outline" size="sm">
                      <Link to={card.routeHref} aria-label={card.routeAriaLabel}>
                        {card.routeLabel}
                        <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                      </Link>
                    </Button>
                  )}
                </div>
                <TechnicalDetails
                  label="Service details"
                  description="Technical routes are available for troubleshooting and support."
                >
                  <a
                    href={card.endpointHref}
                    target="_blank"
                    rel="noreferrer"
                    className="inline-flex max-w-full items-center gap-2 break-all font-mono text-[11px] text-primary hover:underline focus:outline-hidden focus:ring-2 focus:ring-primary/40"
                    aria-label={`Open service details for ${card.title}`}
                  >
                    {card.endpointHref}
                    <ExternalLink className="h-3 w-3 shrink-0" aria-hidden="true" />
                  </a>
                </TechnicalDetails>
              </div>
            ))}
          </div>
          <form
            className="grid gap-3 rounded-md border border-border/70 bg-background/35 px-3 py-3"
            onSubmit={submitLedgerMappingAssignment}
            aria-label="Assign ledger mapping"
          >
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div className="min-w-0">
                <h3 className="text-sm font-semibold text-foreground">Assign ledger mapping</h3>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  Save an account-level ledger group assignment with actor, rationale, and correlation evidence.
                </p>
              </div>
              <Badge variant={ledgerMappingDraft.canAssign ? "warning" : "outline"}>
                {ledgerMappingDraft.statusLabel}
              </Badge>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_minmax(0,1.2fr)_auto]">
              <FilterSelect
                label="Account"
                value={ledgerMappingAssignment.accountId}
                onChange={(value) => setLedgerMappingAssignment((current) => ({ ...current, accountId: value, message: null }))}
                options={ledgerMappingDraft.accountOptions}
                disabled={!ledgerMappingDraft.canAssign || ledgerMappingAssignment.busy}
              />
              <FilterSelect
                label="Ledger group"
                value={ledgerMappingAssignment.ledgerGroupId}
                onChange={(value) => setLedgerMappingAssignment((current) => ({ ...current, ledgerGroupId: value, message: null }))}
                options={ledgerMappingDraft.ledgerGroupOptions}
                disabled={!ledgerMappingDraft.canAssign || ledgerMappingAssignment.busy}
              />
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Rationale
                <Input
                  value={ledgerMappingAssignment.rationale}
                  onChange={(event) => setLedgerMappingAssignment((current) => ({ ...current, rationale: event.target.value, message: null }))}
                  placeholder="Reason for the ledger mapping change"
                  disabled={!ledgerMappingDraft.canAssign || ledgerMappingAssignment.busy}
                  aria-label="Ledger mapping rationale"
                />
              </label>
              <div className="flex items-end">
                <Button
                  type="submit"
                  size="sm"
                  disabled={!ledgerMappingDraft.canAssign || ledgerMappingAssignment.busy}
                  busy={ledgerMappingAssignment.busy}
                  busyLabel="Saving mapping"
                  disabledReason={ledgerMappingDraft.disabledReason}
                >
                  <Save className="h-3.5 w-3.5" aria-hidden="true" />
                  Save mapping
                </Button>
              </div>
            </div>
            {ledgerMappingAssignment.message ? (
              <StatusBanner
                role={ledgerMappingAssignment.tone === "danger" ? "alert" : "status"}
                tone={settingsBannerTone(ledgerMappingAssignment.tone)}
                title={ledgerMappingAssignment.message}
                detail={ledgerMappingAssignment.details.length > 0 ? (
                  <ul className="list-disc space-y-1 pl-5">
                    {ledgerMappingAssignment.details.map((detail) => <li key={detail}>{detail}</li>)}
                  </ul>
                ) : null}
              />
            ) : null}
          </form>
          <form
            className="grid gap-3 rounded-md border border-border/70 bg-background/35 px-3 py-3"
            onSubmit={submitRolePermissionProfile}
            aria-label="Create role profile"
          >
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div className="min-w-0">
                <h3 className="text-sm font-semibold text-foreground">Create role profile</h3>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  Save a reusable authority profile with base role, explicit grants, actor, rationale, and audit evidence.
                </p>
              </div>
              <Badge variant={roleProfileDraft.canSave ? "warning" : "outline"}>
                {roleProfileDraft.statusLabel}
              </Badge>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,0.9fr)_minmax(0,0.9fr)_minmax(0,0.75fr)]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Profile name
                <Input
                  value={rolePermissionProfile.profileName}
                  onChange={(event) => setRolePermissionProfile((current) => ({
                    ...current,
                    profileName: event.target.value,
                    displayName: !current.displayName || current.displayName === current.profileName
                      ? event.target.value
                      : current.displayName,
                    message: null
                  }))}
                  placeholder="Month-end Close Reviewer"
                  disabled={!roleProfileDraft.canSave || rolePermissionProfile.busy}
                  aria-label="Role profile name"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Display name
                <Input
                  value={rolePermissionProfile.displayName}
                  onChange={(event) => setRolePermissionProfile((current) => ({
                    ...current,
                    displayName: event.target.value,
                    message: null
                  }))}
                  placeholder="Close Reviewer"
                  disabled={!roleProfileDraft.canSave || rolePermissionProfile.busy}
                  aria-label="Role profile display name"
                />
              </label>
              <FilterSelect
                label="Base role"
                value={rolePermissionProfile.baseRole}
                onChange={(value) => setRolePermissionProfile((current) => {
                  const baseRole = rolePermissionCatalog?.roles.find((role) => role.role === value);
                  return {
                    ...current,
                    baseRole: value,
                    permissionNames: baseRole?.permissions ?? current.permissionNames,
                    message: null
                  };
                })}
                options={roleProfileDraft.baseRoleOptions}
                disabled={!roleProfileDraft.canSave || rolePermissionProfile.busy}
              />
            </div>
            <label className="grid gap-1 text-xs font-medium text-muted-foreground">
              Description
              <Input
                value={rolePermissionProfile.description}
                onChange={(event) => setRolePermissionProfile((current) => ({
                  ...current,
                  description: event.target.value,
                  message: null
                }))}
                placeholder="Scope this profile to close review and fund operations evidence."
                disabled={!roleProfileDraft.canSave || rolePermissionProfile.busy}
                aria-label="Role profile description"
              />
            </label>
            <fieldset
              className="grid gap-2 rounded-md border border-border/60 bg-secondary/15 px-3 py-3"
              disabled={!roleProfileDraft.canSave || rolePermissionProfile.busy}
            >
              <legend className="px-1 text-xs font-semibold text-foreground">Permissions</legend>
              <div className="grid max-h-56 gap-2 overflow-auto pr-1 sm:grid-cols-2 xl:grid-cols-3">
                {roleProfileDraft.permissionOptions.map((permission) => (
                  <Checkbox
                    key={permission.value}
                    checked={rolePermissionProfile.permissionNames.includes(permission.value)}
                    onCheckedChange={(checked) => toggleRoleProfilePermission(permission.value, checked)}
                    className="min-w-0 rounded-sm border border-border/60 bg-background/40 px-2 py-2 text-xs"
                    label={
                      <span className="min-w-0">
                      <span className="block font-medium text-foreground">{permission.label}</span>
                      <span className="block break-words text-[11px] leading-4">{permission.group}</span>
                    </span>
                    }
                  />
                ))}
              </div>
            </fieldset>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_auto]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Rationale
                <Input
                  value={rolePermissionProfile.rationale}
                  onChange={(event) => setRolePermissionProfile((current) => ({ ...current, rationale: event.target.value, message: null }))}
                  placeholder="Reason for the authority-profile change"
                  disabled={!roleProfileDraft.canSave || rolePermissionProfile.busy}
                  aria-label="Role profile rationale"
                />
              </label>
              <div className="flex items-end">
                <Button
                  type="submit"
                  size="sm"
                  disabled={!roleProfileDraft.canSave || rolePermissionProfile.busy}
                  busy={rolePermissionProfile.busy}
                  busyLabel="Saving role profile"
                  disabledReason={roleProfileDraft.disabledReason}
                >
                  <Save className="h-3.5 w-3.5" aria-hidden="true" />
                  Save profile
                </Button>
              </div>
            </div>
            {rolePermissionProfile.message ? (
              <StatusBanner
                role={rolePermissionProfile.tone === "danger" ? "alert" : "status"}
                tone={settingsBannerTone(rolePermissionProfile.tone)}
                title={rolePermissionProfile.message}
                detail={rolePermissionProfile.details.length > 0 ? (
                  <ul className="list-disc space-y-1 pl-5">
                    {rolePermissionProfile.details.map((detail) => <li key={detail}>{detail}</li>)}
                  </ul>
                ) : null}
              />
            ) : null}
          </form>
          <form
            className="grid gap-3 rounded-md border border-border/70 bg-background/35 px-3 py-3"
            onSubmit={submitApprovalPolicyRule}
            aria-label="Configure approval policy rule"
          >
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div className="min-w-0">
                <h3 className="text-sm font-semibold text-foreground">Configure approval policy</h3>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  Update close approval rules with reviewer separation, required evidence, distinct approvals, and audit rationale.
                </p>
              </div>
              <Badge variant={approvalPolicyDraft.canSave ? "warning" : "outline"}>
                {approvalPolicyDraft.statusLabel}
              </Badge>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1.2fr)_minmax(0,0.8fr)_minmax(0,0.8fr)_minmax(0,0.55fr)]">
              <FilterSelect
                label="Policy rule"
                value={approvalPolicyRule.policyKey}
                onChange={selectApprovalPolicyRule}
                options={approvalPolicyDraft.policyOptions}
                disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
              />
              <FilterSelect
                label="Required permission"
                ariaLabel="Approval policy required permission"
                value={approvalPolicyRule.requiredPermission}
                onChange={(requiredPermission) => setApprovalPolicyRule((current) => ({ ...current, requiredPermission, message: null }))}
                options={buildApprovalPolicyPermissionOptions(
                  roleProfileDraft.permissionOptions,
                  approvalPolicyRule.requiredPermission
                )}
                disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
              />
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Reviewer role
                <Input
                  value={approvalPolicyRule.reviewerRole}
                  onChange={(event) => setApprovalPolicyRule((current) => ({ ...current, reviewerRole: event.target.value, message: null }))}
                  disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
                  aria-label="Approval policy reviewer role"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Approvals
                <Input
                  type="number"
                  min={1}
                  value={String(approvalPolicyRule.requiredDistinctApprovals)}
                  onChange={(event) => setApprovalPolicyRule((current) => ({
                    ...current,
                    requiredDistinctApprovals: Math.max(0, Number.parseInt(event.target.value, 10) || 0),
                    message: null
                  }))}
                  disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
                  aria-label="Approval policy required distinct approvals"
                />
              </label>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,0.8fr)_minmax(0,0.8fr)_minmax(0,1.2fr)]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Submitter role
                <Input
                  value={approvalPolicyRule.submitterRole}
                  onChange={(event) => setApprovalPolicyRule((current) => ({ ...current, submitterRole: event.target.value, message: null }))}
                  disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
                  aria-label="Approval policy submitter role"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Severity
                <Input
                  value={approvalPolicyRule.severity}
                  onChange={(event) => setApprovalPolicyRule((current) => ({ ...current, severity: event.target.value, message: null }))}
                  disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
                  aria-label="Approval policy severity"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Evidence requirement
                <Input
                  value={approvalPolicyRule.evidenceRequirement}
                  onChange={(event) => setApprovalPolicyRule((current) => ({ ...current, evidenceRequirement: event.target.value, message: null }))}
                  disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
                  aria-label="Approval policy evidence requirement"
                />
              </label>
            </div>
            <div className="flex flex-wrap gap-2">
              {([
                {
                  key: "requiresIndependentReviewer",
                  label: "Independent reviewer",
                  checked: approvalPolicyRule.requiresIndependentReviewer
                },
                {
                  key: "requiresReportPack",
                  label: "Report pack",
                  checked: approvalPolicyRule.requiresReportPack
                },
                {
                  key: "requiresChecklistControlApprovals",
                  label: "Checklist control approvals",
                  checked: approvalPolicyRule.requiresChecklistControlApprovals
                }
              ] satisfies Array<{
                key: "requiresIndependentReviewer" | "requiresReportPack" | "requiresChecklistControlApprovals";
                label: string;
                checked: boolean;
              }>).map((option) => (
                <Checkbox
                  key={option.key}
                  checked={option.checked}
                  onCheckedChange={(checked) => setApprovalPolicyRule((current) => ({
                      ...current,
                      [option.key]: checked,
                      message: null
                    }))}
                  className="rounded-md border border-border/60 bg-secondary/20 px-2 py-2 text-xs"
                  disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
                  label={option.label}
                />
              ))}
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_auto]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Rationale
                <Input
                  value={approvalPolicyRule.rationale}
                  onChange={(event) => setApprovalPolicyRule((current) => ({ ...current, rationale: event.target.value, message: null }))}
                  placeholder="Reason for the approval policy change"
                  disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
                  aria-label="Approval policy rationale"
                />
              </label>
              <div className="flex items-end">
                <Button
                  type="submit"
                  size="sm"
                  disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
                  busy={approvalPolicyRule.busy}
                  busyLabel="Saving approval policy"
                  disabledReason={approvalPolicyDraft.disabledReason}
                >
                  <Save className="h-3.5 w-3.5" aria-hidden="true" />
                  Save policy
                </Button>
              </div>
            </div>
            {approvalPolicyRule.message ? (
              <StatusBanner
                role={approvalPolicyRule.tone === "danger" ? "alert" : "status"}
                tone={settingsBannerTone(approvalPolicyRule.tone)}
                title={approvalPolicyRule.message}
                detail={approvalPolicyRule.details.length > 0 ? (
                  <ul className="list-disc space-y-1 pl-5">
                    {approvalPolicyRule.details.map((detail) => <li key={detail}>{detail}</li>)}
                  </ul>
                ) : null}
              />
            ) : null}
          </form>
          <form
            className="grid gap-3 rounded-md border border-border/70 bg-background/35 px-3 py-3"
            onSubmit={submitCloseCalendarItem}
            aria-label="Configure account close calendar"
          >
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div className="min-w-0">
                <h3 className="text-sm font-semibold text-foreground">Configure account close calendar</h3>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  Assign the next close task owner and due date with an audit rationale.
                </p>
              </div>
              <Badge variant={closeCalendarDraft.canSave ? "warning" : "outline"}>
                {closeCalendarDraft.statusLabel}
              </Badge>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1.2fr)_minmax(0,0.8fr)_minmax(0,0.65fr)_minmax(0,0.85fr)]">
              <FilterSelect
                label="Close workflow"
                value={closeCalendarItem.workflowId}
                onChange={selectCloseCalendarItem}
                options={closeCalendarDraft.workflowOptions}
                disabled={!closeCalendarDraft.canSave || closeCalendarItem.busy}
              />
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Task
                <Input
                  value={closeCalendarItem.taskId}
                  onChange={(event) => setCloseCalendarItem((current) => ({ ...current, taskId: event.target.value, message: null }))}
                  disabled={!closeCalendarDraft.canSave || closeCalendarItem.busy}
                  aria-label="Close calendar task"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Due date
                <Input
                  type="date"
                  value={closeCalendarItem.dueDate}
                  onChange={(event) => setCloseCalendarItem((current) => ({ ...current, dueDate: event.target.value, message: null }))}
                  disabled={!closeCalendarDraft.canSave || closeCalendarItem.busy}
                  aria-label="Close calendar due date"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Owner
                <Input
                  value={closeCalendarItem.owner}
                  onChange={(event) => setCloseCalendarItem((current) => ({ ...current, owner: event.target.value, message: null }))}
                  disabled={!closeCalendarDraft.canSave || closeCalendarItem.busy}
                  aria-label="Close calendar owner"
                />
              </label>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_auto]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Rationale
                <Input
                  value={closeCalendarItem.rationale}
                  onChange={(event) => setCloseCalendarItem((current) => ({ ...current, rationale: event.target.value, message: null }))}
                  placeholder="Reason for the calendar change"
                  disabled={!closeCalendarDraft.canSave || closeCalendarItem.busy}
                  aria-label="Close calendar rationale"
                />
              </label>
              <div className="flex items-end">
                <Button
                  type="submit"
                  size="sm"
                  disabled={!closeCalendarDraft.canSave || closeCalendarItem.busy}
                  busy={closeCalendarItem.busy}
                  busyLabel="Saving close calendar"
                  disabledReason={closeCalendarDraft.disabledReason}
                >
                  <Save className="h-3.5 w-3.5" aria-hidden="true" />
                  Save calendar
                </Button>
              </div>
            </div>
            {closeCalendarItem.message ? (
              <StatusBanner
                role={closeCalendarItem.tone === "danger" ? "alert" : "status"}
                tone={settingsBannerTone(closeCalendarItem.tone)}
                title={closeCalendarItem.message}
                detail={closeCalendarItem.details.length > 0 ? (
                  <ul className="list-disc space-y-1 pl-5">
                    {closeCalendarItem.details.map((detail) => <li key={detail}>{detail}</li>)}
                  </ul>
                ) : null}
              />
            ) : null}
          </form>
        </CardContent>
      </Card>
      ) : null}

      {showAssetProfileSection ? (
      <Card id="asset-profile-accounting" className="panel-surface scroll-mt-6 border border-border/70">
        <CardHeader>
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div>
              <div className="eyebrow-label">Security Master</div>
              <CardTitle className="mt-2 flex items-center gap-2 text-base">
                <GitBranch className="h-4 w-4 text-primary" />
                {vm.assetProfileGovernancePanel.title}
              </CardTitle>
              <CardDescription className="mt-2">{vm.assetProfileGovernancePanel.summary}</CardDescription>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <SettingsChip label="Approved" value={vm.assetProfileGovernancePanel.approvedCountLabel} />
              <SettingsChip label="Declared projected" value={vm.assetProfileGovernancePanel.projectedFieldCountLabel} />
              <SettingsChip label="Close IDs" value={vm.assetProfileGovernancePanel.closeIdentifierCountLabel} />
              <SeverityBadge
                status={badgeVariantToOperatorSeverity(vm.assetProfileGovernancePanel.statusVariant)}
                label={vm.assetProfileGovernancePanel.statusLabel}
              />
            </div>
          </div>
        </CardHeader>
        <CardContent className="grid gap-4">
          <div className="grid gap-3 xl:grid-cols-5" role="list" aria-label={vm.assetProfileGovernancePanel.listLabel}>
            {vm.assetProfileGovernancePanel.rows.map((row) => (
              <div
                key={`${row.profileId}-${row.versionLabel}`}
                role="listitem"
                className={cn("grid gap-3 rounded-md border px-3 py-3", diagnosticToneClass[capabilityTone(row.statusVariant)])}
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <h3 className="text-sm font-semibold text-foreground">{row.name}</h3>
                    <p className="mt-1 break-words font-mono text-[11px] text-muted-foreground">{row.profileId}</p>
                  </div>
                  <Badge variant={row.statusVariant}>{row.versionLabel}</Badge>
                </div>
                <dl className="grid gap-2">
                  <SettingsFieldRow label="Category" value={row.categoryLabel} tone="default" />
                  <SettingsFieldRow label="Fields" value={row.fieldCountLabel} tone="muted" />
                  <SettingsFieldRow label="Projection" value={row.projectedFieldLabel} tone="muted" />
                  <SettingsFieldRow label="Close ID" value={row.requiredCloseIdentifierLabel} tone="warning" />
                </dl>
                <p className="text-xs leading-5 text-foreground/75">{row.accountingImpactLabel}</p>
              </div>
            ))}
          </div>

          <form
            className="grid gap-3 rounded-md border border-border/70 bg-background/35 px-3 py-3"
            onSubmit={submitAssetProfileDraft}
            aria-label="Draft asset profile"
          >
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div className="min-w-0">
                <h3 className="text-sm font-semibold text-foreground">Draft asset profile</h3>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  Copy approved no-code fields, close identifiers, lifecycle states, and accounting hints into a governed draft.
                </p>
              </div>
              <Badge variant={selectedDraftStarterProfile ? "warning" : "outline"}>
                {assetProfileDraft.busyAction ? "Working" : selectedDraftStarterProfile ? "Draft ready" : "Unavailable"}
              </Badge>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1.1fr)_minmax(0,0.9fr)_minmax(0,1fr)_minmax(0,0.8fr)]">
              <FilterSelect
                label="Starter"
                value={assetProfileDraft.starterProfileId}
                onChange={selectAssetProfileStarter}
                options={approvedAssetProfiles.map((profile) => ({ value: profile.profileId, label: profile.name }))}
                disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
              />
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Profile id
                <Input
                  value={assetProfileDraft.profileId}
                  onChange={(event) => setAssetProfileDraft((current) => ({ ...current, profileId: event.target.value, message: null }))}
                  disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
                  aria-label="Asset profile id"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Name
                <Input
                  value={assetProfileDraft.name}
                  onChange={(event) => setAssetProfileDraft((current) => ({ ...current, name: event.target.value, message: null }))}
                  disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
                  aria-label="Asset profile name"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Category
                <Input
                  value={assetProfileDraft.category}
                  onChange={(event) => setAssetProfileDraft((current) => ({ ...current, category: event.target.value, message: null }))}
                  disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
                  aria-label="Asset profile category"
                />
              </label>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,0.75fr)_minmax(0,1fr)_auto_auto_auto]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Subtype
                <Input
                  value={assetProfileDraft.subType}
                  onChange={(event) => setAssetProfileDraft((current) => ({ ...current, subType: event.target.value, message: null }))}
                  disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
                  aria-label="Asset profile subtype"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Rationale
                <Input
                  value={assetProfileDraft.rationale}
                  onChange={(event) => setAssetProfileDraft((current) => ({ ...current, rationale: event.target.value, message: null }))}
                  disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
                  aria-label="Asset profile rationale"
                />
              </label>
              <div className="flex items-end">
                <Button type="submit" size="sm" disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null} busy={assetProfileDraft.busyAction === "draft"} busyLabel="Saving draft">
                  <Save className="h-3.5 w-3.5" aria-hidden="true" />
                  Save draft
                </Button>
              </div>
              <div className="flex items-end">
                <Button type="button" variant="outline" size="sm" onClick={approveAssetProfileDraft} disabled={assetProfileDraft.busyAction !== null || !assetProfileDraft.result} busy={assetProfileDraft.busyAction === "approve"} busyLabel="Approving">
                  <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
                  Approve
                </Button>
              </div>
              <div className="flex items-end">
                <Button type="button" variant="outline" size="sm" onClick={loadAssetProfileLineage} disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null} busy={assetProfileDraft.busyAction === "lineage"} busyLabel="Loading lineage">
                  <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
                  Lineage
                </Button>
              </div>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,0.35fr)_auto_minmax(0,1fr)]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Rollback version
                <Input
                  type="number"
                  min={1}
                  value={String(assetProfileDraft.rollbackTargetVersion)}
                  onChange={(event) => setAssetProfileDraft((current) => ({
                    ...current,
                    rollbackTargetVersion: Math.max(1, Number.parseInt(event.target.value, 10) || 1),
                    message: null
                  }))}
                  disabled={assetProfileDraft.busyAction !== null}
                  aria-label="Asset profile rollback target version"
                />
              </label>
              <div className="flex items-end">
                <Button
                  type="button"
                  variant="destructive"
                  size="sm"
                  onClick={() => setMutationConfirmation({
                    id: "rollback-asset-profile",
                    title: "Confirm asset profile rollback",
                    description: `Rollback ${selectedDraftStarterProfile?.name ?? "the selected asset profile"} to version ${assetProfileDraft.rollbackTargetVersion}. This changes the active governed profile and retains a lineage record.`,
                    confirmLabel: "Confirm rollback",
                    confirmAriaLabel: `Confirm asset profile rollback to version ${assetProfileDraft.rollbackTargetVersion}`,
                    destructive: true,
                    run: rollbackAssetProfile
                  })}
                  disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
                  busy={assetProfileDraft.busyAction === "rollback"}
                  busyLabel="Rolling back"
                >
                  <GitBranch className="h-3.5 w-3.5" aria-hidden="true" />
                  Rollback
                </Button>
              </div>
              <div className="flex items-end text-xs text-muted-foreground">
                {assetProfileDraft.lineage ? `${assetProfileDraft.lineage.versions.length} lineage version${assetProfileDraft.lineage.versions.length === 1 ? "" : "s"}` : "Lineage not loaded"}
              </div>
            </div>
            {assetProfileDraft.message ? (
              <StatusBanner
                role={assetProfileDraft.tone === "danger" ? "alert" : "status"}
                tone={settingsBannerTone(assetProfileDraft.tone)}
                title={assetProfileDraft.message}
                detail={assetProfileDraft.details.length > 0 ? (
                  <ul className="list-disc space-y-1 pl-5">
                    {assetProfileDraft.details.map((detail) => <li key={detail}>{detail}</li>)}
                  </ul>
                ) : null}
              />
            ) : null}
          </form>

          <form
            className="grid gap-3 rounded-md border border-border/70 bg-background/35 px-3 py-3"
            onSubmit={submitProfileBackedSecurity}
            aria-label="Create profile-backed security"
          >
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div className="min-w-0">
                <h3 className="text-sm font-semibold text-foreground">Create profile-backed security</h3>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  Pin the Security Master record to the approved profile version used at creation.
                </p>
              </div>
              <Badge variant={selectedProfileBackedSecurityProfile ? "success" : "outline"}>
                {selectedProfileBackedSecurityProfile ? `v${selectedProfileBackedSecurityProfile.version}` : "No profile"}
              </Badge>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_minmax(0,0.75fr)_minmax(0,0.45fr)]">
              <FilterSelect
                label="Profile"
                value={profileBackedSecurity.profileId}
                onChange={selectProfileBackedSecurityProfile}
                options={approvedAssetProfiles.map((profile) => ({ value: profile.profileId, label: profile.name }))}
                disabled={!vm.assetProfileGovernancePanel.canCreateSecurity || profileBackedSecurity.busy}
              />
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Display name
                <Input
                  value={profileBackedSecurity.displayName}
                  onChange={(event) => setProfileBackedSecurity((current) => ({ ...current, displayName: event.target.value, message: null }))}
                  disabled={!selectedProfileBackedSecurityProfile || profileBackedSecurity.busy}
                  aria-label="Profile-backed security display name"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Internal code
                <Input
                  value={profileBackedSecurity.internalCode}
                  onChange={(event) => setProfileBackedSecurity((current) => ({ ...current, internalCode: event.target.value, message: null }))}
                  disabled={!selectedProfileBackedSecurityProfile || profileBackedSecurity.busy}
                  aria-label="Profile-backed security internal code"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Currency
                <Input
                  value={profileBackedSecurity.currency}
                  onChange={(event) => setProfileBackedSecurity((current) => ({ ...current, currency: event.target.value.toUpperCase(), message: null }))}
                  disabled={!selectedProfileBackedSecurityProfile || profileBackedSecurity.busy}
                  aria-label="Profile-backed security currency"
                />
              </label>
            </div>
            {additionalRequiredIdentifierPreferences.length > 0 ? (
              <div className="grid gap-3 lg:grid-cols-3">
                {additionalRequiredIdentifierPreferences.map((preference) => (
                  <label key={preference.kind} className="grid gap-1 text-xs font-medium text-muted-foreground">
                    {`${preference.kind} identifier`}
                    <Input
                      value={profileBackedSecurity.identifierValues[preference.kind] ?? ""}
                      onChange={(event) => setProfileBackedSecurity((current) => ({
                        ...current,
                        identifierValues: { ...current.identifierValues, [preference.kind]: event.target.value },
                        message: null
                      }))}
                      disabled={profileBackedSecurity.busy}
                      aria-label={`Profile-backed security ${preference.kind} identifier`}
                    />
                    {preference.kind === "ProviderSymbol" ? (
                      <Input
                        value={profileBackedSecurity.identifierProviders[preference.kind] ?? ""}
                        onChange={(event) => setProfileBackedSecurity((current) => ({
                          ...current,
                          identifierProviders: { ...current.identifierProviders, [preference.kind]: event.target.value },
                          message: null
                        }))}
                        placeholder="Provider namespace (e.g. bloomberg)"
                        disabled={profileBackedSecurity.busy}
                        aria-label={`Profile-backed security ${preference.kind} provider namespace`}
                      />
                    ) : null}
                  </label>
                ))}
              </div>
            ) : null}
            {selectedProfileBackedSecurityProfile ? (
              <div className="grid gap-3 lg:grid-cols-3">
                {selectedProfileBackedSecurityProfile.fields.map((field) => (
                  <AssetProfileFieldInput
                    key={field.key}
                    field={field}
                    value={profileBackedSecurity.fieldValues[field.key] ?? ""}
                    disabled={profileBackedSecurity.busy}
                    onChange={(value) => updateProfileBackedSecurityField(field.key, value)}
                  />
                ))}
              </div>
            ) : null}
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_auto]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Rationale
                <Input
                  value={profileBackedSecurity.rationale}
                  onChange={(event) => setProfileBackedSecurity((current) => ({ ...current, rationale: event.target.value, message: null }))}
                  disabled={!selectedProfileBackedSecurityProfile || profileBackedSecurity.busy}
                  aria-label="Profile-backed security rationale"
                />
              </label>
              <div className="flex items-end">
                <Button
                  type="submit"
                  size="sm"
                  disabled={!selectedProfileBackedSecurityProfile || profileBackedSecurity.busy}
                  busy={profileBackedSecurity.busy}
                  busyLabel="Creating security"
                  disabledReason={vm.assetProfileGovernancePanel.createDisabledReason ?? undefined}
                >
                  <Save className="h-3.5 w-3.5" aria-hidden="true" />
                  Create security
                </Button>
              </div>
            </div>
            {profileBackedSecurity.message ? (
              <StatusBanner
                role={profileBackedSecurity.tone === "danger" ? "alert" : "status"}
                tone={settingsBannerTone(profileBackedSecurity.tone)}
                title={profileBackedSecurity.message}
                detail={profileBackedSecurity.details.length > 0 ? (
                  <ul className="list-disc space-y-1 pl-5">
                    {profileBackedSecurity.details.map((detail) => <li key={detail}>{detail}</li>)}
                  </ul>
                ) : null}
              />
            ) : null}
          </form>
        </CardContent>
      </Card>
      ) : null}

      {showProviderSection ? (
      <Card id="provider-connection-center" className="panel-surface scroll-mt-6 border border-border/70">
        <CardHeader>
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div>
              <div className="eyebrow-label">Provider management</div>
              <CardTitle className="mt-2 flex items-center gap-2 text-base">
                <MonitorCheck className="h-4 w-4 text-primary" />
                {vm.providerConnectionCenter.title}
              </CardTitle>
              <CardDescription className="mt-2">
                {vm.providerConnectionCenter.description}
                {showProviderSetupControls
                  ? inlineProviderManagementEnabled
                    ? " Guided credential editing and verification are enabled for the selected provider."
                    : " Guided credential editing is disabled by capability flag."
                  : showProviderAdvancedControls
                    ? " Advanced integration and runtime evidence are isolated on this route."
                    : " Choose guided setup or advanced controls for a single provider."}
              </CardDescription>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              {onProviderRoutingRefresh ? (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => void onProviderRoutingRefresh()}
                  disabled={vm.providerConnectionCenter.refreshAction.disabled}
                  disabledReason={vm.providerConnectionCenter.refreshAction.disabledReason}
                  aria-label={vm.providerConnectionCenter.refreshAction.ariaLabel}
                >
                  <RefreshCcw
                    className={cn(
                      "h-3.5 w-3.5",
                      vm.providerConnectionCenter.refreshAction.busy && "animate-spin"
                    )}
                    aria-hidden="true"
                  />
                  {vm.providerConnectionCenter.refreshAction.label}
                </Button>
              ) : null}
              <SeverityBadge
                status={badgeVariantToOperatorSeverity(vm.providerConnectionCenter.statusVariant)}
                label={vm.providerConnectionCenter.statusLabel}
              />
            </div>
          </div>
        </CardHeader>
        <CardContent className="grid gap-4">
          {showProviderFilters ? (
          <section className="grid gap-3 rounded-md border border-border/70 bg-background/35 px-3 py-3">
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_11rem_11rem_11rem_9rem]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Search providers
                <Input
                  value={providerSearch}
                  onChange={(event) => setProviderSearch(event.target.value)}
                  placeholder="Search provider, workflow, or action"
                  leadingIcon={<Search className="h-4 w-4" />}
                  aria-label="Search providers in connection center"
                />
              </label>
              <FilterSelect
                label="Capability"
                value={providerCapabilityFilter}
                onChange={(value) => setProviderCapabilityFilter(value as typeof providerCapabilityFilter)}
                options={[
                  { value: "all", label: "All" },
                  { value: "brokerage", label: "Brokerage" },
                  { value: "data", label: "Data" },
                  { value: "accounting", label: "Accounting" }
                ]}
              />
              <FilterSelect
                label="Health"
                value={providerHealthFilter}
                onChange={(value) => setProviderHealthFilter(value as typeof providerHealthFilter)}
                options={[
                  { value: "all", label: "All" },
                  { value: "healthy", label: "Healthy" },
                  { value: "warning", label: "Warning" },
                  { value: "blocked", label: "Blocked" }
                ]}
              />
              <FilterSelect
                label="Verification"
                value={providerVerificationFilter}
                onChange={(value) => setProviderVerificationFilter(value as typeof providerVerificationFilter)}
                options={[
                  { value: "all", label: "All" },
                  { value: "verified", label: "Verified" },
                  { value: "unverified", label: "Unverified" }
                ]}
              />
              <FilterSelect
                label="Sort"
                value={providerSort}
                onChange={(value) => setProviderSort(value as typeof providerSort)}
                options={[
                  { value: "risk", label: "Risk" },
                  { value: "name", label: "Name" }
                ]}
              />
            </div>
            <p className="text-xs leading-5 text-muted-foreground">
              Providers are sorted by risk by default so blocked and warning rows needing attention appear first.
            </p>
          </section>
          ) : (
            <div className="flex flex-wrap items-center gap-2 rounded-md border border-border/70 bg-background/35 px-3 py-3 text-xs text-muted-foreground">
              <Link to={WORKSTATION_ROUTE_CATALOG.settingsProviders} className="font-semibold text-primary hover:underline focus:outline-hidden focus:ring-2 focus:ring-primary/40">
                Back to provider overview
              </Link>
              <span aria-hidden="true">·</span>
              <span>{activeProviderDisplayName ? `Focused provider: ${activeProviderDisplayName}` : "Choose a provider from the overview."}</span>
            </div>
          )}
          <div className={cn("grid gap-4", visibleProviderGroups.length > 1 && "xl:grid-cols-2")}>
          {visibleProviderGroups.map((group) => (
            <section key={group.id} className="grid gap-3" aria-label={group.label}>
              <div className="min-w-0">
                <h3 className="text-sm font-semibold text-foreground">{group.label}</h3>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">{group.summary}</p>
              </div>
              {group.rows.length > 0 ? (
                <div className="grid gap-2">
                  {group.rows.map((row) => (
                    <article
                      key={`${group.id}-${row.providerId}`}
                      id={row.rowAnchorId === "alpaca-provider-setup" ? undefined : row.rowAnchorId}
                      className="rounded-md border border-border/70 bg-background/35 px-3 py-3"
                    >
                      {showProviderSetupControls && inlineProviderManagementEnabled ? (
                        <OwnedProviderCredentialPanel row={row} session={session}
                          onRefresh={async () => { await onRefresh?.(); await onProviderRoutingRefresh?.(); }} />
                      ) : null}
                      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
                        <div className="min-w-0">
                          <div className="flex flex-wrap items-center gap-2">
                            <h4 className="text-sm font-semibold text-foreground">{row.displayName}</h4>
                            <Badge variant="outline">{row.capabilityLabel}</Badge>
                            <SeverityBadge
                              status={badgeVariantToOperatorSeverity(row.healthTone === "muted" ? "default" : row.healthTone)}
                              label={row.healthLabel}
                            />
                            {showProviderSetupControls && inlineProviderManagementEnabled ? (
                              <SeverityBadge
                                status={badgeVariantToOperatorSeverity(row.readinessTone)}
                                label={row.readinessLabel}
                              />
                            ) : null}
                          </div>
                          <p className="mt-2 text-xs leading-5 text-muted-foreground">{row.recommendedAction}</p>
                        </div>
                        <div className="flex flex-wrap items-center gap-2">
                          {activeTaskView !== "provider-setup" ? (
                            <Button asChild variant="outline" size="sm" className="shrink-0">
                              <Link to={settingsProviderSetupRoute(row.providerId)} aria-label={`Open guided setup for ${row.displayName}`}>
                                Guided setup
                                <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                              </Link>
                            </Button>
                          ) : null}
                          {activeTaskView !== "provider-advanced" ? (
                            <Button asChild variant="outline" size="sm" className="shrink-0">
                              <Link to={settingsProviderAdvancedRoute(row.providerId)} aria-label={`Open advanced controls for ${row.displayName}`}>
                                Advanced
                                <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                              </Link>
                            </Button>
                          ) : null}
                        </div>
                      </div>
                      <dl className="mt-3 grid gap-2 sm:grid-cols-2">
                        <SettingsFieldRow label="Credential" value={row.credentialLabel} tone={row.credentialTone} />
                        <SettingsFieldRow label="Verification" value={row.verificationLabel} tone={row.verificationTone} />
                        <SettingsFieldRow label="Source" value={row.sourceLabel} tone={row.sourceTone} />
                        <SettingsFieldRow label="Environment" value={row.environmentLabel} tone="muted" />
                        <SettingsFieldRow label="Masked key" value={row.maskedKeyPreviewLabel} tone="muted" />
                        <SettingsFieldRow label="Last good heartbeat" value={row.lastHeartbeatLabel} tone={row.lastHeartbeatTone} />
                        <SettingsFieldRow label="Failover" value={row.fallbackLabel} tone={row.fallbackLabel === "Fallback active" ? "warning" : "muted"} />
                        <SettingsFieldRow label="Routing bindings" value={row.routingBindingsLabel} tone="muted" />
                        <SettingsFieldRow label="Trust score" value={row.trustScoreLabel} tone={row.healthTone} />
                        <SettingsFieldRow label="Production gate" value={row.productionStateLabel} tone={row.productionStateTone} />
                        <SettingsFieldRow label="Readiness" value={row.readinessLabel} tone={row.readinessTone} />
                        <SettingsFieldRow label="Affected workflows" value={row.affectedWorkflowsLabel} tone="default" />
                      </dl>
                      {showProviderAdvancedControls ? (
                      <ProviderIntegrationRuntimePanel
                        row={row}
                        state={providerRuntimeState[providerRuntimeStateKey(row)] ?? emptyProviderRuntimeEvidenceState}
                        operatorName={session?.displayName ?? "settings-operator"}
                        openApiImportState={providerOpenApiImportState[providerRuntimeStateKey(row)] ?? createProviderOpenApiImportState(row)}
                        onOpenApiImportStateChange={(updater) => updateProviderOpenApiImportState(row, updater)}
                        onImportOpenApi={() => void submitProviderOpenApiImport(row)}
                        onLoad={() => void loadProviderRuntimeEvidence(row)}
                        onRunDueSync={() => setMutationConfirmation({
                          id: `provider-sync-${row.providerId}`,
                          title: `Run due sync for ${row.displayName}?`,
                          description: "Run only the due, server-planned read-only capabilities. Blocked plan items remain blocked by shared provider policy.",
                          confirmLabel: "Confirm due sync",
                          confirmAriaLabel: `Confirm due provider integration sync for ${row.displayName}`,
                          run: () => runProviderRuntimeDueSync(row)
                        })}
                        onCreateHandoff={() => setMutationConfirmation({
                          id: `provider-handoff-${row.providerId}`,
                          title: `Create reconciliation handoff for ${row.displayName}?`,
                          description: "Create retained handoff evidence only for staging rows the shared promotion-readiness service marked ready.",
                          confirmLabel: "Confirm handoff",
                          confirmAriaLabel: `Confirm reconciliation handoff for ${row.displayName}`,
                          run: () => createProviderRuntimeHandoff(row)
                        })}
                        onReplayQuarantine={() => setMutationConfirmation({
                          id: `provider-replay-${row.providerId}`,
                          title: `Replay quarantined records for ${row.displayName}?`,
                          description: "Replay the retained eligible quarantine batch using its original manifest version. Mapping updates are not applied; remediation requires an explicitly selected newer version. Existing decisions and source evidence remain retained.",
                          confirmLabel: "Confirm replay",
                          confirmAriaLabel: `Confirm quarantine replay for ${row.displayName}`,
                          destructive: true,
                          run: () => replayProviderRuntimeQuarantine(row)
                        })}
                        onResolveQuarantineRecord={(record, action) => setMutationConfirmation({
                          id: `provider-quarantine-${record.quarantineRecordId}-${action}`,
                          title: `Record quarantine decision for ${row.displayName}?`,
                          description: `Record ${action} for retained quarantine record ${record.quarantineRecordId}. The server remains authoritative for allowed transitions and replay eligibility.`,
                          confirmLabel: "Confirm decision",
                          confirmAriaLabel: `Confirm ${action} for quarantine record ${record.quarantineRecordId}`,
                          destructive: action === "IgnoreProviderRecord",
                          run: () => resolveProviderRuntimeQuarantineRecord(row, record, action)
                        })}
                      />
                      ) : null}

                    </article>
                  ))}
                </div>
              ) : (
                <p className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm text-muted-foreground">
                  {group.emptyLabel}
                </p>
              )}
            </section>
          ))}
          </div>
        </CardContent>
      </Card>
      ) : null}

      {showDataProviderModulesSection ? (
      <section id="data-provider-modules" className="scroll-mt-6">
        <ProviderSetupPanel />
      </section>
      ) : null}

      {showAlpacaSection ? (
      <Card
        id="alpaca-provider-setup"
        className={cn("panel-surface scroll-mt-6 border", diagnosticToneClass[vm.alpacaConnectionPanel.statusTone])}
      >
        <CardHeader>
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div>
              <div className="eyebrow-label">Brokerage connection</div>
              <CardTitle className="mt-2 flex items-center gap-2 text-base">
                <KeyRound className="h-4 w-4 text-primary" />
                Alpaca paper API keys
              </CardTitle>
              <CardDescription className="mt-2">{vm.alpacaConnectionPanel.statusDetail}</CardDescription>
            </div>
            <SeverityBadge
              status={badgeVariantToOperatorSeverity(vm.alpacaConnectionPanel.statusTone)}
              label={vm.alpacaConnectionPanel.stateLabel}
            />
          </div>
        </CardHeader>
        <CardContent className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(280px,0.8fr)]">
          <form className="grid gap-3" onSubmit={alpacaForm.connect} noValidate aria-describedby={alpacaForm.formPanelId}>
            <div className="grid gap-3 md:grid-cols-2">
              <AlpacaCredentialField
                field={alpacaForm.keyIdField}
                value={alpacaForm.keyId}
                onValueChange={alpacaForm.setKeyId}
                leadingIcon={<KeyRound className="h-4 w-4" />}
              />
              <AlpacaCredentialField
                field={alpacaForm.secretKeyField}
                value={alpacaForm.secretKey}
                onValueChange={alpacaForm.setSecretKey}
                leadingIcon={<ShieldCheck className="h-4 w-4" />}
              />
              <fieldset className="grid gap-1 text-xs font-medium text-muted-foreground md:col-span-2">
                <legend>{alpacaForm.environmentLegend}</legend>
                <div
                  className="grid gap-2 sm:grid-cols-2"
                  role="radiogroup"
                  aria-label={alpacaForm.environmentLegend}
                  aria-describedby={`${alpacaForm.fieldHelpIds.environment} ${alpacaForm.formPanelId}`}
                >
                  {alpacaForm.environmentOptions.map((option) => (
                    <label
                      key={option.id}
                      htmlFor={option.id}
                      className={cn(
                        "relative grid min-h-[4.75rem] cursor-pointer gap-1 rounded-md border px-3 py-2 transition-colors",
                        option.isSelected ? environmentOptionClass[option.tone].selected : environmentOptionClass[option.tone].idle,
                        option.disabled && "cursor-not-allowed opacity-60"
                      )}
                    >
                      <input
                        id={option.id}
                        type="radio"
                        name="alpaca-environment"
                        value={option.value}
                        checked={option.isSelected}
                        disabled={option.disabled}
                        onChange={() => alpacaForm.setEnvironment(option.value)}
                        aria-label={option.ariaLabel}
                        aria-describedby={joinDescribedByIds(
                          option.descriptionId,
                          alpacaForm.fieldHelpIds.environment,
                          option.disabledReasonId,
                          alpacaForm.formPanelId
                        )}
                        className="peer sr-only"
                      />
                      <span className="pointer-events-none absolute inset-0 rounded-md peer-focus-visible:ring-2 peer-focus-visible:ring-primary/40" aria-hidden="true" />
                      <span className="flex items-center justify-between gap-2">
                        <span className="font-semibold text-foreground">{option.label}</span>
                        <span className={cn("rounded-sm border px-2 py-0.5 font-mono text-[10px] uppercase", environmentOptionClass[option.tone].badge)}>
                          {option.badgeLabel}
                        </span>
                      </span>
                      <span id={option.descriptionId} className="text-[11px] font-normal leading-4 text-muted-foreground">
                        {option.description}
                      </span>
                    </label>
                  ))}
                </div>
                <FieldSupportText
                  helpId={alpacaForm.fieldHelpIds.environment}
                  helpText={alpacaForm.environmentHelpText}
                  helpClassName="text-[11px] leading-4"
                  disabledReason={alpacaForm.environmentOptions[0]?.disabledReason}
                  disabledReasonId={alpacaForm.environmentOptions[0]?.disabledReasonId ?? undefined}
                  disabledReasonClassName="text-[11px] leading-4"
                />
                <TechnicalDetails
                  label="Service details"
                  description="Provider endpoints are shown only for advanced troubleshooting."
                  className="mt-2"
                >
                  <dl className="grid gap-2">
                    {alpacaForm.environmentOptions.map((option) => (
                      <SettingsFieldRow
                        key={`${option.id}-endpoint`}
                        label={`${option.label} endpoint`}
                        value={option.endpointLabel}
                        tone="muted"
                      />
                    ))}
                  </dl>
                </TechnicalDetails>
              </fieldset>
            </div>
            {alpacaForm.liveAcknowledgement.visible ? (
              <Checkbox
                  id={alpacaForm.liveAcknowledgement.id}
                  checked={alpacaForm.liveAcknowledgement.checked}
                  disabled={alpacaForm.liveAcknowledgement.disabled}
                  required={alpacaForm.liveAcknowledgement.required}
                  onCheckedChange={alpacaForm.setLiveAcknowledged}
                  aria-label={alpacaForm.liveAcknowledgement.ariaLabel}
                  aria-describedby={joinDescribedByIds(
                    alpacaForm.liveAcknowledgement.descriptionId,
                    alpacaForm.liveAcknowledgement.disabledReasonId,
                    alpacaForm.formPanelId
                  )}
                  className="rounded-md border border-live-env/35 bg-live-env/10 px-3 py-3 text-live-env"
                  label={alpacaForm.liveAcknowledgement.label}
                  hint={
                    <>
                      <span id={alpacaForm.liveAcknowledgement.descriptionId} className="block">
                        {alpacaForm.liveAcknowledgement.detail}
                      </span>
                      <FieldSupportText
                        disabledReason={alpacaForm.liveAcknowledgement.disabledReason}
                        disabledReasonId={alpacaForm.liveAcknowledgement.disabledReasonId ?? undefined}
                        disabledReasonClassName="mt-1 block"
                      />
                    </>
                  }
                />
            ) : null}
            <StatusBanner
              id={alpacaForm.formPanelId}
              role={alpacaForm.formPanelRole}
              aria-live={alpacaForm.formPanelAriaLive}
              tone={settingsBannerTone(alpacaForm.formPanelTone)}
              title={alpacaForm.formPanelTitle}
              detail={
                <div className="grid gap-3">
                  <p className="leading-5">{alpacaForm.formPanelDetail}</p>
                <div className="flex flex-wrap gap-2" aria-label="Alpaca credential requirements">
                  {alpacaForm.requirements.map((requirement) => (
                    <span
                      key={requirement.id}
                      className={cn("inline-flex items-center gap-2 rounded-sm border px-2 py-1 text-[11px] font-medium", requirementToneClass[requirement.tone])}
                    >
                      <span className="text-muted-foreground">{requirement.label}</span>
                      <span className="font-mono">{requirement.value}</span>
                    </span>
                  ))}
                </div>
              </div>
              }
            />
            <div className="flex flex-wrap items-center gap-2">
              <Button
                type="submit"
                size="sm"
                disabled={!alpacaForm.canSubmit}
                busy={alpacaForm.submitBusy}
                busyLabel="Testing Alpaca"
                disabledReason={alpacaForm.submitDisabledReason}
              >
                <ShieldCheck className="h-4 w-4" aria-hidden="true" />
                {alpacaForm.submitLabel}
              </Button>
              <Button
                type="button"
                size="sm"
                variant="destructive"
                onClick={alpacaForm.clear}
                disabled={alpacaForm.clearDisabledReason !== null}
                busy={alpacaForm.clearBusy}
                busyLabel="Clearing Alpaca"
                disabledReason={alpacaForm.clearDisabledReason}
              >
                <Trash2 className="h-4 w-4" aria-hidden="true" />
                {alpacaForm.clearLabel}
              </Button>
              {alpacaForm.actionMessage ? (
                <div aria-live={alpacaForm.statusRole === "alert" ? "assertive" : "polite"} className={alpacaForm.statusClassName}>
                  <div>{alpacaForm.actionMessage}</div>
                  {alpacaForm.statusDetails.length > 0 ? (
                    <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                      {alpacaForm.statusDetails.map((detail) => (
                        <li key={detail}>{detail}</li>
                      ))}
                    </ul>
                  ) : null}
                </div>
              ) : null}
            </div>
          </form>

          <div className="grid gap-2">
            <div className="flex flex-wrap gap-2">
              <SettingsChip label="Provider" value={vm.alpacaConnectionPanel.providerLabel} />
              <SettingsChip label="Environment" value={vm.alpacaConnectionPanel.environmentLabel} />
            </div>
            <dl className="grid gap-2">
              <SettingsFieldRow label="Key ID" value={vm.alpacaConnectionPanel.maskedKeyIdLabel} tone="muted" />
              <SettingsFieldRow label="Account" value={vm.alpacaConnectionPanel.accountLabel} tone={vm.alpacaConnectionPanel.statusTone === "success" ? "success" : "muted"} />
              <SettingsFieldRow label="Verified" value={vm.alpacaConnectionPanel.verifiedAtLabel} tone={vm.alpacaConnectionPanel.verifiedAtTone} />
            </dl>
            {vm.alpacaConnectionPanel.warnings.length > 0 ? (
              <div className="rounded-md border border-warning/35 bg-warning/10 px-3 py-2 text-xs leading-5 text-warning">
                {vm.alpacaConnectionPanel.warnings[0]}
              </div>
            ) : null}
            <div className="grid gap-2">
              <div className="min-w-0">
                <h3 className={cn("text-xs font-semibold text-muted-foreground", usesTrackedCaps(vm.alpacaConnectionPanel.setupChecklistTitle) && "uppercase")}>
                  {vm.alpacaConnectionPanel.setupChecklistTitle}
                </h3>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  {vm.alpacaConnectionPanel.setupChecklistDetail}
                </p>
              </div>
              <div
                role="list"
                aria-label={vm.alpacaConnectionPanel.setupChecklistAriaLabel}
                className="grid gap-2"
              >
              {vm.alpacaConnectionPanel.setupChecklist.map((step) => (
                <div
                  key={step.id}
                  role="listitem"
                  className={cn("rounded-md border px-3 py-2", setupStepToneClass[step.tone])}
                >
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="text-sm font-medium text-foreground">{step.label}</div>
                      <p className="mt-1 text-xs leading-5 text-muted-foreground">{step.detail}</p>
                    </div>
                    <SeverityBadge
                      status={badgeVariantToOperatorSeverity(step.badgeVariant)}
                      label={step.statusLabel}
                      className="shrink-0"
                    />
                  </div>
                  {step.actionHref && step.actionLabel ? (
                    <Button asChild variant="outline" size="sm" className="mt-3">
                      <Link to={step.actionHref} aria-label={step.actionAriaLabel ?? step.actionLabel}>
                        {step.actionLabel}
                        <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                      </Link>
                    </Button>
                  ) : null}
                </div>
              ))}
              </div>
            </div>
          </div>
        </CardContent>
      </Card>
      ) : null}

      {showRobinhoodSection ? (
      <Card
        id="robinhood-provider-setup"
        className={cn("panel-surface scroll-mt-6 border", diagnosticToneClass[vm.robinhoodConnectionPanel.statusTone])}
      >
        <CardHeader>
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div>
              <div className="eyebrow-label">Brokerage connection</div>
              <CardTitle className="mt-2 flex items-center gap-2 text-base">
                <ShieldCheck className="h-4 w-4 text-primary" />
                Robinhood (read-only)
              </CardTitle>
              <CardDescription className="mt-2">{vm.robinhoodConnectionPanel.statusDetail}</CardDescription>
            </div>
            <SeverityBadge
              status={badgeVariantToOperatorSeverity(vm.robinhoodConnectionPanel.statusTone)}
              label={vm.robinhoodConnectionPanel.stateLabel}
            />
          </div>
        </CardHeader>
        <CardContent className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(280px,0.8fr)]">
          <div className="grid gap-3">
            {!vm.robinhoodConnectionPanel.isConfigured ? (
              <div className="rounded-md border border-warning/35 bg-warning/10 px-3 py-2 text-xs leading-5 text-warning">
                Read-only Robinhood requires an authorized aggregation provider. Set the ROBINHOOD_BROKERAGE_* OAuth
                environment variables.
              </div>
            ) : null}
            {vm.robinhoodConnectionPanel.warnings.length > 0 ? (
              <div className="rounded-md border border-warning/35 bg-warning/10 px-3 py-2 text-xs leading-5 text-warning">
                {vm.robinhoodConnectionPanel.warnings[0]}
              </div>
            ) : null}
            {robinhoodAuthorizationUrl && vm.robinhoodConnectionPanel.canConnect ? (
              <div className="rounded-md border border-primary/35 bg-primary/10 px-3 py-2 text-xs leading-5">
                <p className="leading-5">Complete authorization in the opened tab to finish connecting Robinhood.</p>
                <a
                  href={robinhoodAuthorizationUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="mt-2 inline-flex items-center gap-1 font-medium text-primary underline"
                >
                  Open authorization page
                  <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
                </a>
              </div>
            ) : null}
            <div className="flex flex-wrap items-center gap-2">
              <Button
                type="button"
                size="sm"
                onClick={robinhoodForm.connect}
                disabled={
                  robinhoodForm.busy
                  || !vm.robinhoodConnectionPanel.canConnect
                  || !vm.robinhoodConnectionPanel.isConfigured
                }
                busy={robinhoodForm.busyAction === "connect"}
                busyLabel="Connecting Robinhood"
              >
                <ShieldCheck className="h-4 w-4" aria-hidden="true" />
                Connect Robinhood
              </Button>
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={robinhoodForm.disconnect}
                disabled={robinhoodForm.busy || !vm.robinhoodConnectionPanel.canDisconnect}
                busy={robinhoodForm.busyAction === "disconnect"}
                busyLabel="Disconnecting Robinhood"
              >
                <Trash2 className="h-4 w-4" aria-hidden="true" />
                Disconnect
              </Button>
              {robinhoodForm.actionMessage ? (
                <div
                  role={robinhoodForm.statusRole}
                  aria-live={robinhoodForm.statusRole === "alert" ? "assertive" : "polite"}
                  className={robinhoodForm.statusClassName}
                >
                  <div>{robinhoodForm.actionMessage}</div>
                  {robinhoodForm.actionDetails.length > 0 ? (
                    <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                      {robinhoodForm.actionDetails.map((detail) => (
                        <li key={detail}>{detail}</li>
                      ))}
                    </ul>
                  ) : null}
                </div>
              ) : null}
            </div>
          </div>

          <div className="grid gap-2">
            <div className="flex flex-wrap gap-2">
              <SettingsChip label="Provider" value={vm.robinhoodConnectionPanel.providerLabel} />
            </div>
            <dl className="grid gap-2">
              <SettingsFieldRow
                label="Account"
                value={vm.robinhoodConnectionPanel.accountLabel}
                tone={vm.robinhoodConnectionPanel.statusTone === "success" ? "success" : "muted"}
              />
              <SettingsFieldRow label="Connected" value={vm.robinhoodConnectionPanel.connectedAtLabel} tone="muted" />
              <SettingsFieldRow label="Expires" value={vm.robinhoodConnectionPanel.expiresAtLabel} tone="muted" />
              <SettingsFieldRow label="Scopes" value={vm.robinhoodConnectionPanel.scopesLabel} tone="muted" />
            </dl>
          </div>
        </CardContent>
      </Card>
      ) : null}

      {showDiagnosticsSection ? (
      <section className="grid gap-4 xl:grid-cols-[1.1fr_0.9fr]">
        <Card id="diagnostic-endpoints" className="panel-surface scroll-mt-6">
          <CardHeader>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <div className="eyebrow-label">Event posture</div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Activity className="h-4 w-4 text-primary" />
                  {vm.recentEventsSection.title}
                </CardTitle>
                <CardDescription className="mt-2">{vm.recentEventsSection.description}</CardDescription>
              </div>
              <SeverityBadge
                status={recentEventsSeverity(vm.recentEventsSection.state)}
                label={vm.recentEventsSection.statusLabel}
              />
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap gap-2">
              <SettingsChip label="Count" value={vm.recentEventsSection.countLabel} />
              <SettingsChip label="Heartbeat" value={overview ? vm.systemFreshness.label : "—"} />
              <SettingsChip label="Stream" value={vm.recentEventsSection.state} />
            </div>
            {overview?.lastHeartbeatUtc ? (
              <TechnicalDetails
                label="Heartbeat details"
                description="The source timestamp is retained for diagnostic comparison."
              >
                <code className="break-all text-xs text-muted-foreground">{overview.lastHeartbeatUtc}</code>
              </TechnicalDetails>
            ) : null}
            {recentEventsVm.rows.length > 0 ? (
              <div className="grid gap-3 xl:grid-cols-[minmax(0,1fr)_minmax(260px,0.48fr)]">
                <DenseDataTable
                  columns={recentEventColumns}
                  rows={recentEventsVm.rows}
                  getRowId={(event) => event.id}
                  getRowAriaLabel={(event) => event.ariaLabel}
                  getRowSelectAriaLabel={(event) => event.selectAriaLabel}
                  getRowAriaControls={(event) => event.detailPanelId}
                  getRowAriaExpanded={(event) => event.expanded}
                  getRowClassName={(event) => eventToneClass[event.tone]}
                  onRowSelect={(event) => recentEventsVm.selectRow(event.id)}
                  selectedRowId={recentEventsVm.selectedRowId}
                  emptyText={vm.recentEventsSection.statusDetail}
                  ariaLabel={recentEventsVm.tableLabel}
                  caption={recentEventsVm.tableCaption}
                />
                <RecentEventDetailPanel
                  id={recentEventsVm.detailPanelId}
                  title={recentEventsVm.detailPanelTitle}
                  description={recentEventsVm.detailPanelDescription}
                  emptyText={recentEventsVm.detailPanelEmptyText}
                  ariaLabel={recentEventsVm.detailPanelAriaLabel}
                  detail={recentEventsVm.selectedDetail}
                />
              </div>
            ) : (
              <div
                role={vm.recentEventsSection.state === "unavailable" ? "alert" : "status"}
                className={cn(
                  "rounded-md border px-4 py-4",
                  vm.recentEventsSection.state === "unavailable"
                    ? "border-danger/35 bg-danger/10"
                    : "border-border/70 bg-secondary/25"
                )}
              >
                <div className="text-sm font-semibold text-foreground">{vm.recentEventsSection.statusLabel}</div>
                <p className="mt-2 text-sm leading-6 text-muted-foreground">{vm.recentEventsSection.statusDetail}</p>
              </div>
            )}
          </CardContent>
        </Card>

        <Card className="panel-surface">
          <CardHeader>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <div className="eyebrow-label">Service status</div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <ExternalLink className="h-4 w-4 text-primary" />
                  {showAdvancedDiagnosticsSection ? "Diagnostic service inventory" : "Exceptions requiring attention"}
                </CardTitle>
                <CardDescription className="mt-2">{vm.diagnosticSummary}</CardDescription>
              </div>
              <SeverityBadge
                status={badgeVariantToOperatorSeverity(vm.diagnosticStatusVariant)}
                label={vm.diagnosticStatusLabel}
              />
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap gap-2">
              <SettingsChip label="Loaded" value={vm.diagnosticCounts.loadedLabel} />
              <SettingsChip label="Failed" value={vm.diagnosticCounts.failedLabel} />
              <SettingsChip label="Checking" value={vm.diagnosticCounts.checkingLabel} />
            </div>
            {visibleDiagnosticLinks.length > 0 ? (
            <div
              className="grid gap-3 md:grid-cols-2"
              role="list"
              aria-label={showAdvancedDiagnosticsSection ? vm.diagnosticListLabel : "Diagnostic exceptions requiring attention"}
            >
              {visibleDiagnosticLinks.map((link) => (
                <div key={link.href} role="listitem">
                  <a
                    href={link.href}
                    target="_blank"
                    rel="noreferrer"
                    aria-label={link.ariaLabel}
                    className={cn(
                      "group flex h-full flex-col gap-2 rounded-lg border px-4 py-3 transition-colors hover:bg-secondary/45 focus:outline-hidden focus:ring-2 focus:ring-primary/40",
                      diagnosticToneClass[link.tone]
                    )}
                  >
                    <div className="flex items-center justify-between gap-2">
                      <span className="font-semibold text-foreground transition-colors group-hover:text-primary">
                        {link.label}
                      </span>
                      <span className="inline-flex items-center gap-2">
                        <SeverityBadge
                          status={badgeVariantToOperatorSeverity(link.badgeVariant)}
                          label={link.statusLabel}
                          className="shrink-0"
                        />
                        {link.isLoading ? (
                          <LoaderCircle className="h-3 w-3 shrink-0 animate-spin text-warning" aria-hidden="true" />
                        ) : (
                          <ExternalLink className="h-3 w-3 shrink-0 text-muted-foreground" aria-hidden="true" />
                        )}
                      </span>
                    </div>
                    <p className="text-xs leading-5 text-muted-foreground">{link.description}</p>
                    <p className="text-xs leading-5 text-foreground/75">{link.statusDetail}</p>
                    <span className="mt-1 font-mono text-[10px] text-muted-foreground">{link.href}</span>
                  </a>
                </div>
              ))}
            </div>
            ) : (
              <div className="rounded-md border border-success/35 bg-success/10 px-4 py-4" role="status">
                <div className="text-sm font-semibold text-foreground">No diagnostic exceptions need attention</div>
                <p className="mt-2 text-xs leading-5 text-muted-foreground">
                  {vm.diagnosticHealthyLinks.length} diagnostic services are loaded. Open the advanced inventory for endpoint-level evidence.
                </p>
              </div>
            )}
            {!showAdvancedDiagnosticsSection ? (
              <Button asChild variant="outline" size="sm">
                <Link to={WORKSTATION_ROUTE_CATALOG.settingsDiagnosticsAdvanced} aria-label="Open advanced diagnostic service inventory">
                  Open advanced diagnostics
                  <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                </Link>
              </Button>
            ) : null}
          </CardContent>
        </Card>
      </section>
      ) : null}
      {showRuntimeSection ? <LifecycleControlPanel /> : null}
      {showRuntimeSection ? (
      <Card id="runtime-feature-capabilities" className="panel-surface scroll-mt-6">
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <div className="eyebrow-label">Runtime controls</div>
              <CardTitle className="flex items-center gap-2 text-base">
                <MonitorCheck className="h-4 w-4 text-primary" />
                {vm.runtimeCapabilitySection.title}
              </CardTitle>
              <CardDescription className="mt-2">{vm.runtimeCapabilitySection.summary}</CardDescription>
            </div>
            <SeverityBadge
              status={badgeVariantToOperatorSeverity(vm.runtimeCapabilitySection.statusVariant)}
              label={vm.runtimeCapabilitySection.statusLabel}
            />
          </div>
        </CardHeader>
        <CardContent>
          {vm.runtimeCapabilitySection.toggles.length === 0 ? (
            <p className="text-sm text-muted-foreground">{vm.runtimeCapabilitySection.description}</p>
          ) : (
            <div className="grid gap-3 md:grid-cols-2" role="list" aria-label={vm.runtimeCapabilitySection.listLabel}>
              {vm.runtimeCapabilitySection.toggles.map((capability) => (
                <div
                  key={capability.capabilityKey}
                  role="listitem"
                  className={cn("rounded-lg border px-4 py-4", diagnosticToneClass[capability.statusVariant === "success" ? "success" : "warning"])}
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="font-mono text-[10px] text-muted-foreground">{capability.capabilityKey}</div>
                      <h3 className="mt-2 text-sm font-semibold text-foreground">{capability.displayName}</h3>
                      <p className="mt-2 text-xs leading-5 text-muted-foreground">{capability.description}</p>
                    </div>
                    <SeverityBadge
                      status={badgeVariantToOperatorSeverity(capability.statusVariant)}
                      label={capability.statusLabel}
                      className="shrink-0"
                    />
                  </div>
                  <div className="mt-3 flex flex-wrap gap-2">
                    <SettingsChip label="Default" value={capability.defaultLabel} />
                    <SettingsChip label="Config" value={capability.overrideLabel} />
                  </div>
                  <div className="mt-4 grid gap-1">
                    <Toggle
                       checked={capability.isEnabled}
                       disabled={!capability.canToggle || !onFeatureCapabilityToggle}
                       onCheckedChange={(checked) => {
                         setMutationConfirmation({
                           id: `feature-capability-${capability.capabilityKey}`,
                           title: `${checked ? "Enable" : "Disable"} ${capability.displayName}?`,
                           description: `${checked ? "Enable" : "Disable"} this browser workstation capability override. Required policy remains server-owned and cannot be changed here.`,
                           confirmLabel: checked ? "Confirm enable" : "Confirm disable",
                           confirmAriaLabel: `${checked ? "Confirm enable" : "Confirm disable"} ${capability.displayName}`,
                           destructive: !checked,
                           run: () => onFeatureCapabilityToggle?.(capability.capabilityKey, checked)
                         });
                       }}
                      aria-label={capability.ariaLabel}
                      label={capability.canToggle ? "Allow this browser workstation capability" : "Required capability"}
                    />
                    {capability.disabledReason ? (
                      <p className="text-xs leading-5 text-muted-foreground">{capability.disabledReason}</p>
                    ) : null}
                  </div>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>
      ) : null}

      {showBackendCapabilitySection ? (
      <Card id="backend-capability-coverage" className="panel-surface scroll-mt-6">
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <div className="eyebrow-label">Service reachability</div>
              <CardTitle className="flex items-center gap-2 text-base">
                <ExternalLink className="h-4 w-4 text-primary" />
                Capability coverage
              </CardTitle>
              <CardDescription className="mt-2">{vm.backendCapabilitySummary}</CardDescription>
            </div>
            <SeverityBadge
              status={badgeVariantToOperatorSeverity(vm.backendCapabilityStatusVariant)}
              label={vm.backendCapabilityStatusLabel}
            />
          </div>
        </CardHeader>
        <CardContent>
          <div className="grid gap-3 xl:grid-cols-2" role="list" aria-label={vm.backendCapabilityListLabel}>
            {vm.backendCapabilityGroups.map((group) => (
              <div
                key={group.id}
                role="listitem"
                className={cn("rounded-lg border px-4 py-4", diagnosticToneClass[capabilityTone(group.statusVariant)])}
              >
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="eyebrow-label">{group.workspaceLabel} · {group.route}</div>
                    <h3 className="mt-2 text-sm font-semibold text-foreground">{group.title}</h3>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{group.description}</p>
                  </div>
                  <SeverityBadge
                    status={badgeVariantToOperatorSeverity(group.statusVariant)}
                    label={group.statusLabel}
                    className="shrink-0"
                  />
                </div>
                <div className="mt-3 flex flex-wrap gap-2">
                  <SettingsChip label="Mapped" value={group.endpointCountLabel} />
                  <SettingsChip label="Loaded" value={group.loadedCountLabel} />
                </div>
                <p className="mt-3 text-xs leading-5 text-foreground/75">{group.statusDetail}</p>
                <div className="mt-4 grid gap-2 sm:grid-cols-2">
                  {group.endpoints.map((endpoint) => endpoint.isBrowserNavigable ? (
                    <a
                      key={endpoint.id}
                      href={endpoint.href}
                      target="_blank"
                      rel="noreferrer"
                      aria-label={endpoint.ariaLabel}
                      className="flex min-w-0 items-start gap-2 rounded-md border border-border/60 bg-background/45 px-3 py-2 text-xs transition-colors hover:bg-secondary/45 focus:outline-hidden focus:ring-2 focus:ring-primary/40"
                    >
                      <EndpointReference endpoint={endpoint} />
                    </a>
                  ) : (
                    <div
                      key={endpoint.id}
                      role="group"
                      aria-label={endpoint.ariaLabel}
                      className="flex min-w-0 items-start gap-2 rounded-md border border-border/60 bg-secondary/20 px-3 py-2 text-xs"
                    >
                      <EndpointReference endpoint={endpoint} />
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </CardContent>
      </Card>
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

function EndpointReference({
  endpoint
}: {
  endpoint: {
    method: string;
    label: string;
    href: string;
    interactionLabel: string;
  };
}) {
  return (
    <>
      <Badge variant="outline" className="shrink-0">{endpoint.method}</Badge>
      <span className="min-w-0">
        <span className="block font-semibold text-foreground">{endpoint.label}</span>
        <span className="mt-1 block break-all font-mono text-[10px] leading-4 text-muted-foreground">
          {endpoint.href}
        </span>
        <span className={cn("mt-1 inline-flex rounded-sm border border-border/60 px-1.5 py-0.5 text-[10px] text-muted-foreground", usesTrackedCaps(endpoint.interactionLabel) && "uppercase")}>
          {endpoint.interactionLabel}
        </span>
      </span>
    </>
  );
}

function ProfileAuthenticationStepRow({ step }: { step: SettingsProfileAuthenticationStep }) {
  return (
    <div role="listitem" className={cn("rounded-md border px-3 py-2", setupStepToneClass[step.tone])}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="text-sm font-medium text-foreground">{step.label}</div>
          <p className="mt-1 text-xs leading-5 text-muted-foreground">{step.detail}</p>
        </div>
        <SeverityBadge
          status={badgeVariantToOperatorSeverity(step.badgeVariant)}
          label={step.statusLabel}
          className="shrink-0"
        />
      </div>
      {step.actionHref && step.actionLabel ? (
        <Button asChild variant="outline" size="sm" className="mt-3">
          <Link to={step.actionHref} aria-label={step.actionAriaLabel ?? step.actionLabel}>
            {step.actionLabel}
            <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
          </Link>
        </Button>
      ) : null}
    </div>
  );
}

function RecentEventDetailPanel({
  id,
  title,
  description,
  emptyText,
  ariaLabel,
  detail
}: {
  id: string;
  title: string;
  description: string;
  emptyText: string;
  ariaLabel: string;
  detail: SettingsRecentEventDetail | null;
}) {
  return (
    <aside
      id={id}
      role="complementary"
      aria-label={ariaLabel}
      aria-live="polite"
      className="row-detail-panel h-fit min-w-0"
    >
      <div className="head">{title}</div>
      <div className="body">
        {detail ? (
          <div role="region" aria-label={detail.ariaLabel} className="space-y-3">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <div className={eyebrowClassName(detail.eyebrow)}>{detail.eyebrow}</div>
                <h3 className="mt-2 break-words text-sm font-semibold text-foreground">{detail.title}</h3>
                <p className="mt-1 break-words font-mono text-xs text-muted-foreground">{detail.subtitle}</p>
              </div>
              <SeverityBadge
                status={badgeVariantToOperatorSeverity(detail.statusVariant)}
                label={detail.statusLabel}
                className="shrink-0"
              />
            </div>
            <p className="text-sm leading-6 text-muted-foreground">{detail.description}</p>
            <dl className="grid gap-2 sm:grid-cols-2 xl:grid-cols-1 2xl:grid-cols-2">
              {detail.fields.map((field) => (
                <div key={field.label} className="rounded-sm border border-border/60 bg-background/35 px-2.5 py-2">
                  <dt className={cn("text-[10px] text-muted-foreground", usesTrackedCaps(field.label) && "uppercase tracking-[0.14em]")}>{field.label}</dt>
                  <dd className={cn("mt-1 break-words font-mono text-xs", itemToneClass[field.tone])}>
                    {field.value}
                  </dd>
                </div>
              ))}
            </dl>
          </div>
        ) : (
          <div role="status" className="rounded-md border border-dashed border-border/70 bg-secondary/20 px-3 py-3">
            <div className="text-sm font-semibold text-foreground">{description}</div>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">{emptyText}</p>
          </div>
        )}
      </div>
    </aside>
  );
}

function SettingsChip({ label, value }: { label: string; value: string }) {
  return (
    <div className="toolbar-chip" aria-label={`${label} ${value}`}>
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono text-foreground">{value}</span>
    </div>
  );
}

function AssetProfileFieldInput({
  field,
  value,
  disabled,
  onChange
}: {
  field: SecurityAssetProfileFieldDefinition;
  value: string;
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  if (field.fieldType === "Boolean") {
    if (!field.isRequired) {
      // TRI-STATE for optional Booleans: blank means "not asserted" and stays omitted from the
      // payload, so an operator who touched the control can still return the field to absence -
      // a two-state checkbox renders blank and false identically and would trap the field in a
      // meaningful negative assertion once toggled.
      return (
        <FilterSelect
          label={field.label}
          value={value}
          onChange={onChange}
          options={[
            { value: "", label: "Not asserted" },
            { value: "true", label: "Yes" },
            { value: "false", label: "No" }
          ]}
          disabled={disabled}
        />
      );
    }

    return (
      <Checkbox
        checked={value === "true"}
        onCheckedChange={(checked) => onChange(checked ? "true" : "false")}
        className="min-h-16 rounded-md border border-border/60 bg-secondary/15 px-3 py-2 text-xs"
        disabled={disabled}
        label={`${field.label} *`}
      />
    );
  }

  if (field.fieldType === "Enum") {
    return (
      <FilterSelect
        label={`${field.label}${field.isRequired ? " *" : ""}`}
        value={value}
        onChange={onChange}
        options={[
          { value: "", label: "Select value" },
          ...field.allowedValues.map((allowed) => ({ value: allowed, label: allowed }))
        ]}
        disabled={disabled}
      />
    );
  }

  return (
    <label className="grid gap-1 text-xs font-medium text-muted-foreground">
      {field.label}{field.isRequired ? " *" : ""}
      <Input
        type={assetProfileInputType(field)}
        min={field.minValue ?? undefined}
        max={field.maxValue ?? undefined}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        disabled={disabled}
        aria-label={`Profile field ${field.label}`}
      />
    </label>
  );
}

function assetProfileInputType(field: SecurityAssetProfileFieldDefinition): "text" | "number" | "date" {
  if (field.fieldType === "Date") return "date";
  if (field.fieldType === "Decimal" || field.fieldType === "Integer") return "number";
  return "text";
}

function createAssetProfileDraftState(profile: SecurityAssetProfileDefinition | null): AssetProfileDraftState {
  const profileId = profile ? `${profile.profileId}-variant` : "";
  return {
    starterProfileId: profile?.profileId ?? "",
    profileId,
    name: profile ? `${profile.name} Variant` : "",
    category: profile?.category ?? "",
    subType: profile?.subType ?? "",
    rationale: "Draft asset profile from approved starter template.",
    busyAction: null,
    rollbackTargetVersion: profile?.version ?? 1,
    lineage: null,
    result: null,
    message: null,
    details: [],
    tone: "default"
  };
}

function normalizeAssetProfileId(value: string): string {
  return value.trim().toLowerCase().replace(/[^a-z0-9-]+/g, "-").replace(/^-+|-+$/g, "");
}

/**
 * Today as an asset-profile `effectiveFrom`. Deliberately the UTC calendar day, the same day
 * `isWriteSelectableAssetProfile` compares effective windows against, so a profile approved here
 * is selectable for writes straight away for every operator.
 */
function todayDateOnly(): string {
  return utcToday();
}

function createBrowserGuid(): string {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) {
    return crypto.randomUUID();
  }

  return "10000000-1000-4000-8000-100000000000".replace(/[018]/g, (character) => {
    const value = Number(character);
    return (value ^ (Math.random() * 16 >> (value / 4))).toString(16);
  });
}

const scopedAccessPrincipalKindOptions: Array<{ value: AccessPrincipalKind; label: string }> = [
  { value: "User", label: "User" },
  { value: "Group", label: "Group" }
];

const scopedAccessScopeKindOptions: Array<{ value: AccessScopeKind; label: string }> = [
  { value: "Fund", label: "Fund" },
  { value: "Account", label: "Account" },
  { value: "InvestmentPortfolio", label: "Investment portfolio" },
  { value: "LegalEntity", label: "Legal entity" },
  { value: "Vehicle", label: "Vehicle" },
  { value: "Sleeve", label: "Sleeve" },
  { value: "Client", label: "Client" },
  { value: "Business", label: "Business" },
  { value: "Organization", label: "Organization" },
  { value: "Global", label: "Global" }
];

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

function upsertScopedAccessAssignment(
  assignments: UserAccessAssignment[],
  assignment: UserAccessAssignment
): UserAccessAssignment[] {
  const index = assignments.findIndex((entry) => entry.assignmentId === assignment.assignmentId);
  if (index < 0) {
    return [assignment, ...assignments];
  }

  const next = [...assignments];
  next[index] = assignment;
  return next;
}

function toScopedAccessDateTime(value: string): string | null {
  const trimmed = value.trim();
  if (!trimmed) {
    return null;
  }

  return /^\d{4}-\d{2}-\d{2}$/.test(trimmed) ? `${trimmed}T00:00:00Z` : trimmed;
}

function formatScopedAccessScope(assignment: UserAccessAssignment): string {
  return assignment.scopeKind === "Global"
    ? "Global"
    : `${assignment.scopeKind}: ${assignment.scopeId ?? "Missing scope"}`;
}

function formatScopedAccessWindow(assignment: UserAccessAssignment): string {
  const from = formatScopedAccessDate(assignment.effectiveFrom) ?? "Effective now";
  const to = formatScopedAccessDate(assignment.effectiveTo) ?? "Open-ended";
  return `${from} to ${to}`;
}

function formatScopedAccessApprovalLimit(assignment: UserAccessAssignment): string {
  if (assignment.approvalLimitAmount === null || assignment.approvalLimitAmount === undefined) {
    return "Not specified";
  }

  const amount = formatNumberAmount(assignment.approvalLimitAmount);
  const currency = assignment.approvalLimitCurrency?.trim();
  return currency ? `${currency} ${amount}` : amount;
}

function formatScopedAccessDate(value?: string | null): string | null {
  if (!value) {
    return null;
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }

  return formatUtcDate(parsed);
}

function filterProviderRow(
  row: { displayName: string; recommendedAction: string; affectedWorkflowsLabel: string; capabilityGroup: "brokerage" | "data" | "accounting"; healthTone: string; verificationStatus: "verified" | "pending" | "failed" },
  search: string,
  capabilityFilter: "all" | "brokerage" | "data" | "accounting",
  healthFilter: "all" | "healthy" | "warning" | "blocked",
  verificationFilter: "all" | "verified" | "unverified"
): boolean {
  if (search) {
    const content = `${row.displayName} ${row.recommendedAction} ${row.affectedWorkflowsLabel}`.toLowerCase();
    if (!content.includes(search)) {
      return false;
    }
  }

  if (capabilityFilter === "brokerage" && row.capabilityGroup !== "brokerage") {
    return false;
  }
  if (capabilityFilter === "data" && row.capabilityGroup !== "data") {
    return false;
  }
  if (capabilityFilter === "accounting" && row.capabilityGroup !== "accounting") {
    return false;
  }

  if (healthFilter === "healthy" && row.healthTone !== "success") return false;
  if (healthFilter === "warning" && row.healthTone !== "warning") return false;
  if (healthFilter === "blocked" && row.healthTone !== "danger") return false;

  const verified = row.verificationStatus === "verified";
  if (verificationFilter === "verified" && !verified) return false;
  if (verificationFilter === "unverified" && verified) return false;

  return true;
}

function providerRiskScore(row: { healthTone: string; credentialTone: string; verificationStatus: "verified" | "pending" | "failed"; fallbackStatus: "active" | "available" | "missing" }): number {
  const healthScore = row.healthTone === "danger" ? 100 : row.healthTone === "warning" ? 70 : 20;
  const credentialScore = row.credentialTone === "danger" ? 40 : row.credentialTone === "warning" ? 20 : 0;
  const verificationScore = row.verificationStatus === "failed" ? 30 : row.verificationStatus === "pending" ? 20 : 0;
  const fallbackScore = row.fallbackStatus === "active" ? 10 : row.fallbackStatus === "missing" ? 20 : 0;
  return healthScore + credentialScore + verificationScore + fallbackScore;
}

// Event-stream readiness → Concrete severity (SeverityBadge): a live stream is Ready,
// an empty stream is Info, and an unavailable stream is Blocked.
function recentEventsSeverity(state: "ready" | "empty" | "unavailable"): string {
  if (state === "unavailable") return "blocked";
  if (state === "empty") return "info";
  return "ready";
}

function settingsBannerTone(tone: keyof typeof diagnosticToneClass | keyof typeof itemToneClass): "success" | "warning" | "danger" | "info" {
  if (tone === "success") return "success";
  if (tone === "warning") return "warning";
  if (tone === "danger") return "danger";
  return "info";
}

function capabilityTone(tone: "default" | "success" | "warning" | "danger" | "outline"): keyof typeof diagnosticToneClass {
  if (tone === "success") return "success";
  if (tone === "warning") return "warning";
  if (tone === "danger") return "danger";
  return "default";
}
