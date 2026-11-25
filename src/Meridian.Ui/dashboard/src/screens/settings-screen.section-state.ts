// Draft-state shapes and factories shared by the Settings screen and its section modules.
import type { AccessPrincipalKind, AccessScopeKind, SecurityAssetProfileDefinition, SecurityAssetProfileGovernanceResult, SecurityAssetProfileLineage } from "@/types";

export interface LedgerMappingAssignmentState {
  accountId: string;
  ledgerGroupId: string;
  rationale: string;
  busy: boolean;
  message: string | null;
  details: string[];
  tone: "default" | "success" | "danger";
}

export interface RolePermissionProfileState {
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

export interface ScopedAccessAssignmentState {
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

export interface ApprovalPolicyRuleState {
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

export interface CloseCalendarItemState {
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

export interface AssetProfileDraftState {
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

export function createAssetProfileDraftState(profile: SecurityAssetProfileDefinition | null): AssetProfileDraftState {
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
