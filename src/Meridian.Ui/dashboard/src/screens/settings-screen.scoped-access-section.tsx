// Scoped access section of the Settings screen: its private state/handlers hook and presentational component.
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { createScopedAccessAssignment, revokeScopedAccessAssignment } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { formatNumber as formatNumberAmount, formatUtcDate } from "@/lib/format";
import { cn } from "@/lib/utils";
import { type SettingsMutationConfirmation } from "@/screens/settings-mutation-confirm-dialog";
import { humanizeSettingsIdentifier } from "@/screens/settings-screen.operations-form-options";
import type { AccessPrincipalKind, AccessScopeKind, RolePermissionCatalog, SessionInfo, UserAccessAssignment } from "@/types";
import { Save, ShieldCheck, Trash2 } from "lucide-react";
import { type FormEvent } from "react";
import { diagnosticToneClass, FilterSelect, SettingsFieldRow } from "./settings-provider-integration";
import { SettingsChip, settingsBannerTone } from "@/screens/settings-screen.section-primitives";
import { type ScopedAccessAssignmentState } from "@/screens/settings-screen.section-state";

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

interface UseSettingsScopedAccessSectionStateParams {
  onRefresh: (() => Promise<void> | void) | undefined;
  rolePermissionCatalog: RolePermissionCatalog | null;
  scopedAccess: ScopedAccessAssignmentState;
  scopedAccessAssignments: UserAccessAssignment[];
  session: SessionInfo | null;
  setMutationConfirmation: React.Dispatch<React.SetStateAction<SettingsMutationConfirmation | null>>;
  setScopedAccess: React.Dispatch<React.SetStateAction<ScopedAccessAssignmentState>>;
  setScopedAccessAssignments: React.Dispatch<React.SetStateAction<UserAccessAssignment[]>>;
}

export function useSettingsScopedAccessSectionState({ onRefresh, rolePermissionCatalog, scopedAccess, scopedAccessAssignments, session, setMutationConfirmation, setScopedAccess, setScopedAccessAssignments }: UseSettingsScopedAccessSectionStateParams) {
  const scopedAccessActiveCount = scopedAccessAssignments.filter((assignment) => !assignment.revokedAtUtc).length;
  const scopedAccessRevokedCount = scopedAccessAssignments.length - scopedAccessActiveCount;
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

  return { revokeScopedAccess, scopedAccessActiveCount, scopedAccessRevokedCount, selectScopedAccessRole, submitScopedAccessAssignment, toggleScopedAccessPermission };
}

/** Scoped access assignments: grant, revoke, and review principal access by scope. */
interface SettingsScopedAccessSectionProps {
  scopedAccessActiveCount: ReturnType<typeof useSettingsScopedAccessSectionState>["scopedAccessActiveCount"];
  scopedAccessRevokedCount: ReturnType<typeof useSettingsScopedAccessSectionState>["scopedAccessRevokedCount"];
  scopedAccess: ScopedAccessAssignmentState;
  setScopedAccess: React.Dispatch<React.SetStateAction<ScopedAccessAssignmentState>>;
  scopedAccessAssignments: UserAccessAssignment[];
  setMutationConfirmation: React.Dispatch<React.SetStateAction<SettingsMutationConfirmation | null>>;
  revokeScopedAccess: ReturnType<typeof useSettingsScopedAccessSectionState>["revokeScopedAccess"];
  submitScopedAccessAssignment: ReturnType<typeof useSettingsScopedAccessSectionState>["submitScopedAccessAssignment"];
  roleProfileDraft: { canSave: boolean; statusLabel: string; disabledReason: string | null; baseRoleOptions: Array<{ value: string; label: string; }>; permissionOptions: Array<{ value: string; label: string; group: string; }>; defaultBaseRole: string; defaultPermissionNames: string[]; };
  selectScopedAccessRole: ReturnType<typeof useSettingsScopedAccessSectionState>["selectScopedAccessRole"];
  scopedAccessRoleOptions: { value: string; label: string; }[];
  scopedAccessRoleProfileOptions: { value: string; label: string; }[];
  toggleScopedAccessPermission: ReturnType<typeof useSettingsScopedAccessSectionState>["toggleScopedAccessPermission"];
}

export function SettingsScopedAccessSection({ scopedAccessActiveCount, scopedAccessRevokedCount, scopedAccess, setScopedAccess, scopedAccessAssignments, setMutationConfirmation, revokeScopedAccess, submitScopedAccessAssignment, roleProfileDraft, selectScopedAccessRole, scopedAccessRoleOptions, scopedAccessRoleProfileOptions, toggleScopedAccessPermission }: SettingsScopedAccessSectionProps) {
  return (
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
  );
}
