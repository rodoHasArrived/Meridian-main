import { useCallback, useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { DenseDataTable } from "@/components/meridian/ui-kit-primitives";
import { IntercompanyAgreementEditor, agreementTypes, newAgreementDraft, typeLabel } from "@/components/accounting/intercompany-agreement-editor";
import { IntercompanyAgreementJournals } from "@/components/accounting/intercompany-agreement-journals";
import * as api from "@/lib/api/intercompany-agreements.api";
import type { AgreementMutation } from "@/lib/api/intercompany-agreements.api";
import { ApiError } from "@/lib/api-errors";
import type { AgreementCommand, AgreementDetail, AgreementDraft, AgreementList, AgreementQuery, AgreementRevision } from "@/types/intercompany-agreements";

const today = () => new Date().toISOString().slice(0, 10);
const errorText = (reason: unknown) => reason instanceof Error ? reason.message : "The agreement request failed.";
const text = (value?: string | number | null) => value == null || value === "" ? "Not provided" : String(value);
const span = (draft: AgreementDraft) => draft.action === "Terminate" ? `Terminated from ${draft.terminationEffectiveDate}`
  : `${draft.effectiveFrom} – ${draft.effectiveTo ?? "open ended"} (end exclusive)`;
type Services = typeof api;
type Editor = { agreementId: string; proposalId: string; initial: AgreementDraft; mutation: "create" | "propose" | "save"; fixedIdentity: boolean };

export function IntercompanyAgreementsPanel({ initialEntityId = "", initialAgreementId, initialLedgerBookId, services = api }: { initialEntityId?: string; initialAgreementId?: string | null; initialLedgerBookId?: string; services?: Services }) {
  const id = useId();
  const [entity, setEntity] = useState(initialEntityId);
  const [asOf, setAsOf] = useState(today);
  const [type, setType] = useState("");
  const [status, setStatus] = useState("");
  const [query, setQuery] = useState<AgreementQuery>({ entityId: initialEntityId || undefined, asOf: today() });
  const [list, setList] = useState<AgreementList | null>(null);
  const [detail, setDetail] = useState<AgreementDetail | null>(null);
  const [editor, setEditor] = useState<Editor | null>(null);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [rationale, setRationale] = useState("");
  const [reload, setReload] = useState(0);
  const lifecycle = useRef<AbortController | null>(null);
  const detailRequest = useRef<AbortController | null>(null);
  const lastCommand = useRef<{ key: string; command: AgreementCommand } | null>(null);

  useEffect(() => {
    const controller = new AbortController(); lifecycle.current = controller;
    return () => { controller.abort(); detailRequest.current?.abort(); };
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setError(null); setList(null);
    services.listIntercompanyAgreements(query, { signal: controller.signal }).then(result => {
      if (!controller.signal.aborted) setList(result);
    }).catch(reason => { if (!controller.signal.aborted) setError(errorText(reason)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [query, reload, services]);

  const open = useCallback(async (agreementId: string) => {
    detailRequest.current?.abort();
    const controller = new AbortController(); detailRequest.current = controller;
    setBusy(true); setError(null); setConflict(false); setMessage(null);
    try {
      const next = await services.getIntercompanyAgreement(agreementId, query.asOf, { signal: controller.signal });
      if (!controller.signal.aborted) { setDetail(next); setRationale(""); }
    } catch (reason) { if (!controller.signal.aborted) setError(errorText(reason)); }
    finally { if (!controller.signal.aborted) setBusy(false); }
  }, [query.asOf, services]);
  useEffect(() => { if (initialAgreementId) void open(initialAgreementId); }, [initialAgreementId, open]);

  async function mutate(action: AgreementMutation, draft?: AgreementDraft, note = rationale) {
    const signal = lifecycle.current!.signal;
    const proposal = detail?.proposals.find(item => item.state === "Draft" || item.state === "Submitted");
    const command = {
      agreementId: editor?.agreementId ?? detail!.agreementId, expectedAggregateVersion: editor?.mutation === "create" ? 0 : detail!.aggregateVersion,
      proposalId: editor?.proposalId ?? proposal!.proposalId, draft, rationale: note, submittedHash: proposal?.submittedHash
    };
    const key = JSON.stringify([action, command]);
    if (lastCommand.current?.key !== key) lastCommand.current = { key, command: { ...command, commandId: crypto.randomUUID() } };
    setBusy(true); setError(null); setConflict(false); setMessage(null);
    try {
      const result = await services.mutateIntercompanyAgreement(action, lastCommand.current.command, { signal });
      if (signal.aborted) return;
      setEditor(null); setRationale(""); lastCommand.current = null;
      let currentDetail = result.detail;
      if (result.replayed || (query.asOf && currentDetail.asOf !== query.asOf)) {
        try { currentDetail = await services.getIntercompanyAgreement(result.detail.agreementId, query.asOf, { signal }); }
        catch {
          if (!signal.aborted) { setDetail(null); setList(null); setMessage(`${typeLabel(result.receipt.action)} was retained. Current agreement status is unavailable; refresh before another action.`); }
          return;
        }
      }
      if (signal.aborted) return;
      setDetail(currentDetail);
      setList(current => current ? { ...current, items: current.items.some(item => item.agreementId === currentDetail.agreementId)
        ? current.items.map(item => item.agreementId === currentDetail.agreementId ? currentDetail : item)
        : [currentDetail, ...current.items] } : current);
      setMessage(`${typeLabel(result.receipt.action)} retained at version ${result.detail.aggregateVersion}${result.replayed ? ` (original receipt recovered; current version ${currentDetail.aggregateVersion})` : ""}.`);
    } catch (reason) {
      if (!signal.aborted) { setError(errorText(reason)); setConflict(reason instanceof ApiError && reason.status === 409); }
    } finally { if (!signal.aborted) setBusy(false); }
  }

  const proposal = detail?.proposals.find(item => item.state === "Draft" || item.state === "Submitted");
  function propose(action: "Amend" | "Terminate") {
    const source = detail!.applicableRevision ?? [...detail!.revisions].filter(item => item.action !== "Terminate" && item.terms.effectiveFrom <= (query.asOf ?? asOf))
      .sort((a, b) => b.terms.effectiveFrom.localeCompare(a.terms.effectiveFrom) || b.agreementVersion - a.agreementVersion)[0];
    if (!source) { setError("No approved terms start on or before this as-of date. Choose the intended agreement effective date before creating a proposal."); return; }
    setError(null); setConflict(false); setMessage(null);
    setEditor({ agreementId: detail!.agreementId, proposalId: crypto.randomUUID(), mutation: "propose", fixedIdentity: true,
      initial: { ...source.terms, action, effectiveFrom: query.asOf ?? asOf, terminationEffectiveDate: action === "Terminate" ? query.asOf ?? asOf : null } });
  }
  return <Card id="configure-section-agreements" className="panel-surface configure-anchor scroll-mt-20" role="region" aria-label="Intercompany agreements">
    <CardHeader><CardTitle>Intercompany agreements</CardTitle><CardDescription>Review the terms and evidence behind activity between two legal entities. Approved revisions remain attached to historical journals.</CardDescription></CardHeader>
    <CardContent className="space-y-4">
      <form aria-label="Filter intercompany agreements" onSubmit={event => {
        event.preventDefault(); setDetail(null); setMessage(null);
        setQuery({ entityId: entity.trim() || undefined, asOf, agreementType: type as AgreementQuery["agreementType"], status: status as AgreementQuery["status"] });
      }} className="space-y-3">
        <FormGrid columns={3}>
          <FormRow label="Entity filter" labelFor={`${id}-entity`} hint="Existing legal entity identifier; leave blank for all authorized agreements."><Input id={`${id}-entity`} value={entity} onChange={event => setEntity(event.target.value)} disabled={Boolean(editor) || busy} /></FormRow>
          <FormRow label="Agreement as-of date" labelFor={`${id}-date`}><Input id={`${id}-date`} type="date" required value={asOf} onChange={event => setAsOf(event.target.value)} disabled={Boolean(editor) || busy} /></FormRow>
          <FormRow label="Type filter" labelFor={`${id}-type`}><Select id={`${id}-type`} value={type} onChange={event => setType(event.target.value)} disabled={Boolean(editor) || busy}><option value="">All types</option>{agreementTypes.map(item => <option key={item} value={item}>{typeLabel(item)}</option>)}</Select></FormRow>
          <FormRow label="Status filter" labelFor={`${id}-status`}><Select id={`${id}-status`} value={status} onChange={event => setStatus(event.target.value)} disabled={Boolean(editor) || busy}><option value="">All statuses</option>{["Unapproved", "Scheduled", "Active", "Expired", "Terminated"].map(item => <option key={item}>{item}</option>)}</Select></FormRow>
        </FormGrid>
        <div className="flex flex-wrap gap-2"><Button variant="outline" type="submit" disabled={loading || busy || Boolean(editor)}>Apply agreement filters</Button>
          <Button variant="outline" type="button" disabled={loading || busy || Boolean(editor)} onClick={() => { setDetail(null); setReload(value => value + 1); }}>Refresh agreements</Button>
          <Button type="button" disabled={busy || loading || Boolean(editor) || list?.canCreate !== true} onClick={() => {
            setDetail(null); setEditor({ agreementId: crypto.randomUUID(), proposalId: crypto.randomUUID(), mutation: "create", fixedIdentity: false, initial: newAgreementDraft(query.entityId ?? "", query.asOf ?? asOf) });
          }}>New agreement draft</Button></div>
      </form>
      {loading ? <p role="status" className="text-sm">Loading the authoritative agreement registry…</p> : null}
      {error ? <StatusBanner tone={conflict ? "warning" : "danger"} role="alert" title={conflict ? "Agreement changed — local input retained" : "Agreement request unavailable"} detail={error} /> : null}
      {conflict && detail ? <Button variant="outline" disabled={busy} onClick={() => void open(detail.agreementId)}>Compare with latest retained agreement</Button> : null}
      {message ? <p role="status" className="text-sm">{message}</p> : null}
      {!loading && list?.items.length === 0 ? <p role="status" className="text-sm">No authorized agreements match these filters.</p> : null}
      {list?.items.length ? <DenseDataTable<AgreementDetail> rows={list.items} getRowId={item => item.agreementId}
        ariaLabel="Intercompany agreement registry" emptyText="No authorized agreements match these filters."
        caption={`Agreements for ${query.entityId ?? "authorized entities"} as of ${query.asOf}`} columns={[
          { id: "agreement", label: "Agreement", rowHeader: true, sortValue: item => item.code, render: item => <Button variant="ghost" disabled={busy || Boolean(editor)} onClick={() => void open(item.agreementId)}>{item.code} · {item.name}</Button> },
          { id: "parties", label: "Parties", render: item => <div className="max-w-[20rem] break-words">{item.partyOneRole}: {item.partyOneId}<br />{item.partyTwoRole}: {item.partyTwoId}</div> },
          { id: "type", label: "Type / currency", render: item => `${typeLabel(item.agreementType)} · ${item.currency}` },
          { id: "status", label: "Effective status", sortValue: item => item.effectiveStatus, render: item => item.effectiveStatus },
          { id: "version", label: "Version / review", render: item => <>Applicable: {item.applicableRevision?.agreementVersion ?? "None"}<br />Latest approved: {item.latestApprovedVersion ?? "None"}<br />{item.proposals.find(candidate => candidate.state === "Draft" || candidate.state === "Submitted")?.state ?? "No pending proposal"}</> }
        ]} /> : null}
      {list?.nextCursor ? <Button variant="outline" disabled={busy || loading || Boolean(editor)} onClick={() => { setDetail(null); setQuery(current => ({ ...current, cursor: list.nextCursor! })); }}>Next agreement page</Button> : null}
      {editor ? <IntercompanyAgreementEditor key={`${editor.proposalId}:${editor.mutation}`} initial={editor.initial} fixedIdentity={editor.fixedIdentity} busy={busy}
        onSave={(draft, note) => void mutate(editor.mutation, draft, note)} onCancel={() => { setEditor(null); lastCommand.current = null; setConflict(false); }} /> : null}
      {detail ? <section className="space-y-4 border-t border-border pt-4" aria-label={`Agreement ${detail.code}`}>
        <h3 className="font-semibold">{detail.code} · {detail.name}</h3>
        <p className="text-sm">{detail.effectiveStatus} as of {detail.asOf} · {detail.currency} · Applicable version {detail.applicableRevision?.agreementVersion ?? "none"}; latest approved version {detail.latestApprovedVersion ?? "none"}</p>
        {detail.applicableRevision ? <RevisionSummary revision={detail.applicableRevision} /> : <p className="text-sm text-muted-foreground">There is no approved revision effective on this date.</p>}
        {detail.capabilities.blockingReason ? <p className="text-sm text-muted-foreground">{detail.capabilities.blockingReason}</p> : null}
        {!editor ? <div className="flex flex-wrap gap-2"><Button variant="outline" disabled={busy || !detail.capabilities.canAmend} onClick={() => propose("Amend")}>Draft amendment</Button>
          <Button variant="outline" disabled={busy || !detail.capabilities.canTerminate} onClick={() => propose("Terminate")}>Draft termination</Button>
          {!detail.latestApprovedVersion && !proposal ? <Button variant="outline" disabled={busy || !detail.capabilities.canCreateProposal} onClick={() => {
            const previous = detail.proposals.at(-1)?.draft;
            setEditor({ agreementId: detail.agreementId, proposalId: crypto.randomUUID(), mutation: "propose", fixedIdentity: false,
              initial: previous ? { ...previous, action: "Activate" } : newAgreementDraft(detail.partyOneId, query.asOf ?? asOf) });
          }}>Replace activation draft</Button> : null}</div> : null}
        {proposal ? <div className="space-y-3 rounded-md border border-border p-3" aria-label="Pending agreement proposal">
          <h4 className="font-semibold">{typeLabel(proposal.action)} proposal · {proposal.state}</h4><p className="text-sm">{span(proposal.draft)} · Base version {text(proposal.baseAgreementVersion)}</p>
          <TermsSummary draft={proposal.draft} />
          <p className="text-sm">Authors: {proposal.authors.join(", ")} · Submitted by {text(proposal.submitter)} at {text(proposal.submittedAtUtc)}</p>
          <TechnicalDetails label="Compare proposed and applicable terms"><FormGrid columns={2}><div><h5 className="font-semibold">Applicable version {detail.applicableRevision?.agreementVersion ?? "none"}</h5>{detail.applicableRevision ? <TermsSummary draft={detail.applicableRevision.terms} /> : null}</div><div><h5 className="font-semibold">Proposed revision</h5><TermsSummary draft={proposal.draft} /></div></FormGrid></TechnicalDetails>
          {!editor ? <>
            <FormRow label="Review decision rationale" labelFor={`${id}-rationale`}><Input id={`${id}-rationale`} value={rationale} disabled={busy} onChange={event => setRationale(event.target.value)} /></FormRow>
            <div className="flex flex-wrap gap-2">
              <Button variant="outline" disabled={busy || !detail.capabilities.canEdit} onClick={() => setEditor({ agreementId: detail.agreementId, proposalId: proposal.proposalId, mutation: "save", fixedIdentity: detail.latestApprovedVersion != null, initial: proposal.draft })}>Edit agreement draft</Button>
              {([['submit', 'Submit for independent review', detail.capabilities.canSubmit], ['approve', 'Approve submitted terms', detail.capabilities.canApprove], ['reject', 'Reject proposal', detail.capabilities.canReject], ['withdraw', 'Withdraw proposal', detail.capabilities.canWithdraw]] as const).map(([action, label, allowed]) => <Button key={action} variant={action === "approve" ? "default" : "outline"} disabled={busy || !allowed || !rationale.trim()} onClick={() => void mutate(action)}>{label}</Button>)}
            </div>
          </> : null}
        </div> : null}
        <TechnicalDetails label="Approved revision timeline and evidence"><div className="space-y-4">{detail.revisions.length === 0 ? <p className="text-sm">No approved revisions.</p> : detail.revisions.map(revision => <RevisionSummary key={revision.agreementVersion} revision={revision} />)}</div></TechnicalDetails>
        <TechnicalDetails label="Agreement audit history"><ol className="space-y-2 text-sm">{detail.history.map(event => <li key={event.eventId}>{event.recordedAtUtc} · {typeLabel(event.action)} · {event.actor} · version {event.aggregateVersion}{event.rationale ? ` · ${event.rationale}` : ""}{event.snapshot ? <TechnicalDetails label={`Inspect ${typeLabel(event.action)} snapshot`}><TermsSummary draft={event.snapshot} /></TechnicalDetails> : null}</li>)}</ol></TechnicalDetails>
        <IntercompanyAgreementJournals key={detail.agreementId} agreementId={detail.agreementId} asOf={detail.asOf} initialLedgerBookId={initialLedgerBookId} load={services.getIntercompanyAgreementJournalLines} />
        <p className="text-sm text-muted-foreground">Agreement coverage is advisory. This registry does not post journals, settle cash or change consolidation matching.</p>
      </section> : null}
    </CardContent>
  </Card>;
}

function TermsSummary({ draft }: { draft: AgreementDraft }) {
  return <div className="space-y-2 text-sm"><p>{span(draft)}</p><dl className="grid gap-x-4 gap-y-1 md:grid-cols-2">
    {[["Purpose", draft.purpose], ["Annual rate (fraction)", draft.terms?.fixedAnnualRate], ["Rate terms", draft.terms?.rateDescription], ["Schedule", draft.terms?.scheduleDescription], ["Allocation", draft.terms?.allocationDescription], ["Settlement calendar days", draft.terms?.settlementDays], ["Notes", draft.terms?.notes]].map(([label, value]) => <div key={String(label)}><dt className="text-muted-foreground">{label}</dt><dd className="break-words">{text(value)}</dd></div>)}
    </dl><ul className="space-y-1 break-all" aria-label="Retained agreement evidence">{draft.evidence?.map((item, index) => {
      const vaultId = /^evidence:\/\/evidence-vault\/([A-Za-z0-9_-]+)$/.exec(item.reference)?.[1];
      return <li key={`${item.reference}:${index}`}>{vaultId ? <a className="underline underline-offset-4" href={`/workstation/evidence/vault/${encodeURIComponent(vaultId)}`} target="_blank" rel="noreferrer">{item.reference}</a> : item.reference}{item.artifactId ? ` · artifact ${item.artifactId}` : ""}{item.contentHash ? ` · SHA-256 ${item.contentHash}` : ""}</li>;
    })}</ul></div>;
}
function RevisionSummary({ revision }: { revision: AgreementRevision }) {
  return <div className="space-y-2 border-l-2 border-border pl-3"><h4 className="font-semibold">Approved version {revision.agreementVersion} · {typeLabel(revision.action)}</h4>
    <TermsSummary draft={revision.terms} /><p className="text-sm">Approved by {revision.approver} at {revision.approvedAtUtc} · {revision.rationale}</p>
    {(revision.terms.terminationEffectiveDate ?? revision.terms.effectiveFrom) < revision.approvedAtUtc.slice(0, 10) ? <p className="text-sm text-warning">Retrospective change: effective before approval. Posted journal references remain unchanged.</p> : null}
    <TechnicalDetails label={`Version ${revision.agreementVersion} provenance`}><p className="break-all text-sm">Terms hash: {revision.payloadHash} · Base {text(revision.baseAgreementVersion)} · Approval predecessor {text(revision.predecessorAgreementVersion)}</p></TechnicalDetails></div>;
}
