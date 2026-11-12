import { useEffect, useRef, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { isApiError } from "@/lib/api-errors";
import { formatUtcTimestamp } from "@/lib/format";
import {
  attestCashDividend, CASH_DIVIDEND_ATTESTATION_CONFIRMATION, CASH_DIVIDEND_ATTESTATION_POLICY,
  changeCashDividendBookDeclaration, getCashDividendAttestationWorkspace, postAttestedCashDividend,
  sameCashDividendActor, sameCashDividendScope
} from "@/lib/cash-dividend-attestation-api";
import { PREPARER_ATTESTATION_DISCLOSURE } from "@/lib/single-principal-attestation-api";
import type { CorporateActionCaseScope } from "@/types/workstation-7";
import type {
  AttestCashDividendRequest, CashDividendAccountRoleBinding, CashDividendAttestationWorkspace, ChangeCashDividendBookDeclarationRequest,
  PostAttestedCashDividendRequest
} from "@/types/cash-dividend-attestation";

type PendingCommand = { kind: "declaration"; request: ChangeCashDividendBookDeclarationRequest }
  | { kind: "attest"; request: AttestCashDividendRequest }
  | { kind: "post"; request: PostAttestedCashDividendRequest };

export function CashDividendAttestationPanel({ caseId, scope, onCommitted }: {
  caseId: string; scope: CorporateActionCaseScope; onCommitted?: () => void;
}) {
  return <CashDividendControls key={`${caseId}/${scope.tenantId}/${scope.companyId}/${scope.ledgerBookId ?? ""}`}
    caseId={caseId} scope={scope} onCommitted={onCommitted} />;
}

function CashDividendControls({ caseId, scope, onCommitted }: {
  caseId: string; scope: CorporateActionCaseScope; onCommitted?: () => void;
}) {
  const [workspace, setWorkspace] = useState<CashDividendAttestationWorkspace | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<{ message: string; command: boolean } | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [reload, setReload] = useState(0);
  const [pending, setPending] = useState<PendingCommand | null>(null);
  const [declarationReason, setDeclarationReason] = useState("");
  const [declareConfirmed, setDeclareConfirmed] = useState(false);
  const [bindingConfirmed, setBindingConfirmed] = useState(false);
  const [attestReason, setAttestReason] = useState("");
  const [attestConfirmed, setAttestConfirmed] = useState(false);
  const [postReason, setPostReason] = useState("");
  const [postConfirmed, setPostConfirmed] = useState(false);
  const alive = useRef(true);
  const inFlight = useRef(false);
  const expectedScope = JSON.stringify({ tenantId: scope.tenantId, companyId: scope.companyId, ledgerBookId: scope.ledgerBookId });

  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setWorkspace(null); setAttestConfirmed(false); setPostConfirmed(false); setDeclareConfirmed(false); setBindingConfirmed(false);
    const selectedScope = JSON.parse(expectedScope) as CorporateActionCaseScope;
    void getCashDividendAttestationWorkspace(caseId, selectedScope, { signal: controller.signal }).then(value => {
      if (controller.signal.aborted) return;
      setWorkspace(value); setLoading(false);
      if (!inFlight.current) {
        const recovered = readRecovery(caseId, value);
        setPending(recovered);
        if (recovered) setNotice("An unconfirmed command was recovered. Inspect the current record and retry the same command; do not create another financial action.");
      }
    }).catch(failure => {
      if (!controller.signal.aborted) {
        setLoading(false);
        setError({ message: errorMessage(failure), command: false });
      }
    });
    return () => controller.abort();
  }, [caseId, expectedScope, reload]);

  async function submit(command: PendingCommand) {
    if (!workspace || inFlight.current) return;
    inFlight.current = true; setBusy(true); setError(null); setNotice(null);
    let dispatched = false;
    try {
      sessionStorage.setItem(recoveryKey(caseId, workspace), JSON.stringify({ actor: workspace.actor,
        caseId, scope: workspace.scope, command }));
      setPending(command);
      dispatched = true;
      if (command.kind === "declaration") await changeCashDividendBookDeclaration(caseId, command.request);
      else if (command.kind === "attest") await attestCashDividend(caseId, command.request);
      else await postAttestedCashDividend(caseId, command.request);
      clearRecovery(caseId, workspace);
      if (!alive.current) return;
      setPending(null); setDeclarationReason(""); setAttestReason(""); setPostReason("");
      setDeclareConfirmed(false); setBindingConfirmed(false); setAttestConfirmed(false); setPostConfirmed(false);
      setNotice(command.kind === "post" ? "The attested cash-dividend journal was posted. Its retained decision remains labeled as preparer attestation."
        : command.kind === "attest" ? "Preparer attestation retained. Posting remains a separate explicit command."
          : "Book declaration change retained in its audit history.");
      setReload(value => value + 1);
      onCommitted?.();
    } catch (failure) {
      if (!alive.current) return;
      if (!dispatched || isApiError(failure) && failure.status >= 400 && failure.status < 500) {
        clearRecovery(caseId, workspace); setPending(null); setWorkspace(null);
        setAttestConfirmed(false); setPostConfirmed(false); setDeclareConfirmed(false); setBindingConfirmed(false);
        setError({ message: `${errorMessage(failure)} Refresh the case, declaration and eligibility before another command.`, command: true });
      } else {
        setError({ message: `${errorMessage(failure)} The outcome is unconfirmed. Retry this exact command; do not create a replacement action.`, command: true });
      }
    } finally {
      inFlight.current = false;
      if (alive.current) setBusy(false);
    }
  }

  const known = workspace?.schemaVersion === 1 && workspace.availability === "Available"
    && workspace.policyVersion === CASH_DIVIDEND_ATTESTATION_POLICY
    && Boolean(workspace.actor?.trim()) && canonicalHash(workspace.policyHash)
    && workspace.disclosure === PREPARER_ATTESTATION_DISCLOSURE
    && workspace.confirmation === CASH_DIVIDEND_ATTESTATION_CONFIRMATION;
  const locked = loading || busy || pending !== null;
  const declaration = workspace?.declaration;
  const preview = workspace?.preview;
  const attestation = workspace?.attestation;
  const accountRoleBinding = bindingOperands(preview?.eligibility.accountRoleBinding, workspace?.scope.ledgerBookId);
  const displayedBinding = declaration?.enabled ? bindingOperands(declaration.accountRoleBinding, workspace?.scope.ledgerBookId) : accountRoleBinding;
  const canEnableDeclaration = Boolean(workspace?.policyEnabled && workspace.canBindAccountingRoles && accountRoleBinding);
  const ownsDeclaration = declaration?.enabled === true && sameCashDividendActor(declaration.principalId, workspace?.actor)
    && declaration.ledgerBookId === workspace?.scope.ledgerBookId;
  const hasEvidence = Boolean(workspace?.evidenceReference?.trim() && canonicalHash(workspace.evidenceHash)
    && preview && canonicalHash(preview.eligibility.entitlementSnapshotHash));
  const canAttest = Boolean(known && !locked && workspace?.policyEnabled && workspace.canAttestAccounting
    && ownsDeclaration && preview && preview.projection.isCurrent && hasEvidence
    && !attestation?.isActive && !workspace.posting
    && declaration?.policyVersion === workspace.policyVersion && declaration.policyHash === workspace.policyHash
    && sameCashDividendActor(preview.projection.preparedBy, workspace.actor));
  const canPost = Boolean(known && !locked && workspace?.policyEnabled && workspace.canPostAttestedAccounting
    && ownsDeclaration && attestation?.isActive && !workspace.posting
    && attestation.declarationId === declaration?.declarationId && attestation.declarationRevision === declaration.revision
    && attestation.decisionKind === "PreparerAttestation" && attestation.disclosure === PREPARER_ATTESTATION_DISCLOSURE
    && sameCashDividendActor(attestation.attestedBy, workspace.actor));

  return <section className="space-y-3 rounded-md border border-border bg-secondary/10 p-3"
    aria-label="Bounded cash-dividend preparer attestation" aria-busy={loading || busy}>
    <div className="flex flex-wrap items-start justify-between gap-2">
      <div><h4 className="text-sm font-semibold">Cash-dividend preparer attestation</h4>
        <p className="mt-1 text-sm text-muted-foreground">Regular mandatory cash dividends in one declared USD book. Limits are USD 10,000 per related event and USD 100,000 per book calendar month; the server evaluates gross impact and all posting controls.</p></div>
      <Button type="button" variant="outline" size="sm" disabled={loading || busy} onClick={() => { setError(null); setReload(value => value + 1); }}>Refresh cash-dividend controls</Button>
    </div>
    <p className="text-sm">Required attestation disclosure: <span className="font-semibold">{PREPARER_ATTESTATION_DISCLOSURE}</span></p>
    {loading ? <p role="status" className="text-sm">Loading the native case, declaration and cash-dividend eligibility…</p> : null}
    {error ? <p role={error.command ? "alert" : "status"} className={`break-words text-sm ${error.command ? "text-danger" : "text-muted-foreground"}`}>{error.message}</p> : null}
    {notice ? <p role="status" className="text-sm">{notice}</p> : null}
    {pending && !busy ? <Button type="button" variant="outline" size="sm" onClick={() => void submit(pending)}>Retry same cash-dividend command</Button> : null}
    {workspace && !known ? <p role="status" className="text-sm">Cash-dividend authority is unavailable or its policy version is unsupported. No declaration, attestation or posting is offered.</p> : null}
    {known && workspace ? <>
      <div className="flex flex-wrap gap-2"><Badge variant={workspace.policyEnabled ? "warning" : "outline"}>{workspace.policyEnabled ? "Cash-dividend policy enabled" : "Cash-dividend policy disabled"}</Badge><Badge variant="outline">{workspace.case.state}</Badge></div>
      <dl className="grid gap-2 text-xs sm:grid-cols-2">
        {Object.entries({ Book: workspace.scope.ledgerBookId ?? "Unavailable", "Tenant / company": `${workspace.scope.tenantId} / ${workspace.scope.companyId}`,
          Principal: workspace.actor, "Case revision": workspace.case.version,
          Period: workspace.scope.periodId ?? preview?.projection.periodId ?? "Unavailable",
          "Accounting basis": workspace.scope.accountingBasis ?? preview?.projection.accountingBasis ?? "Unavailable",
          Declaration: declaration ? `${declaration.enabled ? "Enabled" : "Withdrawn"} / revision ${declaration.revision}` : "Not declared" }).map(([label, value]) =>
          <div key={label}><dt className="font-semibold">{label}</dt><dd className="break-all font-mono">{String(value)}</dd></div>)}
      </dl>
      {!workspace.policyEnabled ? <p className="text-sm">The host policy is disabled. New economic attestations and attested posting are unavailable.</p> : null}
      {workspace.blockers?.length ? <ul className="list-disc space-y-1 pl-4 text-sm" aria-label="Cash-dividend blockers">{workspace.blockers.map((blocker, index) => <li key={index}>{blocker}</li>)}</ul> : null}
      <details className="rounded-md border border-border/70 p-2">
        <summary className="cursor-pointer text-sm font-semibold">Book declaration and audit history</summary>
        <p className="mt-2 text-sm">This declaration administers the bounded policy. It grants no business permission or independent review.</p>
        <form className="mt-2 space-y-2" onSubmit={event => {
          event.preventDefault();
          if (locked || !workspace.canAdministerDeclaration || !declarationReason.trim()
              || !workspace.scope.ledgerBookId || !declaration?.enabled && (!canEnableDeclaration || !declareConfirmed || !bindingConfirmed)) return;
          void submit({ kind: "declaration", request: { commandId: crypto.randomUUID(), expectedRevision: declaration?.revision ?? 0,
            ledgerBookId: workspace.scope.ledgerBookId, enabled: !declaration?.enabled,
            policyVersion: workspace.policyVersion, policyHash: workspace.policyHash, reason: declarationReason.trim(),
            ...(!declaration?.enabled && accountRoleBinding ? { accountRoleBinding } : {}) } });
        }}>
          <label className="grid gap-1 text-sm">Cash-dividend declaration reason<Input required maxLength={4000} value={declarationReason}
            disabled={locked || !workspace.canAdministerDeclaration || !canEnableDeclaration && !declaration?.enabled}
            onChange={event => setDeclarationReason(event.target.value)} /></label>
          {!declaration?.enabled ? <label className="flex items-start gap-2 text-sm"><input className="mt-1 accent-primary" type="checkbox" checked={declareConfirmed}
            disabled={locked || !workspace.canAdministerDeclaration || !canEnableDeclaration}
            onChange={event => setDeclareConfirmed(event.target.checked)} /><span>I am the single principal for this USD book and have reviewed the stated per-event and monthly gross limits.</span></label> : null}
          {displayedBinding ? <dl className="grid gap-2 text-xs sm:grid-cols-2" aria-label="Exact cash-dividend accounting binding">
              {Object.entries({ "Dividend receivable": `${displayedBinding.dividendReceivableAccount} / ${displayedBinding.dividendReceivableAccountName}`,
                "Dividend income": `${displayedBinding.dividendIncomeAccount} / ${displayedBinding.dividendIncomeAccountName}`,
                "Promoted rule pack": `${displayedBinding.rulePackId} / ${displayedBinding.rulePackVersion}`,
                "Selected rule": `${displayedBinding.selectedRuleId} / ${displayedBinding.selectedRuleVersion}` }).map(([label, value]) =>
                <div key={label}><dt className="font-semibold">{label}</dt><dd className="break-all font-mono">{value}</dd></div>)}
            </dl> : <p className="text-sm">The exact existing-account and promoted-rule binding is unavailable.</p>}
          {!declaration?.enabled ? <>
            <label className="flex items-start gap-2 text-sm"><input className="mt-1 accent-primary" type="checkbox" checked={bindingConfirmed}
              disabled={locked || !workspace.canAdministerDeclaration || !canEnableDeclaration}
              onChange={event => setBindingConfirmed(event.target.checked)} /><span>I explicitly confirm the server-provided dividend receivable account, dividend income account and promoted accounting rule for this book.</span></label>
            {!workspace.canBindAccountingRoles ? <p className="text-sm">Permission to bind accounting roles is required to enable this declaration.</p> : null}
          </> : null}
          <Button type="submit" size="sm" variant="outline" disabled={locked || !workspace.canAdministerDeclaration || !declarationReason.trim()
            || !declaration?.enabled && (!canEnableDeclaration || !declareConfirmed || !bindingConfirmed)}>{declaration?.enabled ? "Withdraw cash-dividend declaration" : "Enable cash-dividend declaration for this book"}</Button>
        </form>
        <ul className="mt-2 space-y-2 text-sm" aria-label="Cash-dividend declaration history">{(workspace.declarationHistory ?? []).map(entry =>
          <li key={entry.revision}>Revision {entry.revision}: {entry.enabled ? "Enabled" : "Withdrawn"} by {entry.changedBy} at <time dateTime={entry.recordedAtUtc}>{formatUtcTimestamp(new Date(entry.recordedAtUtc))}</time>. {entry.reason}</li>)}</ul>
      </details>
      {preview ? <details className="rounded-md border border-border/70 p-2" open>
        <summary className="cursor-pointer text-sm font-semibold">Exact cash-dividend projection</summary>
        <dl className="mt-2 grid gap-2 text-xs sm:grid-cols-2">{Object.entries({ Projection: preview.projection.projectionId,
          "Projection preparer": preview.projection.preparedBy, "Current case gross impact": `USD ${String(preview.eligibility.grossImpactUsd)}`,
          "Whole related-event gross impact": `USD ${String(preview.eligibility.wholeEventGrossUsd)}`,
          "Book calendar month": preview.eligibility.budgetMonth, "Entitlement snapshot SHA-256": preview.eligibility.entitlementSnapshotHash,
          "Retained evidence": workspace.evidenceReference ?? "Unavailable", "Evidence SHA-256": workspace.evidenceHash ?? "Unavailable" }).map(([label, value]) =>
          <div key={label}><dt className="font-semibold">{label}</dt><dd className="break-all font-mono">{value}</dd></div>)}</dl>
        <p className="mt-2 text-sm text-muted-foreground">Gross limits and eligibility are server decisions. This preview does not submit an order or post a journal.</p>
      </details> : null}
      <form className="space-y-2" onSubmit={event => {
        event.preventDefault();
        if (!canAttest || !preview || !declaration || !attestReason.trim() || !attestConfirmed
            || !workspace.evidenceReference || !workspace.evidenceHash) return;
        void submit({ kind: "attest", request: { idempotencyKey: crypto.randomUUID(), expectedVersion: workspace.case.version,
          projectionId: preview.projection.projectionId, expectedDeclarationRevision: declaration.revision,
          expectedEligibilityHash: preview.eligibility.entitlementSnapshotHash, reason: attestReason.trim(),
          evidenceReference: workspace.evidenceReference, evidenceHash: workspace.evidenceHash, confirmation: workspace.confirmation } });
      }}>
        <label className="grid gap-1 text-sm">Cash-dividend attestation reason<Input required maxLength={4000} value={attestReason}
          disabled={!canAttest} onChange={event => setAttestReason(event.target.value)} /></label>
        <label className="flex items-start gap-2 text-sm"><input className="mt-1 accent-primary" type="checkbox" checked={attestConfirmed}
          disabled={!canAttest} onChange={event => setAttestConfirmed(event.target.checked)} /><span>{workspace.confirmation}</span></label>
        <Button type="submit" size="sm" disabled={!canAttest || !attestReason.trim() || !attestConfirmed}>Attest this cash-dividend projection</Button>
      </form>
      {attestation ? <div role="region" className="space-y-2 rounded-md border border-border/70 bg-background/50 p-3 text-sm" aria-label="Retained cash-dividend attestation">
        <p className="font-semibold">{attestation.decisionKind === "PreparerAttestation" && attestation.disclosure === PREPARER_ATTESTATION_DISCLOSURE
          ? PREPARER_ATTESTATION_DISCLOSURE : "Review classification unavailable"}</p>
        <p className="break-words">{attestation.attestedBy} at <time dateTime={attestation.attestedAtUtc}>{formatUtcTimestamp(new Date(attestation.attestedAtUtc))}</time>. {attestation.reason}</p>
        <p className="break-all font-mono text-xs">Attestation {attestation.attestationId} · projection {attestation.projectionId}</p>
        {attestation.voidedAtUtc ? <p>Voided at <time dateTime={attestation.voidedAtUtc}>{formatUtcTimestamp(new Date(attestation.voidedAtUtc))}</time>. The original decision remains retained.</p> : null}
      </div> : null}
      <form className="space-y-2" onSubmit={event => {
        event.preventDefault();
        if (!canPost || !attestation || !postReason.trim() || !postConfirmed) return;
        void submit({ kind: "post", request: { idempotencyKey: crypto.randomUUID(), expectedVersion: workspace.case.version,
          projectionId: attestation.projectionId, attestationId: attestation.attestationId, reason: postReason.trim() } });
      }}>
        <label className="grid gap-1 text-sm">Attested posting reason<Input required maxLength={4000} value={postReason}
          disabled={!canPost} onChange={event => setPostReason(event.target.value)} /></label>
        <label className="flex items-start gap-2 text-sm"><input className="mt-1 accent-primary" type="checkbox" checked={postConfirmed}
          disabled={!canPost} onChange={event => setPostConfirmed(event.target.checked)} /><span>Post this attested cash-dividend journal under the current book, period and posting controls.</span></label>
        <Button type="submit" size="sm" variant="outline" disabled={!canPost || !postReason.trim() || !postConfirmed}>Post attested cash-dividend journal</Button>
      </form>
      {workspace.posting ? <div role="region" className="space-y-1 text-sm" aria-label="Attested cash-dividend posting receipt">
        <p>Journal {workspace.posting.journalEntryId} · {workspace.posting.postingStatus} by {workspace.posting.postedBy} at <time dateTime={workspace.posting.postedAtUtc}>{formatUtcTimestamp(new Date(workspace.posting.postedAtUtc))}</time>.</p>
        <p>{PREPARER_ATTESTATION_DISCLOSURE}</p>
      </div> : null}
    </> : null}
  </section>;
}

function errorMessage(error: unknown) { return error instanceof Error ? error.message : "Cash-dividend attestation controls are unavailable."; }
function canonicalHash(value?: string | null) { return typeof value === "string" && /^[a-f0-9]{64}$/.test(value); }
function recoveryKey(caseId: string, workspace: CashDividendAttestationWorkspace) {
  return `meridian.cash-dividend.pending.v1:${[workspace.actor, workspace.scope.tenantId, workspace.scope.companyId,
    workspace.scope.ledgerBookId ?? "", caseId].map(value => encodeURIComponent(value.trim().toLowerCase())).join(":")}`;
}
function clearRecovery(caseId: string, workspace: CashDividendAttestationWorkspace) {
  try { sessionStorage.removeItem(recoveryKey(caseId, workspace)); } catch { /* A failed cleanup cannot change a retained server result. */ }
}
function readRecovery(caseId: string, workspace: CashDividendAttestationWorkspace): PendingCommand | null {
  try {
    const text = sessionStorage.getItem(recoveryKey(caseId, workspace));
    if (!text) return null;
    const value = JSON.parse(text) as { actor?: string; caseId?: string; scope?: CorporateActionCaseScope; command?: PendingCommand };
    if (!sameCashDividendActor(value.actor, workspace.actor) || value.caseId !== caseId
        || !sameCashDividendScope(value.scope, workspace.scope) || !value.command) return null;
    const command = value.command;
    // Reconstruct only command operands; storage never supplies actor, tenant or authority.
    if (command.kind === "declaration") {
      const r = command.request;
      if (!r.commandId || !Number.isInteger(r.expectedRevision) || r.ledgerBookId !== workspace.scope.ledgerBookId
          || typeof r.enabled !== "boolean" || r.policyVersion !== CASH_DIVIDEND_ATTESTATION_POLICY
          || !canonicalHash(r.policyHash) || !validReason(r.reason)) return null;
      const accountRoleBinding = bindingOperands(r.accountRoleBinding, workspace.scope.ledgerBookId);
      if (r.enabled && !accountRoleBinding) return null;
      return { kind: "declaration", request: { commandId: r.commandId, expectedRevision: r.expectedRevision,
        ledgerBookId: r.ledgerBookId, enabled: r.enabled, policyVersion: r.policyVersion, policyHash: r.policyHash, reason: r.reason,
        ...(accountRoleBinding ? { accountRoleBinding } : {}) } };
    }
    if (command.kind === "attest") {
      const r = command.request;
      if (!r.idempotencyKey || !Number.isInteger(r.expectedVersion) || !r.projectionId
          || !Number.isInteger(r.expectedDeclarationRevision) || !canonicalHash(r.expectedEligibilityHash)
          || !validReason(r.reason) || !r.evidenceReference || !canonicalHash(r.evidenceHash)
          || r.confirmation !== CASH_DIVIDEND_ATTESTATION_CONFIRMATION) return null;
      return { kind: "attest", request: { idempotencyKey: r.idempotencyKey, expectedVersion: r.expectedVersion,
        projectionId: r.projectionId, expectedDeclarationRevision: r.expectedDeclarationRevision,
        expectedEligibilityHash: r.expectedEligibilityHash, reason: r.reason,
        evidenceReference: r.evidenceReference, evidenceHash: r.evidenceHash, confirmation: r.confirmation } };
    }
    if (command.kind === "post") {
      const r = command.request;
      if (!r.idempotencyKey || !Number.isInteger(r.expectedVersion) || !r.projectionId || !r.attestationId || !validReason(r.reason)) return null;
      return { kind: "post", request: { idempotencyKey: r.idempotencyKey, expectedVersion: r.expectedVersion,
        projectionId: r.projectionId, attestationId: r.attestationId, reason: r.reason } };
    }
    return null;
  } catch { return null; }
}
function validReason(value: unknown): value is string {
  return typeof value === "string" && Boolean(value.trim()) && value.length <= 4000;
}

function bindingOperands(value: CashDividendAccountRoleBinding | null | undefined, bookId?: string | null): CashDividendAccountRoleBinding | null {
  if (!value || value.schemaVersion !== 1 || value.ledgerBookId !== bookId
      || ![value.dividendReceivableAccount, value.dividendIncomeAccount, value.dividendReceivableAccountName,
        value.dividendIncomeAccountName, value.rulePackId, value.rulePackVersion, value.selectedRuleId, value.selectedRuleVersion]
        .every(item => typeof item === "string" && item.trim())
      || value.dividendReceivableAccount === value.dividendIncomeAccount) return null;
  return { schemaVersion: value.schemaVersion, ledgerBookId: value.ledgerBookId,
    dividendReceivableAccount: value.dividendReceivableAccount, dividendIncomeAccount: value.dividendIncomeAccount,
    dividendReceivableAccountName: value.dividendReceivableAccountName, dividendIncomeAccountName: value.dividendIncomeAccountName,
    rulePackId: value.rulePackId, rulePackVersion: value.rulePackVersion,
    selectedRuleId: value.selectedRuleId, selectedRuleVersion: value.selectedRuleVersion };
}
