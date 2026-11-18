import { useState, type FormEvent } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { FormGrid } from "@/components/ui/form";
import { PanelReload } from "@/components/ui/panel-reload";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { entityOperationEndpoints as endpoints } from "@/lib/api/entity-operations.api";
import { formatTimestamp } from "@/lib/format";
import type { EntityOperationScope } from "@/types/entity-operations";
import type { IntercompanyRateFixing } from "@/types/intercompany-rate-fixings";
import { EntityOperationTable, MutationFeedback, OperationError, OperationField, useEntityData, useEntityMutation } from "./accounting-screen.entity-operations.shared";

const exactFraction = "-?[0-9]+(\\.[0-9]+)?";
const normalizedIndex = (value: string) => value.trim().toUpperCase();

function fixingStatus(fixing: IntercompanyRateFixing) {
  if (fixing.approved && fixing.pending) return "Approved · correction pending review";
  if (fixing.approved) return "Approved";
  if (fixing.pending) return "Pending review · not used for accrual";
  return "No approved value";
}

/**
 * Reviewed index fixings for floating-rate intercompany accrual. An operator records what a publisher showed;
 * a different person approves it before any accrual uses it. Rates stay exact decimal text throughout.
 */
export function EntityRateFixings({ scope }: { scope: EntityOperationScope }) {
  const canManage = scope.capabilities.canManageLedger;
  const [indexReference, setIndexReference] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [query, setQuery] = useState<string | null>(null);
  const fixings = useEntityData<IntercompanyRateFixing[]>(query);
  const rangeEntered = Boolean(indexReference.trim() && from && to);
  const rangeOrdered = !rangeEntered || from <= to;

  // Any change to the requested range withdraws the listed records rather than showing a stale range.
  function changeRange(update: () => void) { update(); setQuery(null); }
  function load(event: FormEvent) {
    event.preventDefault();
    if (!rangeEntered || !rangeOrdered) return;
    const path = endpoints.rateFixingRange(indexReference.trim(), from, to);
    if (path === query) fixings.refresh(); else setQuery(path);
  }

  const [proposedIndex, setProposedIndex] = useState("");
  const [fixingDate, setFixingDate] = useState("");
  const [rateFraction, setRateFraction] = useState("");
  const [sourceReference, setSourceReference] = useState("");
  const [justification, setJustification] = useState("");
  const proposal = useEntityMutation();
  const loadedForDate = fixings.data?.find((item) => item.indexReference === normalizedIndex(proposedIndex) && item.fixingDate === fixingDate);
  const expectedVersion = loadedForDate?.version ?? 0;
  async function propose(event: FormEvent) {
    event.preventDefault();
    if (!canManage) return;
    const command = { indexReference: proposedIndex.trim(), fixingDate, rateFraction: rateFraction.trim(),
      sourceReference: sourceReference.trim(), justification: justification.trim(), expectedVersion };
    const result = await proposal.run<IntercompanyRateFixing>(endpoints.rateFixings, { requestId: proposal.identity(command), ...command },
      "Fixing value retained as pending. It sets no accrual rate until a different person approves it.");
    if (result && query) fixings.refresh();
  }

  const [selectedId, setSelectedId] = useState("");
  const [rejectReason, setRejectReason] = useState("");
  const decision = useEntityMutation();
  const selected = fixings.data?.find((item) => item.fixingId === selectedId && item.pending);
  async function decide(action: "Approve" | "Reject") {
    if (!canManage || !selected?.pending) return;
    const reason = action === "Reject" ? rejectReason.trim() : null;
    if (action === "Reject" && !reason) return;
    const command = { fixingId: selected.fixingId, action, expectedVersion: selected.version, expectedValueDigest: selected.pending.valueDigest, reason };
    const { fixingId, ...body } = command;
    const result = await decision.run<IntercompanyRateFixing>(endpoints.rateFixingDecisions(fixingId), { requestId: decision.identity(command), ...body },
      action === "Approve" ? "Fixing value approved. Floating accruals whose fixing date it covers now use it; existing drafts must be re-previewed."
        : "Pending fixing value rejected. Any previously approved value is unchanged.");
    if (result) { setRejectReason(""); setSelectedId(""); fixings.refresh(); }
  }

  return <section className="space-y-4" aria-labelledby="entity-rate-fixings-title">
    <div className="flex flex-wrap items-center gap-2"><h3 id="entity-rate-fixings-title" className="font-semibold">Reviewed index rate fixings</h3>
      {!canManage ? <Badge variant="outline">Read only</Badge> : null}</div>
    <p className="text-sm text-muted-foreground">Only approved fixings set floating intercompany accrual rates. A missing fixing is never inferred, carried forward or interpolated, and nothing is fetched from a market feed: an operator records the value a publisher showed and a different person approves it. A correction stays pending and the approved value keeps applying until the correction is approved.</p>
    {!canManage ? <p className="text-sm text-muted-foreground">You can inspect retained fixings. Ledger management permission is required to propose, approve or reject a fixing value.</p> : null}

    <form className="space-y-3" aria-label="Load rate fixings" onSubmit={load}><FormGrid columns={3}>
      <OperationField label="Fixing index reference" value={indexReference} onChange={(value) => changeRange(() => setIndexReference(value))} required hint="For example SOFR. Matched case-insensitively." />
      <OperationField label="Fixing dates from" value={from} onChange={(value) => changeRange(() => setFrom(value))} type="date" required />
      <OperationField label="Fixing dates to (inclusive)" value={to} onChange={(value) => changeRange(() => setTo(value))} type="date" required hint="At most 3,700 days per request." />
    </FormGrid>
      <div className="flex flex-wrap gap-2"><Button type="submit" variant="outline" disabled={!rangeEntered || !rangeOrdered} disabledReason={!rangeEntered ? "Enter an index reference and fixing date range." : "The range must not end before it starts."}>Load fixings</Button>
        {query ? <PanelReload reload={fixings.refresh} failed={Boolean(fixings.error)} busy={fixings.loading} what="rate fixings" /> : null}</div>
      {!rangeOrdered ? <p className="text-sm text-warning">The fixing date range must not end before it starts.</p> : null}
    </form>
    <OperationError error={fixings.error} />
    {fixings.loading ? <p role="status">Loading retained rate fixings…</p> : null}
    {fixings.data ? <EntityOperationTable label="Retained rate fixings" rows={fixings.data} getRowId={(fixing) => fixing.fixingId} emptyText="No fixings are retained for this index and date range. None are inferred."
      columns={[
        { id: "date", label: "Fixing date", rowHeader: true, render: (fixing) => <>{fixing.indexReference} {fixing.fixingDate}<p className="mt-1 text-xs">Version {fixing.version}</p>
          {canManage && fixing.pending ? <Button className="mt-2" type="button" size="sm" variant="outline" aria-pressed={selectedId === fixing.fixingId} onClick={() => { setSelectedId(fixing.fixingId); setRejectReason(""); }}>Review pending value for {fixing.fixingDate}</Button> : null}</> },
        { id: "approved", label: "Approved rate", numeric: true, render: (fixing) => fixing.approved ? fixing.approved.rateFraction : "None approved" },
        { id: "pending", label: "Pending rate", numeric: true, render: (fixing) => fixing.pending ? <>{fixing.pending.rateFraction}<p className="mt-1 text-xs">proposed by {fixing.pending.proposedBy}</p></> : "None" },
        { id: "status", label: "Status", render: (fixing) => <Badge variant={fixing.pending ? "warning" : fixing.approved ? "outline" : "warning"}>{fixingStatus(fixing)}</Badge> },
        { id: "source", label: "Source", className: "break-all", render: (fixing) => <>{fixing.approved ? <p>Approved: {fixing.approved.sourceReference}</p> : null}{fixing.pending ? <p>Pending: {fixing.pending.sourceReference}</p> : null}{!fixing.approved && !fixing.pending ? "None" : null}</> },
        { id: "history", label: "History", render: (fixing) => <TechnicalDetails label={`History for ${fixing.indexReference} ${fixing.fixingDate}`}><dl className="space-y-2 break-all text-xs">
          {fixing.approved ? <div><dt>Approved value</dt><dd>{fixing.approved.rateFraction} · {fixing.approved.justification} · proposed by {fixing.approved.proposedBy} at {formatTimestamp(fixing.approved.proposedAt)} · <span className="font-mono">{fixing.approved.valueDigest}</span></dd></div> : null}
          {fixing.pending ? <div><dt>Pending value</dt><dd>{fixing.pending.rateFraction} · {fixing.pending.justification} · proposed by {fixing.pending.proposedBy} at {formatTimestamp(fixing.pending.proposedAt)} · <span className="font-mono">{fixing.pending.valueDigest}</span></dd></div> : null}
          {fixing.events.map((item) => <div key={item.eventId}><dt>{item.action} · {item.actor} · {formatTimestamp(item.recordedAt)}</dt><dd><span className="font-mono">{item.valueDigest}</span>{item.reason ? ` · ${item.reason}` : null}</dd></div>)}
          <div><dt>Fixing ID</dt><dd className="font-mono">{fixing.fixingId}</dd></div>
        </dl></TechnicalDetails> }
      ]} /> : null}

    {canManage && selected?.pending ? <section aria-label="Pending fixing review" className="space-y-3 rounded border border-border p-4">
      <h4 className="font-semibold">Review {selected.indexReference} {selected.fixingDate} · version {selected.version}</h4>
      <p className="text-sm">Pending rate <span className="font-semibold">{selected.pending.rateFraction}</span>{selected.approved ? <> replaces approved {selected.approved.rateFraction}</> : null} · source {selected.pending.sourceReference} · proposed by {selected.pending.proposedBy}</p>
      <p className="text-sm">{selected.pending.justification}</p>
      <p className="text-xs text-muted-foreground">The person who proposed a value cannot approve it; the server refuses a self-approval. The decision applies only to this exact pending value and version.</p>
      <OperationField label="Rejection reason" value={rejectReason} onChange={setRejectReason} hint="Required to reject. Not sent with an approval." />
      <div className="flex flex-wrap gap-2"><Button type="button" disabled={decision.busy} disabledReason="Wait for the current fixing decision to finish." onClick={() => void decide("Approve")}>Approve pending value</Button>
        <Button type="button" variant="outline" disabled={decision.busy || !rejectReason.trim()} disabledReason={decision.busy ? "Wait for the current fixing decision to finish." : "Enter a reason before rejecting."} onClick={() => void decide("Reject")}>Reject pending value</Button></div>
    </section> : null}
    {canManage ? <MutationFeedback state={decision} /> : null}

    {canManage ? <form className="space-y-3 rounded border border-border p-4" aria-label="Propose a fixing value" onSubmit={(event) => void propose(event)}>
      <h4 className="font-semibold">Propose a fixing value</h4>
      <p className="text-sm text-muted-foreground">Record the published value with its source. A proposal for a date that already has an approved value is a correction; it is refused while another value for that date is pending.</p>
      <fieldset disabled={proposal.busy} className="space-y-3"><FormGrid columns={3}>
        <OperationField label="Proposed index reference" value={proposedIndex} onChange={setProposedIndex} required />
        <OperationField label="Proposed fixing date" value={fixingDate} onChange={setFixingDate} type="date" required />
        <OperationField label="Rate fraction (annual)" value={rateFraction} onChange={setRateFraction} pattern={exactFraction} required hint="Exact decimal fraction: 0.0425 means 4.25%. Sent as text, never rounded." />
        <OperationField label="Fixing source reference" value={sourceReference} onChange={setSourceReference} required />
        <OperationField label="Fixing justification" value={justification} onChange={setJustification} required />
      </FormGrid>
        <p className="text-xs text-muted-foreground">{loadedForDate ? `Corrects the loaded record at version ${loadedForDate.version}.` : "No loaded record for this index and date; proposed as a new date (version 0). Load the range first to correct an existing record."}</p>
        <Button type="submit">Propose fixing value</Button></fieldset>
      <MutationFeedback state={proposal} />
    </form> : null}
  </section>;
}
