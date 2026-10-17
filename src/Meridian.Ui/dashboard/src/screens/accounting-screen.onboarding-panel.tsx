import { useEffect, useId, useRef, useState, type FormEvent, type InputHTMLAttributes } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import * as api from "@/lib/api/accounting-onboarding.api";
import type {
  OnboardingComparison, OnboardingCriteria, OnboardingDifference, OnboardingReadinessPacket, OnboardingSourceSelection, OnboardingWorkspace
} from "@/types/accounting-onboarding";

const split = (value: FormDataEntryValue | null) => String(value ?? "").split(/[,\n]/).map(item => item.trim()).filter(Boolean);
const field = (data: FormData, name: string) => String(data.get(name) ?? "").trim();
const errorText = (reason: unknown) => reason instanceof Error ? reason.message : "The onboarding request failed.";
const amount = (value: number | null) => value == null ? "Missing" : String(value);

function TextField({ label, hint, ...props }: InputHTMLAttributes<HTMLInputElement> & { label: string; hint?: string }) {
  const id = useId();
  return <FormRow label={label} labelFor={id} hint={hint}><Input id={id} {...props} /></FormRow>;
}

function CriteriaFields({ criteria }: { criteria?: OnboardingCriteria }) {
  return <FormGrid columns={3}>
    <TextField name="requiredDates" label="Required dates" required defaultValue={criteria?.requiredDates.join(", ")} hint="Comma-separated YYYY-MM-DD dates inside the scope." />
    <TextField name="balanceTolerance" label="Balance tolerance" type="number" min="0" step="any" required defaultValue={criteria?.balanceTolerance ?? 0} />
    <TextField name="positionTolerance" label="Position tolerance" type="number" min="0" step="any" required defaultValue={criteria?.positionTolerance ?? 0} />
    <TextField name="navTolerance" label="NAV tolerance" type="number" min="0" step="any" required defaultValue={criteria?.navTolerance ?? 0} />
    <TextField name="minimumCoveragePercent" label="Minimum coverage (%)" type="number" min="0" max="100" step="any" required defaultValue={criteria?.minimumCoveragePercent ?? 100} />
    <TextField name="minimumReviewers" label="Minimum independent reviewers" type="number" min="1" step="1" required defaultValue={criteria?.minimumReviewers ?? 1} />
    <TextField name="requiredReviewerIds" label="Designated reviewer identities" defaultValue={criteria?.requiredReviewerIds.join(", ")} hint="Comma-separated reviewer roster. The minimum reviewer count must approve; decisions use the signed-in reviewer." />
    <TextField name="reviewInstructions" label="Review criteria" required defaultValue={criteria?.reviewInstructions} />
    <fieldset className="space-y-2 text-sm">
      <legend className="mb-2 text-xs font-semibold">Required coverage</legend>
      {[["Balance", "Balances"], ["Position", "Positions"], ["Nav", "NAV"]].map(([value, label]) => (
        <label key={value} className="mr-3 inline-flex items-center gap-2"><input type="checkbox" name="requiredKinds" value={value} defaultChecked={criteria ? criteria.requiredKinds.includes(value) : true} />{label}</label>
      ))}
      <label className="flex items-center gap-2"><input type="checkbox" name="requireCloseReadiness" defaultChecked={criteria?.requireCloseReadiness ?? true} />Require close readiness</label>
    </fieldset>
  </FormGrid>;
}

function criteriaFrom(data: FormData): OnboardingCriteria {
  return {
    requiredDates: split(data.get("requiredDates")), requiredKinds: data.getAll("requiredKinds").map(String),
    balanceTolerance: Number(data.get("balanceTolerance")), positionTolerance: Number(data.get("positionTolerance")), navTolerance: Number(data.get("navTolerance")),
    minimumCoveragePercent: Number(data.get("minimumCoveragePercent")), minimumReviewers: Number(data.get("minimumReviewers")),
    requiredReviewerIds: split(data.get("requiredReviewerIds")), requireCloseReadiness: data.has("requireCloseReadiness"), reviewInstructions: field(data, "reviewInstructions")
  };
}

/** Bounded support work stays inside Accounting's existing External GL surface. */
export function AccountingOnboardingPanel() {
  const [workspaces, setWorkspaces] = useState<OnboardingWorkspace[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reload, setReload] = useState(0);
  const scopeController = useRef<AbortController | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    scopeController.current = controller;
    setLoading(true); setError(null);
    api.listOnboardingWorkspaces({ signal: controller.signal }).then(next => {
      if (controller.signal.aborted) return;
      setWorkspaces(next);
      setSelectedId(current => next.some(item => item.workspaceId === current) ? current : "");
    }).catch(reason => { if (!controller.signal.aborted) setError(errorText(reason)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [reload]);

  function update(workspace: OnboardingWorkspace) {
    setWorkspaces(current => [...current.filter(item => item.workspaceId !== workspace.workspaceId), workspace]);
    setSelectedId(workspace.workspaceId);
  }

  async function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    setLoading(true); setError(null);
    const signal = scopeController.current?.signal;
    try {
      const workspace = await api.createOnboardingWorkspace({
        name: field(data, "name"),
        scope: { entityId: field(data, "entityId"), fundProfileId: field(data, "fundProfileId"), ledgerBookId: field(data, "ledgerBookId"),
          accountIds: split(data.get("accountIds")), startDate: field(data, "startDate"), endDate: field(data, "endDate") },
        criteria: criteriaFrom(data)
      }, { signal });
      if (!signal?.aborted) update(workspace);
    } catch (reason) { if (!signal?.aborted) setError(errorText(reason)); }
    finally { if (!signal?.aborted) setLoading(false); }
  }

  const workspace = workspaces.find(item => item.workspaceId === selectedId);
  return <Card id="accounting-onboarding" className="panel-surface scroll-mt-6" role="region" aria-label="Bounded onboarding workspace">
    <CardHeader>
      <CardTitle>Bounded onboarding</CardTitle>
      <CardDescription>Compare one entity and account population over required dates. Every comparison retains its source snapshots, mapping version, differences and review evidence.</CardDescription>
    </CardHeader>
    <CardContent className="space-y-4">
      <p className="text-sm text-muted-foreground">External books remain read-only. A readiness packet supports review; any accounting-authority transition requires the separately governed process.</p>
      {error ? <StatusBanner tone="danger" role="alert" title="Onboarding unavailable" detail={error} /> : null}
      <div className="flex flex-wrap items-end gap-3">
        <FormRow label="Retained workspace" labelFor="onboarding-workspace" className="min-w-[16rem] flex-1">
          <Select id="onboarding-workspace" value={selectedId} onChange={event => setSelectedId(event.target.value)} disabled={loading}>
            <option value="">{loading ? "Loading workspaces…" : "Select a workspace"}</option>
            {workspaces.map(item => <option key={item.workspaceId} value={item.workspaceId}>{item.name} · {item.scope.entityId} · {item.scope.startDate} – {item.scope.endDate}</option>)}
          </Select>
        </FormRow>
        <Button variant="outline" disabled={loading} onClick={() => { setSelectedId(""); setReload(value => value + 1); }}>Refresh workspaces</Button>
      </div>
      {!loading && !error && workspaces.length === 0 ? <p role="status" className="text-sm">No onboarding workspaces retained. Define a bounded scope to begin.</p> : null}
      <TechnicalDetails label="Define onboarding scope">
        <form onSubmit={event => void create(event)} className="space-y-4" aria-label="Create onboarding workspace">
          <FormGrid columns={3}>
            <TextField name="name" label="Workspace name" required />
            <TextField name="entityId" label="Entity ID" required />
            <TextField name="fundProfileId" label="Book profile ID" required />
            <TextField name="ledgerBookId" label="Ledger book ID" required />
            <TextField name="accountIds" label="Financial account IDs" required hint="Comma-separated financial account GUIDs belonging to the selected entity and book; the scope is fixed after creation." />
            <TextField name="startDate" label="Start date" type="date" required />
            <TextField name="endDate" label="End date" type="date" required />
          </FormGrid>
          <CriteriaFields />
          <Button type="submit" disabled={loading}>Create workspace</Button>
        </form>
      </TechnicalDetails>
      {workspace ? <WorkspaceDetail key={workspace.workspaceId} workspace={workspace} onChanged={update} /> : null}
    </CardContent>
  </Card>;
}

function WorkspaceDetail({ workspace, onChanged }: { workspace: OnboardingWorkspace; onChanged: (next: OnboardingWorkspace) => void }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [comparisonId, setComparisonId] = useState("");
  const [packet, setPacket] = useState<OnboardingReadinessPacket | null>(null);
  const [sources, setSources] = useState<OnboardingSourceSelection[]>([]);
  const [sourceError, setSourceError] = useState<string | null>(null);
  const [sourceReload, setSourceReload] = useState(0);
  const [capture, setCapture] = useState<OnboardingSourceSelection>({ providerId: "", importId: "", asOfDate: "", mappingProfileId: "", mappingVersion: "" });
  const controller = useRef<AbortController | null>(null);
  useEffect(() => {
    controller.current = new AbortController();
    return () => controller.current?.abort();
  }, []);
  useEffect(() => {
    const sourceController = new AbortController();
    setSourceError(null);
    api.getOnboardingSources(workspace.workspaceId, { signal: sourceController.signal }).then(next => {
      if (!sourceController.signal.aborted) setSources(next);
    }).catch(reason => { if (!sourceController.signal.aborted) setSourceError(errorText(reason)); });
    return () => sourceController.abort();
  }, [workspace.workspaceId, sourceReload]);
  async function mutate(action: (signal: AbortSignal) => Promise<OnboardingWorkspace>, success: string) {
    const signal = controller.current!.signal;
    setBusy(true); setError(null); setMessage(null);
    try { const result = await action(signal); if (!signal.aborted) { onChanged(result); setMessage(success); } }
    catch (reason) { if (!signal.aborted) setError(errorText(reason)); }
    finally { if (!signal.aborted) setBusy(false); }
  }
  const selected = workspace.comparisons.find(item => item.comparisonId === comparisonId) ?? workspace.comparisons.at(-1);
  const sourceIndex = sources.findIndex(source => source.providerId === capture.providerId && source.importId === capture.importId
    && source.asOfDate === capture.asOfDate && source.mappingProfileId === capture.mappingProfileId && source.mappingVersion === capture.mappingVersion);

  return <section className="space-y-4" aria-label={`Onboarding ${workspace.name}`}>
    <div className="text-sm">
      <h3 className="font-semibold">{workspace.name}</h3>
      <p>Entity {workspace.scope.entityId} · {workspace.scope.startDate} – {workspace.scope.endDate} · Owner {workspace.ownerId}</p>
      <p>Accounts: {workspace.scope.accountIds.join(", ")} · Data revision {workspace.dataRevision}</p>
    </div>
    <StatusBanner tone={workspace.readiness.isReady ? "success" : "warning"} title={`Readiness: ${workspace.readiness.status}`} detail={`${workspace.readiness.unresolvedDifferenceCount} unresolved differences · ${workspace.readiness.approvedReviewers}/${workspace.readiness.requiredReviewers} reviewers approved`} />
    {workspace.readiness.blockers.length ? <ul className="list-disc space-y-1 pl-5 text-sm" aria-label="Readiness blockers">{workspace.readiness.blockers.map((item, index) => <li key={index}>{item}</li>)}</ul> : null}
    {workspace.readiness.missingDates.length ? <p className="text-sm">Missing required dates: {workspace.readiness.missingDates.join(", ")}</p> : null}
    {workspace.readiness.missingSources.length ? <ul className="list-disc pl-5 text-sm" aria-label="Missing sources">{workspace.readiness.missingSources.map((item, index) => <li key={index}>{item.sourceKind} · {item.accountId ?? "Book"}: {item.message}</li>)}</ul> : null}
    {workspace.readiness.unresolvedDifferences.length ? <TechnicalDetails label="Unresolved differences across required dates">
      {workspace.readiness.unresolvedDifferences.map(item => <div className="mb-3" key={`${item.comparisonId}-${item.difference.differenceKey}`}><h4 className="text-sm font-semibold">{item.asOfDate}</h4><DifferenceTable differences={[item.difference]} /></div>)}
    </TechnicalDetails> : null}
    {error ? <StatusBanner tone="danger" role="alert" title="Onboarding action failed" detail={error} /> : null}
    {message ? <p role="status" className="text-sm">{message}</p> : null}
    <TechnicalDetails label="Coverage and review criteria" description="The onboarding owner can revise criteria. Earlier comparisons and frozen packets keep their original criteria; new work requires review of the current revision.">
      <form aria-label="Update onboarding criteria" key={workspace.dataRevision} className="space-y-3" onSubmit={event => { event.preventDefault(); const criteria = criteriaFrom(new FormData(event.currentTarget)); void mutate(signal => api.updateOnboardingCriteria(workspace.workspaceId, workspace.version, criteria, { signal }), "Criteria revision retained."); }}>
        <CriteriaFields criteria={workspace.criteria} />
        <Button type="submit" variant="outline" disabled={busy}>Save criteria revision</Button>
      </form>
    </TechnicalDetails>
    <TechnicalDetails label="Capture next comparison" description="Import external evidence through the existing import workflow first. Supply its exact retained import and mapping identities. The server captures Meridian ledger, position, NAV and close evidence for the selected date; unavailable sources remain missing.">
      {sourceError ? <StatusBanner role="alert" tone="danger" title="Source selections unavailable" detail={sourceError} /> : null}
      <div className="mb-3 flex flex-wrap items-end gap-3">
        <FormRow label="Available retained source" labelFor="onboarding-source" className="min-w-[16rem] flex-1">
          <Select id="onboarding-source" value={sourceIndex < 0 ? "" : sourceIndex} onChange={event => { if (!event.target.value) return; const selected = sources[Number(event.target.value)]; if (selected) setCapture(selected); }}>
            <option value="">Select exact import and mapping, or enter identities below</option>
            {sources.map((source, index) => <option key={index} value={index}>{source.asOfDate} · {source.providerId} · {source.importId} · mapping {source.mappingVersion}</option>)}
          </Select>
        </FormRow>
        <Button variant="outline" disabled={busy} onClick={() => setSourceReload(value => value + 1)}>Refresh sources</Button>
      </div>
      <form aria-label="Capture onboarding comparison" className="space-y-3" onSubmit={event => {
        event.preventDefault(); const data = new FormData(event.currentTarget);
        void mutate(signal => api.captureOnboardingComparison(workspace.workspaceId, {
          expectedVersion: workspace.version, asOfDate: field(data, "asOfDate"), providerId: field(data, "providerId"), importId: field(data, "importId"),
          mappingProfileId: field(data, "mappingProfileId"), mappingVersion: field(data, "mappingVersion"), notes: field(data, "notes")
        }, { signal }), "Comparison retained against exact source snapshots.");
      }}>
        <FormGrid columns={3}>
          <TextField label="Comparison date" name="asOfDate" type="date" min={workspace.scope.startDate} max={workspace.scope.endDate} value={capture.asOfDate} onChange={event => setCapture(current => ({ ...current, asOfDate: event.target.value }))} required />
          <TextField label="Provider ID" name="providerId" value={capture.providerId} onChange={event => setCapture(current => ({ ...current, providerId: event.target.value }))} required />
          <TextField label="Retained import ID" name="importId" value={capture.importId} onChange={event => setCapture(current => ({ ...current, importId: event.target.value }))} required />
          <TextField label="Mapping profile ID" name="mappingProfileId" value={capture.mappingProfileId} onChange={event => setCapture(current => ({ ...current, mappingProfileId: event.target.value }))} required />
          <TextField label="Mapping version" name="mappingVersion" value={capture.mappingVersion} onChange={event => setCapture(current => ({ ...current, mappingVersion: event.target.value }))} required />
          <TextField label="Comparison notes" name="notes" />
        </FormGrid>
        <Button type="submit" disabled={busy}>Capture comparison</Button>
      </form>
    </TechnicalDetails>
    <section className="space-y-3" aria-label="Consecutive comparison history">
      <h4 className="text-sm font-semibold">Consecutive comparisons</h4>
      {workspace.comparisons.length ? <>
        <FormRow label="Retained comparison" labelFor="onboarding-comparison">
          <Select id="onboarding-comparison" value={selected?.comparisonId ?? ""} onChange={event => setComparisonId(event.target.value)}>
            {workspace.comparisons.map(item => <option key={item.comparisonId} value={item.comparisonId}>#{item.sequence} · {item.asOfDate} · {item.coveragePercent}% coverage · revision {item.dataRevision}</option>)}
          </Select>
        </FormRow>
        {selected ? <>
          <ComparisonDetail comparison={selected} />
          <Button variant="outline" disabled={busy} onClick={() => void mutate(async signal => {
            const replayed = await api.replayOnboardingComparison(workspace.workspaceId, selected.comparisonId, { signal });
            if (replayed.contentHash !== selected.contentHash) throw new Error("Historical replay did not match the retained comparison hash.");
            return workspace;
          }, `Comparison #${selected.sequence} reproduced from its retained snapshots and mapping.`)}>Reproduce selected comparison</Button>
        </> : null}
      </> : <p className="text-sm text-muted-foreground">No comparisons retained. Import sources, then capture the first required date.</p>}
    </section>
    <section className="space-y-3" aria-label="Current difference ownership">
      <h4 className="text-sm font-semibold">Current differences and ownership</h4>
      {workspace.currentDifferences.length ? workspace.currentDifferences.map(item => <TechnicalDetails key={item.differenceKey} label={`${item.status} · ${item.kind} · ${item.accountId} · ${item.instrumentId ?? item.currency} · owner ${item.ownerId || "Unassigned"}`}>
        <DifferenceTable differences={[item]} />
        <form aria-label={`Assign ${item.kind} difference`} className="mt-3 space-y-3" onSubmit={event => { event.preventDefault(); const data = new FormData(event.currentTarget); void mutate(signal => api.assignOnboardingDifference(workspace.workspaceId, item.differenceKey, {
          expectedVersion: workspace.version, ownerId: field(data, "ownerId"), notes: field(data, "notes"), evidenceIds: split(data.get("evidenceIds"))
        }, { signal }), "Difference owner and supporting evidence retained."); }}>
          <FormGrid columns={3}>
            <TextField name="ownerId" label="Difference owner" required defaultValue={item.ownerId} />
            <TextField name="notes" label="Ownership notes" required />
            <TextField name="evidenceIds" label="Supporting evidence IDs" hint="Comma-separated retained evidence identities." />
          </FormGrid>
          <Button type="submit" variant="outline" disabled={busy}>Save difference ownership</Button>
        </form>
      </TechnicalDetails>) : <p className="text-sm text-muted-foreground">No differences recorded. Readiness still depends on required dates, source coverage and review.</p>}
    </section>
    <TechnicalDetails label="Readiness review" description="Record an independent decision on the current data revision. The signed-in user's identity is retained by the server; a decision does not resolve a numerical difference.">
      <p className="mb-3 text-sm">{workspace.criteria.reviewInstructions}</p>
      <form aria-label="Review onboarding readiness" className="space-y-3" onSubmit={event => { event.preventDefault(); const data = new FormData(event.currentTarget); void mutate(signal => api.reviewOnboardingWorkspace(workspace.workspaceId, {
        expectedVersion: workspace.version, dataRevision: workspace.dataRevision, decision: field(data, "decision"), notes: field(data, "notes"), evidenceIds: split(data.get("evidenceIds"))
      }, { signal }), "Reviewer decision retained for this data revision."); }}>
        <FormGrid columns={3}>
          <FormRow label="Reviewer decision" labelFor="onboarding-review-decision"><Select id="onboarding-review-decision" name="decision"><option value="ChangesRequested">Changes requested</option><option value="Approved">Approved</option></Select></FormRow>
          <TextField name="notes" label="Reviewer notes" required />
          <TextField name="evidenceIds" label="Review evidence IDs" />
        </FormGrid>
        <Button type="submit" variant="outline" disabled={busy}>Record reviewer decision</Button>
      </form>
      <ul className="mt-3 space-y-2 text-sm" aria-label="Reviewer decision history">{workspace.reviews.map(item => <li key={item.reviewId}>{item.decision} · {item.reviewerId} · revision {item.dataRevision} · {item.recordedAtUtc}<p>{item.notes}</p><p className="break-all">Evidence: {item.evidenceIds.join(", ") || "None"}</p></li>)}</ul>
    </TechnicalDetails>
    <section className="space-y-3" aria-label="Frozen readiness packets">
      <h4 className="text-sm font-semibold">Frozen readiness packets</h4>
      <p className="text-sm text-muted-foreground">Freeze the current scope, comparisons, unresolved differences, missing sources and reviewer decisions. A blocked packet remains blocked and historical packets never change.</p>
      <Button variant="outline" disabled={busy} onClick={() => void mutate(async signal => {
        const result = await api.freezeOnboardingPacket(workspace.workspaceId, workspace.version, { signal });
        if (!signal.aborted) setPacket(result);
        return api.getOnboardingWorkspace(workspace.workspaceId, { signal });
      }, "Readiness packet frozen.")}>Freeze readiness packet</Button>
      {workspace.packets.map(item => <div className="flex flex-wrap items-center gap-3 text-sm" key={item.packetId}>
        <Button variant="ghost" disabled={busy} onClick={() => { setPacket(item); }}>{item.frozenAtUtc} · {item.content.readiness.status} · revision {item.content.dataRevision}</Button>
        <span>Frozen by {item.frozenBy}</span>
      </div>)}
      {packet ? <PacketDetail packet={packet} /> : null}
    </section>
  </section>;
}

function DifferenceTable({ differences }: { differences: OnboardingDifference[] }) {
  return <div className="overflow-x-auto"><table className="w-full text-left text-xs" aria-label="Retained balance position and NAV differences">
    <thead><tr>{["State / kind", "Account / instrument", "Meridian", "External", "Difference / tolerance", "Owner / evidence"].map(label => <th key={label} scope="col" className="p-2">{label}</th>)}</tr></thead>
    <tbody>{differences.map(item => <tr key={item.differenceKey} className="border-t border-border">
      <th scope="row" className="p-2 font-normal">{item.status} · {item.kind}</th>
      <td className="p-2">{item.accountId}<br />{item.instrumentId ?? item.currency}</td>
      <td className="p-2 font-mono">{amount(item.meridianAmount)}</td><td className="p-2 font-mono">{amount(item.externalAmount)}</td>
      <td className="p-2 font-mono">{amount(item.difference)} / {item.tolerance}<p>{item.missingReason}</p></td>
      <td className="max-w-xs break-all p-2">{item.ownerId || "Unassigned"}<p>Evidence: {item.evidenceIds.join(", ") || "None retained"}</p></td>
    </tr>)}</tbody>
  </table></div>;
}

function ComparisonDetail({ comparison }: { comparison: OnboardingComparison }) {
  return <div className="space-y-3">
    <p className="text-sm">{comparison.asOfDate} · {comparison.coveragePercent}% coverage · Captured by {comparison.actorId} at {comparison.capturedAtUtc}</p>
    {comparison.differences.length ? <DifferenceTable differences={comparison.differences} /> : <p className="text-sm">No differences in this retained comparison.</p>}
    <TechnicalDetails label="Exact source snapshots and mapping" description="These retained inputs belong to this comparison; selecting a historical run never reads today's sources.">
      <dl className="space-y-2 break-all text-xs">
        <dt className="font-semibold">Import / mapping</dt><dd>{comparison.providerId} · {comparison.importId} · {comparison.mappingProfileId} · {comparison.mappingVersion}</dd>
        <dt className="font-semibold">Comparison hash / predecessor / algorithm</dt><dd>{comparison.contentHash}<br />{comparison.previousContentHash ?? "First comparison"}<br />{comparison.algorithmVersion}</dd>
      </dl>
      {comparison.inputs.snapshots.map(snapshot => <TechnicalDetails key={snapshot.snapshotId} className="mt-3" label={`${snapshot.sourceKind} · ${snapshot.sourceId} · ${snapshot.version}`}>
        <p className="break-all text-xs">Snapshot {snapshot.snapshotId} · {snapshot.asOfDate} · {snapshot.capturedAtUtc}<br />Hash {snapshot.contentHash}<br />Mapping {snapshot.mappingVersion}<br />Evidence: {snapshot.evidenceIds.join(", ") || "None retained"}</p>
        <pre className="mt-3 max-h-64 overflow-auto whitespace-pre-wrap break-all text-xs">{snapshot.payloadJson}</pre>
      </TechnicalDetails>)}
      <TechnicalDetails label="Retained criteria and close evidence" className="mt-3"><pre className="max-h-64 overflow-auto whitespace-pre-wrap break-all text-xs">{JSON.stringify({ criteria: comparison.criteria, closeReadiness: comparison.inputs.closeReadiness, missingSources: comparison.inputs.missingSources }, null, 2)}</pre></TechnicalDetails>
    </TechnicalDetails>
  </div>;
}

function PacketDetail({ packet }: { packet: OnboardingReadinessPacket }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const controller = useRef<AbortController | null>(null);
  useEffect(() => {
    controller.current = new AbortController();
    setBusy(false); setError(null);
    return () => controller.current?.abort();
  }, [packet.packetId]);
  async function download() {
    const signal = controller.current!.signal;
    setBusy(true); setError(null);
    try {
      const blob = await api.downloadOnboardingPacket(packet.content.workspaceId, packet.packetId, { signal });
      if (signal.aborted) return;
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url; link.download = `onboarding-readiness-${packet.packetId}.json`;
      document.body.append(link); link.click(); link.remove();
      window.setTimeout(() => URL.revokeObjectURL(url), 0);
    } catch (reason) { if (!signal.aborted) setError(errorText(reason)); }
    finally { if (!signal.aborted) setBusy(false); }
  }
  return <TechnicalDetails open label={`Frozen packet · ${packet.content.readiness.status} · revision ${packet.content.dataRevision}`}>
    <p className="text-sm">{packet.content.readiness.unresolvedDifferenceCount} unresolved differences · {packet.content.readiness.missingSources.length} missing sources · {packet.content.reviews.length} reviewer decisions</p>
    <p className="mt-2 break-all text-xs">{packet.hashAlgorithm}: {packet.contentHash}</p>
    {packet.content.readiness.unresolvedDifferences.length ? <TechnicalDetails className="mt-3" label="Frozen unresolved differences">
      {packet.content.readiness.unresolvedDifferences.map(item => <div className="mb-3" key={`${item.comparisonId}-${item.difference.differenceKey}`}><h4 className="text-sm font-semibold">{item.asOfDate}</h4><DifferenceTable differences={[item.difference]} /></div>)}
    </TechnicalDetails> : null}
    {packet.content.readiness.missingDates.length ? <p className="mt-3 text-sm">Missing dates at freeze: {packet.content.readiness.missingDates.join(", ")}</p> : null}
    {packet.content.readiness.missingSources.length ? <ul className="mt-3 list-disc pl-5 text-sm" aria-label="Frozen missing sources">{packet.content.readiness.missingSources.map((item, index) => <li key={index}>{item.sourceKind} · {item.accountId ?? "Book"}: {item.message}</li>)}</ul> : null}
    {packet.content.reviews.length ? <TechnicalDetails className="mt-3" label="Frozen reviewer decisions">
      <ul className="space-y-2 text-sm">{packet.content.reviews.map(review => <li key={review.reviewId}>{review.decision} · {review.reviewerId} · revision {review.dataRevision} · {review.recordedAtUtc}<p>{review.notes}</p><p className="break-all">Evidence: {review.evidenceIds.join(", ") || "None"}</p></li>)}</ul>
    </TechnicalDetails> : null}
    <Button className="my-3" variant="outline" disabled={busy} onClick={() => void download()}>Download frozen readiness packet</Button>
    {error ? <StatusBanner role="alert" tone="danger" title="Packet download failed" detail={error} /> : null}
    <TechnicalDetails label="Frozen manifest preview" description="The download retains exact server bytes and decimal precision."><pre className="max-h-96 overflow-auto whitespace-pre-wrap break-all text-xs">{JSON.stringify(packet, null, 2)}</pre></TechnicalDetails>
  </TechnicalDetails>;
}
