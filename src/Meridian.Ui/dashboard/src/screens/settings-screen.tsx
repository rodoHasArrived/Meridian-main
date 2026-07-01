import { Activity, ArrowRight, ExternalLink, GitBranch, KeyRound, LoaderCircle, MonitorCheck, RefreshCcw, Save, Search, ShieldCheck, Trash2, User } from "lucide-react";
import { ProviderSetupPanel } from "@/components/data/provider-setup-panel";
import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from "react";
import { Link, useLocation } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox, Toggle } from "@/components/ui/checkbox";
import { FieldSupportText, joinDescribedByIds } from "@/components/ui/field-support";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { WorkspaceFilterBar, WorkspaceTabStrip } from "@/components/meridian/workspace-primitives";
import {
  activateProviderIntegration,
  approveSecurityAssetProfile,
  assignLedgerMapping,
  checkProviderIntegrationSchemaDrift,
  createProviderIntegrationReconciliationHandoff,
  createRolePermissionProfile,
  createScopedAccessAssignment,
  createSecurityMasterEntry,
  deleteProviderCredentials,
  draftSecurityAssetProfile,
  getProviderIntegrationConnectionMonitor,
  getProviderIntegrationConnectionSyncPlan,
  getProviderIntegrationConnectionSyncRuns,
  getProviderIntegrationIdentityResolution,
  getProviderIntegrationPromotionReadiness,
  getProviderIntegrationQuarantineReview,
  getProviderIntegrationReadiness,
  getProviderIntegrationReconciliationHandoffHistory,
  getProviderIntegrationStagingReview,
  getProviderIntegrationTemplate,
  getProviderIntegrationTemplates,
  getSecurityAssetProfileLineage,
  importProviderIntegrationOpenApi,
  listScopedAccessAssignments,
  putProviderCredentials,
  replayProviderIntegrationQuarantineRecords,
  resolveProviderIntegrationQuarantineRecord,
  revokeScopedAccessAssignment,
  rollbackSecurityAssetProfile,
  runDueProviderIntegrationSync,
  runManualCsvProviderIntegrationDryRun,
  runRestProviderIntegrationDryRun,
  saveProviderIntegrationSetup,
  testProviderConnection,
  upsertOperationsApprovalPolicyRule,
  upsertOperationsCloseCalendarItem,
  verifyProviderConnection
} from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { cn } from "@/lib/utils";
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
import type {
  BrokerageConnectionStatus,
  DataWorkspaceResponse,
  FeatureCapabilitySettingsResponse,
  AccountingWorkspaceResponse,
  ReportingWorkspaceResponse,
  PortfolioWorkspaceResponse,
  ProviderConnectionRow,
  ProviderCredentialFieldMetadata,
  ProviderEnvironmentOption,
  ProviderRoutingBinding,
  ProviderRoutingConnection,
  ProviderRoutingTrustSnapshot,
  AccessPrincipalKind,
  AccessScopeKind,
  LedgerMappingWorkbench,
  OperationsApprovalPolicyMatrix,
  OperationsApprovalPolicyMatrixRow,
  OperationsCloseCalendar,
  OperationsCloseCalendarItem,
  ProviderIntegrationAuthType,
  ProviderIntegrationActivationReadiness,
  ProviderIntegrationActivationResult,
  ProviderIntegrationCapabilityKind,
  ProviderIntegrationConnection,
  ProviderIntegrationConnectionMonitor,
  ProviderIntegrationDryRunResult,
  ProviderIntegrationEndpointDefinition,
  ProviderIntegrationFieldMapping,
  ProviderIntegrationManifest,
  ProviderIntegrationOpenApiImportResult,
  ProviderIntegrationProcessingStatus,
  ProviderIntegrationPromotionReadinessPreview,
  ProviderIntegrationQuarantinedRecord,
  ProviderIntegrationQuarantineDecision,
  ProviderIntegrationQuarantineResolutionAction,
  ProviderIntegrationQuarantineReview,
  ProviderIntegrationReconciliationHandoffHistory,
  ProviderIntegrationSchemaDriftCheckResult,
  ProviderIntegrationSetupSaveResult,
  ProviderIntegrationStagingIdentityResolutionPreview,
  ProviderIntegrationStagingReview,
  ProviderIntegrationSyncPlan,
  ProviderIntegrationSyncRunEvidence,
  ProviderIntegrationSyncRunHistory,
  ProviderIntegrationTemplateCatalogEntry,
  RolePermissionCatalog,
  SecurityAssetProfileDefinition,
  SecurityAssetProfileFieldDefinition,
  SecurityAssetProfileGovernanceResult,
  SecurityAssetProfileLineage,
  StrategyWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
  TradingWorkspaceResponse,
  UserAccessAssignment,
  WorkspaceKey
} from "@/types";

interface SettingsScreenProps {
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

type ProviderInlineField = string;
type ProviderInlineBusyAction = "test" | "save" | "verify" | "clear" | null;

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

interface ProfileBackedSecurityState {
  profileId: string;
  displayName: string;
  internalCode: string;
  currency: string;
  fieldValues: Record<string, string>;
  rationale: string;
  busy: boolean;
  message: string | null;
  details: string[];
  tone: "default" | "success" | "danger" | "warning";
}

interface ProviderInlineFieldDefinition {
  field: ProviderInlineField;
  label: string;
  type: "password" | "url" | "text";
  placeholder: string;
  helpText: string;
  required: boolean;
}

interface ProviderInlineState {
  editing: boolean;
  values: Record<ProviderInlineField, string>;
  environment: string;
  liveAcknowledged: boolean;
  dirty: boolean;
  busyAction: ProviderInlineBusyAction;
  statusMessage: string | null;
  statusDetails: string[];
  statusTone: "default" | "success" | "warning" | "danger";
  verificationFailed: boolean;
  testLatencyLabel: string | null;
}

type ProviderRuntimePhase = "idle" | "loading" | "loaded" | "error";

interface ProviderRuntimeEvidenceState {
  phase: ProviderRuntimePhase;
  message: string | null;
  details: string[];
  monitor: ProviderIntegrationConnectionMonitor | null;
  syncRuns: ProviderIntegrationSyncRunHistory | null;
  syncPlan: ProviderIntegrationSyncPlan | null;
  staging: ProviderIntegrationStagingReview | null;
  identity: ProviderIntegrationStagingIdentityResolutionPreview | null;
  promotion: ProviderIntegrationPromotionReadinessPreview | null;
  handoff: ProviderIntegrationReconciliationHandoffHistory | null;
  quarantine: ProviderIntegrationQuarantineReview | null;
}

interface ProviderOpenApiImportState {
  manifestId: string;
  displayName: string;
  environment: string;
  authType: ProviderIntegrationAuthType;
  tokenUrl: string;
  scopes: string;
  capabilities: string;
  openApiDocumentJson: string;
  changeReason: string;
  busy: boolean;
  result: ProviderIntegrationOpenApiImportResult | null;
  message: string | null;
  details: string[];
  tone: "default" | "success" | "danger" | "warning";
}
type ProviderIntegrationWorkbenchBusyAction =
  | "activate"
  | "csv-dry-run"
  | "drift"
  | "readiness"
  | "rest-dry-run"
  | "save"
  | "template"
  | "templates"
  | null;

interface ProviderIntegrationWorkbenchState {
  templates: ProviderIntegrationTemplateCatalogEntry[] | null;
  selectedManifestId: string;
  manifest: ProviderIntegrationManifest | null;
  connection: ProviderIntegrationConnection | null;
  draftManifestJson: string;
  draftConnectionJson: string;
  capability: ProviderIntegrationCapabilityKind;
  endpointKey: string;
  csvFileName: string;
  csvContent: string;
  restPathParametersJson: string;
  restQueryParametersJson: string;
  readiness: ProviderIntegrationActivationReadiness | null;
  setupResult: ProviderIntegrationSetupSaveResult | null;
  dryRunResult: ProviderIntegrationDryRunResult | null;
  driftResult: ProviderIntegrationSchemaDriftCheckResult | null;
  activationResult: ProviderIntegrationActivationResult | null;
  busyAction: ProviderIntegrationWorkbenchBusyAction;
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

const itemToneClass = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger",
  muted: "text-muted-foreground"
} as const;

const diagnosticToneClass = {
  default: "border-border/70 bg-secondary/30",
  success: "border-success/30 bg-success/10",
  warning: "border-warning/35 bg-warning/10",
  danger: "border-danger/35 bg-danger/10"
} as const;

const emptyProviderInlineValues: Record<ProviderInlineField, string> = {};

const requirementToneClass = {
  success: "border-success/30 bg-success/10 text-success",
  warning: "border-warning/35 bg-warning/10 text-warning",
  muted: "border-border/70 bg-secondary/25 text-muted-foreground"
} as const;

const environmentOptionClass = {
  paper: {
    selected: "border-paper/40 bg-paper/10 text-paper",
    idle: "border-border/70 bg-secondary/25 text-foreground hover:border-paper/35 hover:bg-paper/10",
    badge: "border-paper/30 bg-paper/10 text-paper"
  },
  live: {
    selected: "border-live-env/40 bg-live-env/10 text-live-env",
    idle: "border-border/70 bg-secondary/25 text-foreground hover:border-live-env/35 hover:bg-live-env/10",
    badge: "border-live-env/35 bg-live-env/10 text-live-env"
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

type SettingsTaskViewId =
  | "overview"
  | "operations"
  | "providers"
  | "data-providers"
  | "brokerage"
  | "diagnostics"
  | "runtime";

interface SettingsTaskView {
  id: SettingsTaskViewId;
  label: string;
  href: string;
  sectionId: string;
}

const settingsTaskViews: SettingsTaskView[] = [
  {
    id: "overview",
    label: "Profile",
    href: "#settings-overview",
    sectionId: "settings-overview"
  },
  {
    id: "operations",
    label: "Accounting Systems",
    href: "#fund-operations-control-center",
    sectionId: "fund-operations-control-center"
  },
  {
    id: "providers",
    label: "Provider Connections",
    href: "#provider-connection-center",
    sectionId: "provider-connection-center"
  },
  {
    id: "data-providers",
    label: "Data Providers",
    href: "#data-provider-modules",
    sectionId: "data-provider-modules"
  },
  {
    id: "brokerage",
    label: "Provider Setup",
    href: "#alpaca-provider-setup",
    sectionId: "alpaca-provider-setup"
  },
  {
    id: "diagnostics",
    label: "Diagnostics",
    href: "#diagnostic-endpoints",
    sectionId: "diagnostic-endpoints"
  },
  {
    id: "runtime",
    label: "Feature Coverage",
    href: "#runtime-feature-capabilities",
    sectionId: "runtime-feature-capabilities"
  }
];

function resolveSettingsTaskViewId(hash: string): SettingsTaskViewId {
  const normalizedHash = hash.replace(/^#/, "");
  if (normalizedHash === "backend-capability-coverage") {
    return "runtime";
  }
  if (normalizedHash === "robinhood-provider-setup") {
    return "providers";
  }
  if (normalizedHash === "scoped-access-control") {
    return "overview";
  }
  if (normalizedHash === "asset-profile-accounting") {
    return "operations";
  }
  return settingsTaskViews.find((view) => view.sectionId === normalizedHash)?.id ?? "overview";
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
>): SettingsTaskViewId {
  if (providerConnections || providerRoutingConnections || providerRoutingBindings || providerRoutingTrustSnapshots || brokerageConnection) {
    return "providers";
  }

  if (securityAssetProfiles) {
    return "operations";
  }

  if (featureCapabilities) {
    return "runtime";
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

  return "overview";
}

export function SettingsScreen({
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
  const vm = buildSettingsScreenViewModel({
    session,
    overview,
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
  const [hashTaskView, setHashTaskView] = useState<SettingsTaskViewId | null>(() => {
    const initialHash = typeof window === "undefined" ? "" : window.location.hash;
    return initialHash ? resolveSettingsTaskViewId(initialHash) : null;
  });
  const routeHashTaskView = location.hash ? resolveSettingsTaskViewId(location.hash) : null;
  const activeTaskView = routeHashTaskView ?? hashTaskView ?? inferredTaskView;
  const [providerSearch, setProviderSearch] = useState("");
  const [providerCapabilityFilter, setProviderCapabilityFilter] = useState<"all" | "brokerage" | "data" | "accounting">("all");
  const [providerHealthFilter, setProviderHealthFilter] = useState<"all" | "healthy" | "warning" | "blocked">("all");
  const [providerVerificationFilter, setProviderVerificationFilter] = useState<"all" | "verified" | "unverified">("all");
  const [providerSort, setProviderSort] = useState<"risk" | "name">("risk");
  const [providerInlineState, setProviderInlineState] = useState<Record<string, ProviderInlineState>>({});
  const [providerRuntimeState, setProviderRuntimeState] = useState<Record<string, ProviderRuntimeEvidenceState>>({});
  const [providerOpenApiImportState, setProviderOpenApiImportState] = useState<Record<string, ProviderOpenApiImportState>>({});
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
    () => (securityAssetProfiles ?? []).filter((profile) => profile.status === "Approved"),
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
  const providerRowIdsSignature = useMemo(
    () => allProviderRows.map((row) => row.providerId).sort().join("|"),
    [allProviderRows]
  );
  const providerFieldDefinitions = useMemo(
    () => Object.fromEntries(allProviderRows.map((row) => [row.providerId, buildProviderFieldDefinitions(row)])),
    [allProviderRows]
  );
  const providerRiskScores = useMemo(
    () => Object.fromEntries(allProviderRows.map((row) => [row.providerId, providerRiskScore(row)])),
    [allProviderRows]
  );
  const settingsTaskTabItems = settingsTaskViews.map((view) => ({
    id: view.id,
    label: view.label,
    selected: activeTaskView === view.id,
    panelId: view.sectionId,
    href: view.href
  }));
  const settingsTaskFields = [
    { id: "providers", label: "Provider connections", value: String(allProviderRows.length) },
    { id: "access", label: "Profile access", value: String(scopedAccessAssignments.length) },
    { id: "operations", label: "Accounting systems", value: vm.operationsControlCenter.loadedCountLabel },
    { id: "profiles", label: "Data profiles", value: vm.assetProfileGovernancePanel.approvedCountLabel },
    { id: "diagnostics", label: "System details", value: vm.diagnosticCounts.loadedLabel }
  ];
  const showAccessSection = activeTaskView === "overview";
  const showOperationsSection = activeTaskView === "operations";
  const showAssetProfileSection = activeTaskView === "operations";
  const showProviderSection = activeTaskView === "providers";
  const showDataProviderModulesSection = activeTaskView === "data-providers";
  const showBrokerageSection = activeTaskView === "providers";
  const showDiagnosticsSection = activeTaskView === "diagnostics";
  const showRuntimeSection = activeTaskView === "runtime";
  const showBackendCapabilitySection = activeTaskView === "diagnostics" || activeTaskView === "runtime";

  useEffect(() => {
    setHashTaskView(routeHashTaskView);
  }, [routeHashTaskView]);

  useEffect(() => {
    const updateActiveTaskView = () => {
      setHashTaskView(window.location.hash ? resolveSettingsTaskViewId(window.location.hash) : null);
    };

    window.addEventListener("hashchange", updateActiveTaskView);
    return () => window.removeEventListener("hashchange", updateActiveTaskView);
  }, []);

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
    if (!inlineProviderManagementEnabled) {
      return;
    }

    setProviderInlineState((current) => {
      let changed = false;
      const next = { ...current };
      for (const row of allProviderRows) {
        if (next[row.providerId]) {
          continue;
        }
        next[row.providerId] = createProviderInlineState(row);
        changed = true;
      }
      return changed ? next : current;
    });
  }, [inlineProviderManagementEnabled, providerRowIdsSignature]);

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

  const updateProviderInlineState = (providerId: string, updater: (state: ProviderInlineState) => ProviderInlineState) => {
    setProviderInlineState((current) => {
      const previous = current[providerId];
      if (!previous) {
        return current;
      }
      return {
        ...current,
        [providerId]: updater(previous)
      };
    });
  };

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
        requestedAt: requestedAt.toISOString()
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

  const toggleProviderEdit = (providerId: string) => {
    updateProviderInlineState(providerId, (state) => ({
      ...state,
      editing: !state.editing,
      statusMessage: null,
      statusDetails: [],
      statusTone: "default"
    }));
  };

  const updateProviderField = (providerId: string, field: ProviderInlineField, value: string) => {
    updateProviderInlineState(providerId, (state) => ({
      ...state,
      dirty: true,
      values: {
        ...state.values,
        [field]: value
      },
      statusMessage: null,
      statusDetails: [],
      statusTone: "default"
    }));
  };

  const updateProviderEnvironment = (providerId: string, value: ProviderInlineState["environment"]) => {
    updateProviderInlineState(providerId, (state) => ({
      ...state,
      dirty: true,
      environment: value,
      liveAcknowledged: value === "live" ? state.liveAcknowledged : false,
      statusMessage: null,
      statusDetails: [],
      statusTone: "default"
    }));
  };

  const updateProviderLiveAcknowledgement = (providerId: string, checked: boolean) => {
    updateProviderInlineState(providerId, (state) => ({
      ...state,
      dirty: true,
      liveAcknowledged: checked,
      statusMessage: null,
      statusDetails: [],
      statusTone: "default"
    }));
  };

  const runProviderTest = async (providerId: string) => {
    updateProviderInlineState(providerId, (state) => ({
      ...state,
      busyAction: "test",
      statusMessage: null,
      statusDetails: [],
      statusTone: "default"
    }));
    try {
      const result = await testProviderConnection(providerId);
      updateProviderInlineState(providerId, (state) => ({
        ...state,
        busyAction: null,
        testLatencyLabel: result.latency ?? null,
        statusMessage: result.success ? "Connection test passed." : "Connection test failed.",
        statusDetails: [result.message].filter(Boolean),
        statusTone: result.success ? "success" : "danger"
      }));
    } catch (error) {
      const display = describeApiError(error, "Provider connection test failed.");
      updateProviderInlineState(providerId, (state) => ({
        ...state,
        busyAction: null,
        statusMessage: display.summary,
        statusDetails: display.details,
        statusTone: "danger"
      }));
    }
  };

  const saveProviderDraft = async (row: (typeof allProviderRows)[number]) => {
    const state = providerInlineState[row.providerId];
    if (!state) {
      return;
    }
    const definitions = providerFieldDefinitions[row.providerId] ?? buildProviderFieldDefinitions(row);
    const missingField = definitions.find((definition) => (
      definition.required && !(state.values[definition.field] ?? "").trim()
    ));
    if (missingField) {
      updateProviderInlineState(row.providerId, (current) => ({
        ...current,
        statusMessage: `${missingField.label} is required before save.`,
        statusDetails: [],
        statusTone: "warning"
      }));
      return;
    }
    if (state.environment === "live" && !state.liveAcknowledged) {
      updateProviderInlineState(row.providerId, (current) => ({
        ...current,
        statusMessage: "Acknowledge live routing before saving live credentials.",
        statusDetails: [],
        statusTone: "warning"
      }));
      return;
    }

    updateProviderInlineState(row.providerId, (current) => ({
      ...current,
      busyAction: "save",
      statusMessage: null,
      statusDetails: [],
      statusTone: "default"
    }));

    try {
      const result = await putProviderCredentials(row.providerId, {
        credentials: definitions.reduce<Record<string, string | null>>((acc, definition) => {
          const value = (state.values[definition.field] ?? "").trim();
          acc[definition.field] = value.length > 0 ? value : null;
          return acc;
        }, {}),
        environment: state.environment,
        requestedBy: session?.displayName ?? "settings-screen"
      });
      await onRefresh?.();
      await onProviderRoutingRefresh?.();
      updateProviderInlineState(row.providerId, (current) => ({
        ...current,
        busyAction: null,
        dirty: false,
        editing: false,
        verificationFailed: false,
        statusMessage: `Credentials saved (${result.credentialState}).`,
        statusDetails: result.warnings ?? [],
        statusTone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Saving provider credentials failed.");
      updateProviderInlineState(row.providerId, (current) => ({
        ...current,
        busyAction: null,
        statusMessage: display.summary,
        statusDetails: display.details,
        statusTone: "danger"
      }));
    }
  };

  const runProviderVerification = async (providerId: string) => {
    updateProviderInlineState(providerId, (state) => ({
      ...state,
      busyAction: "verify",
      statusMessage: null,
      statusDetails: [],
      statusTone: "default"
    }));
    try {
      const result = await verifyProviderConnection(providerId);
      await onRefresh?.();
      await onProviderRoutingRefresh?.();
      updateProviderInlineState(providerId, (state) => ({
        ...state,
        busyAction: null,
        verificationFailed: !result.success,
        statusMessage: result.success ? "Verification succeeded." : "Verification failed.",
        statusDetails: [
          result.lastError,
          ...(result.warnings ?? [])
        ].filter((value): value is string => Boolean(value)),
        statusTone: result.success ? "success" : "danger"
      }));
    } catch (error) {
      const display = describeApiError(error, "Provider verification failed.");
      updateProviderInlineState(providerId, (state) => ({
        ...state,
        busyAction: null,
        verificationFailed: true,
        statusMessage: display.summary,
        statusDetails: display.details,
        statusTone: "danger"
      }));
    }
  };

  const clearProviderCredentials = async (providerId: string) => {
    updateProviderInlineState(providerId, (state) => ({
      ...state,
      busyAction: "clear",
      statusMessage: null,
      statusDetails: [],
      statusTone: "default"
    }));
    try {
      const result = await deleteProviderCredentials(providerId);
      await onRefresh?.();
      await onProviderRoutingRefresh?.();
      updateProviderInlineState(providerId, (state) => ({
        ...state,
        busyAction: null,
        dirty: false,
        values: { ...emptyProviderInlineValues },
        statusMessage: "Credentials cleared.",
        statusDetails: result.warnings ?? [],
        statusTone: "success",
        verificationFailed: result.verificationState === "Failed"
      }));
    } catch (error) {
      const display = describeApiError(error, "Clearing provider credentials failed.");
      updateProviderInlineState(providerId, (state) => ({
        ...state,
        busyAction: null,
        statusMessage: display.summary,
        statusDetails: display.details,
        statusTone: "danger"
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
    if (!profileBackedSecurity.displayName.trim() || !profileBackedSecurity.internalCode.trim() || missingFields.length > 0) {
      setProfileBackedSecurity((current) => ({
        ...current,
        message: "Display name, internal code, and required profile fields are required.",
        details: missingFields,
        tone: "warning"
      }));
      return;
    }

    setProfileBackedSecurity((current) => ({ ...current, busy: true, message: null, details: [], tone: "default" }));
    try {
      const securityId = createBrowserGuid();
      const effectiveFrom = new Date().toISOString();
      const profileFields = buildProfileFieldPayload(selected.fields, profileBackedSecurity.fieldValues);
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
            approvalReference: `profile:${selected.profileId}:v${selected.version}`
          },
          evidenceLinks: []
        },
        identifiers: [
          {
            kind: "InternalCode",
            value: profileBackedSecurity.internalCode.trim(),
            isPrimary: true,
            validFrom: effectiveFrom
          }
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
    <div className="space-y-8">
      <section
        id="settings-overview"
        role="region"
        aria-label="Settings workbench context"
        className="panel-surface-strong flex flex-wrap items-center justify-between gap-3 px-4 py-4"
      >
        <div className="min-w-0">
          <div className="eyebrow-label">Settings lane</div>
          <h2 className="mt-2 font-display text-[1.375rem] font-semibold leading-tight text-foreground">
            Operator control posture
          </h2>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">
            Session context, bootstrap health, and diagnostic reachability stay visible from one operator-facing
            control surface.
          </p>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2">
          {vm.headerChips.map((chip) => (
            <SettingsChip key={chip.label} label={chip.label} value={chip.value} />
          ))}
        </div>
      </section>

      <WorkspaceFilterBar
        label="Settings task navigator"
        searchLabel="Settings tasks"
        searchValue={settingsTaskViews.find((view) => view.id === activeTaskView)?.label ?? "Overview"}
        fields={settingsTaskFields}
        actions={
          <WorkspaceTabStrip
            label="Settings sub-task screens"
            tabs={settingsTaskTabItems}
          />
        }
      />

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
                <div className="eyebrow-label">Profile and authentication</div>
                <CardTitle className="mt-2 flex items-center gap-2">
                  <User className="h-5 w-5 text-primary" />
                  {vm.profileAuthenticationPanel.title}
                </CardTitle>
                <CardDescription className="mt-2">{vm.profileAuthenticationPanel.summary}</CardDescription>
              </div>
              <Badge
                variant={vm.profileAuthenticationPanel.badgeVariant}
                dot={vm.profileAuthenticationPanel.statusTone === "success"}
              >
                {vm.profileAuthenticationPanel.statusLabel}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-4 lg:grid-cols-[minmax(0,0.78fr)_minmax(0,1fr)]">
              <div className="rounded-md border border-border/70 bg-background/35 px-4 py-4">
                <div className="flex items-start gap-3">
                  <div
                    aria-hidden="true"
                    className="grid h-12 w-12 shrink-0 place-items-center rounded-md border border-primary/35 bg-primary/12 font-mono text-sm font-semibold text-primary"
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
                <dl className="grid gap-2 sm:grid-cols-2" aria-label="Profile authentication facts">
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

            <div role="list" aria-label={vm.profileAuthenticationPanel.stepsAriaLabel} className="grid gap-2">
              <h3 className="text-xs font-semibold uppercase text-muted-foreground">
                {vm.profileAuthenticationPanel.stepsTitle}
              </h3>
              {vm.profileAuthenticationPanel.steps.map((step) => (
                <ProfileAuthenticationStepRow key={step.id} step={step} />
              ))}
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
              <Badge variant={systemVariant(vm.systemTone)} dot={vm.systemTone === "success"}>
                {overview?.systemStatus ?? "Unavailable"}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap gap-2">
              <SettingsChip label="Providers" value={overview ? `${overview.providersOnline} / ${overview.providersTotal}` : "—"} />
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

          <div role="list" aria-label="Scoped access assignments" className="grid gap-2">
            {scopedAccessAssignments.length > 0 ? (
              scopedAccessAssignments.map((assignment) => (
                <article
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
                      onClick={() => void revokeScopedAccess(assignment)}
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
                    <SettingsFieldRow label="Role" value={assignment.roleProfileName ?? assignment.role} tone="default" />
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
                        {permission}
                      </Badge>
                    ))}
                  </div>
                  {assignment.revocationReason ? (
                    <p className="text-xs leading-5 text-muted-foreground">
                      Revoked by {assignment.revokedBy ?? "unknown"}: {assignment.revocationReason}
                    </p>
                  ) : null}
                </article>
              ))
            ) : (
              <p className="rounded-md border border-border/70 bg-secondary/25 px-4 py-4 text-center text-sm text-muted-foreground">
                No scoped access assignments are loaded for the selected filter.
              </p>
            )}
          </div>

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
              <Badge
                variant={vm.operationsControlCenter.statusVariant}
                dot={vm.operationsControlCenter.statusVariant === "success"}
              >
                {vm.operationsControlCenter.statusLabel}
              </Badge>
            </div>
          </div>
        </CardHeader>
        <CardContent className="grid gap-4">
          <div className="grid gap-3 xl:grid-cols-4" role="list" aria-label={vm.operationsControlCenter.listLabel}>
            {vm.operationsControlCenter.cards.map((card) => (
              <article
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
                  <Badge variant={card.statusVariant} className="shrink-0">
                    {card.statusLabel}
                  </Badge>
                </div>
                <dl className="grid gap-2 sm:grid-cols-2 xl:grid-cols-1 2xl:grid-cols-2">
                  {card.metrics.map((metric) => (
                    <SettingsFieldRow key={`${card.id}-${metric.label}`} label={metric.label} value={metric.value} tone={metric.tone} />
                  ))}
                </dl>
                <p className="text-xs leading-5 text-foreground/75">{card.detail}</p>
                <div className="mt-auto flex flex-wrap gap-2">
                  {card.routeHref.startsWith("/api/") ? (
                    <Button asChild variant="outline" size="sm">
                      <a href={card.routeHref} target="_blank" rel="noreferrer" aria-label={card.routeAriaLabel}>
                        {card.routeLabel}
                        <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
                      </a>
                    </Button>
                  ) : (
                    <Button asChild variant="outline" size="sm">
                      <Link to={card.routeHref} aria-label={card.routeAriaLabel}>
                        {card.routeLabel}
                        <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                      </Link>
                    </Button>
                  )}
                  <a
                    href={card.endpointHref}
                    target="_blank"
                    rel="noreferrer"
                    className="inline-flex items-center gap-2 rounded-md border border-border/60 px-2 py-1 text-[11px] font-mono text-muted-foreground transition-colors hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40"
                    aria-label={`Open service details for ${card.title}`}
                  >
                    GET
                    <ExternalLink className="h-3 w-3" aria-hidden="true" />
                  </a>
                </div>
              </article>
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
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Required permission
                <Input
                  value={approvalPolicyRule.requiredPermission}
                  onChange={(event) => setApprovalPolicyRule((current) => ({ ...current, requiredPermission: event.target.value, message: null }))}
                  disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
                  aria-label="Approval policy required permission"
                />
              </label>
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
              <SettingsChip label="Projected" value={vm.assetProfileGovernancePanel.projectedFieldCountLabel} />
              <SettingsChip label="Close IDs" value={vm.assetProfileGovernancePanel.closeIdentifierCountLabel} />
              <Badge variant={vm.assetProfileGovernancePanel.statusVariant} dot={vm.assetProfileGovernancePanel.statusVariant === "success"}>
                {vm.assetProfileGovernancePanel.statusLabel}
              </Badge>
            </div>
          </div>
        </CardHeader>
        <CardContent className="grid gap-4">
          <div className="grid gap-3 xl:grid-cols-5" role="list" aria-label={vm.assetProfileGovernancePanel.listLabel}>
            {vm.assetProfileGovernancePanel.rows.map((row) => (
              <article
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
              </article>
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
                <Button type="button" variant="outline" size="sm" onClick={rollbackAssetProfile} disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null} busy={assetProfileDraft.busyAction === "rollback"} busyLabel="Rolling back">
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
                {inlineProviderManagementEnabled
                  ? " Inline credential editing, verification, and runtime impact checks are enabled."
                  : " Inline editing is disabled by capability flag."}
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
              <Badge
                variant={vm.providerConnectionCenter.statusVariant}
                dot={vm.providerConnectionCenter.statusVariant === "success"}
              >
                {vm.providerConnectionCenter.statusLabel}
              </Badge>
            </div>
          </div>
        </CardHeader>
        <CardContent className="grid gap-4">
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
          <div className="grid gap-4 xl:grid-cols-2">
          {filteredProviderGroups.map((group) => (
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
                      {inlineProviderManagementEnabled ? (
                        <ProviderInlineActionPanel
                          row={row}
                          state={providerInlineState[row.providerId] ?? createProviderInlineState(row)}
                          fieldDefinitions={providerFieldDefinitions[row.providerId] ?? buildProviderFieldDefinitions(row)}
                          onToggleEdit={() => toggleProviderEdit(row.providerId)}
                          onFieldChange={(field, value) => updateProviderField(row.providerId, field, value)}
                          onEnvironmentChange={(value) => updateProviderEnvironment(row.providerId, value)}
                          onLiveAcknowledgementChange={(value) => updateProviderLiveAcknowledgement(row.providerId, value)}
                          onTest={() => void runProviderTest(row.providerId)}
                          onSave={() => void saveProviderDraft(row)}
                          onVerify={() => void runProviderVerification(row.providerId)}
                          onClear={() => void clearProviderCredentials(row.providerId)}
                        />
                      ) : null}
                      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
                        <div className="min-w-0">
                          <div className="flex flex-wrap items-center gap-2">
                            <h4 className="text-sm font-semibold text-foreground">{row.displayName}</h4>
                            <Badge variant="outline">{row.capabilityLabel}</Badge>
                            <Badge variant={toneVariant(row.healthTone)} dot={row.healthTone === "success"}>
                              {row.healthLabel}
                            </Badge>
                            {inlineProviderManagementEnabled ? (
                              <Badge variant={providerDraftStatusVariant(providerInlineState[row.providerId], row)}>
                                {providerDraftStatusLabel(providerInlineState[row.providerId], row)}
                              </Badge>
                            ) : null}
                          </div>
                          <p className="mt-2 text-xs leading-5 text-muted-foreground">{row.recommendedAction}</p>
                        </div>
                        <div className="flex flex-wrap items-center gap-2">
                          <Button asChild variant="outline" size="sm" className="shrink-0">
                            <Link to={row.actionHref} aria-label={row.actionAriaLabel}>
                              {row.actionLabel}
                              <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                            </Link>
                          </Button>
                        </div>
                      </div>
                      <dl className="mt-3 grid gap-2 sm:grid-cols-2">
                        <SettingsFieldRow label="Credential" value={row.credentialLabel} tone={row.credentialTone} />
                        <SettingsFieldRow label="Verification" value={row.verificationLabel} tone={row.credentialTone} />
                        <SettingsFieldRow label="Source" value={row.sourceLabel} tone="muted" />
                        <SettingsFieldRow label="Environment" value={row.environmentLabel} tone="muted" />
                        <SettingsFieldRow label="Masked key" value={row.maskedKeyPreviewLabel} tone="muted" />
                        <SettingsFieldRow label="Last good heartbeat" value={row.lastHeartbeatLabel} tone="muted" />
                        <SettingsFieldRow label="Failover" value={row.fallbackLabel} tone={row.fallbackLabel === "Fallback active" ? "warning" : "muted"} />
                        <SettingsFieldRow label="Routing bindings" value={row.routingBindingsLabel} tone="muted" />
                        <SettingsFieldRow label="Trust score" value={row.trustScoreLabel} tone={row.healthTone} />
                        <SettingsFieldRow label="Production gate" value={row.productionStateLabel} tone={row.productionStateLabel === "Production ready" ? "success" : "warning"} />
                        <SettingsFieldRow label="Affected workflows" value={row.affectedWorkflowsLabel} tone="default" />
                      </dl>
                      <ProviderIntegrationRuntimePanel
                        row={row}
                        state={providerRuntimeState[providerRuntimeStateKey(row)] ?? emptyProviderRuntimeEvidenceState}
                        operatorName={session?.displayName ?? "settings-operator"}
                        openApiImportState={providerOpenApiImportState[providerRuntimeStateKey(row)] ?? createProviderOpenApiImportState(row)}
                        onOpenApiImportStateChange={(updater) => updateProviderOpenApiImportState(row, updater)}
                        onImportOpenApi={() => void submitProviderOpenApiImport(row)}
                        onLoad={() => void loadProviderRuntimeEvidence(row)}
                        onRunDueSync={() => void runProviderRuntimeDueSync(row)}
                        onCreateHandoff={() => void createProviderRuntimeHandoff(row)}
                        onReplayQuarantine={() => void replayProviderRuntimeQuarantine(row)}
                        onResolveQuarantineRecord={(record, action) => void resolveProviderRuntimeQuarantineRecord(row, record, action)}
                      />
                      {inlineProviderManagementEnabled ? (
                        <ProviderReadinessChecklist
                          row={row}
                          state={providerInlineState[row.providerId] ?? createProviderInlineState(row)}
                          fieldDefinitions={providerFieldDefinitions[row.providerId] ?? buildProviderFieldDefinitions(row)}
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

      {showBrokerageSection ? (
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
            <Badge variant={vm.alpacaConnectionPanel.badgeVariant} dot={vm.alpacaConnectionPanel.statusTone === "success"}>
              {vm.alpacaConnectionPanel.stateLabel}
            </Badge>
          </div>
        </CardHeader>
        <CardContent className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(280px,0.8fr)]">
          <form className="grid gap-3" onSubmit={alpacaForm.connect} noValidate aria-describedby={alpacaForm.formPanelId}>
            <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_10rem]">
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
              <fieldset className="grid gap-1 text-xs font-medium text-muted-foreground">
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
                      <span className="break-all font-mono text-[10px] font-normal leading-4 text-muted-foreground">
                        {option.endpointLabel}
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
                <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_auto] lg:items-start">
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
                variant="outline"
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
              <SettingsFieldRow label="Verified" value={vm.alpacaConnectionPanel.verifiedAtLabel} tone="muted" />
            </dl>
            {vm.alpacaConnectionPanel.warnings.length > 0 ? (
              <div className="rounded-md border border-warning/35 bg-warning/10 px-3 py-2 text-xs leading-5 text-warning">
                {vm.alpacaConnectionPanel.warnings[0]}
              </div>
            ) : null}
            <div
              role="list"
              aria-label={vm.alpacaConnectionPanel.setupChecklistAriaLabel}
              className="grid gap-2"
            >
              <div className="min-w-0">
                <h3 className="text-xs font-semibold uppercase text-muted-foreground">
                  {vm.alpacaConnectionPanel.setupChecklistTitle}
                </h3>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  {vm.alpacaConnectionPanel.setupChecklistDetail}
                </p>
              </div>
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
                    <Badge variant={step.badgeVariant} className="shrink-0">
                      {step.statusLabel}
                    </Badge>
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
        </CardContent>
      </Card>
      ) : null}

      {showBrokerageSection ? (
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
            <Badge variant={vm.robinhoodConnectionPanel.badgeVariant} dot={vm.robinhoodConnectionPanel.statusTone === "success"}>
              {vm.robinhoodConnectionPanel.stateLabel}
            </Badge>
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
              <Badge variant={recentEventsVariant(vm.recentEventsSection.state)} dot={vm.recentEventsSection.state === "ready"}>
                {vm.recentEventsSection.statusLabel}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap gap-2">
              <SettingsChip label="Count" value={vm.recentEventsSection.countLabel} />
              <SettingsChip label="Heartbeat" value={overview?.lastHeartbeatUtc ?? "—"} />
              <SettingsChip label="Stream" value={vm.recentEventsSection.state} />
            </div>
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
                  Diagnostic services
                </CardTitle>
                <CardDescription className="mt-2">{vm.diagnosticSummary}</CardDescription>
              </div>
              <Badge variant={vm.diagnosticStatusVariant} dot={vm.diagnosticStatusVariant === "success"}>
                {vm.diagnosticStatusLabel}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap gap-2">
              <SettingsChip label="Loaded" value={vm.diagnosticCounts.loadedLabel} />
              <SettingsChip label="Failed" value={vm.diagnosticCounts.failedLabel} />
              <SettingsChip label="Checking" value={vm.diagnosticCounts.checkingLabel} />
            </div>
            <div className="grid gap-3 md:grid-cols-2" role="list" aria-label={vm.diagnosticListLabel}>
              {vm.diagnosticLinks.map((link) => (
                <div key={link.href} role="listitem">
                  <a
                    href={link.href}
                    target="_blank"
                    rel="noreferrer"
                    aria-label={link.ariaLabel}
                    className={cn(
                      "group flex h-full flex-col gap-2 rounded-lg border px-4 py-3 transition-colors hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40",
                      diagnosticToneClass[link.tone]
                    )}
                  >
                    <div className="flex items-center justify-between gap-2">
                      <span className="font-semibold text-foreground transition-colors group-hover:text-primary">
                        {link.label}
                      </span>
                      <span className="inline-flex items-center gap-2">
                        <Badge variant={link.badgeVariant} className="shrink-0">
                          {link.statusLabel}
                        </Badge>
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
          </CardContent>
        </Card>
      </section>
      ) : null}

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
            <Badge variant={vm.runtimeCapabilitySection.statusVariant} dot={vm.runtimeCapabilitySection.statusVariant === "success"}>
              {vm.runtimeCapabilitySection.statusLabel}
            </Badge>
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
                    <Badge variant={capability.statusVariant} className="shrink-0">
                      {capability.statusLabel}
                    </Badge>
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
                        void onFeatureCapabilityToggle?.(capability.capabilityKey, checked);
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
            <Badge variant={vm.backendCapabilityStatusVariant} dot={vm.backendCapabilityStatusVariant === "success"}>
              {vm.backendCapabilityStatusLabel}
            </Badge>
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
                  <Badge variant={group.statusVariant} className="shrink-0">
                    {group.statusLabel}
                  </Badge>
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
                      className="flex min-w-0 items-start gap-2 rounded-md border border-border/60 bg-background/45 px-3 py-2 text-xs transition-colors hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40"
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
        <span className="mt-1 inline-flex rounded-sm border border-border/60 px-1.5 py-0.5 text-[10px] uppercase text-muted-foreground">
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
        <Badge variant={step.badgeVariant} className="shrink-0">
          {step.statusLabel}
        </Badge>
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
                <div className="eyebrow-label">{detail.eyebrow}</div>
                <h3 className="mt-2 break-words text-sm font-semibold text-foreground">{detail.title}</h3>
                <p className="mt-1 break-words font-mono text-xs text-muted-foreground">{detail.subtitle}</p>
              </div>
              <Badge variant={detail.statusVariant} className="shrink-0">
                {detail.statusLabel}
              </Badge>
            </div>
            <p className="text-sm leading-6 text-muted-foreground">{detail.description}</p>
            <dl className="grid gap-2 sm:grid-cols-2 xl:grid-cols-1 2xl:grid-cols-2">
              {detail.fields.map((field) => (
                <div key={field.label} className="rounded-sm border border-border/60 bg-background/35 px-2.5 py-2">
                  <dt className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{field.label}</dt>
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

function SettingsFieldRow({
  label,
  value,
  tone
}: {
  label: string;
  value: string;
  tone: keyof typeof itemToneClass;
}) {
  return (
    <div className="grid grid-cols-[minmax(0,0.7fr)_minmax(0,1fr)] items-start gap-3 rounded-md border border-border/60 bg-secondary/25 px-3 py-2">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className={cn("text-right font-mono text-xs", itemToneClass[tone])}>{value}</dd>
    </div>
  );
}

function buildLedgerMappingAssignmentDraft(workbench: LedgerMappingWorkbench | null): {
  canAssign: boolean;
  statusLabel: string;
  disabledReason: string | null;
  accountOptions: Array<{ value: string; label: string }>;
  ledgerGroupOptions: Array<{ value: string; label: string }>;
} {
  if (!workbench) {
    return {
      canAssign: false,
      statusLabel: "Workbench unavailable",
      disabledReason: "Ledger mapping workbench data has not loaded.",
      accountOptions: [],
      ledgerGroupOptions: []
    };
  }

  const accountOptions = workbench.accounts
    .filter((account) => account.mapping.requiresUserMapping)
    .map((account) => ({
      value: account.accountId,
      label: `${account.accountCode} - ${account.displayName}`
    }));
  const ledgerGroupOptions = workbench.ledgerGroups
    .filter((group) => group.ledgerGroupId.toLowerCase() !== "unassigned")
    .map((group) => ({
      value: group.ledgerGroupId,
      label: group.displayName ? `${group.displayName} (${group.ledgerGroupId})` : group.ledgerGroupId
    }));

  if (accountOptions.length === 0) {
    return {
      canAssign: false,
      statusLabel: "No unmapped accounts",
      disabledReason: "All accounts in the loaded workbench already have ledger mappings.",
      accountOptions,
      ledgerGroupOptions
    };
  }

  if (ledgerGroupOptions.length === 0) {
    return {
      canAssign: false,
      statusLabel: "No ledger groups",
      disabledReason: "Create or load at least one ledger group before assigning an account mapping.",
      accountOptions,
      ledgerGroupOptions
    };
  }

  return {
    canAssign: true,
    statusLabel: `${accountOptions.length} ready`,
    disabledReason: null,
    accountOptions,
    ledgerGroupOptions
  };
}

function buildRolePermissionProfileDraft(catalog: RolePermissionCatalog | null): {
  canSave: boolean;
  statusLabel: string;
  disabledReason: string | null;
  baseRoleOptions: Array<{ value: string; label: string }>;
  permissionOptions: Array<{ value: string; label: string; group: string }>;
  defaultBaseRole: string;
  defaultPermissionNames: string[];
} {
  if (!catalog) {
    return {
      canSave: false,
      statusLabel: "Catalog unavailable",
      disabledReason: "Role and permission catalog data has not loaded.",
      baseRoleOptions: [],
      permissionOptions: [],
      defaultBaseRole: "",
      defaultPermissionNames: []
    };
  }

  const builtInRoles = catalog.roles.filter((role) => role.isBuiltIn);
  const defaultRole = builtInRoles.find((role) => role.role === "Accounting") ?? builtInRoles[0];
  const baseRoleOptions = builtInRoles.map((role) => ({
    value: role.role,
    label: `${role.displayName} (${role.role})`
  }));
  const permissionOptions = catalog.permissions.map((permission) => ({
    value: permission.name,
    label: permission.name,
    group: permission.group
  }));

  if (baseRoleOptions.length === 0) {
    return {
      canSave: false,
      statusLabel: "No base roles",
      disabledReason: "At least one built-in base role is required before creating a custom profile.",
      baseRoleOptions,
      permissionOptions,
      defaultBaseRole: "",
      defaultPermissionNames: []
    };
  }

  if (permissionOptions.length === 0) {
    return {
      canSave: false,
      statusLabel: "No permissions",
      disabledReason: "At least one permission is required before creating a custom profile.",
      baseRoleOptions,
      permissionOptions,
      defaultBaseRole: defaultRole?.role ?? "",
      defaultPermissionNames: []
    };
  }

  return {
    canSave: true,
    statusLabel: `${permissionOptions.length} grants`,
    disabledReason: null,
    baseRoleOptions,
    permissionOptions,
    defaultBaseRole: defaultRole?.role ?? baseRoleOptions[0]?.value ?? "",
    defaultPermissionNames: defaultRole?.permissions ?? []
  };
}

function buildApprovalPolicyRuleDraft(matrix: OperationsApprovalPolicyMatrix | null): {
  canSave: boolean;
  statusLabel: string;
  disabledReason: string | null;
  rows: OperationsApprovalPolicyMatrixRow[];
  policyOptions: Array<{ value: string; label: string }>;
} {
  if (!matrix) {
    return {
      canSave: false,
      statusLabel: "Matrix unavailable",
      disabledReason: "Approval policy matrix data has not loaded.",
      rows: [],
      policyOptions: []
    };
  }

  if (matrix.rows.length === 0) {
    return {
      canSave: false,
      statusLabel: "No rules",
      disabledReason: "At least one approval policy rule is required before configuration can be saved.",
      rows: [],
      policyOptions: []
    };
  }

  return {
    canSave: true,
    statusLabel: `${matrix.rows.length} rules`,
    disabledReason: null,
    rows: matrix.rows,
    policyOptions: matrix.rows.map((row) => ({
      value: row.policyKey,
      label: `${row.action} (${row.policyKey})`
    }))
  };
}

function buildCloseCalendarItemDraft(calendar: OperationsCloseCalendar | null): {
  canSave: boolean;
  statusLabel: string;
  disabledReason: string | null;
  items: OperationsCloseCalendarItem[];
  workflowOptions: Array<{ value: string; label: string }>;
} {
  if (!calendar) {
    return {
      canSave: false,
      statusLabel: "Calendar unavailable",
      disabledReason: "Account close calendar data has not loaded.",
      items: [],
      workflowOptions: []
    };
  }

  const configurableItems = calendar.items.filter((item) => item.nextDueTaskId && item.nextDueDate);
  if (configurableItems.length === 0) {
    return {
      canSave: false,
      statusLabel: "No open tasks",
      disabledReason: "At least one account close workflow with a next due task is required before the calendar can be configured.",
      items: [],
      workflowOptions: []
    };
  }

  return {
    canSave: true,
    statusLabel: `${configurableItems.length} workflows`,
    disabledReason: null,
    items: configurableItems,
    workflowOptions: configurableItems.map((item) => ({
      value: item.workflowId,
      label: `${item.periodId}: ${item.nextDueLabel ?? item.nextDueTaskId}`
    }))
  };
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
    return (
      <Checkbox
        checked={value === "true"}
        onCheckedChange={(checked) => onChange(checked ? "true" : "false")}
        className="min-h-16 rounded-md border border-border/60 bg-secondary/15 px-3 py-2 text-xs"
        disabled={disabled}
        label={`${field.label}${field.isRequired ? " *" : ""}`}
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

function createProfileBackedSecurityState(profile: SecurityAssetProfileDefinition | null): ProfileBackedSecurityState {
  return {
    profileId: profile?.profileId ?? "",
    displayName: "",
    internalCode: "",
    currency: "USD",
    fieldValues: profile ? buildProfileFieldValueState(profile, {}) : {},
    rationale: "Create profile-backed custom asset with approved Security Master profile version.",
    busy: false,
    message: null,
    details: [],
    tone: "default"
  };
}

function buildProfileFieldValueState(
  profile: SecurityAssetProfileDefinition,
  previous: Record<string, string>
): Record<string, string> {
  return Object.fromEntries(profile.fields.map((field) => [
    field.key,
    previous[field.key] ?? defaultProfileFieldValue(field)
  ]));
}

function defaultProfileFieldValue(field: SecurityAssetProfileFieldDefinition): string {
  if (field.fieldType === "Boolean") return "false";
  return "";
}

function buildProfileFieldPayload(
  fields: SecurityAssetProfileFieldDefinition[],
  values: Record<string, string>
): Record<string, unknown> {
  return fields.reduce<Record<string, unknown>>((acc, field) => {
    const raw = values[field.key]?.trim() ?? "";
    if (!raw && !field.isRequired) {
      return acc;
    }

    switch (field.fieldType) {
      case "Decimal":
        acc[field.key] = Number.parseFloat(raw);
        break;
      case "Integer":
        acc[field.key] = Number.parseInt(raw, 10);
        break;
      case "Boolean":
        acc[field.key] = raw === "true";
        break;
      default:
        acc[field.key] = raw;
        break;
    }
    return acc;
  }, {});
}

function normalizeAssetProfileId(value: string): string {
  return value.trim().toLowerCase().replace(/[^a-z0-9-]+/g, "-").replace(/^-+|-+$/g, "");
}

function todayDateOnly(): string {
  return new Date().toISOString().slice(0, 10);
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
    label: role.displayName ? `${role.displayName} (${role.role})` : role.role
  }));
}

function buildScopedAccessRoleProfileOptions(catalog: RolePermissionCatalog | null): Array<{ value: string; label: string }> {
  const profileOptions = (catalog?.roles ?? [])
    .filter((role) => !role.isBuiltIn)
    .map((role) => ({
      value: role.role,
      label: role.displayName ? `${role.displayName} (${role.role})` : role.role
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

  const amount = new Intl.NumberFormat("en-US", {
    maximumFractionDigits: 2
  }).format(assignment.approvalLimitAmount);
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

  return new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
    year: "numeric",
    timeZone: "UTC"
  }).format(parsed);
}

function FilterSelect({
  label,
  value,
  onChange,
  options,
  disabled = false
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  options: Array<{ value: string; label: string }>;
  disabled?: boolean;
}) {
  return (
    <label className="grid gap-1 text-xs font-medium text-muted-foreground">
      {label}
      <select
        value={value}
        onChange={(event) => onChange(event.target.value)}
        disabled={disabled}
        className="h-9 rounded-md border border-border/70 bg-background px-2 text-sm text-foreground"
        aria-label={label}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </label>
  );
}

function ProviderInlineActionPanel({
  row,
  state,
  fieldDefinitions,
  onToggleEdit,
  onFieldChange,
  onEnvironmentChange,
  onLiveAcknowledgementChange,
  onTest,
  onSave,
  onVerify,
  onClear
}: {
  row: {
    providerId: string;
    displayName: string;
    affectedWorkflowsLabel: string;
    productionStateLabel: string;
    fallbackLabel: string;
    environmentOptions: ProviderEnvironmentOption[];
  };
  state: ProviderInlineState;
  fieldDefinitions: ProviderInlineFieldDefinition[];
  onToggleEdit: () => void;
  onFieldChange: (field: ProviderInlineField, value: string) => void;
  onEnvironmentChange: (value: string) => void;
  onLiveAcknowledgementChange: (value: boolean) => void;
  onTest: () => void;
  onSave: () => void;
  onVerify: () => void;
  onClear: () => void;
}) {
  const busy = state.busyAction !== null;
  const environmentOptions = buildProviderEnvironmentOptions(row.environmentOptions, state.environment);
  return (
    <section className="mb-3 grid gap-3 rounded-md border border-border/70 bg-secondary/20 px-3 py-3" aria-label={`${row.displayName} inline provider actions`}>
      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={onToggleEdit}
          disabled={busy}
          aria-label={state.editing ? `Close ${row.displayName} editor` : `Edit ${row.displayName} credentials`}
        >
          {state.editing ? "Close editor" : "Edit"}
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={onTest}
          busy={state.busyAction === "test"}
          disabled={busy}
          busyLabel="Testing provider"
          aria-label={`Test ${row.displayName} connection`}
        >
          Test
        </Button>
        <Button
          type="button"
          size="sm"
          onClick={onSave}
          busy={state.busyAction === "save"}
          disabled={busy || (state.environment === "live" && !state.liveAcknowledged)}
          busyLabel="Saving draft"
          aria-label={`Save ${row.displayName} credentials`}
        >
          <Save className="h-3.5 w-3.5" aria-hidden="true" />
          Save
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={onVerify}
          busy={state.busyAction === "verify"}
          disabled={busy}
          busyLabel="Verifying provider"
          aria-label={`Re-verify ${row.displayName} connection`}
        >
          Re-verify
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={onClear}
          busy={state.busyAction === "clear"}
          disabled={busy}
          busyLabel="Clearing credentials"
          aria-label={`Clear ${row.displayName} credentials`}
        >
          <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
          Clear credentials
        </Button>
      </div>
      {state.editing ? (
        <div className="grid gap-3 md:grid-cols-2">
          {fieldDefinitions.length > 0 ? (
            fieldDefinitions.map((field) => (
              <label key={`${row.providerId}-${field.field}`} className="grid gap-1 text-xs font-medium text-muted-foreground">
                {field.label}
                <Input
                  type={field.type}
                  value={state.values[field.field] ?? ""}
                  onChange={(event) => onFieldChange(field.field, event.target.value)}
                  placeholder={field.placeholder}
                  autoComplete={field.type === "password" ? "new-password" : "off"}
                  disabled={busy}
                  aria-label={`${row.displayName} ${field.label}`}
                />
                <span className="text-[11px] leading-4 text-muted-foreground">{field.helpText}</span>
              </label>
            ))
          ) : (
            <div className="rounded-md border border-border/60 bg-background/40 px-3 py-2 text-xs leading-5 text-muted-foreground">
              No credential fields are required for this provider.
            </div>
          )}
          <label className="grid gap-1 text-xs font-medium text-muted-foreground">
            Environment
            <select
              value={state.environment}
              onChange={(event) => onEnvironmentChange(event.target.value)}
              className="h-9 rounded-md border border-border/70 bg-background px-2 text-sm text-foreground"
              disabled={busy}
              aria-label={`${row.displayName} environment`}
            >
              {environmentOptions.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
          {state.environment === "live" ? (
            <Checkbox
              checked={state.liveAcknowledged}
              onCheckedChange={onLiveAcknowledgementChange}
              className="rounded-md border border-live-env/35 bg-live-env/10 px-2 py-2 text-xs text-live-env"
              disabled={busy}
              label="I understand this save updates live provider routing credentials."
            />
          ) : null}
        </div>
      ) : null}
      <div className="rounded-sm border border-border/60 bg-background/40 px-2 py-2 text-xs text-muted-foreground">
        Impact summary: {row.affectedWorkflowsLabel} · Production gate {row.productionStateLabel} · Failover {row.fallbackLabel}
      </div>
      {state.testLatencyLabel ? <div className="text-xs text-muted-foreground">Latest test latency: {state.testLatencyLabel}</div> : null}
      {state.statusMessage ? (
        <div className={cn("text-xs", itemToneClass[state.statusTone])}>
          <div>{state.statusMessage}</div>
          {state.statusDetails.length > 0 ? (
            <ul className="mt-1 list-disc space-y-1 pl-4 text-[11px] text-muted-foreground">
              {state.statusDetails.map((detail) => <li key={detail}>{detail}</li>)}
            </ul>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}

function ProviderIntegrationRuntimePanel({
  row,
  state,
  operatorName,
  openApiImportState,
  onOpenApiImportStateChange,
  onImportOpenApi,
  onLoad,
  onRunDueSync,
  onCreateHandoff,
  onReplayQuarantine,
  onResolveQuarantineRecord
}: {
  row: SettingsProviderConnectionRow;
  state: ProviderRuntimeEvidenceState;
  operatorName: string;
  openApiImportState: ProviderOpenApiImportState;
  onOpenApiImportStateChange: (updater: (state: ProviderOpenApiImportState) => ProviderOpenApiImportState) => void;
  onImportOpenApi: () => void;
  onLoad: () => void;
  onRunDueSync: () => void;
  onCreateHandoff: () => void;
  onReplayQuarantine: () => void;
  onResolveQuarantineRecord: (
    record: ProviderIntegrationQuarantinedRecord,
    action: ProviderIntegrationQuarantineResolutionAction
  ) => void;
}) {
  const runs = providerRuntimeRuns(state);
  const latestRun = runs[0] ?? null;
  const issueGroups = state.quarantine?.issueGroups ?? [];
  const quarantineRecords = state.quarantine?.records ?? [];
  const quarantineDecisionCount = state.quarantine?.decisionedRecordCount ?? state.quarantine?.decisions.length ?? 0;
  const pendingReviewRecordCount = state.quarantine?.pendingReviewRecordCount ?? Math.max(quarantineRecords.length - quarantineDecisionCount, 0);
  const replayRequestedRecordCount = state.quarantine?.replayRequestedRecordCount ?? 0;
  const ignoredRecordCount = state.quarantine?.ignoredRecordCount ?? 0;
  const cashPositionCandidateCount = state.quarantine?.cashPositionCandidateCount ?? 0;
  const syncPlanItems = state.syncPlan?.items ?? [];
  const blockedSyncItems = state.syncPlan?.blockedCount ?? syncPlanItems.filter((item) => item.isBlocked).length;
  const dueSyncItems = state.syncPlan?.dueCount ?? syncPlanItems.filter((item) => item.isDue).length;
  const stagingReviewRows = state.staging?.records.length ?? 0;
  const identityReviewRequired = (state.identity?.accountReviewRequiredCount ?? 0) + (state.identity?.securityReviewRequiredCount ?? 0);
  const promotionReady = state.promotion?.readyForReconciliationCount ?? 0;
  const promotionBlocked = state.promotion?.blockedCount ?? 0;
  const handoffCount = state.handoff?.handoffCount ?? 0;
  const criticalIssueCount = providerRuntimeCriticalIssueCount(state);
  const warningIssueCount = providerRuntimeWarningIssueCount(state);
  const receivedCount = providerRuntimeReceivedCount(state, runs);
  const acceptedCount = providerRuntimeAcceptedCount(state, runs);
  const quarantinedCount = providerRuntimeQuarantinedCount(state, runs);
  const stagedCount = providerRuntimeStagedCount(state, runs);
  const durableQuarantinedCount = providerRuntimeDurableQuarantinedCount(state, runs);
  const totalRuns = state.syncRuns?.totalSyncRuns ?? runs.length;
  const returnedRuns = state.syncRuns?.returnedSyncRuns ?? runs.length;
  const loading = state.phase === "loading";
  const replayEligibleQuarantineRecords = quarantineRecords.filter((record) =>
    !providerRuntimeLatestQuarantineDecision(state.quarantine?.decisions, record)
  );
  const quarantineReplayCount = replayEligibleQuarantineRecords.length;
  const canReplayQuarantine = !loading && quarantineReplayCount > 0 && Boolean(state.monitor?.manifestId);
  const canRunDueSync = !loading && Boolean(state.syncPlan) && dueSyncItems > 0 && blockedSyncItems === 0;
  const alreadyHandedOff = new Set((state.handoff?.records ?? []).map((record) => record.stagingRecordId));
  const handoffReadyCount = (state.promotion?.rows ?? [])
    .filter((promotionRow) => promotionRow.status === "ReadyForReconciliation" && !alreadyHandedOff.has(promotionRow.stagingRecordId))
    .length;
  const canCreateHandoff = !loading && handoffReadyCount > 0;
  const actionLabel = state.phase === "loaded" || state.phase === "error" ? "Refresh runtime" : loading ? "Loading runtime" : "Load runtime";

  return (
    <section
      className={cn("mt-3 rounded-md border px-3 py-3", diagnosticToneClass[providerRuntimePanelTone(state)])}
      aria-label={`${row.displayName} provider integration runtime evidence`}
    >
      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <h5 className="text-xs font-semibold uppercase tracking-[0.12em] text-foreground">Runtime evidence</h5>
            <Badge variant={providerRuntimeStatusVariant(state)} dot={state.phase === "loaded"}>
              {providerRuntimeStatusLabel(state)}
            </Badge>
          </div>
          <p className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{row.integrationConnectionId}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="shrink-0"
            onClick={onRunDueSync}
            disabled={!canRunDueSync}
            disabledReason={!state.syncPlan ? "Load runtime evidence before running due sync." : blockedSyncItems > 0 ? "Blocked sync-plan items must be resolved before due sync." : dueSyncItems === 0 ? "No due sync-plan items are available." : undefined}
            aria-label={`Run due provider integration sync for ${row.displayName}`}
          >
            {loading ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> : <Activity className="h-3.5 w-3.5" aria-hidden="true" />}
            Run due sync
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="shrink-0"
            onClick={onCreateHandoff}
            disabled={!canCreateHandoff}
            disabledReason={!state.promotion ? "Load promotion readiness before creating a handoff." : handoffReadyCount === 0 ? "No unhanded promotion-ready staging rows are available." : undefined}
            aria-label={`Create reconciliation handoff for ${handoffReadyCount} provider integration staging rows for ${row.displayName}`}
          >
            {loading ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> : <GitBranch className="h-3.5 w-3.5" aria-hidden="true" />}
            Hand off ready
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="shrink-0"
            onClick={onReplayQuarantine}
            disabled={!canReplayQuarantine}
            aria-label={`Replay ${quarantineReplayCount} quarantined provider integration records for ${row.displayName}`}
          >
            {loading ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> : <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />}
            Replay quarantine
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="shrink-0"
            onClick={onLoad}
            disabled={loading}
            aria-label={`${state.phase === "loaded" || state.phase === "error" ? "Refresh" : "Load"} provider integration runtime evidence for ${row.displayName}`}
          >
            {loading ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> : <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />}
            {actionLabel}
          </Button>
        </div>
      </div>
      <ProviderOpenApiImportForm
        row={row}
        state={openApiImportState}
        onStateChange={onOpenApiImportStateChange}
        onSubmit={onImportOpenApi}
      />
      <ProviderIntegrationWorkbenchPanel
        row={row}
        runtimeState={state}
        operatorName={operatorName}
      />
      <dl className="mt-3 grid gap-2 sm:grid-cols-2">
        <SettingsFieldRow
          label="Last sync"
          value={formatProviderRuntimeUtcMinute(state.syncRuns?.latestStartedAt ?? latestRun?.startedAt)}
          tone={latestRun ? providerRuntimeRunTone(latestRun) : "muted"}
        />
        <SettingsFieldRow label="Sync runs" value={`${formatProviderRuntimeNumber(returnedRuns)} / ${formatProviderRuntimeNumber(totalRuns)}`} tone={returnedRuns > 0 ? "success" : "muted"} />
        <SettingsFieldRow label="Records received" value={formatProviderRuntimeNumber(receivedCount)} tone={receivedCount > 0 ? "success" : "muted"} />
        <SettingsFieldRow
          label="Accepted / quarantined"
          value={`${formatProviderRuntimeNumber(acceptedCount)} / ${formatProviderRuntimeNumber(quarantinedCount)}`}
          tone={quarantinedCount > 0 ? providerRuntimeIssueTone(criticalIssueCount, warningIssueCount) : acceptedCount > 0 ? "success" : "muted"}
        />
        <SettingsFieldRow label="Staged retained" value={formatProviderRuntimeNumber(stagedCount)} tone={stagedCount > 0 ? "success" : "muted"} />
        <SettingsFieldRow label="Quarantine retained" value={formatProviderRuntimeNumber(durableQuarantinedCount)} tone={durableQuarantinedCount > 0 ? providerRuntimeIssueTone(criticalIssueCount, warningIssueCount) : "muted"} />
        <SettingsFieldRow label="Decisioned records" value={formatProviderRuntimeNumber(quarantineDecisionCount)} tone={quarantineDecisionCount > 0 ? "success" : "muted"} />
        <SettingsFieldRow
          label="Review posture"
          value={`${formatProviderRuntimeNumber(pendingReviewRecordCount)} pending / ${formatProviderRuntimeNumber(replayRequestedRecordCount)} replay / ${formatProviderRuntimeNumber(ignoredRecordCount)} ignored / ${formatProviderRuntimeNumber(cashPositionCandidateCount)} cash`}
          tone={pendingReviewRecordCount > 0 ? "warning" : quarantineDecisionCount > 0 ? "success" : "muted"}
        />
        <SettingsFieldRow
          label="Quarantine groups"
          value={`${formatProviderRuntimeNumber(issueGroups.length)} groups`}
          tone={issueGroups.length > 0 ? providerRuntimeIssueTone(criticalIssueCount, warningIssueCount) : "muted"}
        />
        <SettingsFieldRow
          label="Issue counts"
          value={`${formatProviderRuntimeNumber(criticalIssueCount)} critical / ${formatProviderRuntimeNumber(warningIssueCount)} warning`}
          tone={providerRuntimeIssueTone(criticalIssueCount, warningIssueCount)}
        />
        <SettingsFieldRow
          label="Sync plan"
          value={`${formatProviderRuntimeNumber(dueSyncItems)} due / ${formatProviderRuntimeNumber(blockedSyncItems)} blocked`}
          tone={blockedSyncItems > 0 ? "danger" : dueSyncItems > 0 ? "warning" : state.syncPlan ? "success" : "muted"}
        />
        <SettingsFieldRow
          label="Staging review"
          value={`${formatProviderRuntimeNumber(stagingReviewRows)} rows`}
          tone={stagingReviewRows > 0 ? "success" : "muted"}
        />
        <SettingsFieldRow
          label="Identity review"
          value={`${formatProviderRuntimeNumber(identityReviewRequired)} review required`}
          tone={identityReviewRequired > 0 ? "warning" : state.identity ? "success" : "muted"}
        />
        <SettingsFieldRow
          label="Promotion readiness"
          value={`${formatProviderRuntimeNumber(promotionReady)} ready / ${formatProviderRuntimeNumber(promotionBlocked)} blocked`}
          tone={promotionBlocked > 0 ? "danger" : promotionReady > 0 ? "success" : state.promotion ? "muted" : "muted"}
        />
        <SettingsFieldRow
          label="Reconciliation handoffs"
          value={`${formatProviderRuntimeNumber(handoffCount)} handoffs`}
          tone={handoffCount > 0 ? "success" : "muted"}
        />
      </dl>
      {state.message ? (
        <div role={state.phase === "error" ? "alert" : "status"} className={cn("mt-3 text-xs", itemToneClass[providerRuntimeMessageTone(state)])}>
          <div>{state.message}</div>
          {state.details.length > 0 ? (
            <ul className="mt-1 list-disc space-y-1 pl-4 text-[11px] text-muted-foreground">
              {state.details.map((detail) => <li key={detail}>{detail}</li>)}
            </ul>
          ) : null}
        </div>
      ) : null}
      {state.phase === "idle" ? (
        <p className="mt-3 text-xs text-muted-foreground">No runtime evidence loaded.</p>
      ) : null}
      {runs.length > 0 ? (
        <div className="mt-3 grid gap-2" aria-label={`${row.displayName} recent provider integration sync runs`}>
          {runs.slice(0, 3).map((run) => (
            <div key={run.syncRunId} className="rounded-sm border border-border/60 bg-background/35 px-2 py-2">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="font-mono text-[11px] text-foreground">{run.syncRunId}</span>
                <Badge variant={providerRuntimeRunVariant(run)}>{run.status}</Badge>
              </div>
              <div className="mt-1 text-[11px] text-muted-foreground">
                {run.capability} · {formatProviderRuntimeUtcMinute(run.startedAt)} · {formatProviderRuntimeNumber(run.recordsAccepted)} accepted / {formatProviderRuntimeNumber(run.recordsQuarantined)} quarantined
              </div>
            </div>
          ))}
        </div>
      ) : null}
      {quarantineRecords.length > 0 ? (
        <div className="mt-3 grid gap-2" aria-label={`${row.displayName} provider integration quarantine records`}>
          {quarantineRecords.slice(0, 3).map((record) => {
            const latestDecision = providerRuntimeLatestQuarantineDecision(state.quarantine?.decisions, record);
            const hasRecordedDecision = Boolean(latestDecision);
            const supportsCashDecision = record.capability === "Positions";

            return (
              <div key={record.quarantineRecordId} className="rounded-sm border border-border/60 bg-background/35 px-2 py-2">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <Badge variant={providerRuntimeProcessingStatusVariant(record.status)}>{record.status}</Badge>
                      <span className="font-mono text-[11px] text-foreground">{record.quarantineRecordId}</span>
                    </div>
                    <p className="mt-1 text-[11px] leading-4 text-muted-foreground">
                      {record.capability} · {formatProviderRuntimeUtcMinute(record.createdAt)} · {formatProviderRuntimeNumber(record.validationErrors.length)} issues
                    </p>
                    {latestDecision ? (
                      <p className="mt-1 text-[11px] leading-4 text-success">
                        Decision: {providerRuntimeQuarantineActionLabel(latestDecision.action)} by {latestDecision.reviewedBy} · {formatProviderRuntimeUtcMinute(latestDecision.reviewedAt)}
                      </p>
                    ) : null}
                  </div>
                  <div className="flex flex-wrap justify-end gap-2">
                    {hasRecordedDecision ? (
                      <Badge variant="success">Decision recorded</Badge>
                    ) : (
                      <>
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          className="shrink-0"
                          onClick={() => onResolveQuarantineRecord(record, "ReviewOnly")}
                          disabled={loading}
                          aria-label={`Review quarantine record ${record.quarantineRecordId} for ${row.displayName}`}
                        >
                          {loading ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> : <MonitorCheck className="h-3.5 w-3.5" aria-hidden="true" />}
                          Review
                        </Button>
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          className="shrink-0"
                          onClick={() => onResolveQuarantineRecord(record, "ReplayAfterMappingChange")}
                          disabled={loading}
                          aria-label={`Mark quarantine record ${record.quarantineRecordId} for replay after mapping change for ${row.displayName}`}
                        >
                          {loading ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> : <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />}
                          Replay later
                        </Button>
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          className="shrink-0"
                          onClick={() => onResolveQuarantineRecord(record, "IgnoreProviderRecord")}
                          disabled={loading}
                          aria-label={`Ignore quarantine record ${record.quarantineRecordId} for ${row.displayName}`}
                        >
                          {loading ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> : <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />}
                          Ignore
                        </Button>
                        {supportsCashDecision ? (
                          <Button
                            type="button"
                            variant="outline"
                            size="sm"
                            className="shrink-0"
                            onClick={() => onResolveQuarantineRecord(record, "MarkAsCashPosition")}
                            disabled={loading}
                            aria-label={`Mark quarantine record ${record.quarantineRecordId} as cash position for ${row.displayName}`}
                          >
                            {loading ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> : <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />}
                            Mark cash
                          </Button>
                        ) : null}
                      </>
                    )}
                  </div>
                </div>
                {record.validationErrors[0] ? (
                  <p className="mt-1 text-[11px] leading-4 text-muted-foreground">{record.validationErrors[0].message}</p>
                ) : null}
              </div>
            );
          })}
        </div>
      ) : null}
      {issueGroups.length > 0 ? (
        <div className="mt-3 grid gap-2" aria-label={`${row.displayName} provider integration quarantine issue groups`}>
          {issueGroups.slice(0, 3).map((group) => (
            <div key={`${group.issueCode}-${group.targetField ?? "record"}`} className="rounded-sm border border-border/60 bg-background/35 px-2 py-2">
              <div className="flex flex-wrap items-center gap-2">
                <Badge variant={providerRuntimeSeverityVariant(group.severity)}>{group.severity}</Badge>
                <span className="font-mono text-[11px] text-foreground">{group.issueCode}</span>
                <span className="text-[11px] text-muted-foreground">{formatProviderRuntimeNumber(group.recordCount)} records</span>
              </div>
              <p className="mt-1 text-[11px] leading-4 text-muted-foreground">{group.message}</p>
            </div>
          ))}
        </div>
      ) : null}
      {syncPlanItems.length > 0 ? (
        <div className="mt-3 grid gap-2" aria-label={`${row.displayName} provider integration sync plan`}>
          {syncPlanItems.slice(0, 3).map((item) => (
            <div key={`${item.capability}-${item.endpointKey ?? "no-service"}`} className="rounded-sm border border-border/60 bg-background/35 px-2 py-2">
              <div className="flex flex-wrap items-center gap-2">
                <Badge variant={item.isBlocked ? "danger" : item.isDue ? "warning" : "success"}>
                  {item.isBlocked ? "Blocked" : item.isDue ? "Due" : "Scheduled"}
                </Badge>
                <span className="text-[11px] font-medium text-foreground">{item.capability}</span>
                <span className="font-mono text-[11px] text-muted-foreground">{item.endpointKey ?? "no service"}</span>
              </div>
              <p className="mt-1 text-[11px] leading-4 text-muted-foreground">{item.reason}</p>
            </div>
          ))}
        </div>
      ) : null}
      {state.promotion && state.promotion.rows.length > 0 ? (
        <div className="mt-3 grid gap-2" aria-label={`${row.displayName} provider integration promotion readiness rows`}>
          {state.promotion.rows.slice(0, 3).map((promotionRow) => (
            <div key={promotionRow.stagingRecordId} className="rounded-sm border border-border/60 bg-background/35 px-2 py-2">
              <div className="flex flex-wrap items-center gap-2">
                <Badge variant={providerRuntimePromotionVariant(promotionRow.status)}>{promotionRow.status}</Badge>
                <span className="font-mono text-[11px] text-foreground">{promotionRow.stagingRecordId}</span>
                <span className="text-[11px] text-muted-foreground">{promotionRow.promotionTarget}</span>
              </div>
              <p className="mt-1 text-[11px] leading-4 text-muted-foreground">
                {promotionRow.providerAccountId ?? "No provider account"} · {promotionRow.securityDisplayName ?? promotionRow.internalSecurityId ?? "No security match"}
              </p>
            </div>
          ))}
        </div>
      ) : null}
    </section>
  );
}

function ProviderIntegrationWorkbenchPanel({
  row,
  runtimeState,
  operatorName
}: {
  row: SettingsProviderConnectionRow;
  runtimeState: ProviderRuntimeEvidenceState;
  operatorName: string;
}) {
  const [state, setState] = useState<ProviderIntegrationWorkbenchState>(() => createProviderIntegrationWorkbenchState(row));

  useEffect(() => {
    setState(createProviderIntegrationWorkbenchState(row));
  }, [row.integrationConnectionId, row.providerId]);

  const busy = state.busyAction !== null;
  const manifestId = providerIntegrationWorkbenchManifestId(state, runtimeState);
  const connectionId = providerIntegrationWorkbenchConnectionId(row, state, runtimeState);
  const capabilityOptions = providerIntegrationWorkbenchCapabilities(state);
  const endpointOptions = providerIntegrationWorkbenchEndpoints(state, state.capability);
  const mappingPreview = providerIntegrationWorkbenchMappings(state, state.capability);
  const latestRawPayload = providerIntegrationLatestRawPayload(state, runtimeState);
  const canCheckDrift = Boolean(manifestId && connectionId && latestRawPayload && state.endpointKey.trim());
  const activationReady = state.readiness?.isReady ?? false;

  const updateField = <K extends keyof ProviderIntegrationWorkbenchState>(
    field: K,
    value: ProviderIntegrationWorkbenchState[K]
  ) => {
    setState((current) => ({
      ...current,
      [field]: value,
      message: null,
      details: [],
      tone: "default"
    }));
  };

  const loadTemplates = async () => {
    setState((current) => ({ ...current, busyAction: "templates", message: "Loading provider integration templates.", details: [], tone: "default" }));
    try {
      const templates = await getProviderIntegrationTemplates();
      setState((current) => ({
        ...current,
        templates,
        selectedManifestId: providerIntegrationDefaultSelectedManifestId(row, templates, current.selectedManifestId),
        busyAction: null,
        message: templates.length === 0 ? "No provider integration templates returned." : `${templates.length} provider integration templates loaded.`,
        details: templates.slice(0, 3).map((template) => `${template.displayName}: ${template.summary}`),
        tone: templates.length === 0 ? "warning" : "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Provider integration templates could not be loaded.");
      setState((current) => ({ ...current, busyAction: null, message: display.summary, details: display.details, tone: "danger" }));
    }
  };

  const useTemplate = async () => {
    const selectedManifestId = state.selectedManifestId.trim();
    if (!selectedManifestId) {
      setState((current) => ({ ...current, message: "Select or enter a manifest id before loading a template.", details: [], tone: "warning" }));
      return;
    }

    setState((current) => ({ ...current, busyAction: "template", message: `Loading template ${selectedManifestId}.`, details: [], tone: "default" }));
    try {
      const manifest = await getProviderIntegrationTemplate(selectedManifestId);
      const connection = createProviderIntegrationConnectionDraft(row, manifest, operatorName);
      setState((current) => providerIntegrationWorkbenchWithDraft(row, current, manifest, connection, {
        message: `Template ${manifest.manifestId} loaded into draft setup editor.`,
        details: providerIntegrationWorkbenchDraftDetails(manifest),
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Provider integration template could not be loaded.");
      setState((current) => ({ ...current, busyAction: null, message: display.summary, details: display.details, tone: "danger" }));
    }
  };

  const saveSetupDraft = async () => {
    const manifestDraft = parseProviderIntegrationWorkbenchJson<ProviderIntegrationManifest>(state.draftManifestJson, "Manifest draft JSON");
    const connectionDraft = parseProviderIntegrationWorkbenchJson<ProviderIntegrationConnection>(state.draftConnectionJson, "Connection draft JSON");
    if (manifestDraft.ok === false || connectionDraft.ok === false) {
      const details = [
        manifestDraft.ok === false ? manifestDraft.error : null,
        connectionDraft.ok === false ? connectionDraft.error : null
      ].filter((detail): detail is string => Boolean(detail));
      setState((current) => ({
        ...current,
        message: "Provider integration setup draft is not valid JSON.",
        details,
        tone: "warning"
      }));
      return;
    }

    const savedAt = new Date().toISOString();
    setState((current) => ({ ...current, busyAction: "save", message: "Saving provider integration setup draft.", details: [], tone: "default" }));
    try {
      const result = await saveProviderIntegrationSetup({
        manifest: manifestDraft.value,
        connection: connectionDraft.value,
        savedBy: operatorName,
        savedAt,
        changeReason: manifestDraft.value.changeReason ?? "Saved from the Settings Provider Connection Center guided workbench."
      });
      setState((current) => ({
        ...current,
        manifest: manifestDraft.value,
        connection: connectionDraft.value,
        selectedManifestId: result.manifestId,
        setupResult: result,
        readiness: result.readiness,
        busyAction: null,
        message: result.message ?? `Provider integration setup saved for ${result.connectionId}.`,
        details: providerIntegrationReadinessDetails(result.readiness),
        tone: result.readiness.isReady ? "success" : "warning"
      }));
    } catch (error) {
      const display = describeApiError(error, "Provider integration setup save failed.");
      setState((current) => ({ ...current, busyAction: null, message: display.summary, details: display.details, tone: "danger" }));
    }
  };

  const checkReadiness = async () => {
    if (!manifestId) {
      setState((current) => ({
        ...current,
        message: "Load a template, import a draft, or load runtime monitor evidence before checking activation readiness.",
        details: [],
        tone: "warning"
      }));
      return;
    }

    setState((current) => ({ ...current, busyAction: "readiness", message: "Checking provider integration activation readiness.", details: [], tone: "default" }));
    try {
      const readiness = await getProviderIntegrationReadiness(manifestId, connectionId);
      setState((current) => ({
        ...current,
        readiness,
        busyAction: null,
        message: readiness.isReady ? "Activation readiness passed." : "Activation readiness requires review.",
        details: providerIntegrationReadinessDetails(readiness),
        tone: readiness.isReady ? "success" : "warning"
      }));
    } catch (error) {
      const display = describeApiError(error, "Provider integration activation readiness could not be checked.");
      setState((current) => ({ ...current, busyAction: null, message: display.summary, details: display.details, tone: "danger" }));
    }
  };

  const runCsvDryRun = async () => {
    if (!manifestId || !state.csvContent.trim()) {
      setState((current) => ({ ...current, message: "Manifest id and CSV content are required before running a manual CSV dry-run.", details: [], tone: "warning" }));
      return;
    }

    const requestedAt = new Date();
    setState((current) => ({ ...current, busyAction: "csv-dry-run", message: "Running manual CSV provider integration dry-run.", details: [], tone: "default" }));
    try {
      const result = await runManualCsvProviderIntegrationDryRun({
        syncRunId: providerIntegrationWorkbenchSyncRunId(row.integrationConnectionId, "csv", requestedAt),
        manifestId,
        connectionId,
        capability: state.capability,
        fileName: state.csvFileName.trim() || `${row.providerId}-sample.csv`,
        csvContent: state.csvContent,
        requestedBy: operatorName,
        requestedAt: requestedAt.toISOString()
      });
      setState((current) => ({
        ...current,
        dryRunResult: result,
        busyAction: null,
        message: `CSV dry-run completed: ${result.recordsAccepted} accepted / ${result.recordsQuarantined} quarantined.`,
        details: result.issues.map((issue) => issue.message),
        tone: result.recordsQuarantined > 0 || result.issues.length > 0 ? "warning" : "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Manual CSV provider integration dry-run failed.");
      setState((current) => ({ ...current, busyAction: null, message: display.summary, details: display.details, tone: "danger" }));
    }
  };

  const runRestDryRun = async () => {
    const pathParameters = parseProviderIntegrationStringRecord(state.restPathParametersJson, "REST path parameters JSON");
    const queryParameters = parseProviderIntegrationStringRecord(state.restQueryParametersJson, "REST query parameters JSON");
    if (!manifestId || !state.endpointKey.trim() || pathParameters.ok === false || queryParameters.ok === false) {
      const details = [
        pathParameters.ok === false ? pathParameters.error : null,
        queryParameters.ok === false ? queryParameters.error : null
      ].filter((detail): detail is string => Boolean(detail));
      setState((current) => ({
        ...current,
        message: "Manifest id, service key, and valid REST parameter JSON are required before running a REST dry-run.",
        details,
        tone: "warning"
      }));
      return;
    }

    const requestedAt = new Date();
    setState((current) => ({ ...current, busyAction: "rest-dry-run", message: "Running REST provider integration dry-run.", details: [], tone: "default" }));
    try {
      const result = await runRestProviderIntegrationDryRun({
        syncRunId: providerIntegrationWorkbenchSyncRunId(row.integrationConnectionId, "rest", requestedAt),
        manifestId,
        connectionId,
        capability: state.capability,
        endpointKey: state.endpointKey.trim(),
        pathParameters: pathParameters.value,
        queryParameters: queryParameters.value,
        requestedBy: operatorName,
        requestedAt: requestedAt.toISOString(),
        maxPages: 2
      });
      setState((current) => ({
        ...current,
        dryRunResult: result,
        busyAction: null,
        message: `REST dry-run completed: ${result.recordsAccepted} accepted / ${result.recordsQuarantined} quarantined.`,
        details: result.issues.map((issue) => issue.message),
        tone: result.recordsQuarantined > 0 || result.issues.length > 0 ? "warning" : "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "REST provider integration dry-run failed.");
      setState((current) => ({ ...current, busyAction: null, message: display.summary, details: display.details, tone: "danger" }));
    }
  };

  const checkSchemaDrift = async () => {
    if (!manifestId || !latestRawPayload || !state.endpointKey.trim()) {
      setState((current) => ({ ...current, message: "A manifest id, service key, and dry-run or runtime sample are required before schema drift review.", details: [], tone: "warning" }));
      return;
    }

    const checkedAt = new Date();
    setState((current) => ({ ...current, busyAction: "drift", message: "Checking provider integration schema drift.", details: [], tone: "default" }));
    try {
      const result = await checkProviderIntegrationSchemaDrift({
        manifestId,
        connectionId,
        capability: latestRawPayload.capability,
        endpointKey: state.endpointKey.trim(),
        syncRunId: latestRawPayload.syncRunId,
        rawPayloadId: latestRawPayload.rawPayloadId,
        checkedBy: operatorName,
        checkedAt: checkedAt.toISOString()
      });
      setState((current) => ({
        ...current,
        driftResult: result,
        busyAction: null,
        message: result.driftDetected ? "Schema drift detected." : "Schema drift check passed.",
        details: result.issues.map((issue) => issue.message),
        tone: result.shouldPauseCapability ? "danger" : result.driftDetected ? "warning" : "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Provider integration schema drift check failed.");
      setState((current) => ({ ...current, busyAction: null, message: display.summary, details: display.details, tone: "danger" }));
    }
  };

  const activateSetup = async () => {
    if (!manifestId || !activationReady) {
      setState((current) => ({ ...current, message: "Activation is blocked until readiness passes.", details: providerIntegrationReadinessDetails(current.readiness), tone: "warning" }));
      return;
    }

    const approvedAt = new Date();
    setState((current) => ({ ...current, busyAction: "activate", message: "Requesting provider integration activation.", details: [], tone: "default" }));
    try {
      const result = await activateProviderIntegration({
        manifestId,
        connectionId,
        approvedBy: operatorName,
        approvedAt: approvedAt.toISOString(),
        approvalEvidenceId: providerIntegrationWorkbenchEvidenceId(row.integrationConnectionId, "activation", approvedAt),
        changeReason: "Activated from the Settings Provider Connection Center guided workbench."
      });
      setState((current) => ({
        ...current,
        activationResult: result,
        readiness: result.readiness,
        busyAction: null,
        message: result.message ?? `Provider integration ${result.activated ? "activated" : "activation reviewed"}.`,
        details: providerIntegrationReadinessDetails(result.readiness),
        tone: result.activated ? "success" : "warning"
      }));
    } catch (error) {
      const display = describeApiError(error, "Provider integration activation failed.");
      setState((current) => ({ ...current, busyAction: null, message: display.summary, details: display.details, tone: "danger" }));
    }
  };

  return (
    <section className="mt-3 rounded-md border border-border/60 bg-background/35 px-3 py-3" aria-label={`${row.displayName} guided provider integration workbench`}>
      <div className="flex flex-col gap-2 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <h6 className="text-xs font-semibold uppercase tracking-[0.12em] text-foreground">Guided integration workbench</h6>
          <p className="mt-1 text-[11px] leading-4 text-muted-foreground">Template, setup, dry-run, readiness, drift, and quarantine evidence stay on shared provider-integration endpoints.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button type="button" variant="outline" size="sm" onClick={() => void loadTemplates()} disabled={busy} busy={state.busyAction === "templates"} aria-label={`Load provider integration templates for ${row.displayName}`}>
            <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
            Load templates
          </Button>
          <Button type="button" variant="outline" size="sm" onClick={() => void useTemplate()} disabled={busy || !state.selectedManifestId.trim()} busy={state.busyAction === "template"} aria-label={`Use selected provider integration template for ${row.displayName}`}>
            <Save className="h-3.5 w-3.5" aria-hidden="true" />
            Use template
          </Button>
          <Button type="button" variant="outline" size="sm" onClick={() => void checkReadiness()} disabled={busy || !manifestId} busy={state.busyAction === "readiness"} aria-label={`Check provider integration activation readiness for ${row.displayName}`}>
            <MonitorCheck className="h-3.5 w-3.5" aria-hidden="true" />
            Check readiness
          </Button>
        </div>
      </div>
      <div className="mt-3 grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Template or manifest ID
          <Input value={state.selectedManifestId} onChange={(event) => updateField("selectedManifestId", event.target.value)} disabled={busy} aria-label={`${row.displayName} provider integration manifest id`} />
        </label>
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Template catalog
          <select className="rounded-md border border-border/70 bg-background px-3 py-2 text-xs text-foreground outline-none focus:border-primary" value={state.selectedManifestId} onChange={(event) => updateField("selectedManifestId", event.target.value)} disabled={busy || !state.templates || state.templates.length === 0} aria-label={`${row.displayName} provider integration template`}>
            <option value={state.selectedManifestId}>{state.selectedManifestId || "No template selected"}</option>
            {(state.templates ?? []).map((template) => <option key={template.manifestId} value={template.manifestId}>{template.displayName}</option>)}
          </select>
        </label>
      </div>
      <div className="mt-3 grid gap-3 lg:grid-cols-2">
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Manifest draft JSON
          <textarea className="min-h-36 rounded-md border border-border/70 bg-background px-3 py-2 font-mono text-[11px] text-foreground outline-none focus:border-primary" value={state.draftManifestJson} onChange={(event) => updateField("draftManifestJson", event.target.value)} disabled={busy} aria-label={`${row.displayName} provider integration manifest draft JSON`} spellCheck={false} />
        </label>
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Connection draft JSON
          <textarea className="min-h-36 rounded-md border border-border/70 bg-background px-3 py-2 font-mono text-[11px] text-foreground outline-none focus:border-primary" value={state.draftConnectionJson} onChange={(event) => updateField("draftConnectionJson", event.target.value)} disabled={busy} aria-label={`${row.displayName} provider integration connection draft JSON`} spellCheck={false} />
        </label>
      </div>
      <div className="mt-3 flex flex-wrap gap-2">
        <Button type="button" variant="outline" size="sm" onClick={() => void saveSetupDraft()} disabled={busy || !state.draftManifestJson.trim() || !state.draftConnectionJson.trim()} busy={state.busyAction === "save"} aria-label={`Save provider integration setup draft for ${row.displayName}`}>
          <Save className="h-3.5 w-3.5" aria-hidden="true" />
          Save setup draft
        </Button>
        <Button type="button" variant="outline" size="sm" onClick={() => void activateSetup()} disabled={busy || !activationReady} busy={state.busyAction === "activate"} disabledReason={!activationReady ? "Activation readiness must pass before activation." : undefined} aria-label={`Activate provider integration setup for ${row.displayName}`}>
          <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
          Activate when ready
        </Button>
      </div>
      {state.manifest ? (
        <div className="mt-3 grid gap-2 rounded-sm border border-border/60 bg-background/40 px-2 py-2" aria-label={`${row.displayName} provider integration mapping preview`}>
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-xs font-semibold text-foreground">Mapping preview</span>
            <Badge variant="outline">{state.manifest.integrationType}</Badge>
            <Badge variant={state.manifest.state === "Active" ? "success" : "warning"}>{state.manifest.state}</Badge>
          </div>
          <div className="grid gap-2 md:grid-cols-2">
            {mappingPreview.length > 0 ? mappingPreview.slice(0, 4).map((mapping) => (
              <div key={`${mapping.capability}-${mapping.sourcePath}-${mapping.targetField}`} className="rounded-sm border border-border/60 bg-secondary/20 px-2 py-2 text-[11px] text-muted-foreground">
                <span className="font-mono text-foreground">{mapping.sourcePath}</span> {" -> "} <span className="font-mono text-foreground">{mapping.targetField}</span> · {mapping.confidence}{mapping.required ? " · required" : ""}
              </div>
            )) : <p className="text-[11px] text-muted-foreground">No mapping rows for the selected capability.</p>}
          </div>
        </div>
      ) : null}
      <div className="mt-3 grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_minmax(0,1fr)]">
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Capability
          <select className="rounded-md border border-border/70 bg-background px-3 py-2 text-xs text-foreground outline-none focus:border-primary" value={state.capability} onChange={(event) => setState((current) => {
            const capability = event.target.value as ProviderIntegrationCapabilityKind;
            return { ...current, capability, endpointKey: providerIntegrationPreferredEndpointKey(current.manifest, capability, current.endpointKey), message: null, details: [], tone: "default" };
          })} disabled={busy} aria-label={`${row.displayName} provider integration dry-run capability`}>
            {capabilityOptions.map((capability) => <option key={capability} value={capability}>{capability}</option>)}
          </select>
        </label>
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Service key
          <Input value={state.endpointKey} onChange={(event) => updateField("endpointKey", event.target.value)} list={`${row.providerId}-provider-endpoint-options`} disabled={busy} aria-label={`${row.displayName} provider integration dry-run service key`} />
          <datalist id={`${row.providerId}-provider-endpoint-options`}>{endpointOptions.map((endpoint) => <option key={endpoint.endpointKey} value={endpoint.endpointKey} />)}</datalist>
        </label>
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          CSV file name
          <Input value={state.csvFileName} onChange={(event) => updateField("csvFileName", event.target.value)} disabled={busy} aria-label={`${row.displayName} provider integration CSV dry-run file name`} />
        </label>
      </div>
      <div className="mt-3 grid gap-3 lg:grid-cols-2">
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          CSV content
          <textarea className="min-h-24 rounded-md border border-border/70 bg-background px-3 py-2 font-mono text-[11px] text-foreground outline-none focus:border-primary" value={state.csvContent} onChange={(event) => updateField("csvContent", event.target.value)} disabled={busy} aria-label={`${row.displayName} provider integration CSV dry-run content`} spellCheck={false} />
        </label>
        <div className="grid gap-3">
          <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
            REST path parameters JSON
            <Input value={state.restPathParametersJson} onChange={(event) => updateField("restPathParametersJson", event.target.value)} disabled={busy} aria-label={`${row.displayName} provider integration REST path parameters JSON`} />
          </label>
          <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
            REST query parameters JSON
            <Input value={state.restQueryParametersJson} onChange={(event) => updateField("restQueryParametersJson", event.target.value)} disabled={busy} aria-label={`${row.displayName} provider integration REST query parameters JSON`} />
          </label>
        </div>
      </div>
      <div className="mt-3 flex flex-wrap gap-2">
        <Button type="button" variant="outline" size="sm" onClick={() => void runCsvDryRun()} disabled={busy || !manifestId || !state.csvContent.trim()} busy={state.busyAction === "csv-dry-run"} aria-label={`Run provider integration CSV dry-run for ${row.displayName}`}>
          <Activity className="h-3.5 w-3.5" aria-hidden="true" />
          CSV dry-run
        </Button>
        <Button type="button" variant="outline" size="sm" onClick={() => void runRestDryRun()} disabled={busy || !manifestId || !state.endpointKey.trim()} busy={state.busyAction === "rest-dry-run"} aria-label={`Run provider integration REST dry-run for ${row.displayName}`}>
          <Activity className="h-3.5 w-3.5" aria-hidden="true" />
          REST dry-run
        </Button>
        <Button type="button" variant="outline" size="sm" onClick={() => void checkSchemaDrift()} disabled={busy || !canCheckDrift} busy={state.busyAction === "drift"} disabledReason={!canCheckDrift ? "A dry-run or runtime sample is required before schema drift review." : undefined} aria-label={`Check provider integration schema drift for ${row.displayName}`}>
          <MonitorCheck className="h-3.5 w-3.5" aria-hidden="true" />
          Schema drift
        </Button>
      </div>
      <dl className="mt-3 grid gap-2 sm:grid-cols-2">
        <SettingsFieldRow label="Manifest" value={manifestId || "Not selected"} tone={manifestId ? "success" : "muted"} />
        <SettingsFieldRow label="Connection" value={state.connection?.connectionName ?? runtimeState.monitor?.connectionName ?? "Runtime connection"} tone="muted" />
        <SettingsFieldRow label="Activation readiness" value={state.readiness ? (state.readiness.isReady ? "Ready" : "Review required") : "Not checked"} tone={state.readiness?.isReady ? "success" : state.readiness ? "warning" : "muted"} />
        <SettingsFieldRow label="Dry-run result" value={state.dryRunResult ? `${state.dryRunResult.recordsAccepted} accepted / ${state.dryRunResult.recordsQuarantined} quarantined` : "Not run"} tone={state.dryRunResult ? (state.dryRunResult.recordsQuarantined > 0 ? "warning" : "success") : "muted"} />
        <SettingsFieldRow label="Schema drift" value={state.driftResult ? (state.driftResult.driftDetected ? `${state.driftResult.issues.length} issues` : "No drift") : "Not checked"} tone={state.driftResult ? (state.driftResult.shouldPauseCapability ? "danger" : state.driftResult.driftDetected ? "warning" : "success") : "muted"} />
        <SettingsFieldRow label="Activation" value={state.activationResult ? state.activationResult.connectionState : "Not requested"} tone={state.activationResult?.activated ? "success" : "muted"} />
      </dl>
      {state.message ? (
        <div role={state.tone === "danger" ? "alert" : "status"} className={cn("mt-3 text-xs", itemToneClass[state.tone])}>
          <div>{state.message}</div>
          {state.details.length > 0 ? (
            <ul className="mt-1 list-disc space-y-1 pl-4 text-[11px] text-muted-foreground">
              {state.details.map((detail) => <li key={detail}>{detail}</li>)}
            </ul>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}
function ProviderOpenApiImportForm({
  row,
  state,
  onStateChange,
  onSubmit
}: {
  row: SettingsProviderConnectionRow;
  state: ProviderOpenApiImportState;
  onStateChange: (updater: (state: ProviderOpenApiImportState) => ProviderOpenApiImportState) => void;
  onSubmit: () => void;
}) {
  const updateField = <K extends keyof ProviderOpenApiImportState>(
    field: K,
    value: ProviderOpenApiImportState[K]
  ) => {
    onStateChange((current) => ({
      ...current,
      [field]: value,
      message: null,
      details: [],
      tone: "default"
    }));
  };

  return (
    <form
      className="mt-3 grid gap-3 rounded-md border border-border/60 bg-background/35 px-3 py-3"
      aria-label={`${row.displayName} OpenAPI import draft`}
      onSubmit={(event) => {
        event.preventDefault();
        onSubmit();
      }}
      noValidate
    >
      <div className="flex flex-col gap-2 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <h6 className="text-xs font-semibold uppercase tracking-[0.12em] text-foreground">OpenAPI import draft</h6>
          <p className="mt-1 text-[11px] leading-4 text-muted-foreground">
            Seed a draft manifest through the shared import endpoint; readiness, mapping, and runtime evidence stay service-owned.
          </p>
        </div>
        <Button
          type="submit"
          variant="outline"
          size="sm"
          className="shrink-0"
          disabled={state.busy}
          busy={state.busy}
          busyLabel="Importing OpenAPI"
          aria-label={`Import OpenAPI draft manifest for ${row.displayName}`}
        >
          <Save className="h-3.5 w-3.5" aria-hidden="true" />
          Import draft
        </Button>
      </div>
      <div className="grid gap-3 md:grid-cols-2">
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Manifest ID
          <Input
            value={state.manifestId}
            onChange={(event) => updateField("manifestId", event.target.value)}
            disabled={state.busy}
            aria-label={`${row.displayName} OpenAPI manifest id`}
          />
        </label>
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Display name
          <Input
            value={state.displayName}
            onChange={(event) => updateField("displayName", event.target.value)}
            disabled={state.busy}
            aria-label={`${row.displayName} OpenAPI display name`}
          />
        </label>
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Environment
          <Input
            value={state.environment}
            onChange={(event) => updateField("environment", event.target.value)}
            disabled={state.busy}
            aria-label={`${row.displayName} OpenAPI environment`}
          />
        </label>
        <FilterSelect
          label="Auth type"
          value={state.authType}
          onChange={(value) => updateField("authType", value as ProviderIntegrationAuthType)}
          options={PROVIDER_OPEN_API_AUTH_OPTIONS}
        />
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Capabilities
          <Input
            value={state.capabilities}
            onChange={(event) => updateField("capabilities", event.target.value)}
            disabled={state.busy}
            aria-label={`${row.displayName} OpenAPI capabilities`}
          />
        </label>
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          OAuth token URL
          <Input
            value={state.tokenUrl}
            onChange={(event) => updateField("tokenUrl", event.target.value)}
            disabled={state.busy}
            aria-label={`${row.displayName} OpenAPI token URL`}
          />
        </label>
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Scopes
          <Input
            value={state.scopes}
            onChange={(event) => updateField("scopes", event.target.value)}
            disabled={state.busy}
            aria-label={`${row.displayName} OpenAPI scopes`}
          />
        </label>
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Change reason
          <Input
            value={state.changeReason}
            onChange={(event) => updateField("changeReason", event.target.value)}
            disabled={state.busy}
            aria-label={`${row.displayName} OpenAPI change reason`}
          />
        </label>
      </div>
      <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
        OpenAPI JSON
        <textarea
          className="min-h-24 rounded-md border border-border/70 bg-background px-3 py-2 font-mono text-xs text-foreground outline-none focus:border-primary"
          value={state.openApiDocumentJson}
          onChange={(event) => updateField("openApiDocumentJson", event.target.value)}
          disabled={state.busy}
          aria-label={`${row.displayName} OpenAPI JSON`}
          spellCheck={false}
        />
      </label>
      {state.message ? (
        <div role={state.tone === "danger" ? "alert" : "status"} className={cn("text-xs", itemToneClass[state.tone])}>
          <div>{state.message}</div>
          {state.result ? (
            <div className="mt-1 text-[11px] text-muted-foreground">
              {state.result.manifest.integrationType} · {state.result.manifest.state} · {state.result.readiness.isReady ? "Ready" : "Readiness review"}
            </div>
          ) : null}
          {state.details.length > 0 ? (
            <ul className="mt-1 list-disc space-y-1 pl-4 text-[11px] text-muted-foreground">
              {state.details.map((detail) => <li key={detail}>{detail}</li>)}
            </ul>
          ) : null}
        </div>
      ) : null}
    </form>
  );
}

function ProviderReadinessChecklist({
  row,
  state,
  fieldDefinitions
}: {
  row: { credentialStatus: "present" | "missing" | "not-required"; verificationStatus: "verified" | "pending" | "failed"; fallbackStatus: "active" | "available" | "missing"; displayName: string };
  state: ProviderInlineState;
  fieldDefinitions: ProviderInlineFieldDefinition[];
}) {
  const credentialsReady = state.dirty
    ? fieldDefinitions
      .filter((definition) => definition.required)
      .every((definition) => (state.values[definition.field] ?? "").trim().length > 0)
    : row.credentialStatus !== "missing";
  const verified = !state.verificationFailed && row.verificationStatus === "verified";
  const fallbackSet = row.fallbackStatus !== "missing";
  const checks = [
    { label: "Credentials present", ready: credentialsReady },
    { label: "Verified recently", ready: verified },
    { label: "Fallback set", ready: fallbackSet }
  ];
  return (
    <div className="mt-3 rounded-md border border-border/60 bg-secondary/15 px-3 py-2" aria-label={`${row.displayName} readiness checks`}>
      <div className="text-xs font-semibold text-foreground">Readiness checks</div>
      <div className="mt-2 flex flex-wrap gap-2">
        {checks.map((check) => (
          <Badge key={check.label} variant={check.ready ? "success" : "warning"}>
            {check.label}: {check.ready ? "Ready" : "Review"}
          </Badge>
        ))}
      </div>
    </div>
  );
}

function createProviderInlineState(row: {
  environmentLabel: string;
  credentialStatus: "present" | "missing" | "not-required";
  verificationStatus: "verified" | "pending" | "failed";
  credentialFields: ProviderCredentialFieldMetadata[];
  environmentOptions: ProviderEnvironmentOption[];
}): ProviderInlineState {
  const fieldDefinitions = buildProviderFieldDefinitions(row);
  return {
    editing: false,
    values: buildEmptyProviderInlineValues(fieldDefinitions),
    environment: normalizeInlineEnvironment(row.environmentLabel, row.environmentOptions),
    liveAcknowledged: false,
    dirty: false,
    busyAction: null,
    statusMessage: null,
    statusDetails: [],
    statusTone: "default",
    verificationFailed: row.verificationStatus === "failed" || row.credentialStatus === "missing",
    testLatencyLabel: null
  };
}

function normalizeInlineEnvironment(label: string, options: ProviderEnvironmentOption[] = []): string {
  const value = label.trim().toLowerCase();
  const fromOption = options.find((option) => (
    option.value.trim().toLowerCase() === value ||
    option.label.trim().toLowerCase() === value
  ));
  if (fromOption) {
    return fromOption.value;
  }

  const defaultOption = options.find((option) => option.isDefault) ?? options[0];
  if (defaultOption) {
    return defaultOption.value;
  }

  if (value === "live" || value === "sandbox" || value === "production" || value === "development" || value === "custom") {
    return value;
  }

  return "paper";
}

function buildProviderFieldDefinitions(row: {
  credentialStatus?: "present" | "missing" | "not-required";
  credentialFields?: ProviderCredentialFieldMetadata[] | null;
}): ProviderInlineFieldDefinition[] {
  const metadata = row.credentialFields ?? [];
  if (metadata.length > 0) {
    return metadata.map((field) => ({
      field: field.name,
      label: field.label,
      type: providerInputType(field.inputKind),
      placeholder: field.placeholder ?? "Stored server-side and masked after save",
      helpText: field.helpText ?? "Stored in Meridian's encrypted local provider store and masked after save.",
      required: field.required
    }));
  }

  if (row.credentialStatus === "not-required") {
    return [];
  }

  return [
    {
      field: "CredentialReference",
      label: "Credential reference",
      type: "password",
      placeholder: "Stored server-side and masked after save",
      helpText: "Fallback credential field used only when provider metadata is unavailable.",
      required: row.credentialStatus === "missing"
    }
  ];
}

function providerInputType(kind: ProviderCredentialFieldMetadata["inputKind"]): ProviderInlineFieldDefinition["type"] {
  if (kind === "Url") {
    return "url";
  }

  if (kind === "Text") {
    return "text";
  }

  return "password";
}

function buildEmptyProviderInlineValues(definitions: ProviderInlineFieldDefinition[]): Record<ProviderInlineField, string> {
  if (definitions.length === 0) {
    return { ...emptyProviderInlineValues };
  }

  return Object.fromEntries(definitions.map((definition) => [definition.field, ""]));
}

function buildProviderEnvironmentOptions(options: ProviderEnvironmentOption[], currentEnvironment: string): ProviderEnvironmentOption[] {
  const base = options.length > 0
    ? options
    : [
        { value: "paper", label: "Paper", isDefault: true },
        { value: "live", label: "Live", isDefault: false },
        { value: "sandbox", label: "Sandbox", isDefault: false },
        { value: "custom", label: "Custom", isDefault: false }
      ];
  const current = currentEnvironment.trim();
  if (!current || base.some((option) => option.value === current)) {
    return base;
  }

  return [
    {
      value: current,
      label: current.toUpperCase(),
      isDefault: false,
      helpText: "Current provider environment from the server."
    },
    ...base
  ];
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

function providerDraftStatusLabel(state: ProviderInlineState | undefined, row: { verificationStatus: "verified" | "pending" | "failed" }): string {
  if (!state) return "Active";
  if (state.busyAction === "save") return "Saving";
  if (state.dirty) return "Unsaved";
  if (state.verificationFailed || row.verificationStatus === "failed") return "Verification failed";
  return "Active";
}

function providerDraftStatusVariant(state: ProviderInlineState | undefined, row: { verificationStatus: "verified" | "pending" | "failed" }): "outline" | "success" | "warning" | "danger" {
  const label = providerDraftStatusLabel(state, row);
  if (label === "Saving") return "warning";
  if (label === "Unsaved") return "warning";
  if (label === "Verification failed") return "danger";
  return "success";
}

function providerRuntimeStateKey(row: Pick<SettingsProviderConnectionRow, "integrationConnectionId" | "providerId">): string {
  return row.integrationConnectionId || row.providerId;
}

function providerRuntimeReplaySyncRunId(connectionId: string, requestedAt: Date): string {
  const suffix = requestedAt.toISOString().replace(/[^0-9A-Za-z]/g, "").toLowerCase();
  const normalizedConnection = (connectionId || "connection").replace(/[^0-9A-Za-z-]/g, "-").toLowerCase();
  return `provider-replay-${normalizedConnection}-${suffix}`;
}

function providerRuntimeHandoffEvidenceId(connectionId: string, requestedAt: Date): string {
  const suffix = requestedAt.toISOString().replace(/[^0-9A-Za-z]/g, "").toLowerCase();
  const normalizedConnection = (connectionId || "connection").replace(/[^0-9A-Za-z-]/g, "-").toLowerCase();
  return `settings-provider-handoff-${normalizedConnection}-${suffix}`;
}

function createProviderIntegrationWorkbenchState(row: SettingsProviderConnectionRow): ProviderIntegrationWorkbenchState {
  const capability = providerIntegrationDefaultCapability(row);
  return {
    templates: null,
    selectedManifestId: providerIntegrationDefaultManifestId(row),
    manifest: null,
    connection: null,
    draftManifestJson: "",
    draftConnectionJson: "",
    capability,
    endpointKey: providerIntegrationDefaultEndpointKey(capability),
    csvFileName: `${providerIntegrationNormalizedId(row.providerId)}-${capability.toLowerCase()}.csv`,
    csvContent: providerIntegrationSampleCsv(capability),
    restPathParametersJson: "{}",
    restQueryParametersJson: "{}",
    readiness: null,
    setupResult: null,
    dryRunResult: null,
    driftResult: null,
    activationResult: null,
    busyAction: null,
    message: null,
    details: [],
    tone: "default"
  };
}

function providerIntegrationDefaultCapability(row: Pick<SettingsProviderConnectionRow, "capabilityGroup">): ProviderIntegrationCapabilityKind {
  return row.capabilityGroup === "accounting" ? "Transactions" : "Positions";
}

function providerIntegrationDefaultEndpointKey(capability: ProviderIntegrationCapabilityKind): string {
  return capability.replace(/([a-z])([A-Z])/g, "$1-$2").toLowerCase();
}

function providerIntegrationDefaultManifestId(row: Pick<SettingsProviderConnectionRow, "providerId" | "capabilityGroup">): string {
  const suffix = row.capabilityGroup === "accounting" ? "accounting" : row.capabilityGroup === "brokerage" ? "brokerage" : "data";
  return `template-${providerIntegrationNormalizedId(row.providerId)}-${suffix}-v1`;
}

function providerIntegrationDefaultSelectedManifestId(
  row: Pick<SettingsProviderConnectionRow, "providerId" | "displayName" | "capabilityGroup">,
  templates: ProviderIntegrationTemplateCatalogEntry[],
  currentManifestId: string
): string {
  const current = currentManifestId.trim();
  if (current && templates.some((template) => template.manifestId === current)) {
    return current;
  }

  const normalizedProvider = providerIntegrationNormalizedId(row.providerId);
  const normalizedName = providerIntegrationNormalizedId(row.displayName);
  const matched = templates.find((template) => (
    providerIntegrationNormalizedId(template.providerId) === normalizedProvider ||
    providerIntegrationNormalizedId(template.displayName).includes(normalizedName) ||
    providerIntegrationNormalizedId(template.manifestId).includes(normalizedProvider)
  ));

  return matched?.manifestId ?? templates[0]?.manifestId ?? current ?? providerIntegrationDefaultManifestId(row);
}

function createProviderIntegrationConnectionDraft(
  row: SettingsProviderConnectionRow,
  manifest: ProviderIntegrationManifest,
  operatorName: string
): ProviderIntegrationConnection {
  const now = new Date().toISOString();
  const enabledCapabilities = manifest.capabilities
    .filter((capability) => capability.enabled)
    .map((capability) => capability.capability);
  return {
    connectionId: row.integrationConnectionId || `${manifest.manifestId}-connection`,
    providerId: manifest.providerId || row.providerId,
    manifestId: manifest.manifestId,
    connectionName: `${manifest.displayName || row.displayName} ${manifest.environment || providerOpenApiEnvironment(row.environmentLabel)}`.trim(),
    environment: manifest.environment || providerOpenApiEnvironment(row.environmentLabel),
    state: manifest.state === "Active" ? "PendingApproval" : manifest.state,
    credentialSecretRef: row.credentialStatus === "not-required" ? "" : providerIntegrationCredentialReference(row),
    enabledCapabilities: enabledCapabilities.length > 0 ? enabledCapabilities : [providerIntegrationDefaultCapability(row)],
    ownerUserId: operatorName,
    createdAt: manifest.createdAt || now,
    updatedAt: now,
    approvalEvidenceId: null
  };
}

function providerIntegrationWorkbenchWithDraft(
  row: SettingsProviderConnectionRow,
  state: ProviderIntegrationWorkbenchState,
  manifest: ProviderIntegrationManifest,
  connection: ProviderIntegrationConnection,
  status: { message: string; details: string[]; tone: ProviderIntegrationWorkbenchState["tone"] }
): ProviderIntegrationWorkbenchState {
  const capability = providerIntegrationWorkbenchCapabilities({ ...state, manifest })[0] ?? providerIntegrationDefaultCapability(row);
  return {
    ...state,
    selectedManifestId: manifest.manifestId,
    manifest,
    connection,
    draftManifestJson: providerIntegrationFormatJson(manifest),
    draftConnectionJson: providerIntegrationFormatJson(connection),
    capability,
    endpointKey: providerIntegrationPreferredEndpointKey(manifest, capability, state.endpointKey),
    readiness: null,
    setupResult: null,
    driftResult: null,
    activationResult: null,
    busyAction: null,
    message: status.message,
    details: status.details,
    tone: status.tone
  };
}

function providerIntegrationWorkbenchCapabilities(state: Pick<ProviderIntegrationWorkbenchState, "manifest" | "capability">): ProviderIntegrationCapabilityKind[] {
  const capabilities = new Set<ProviderIntegrationCapabilityKind>();
  if (state.capability) {
    capabilities.add(state.capability);
  }
  state.manifest?.capabilities
    .filter((capability) => capability.enabled)
    .forEach((capability) => capabilities.add(capability.capability));
  if (capabilities.size === 0) {
    capabilities.add("Positions");
  }
  return Array.from(capabilities);
}

function providerIntegrationWorkbenchEndpoints(
  state: Pick<ProviderIntegrationWorkbenchState, "manifest">,
  capability: ProviderIntegrationCapabilityKind
): ProviderIntegrationEndpointDefinition[] {
  return (state.manifest?.endpoints ?? []).filter((endpoint) => endpoint.capability === capability);
}

function providerIntegrationWorkbenchMappings(
  state: Pick<ProviderIntegrationWorkbenchState, "manifest">,
  capability: ProviderIntegrationCapabilityKind
): ProviderIntegrationFieldMapping[] {
  return (state.manifest?.fieldMappings ?? []).filter((mapping) => mapping.capability === capability);
}

function providerIntegrationPreferredEndpointKey(
  manifest: ProviderIntegrationManifest | null,
  capability: ProviderIntegrationCapabilityKind,
  currentEndpointKey: string
): string {
  const current = currentEndpointKey.trim();
  const endpoints = (manifest?.endpoints ?? []).filter((endpoint) => endpoint.capability === capability);
  if (current && endpoints.some((endpoint) => endpoint.endpointKey === current)) {
    return current;
  }
  return endpoints[0]?.endpointKey ?? (current || providerIntegrationDefaultEndpointKey(capability));
}

function providerIntegrationWorkbenchManifestId(
  state: Pick<ProviderIntegrationWorkbenchState, "manifest" | "selectedManifestId">,
  runtimeState: ProviderRuntimeEvidenceState
): string {
  if (state.manifest?.manifestId) {
    return state.manifest.manifestId;
  }

  const selectedManifestId = state.selectedManifestId.trim();
  if (selectedManifestId) {
    return selectedManifestId;
  }

  return runtimeState.monitor?.manifestId ?? "";
}

function providerIntegrationWorkbenchConnectionId(
  row: Pick<SettingsProviderConnectionRow, "integrationConnectionId">,
  state: Pick<ProviderIntegrationWorkbenchState, "connection">,
  runtimeState: ProviderRuntimeEvidenceState
): string {
  return state.connection?.connectionId ?? runtimeState.monitor?.connectionId ?? row.integrationConnectionId;
}

function providerIntegrationLatestRawPayload(
  state: Pick<ProviderIntegrationWorkbenchState, "dryRunResult" | "capability" | "endpointKey">,
  runtimeState: ProviderRuntimeEvidenceState
): { syncRunId: string; rawPayloadId: string; capability: ProviderIntegrationCapabilityKind; endpointKey: string } | null {
  if (state.dryRunResult?.rawPayloadId) {
    return {
      syncRunId: state.dryRunResult.syncRunId,
      rawPayloadId: state.dryRunResult.rawPayloadId,
      capability: state.dryRunResult.capability,
      endpointKey: state.endpointKey
    };
  }

  const latestRunWithPayload = providerRuntimeRuns(runtimeState).find((run) => Boolean(run.rawPayloadId));
  if (!latestRunWithPayload?.rawPayloadId) {
    return null;
  }

  return {
    syncRunId: latestRunWithPayload.syncRunId,
    rawPayloadId: latestRunWithPayload.rawPayloadId,
    capability: latestRunWithPayload.capability,
    endpointKey: latestRunWithPayload.endpointKey
  };
}

function providerIntegrationWorkbenchDraftDetails(manifest: ProviderIntegrationManifest): string[] {
  return [
    `${manifest.endpoints.length} endpoint definitions`,
    `${manifest.fieldMappings.length} mapping rows`,
    `${manifest.validationRules.length} validation rules`
  ];
}

function providerIntegrationReadinessDetails(readiness: ProviderIntegrationActivationReadiness | null): string[] {
  if (!readiness) {
    return [];
  }

  return [
    ...readiness.requiredEvidence.map((evidence) => `Evidence required: ${evidence}`),
    ...readiness.issues.map((issue) => `${issue.severity}: ${issue.message}`)
  ];
}

function providerIntegrationFormatJson(value: unknown): string {
  return JSON.stringify(value, null, 2);
}

function parseProviderIntegrationWorkbenchJson<T>(
  value: string,
  label: string
): { ok: true; value: T } | { ok: false; error: string } {
  try {
    return { ok: true, value: JSON.parse(value) as T };
  } catch (error) {
    return { ok: false, error: `${label}: ${error instanceof Error ? error.message : "Invalid JSON"}` };
  }
}

function parseProviderIntegrationStringRecord(
  value: string,
  label: string
): { ok: true; value: Record<string, string> } | { ok: false; error: string } {
  const parsed = parseProviderIntegrationWorkbenchJson<unknown>(value || "{}", label);
  if (parsed.ok === false) {
    return { ok: false, error: parsed.error };
  }
  if (!parsed.value || typeof parsed.value !== "object" || Array.isArray(parsed.value)) {
    return { ok: false, error: `${label}: expected a JSON object.` };
  }

  return {
    ok: true,
    value: Object.fromEntries(Object.entries(parsed.value).map(([key, item]) => [key, String(item)]))
  };
}

function providerIntegrationCredentialReference(row: Pick<SettingsProviderConnectionRow, "providerId" | "sourceLabel">): string {
  return `provider-credential:${providerIntegrationNormalizedId(row.providerId)}:${providerIntegrationNormalizedId(row.sourceLabel || "local")}`;
}

function providerIntegrationWorkbenchSyncRunId(connectionId: string, mode: string, requestedAt: Date): string {
  return `settings-${mode}-${providerIntegrationNormalizedId(connectionId || "connection")}-${providerIntegrationTimestampSuffix(requestedAt)}`;
}

function providerIntegrationWorkbenchEvidenceId(connectionId: string, purpose: string, requestedAt: Date): string {
  return `settings-provider-${purpose}-${providerIntegrationNormalizedId(connectionId || "connection")}-${providerIntegrationTimestampSuffix(requestedAt)}`;
}

function providerIntegrationTimestampSuffix(value: Date): string {
  return value.toISOString().replace(/[^0-9A-Za-z]/g, "").toLowerCase();
}

function providerIntegrationNormalizedId(value: string): string {
  return (value || "provider").replace(/[^0-9A-Za-z-]/g, "-").replace(/-+/g, "-").replace(/^-|-$/g, "").toLowerCase() || "provider";
}

function providerIntegrationSampleCsv(capability: ProviderIntegrationCapabilityKind): string {
  if (capability === "Transactions") {
    return "transactionId,accountId,amount,currency,postedAt\ntxn-1,acct-1,125.00,USD,2026-06-01";
  }
  return "positionId,accountId,symbol,quantity,asOfDate\npos-1,acct-1,MSFT,10,2026-06-01";
}
function createProviderOpenApiImportState(row: SettingsProviderConnectionRow): ProviderOpenApiImportState {
  const normalizedProvider = (row.providerId || row.integrationConnectionId || "provider")
    .replace(/[^0-9A-Za-z-]/g, "-")
    .replace(/-+/g, "-")
    .toLowerCase();
  const capability = row.capabilityGroup === "accounting" ? "Transactions" : "Positions";

  return {
    manifestId: `draft-${normalizedProvider}-openapi-v1`,
    displayName: `${row.displayName} OpenAPI`,
    environment: providerOpenApiEnvironment(row.environmentLabel),
    authType: "OAuth2",
    tokenUrl: "",
    scopes: "",
    capabilities: capability,
    openApiDocumentJson: PROVIDER_OPEN_API_SAMPLE_DOCUMENT,
    changeReason: "Imported from the Settings Provider Connection Center.",
    busy: false,
    result: null,
    message: null,
    details: [],
    tone: "default"
  };
}

function providerOpenApiEnvironment(environmentLabel: string): string {
  const normalized = environmentLabel.trim().toLowerCase();
  return normalized && normalized !== "not set" ? normalized : "paper";
}

function parseProviderOpenApiCapabilities(value: string): ProviderIntegrationCapabilityKind[] {
  const allowed = new Set<ProviderIntegrationCapabilityKind>(PROVIDER_OPEN_API_CAPABILITIES);
  const capabilities = value
    .split(",")
    .map((capability) => capability.trim())
    .filter((capability): capability is ProviderIntegrationCapabilityKind =>
      allowed.has(capability as ProviderIntegrationCapabilityKind));

  return Array.from(new Set(capabilities));
}

function providerRuntimeValue<T>(result: PromiseSettledResult<T>): T | null {
  return result.status === "fulfilled" ? result.value : null;
}

function providerRuntimeErrorDetail<T>(result: PromiseSettledResult<T>, label: string): string | null {
  if (result.status === "fulfilled") {
    return null;
  }

  const display = describeApiError(result.reason, `${label} could not be loaded.`);
  return [display.summary, ...display.details].filter(Boolean).join(" ");
}

function providerRuntimeRuns(state: ProviderRuntimeEvidenceState): ProviderIntegrationSyncRunEvidence[] {
  const runs = new Map<string, ProviderIntegrationSyncRunEvidence>();
  const addRun = (run: ProviderIntegrationSyncRunEvidence | null | undefined) => {
    if (run) {
      runs.set(run.syncRunId, run);
    }
  };

  addRun(state.monitor?.lastSyncRun);
  state.monitor?.recentSyncRuns.forEach(addRun);
  state.syncRuns?.syncRuns.forEach(addRun);

  return Array.from(runs.values()).sort((left, right) => providerRuntimeDateValue(right.startedAt) - providerRuntimeDateValue(left.startedAt));
}

function providerRuntimeReceivedCount(state: ProviderRuntimeEvidenceState, runs: ProviderIntegrationSyncRunEvidence[]): number {
  return state.monitor?.recentRecordsReceived ?? providerRuntimeSum(runs, (run) => run.recordsReceived);
}

function providerRuntimeAcceptedCount(state: ProviderRuntimeEvidenceState, runs: ProviderIntegrationSyncRunEvidence[]): number {
  return state.monitor?.recentRecordsAccepted ?? providerRuntimeSum(runs, (run) => run.recordsAccepted);
}

function providerRuntimeQuarantinedCount(state: ProviderRuntimeEvidenceState, runs: ProviderIntegrationSyncRunEvidence[]): number {
  return state.monitor?.recentRecordsQuarantined ?? state.quarantine?.totalQuarantinedRecords ?? providerRuntimeSum(runs, (run) => run.recordsQuarantined);
}

function providerRuntimeStagedCount(state: ProviderRuntimeEvidenceState, runs: ProviderIntegrationSyncRunEvidence[]): number {
  return state.monitor?.durableStagingRecordCount ?? providerRuntimeSum(runs, (run) => run.durableStagingRecordCount);
}

function providerRuntimeDurableQuarantinedCount(state: ProviderRuntimeEvidenceState, runs: ProviderIntegrationSyncRunEvidence[]): number {
  return state.monitor?.durableQuarantinedRecordCount ?? state.quarantine?.totalQuarantinedRecords ?? providerRuntimeSum(runs, (run) => run.durableQuarantinedRecordCount);
}

function providerRuntimeCriticalIssueCount(state: ProviderRuntimeEvidenceState): number {
  return Math.max(
    state.quarantine?.criticalIssueCount ?? 0,
    providerRuntimeRuns(state).reduce((sum, run) => sum + run.criticalIssueCount, 0),
    state.monitor?.hasCriticalIssues ? 1 : 0
  );
}

function providerRuntimeWarningIssueCount(state: ProviderRuntimeEvidenceState): number {
  return Math.max(
    state.quarantine?.warningIssueCount ?? 0,
    providerRuntimeRuns(state).reduce((sum, run) => sum + run.warningIssueCount, 0),
    state.identity?.accountReviewRequiredCount ?? 0,
    state.identity?.securityReviewRequiredCount ?? 0,
    state.promotion?.reviewRequiredCount ?? 0
  );
}

function providerRuntimeSum(
  runs: ProviderIntegrationSyncRunEvidence[],
  selector: (run: ProviderIntegrationSyncRunEvidence) => number
): number {
  return runs.reduce((sum, run) => sum + selector(run), 0);
}

function providerRuntimeDateValue(value: string | null | undefined): number {
  if (!value) {
    return 0;
  }

  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? 0 : parsed;
}

function providerRuntimePanelTone(state: ProviderRuntimeEvidenceState): keyof typeof diagnosticToneClass {
  if (state.phase === "error") return "danger";
  if (state.phase === "loading") return "warning";
  if (providerRuntimeCriticalIssueCount(state) > 0) return "danger";
  if (providerRuntimeQuarantinedCount(state, providerRuntimeRuns(state)) > 0 || providerRuntimeWarningIssueCount(state) > 0) return "warning";
  if (state.phase === "loaded") return "success";
  return "default";
}

function providerRuntimeStatusLabel(state: ProviderRuntimeEvidenceState): string {
  if (state.phase === "idle") return "Not loaded";
  if (state.phase === "loading") return "Loading";
  if (state.phase === "error") return "Unavailable";
  if (providerRuntimeCriticalIssueCount(state) > 0) return "Critical";
  if (providerRuntimeQuarantinedCount(state, providerRuntimeRuns(state)) > 0 || providerRuntimeWarningIssueCount(state) > 0) return "Review";
  return "Synced";
}

function providerRuntimeStatusVariant(state: ProviderRuntimeEvidenceState): "outline" | "success" | "warning" | "danger" {
  const tone = providerRuntimePanelTone(state);
  if (tone === "success") return "success";
  if (tone === "warning") return "warning";
  if (tone === "danger") return "danger";
  return "outline";
}

function providerRuntimeMessageTone(state: ProviderRuntimeEvidenceState): keyof typeof itemToneClass {
  if (state.phase === "error") return "danger";
  if (state.details.length > 0) return "warning";
  return "success";
}

function providerRuntimeIssueTone(criticalIssueCount: number, warningIssueCount: number): keyof typeof itemToneClass {
  if (criticalIssueCount > 0) return "danger";
  if (warningIssueCount > 0) return "warning";
  return "success";
}

function providerRuntimeRunVariant(run: ProviderIntegrationSyncRunEvidence): "outline" | "success" | "warning" | "danger" {
  const status = run.status.toLowerCase();
  if (status.includes("fail") || run.criticalIssueCount > 0) return "danger";
  if (run.recordsQuarantined > 0 || run.warningIssueCount > 0 || status.includes("review")) return "warning";
  if (status.includes("accepted") || status.includes("complete") || status.includes("success")) return "success";
  return "outline";
}

function providerRuntimeProcessingStatusVariant(status: ProviderIntegrationProcessingStatus): "outline" | "success" | "warning" | "danger" {
  const normalized = status.toLowerCase();
  if (normalized.includes("block")) return "danger";
  if (normalized.includes("quarantine")) return "warning";
  if (normalized.includes("validated") || normalized.includes("loaded") || normalized.includes("published")) return "success";
  return "outline";
}

function providerRuntimeLatestQuarantineDecision(
  decisions: readonly ProviderIntegrationQuarantineDecision[] | null | undefined,
  record: ProviderIntegrationQuarantinedRecord
): ProviderIntegrationQuarantineDecision | null {
  const matching = (decisions ?? [])
    .filter((decision) => decision.quarantineRecordId === record.quarantineRecordId && decision.syncRunId === record.syncRunId)
    .sort((left, right) => providerRuntimeDateValue(right.reviewedAt) - providerRuntimeDateValue(left.reviewedAt));

  return matching[0] ?? null;
}

function providerRuntimeQuarantineActionLabel(action: ProviderIntegrationQuarantineResolutionAction): string {
  switch (action) {
    case "ReplayAfterMappingChange":
      return "Replay after mapping change";
    case "IgnoreProviderRecord":
      return "Ignore provider record";
    case "MarkAsCashPosition":
      return "Mark as cash position";
    case "ReviewOnly":
    default:
      return "Review";
  }
}

function providerRuntimeQuarantineActionNote(action: ProviderIntegrationQuarantineResolutionAction): string {
  switch (action) {
    case "ReplayAfterMappingChange":
      return "Marked from the Settings Provider Connection Center for replay after mapping changes.";
    case "IgnoreProviderRecord":
      return "Ignored from the Settings Provider Connection Center after operator review.";
    case "MarkAsCashPosition":
      return "Marked from the Settings Provider Connection Center as a cash position candidate.";
    case "ReviewOnly":
    default:
      return "Reviewed from the Settings Provider Connection Center runtime evidence panel.";
  }
}

function providerRuntimeRunTone(run: ProviderIntegrationSyncRunEvidence): keyof typeof itemToneClass {
  const variant = providerRuntimeRunVariant(run);
  if (variant === "danger") return "danger";
  if (variant === "warning") return "warning";
  if (variant === "success") return "success";
  return "muted";
}

function providerRuntimeSeverityVariant(severity: string): "outline" | "success" | "warning" | "danger" {
  const normalized = severity.toLowerCase();
  if (normalized === "critical" || normalized === "error") return "danger";
  if (normalized === "warning") return "warning";
  if (normalized === "info") return "outline";
  return "outline";
}

function providerRuntimePromotionVariant(status: string): "outline" | "success" | "warning" | "danger" {
  const normalized = status.toLowerCase();
  if (normalized.includes("blocked")) return "danger";
  if (normalized.includes("review")) return "warning";
  if (normalized.includes("ready")) return "success";
  return "outline";
}

function formatProviderRuntimeNumber(value: number): string {
  return value.toLocaleString("en-US");
}

function formatProviderRuntimeUtcMinute(value: string | Date | null | undefined, unavailableLabel = "Not synced"): string {
  if (!value) {
    return unavailableLabel;
  }

  const date = typeof value === "string" ? new Date(value) : value;
  if (Number.isNaN(date.getTime())) {
    return unavailableLabel;
  }

  return `${PROVIDER_RUNTIME_UTC_MONTH_LABELS[date.getUTCMonth()]} ${date.getUTCDate()}, ${padProviderRuntimeUtc(date.getUTCHours())}:${padProviderRuntimeUtc(date.getUTCMinutes())} UTC`;
}

function padProviderRuntimeUtc(value: number): string {
  return value.toString().padStart(2, "0");
}

const PROVIDER_RUNTIME_UTC_MONTH_LABELS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

const PROVIDER_OPEN_API_AUTH_OPTIONS: { value: ProviderIntegrationAuthType; label: string }[] = [
  { value: "OAuth2", label: "OAuth2" },
  { value: "ApiKey", label: "API key" },
  { value: "BearerToken", label: "Bearer token" },
  { value: "ClientCredentials", label: "Client credentials" },
  { value: "Basic", label: "Basic" },
  { value: "None", label: "None" }
];

const PROVIDER_OPEN_API_CAPABILITIES: ProviderIntegrationCapabilityKind[] = [
  "Accounts",
  "Balances",
  "Positions",
  "Holdings",
  "Transactions",
  "TaxLots",
  "SecurityReferenceData",
  "MarketPrices",
  "CorporateActions",
  "Documents",
  "Alerts",
  "Events",
  "OrderPreview",
  "OrderPlacement",
  "OrderCancellation",
  "OrderStatus",
  "Executions"
];

const PROVIDER_OPEN_API_SAMPLE_DOCUMENT = `{
  "openapi": "3.0.3",
  "info": { "title": "Provider API", "version": "draft" },
  "paths": {}
}`;

function recentEventsVariant(state: "ready" | "empty" | "unavailable"): "default" | "outline" | "danger" {
  if (state === "unavailable") return "danger";
  if (state === "empty") return "outline";
  return "default";
}

function settingsBannerTone(tone: keyof typeof diagnosticToneClass | keyof typeof itemToneClass): "success" | "warning" | "danger" | "info" {
  if (tone === "success") return "success";
  if (tone === "warning") return "warning";
  if (tone === "danger") return "danger";
  return "info";
}

function systemVariant(tone: keyof typeof systemToneClass): "outline" | "success" | "warning" | "danger" {
  if (tone === "success") return "success";
  if (tone === "warning") return "warning";
  if (tone === "danger") return "danger";
  return "outline";
}

function toneVariant(tone: keyof typeof itemToneClass): "outline" | "success" | "warning" | "danger" {
  if (tone === "success") return "success";
  if (tone === "warning") return "warning";
  if (tone === "danger") return "danger";
  return "outline";
}

function capabilityTone(tone: "default" | "success" | "warning" | "danger" | "outline"): keyof typeof diagnosticToneClass {
  if (tone === "success") return "success";
  if (tone === "warning") return "warning";
  if (tone === "danger") return "danger";
  return "default";
}
