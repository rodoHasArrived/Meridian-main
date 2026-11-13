// Session, workspace, workflow, feature-capability, user/access, asset-profile, and ledger-mapping
// endpoints. Re-exported through @/lib/api.
import type { ExtensibilityActivationReadiness, ExtensibilityCatalog, InvestmentAccountingTransactionLabPreview, InvestmentAccountingTransactionLabRequest, OperatorInbox, OperatorWorkflowHomeSummary, TenantTemplateActivationRequest, TenantTemplateActivationResult, TenantTemplateConfigurationBundle, StrategyBriefingResponse, StrategyWorkspaceResponse, SecurityAssetProfileApprovalRequest, SecurityAssetProfileDefinition, SecurityAssetProfileDraftRequest, SecurityAssetProfileGovernanceResult, SecurityAssetProfileLineage, SecurityAssetProfileRollbackRequest, SessionInfo, TradingOperatorReadiness, TradingWorkspaceResponse, WorkflowLibrary, WorkflowPreset, WorkflowPresetLibrary, WorkflowPresetSaveRequest, FeatureCapabilitySettingsResponse, LedgerMappingAssignmentRequest, LedgerMappingAssignmentResult, LedgerMappingWorkbench, RolePermissionCatalog, RolePermissionProfileUpsertRequest, RolePermissionProfileUpsertResult, UserAccount, UserAccountAuditEvent, UserAccountDisableRequest, UserAccountMutationResult, UserAccountUpsertRequest, UserAccessAssignment, UserAccessAssignmentCreateRequest, UserAccessAssignmentMutationResult, UserAccessAssignmentQuery, UserAccessAssignmentRevokeRequest, UserPasswordResetRequest, UserSessionRevokeRequest, UserSessionRevokeResult } from "@/types";
import { AUTH_API_ENDPOINTS, FUND_STRUCTURE_API_ENDPOINTS, WORKSTATION_API_ENDPOINTS, securityMasterAssetProfileApproveEndpoint, securityMasterAssetProfileDraftsEndpoint, securityMasterAssetProfileLineageEndpoint, securityMasterAssetProfileRollbackEndpoint, securityMasterAssetProfilesEndpoint, workstationExtensibilityTenantTemplateActivateEndpoint, workstationExtensibilityTenantTemplateActivationsEndpoint, workstationExtensibilityTenantTemplateEndpoint, workstationExtensibilityTenantTemplateReadinessEndpoint, workstationOperatorInboxEndpoint, workstationTradingEndpoint, workstationTradingReadinessEndpoint, workstationWorkflowSummaryEndpoint, workstationWorkflowPresetEndpoint, workstationWorkflowPresetPinEndpoint, workstationWorkflowPresetUsedEndpoint } from "@/lib/workstation-endpoints";
import { deleteJson, getJson, postJson, putJson, type ApiRequestOptions } from "@/lib/api/http-client";

export function getSession(options: ApiRequestOptions = {}) {
  return getJson<SessionInfo>(WORKSTATION_API_ENDPOINTS.session, options);
}

export function getStrategyWorkspace(options: ApiRequestOptions = {}) {
  return getJson<StrategyWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.strategy, options);
}

export function getStrategyBriefing(options: ApiRequestOptions = {}) {
  return getJson<StrategyBriefingResponse>(WORKSTATION_API_ENDPOINTS.strategyBriefing, options);
}

export function getTradingWorkspace(options: ApiRequestOptions & { fundAccountId?: string } = {}) {
  const { fundAccountId, ...requestOptions } = options;
  return getJson<TradingWorkspaceResponse>(workstationTradingEndpoint(fundAccountId), requestOptions);
}

export function getTradingReadiness(options: ApiRequestOptions & { fundAccountId?: string } = {}) {
  const { fundAccountId, ...requestOptions } = options;
  return getJson<TradingOperatorReadiness>(workstationTradingReadinessEndpoint(fundAccountId), requestOptions);
}

export function synchronizeTradingBrokerage(fundAccountId: string, options: ApiRequestOptions = {}) {
  return postJson<TradingOperatorReadiness>(WORKSTATION_API_ENDPOINTS.tradingBrokerageRecovery, { fundAccountId }, options);
}

export function getOperatorInbox(fundAccountId?: string, options: ApiRequestOptions = {}) {
  return getJson<OperatorInbox>(workstationOperatorInboxEndpoint(fundAccountId), options);
}

export function getWorkstationWorkflowSummary(options: ApiRequestOptions & {
  hasOperatingContext?: boolean;
  operatingContext?: string;
  fundProfileId?: string;
  fundAccountId?: string;
  fundDisplayName?: string;
} = {}) {
  const { signal, ...queryOptions } = options;
  return getJson<OperatorWorkflowHomeSummary>(workstationWorkflowSummaryEndpoint(queryOptions), { signal });
}

export function getWorkflowLibrary(options: ApiRequestOptions = {}) {
  return getJson<WorkflowLibrary>(WORKSTATION_API_ENDPOINTS.workflowLibrary, options);
}

export function getWorkflowPresets(options: ApiRequestOptions = {}) {
  return getJson<WorkflowPresetLibrary>(WORKSTATION_API_ENDPOINTS.workflowPresets, options);
}

export function getFeatureCapabilities(options: ApiRequestOptions = {}) {
  return getJson<FeatureCapabilitySettingsResponse>(WORKSTATION_API_ENDPOINTS.featureCapabilities, options);
}

export function getExtensibilityCatalog(options: ApiRequestOptions = {}) {
  return getJson<ExtensibilityCatalog>(WORKSTATION_API_ENDPOINTS.extensibilityCatalog, options);
}

export function listExtensibilityTenantTemplates(options: ApiRequestOptions = {}) {
  return getJson<TenantTemplateConfigurationBundle[]>(WORKSTATION_API_ENDPOINTS.extensibilityTenantTemplates, options);
}

export function saveExtensibilityTenantTemplate(
  tenantTemplate: TenantTemplateConfigurationBundle,
  options: ApiRequestOptions = {}
) {
  return putJson<TenantTemplateConfigurationBundle>(
    workstationExtensibilityTenantTemplateEndpoint(tenantTemplate.tenantTemplateId),
    tenantTemplate,
    options
  );
}

export function activateExtensibilityTenantTemplate(
  tenantTemplateId: string,
  request: TenantTemplateActivationRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<TenantTemplateActivationResult>(
    workstationExtensibilityTenantTemplateActivateEndpoint(tenantTemplateId),
    request,
    options
  );
}

export function listExtensibilityTenantTemplateActivations(tenantTemplateId: string, options: ApiRequestOptions = {}) {
  return getJson<TenantTemplateActivationResult[]>(
    workstationExtensibilityTenantTemplateActivationsEndpoint(tenantTemplateId),
    options
  );
}

export function getExtensibilityTenantTemplateReadiness(tenantTemplateId: string, options: ApiRequestOptions = {}) {
  return getJson<ExtensibilityActivationReadiness>(
    workstationExtensibilityTenantTemplateReadinessEndpoint(tenantTemplateId),
    options
  );
}

export function getRolePermissionCatalog(options: ApiRequestOptions = {}) {
  return getJson<RolePermissionCatalog>(AUTH_API_ENDPOINTS.roles, options);
}

export function createRolePermissionProfile(
  request: RolePermissionProfileUpsertRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<RolePermissionProfileUpsertResult>(AUTH_API_ENDPOINTS.roleProfiles, request, options);
}

export function getUserAccounts(options: ApiRequestOptions = {}) {
  return getJson<UserAccount[]>(AUTH_API_ENDPOINTS.accounts, options);
}

export function upsertUserAccount(request: UserAccountUpsertRequest, options: ApiRequestOptions = {}) {
  return putJson<UserAccountMutationResult>(authAccountEndpoint(request.username), request, options);
}

export function resetUserPassword(request: UserPasswordResetRequest, options: ApiRequestOptions = {}) {
  return postJson<UserAccountMutationResult>(authAccountPasswordResetEndpoint(request.username), request, options);
}

export function setUserAccountDisabled(request: UserAccountDisableRequest, options: ApiRequestOptions = {}) {
  return postJson<UserAccountMutationResult>(authAccountDisableEndpoint(request.username), request, options);
}

export function revokeUserSessions(request: UserSessionRevokeRequest, options: ApiRequestOptions = {}) {
  return postJson<UserSessionRevokeResult>(AUTH_API_ENDPOINTS.sessionsRevoke, request, options);
}

export function getUserAccountAudit(options: ApiRequestOptions = {}) {
  return getJson<UserAccountAuditEvent[]>(AUTH_API_ENDPOINTS.audit, options);
}

export function listScopedAccessAssignments(
  query: UserAccessAssignmentQuery = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  if (query.principalId) {
    params.set("principalId", query.principalId);
  }

  if (query.scopeKind) {
    params.set("scopeKind", query.scopeKind);
  }

  if (query.scopeId) {
    params.set("scopeId", query.scopeId);
  }

  if (query.includeRevoked) {
    params.set("includeRevoked", "true");
  }

  const suffix = params.toString();
  return getJson<UserAccessAssignment[]>(
    suffix ? `${AUTH_API_ENDPOINTS.accessAssignments}?${suffix}` : AUTH_API_ENDPOINTS.accessAssignments,
    options
  );
}

export function createScopedAccessAssignment(
  request: UserAccessAssignmentCreateRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<UserAccessAssignmentMutationResult>(AUTH_API_ENDPOINTS.accessAssignments, request, options);
}

export function revokeScopedAccessAssignment(
  request: UserAccessAssignmentRevokeRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<UserAccessAssignmentMutationResult>(
    authAccessAssignmentRevokeEndpoint(request.assignmentId),
    request,
    options
  );
}

function authAccountEndpoint(username: string) {
  return AUTH_API_ENDPOINTS.accountByUsername.replace("{username}", encodeURIComponent(username));
}

function authAccountPasswordResetEndpoint(username: string) {
  return AUTH_API_ENDPOINTS.accountPasswordReset.replace("{username}", encodeURIComponent(username));
}

function authAccountDisableEndpoint(username: string) {
  return AUTH_API_ENDPOINTS.accountDisable.replace("{username}", encodeURIComponent(username));
}

function authAccessAssignmentRevokeEndpoint(assignmentId: string) {
  return AUTH_API_ENDPOINTS.accessAssignmentRevoke.replace("{assignmentId}", encodeURIComponent(assignmentId));
}

export function getSecurityAssetProfiles(options: ApiRequestOptions = {}) {
  return getJson<SecurityAssetProfileDefinition[]>(securityMasterAssetProfilesEndpoint(), options);
}

export function getSecurityAssetProfileLineage(profileId: string, options: ApiRequestOptions = {}) {
  return getJson<SecurityAssetProfileLineage>(securityMasterAssetProfileLineageEndpoint(profileId), options);
}

export function draftSecurityAssetProfile(
  request: SecurityAssetProfileDraftRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<SecurityAssetProfileGovernanceResult>(securityMasterAssetProfileDraftsEndpoint(), request, options);
}

export function approveSecurityAssetProfile(
  request: SecurityAssetProfileApprovalRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<SecurityAssetProfileGovernanceResult>(securityMasterAssetProfileApproveEndpoint(), request, options);
}

export function rollbackSecurityAssetProfile(
  request: SecurityAssetProfileRollbackRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<SecurityAssetProfileGovernanceResult>(securityMasterAssetProfileRollbackEndpoint(), request, options);
}

export function getLedgerMappingWorkbench(options: ApiRequestOptions = {}) {
  return getJson<LedgerMappingWorkbench>(FUND_STRUCTURE_API_ENDPOINTS.ledgerMappingWorkbench, options);
}

export function assignLedgerMapping(request: LedgerMappingAssignmentRequest, options: ApiRequestOptions = {}) {
  return postJson<LedgerMappingAssignmentResult>(
    FUND_STRUCTURE_API_ENDPOINTS.ledgerMappingAssignments,
    request,
    options
  );
}

export function previewInvestmentAccountingTransaction(
  request: InvestmentAccountingTransactionLabRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<InvestmentAccountingTransactionLabPreview>(
    FUND_STRUCTURE_API_ENDPOINTS.transactionLabPreview,
    request,
    options
  );
}

export function setFeatureCapability(capabilityKey: string, isEnabled: boolean, options: ApiRequestOptions = {}) {
  return putJson<FeatureCapabilitySettingsResponse>(
    `${WORKSTATION_API_ENDPOINTS.featureCapabilities}/${encodeURIComponent(capabilityKey)}`,
    { isEnabled },
    options
  );
}

export function saveWorkflowPreset(request: WorkflowPresetSaveRequest) {
  return postJson<WorkflowPreset>(workstationWorkflowPresetEndpoint(), request);
}

export function updateWorkflowPreset(presetId: string, request: WorkflowPresetSaveRequest) {
  return putJson<WorkflowPreset>(workstationWorkflowPresetEndpoint(presetId), request);
}

export function pinWorkflowPreset(presetId: string, isPinned: boolean) {
  return postJson<WorkflowPreset>(workstationWorkflowPresetPinEndpoint(presetId), { isPinned });
}

export function markWorkflowPresetUsed(presetId: string) {
  return postJson<WorkflowPreset>(workstationWorkflowPresetUsedEndpoint(presetId));
}

export function deleteWorkflowPreset(presetId: string) {
  return deleteJson<void>(workstationWorkflowPresetEndpoint(presetId));
}
