// Fund operations control center section of the Settings screen: its private state/handlers hook and
// presentational component.
import { SeverityBadge } from "@/components/operations";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { assignLedgerMapping, createRolePermissionProfile, upsertOperationsApprovalPolicyRule, upsertOperationsCloseCalendarItem } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { badgeVariantToOperatorSeverity } from "@/lib/shared-tone-mappings";
import { cn } from "@/lib/utils";
import { buildApprovalPolicyPermissionOptions } from "@/screens/settings-screen.operations-form-options";
import { type SettingsScreenViewModel } from "@/screens/settings-screen.view-model";
import type { OperationsApprovalPolicyMatrixRow, OperationsCloseCalendarItem, RolePermissionCatalog, SessionInfo } from "@/types";
import { ArrowRight, ExternalLink, Save, ShieldCheck } from "lucide-react";
import { type FormEvent } from "react";
import { Link } from "react-router-dom";
import { diagnosticToneClass, FilterSelect, SettingsFieldRow } from "./settings-provider-integration";
import { SettingsChip, capabilityTone, settingsBannerTone } from "@/screens/settings-screen.section-primitives";
import { type ApprovalPolicyRuleState, type CloseCalendarItemState, type LedgerMappingAssignmentState, type RolePermissionProfileState } from "@/screens/settings-screen.section-state";

interface UseSettingsOperationsSectionStateParams {
  approvalPolicyDraft: { canSave: boolean; statusLabel: string; disabledReason: string | null; rows: OperationsApprovalPolicyMatrixRow[]; policyOptions: Array<{ value: string; label: string; }>; };
  approvalPolicyRule: ApprovalPolicyRuleState;
  closeCalendarDraft: { canSave: boolean; statusLabel: string; disabledReason: string | null; items: OperationsCloseCalendarItem[]; workflowOptions: Array<{ value: string; label: string; }>; };
  closeCalendarItem: CloseCalendarItemState;
  ledgerMappingAssignment: LedgerMappingAssignmentState;
  onRefresh: (() => Promise<void> | void) | undefined;
  rolePermissionProfile: RolePermissionProfileState;
  roleProfileDraft: { canSave: boolean; statusLabel: string; disabledReason: string | null; baseRoleOptions: Array<{ value: string; label: string; }>; permissionOptions: Array<{ value: string; label: string; group: string; }>; defaultBaseRole: string; defaultPermissionNames: string[]; };
  session: SessionInfo | null;
  setApprovalPolicyRule: React.Dispatch<React.SetStateAction<ApprovalPolicyRuleState>>;
  setCloseCalendarItem: React.Dispatch<React.SetStateAction<CloseCalendarItemState>>;
  setLedgerMappingAssignment: React.Dispatch<React.SetStateAction<LedgerMappingAssignmentState>>;
  setRolePermissionProfile: React.Dispatch<React.SetStateAction<RolePermissionProfileState>>;
}

export function useSettingsOperationsSectionState({ approvalPolicyDraft, approvalPolicyRule, closeCalendarDraft, closeCalendarItem, ledgerMappingAssignment, onRefresh, rolePermissionProfile, roleProfileDraft, session, setApprovalPolicyRule, setCloseCalendarItem, setLedgerMappingAssignment, setRolePermissionProfile }: UseSettingsOperationsSectionStateParams) {
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

  return { selectApprovalPolicyRule, selectCloseCalendarItem, submitApprovalPolicyRule, submitCloseCalendarItem, submitLedgerMappingAssignment, submitRolePermissionProfile, toggleRoleProfilePermission };
}

/** Fund operations control center: ledger mapping, role profiles, approval policy, and close calendar. */
interface SettingsOperationsSectionProps {
  vm: SettingsScreenViewModel;
  submitLedgerMappingAssignment: ReturnType<typeof useSettingsOperationsSectionState>["submitLedgerMappingAssignment"];
  ledgerMappingDraft: { canAssign: boolean; statusLabel: string; disabledReason: string | null; accountOptions: Array<{ value: string; label: string; }>; ledgerGroupOptions: Array<{ value: string; label: string; }>; };
  ledgerMappingAssignment: LedgerMappingAssignmentState;
  setLedgerMappingAssignment: React.Dispatch<React.SetStateAction<LedgerMappingAssignmentState>>;
  submitRolePermissionProfile: ReturnType<typeof useSettingsOperationsSectionState>["submitRolePermissionProfile"];
  roleProfileDraft: { canSave: boolean; statusLabel: string; disabledReason: string | null; baseRoleOptions: Array<{ value: string; label: string; }>; permissionOptions: Array<{ value: string; label: string; group: string; }>; defaultBaseRole: string; defaultPermissionNames: string[]; };
  rolePermissionProfile: RolePermissionProfileState;
  setRolePermissionProfile: React.Dispatch<React.SetStateAction<RolePermissionProfileState>>;
  rolePermissionCatalog: RolePermissionCatalog | null | undefined;
  toggleRoleProfilePermission: ReturnType<typeof useSettingsOperationsSectionState>["toggleRoleProfilePermission"];
  submitApprovalPolicyRule: ReturnType<typeof useSettingsOperationsSectionState>["submitApprovalPolicyRule"];
  approvalPolicyDraft: { canSave: boolean; statusLabel: string; disabledReason: string | null; rows: OperationsApprovalPolicyMatrixRow[]; policyOptions: Array<{ value: string; label: string; }>; };
  approvalPolicyRule: ApprovalPolicyRuleState;
  selectApprovalPolicyRule: ReturnType<typeof useSettingsOperationsSectionState>["selectApprovalPolicyRule"];
  setApprovalPolicyRule: React.Dispatch<React.SetStateAction<ApprovalPolicyRuleState>>;
  submitCloseCalendarItem: ReturnType<typeof useSettingsOperationsSectionState>["submitCloseCalendarItem"];
  closeCalendarDraft: { canSave: boolean; statusLabel: string; disabledReason: string | null; items: OperationsCloseCalendarItem[]; workflowOptions: Array<{ value: string; label: string; }>; };
  closeCalendarItem: CloseCalendarItemState;
  selectCloseCalendarItem: ReturnType<typeof useSettingsOperationsSectionState>["selectCloseCalendarItem"];
  setCloseCalendarItem: React.Dispatch<React.SetStateAction<CloseCalendarItemState>>;
}

export function SettingsOperationsSection({ vm, submitLedgerMappingAssignment, ledgerMappingDraft, ledgerMappingAssignment, setLedgerMappingAssignment, submitRolePermissionProfile, roleProfileDraft, rolePermissionProfile, setRolePermissionProfile, rolePermissionCatalog, toggleRoleProfilePermission, submitApprovalPolicyRule, approvalPolicyDraft, approvalPolicyRule, selectApprovalPolicyRule, setApprovalPolicyRule, submitCloseCalendarItem, closeCalendarDraft, closeCalendarItem, selectCloseCalendarItem, setCloseCalendarItem }: SettingsOperationsSectionProps) {
  return (
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
  );
}
