import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { DesignSystemStatus } from "@/design-system/status";
import { applyApprovalCollaboration, getApprovalCollaboration, type ApprovalCollaboration, type ApprovalCollaborationCommand } from "@/lib/approval-collaboration";

export function ApprovalCollaborationPanel({ workflowId, onChanged }: { workflowId: string; onChanged?: () => void }) {
  const [data, setData] = useState<ApprovalCollaboration | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [commandError, setCommandError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [body, setBody] = useState("");
  const [mentions, setMentions] = useState("");
  const [reviewer, setReviewer] = useState("");
  const [reason, setReason] = useState("");
  const [retry, setRetry] = useState<{ action: "comment" | "reassign"; command: ApprovalCollaborationCommand } | null>(null);
  const [revision, setRevision] = useState(0);
  const session = useRef(0);
  const lifetime = useRef<AbortController | null>(null);
  const current = data?.workflowId.toLowerCase() === workflowId.toLowerCase() ? data : null;

  useEffect(() => {
    setData(null); setBody(""); setMentions(""); setReviewer(""); setReason(""); setRetry(null); setNotice(null); setCommandError(null);
  }, [workflowId]);
  useEffect(() => {
    const controller = new AbortController(); lifetime.current = controller;
    const invocation = ++session.current;
    setLoading(true); setError(null);
    void getApprovalCollaboration(workflowId, { signal: controller.signal }).then((result) => {
      if (!controller.signal.aborted && session.current === invocation) setData(result);
    }).catch((failure) => {
      if (!controller.signal.aborted && session.current === invocation) { setData(null); setError(failure instanceof Error ? failure.message : "Approval discussion is unavailable."); }
    }).finally(() => { if (!controller.signal.aborted && session.current === invocation) setLoading(false); });
    return () => { controller.abort(); session.current += 1; };
  }, [workflowId, revision]);

  async function send(action: "comment" | "reassign", command: ApprovalCollaborationCommand) {
    const invocation = session.current;
    const signal = lifetime.current?.signal;
    setBusy(true); setCommandError(null); setNotice(null);
    try {
      const result = await applyApprovalCollaboration(workflowId, action, command, { signal });
      if (signal?.aborted || invocation !== session.current) return;
      if (!result || typeof result.success !== "boolean") throw new Error("Approval command response could not be verified.");
      setRetry(null);
      if (result.success) {
        setNotice(action === "comment" ? "Comment retained. Approval state is unchanged." : "Reviewer reassignment retained. No approval decision was made.");
        setBody(""); setMentions(""); setReviewer(""); setReason("");
        onChanged?.();
      } else setCommandError(result.errorMessage ?? result.errorCode ?? "The approval change was refused. Refresh and review its current state.");
      setRevision((value) => value + 1);
    } catch (failure) {
      if (signal?.aborted || invocation !== session.current) return;
      setRetry({ action, command });
      setCommandError(`Outcome unconfirmed. Retry the same retained command before making another change. ${failure instanceof Error ? failure.message : "Request failed."}`);
    } finally {
      if (!signal?.aborted && invocation === session.current) setBusy(false);
    }
  }

  const canWrite = Boolean(current && !loading && !busy && !retry);
  const baseCommand = () => ({ expectedVersion: current!.version, approvalId: current!.currentApprovalId!, commandId: crypto.randomUUID(), actor: "" });
  return <section aria-label="Approval discussion and assignment" className="space-y-3 border-t border-border pt-3" aria-busy={loading || busy}>
    <h4 className="text-sm font-semibold">Discussion and reviewer</h4>
    <p className="text-xs text-muted-foreground">Comments retain context and mentions. They never approve, post or release work; mention notifications are not sent.</p>
    {loading ? <p role="status" className="text-xs">Loading retained approval discussion…</p> : null}
    {error ? <p role="alert" className="text-xs">{error}</p> : null}
    {commandError ? <p role="alert" className="text-xs">{commandError}</p> : null}
    {notice ? <p role="status" className="text-xs">{notice}</p> : null}
    {retry ? <Button size="sm" disabled={busy || loading} onClick={() => void send(retry.action, retry.command)}>Retry same approval command</Button> : null}
    <Button size="sm" variant="outline" disabled={loading || busy} onClick={() => setRevision((value) => value + 1)}>Refresh discussion</Button>
    {current ? <>
      <div className="flex flex-wrap items-center gap-2 text-xs"><DesignSystemStatus status={current.approvalState}>{current.approvalState}</DesignSystemStatus><span>Reviewer: {current.reviewer ?? "Unassigned"}</span><span>Submitted by: {current.submitter ?? "Unavailable"}</span></div>
      {current.disabledReason ? <p className="text-xs text-muted-foreground">{current.disabledReason}</p> : null}
      <ol aria-label="Retained approval comments" className="space-y-3">{current.comments.map((comment) => <li key={comment.commentId} className="border-b border-border pb-2 text-xs">
        <p className="font-medium">{comment.author} · {approvalDiscussionDate(comment.createdAtUtc)}</p><p className="mt-1 whitespace-pre-wrap">{comment.body}</p>
        {comment.mentions.length ? <p className="mt-1 text-muted-foreground">Mentioned: {comment.mentions.join(", ")}</p> : null}
      </li>)}</ol>
      {current.comments.length === 0 ? <p className="text-xs text-muted-foreground">No retained comments.</p> : null}
      <form className="space-y-2" onSubmit={(event) => { event.preventDefault(); if (canWrite && current.canComment && body.trim()) void send("comment", { ...baseCommand(), body: body.trim(), mentions: mentions.split(",").map((value) => value.trim()).filter(Boolean) }); }}>
        <label className="block text-xs">Comment<Input value={body} onChange={(event) => setBody(event.target.value)} maxLength={4000} disabled={!canWrite || !current.canComment} /></label>
        <label className="block text-xs">Mention operator IDs · comma separated<Input value={mentions} onChange={(event) => setMentions(event.target.value)} disabled={!canWrite || !current.canComment} /></label>
        <Button type="submit" size="sm" disabled={!canWrite || !current.canComment || !body.trim()}>Retain comment</Button>
      </form>
      <details><summary className="cursor-pointer text-xs font-medium">Reassign reviewer</summary><form className="mt-2 space-y-2" onSubmit={(event) => { event.preventDefault(); if (canWrite && current.canReassign && reviewer.trim() && reason.trim()) void send("reassign", { ...baseCommand(), reviewer: reviewer.trim(), rationale: reason.trim() }); }}>
        <label className="block text-xs">Independent reviewer operator ID<Input data-approval-reviewer-input value={reviewer} onChange={(event) => setReviewer(event.target.value)} maxLength={128} disabled={!canWrite || !current.canReassign} /></label>
        <label className="block text-xs">Reassignment reason<Input value={reason} onChange={(event) => setReason(event.target.value)} maxLength={4000} disabled={!canWrite || !current.canReassign} /></label>
        <Button type="submit" size="sm" disabled={!canWrite || !current.canReassign || !reviewer.trim() || !reason.trim()}>Retain reassignment</Button>
      </form></details>
      <details><summary className="cursor-pointer text-xs font-medium">Decision and reviewer history · {current.approvalHistory.length}</summary><ol className="mt-2 space-y-2">{current.approvalHistory.map((entry) => <li key={entry.approvalId} className="text-xs"><p>{entry.status} · {entry.reviewer ?? "Unassigned"}</p><p>{entry.rationale}</p><p className="text-muted-foreground">Submitted {approvalDiscussionDate(entry.submittedAtUtc)}{entry.decidedAtUtc ? ` · Decided ${approvalDiscussionDate(entry.decidedAtUtc)}` : ""}</p></li>)}</ol></details>
    </> : null}
  </section>;
}

function approvalDiscussionDate(value: string | null) {
  const timestamp = Date.parse(value ?? "");
  return Number.isFinite(timestamp) ? new Intl.DateTimeFormat("en-US", { dateStyle: "medium", timeStyle: "short", timeZone: "UTC" }).format(timestamp) + " UTC" : "Unavailable";
}
