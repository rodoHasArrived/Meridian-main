import {
  AUTH_API_ENDPOINTS,
  FUND_STRUCTURE_API_ENDPOINTS,
  WORKSTATION_API_ENDPOINTS
} from "@/lib/workstation-endpoints";
import {
  formatSettingsDateOnly,
  formatSettingsUtcMinute
} from "@/screens/settings-screen.date-format";
import type { SettingsScreenPayload } from "@/screens/settings-screen.view-model";
import type {
  LedgerMappingWorkbench,
  OperationsApprovalPolicyMatrix,
  OperationsCloseCalendar,
  RolePermissionCatalog,
  SecurityAssetProfileDefinition,
  SessionInfo
} from "@/types";

export interface SettingsOperationsControlMetric {
  label: string;
  value: string;
  tone: "default" | "success" | "warning" | "danger" | "muted";
}

export interface SettingsOperationsControlCard {
  id: string;
  title: string;
  description: string;
  statusLabel: string;
  statusVariant: "default" | "success" | "warning" | "danger" | "outline";
  endpointHref: string;
  routeHref: string;
  routeLabel: string;
  routeAriaLabel: string;
  metrics: SettingsOperationsControlMetric[];
  detail: string;
}

export interface SettingsOperationsControlCenter {
  title: string;
  summary: string;
  statusLabel: string;
  statusVariant: "default" | "success" | "warning" | "danger" | "outline";
  loadedCountLabel: string;
  reviewCountLabel: string;
  listLabel: string;
  cards: SettingsOperationsControlCard[];
}

export interface SettingsAssetProfileRow {
  profileId: string;
  versionLabel: string;
  name: string;
  categoryLabel: string;
  statusLabel: string;
  statusVariant: "default" | "success" | "warning" | "danger" | "outline";
  fieldCountLabel: string;
  projectedFieldLabel: string;
  requiredCloseIdentifierLabel: string;
  accountingImpactLabel: string;
  effectiveLabel: string;
}

export interface SettingsAssetProfileGovernancePanel {
  title: string;
  summary: string;
  statusLabel: string;
  statusVariant: "default" | "success" | "warning" | "danger" | "outline";
  approvedCountLabel: string;
  projectedFieldCountLabel: string;
  closeIdentifierCountLabel: string;
  listLabel: string;
  canCreateSecurity: boolean;
  createDisabledReason: string | null;
  rows: SettingsAssetProfileRow[];
}

export function buildAssetProfileGovernancePanel(
  profiles: SecurityAssetProfileDefinition[] | null,
  loading: boolean
): SettingsAssetProfileGovernancePanel {
  if (!profiles) {
    return {
      title: "Asset Profile accounting",
      summary: loading
        ? "Asset profiles are loading from Security Master."
        : "Asset profile catalog has not loaded.",
      statusLabel: loading ? "Checking" : "Unavailable",
      statusVariant: loading ? "warning" : "outline",
      approvedCountLabel: "0",
      projectedFieldCountLabel: "0",
      closeIdentifierCountLabel: "0",
      listLabel: "Asset profile accounting rows",
      canCreateSecurity: false,
      createDisabledReason: loading
        ? "Asset profile catalog is still loading."
        : "Asset profile catalog has not loaded.",
      rows: []
    };
  }

  const approvedProfiles = profiles.filter((profile) => profile.status === "Approved");
  const projectedFieldCount = approvedProfiles.reduce(
    (sum, profile) => sum + profile.fields.filter((field) => field.isProjected || field.isSearchable).length,
    0
  );
  const requiredIdentifierCount = approvedProfiles.reduce(
    (sum, profile) => sum + profile.identifierPreferences.filter((preference) => preference.isRequiredForClose).length,
    0
  );
  const statusVariant: SettingsAssetProfileGovernancePanel["statusVariant"] =
    approvedProfiles.length > 0 ? "success" : "warning";

  return {
    title: "Asset Profile accounting",
    summary: approvedProfiles.length > 0
      ? `${approvedProfiles.length} approved alternative-asset profile${approvedProfiles.length === 1 ? "" : "s"} are available for governed Security Master creation.`
      : "No approved asset profiles are available for Security Master creation.",
    statusLabel: approvedProfiles.length > 0 ? `${approvedProfiles.length} approved` : "Approval needed",
    statusVariant,
    approvedCountLabel: String(approvedProfiles.length),
    projectedFieldCountLabel: String(projectedFieldCount),
    closeIdentifierCountLabel: String(requiredIdentifierCount),
    listLabel: `${profiles.length} asset profile${profiles.length === 1 ? "" : "s"}`,
    canCreateSecurity: approvedProfiles.length > 0,
    createDisabledReason: approvedProfiles.length > 0 ? null : "Approve an asset profile before creating custom assets.",
    rows: profiles.map((profile) => {
      const projectedFields = profile.fields.filter((field) => field.isProjected || field.isSearchable).length;
      const requiredIdentifiers = profile.identifierPreferences
        .filter((preference) => preference.isRequiredForClose)
        .map((preference) => preference.kind);
      return {
        profileId: profile.profileId,
        versionLabel: `v${profile.version}`,
        name: profile.name,
        categoryLabel: profile.subType ? `${profile.category} / ${profile.subType}` : profile.category,
        statusLabel: profile.status,
        statusVariant: assetProfileStatusVariant(profile.status),
        fieldCountLabel: `${profile.fields.length} field${profile.fields.length === 1 ? "" : "s"}`,
        projectedFieldLabel: `${projectedFields} projected`,
        requiredCloseIdentifierLabel: requiredIdentifiers.length > 0
          ? requiredIdentifiers.join(", ")
          : "No close identifier",
        accountingImpactLabel: profile.accountingImpactHints.length > 0
          ? profile.accountingImpactHints.join(", ")
          : "No accounting hints",
        effectiveLabel: formatSettingsDateOnly(profile.effectiveFrom)
      };
    })
  };
}

export function buildOperationsControlCenter(payload: SettingsScreenPayload): SettingsOperationsControlCenter {
  const cards = [
    buildLedgerMappingControlCard(payload.ledgerMappingWorkbench ?? null, payload.loading === true),
    buildRolePermissionControlCard(payload.rolePermissionCatalog ?? null, payload.session, payload.loading === true),
    buildApprovalPolicyControlCard(payload.operationsApprovalPolicyMatrix ?? null, payload.loading === true),
    buildCloseCalendarControlCard(payload.operationsCloseCalendar ?? null, payload.loading === true)
  ];
  const loadedCount = cards.filter((card) => card.statusVariant !== "outline").length;
  const reviewCount = cards.filter((card) => card.statusVariant === "warning" || card.statusVariant === "danger").length;
  const checkingCount = cards.length - loadedCount;
  const statusVariant: SettingsOperationsControlCenter["statusVariant"] = checkingCount > 0
    ? "warning"
    : reviewCount > 0
      ? "warning"
      : "success";

  return {
    title: "Fund operations control center",
    summary: checkingCount > 0
      ? `${checkingCount} configuration surface${checkingCount === 1 ? "" : "s"} still loading; ${loadedCount} loaded.`
      : reviewCount > 0
        ? `${reviewCount} configuration surface${reviewCount === 1 ? "" : "s"} need operator review before close accounting is clean.`
        : "Ledger mappings, role authority, approval rules, and close posture are loaded for operator review.",
    statusLabel: checkingCount > 0
      ? `${checkingCount} checking`
      : reviewCount > 0
        ? `${reviewCount} review`
        : "Ready",
    statusVariant,
    loadedCountLabel: `${loadedCount} / ${cards.length}`,
    reviewCountLabel: String(reviewCount),
    listLabel: "Fund operations configuration surfaces",
    cards
  };
}

function assetProfileStatusVariant(
  status: SecurityAssetProfileDefinition["status"]
): SettingsAssetProfileRow["statusVariant"] {
  switch (status) {
    case "Approved":
      return "success";
    case "Draft":
      return "warning";
    case "Retired":
      return "danger";
    case "Superseded":
      return "outline";
    default:
      return "default";
  }
}

function buildLedgerMappingControlCard(
  workbench: LedgerMappingWorkbench | null,
  loading: boolean
): SettingsOperationsControlCard {
  if (!workbench) {
    return buildUnavailableOperationsControlCard(
      "ledger-mapping",
      "Ledger Mapping Workbench",
      "Maps fund accounts to ledger groups and exposes unmapped posting destinations.",
      FUND_STRUCTURE_API_ENDPOINTS.ledgerMappingWorkbench,
      "/settings#fund-operations-control-center",
      loading
    );
  }

  const unmapped = workbench.unmappedAccountCount;
  const statusVariant: SettingsOperationsControlCard["statusVariant"] = unmapped > 0 ? "warning" : "success";
  const firstUnmapped = workbench.accounts.find((account) => account.mapping.requiresUserMapping);
  return {
    id: "ledger-mapping",
    title: "Ledger Mapping Workbench",
    description: "Maps fund accounts to ledger groups and exposes unmapped posting destinations.",
    statusLabel: unmapped > 0 ? `${unmapped} unmapped` : "All mapped",
    statusVariant,
    endpointHref: FUND_STRUCTURE_API_ENDPOINTS.ledgerMappingWorkbench,
    routeHref: "/settings#fund-operations-control-center",
    routeLabel: "Review mappings",
    routeAriaLabel: "Review ledger mapping workbench",
    metrics: [
      { label: "Accounts", value: String(workbench.accountCount), tone: "default" },
      { label: "Mapped", value: String(workbench.mappedAccountCount), tone: unmapped === 0 ? "success" : "default" },
      { label: "Unmapped", value: String(unmapped), tone: unmapped > 0 ? "warning" : "muted" },
      { label: "Ledger groups", value: String(workbench.ledgerGroups.length), tone: "muted" }
    ],
    detail: firstUnmapped
      ? `${firstUnmapped.accountCode} needs mapping. ${firstUnmapped.recommendedAction}`
      : `Mapping view generated ${formatSettingsUtcMinute(workbench.asOf)}.`
  };
}

function buildRolePermissionControlCard(
  catalog: RolePermissionCatalog | null,
  session: SessionInfo | null,
  loading: boolean
): SettingsOperationsControlCard {
  if (!catalog) {
    return buildUnavailableOperationsControlCard(
      "role-permissions",
      "Role and Permission Studio",
      "Shows built-in roles, permission groups, and the active operator authority profile.",
      AUTH_API_ENDPOINTS.roles,
      AUTH_API_ENDPOINTS.roles,
      loading
    );
  }

  const activeRole = session ? catalog.roles.find((role) => (
    role.role === session.role || role.displayName === session.role
  )) : null;
  const permissionCount = activeRole?.permissions.length ?? 0;
  const statusVariant: SettingsOperationsControlCard["statusVariant"] = session && !activeRole ? "warning" : "success";
  return {
    id: "role-permissions",
    title: "Role and Permission Studio",
    description: "Shows built-in roles, permission groups, and the active operator authority profile.",
    statusLabel: activeRole ? `${activeRole.displayName} active` : `${catalog.roles.length} roles`,
    statusVariant,
    endpointHref: AUTH_API_ENDPOINTS.roles,
    routeHref: AUTH_API_ENDPOINTS.roles,
    routeLabel: "Open catalog",
    routeAriaLabel: "Open role and permission catalog service",
    metrics: [
      { label: "Roles", value: String(catalog.roles.length), tone: "default" },
      { label: "Permissions", value: String(catalog.permissions.length), tone: "default" },
      { label: "Current grants", value: activeRole ? String(permissionCount) : "—", tone: activeRole ? "success" : "warning" },
      { label: "Built-in", value: String(catalog.roles.filter((role) => role.isBuiltIn).length), tone: "muted" }
    ],
    detail: activeRole
      ? activeRole.description
      : "Active session role was not found in the loaded role catalog."
  };
}

function buildApprovalPolicyControlCard(
  matrix: OperationsApprovalPolicyMatrix | null,
  loading: boolean
): SettingsOperationsControlCard {
  if (!matrix) {
    return buildUnavailableOperationsControlCard(
      "approval-policy",
      "Approval Policy Matrix",
      "Shows required permissions, reviewer separation, report-pack, and checklist-control approval rules.",
      WORKSTATION_API_ENDPOINTS.operationsContinuityApprovalPolicyMatrix,
      WORKSTATION_API_ENDPOINTS.operationsContinuityApprovalPolicyMatrix,
      loading
    );
  }

  const independentRules = matrix.rows.filter((row) => row.requiresIndependentReviewer).length;
  const reportPackRules = matrix.rows.filter((row) => row.requiresReportPack).length;
  const checklistRules = matrix.rows.filter((row) => row.requiresChecklistControlApprovals).length;
  return {
    id: "approval-policy",
    title: "Approval Policy Matrix",
    description: "Shows required permissions, reviewer separation, report-pack, and checklist-control approval rules.",
    statusLabel: `${matrix.rows.length} rules`,
    statusVariant: "success",
    endpointHref: WORKSTATION_API_ENDPOINTS.operationsContinuityApprovalPolicyMatrix,
    routeHref: WORKSTATION_API_ENDPOINTS.operationsContinuityApprovalPolicyMatrix,
    routeLabel: "Open matrix",
    routeAriaLabel: "Open approval policy matrix service",
    metrics: [
      { label: "Version", value: matrix.version, tone: "muted" },
      { label: "Rules", value: String(matrix.rows.length), tone: "default" },
      { label: "Independent", value: String(independentRules), tone: independentRules > 0 ? "success" : "warning" },
      { label: "Report pack", value: String(reportPackRules), tone: "default" }
    ],
    detail: checklistRules > 0
      ? `${checklistRules} rule${checklistRules === 1 ? "" : "s"} require checklist-control approvals before close.`
      : `Policy generated ${formatSettingsUtcMinute(matrix.generatedAtUtc)}.`
  };
}

function buildCloseCalendarControlCard(
  calendar: OperationsCloseCalendar | null,
  loading: boolean
): SettingsOperationsControlCard {
  if (!calendar) {
    return buildUnavailableOperationsControlCard(
      "close-calendar",
      "Account Close Calendar",
      "Tracks period close due dates, blockers, checklist work, and approval progress by fund account.",
      WORKSTATION_API_ENDPOINTS.operationsContinuityCloseCalendar,
      "/accounting/operations-continuity",
      loading
    );
  }

  const blocked = calendar.items.filter((item) => item.blockerCount > 0 || item.status === "Blocked").length;
  const ready = calendar.items.filter((item) => item.isReadyToClose).length;
  const openChecklist = calendar.items.reduce((sum, item) => sum + item.openChecklistCount, 0);
  const nextDue = [...calendar.items]
    .filter((item) => item.nextDueDate)
    .sort((left, right) => String(left.nextDueDate).localeCompare(String(right.nextDueDate)))[0];
  const statusVariant: SettingsOperationsControlCard["statusVariant"] = blocked > 0
    ? "danger"
    : openChecklist > 0
      ? "warning"
      : "success";

  return {
    id: "close-calendar",
    title: "Account Close Calendar",
    description: "Tracks period close due dates, blockers, checklist work, and approval progress by fund account.",
    statusLabel: blocked > 0 ? `${blocked} blocked` : `${ready}/${calendar.items.length} ready`,
    statusVariant,
    endpointHref: WORKSTATION_API_ENDPOINTS.operationsContinuityCloseCalendar,
    routeHref: nextDue?.route ?? "/accounting/operations-continuity",
    routeLabel: "Open close workflow",
    routeAriaLabel: "Open account close workflow",
    metrics: [
      { label: "Workflows", value: String(calendar.items.length), tone: "default" },
      { label: "Ready", value: String(ready), tone: ready > 0 ? "success" : "muted" },
      { label: "Open checks", value: String(openChecklist), tone: openChecklist > 0 ? "warning" : "success" },
      { label: "Blockers", value: String(blocked), tone: blocked > 0 ? "danger" : "success" }
    ],
    detail: nextDue
      ? `${nextDue.periodId}: ${nextDue.nextDueLabel ?? "Next close task"} due ${formatSettingsDateOnly(nextDue.nextDueDate)} for ${nextDue.nextDueOwner ?? "unassigned"}.`
      : `Close calendar generated ${formatSettingsUtcMinute(calendar.generatedAtUtc)}.`
  };
}

function buildUnavailableOperationsControlCard(
  id: string,
  title: string,
  description: string,
  endpointHref: string,
  routeHref: string,
  loading: boolean
): SettingsOperationsControlCard {
  return {
    id,
    title,
    description,
    statusLabel: loading ? "Checking" : "Unavailable",
    statusVariant: "outline",
    endpointHref,
    routeHref,
    routeLabel: "Open service",
    routeAriaLabel: `Open ${title} service detail`,
    metrics: [
      { label: "Loaded", value: "No", tone: loading ? "muted" : "warning" },
      { label: "Access", value: "Read", tone: "muted" }
    ],
    detail: loading
      ? "Waiting for workspace settings to finish loading."
      : "This configuration data did not load during the workspace refresh."
  };
}
