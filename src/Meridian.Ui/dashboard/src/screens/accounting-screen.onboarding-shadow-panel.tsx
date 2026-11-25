import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { TextArea } from "@/components/ui/text-area";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import * as api from "@/lib/api/accounting-onboarding.api";
import {
  ONBOARDING_DIFFERENCE_CLASSIFICATIONS, ONBOARDING_OPENING_SOURCE_KINDS,
  type OnboardingDifferenceClassification, type OnboardingOpeningAccountMapping, type OnboardingOpeningBalanceLine,
  type OnboardingOpeningSourceKind, type OnboardingParallelRunCertificate, type OnboardingPeriodSummary, type OnboardingWorkspace
} from "@/types/accounting-onboarding";

const field = (data: FormData, name: string) => String(data.get(name) ?? "").trim();
const list = (value: string) => value.split(/[,\n]/).map(item => item.trim()).filter(Boolean);
const amount = (value: number | null | undefined) => value == null ? "Missing" : String(value);
const errorText = (reason: unknown) => reason instanceof Error ? reason.message : "The shadow-mode request failed.";
const SOURCE_LABELS: Record<OnboardingOpeningSourceKind, string> = {
  StatementOpening: "Bank statement opening balance (camt.053 / BAI2)",
  AdministratorPack: "Administrator opening pack (CSV)",
  GlTrialBalance: "Retained GL trial balance"
};

const SOURCE_SHORT_LABELS: Record<OnboardingOpeningSourceKind, string> = {
  StatementOpening: "Bank statement",
  AdministratorPack: "Administrator pack",
  GlTrialBalance: "GL trial balance"
};

/** A difference the owner can explain: a period difference or an opening-balance break. */
interface ExplainableDifference {
  comparisonId: string; differenceKey: string; label: string; formLabel: string; kind: string;
  accountId: string; instrumentId: string | null; meridianAmount: number | null; externalAmount: number | null;
}

const OPENING_COLUMNS: DenseDataTableColumn<OnboardingOpeningBalanceLine>[] = [
  { id: "kind", label: "Kind", rowHeader: true, render: line => line.kind },
  { id: "account", label: "Account", className: "break-all", render: line => line.accountId },
  { id: "instrument", label: "Instrument", render: line => line.instrumentId ?? "—" },
  { id: "currency", label: "Currency", render: line => line.currency },
  { id: "source", label: "Source", render: line => line.sourceKind ? SOURCE_SHORT_LABELS[line.sourceKind as OnboardingOpeningSourceKind] ?? line.sourceKind : "—" },
  { id: "meridian", label: "Meridian", numeric: true, render: line => amount(line.meridianAmount) },
  { id: "incumbent", label: "Incumbent", numeric: true, render: line => amount(line.incumbentAmount) },
  { id: "difference", label: "Difference", numeric: true, render: line => amount(line.difference) },
  { id: "status", label: "Status", render: line => `${line.status}${line.classification ? ` (${line.classification})` : ""}${line.missingReason ? ` — ${line.missingReason}` : ""}` }
];

const PERIOD_COLUMNS: DenseDataTableColumn<OnboardingPeriodSummary>[] = [
  { id: "period", label: "Period", rowHeader: true, render: period => period.asOfDate },
  { id: "meridianNav", label: "Meridian NAV", numeric: true, render: period => amount(period.meridianNav) },
  { id: "incumbentNav", label: "Incumbent NAV", numeric: true, render: period => amount(period.externalNav) },
  { id: "navVariance", label: "NAV variance", numeric: true, render: period => amount(period.navVariance) },
  { id: "matched", label: "Matched", numeric: true, render: period => period.matchedCount },
  { id: "explained", label: "Explained", render: period => `${period.explainedCount}${period.explainedByClassification.length
    ? ` (${period.explainedByClassification.map(item => `${item.classification} ${item.count}`).join(", ")})` : ""}` },
  { id: "unexplained", label: "Unexplained", numeric: true, render: period => period.unexplainedCount },
  { id: "missing", label: "Missing sources", numeric: true, render: period => period.missingSourceCount },
  { id: "status", label: "Status", render: period => period.isClean ? "Clean" : "Open" }
];

/** `EXTERNAL=account-id` per line or comma; an empty list maps incumbent rows that already use Meridian account IDs. */
export function parseAccountMappings(value: string): OnboardingOpeningAccountMapping[] {
  return list(value).map(entry => {
    const index = entry.indexOf("=");
    if (index <= 0 || index === entry.length - 1) throw new Error(`Account mapping "${entry}" must look like EXTERNAL=account-id.`);
    return { externalAccount: entry.slice(0, index).trim(), accountId: entry.slice(index + 1).trim() };
  });
}

/** Reads the exact file bytes; the server retains this content as the replayable opening source. */
function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(reader.error ?? new Error("The opening-balance file could not be read."));
    reader.onload = () => {
      const dataUrl = String(reader.result ?? "");
      resolve(dataUrl.slice(dataUrl.indexOf(",") + 1));
    };
    reader.readAsDataURL(file);
  });
}

function Field({ label, hint, children }: { label: string; hint?: string; children: (id: string) => ReactNode }) {
  const id = useId();
  return <FormRow label={label} labelFor={id} hint={hint}>{children(id)}</FormRow>;
}

/**
 * Shadow-mode onboarding (W11-SHADOW-001): run Meridian beside the incumbent, reconcile opening balances and
 * every period, explain each difference, and cut over only through an independent, certificate-backed promotion.
 */
export function OnboardingShadowPanel({ workspace, onChanged }: { workspace: OnboardingWorkspace; onChanged: (next: OnboardingWorkspace) => void }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [sourceKind, setSourceKind] = useState<OnboardingOpeningSourceKind>("AdministratorPack");
  const [certificate, setCertificate] = useState<OnboardingParallelRunCertificate | null>(null);
  const controller = useRef<AbortController | null>(null);
  const openingFile = useRef<HTMLInputElement | null>(null);
  useEffect(() => {
    controller.current = new AbortController();
    return () => controller.current?.abort();
  }, []);

  async function mutate(action: (signal: AbortSignal) => Promise<OnboardingWorkspace>, success: string) {
    const signal = controller.current!.signal;
    setBusy(true); setError(null); setMessage(null);
    try { const result = await action(signal); if (!signal.aborted) { onChanged(result); setMessage(success); } }
    catch (reason) { if (!signal.aborted) setError(errorText(reason)); }
    finally { if (!signal.aborted) setBusy(false); }
  }

  const designation = workspace.shadowDesignation ?? null;
  const readiness = workspace.readiness;
  const opening = readiness.opening ?? null;
  const periods = readiness.periods ?? [];
  const certificates = workspace.certificates ?? [];
  const inShadow = designation?.state === "Shadow";
  // Missing lines are coverage gaps the server refuses to explain; only numeric breaks are explainable.
  const explainable: ExplainableDifference[] = [
    ...(opening?.lines ?? []).filter(line => line.status === "Break" && line.comparisonId && line.difference != null).map(line => ({
      comparisonId: line.comparisonId!, differenceKey: line.differenceKey, label: "Opening", kind: line.kind,
      formLabel: `Explain opening ${line.kind} difference${line.sourceKind ? ` (${SOURCE_SHORT_LABELS[line.sourceKind as OnboardingOpeningSourceKind] ?? line.sourceKind})` : ""}${line.instrumentId ? ` for ${line.instrumentId}` : ""}`,
      accountId: line.accountId, instrumentId: line.instrumentId, meridianAmount: line.meridianAmount, externalAmount: line.incumbentAmount
    })),
    ...readiness.unresolvedDifferences.filter(item => item.difference.difference != null).map(item => ({
      comparisonId: item.comparisonId, differenceKey: item.difference.differenceKey, label: item.asOfDate, kind: item.difference.kind,
      formLabel: `Explain ${item.difference.kind} difference on ${item.asOfDate}`,
      accountId: item.difference.accountId, instrumentId: item.difference.instrumentId,
      meridianAmount: item.difference.meridianAmount, externalAmount: item.difference.externalAmount
    }))
  ];

  async function importOpening(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const file = openingFile.current?.files?.[0];
    await mutate(async signal => {
      const accountMappings = parseAccountMappings(field(data, "accountMappings"));
      const base = { expectedVersion: workspace.version, sourceKind, mappingProfileId: field(data, "openingMappingProfileId"),
        mappingVersion: field(data, "openingMappingVersion"), accountMappings, notes: field(data, "openingNotes") || undefined };
      if (sourceKind === "GlTrialBalance")
        return api.importOnboardingOpeningBalances(workspace.workspaceId,
          { ...base, providerId: field(data, "openingProviderId"), importId: field(data, "openingImportId") }, { signal });
      if (file == null || file.size === 0) throw new Error("Attach the incumbent opening-balance file.");
      return api.importOnboardingOpeningBalances(workspace.workspaceId,
        { ...base, fileName: file.name, contentBase64: await fileToBase64(file) }, { signal });
    }, "Opening balances imported and reconciled against Meridian's records.");
  }

  return <section className="space-y-4" aria-label="Shadow-mode parallel run">
    <h4 className="text-sm font-semibold">Shadow-mode parallel run</h4>
    {designation == null ? <StatusBanner tone="info" title="Not in shadow mode"
      detail="Designate this ledger book as a shadow book to run Meridian beside the incumbent administrator or GL. A shadow book cannot release reports or hard-close as the book of record until it is promoted." />
      : designation.state === "Shadow" ? <StatusBanner tone="warning" title="Shadow book — not the book of record"
        detail={`Designated by ${designation.designatedBy} on ${designation.designatedAtUtc}. Report release and period hard-close are refused until promotion.`} />
      : designation.state === "Promoted" ? <StatusBanner tone="success" title="Promoted to book of record"
        detail={`Promoted by ${designation.resolvedBy} on ${designation.resolvedAtUtc} with certificate ${workspace.promotion?.certificateId ?? ""}.`} />
      : <StatusBanner tone="danger" title="Shadow trial withdrawn" detail={`${designation.withdrawalReason ?? ""} The book stays non-authoritative.`} />}
    {error ? <StatusBanner tone="danger" role="alert" title="Shadow-mode action failed" detail={error} /> : null}
    {message ? <p role="status" className="text-sm">{message}</p> : null}

    {designation == null ? <form aria-label="Designate shadow book" className="space-y-3" onSubmit={event => {
      event.preventDefault(); const rationale = field(new FormData(event.currentTarget), "shadowRationale");
      void mutate(signal => api.designateShadowBook(workspace.workspaceId, workspace.version, rationale, { signal }), "Ledger book designated as a shadow book.");
    }}>
      <Field label="Designation rationale">{id => <Input id={id} name="shadowRationale" required />}</Field>
      <Button type="submit" disabled={busy}>Designate shadow book</Button>
    </form> : null}

    {inShadow ? <form aria-label="Import incumbent opening balances" className="space-y-3" onSubmit={event => void importOpening(event)}>
      <p className="text-sm text-muted-foreground">Load the incumbent's opening state for {workspace.scope.startDate}. Rows are compared side by side with Meridian's records; an empty amount is missing, never zero. Sources combine: a bank statement or GL trial balance covers cash balances only, so add an administrator pack for positions and NAV. Re-importing a source replaces only that source.</p>
      <FormGrid columns={2}>
        <Field label="Opening source">{id => <Select id={id} value={sourceKind} onChange={event => setSourceKind(event.target.value as OnboardingOpeningSourceKind)}>
          {ONBOARDING_OPENING_SOURCE_KINDS.map(kind => <option key={kind} value={kind}>{SOURCE_LABELS[kind]}</option>)}
        </Select>}</Field>
        {sourceKind === "GlTrialBalance" ? <>
          <Field label="GL provider ID">{id => <Input id={id} name="openingProviderId" required />}</Field>
          <Field label="GL import ID">{id => <Input id={id} name="openingImportId" required />}</Field>
        </> : <Field label="Opening file">{id => <Input id={id} ref={openingFile} name="openingFile" type="file" accept={sourceKind === "AdministratorPack" ? ".csv,text/csv" : ".xml,.camt,.053,.bai,.bai2,.txt"} />}</Field>}
        <Field label="Mapping profile ID">{id => <Input id={id} name="openingMappingProfileId" required />}</Field>
        <Field label="Mapping version">{id => <Input id={id} name="openingMappingVersion" required />}</Field>
      </FormGrid>
      <Field label="Account mappings" hint="One EXTERNAL=account-id per line, for example DE89370400440532013000=<financial account ID>.">
        {id => <TextArea id={id} name="accountMappings" rows={3} />}
      </Field>
      <Field label="Import notes">{id => <Input id={id} name="openingNotes" />}</Field>
      <Button type="submit" disabled={busy}>Import opening balances</Button>
    </form> : null}

    {opening ? <section className="space-y-2" aria-label="Opening-balance reconciliation">
      <StatusBanner tone={opening.isReconciled ? "success" : "warning"} title={opening.isReconciled ? "Opening balances reconciled" : "Opening balances not reconciled"}
        detail={`${opening.matchedCount} matched · ${opening.explainedCount} explained · ${opening.breakCount} breaks · ${opening.missingCount} missing`} />
      {opening.uncoveredKinds?.length ? <StatusBanner tone="warning" title="Opening kinds not yet covered"
        detail={`No incumbent opening source covers ${opening.uncoveredKinds.join(", ")} for every selected account. Import a source that supplies ${opening.uncoveredKinds.length === 1 ? "it" : "them"}.`} /> : null}
      {opening.sources?.length ? <ul className="list-disc space-y-1 pl-5 text-xs" aria-label="Opening sources">{opening.sources.map(source =>
        <li key={source.comparisonId}>{SOURCE_SHORT_LABELS[source.sourceKind as OnboardingOpeningSourceKind] ?? source.sourceKind} · {source.kinds.join(", ")} · <span className="break-all">{source.sourceContentHash.slice(0, 12)}</span></li>)}
      </ul> : null}
      <DenseDataTable ariaLabel="Opening balance lines" columns={OPENING_COLUMNS} rows={opening.lines}
        getRowId={line => line.differenceKey} emptyText="No opening lines were compared." />
    </section> : null}

    {periods.length ? <DenseDataTable ariaLabel="Parallel-run period agreement" columns={PERIOD_COLUMNS} rows={periods}
      getRowId={period => period.comparisonId} emptyText="No required periods have been compared." /> : null}

    {inShadow && explainable.length ? <TechnicalDetails label="Explain parallel-run differences"
      description="Classify each opening break or period difference with retained evidence. A Meridian error never clears; a timing difference lapses after its expected reversal date; any change in the compared amounts needs a new explanation.">
      {explainable.map(item =>
        <form key={`${item.comparisonId}-${item.differenceKey}`} className="mb-4 space-y-2"
          aria-label={item.formLabel} onSubmit={event => {
            event.preventDefault(); const data = new FormData(event.currentTarget);
            const classification = field(data, "classification") as OnboardingDifferenceClassification;
            void mutate(signal => api.explainOnboardingDifference(workspace.workspaceId, item.differenceKey, {
              expectedVersion: workspace.version, comparisonId: item.comparisonId, classification,
              notes: field(data, "explanationNotes"), evidenceIds: list(field(data, "explanationEvidence")),
              expectedReversalDate: classification === "Timing" ? field(data, "expectedReversalDate") || null : null
            }, { signal }), "Difference explanation retained.");
          }}>
          <p className="text-sm">{item.label} · {item.kind} · {item.accountId} {item.instrumentId ?? ""} · Meridian {amount(item.meridianAmount)} vs incumbent {amount(item.externalAmount)}</p>
          <FormGrid columns={2}>
            <Field label="Classification">{id => <Select id={id} name="classification" defaultValue="Timing">
              {ONBOARDING_DIFFERENCE_CLASSIFICATIONS.map(kind => <option key={kind} value={kind}>{kind}</option>)}
            </Select>}</Field>
            <Field label="Expected reversal date" hint="Required for timing differences.">{id => <Input id={id} name="expectedReversalDate" type="date" />}</Field>
            <Field label="Explanation">{id => <Input id={id} name="explanationNotes" required />}</Field>
            <Field label="Evidence IDs">{id => <Input id={id} name="explanationEvidence" required />}</Field>
          </FormGrid>
          <Button type="submit" disabled={busy}>Retain explanation</Button>
        </form>)}
    </TechnicalDetails> : null}

    {inShadow ? <section className="space-y-3" aria-label="Parallel-run certificate">
      <p className="text-sm text-muted-foreground">Issue the certificate once opening balances reconcile, every required period is clean and independent review has approved the current revision.</p>
      <Button variant="outline" disabled={busy || !readiness.isReady} onClick={() => void mutate(async signal => {
        const issued = await api.issueParallelRunCertificate(workspace.workspaceId, workspace.version, { signal });
        if (!signal.aborted) setCertificate(issued);
        return api.getOnboardingWorkspace(workspace.workspaceId, { signal });
      }, "Parallel-run certificate issued.")}>Issue parallel-run certificate</Button>
    </section> : null}

    {certificates.length ? <ul className="space-y-1 text-sm" aria-label="Parallel-run certificates">{certificates.map(item =>
      <li key={item.certificateId}><Button variant="ghost" onClick={() => setCertificate(item)}>{item.issuedAtUtc} · revision {item.content.dataRevision} · {item.contentHash.slice(0, 12)}</Button></li>)}
    </ul> : null}
    {certificate ? <div className="rounded border p-3 text-sm" role="group" aria-label="Certificate detail">
      <p className="font-semibold">Certificate {certificate.certificateId}</p>
      <p>{certificate.content.statement}</p>
      <p>Clean periods: {certificate.content.cleanPeriods.join(", ")} · {certificate.content.explanations.length} explained difference(s)</p>
      <p className="break-all">{certificate.hashAlgorithm} {certificate.contentHash} · packet {certificate.content.packetContentHash}</p>
    </div> : null}

    {inShadow && certificates.length ? <form aria-label="Promote shadow book" className="space-y-3" onSubmit={event => {
      event.preventDefault(); const data = new FormData(event.currentTarget);
      void mutate(signal => api.promoteShadowBook(workspace.workspaceId, {
        expectedVersion: workspace.version, certificateId: field(data, "promotionCertificate"),
        rationale: field(data, "promotionRationale"), evidenceIds: list(field(data, "promotionEvidence"))
      }, { signal }), "Shadow book promoted to book of record.");
    }}>
      <p className="text-sm text-muted-foreground">Promotion is the cutover decision. It must be made by a designated reviewer independent of the onboarding owner, and the certificate must still match the current revision.</p>
      <FormGrid columns={2}>
        <Field label="Certificate">{id => <Select id={id} name="promotionCertificate" defaultValue={certificates.at(-1)!.certificateId}>
          {certificates.map(item => <option key={item.certificateId} value={item.certificateId}>{item.issuedAtUtc} · revision {item.content.dataRevision}</option>)}
        </Select>}</Field>
        <Field label="Promotion rationale">{id => <Input id={id} name="promotionRationale" required />}</Field>
        <Field label="Supporting evidence IDs" hint="Optional; the certificate is always attached.">{id => <Input id={id} name="promotionEvidence" />}</Field>
      </FormGrid>
      <Button type="submit" disabled={busy}>Promote to book of record</Button>
    </form> : null}

    {inShadow ? <form aria-label="Withdraw shadow trial" className="space-y-3" onSubmit={event => {
      event.preventDefault(); const reason = field(new FormData(event.currentTarget), "withdrawReason");
      void mutate(signal => api.withdrawShadowBook(workspace.workspaceId, workspace.version, reason, { signal }), "Shadow trial withdrawn.");
    }}>
      <Field label="Withdrawal reason">{id => <Input id={id} name="withdrawReason" required />}</Field>
      <Button type="submit" variant="outline" disabled={busy}>Withdraw shadow trial</Button>
    </form> : null}

    {workspace.shadowAudit?.length ? <TechnicalDetails label="Shadow-mode audit trail">
      <ol className="list-decimal space-y-1 pl-5 text-xs" aria-label="Shadow-mode audit entries">{workspace.shadowAudit.map(entry =>
        <li key={entry.sequence}>{entry.occurredAtUtc} · {entry.action} · {entry.actorId} · {entry.detail} <span className="break-all">({entry.entryHash.slice(0, 12)})</span></li>)}
      </ol>
    </TechnicalDetails> : null}
  </section>;
}
