import { useEffect, useId, useRef, useState } from "react";
import { addReconciliationBreakComment, assignReconciliationBreak, getReconciliationBreakDetail } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { TextArea } from "@/components/ui/text-area";
import { FormRow } from "@/components/ui/form";
import { StatusBanner } from "@/components/ui/status-banner";
import { isApiError } from "@/lib/api-errors";
import { formatDateTimeLabel } from "./accounting-screen.formatting";
import { requireSuccessfulReconciliationCasework } from "./reconciliation-casework-outcome";
import type { ReconciliationBreakQueueItem, ReconciliationCaseworkCommand, ReconciliationCaseworkOperationResult } from "@/types";

export interface CaseDiscussionServices {
  read: (id: string) => Promise<ReconciliationBreakQueueItem>;
  comment: (command: ReconciliationCaseworkCommand) => Promise<ReconciliationCaseworkOperationResult>;
  assign: (command: ReconciliationCaseworkCommand) => Promise<ReconciliationCaseworkOperationResult>;
}
const defaultServices: CaseDiscussionServices = {
  read: getReconciliationBreakDetail,
  comment: addReconciliationBreakComment,
  assign: assignReconciliationBreak
};

/** Mutations use retained versions and the server-authenticated actor, never queue display names. */
export function ReconciliationCaseDiscussion({ breakId, onUpdated, services = defaultServices }: {
  breakId: string;
  onUpdated?: (item: ReconciliationBreakQueueItem) => void;
  services?: CaseDiscussionServices;
}) {
  const id = useId();
  const [open, setOpen] = useState(false);
  const [item, setItem] = useState<ReconciliationBreakQueueItem | null>(null);
  const [note, setNote] = useState("");
  const [mentions, setMentions] = useState("");
  const [assignee, setAssignee] = useState("");
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [pending, setPending] = useState<ReconciliationCaseworkCommand | null>(null);
  const [requiresReload, setRequiresReload] = useState(false);
  const [reloadVersion, setReloadVersion] = useState(0);
  const revision = useRef(0);
  const loadedFor = useRef<string | null>(null);
  useEffect(() => {
    revision.current += 1;
    loadedFor.current = null;
    setItem(null); setPending(null); setError(null); setStatus(null); setBusy(false); setRequiresReload(false);
    setNote(""); setMentions(""); setAssignee(""); setReason("");
    return () => { revision.current += 1; };
  }, [breakId, services]);
  useEffect(() => {
    if (!open || loadedFor.current === breakId) return;
    const current = revision.current;
    let cancelled = false;
    setBusy(true);
    services.read(breakId).then((loaded) => {
      if (cancelled || revision.current !== current) return;
      if (loaded.breakId !== breakId) throw new Error("The loaded case does not match the selected case.");
      loadedFor.current = breakId;
      setItem(loaded);
      setAssignee(loaded.assigneeId ?? loaded.assignedTo ?? "");
      setRequiresReload(false);
      setError(null);
    }).catch((failure: unknown) => {
      if (!cancelled && revision.current === current) setError(failure instanceof Error ? failure.message : "Case discussion could not be loaded.");
    }).finally(() => { if (!cancelled && revision.current === current) setBusy(false); });
    return () => { cancelled = true; };
  }, [breakId, open, reloadVersion, services]);

  function reloadCase() {
    if (busy || pending) return;
    revision.current += 1;
    loadedFor.current = null;
    setItem(null); setError(null); setStatus(null);
    setReloadVersion((value) => value + 1);
  }

  async function submit(action: "AddComment" | "Assign") {
    if (busy || requiresReload || !item || item.breakId !== breakId || item.version == null || (!pending && (action === "AddComment" ? !note.trim() : !assignee.trim() || !reason.trim()))) return;
    const current = revision.current;
    const command: ReconciliationCaseworkCommand = pending ?? {
      // Compatibility field only: the endpoint replaces Actor with the authenticated principal.
      breakId, action, actor: "", commandId: crypto.randomUUID(), correlationId: crypto.randomUUID(),
      source: "browser-case-discussion", expectedVersion: item.version,
      ...(action === "AddComment"
        ? { note: note.trim(), mentions: [...new Set(mentions.split(",").map((value) => value.trim()).filter(Boolean))], visibility: "Internal" }
        : { assignee: assignee.trim(), reason: reason.trim() })
    };
    setPending(command); setBusy(true); setError(null); setStatus(null);
    let confirmedConflict = false;
    try {
      const result = await (command.action === "AddComment" ? services.comment(command) : services.assign(command));
      confirmedConflict = result.transitionStatus === "Conflict" || result.errorCode === "ConcurrencyConflict";
      if (confirmedConflict) throw new Error(result.error ?? "The case changed after it was loaded.");
      const updated = requireSuccessfulReconciliationCasework(result);
      if (revision.current !== current) return;
      if (updated.breakId !== breakId) throw new Error("The casework result belongs to another case. Refresh the case before continuing.");
      setItem(updated); setPending(null); onUpdated?.(updated);
      if (command.action === "AddComment") { setNote(""); setMentions(""); }
      else setReason("");
      setStatus(command.action === "AddComment" ? "Comment retained in case history." : "Assignment retained in case history.");
    } catch (failure) {
      if (revision.current === current) {
        if (confirmedConflict || (isApiError(failure) && failure.status === 409)) {
          setPending(null);
          setRequiresReload(true);
          setError("This case changed after it was loaded. Reload retained case, inspect the current version, then submit again. Your draft text is retained.");
        } else {
          setError(`${failure instanceof Error ? failure.message : "Casework request failed."} Retry uses the same command identity; no case resolution is implied.`);
        }
      }
    } finally { if (revision.current === current) setBusy(false); }
  }

  const editable = !busy && !pending && !requiresReload && item?.breakId === breakId && item.version != null;
  const comments = item?.comments ?? [];
  return (
    <details className="border-t border-border pt-3" open={open} onToggle={(event) => setOpen(event.currentTarget.open)}>
      <summary className="cursor-pointer text-sm font-semibold">Discussion and assignment</summary>
      {open ? <div className="mt-3 grid gap-3" aria-busy={busy}>
        {busy && !item ? <p role="status">Loading retained case discussion…</p> : null}
        {error ? <StatusBanner role="alert" tone="warning" title="Casework needs review" detail={error} /> : null}
        {status ? <p role="status" className="text-sm">{status}</p> : null}
        {item ? <>
          <p className="text-sm text-muted-foreground">Owner: {item.assigneeDisplayName ?? item.assignedTo ?? "Unassigned"}. Discussion and acknowledgement do not resolve this case.</p>
          <ol aria-label="Retained case discussion" className="grid gap-3">
            {comments.map((comment) => <li key={comment.commentId} className="border-b border-border/60 pb-2 text-sm">
              <p className="font-medium">{comment.authorDisplayName || comment.authorId} · <time dateTime={comment.createdAt} className="whitespace-nowrap">{formatDateTimeLabel(comment.createdAt)}</time></p>
              <p className="whitespace-pre-wrap">{comment.deletedAt ? "Comment removed; audit history retained." : comment.body}</p>
              {comment.mentions?.length ? <p className="text-xs text-muted-foreground">Mentions: {comment.mentions.join(", ")}</p> : null}
              {comment.editedAt ? <p className="text-xs text-muted-foreground">Edited {formatDateTimeLabel(comment.editedAt)}</p> : null}
            </li>)}
          </ol>
          {!comments.length ? <p className="text-sm text-muted-foreground">No retained comments.</p> : null}
          <FormRow label="Comment" labelFor={`${id}-note`}><TextArea id={`${id}-note`} rows={2} className="min-h-20" value={note} disabled={!editable} onChange={(event) => setNote(event.target.value)} /></FormRow>
          <FormRow label="Mention operator IDs (comma separated)" labelFor={`${id}-mentions`}><Input id={`${id}-mentions`} value={mentions} disabled={!editable} onChange={(event) => setMentions(event.target.value)} /></FormRow>
          <Button size="sm" disabled={!editable || !note.trim()} disabledReason="Load a versioned case and enter a comment before submitting." onClick={() => void submit("AddComment")}>Add comment</Button>
          <FormRow label="Assign to operator ID" labelFor={`${id}-assignee`}><Input id={`${id}-assignee`} value={assignee} disabled={!editable} onChange={(event) => setAssignee(event.target.value)} /></FormRow>
          <FormRow label="Assignment reason" labelFor={`${id}-reason`}><Input id={`${id}-reason`} value={reason} disabled={!editable} onChange={(event) => setReason(event.target.value)} /></FormRow>
          <Button size="sm" variant="outline" disabled={!editable || !assignee.trim() || !reason.trim()} disabledReason="Load a versioned case, name an operator and give an assignment reason." onClick={() => void submit("Assign")}>Assign case</Button>
          {item.version == null ? <p role="status" className="text-sm text-muted-foreground">Case version unavailable; changes are disabled.</p> : null}
        </> : null}
        {pending && !busy ? <Button size="sm" variant="outline" onClick={() => void submit(pending.action === "AddComment" ? "AddComment" : "Assign")}>Retry same casework command</Button> : null}
        {requiresReload && !busy ? <Button size="sm" variant="outline" onClick={reloadCase}>Reload retained case</Button> : null}
        <p className="text-xs text-muted-foreground">Mentions are retained references; this action sends no notification.</p>
      </div> : null}
    </details>
  );
}
