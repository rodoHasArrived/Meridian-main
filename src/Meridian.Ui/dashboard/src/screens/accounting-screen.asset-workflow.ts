import { useCallback, useEffect, useRef, useState } from "react";
import { describeApiError } from "@/lib/api-errors";
import { assetCapabilitiesMatchScope, assetCommandMatchesScope, postedAssetJournalMatchesScope, sameAssetPrincipal } from "@/lib/api/asset-accounting-workflow.api";
import type { ApiRequestOptions } from "@/lib/api";
import type { AssetAccountingDraftCapabilities, AssetPostedJournalReceipt, AssetWorkflowScope } from "@/types/asset-accounting-workflow";
import type { JournalEntryLifecycleActionRequest, JournalEntryLifecycleActionResult, ManualJournalEntryDraft } from "@/types";

type AssetAction = "Approve" | "Post";
interface PendingCommand { scopeKey: string; candidateFingerprint: string; request: JournalEntryLifecycleActionRequest }
interface Options {
  draft: ManualJournalEntryDraft;
  active: boolean;
  sessionIdentity?: string | null;
  load?: (scope: AssetWorkflowScope, options?: ApiRequestOptions) => Promise<AssetAccountingDraftCapabilities>;
  execute: (request: JournalEntryLifecycleActionRequest) => Promise<JournalEntryLifecycleActionResult>;
  onResult: (result: JournalEntryLifecycleActionResult) => void;
  refreshWorkbench: () => Promise<void>;
}
const storageKey = (scopeKey: string) => `meridian.asset-accounting.command.v1:${scopeKey}`;
function readPending(scopeKey: string): PendingCommand | null {
  try {
    const result = JSON.parse(sessionStorage.getItem(storageKey(scopeKey)) ?? "null") as PendingCommand | null;
    return result?.scopeKey === scopeKey && result.request?.correlationId && result.candidateFingerprint
      && ["Approve", "Post"].includes(result.request.action) ? result : null;
  } catch { return null; }
}
function retain(scopeKey: string, pending: PendingCommand | null) {
  try {
    if (pending) sessionStorage.setItem(storageKey(scopeKey), JSON.stringify(pending));
    else sessionStorage.removeItem(storageKey(scopeKey));
  } catch { /* In-memory exact retry remains available when session storage is unavailable. */ }
}

export function useAssetAccountingWorkflow({ draft, active, sessionIdentity = null, load, execute, onResult, refreshWorkbench }: Options) {
  const scope: AssetWorkflowScope | null = draft.assetAccountingDraft && draft.ledgerBookId && draft.periodId
    ? { journalEntryId: draft.journalEntryId, fundProfileId: draft.fundProfileId, ledgerBookId: draft.ledgerBookId, periodId: draft.periodId, draftVersion: draft.version, accountingBasis: draft.accountingBasis } : null;
  const scopeKey = scope ? JSON.stringify([sessionIdentity, scope.fundProfileId, scope.ledgerBookId, scope.periodId, scope.journalEntryId]) : "";
  const loadKey = `${scopeKey}:${draft.version}:${draft.accountingBasis}`;
  const previousSession = useRef(sessionIdentity);
  const current = useRef({ scope, scopeKey, loadKey, active });
  current.current = { scope, scopeKey, loadKey, active };
  const [snapshot, setSnapshot] = useState<{ key: string; value: AssetAccountingDraftCapabilities } | null>(null);
  const [pending, setPending] = useState<PendingCommand | null>(null);
  const pendingRef = useRef(pending);
  pendingRef.current = pending;
  const [busyAction, setBusyAction] = useState<AssetAction | null>(null);
  const busy = useRef(false);
  const [loading, setLoading] = useState(false);
  const [errorText, setErrorText] = useState<string | null>(null);
  const [statusText, setStatusText] = useState<string | null>(null);
  const [reviewNote, setReviewNote] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [reloadCount, setReloadCount] = useState(0);
  const [posted, setPosted] = useState<{ key: string; receipt: AssetPostedJournalReceipt } | null>(null);
  const capability = active && snapshot?.key === loadKey ? snapshot.value : null;
  const currentPending = pending?.scopeKey === scopeKey ? pending : null;
  const retainedPostPending = !!draft.assetAccountingPostIntent && !draft.assetAccountingPostingResult;
  const liveCapability = useRef(capability);
  liveCapability.current = capability;

  useEffect(() => {
    const selected = current.current;
    if (previousSession.current !== sessionIdentity) {
      if (pendingRef.current) retain(pendingRef.current.scopeKey, null);
      pendingRef.current = null; setPending(null); liveCapability.current = null; setSnapshot(null); previousSession.current = sessionIdentity;
    }
    if (!selected.scope || !active || !load) return;
    const controller = new AbortController();
    liveCapability.current = null; setSnapshot(null); setLoading(true); setErrorText(null); setStatusText(null); setConfirmed(false); setReviewNote("");
    const retained = pendingRef.current?.scopeKey === selected.scopeKey ? pendingRef.current : readPending(selected.scopeKey);
    setPending(retained);
    void load(selected.scope, { signal: controller.signal, allowDevelopmentFallback: false }).then(result => {
      if (controller.signal.aborted || current.current.loadKey !== selected.loadKey) return;
      if (!assetCapabilitiesMatchScope(result, selected.scope!)) throw new Error("Asset workflow scope, protocol or version is unsupported. Reload the retained journal.");
      let recovered = retained;
      if (result.pendingPostCommand) recovered = { scopeKey: selected.scopeKey, candidateFingerprint: result.candidateFingerprint, request: result.pendingPostCommand };
      else if (result.hasPendingPost || result.postedJournal || (result.approval && retained?.request.action === "Approve")) recovered = null;
      if (recovered && (!assetCommandMatchesScope(recovered.request, selected.scope!) || !Number.isSafeInteger(recovered.request.version) || recovered.request.version < 0)) {
        throw new Error("The retained retry does not match the selected journal scope. Inspect the retained workflow.");
      }
      if (recovered && !sameAssetPrincipal(recovered.request.actor, result.authenticatedActor)) {
        recovered = null; setStatusText("The authenticated operator changed. The prior operator's command was not replayed; review retained state before a new decision.");
      }
      if (recovered && !result.hasPendingPost && !result.postedJournal && recovered.request.version !== result.draftVersion) {
        recovered = null; setStatusText("The retained journal version changed without a pending command. Review the current version before a new decision.");
      }
      setPending(recovered); retain(selected.scopeKey, recovered);
      if (recovered && recovered.candidateFingerprint !== result.candidateFingerprint) throw new Error("The retained command belongs to a different candidate. Inspect the retained workflow before continuing.");
      liveCapability.current = result;
      setSnapshot({ key: selected.loadKey, value: result });
      if (result.postedJournal) setPosted({ key: selected.scopeKey, receipt: result.postedJournal });
      if (recovered) { setReviewNote(recovered.request.notes ?? ""); setStatusText("An unresolved command is retained. Review its identity and retry that same command."); }
    }).catch(error => {
      if (!controller.signal.aborted && current.current.loadKey === selected.loadKey) setErrorText(describeApiError(error, "Governed asset workflow is unavailable. This draft remains read-only.").summary);
    }).finally(() => { if (!controller.signal.aborted && current.current.loadKey === selected.loadKey) setLoading(false); });
    return () => controller.abort();
  }, [active, load, loadKey, reloadCount, sessionIdentity]);

  const blockedReason = (action: AssetAction): string | null => {
    if (!scope || !capability) return loading ? "Loading authenticated asset workflow capabilities." : "The governed asset workflow is unavailable; generic approval, posting and corrections are unavailable. Reload retained workflow.";
    if (!sessionIdentity) return "Authenticated session identity is unavailable. Refresh the session before a decision.";
    if (retainedPostPending && !capability.hasPendingPost) return "Retained posting intent requires a fresh matching recovery projection.";
    if (capability.hasPendingPost && action !== "Post") return "Resolve the retained command before another decision.";
    if (busyAction) return "A governed asset command is in progress.";
    if (currentPending && currentPending.request.action !== action) return "Resolve the retained command before another decision.";
    if (action === "Approve" ? capability.canApprove !== true : capability.canPost !== true) return (action === "Approve" ? capability.approveBlockedReason : capability.postBlockedReason) || "This action is unavailable for the current authenticated operator.";
    if (action === "Post" && !capability.approval) return "A verified retained independent approval receipt is required for posting.";
    if (!confirmed) return "Confirm review of the retained candidate and approval evidence first.";
    if (!currentPending && !reviewNote.trim()) return "Enter a review rationale before continuing.";
    return null;
  };
  const run = async (action: AssetAction) => {
    if (!current.current.active || current.current.loadKey !== loadKey || liveCapability.current !== capability || busy.current || blockedReason(action) || !scope || !capability) return;
    const selected = { scope, scopeKey, loadKey };
    const request = currentPending?.request ?? {
      journalEntryId: scope.journalEntryId, ledgerBookId: scope.ledgerBookId, fundProfileId: scope.fundProfileId,
      action, actor: capability.authenticatedActor!, version: capability.draftVersion, notes: reviewNote.trim(),
      correlationId: crypto.randomUUID(), evidenceLinks: [], actionOrigin: "HumanOperator" as const,
      periodIsLocked: false, rebookLines: []
    };
    const retained = currentPending ?? { scopeKey, candidateFingerprint: capability.candidateFingerprint, request };
    pendingRef.current = retained; setPending(retained); retain(scopeKey, retained);
    busy.current = true; setBusyAction(action); setErrorText(null); setStatusText(null);
    try {
      const result = await execute(request);
      if (!assetCommandMatchesScope(result.journalEntry, scope) || result.journalEntry.periodId?.toLowerCase() !== scope.periodId.toLowerCase()
        || result.journalEntry.version < capability.draftVersion || !result.journalEntry.assetAccountingDraft
        || result.transition.correlationId !== request.correlationId) throw new Error("The command response does not match the reviewed journal and request identity.");
      if (!sameAssetPrincipal(result.transition.actor, capability.authenticatedActor)) throw new Error("The command response belongs to a different authenticated operator. Reload retained state.");
      if (action === "Post" && !sameAssetPrincipal(result.transition.actor, capability.approval?.approver)) throw new Error("The posting response does not belong to the retained approver.");
      if (action === "Post" && (!result.postedJournal || !postedAssetJournalMatchesScope(result.postedJournal, scope))) throw new Error("Posting was not confirmed by a canonical journal receipt. Reload the retained workflow.");
      if (!current.current.active || current.current.loadKey !== selected.loadKey) return;
      retain(selected.scopeKey, null); pendingRef.current = null; setPending(null); liveCapability.current = null; setSnapshot(null);
      if (result.postedJournal) setPosted({ key: selected.scopeKey, receipt: result.postedJournal });
      setConfirmed(false); setStatusText(`${action} retained for command ${request.correlationId}.`);
      onResult(result);
    } catch (error) {
      if (current.current.loadKey === selected.loadKey) {
        liveCapability.current = null; setSnapshot(null);
        setErrorText(describeApiError(error, "The command outcome was not confirmed.").summary);
        setStatusText("Reload retained workflow before retrying. The exact command identity and payload are preserved.");
      }
    } finally { busy.current = false; setBusyAction(null); }
  };
  const reload = useCallback(async () => {
    if (busy.current) return;
    liveCapability.current = null; setSnapshot(null); await refreshWorkbench(); setReloadCount(value => value + 1);
  }, [refreshWorkbench]);
  return {
    capability, sessionReady: !!sessionIdentity, loading, errorText, statusText, busyAction: current.current.scopeKey === scopeKey ? busyAction : null,
    reviewNote, setReviewNote: (value: string) => { if (!currentPending) { setReviewNote(value); setConfirmed(false); } },
    confirmed, setConfirmed, pendingAction: currentPending?.request.action ?? null,
    pendingCommandId: currentPending?.request.correlationId ?? null,
    approval: capability?.approval ?? null,
    hasPendingPost: capability?.hasPendingPost === true || retainedPostPending,
    postedJournal: posted?.key === scopeKey ? posted.receipt : capability?.postedJournal ?? null,
    blockedReason, run, reload
  };
}
export type AssetAccountingWorkflowViewModel = ReturnType<typeof useAssetAccountingWorkflow>;
