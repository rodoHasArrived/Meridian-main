import { useCallback, useMemo, useState } from "react";
import { AlertTriangle, RefreshCw, Save } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import {
  approveSecurityMasterRevision as defaultApprove,
  classifyWorkbenchWriteError,
  publishSecurityMasterRevision as defaultPublish,
  resolveSourceConflict as defaultResolveConflict,
  submitSecurityMasterRevision as defaultSubmit,
  updateSecurityField as defaultUpdateField,
  type SecurityMasterEditResult,
  type SecurityMasterPublishResult,
  type SecurityMasterRevisionState
} from "@/lib/api/security-master-workbench.api";
import {
  buildConflictRows,
  buildPassportActions,
  buildPassportStateBadge,
  buildTrustPosture,
  buildWorkbenchErrorBanner,
  validateConflictOverride,
  type PassportBannerViewModel,
  type PassportConflictInput,
  type PassportLifecycleAction,
  type PassportSeverityTone,
  type PassportStateTone
} from "./security-passport-editor.view-model";

/** Injectable workbench client so the editor can be unit-tested without the network. */
export interface SecurityPassportWorkbenchService {
  updateField: typeof defaultUpdateField;
  resolveConflict: typeof defaultResolveConflict;
  submit: typeof defaultSubmit;
  approve: typeof defaultApprove;
  publish: typeof defaultPublish;
}

const defaultService: SecurityPassportWorkbenchService = {
  updateField: defaultUpdateField,
  resolveConflict: defaultResolveConflict,
  submit: defaultSubmit,
  approve: defaultApprove,
  publish: defaultPublish
};

export interface PassportApprovalContext {
  workflowId: string;
  expectedWorkflowVersion: number;
  reviewer: string;
  reportPackId: string;
}

export interface SecurityPassportEditorProps {
  securityId: string;
  symbol: string;
  assetClass?: string | null;
  /** The loaded passport version, captured as the optimistic-concurrency token. */
  version: number;
  trustPosture?: string | null;
  fundProfileId?: string | null;
  /** Unresolved source conflicts for this passport, rendered in the Source conflicts section. */
  conflicts?: PassportConflictInput[];
  service?: Partial<SecurityPassportWorkbenchService>;
  /** Invoked when the operator chooses to reload after a stale-version conflict. */
  onReloadRequested?: (toVersion: number | null) => void;
}

const SEVERITY_VARIANT: Record<PassportSeverityTone, "danger" | "warning" | "outline"> = {
  high: "danger",
  medium: "warning",
  low: "outline",
  none: "outline"
};

interface OverrideDraft {
  chosenWinnerSource: string;
  reason: string;
  acknowledgeDeviation: boolean;
}

interface WorkingRevision {
  revisionId: string;
  state: SecurityMasterRevisionState;
  version: number;
}

const STATE_BADGE_VARIANT: Record<PassportStateTone, "default" | "outline" | "success" | "warning" | "danger"> = {
  draft: "outline",
  submitted: "warning",
  approved: "default",
  published: "success",
  rejected: "danger"
};

const BANNER_CLASS: Record<PassportBannerViewModel["tone"], string> = {
  error: "border-[var(--danger)] text-[var(--danger)]",
  warning: "border-[var(--warning)] text-[var(--warning)]",
  info: "border-border text-foreground"
};

export function SecurityPassportEditor({
  securityId,
  symbol,
  assetClass,
  version,
  trustPosture,
  fundProfileId,
  conflicts = [],
  service,
  onReloadRequested
}: SecurityPassportEditorProps) {
  const client = useMemo<SecurityPassportWorkbenchService>(() => ({ ...defaultService, ...service }), [service]);

  const [revision, setRevision] = useState<WorkingRevision | null>(null);
  const [isBusy, setBusy] = useState(false);
  const [banner, setBanner] = useState<PassportBannerViewModel | null>(null);
  const [liveVersion, setLiveVersion] = useState(version);
  const [resolvedConflictIds, setResolvedConflictIds] = useState<readonly string[]>([]);
  const [overrideConflictId, setOverrideConflictId] = useState<string | null>(null);
  const [overrideDraft, setOverrideDraft] = useState<OverrideDraft>({
    chosenWinnerSource: "",
    reason: "",
    acknowledgeDeviation: false
  });

  const [fieldPath, setFieldPath] = useState("");
  const [newValue, setNewValue] = useState("");
  const [effectiveFrom, setEffectiveFrom] = useState("");
  const [justification, setJustification] = useState("");

  const [approval, setApproval] = useState<PassportApprovalContext>({
    workflowId: "",
    expectedWorkflowVersion: 0,
    reviewer: "",
    reportPackId: ""
  });

  const hasPendingEdit = fieldPath.trim().length > 0 && justification.trim().length > 0;
  const stateBadge = buildPassportStateBadge(revision?.state ?? null);
  const trust = buildTrustPosture(trustPosture);
  const expectedVersion = revision?.version ?? liveVersion;

  const actions = useMemo(
    () => buildPassportActions({ state: revision?.state ?? null, hasPendingEdit, isBusy }),
    [revision?.state, hasPendingEdit, isBusy]
  );

  const conflictRows = useMemo(
    () =>
      buildConflictRows(
        conflicts.map((c) => ({ ...c, isResolved: c.isResolved || resolvedConflictIds.includes(c.conflictId) })),
        { isBusy }
      ),
    [conflicts, resolvedConflictIds, isBusy]
  );

  const run = useCallback(
    async (work: () => Promise<void>) => {
      setBusy(true);
      setBanner(null);
      try {
        await work();
      } catch (error) {
        setBanner(buildWorkbenchErrorBanner(classifyWorkbenchWriteError(error)));
      } finally {
        setBusy(false);
      }
    },
    []
  );

  const applyEditResult = useCallback((result: SecurityMasterEditResult) => {
    setRevision({ revisionId: result.revisionId, state: result.state, version: result.newVersion });
  }, []);

  const handleSaveDraft = useCallback(() => {
    void run(async () => {
      const result = await client.updateField(securityId, {
        expectedVersion,
        fieldPath: fieldPath.trim(),
        newValue: newValue.length > 0 ? newValue : null,
        effectiveFrom: effectiveFrom.length > 0 ? effectiveFrom : new Date().toISOString(),
        justification: justification.trim(),
        fundProfileId: fundProfileId ?? null
      });
      applyEditResult(result);
    });
  }, [run, client, securityId, expectedVersion, fieldPath, newValue, effectiveFrom, justification, fundProfileId, applyEditResult]);

  const handleSubmit = useCallback(() => {
    if (!revision) return;
    void run(async () => {
      const result = await client.submit(securityId, {
        revisionId: revision.revisionId,
        workflowId: approval.workflowId.trim(),
        expectedWorkflowVersion: approval.expectedWorkflowVersion,
        reviewer: approval.reviewer.trim(),
        reportPackId: approval.reportPackId.trim() || null,
        fundProfileId: fundProfileId ?? null
      });
      applyEditResult(result);
    });
  }, [run, client, securityId, revision, approval, fundProfileId, applyEditResult]);

  const handleApprove = useCallback(() => {
    if (!revision) return;
    void run(async () => {
      const result = await client.approve(securityId, {
        revisionId: revision.revisionId,
        workflowId: approval.workflowId.trim(),
        expectedWorkflowVersion: approval.expectedWorkflowVersion,
        rationale: justification.trim() || "Approved via passport workbench.",
        reportPackId: approval.reportPackId.trim()
      });
      applyEditResult(result);
    });
  }, [run, client, securityId, revision, approval, justification, applyEditResult]);

  const handlePublish = useCallback(() => {
    if (!revision) return;
    void run(async () => {
      const result: SecurityMasterPublishResult = await client.publish(securityId, {
        revisionId: revision.revisionId
      });
      setRevision({ revisionId: result.revisionId, state: "Published", version: result.newVersion });
    });
  }, [run, client, securityId, revision]);

  const resolveConflict = useCallback(
    (conflictId: string, chosenWinnerSource: string, reason: string, acknowledgeDeviation: boolean) => {
      void run(async () => {
        const result = await client.resolveConflict(securityId, {
          conflictId,
          expectedVersion,
          chosenWinnerSource,
          reason,
          acknowledgePolicyDeviation: acknowledgeDeviation
        });
        setLiveVersion(result.newVersion);
        setResolvedConflictIds((ids) => (ids.includes(conflictId) ? ids : [...ids, conflictId]));
        setOverrideConflictId(null);
      });
    },
    [run, client, securityId, expectedVersion]
  );

  const handleAcceptConflict = useCallback(
    (conflict: PassportConflictInput) => {
      resolveConflict(conflict.conflictId, conflict.policyWinnerSource, "Accepted the recommended policy winner.", false);
    },
    [resolveConflict]
  );

  const openOverride = useCallback((conflict: PassportConflictInput) => {
    setOverrideConflictId(conflict.conflictId);
    setOverrideDraft({ chosenWinnerSource: conflict.challengerSource, reason: "", acknowledgeDeviation: false });
  }, []);

  const actionHandlers: Record<PassportLifecycleAction, () => void> = {
    "save-draft": handleSaveDraft,
    submit: handleSubmit,
    approve: handleApprove,
    publish: handlePublish
  };

  return (
    <Card data-testid="security-passport-editor">
      <CardHeader>
        <div className="flex flex-wrap items-center gap-2">
          <CardTitle className="mr-2">{symbol}</CardTitle>
          {assetClass ? <Badge variant="outline">{assetClass}</Badge> : null}
          <Badge variant={trust.tone === "blocked" ? "danger" : trust.tone === "trusted" ? "success" : "warning"} dot>
            {trust.label}
          </Badge>
          <Badge variant={STATE_BADGE_VARIANT[stateBadge.tone]}>{stateBadge.label}</Badge>
          <Badge variant="outline">v{expectedVersion}</Badge>
        </div>
        <CardDescription>Governed passport edit — changes are staged as a draft and published through the approval gate.</CardDescription>
      </CardHeader>

      <CardContent className="space-y-4">
        {banner ? (
          <div
            role="alert"
            className={cn("flex items-start gap-2 rounded-md border px-3 py-2 text-sm", BANNER_CLASS[banner.tone])}
          >
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
            <div className="space-y-1">
              <p className="font-medium">{banner.title}</p>
              <p>{banner.message}</p>
              {banner.reloadToVersion !== undefined ? (
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => onReloadRequested?.(banner.reloadToVersion ?? null)}
                >
                  <RefreshCw className="mr-1 h-3.5 w-3.5" aria-hidden /> Reload passport
                </Button>
              ) : null}
            </div>
          </div>
        ) : null}

        <fieldset className="grid gap-3 sm:grid-cols-2" disabled={isBusy}>
          <label className="space-y-1 text-sm">
            <span className="text-muted-foreground">Field path</span>
            <Input value={fieldPath} onChange={(e) => setFieldPath(e.target.value)} placeholder="EconomicDefinition.Coupon" />
          </label>
          <label className="space-y-1 text-sm">
            <span className="text-muted-foreground">New value</span>
            <Input value={newValue} onChange={(e) => setNewValue(e.target.value)} placeholder="5.125" />
          </label>
          <label className="space-y-1 text-sm">
            <span className="text-muted-foreground">Effective from</span>
            <Input type="date" value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)} />
          </label>
          <label className="space-y-1 text-sm">
            <span className="text-muted-foreground">Justification (required)</span>
            <Input
              value={justification}
              onChange={(e) => setJustification(e.target.value)}
              placeholder="Vendor confirmation #…"
            />
          </label>
        </fieldset>

        {(revision?.state ?? null) === "Draft" || (revision?.state ?? null) === "Submitted" ? (
          <fieldset className="grid gap-3 sm:grid-cols-2" disabled={isBusy}>
            <label className="space-y-1 text-sm">
              <span className="text-muted-foreground">Approval workflow id</span>
              <Input
                value={approval.workflowId}
                onChange={(e) => setApproval((a) => ({ ...a, workflowId: e.target.value }))}
              />
            </label>
            <label className="space-y-1 text-sm">
              <span className="text-muted-foreground">Independent reviewer</span>
              <Input
                value={approval.reviewer}
                onChange={(e) => setApproval((a) => ({ ...a, reviewer: e.target.value }))}
              />
            </label>
          </fieldset>
        ) : null}

        {conflictRows.length > 0 ? (
          <section className="space-y-2" aria-label="Source conflicts">
            <h3 className="text-sm font-medium">Source conflicts</h3>
            <ul className="space-y-2">
              {conflictRows.map((row) => {
                const overrideOpen = overrideConflictId === row.conflictId;
                const validation = validateConflictOverride({
                  chosenWinnerSource: overrideDraft.chosenWinnerSource,
                  policyWinnerSource: row.policyWinnerSource,
                  reason: overrideDraft.reason,
                  acknowledgeDeviation: overrideDraft.acknowledgeDeviation
                });
                return (
                  <li key={row.conflictId} className="rounded-md border px-3 py-2 text-sm">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-medium">{row.fieldLabel}</span>
                      <Badge variant={SEVERITY_VARIANT[row.severityTone]}>{row.severityLabel}</Badge>
                      <span className="text-muted-foreground">
                        winner <code>{row.policyWinnerSource}</code> · challenger <code>{row.challengerSource}</code>
                      </span>
                      <div className="ml-auto flex gap-2">
                        <Button type="button" size="sm" variant="secondary" disabled={!row.canAct} onClick={() => handleAcceptConflict(row)}>
                          Accept
                        </Button>
                        <Button type="button" size="sm" variant="outline" disabled={!row.canAct} onClick={() => openOverride(row)}>
                          Override…
                        </Button>
                      </div>
                    </div>
                    {overrideOpen ? (
                      <div className="mt-2 space-y-2 border-t pt-2" aria-label={`Override ${row.fieldLabel}`}>
                        <label className="space-y-1 text-sm block">
                          <span className="text-muted-foreground">Winning source</span>
                          <Input
                            value={overrideDraft.chosenWinnerSource}
                            onChange={(e) => setOverrideDraft((d) => ({ ...d, chosenWinnerSource: e.target.value }))}
                          />
                        </label>
                        <label className="space-y-1 text-sm block">
                          <span className="text-muted-foreground">Reason (required)</span>
                          <Input
                            value={overrideDraft.reason}
                            onChange={(e) => setOverrideDraft((d) => ({ ...d, reason: e.target.value }))}
                          />
                        </label>
                        <label className="flex items-center gap-2 text-sm">
                          <input
                            type="checkbox"
                            checked={overrideDraft.acknowledgeDeviation}
                            onChange={(e) => setOverrideDraft((d) => ({ ...d, acknowledgeDeviation: e.target.checked }))}
                          />
                          <span>Acknowledge deviation from the policy winner</span>
                        </label>
                        <div className="flex items-center gap-2">
                          <Button
                            type="button"
                            size="sm"
                            disabled={!validation.valid || isBusy}
                            title={validation.reason ?? undefined}
                            onClick={() =>
                              resolveConflict(
                                row.conflictId,
                                overrideDraft.chosenWinnerSource.trim(),
                                overrideDraft.reason.trim(),
                                overrideDraft.acknowledgeDeviation
                              )
                            }
                          >
                            Resolve
                          </Button>
                          <Button type="button" size="sm" variant="ghost" onClick={() => setOverrideConflictId(null)}>
                            Cancel
                          </Button>
                          {!validation.valid ? <span className="text-xs text-muted-foreground">{validation.reason}</span> : null}
                        </div>
                      </div>
                    ) : null}
                  </li>
                );
              })}
            </ul>
          </section>
        ) : null}

        <div className="flex flex-wrap gap-2">
          {actions.map((action) => (
            <Button
              key={action.action}
              type="button"
              variant={action.action === "save-draft" ? "secondary" : "default"}
              disabled={!action.enabled}
              title={action.disabledReason ?? undefined}
              aria-label={action.disabledReason ? `${action.label} — ${action.disabledReason}` : action.label}
              onClick={actionHandlers[action.action]}
            >
              {action.action === "save-draft" ? <Save className="mr-1 h-4 w-4" aria-hidden /> : null}
              {action.label}
            </Button>
          ))}
        </div>
      </CardContent>
    </Card>
  );
}
