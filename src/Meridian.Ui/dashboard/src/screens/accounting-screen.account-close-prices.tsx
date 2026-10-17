import { useEffect, useRef, useState, type FormEvent } from "react";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { approveClosePriceSet, createClosePriceSet, getClosePriceSet, listClosePriceSets } from "@/lib/api/account-oversight.api";
import { describeApiError, isApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import type { ClosePriceLine, ClosePriceSet, CreateClosePriceSetRequest, OversightContext, OversightScope, PriceUnit } from "@/types/account-oversight";

interface Props {
  scope: OversightScope;
  context: OversightContext;
  onDirtyChange?: (dirty: boolean) => void;
}

interface SecurityDraft {
  key: string;
  securityId: string;
  overridePrice: string;
  unit: PriceUnit;
  rationale: string;
  evidenceReference: string;
}

const securityIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const decimalPattern = /^(?:0|[1-9]\d*)(?:\.\d+)?$/;
function newSecurity(key: string, line?: ClosePriceLine): SecurityDraft {
  return {
    key, securityId: line?.securityId ?? "", overridePrice: line?.override?.price ?? "",
    unit: line?.override?.unit ?? line?.unit ?? "CurrencyPerUnit",
    rationale: line?.override?.rationale ?? "", evidenceReference: line?.override?.evidenceReference ?? ""
  };
}

function utcInput(value: string): string | null {
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2})?$/.test(value)) return null;
  const parsed = new Date(`${value}Z`);
  return Number.isNaN(parsed.getTime()) ? null : parsed.toISOString();
}

function belongsTo(set: ClosePriceSet, scope: OversightScope, date: string): boolean {
  return set.snapshot.accountId === scope.accountId && set.snapshot.bookId === scope.ledgerBookId
    && set.snapshot.valuationDate === date;
}

function unitLabel(unit: PriceUnit): string {
  return unit === "CurrencyPerUnit" ? "Currency per unit" : unit === "PercentOfPar" ? "Percent of par" : "Unspecified";
}

/** Scope remounts drop the old account's requests and retained approval authority. */
export function AccountClosePricesPanel(props: Props) {
  return <ScopedClosePricesPanel key={`${props.scope.ledgerBookId}:${props.scope.accountId}:${props.context.actor}:${props.context.canReadPrices}:${props.context.canWrite}:${props.context.canManagePrices}`} {...props} />;
}

function ScopedClosePricesPanel({ scope, context, onDirtyChange }: Props) {
  const { accountId, ledgerBookId, baseCurrency } = scope;
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [closeCutoff, setCloseCutoff] = useState("");
  const [knowledgeCutoff, setKnowledgeCutoff] = useState("");
  const [dispersion, setDispersion] = useState("5");
  const [securities, setSecurities] = useState<SecurityDraft[]>(() => [newSecurity("security-1")]);
  const nextKey = useRef(2);
  const [predecessorId, setPredecessorId] = useState<string | null>(null);
  const [correctionRationale, setCorrectionRationale] = useState("");
  const [reviewRationale, setReviewRationale] = useState("");
  const [sets, setSets] = useState<ClosePriceSet[]>([]);
  const [selected, setSelected] = useState<ClosePriceSet | null>(null);
  const [busy, setBusy] = useState<"history" | "selection" | "prepare" | "approve" | null>(null);
  const [historyReady, setHistoryReady] = useState(false);
  const [uncertainWrite, setUncertainWrite] = useState(false);
  const [uncertainCapture, setUncertainCapture] = useState(false);
  const [captureRecoveryReady, setCaptureRecoveryReady] = useState(false);
  const [captureFoundInHistory, setCaptureFoundInHistory] = useState(false);
  const capture = useRef<{ fingerprint: string; request: CreateClosePriceSetRequest } | null>(null);
  /** The approval whose server outcome is unknown; it is verified only on its own valuation date. */
  const pendingApproval = useRef<{ valuationDate: string; snapshotId: string } | null>(null);
  const [error, setError] = useState<ApiErrorDisplay | null>(null);
  const [notice, setNotice] = useState("");
  const [draftDirty, setDraftDirty] = useState(false);
  const dirty = draftDirty || reviewRationale.length > 0;
  const active = useRef<AbortController | null>(null);
  const contextMatches = context.accountId === scope.accountId && context.bookId === scope.ledgerBookId;
  const canRead = contextMatches && context.canReadPrices;
  const canManage = canRead && context.canWrite && context.canManagePrices && Boolean(context.actor.trim());
  const latest = sets[0] ?? null;
  const visible = selected && belongsTo(selected, scope, date) ? selected : null;

  useEffect(() => { onDirtyChange?.(dirty); }, [dirty, onDirtyChange]);
  useEffect(() => () => { active.current?.abort(); }, []);

  useEffect(() => {
    const requestScope = { accountId, ledgerBookId, baseCurrency };
    const controller = new AbortController();
    active.current?.abort();
    active.current = controller;
    setSets([]);
    setSelected(null);
    setHistoryReady(false);
    setBusy(canRead && date ? "history" : null);
    setError(null);
    if (canRead && date) {
      void listClosePriceSets(requestScope, date, { signal: controller.signal }).then((result) => {
        if (controller.signal.aborted) return;
        if (result.some((set) => !belongsTo(set, requestScope, date))) throw new Error("The returned price history does not match the selected account, book and date.");
        const sorted = [...result].sort((a, b) => b.snapshot.version - a.snapshot.version);
        setSets(sorted);
        setSelected(sorted[0] ?? null);
        setHistoryReady(true);
      }).catch((failure: unknown) => {
        if (!controller.signal.aborted) setError(describeApiError(failure, "Close-price history is unavailable. Reload before preparing or approving."));
      }).finally(() => {
        if (!controller.signal.aborted) { active.current = null; setBusy(null); }
      });
    }
    return () => controller.abort();
  }, [canRead, date, accountId, ledgerBookId, baseCurrency]);

  function edit() { setDraftDirty(true); setNotice(""); }
  function updateSecurity(key: string, field: keyof Omit<SecurityDraft, "key">, value: string) {
    setSecurities((rows) => rows.map((row) => row.key === key ? { ...row, [field]: value } : row));
    edit();
  }
  function changeDate(value: string) {
    if (busy === "prepare" || busy === "approve") {
      setUncertainWrite(true);
      setUncertainCapture(busy === "prepare");
    }
    active.current?.abort();
    active.current = null;
    setBusy(null);
    setDate(value);
    setSelected(null);
    setSets([]);
    setHistoryReady(false);
    if (!uncertainCapture && busy !== "prepare") {
      setPredecessorId(null);
      setCorrectionRationale("");
    }
    setReviewRationale("");
    setNotice("");
    setCaptureRecoveryReady(false);
    setCaptureFoundInHistory(false);
  }

  async function reload() {
    if (!canRead || !date || active.current) return;
    const controller = new AbortController();
    active.current = controller;
    setBusy("history"); setHistoryReady(false); setError(null); setSelected(null);
    try {
      const result = await listClosePriceSets(scope, date, { signal: controller.signal });
      if (controller.signal.aborted) return;
      if (result.some((set) => !belongsTo(set, scope, date))) throw new Error("The returned price history does not match the selected account, book and date.");
      const sorted = [...result].sort((a, b) => b.snapshot.version - a.snapshot.version);
      setSets(sorted); setSelected(sorted[0] ?? null); setHistoryReady(true);
      if (uncertainWrite && !uncertainCapture && pendingApproval.current) {
        const approval = pendingApproval.current;
        if (approval.valuationDate !== date) {
          setNotice(`Return to valuation date ${approval.valuationDate} and reload its history to verify the pending approval before approving or preparing again.`);
        } else {
          const found = sorted.find((set) => set.snapshot.snapshotId === approval.snapshotId);
          pendingApproval.current = null;
          setUncertainWrite(false);
          if (found) setSelected(found);
          setNotice(found?.approval
            ? "The pending approval is retained against the immutable snapshot digest."
            : "The pending approval was not retained. Review the current version before submitting the approval again.");
        }
      } else if (uncertainWrite && uncertainCapture && capture.current) {
        const original = capture.current.request;
        const found = sorted.find((set) => set.snapshot.snapshotId === original.requestId);
        const sameDate = original.valuationDate === date;
        setCaptureRecoveryReady(sameDate);
        setCaptureFoundInHistory(Boolean(found));
        if (found) setSelected(found);
        setNotice(!sameDate
          ? `Return to valuation date ${original.valuationDate} and reload its history to recover the pending capture.`
          : found ? "The original capture identity is retained. Verify the same command to confirm its immutable evidence before proceeding."
            : "The original capture is not visible in this history. Retry only its unchanged command with the same request identity; new captures remain locked.");
      } else {
        setUncertainWrite(false);
        setNotice("Retained history reloaded. Review its versions and approvals before submitting another action.");
      }
    } catch (failure) {
      if (!controller.signal.aborted) setError(describeApiError(failure, "Close-price history could not be reloaded."));
    } finally {
      if (!controller.signal.aborted) { active.current = null; setBusy(null); }
    }
  }

  async function selectSet(set: ClosePriceSet) {
    if (active.current || !canRead) return;
    const controller = new AbortController();
    active.current = controller;
    setBusy("selection"); setSelected(null); setReviewRationale(""); setError(null);
    try {
      const result = await getClosePriceSet(scope, date, set.snapshot.snapshotId, { signal: controller.signal });
      if (controller.signal.aborted) return;
      if (!belongsTo(result, scope, date) || result.snapshot.snapshotId !== set.snapshot.snapshotId) throw new Error("The returned snapshot does not match the selected retained version.");
      setSelected(result);
      setSets((current) => current.map((item) => item.snapshot.snapshotId === result.snapshot.snapshotId ? result : item));
    } catch (failure) {
      if (!controller.signal.aborted) setError(describeApiError(failure, "This retained price version is unavailable. Reload history."));
    } finally {
      if (!controller.signal.aborted) { active.current = null; setBusy(null); }
    }
  }

  function beginCorrection() {
    if (!latest || !canManage || busy || uncertainWrite || !historyReady) return;
    setPredecessorId(latest.snapshot.snapshotId);
    setSecurities(latest.snapshot.lines.map((line) => newSecurity(`security-${nextKey.current++}`, line)));
    setCloseCutoff(latest.snapshot.evaluatedAsOf.slice(0, 19));
    setKnowledgeCutoff(latest.snapshot.knowledgeAsOf.slice(0, 19));
    setDispersion(latest.snapshot.maxSourceDispersionPercent);
    setCorrectionRationale("");
    edit();
  }

  const evaluatedAsOf = utcInput(closeCutoff);
  const knowledgeAsOf = utcInput(knowledgeCutoff);
  const securityIds = securities.map((row) => row.securityId.trim().toLowerCase());
  const coverageMatches = !predecessorId || Boolean(latest && predecessorId === latest.snapshot.snapshotId
    && latest.snapshot.lines.length === securityIds.length && latest.snapshot.lines.every((line) => securityIds.includes(line.securityId.toLowerCase())));
  const draftError = !date ? "Select a valuation date."
    : !evaluatedAsOf || !knowledgeAsOf ? "Enter explicit close and knowledge cutoffs in UTC."
      : evaluatedAsOf.slice(0, 10) !== date ? "The close cutoff must fall on the selected UTC valuation date."
        : !decimalPattern.test(dispersion) ? "Source dispersion must be nonnegative decimal text."
          : securityIds.some((id) => !securityIdPattern.test(id) || /^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(id)) ? "Enter a complete, nonzero security ID for every row."
            : new Set(securityIds).size !== securityIds.length ? "The security population must contain distinct IDs."
              : securities.some((row) => row.overridePrice && (!decimalPattern.test(row.overridePrice) || !/[1-9]/.test(row.overridePrice) || row.unit === "Unspecified" || !row.rationale.trim() || !row.evidenceReference.trim())) ? "Each override needs a positive decimal price, explicit unit, rationale and evidence reference."
                : latest && !predecessorId ? "Start a linked correction from the latest retained version."
                  : !coverageMatches ? "Reload history and start a correction from its latest version with the same security coverage."
                    : predecessorId && !correctionRationale.trim() ? "Explain the correction before preparing its linked version." : null;
  const canRecoverCapture = uncertainWrite && uncertainCapture && captureRecoveryReady && capture.current?.request.valuationDate === date;
  const prepareReason = !canManage ? "Preparing a close input requires an authenticated session with ledger and pricing management permission."
    : busy ? "Wait for the current request to finish." : uncertainWrite ? canRecoverCapture ? null : "The last write outcome is unknown. Reload the original valuation date's history before retrying the same capture."
      : !historyReady ? "Reload retained history before preparing a version." : draftError;
  const approvalReason = !canManage ? "Approval requires an authenticated session with ledger and pricing management permission."
    : busy ? "Wait for the current request to finish." : uncertainWrite ? "The last write outcome is unknown. Reload retained history before approving."
      : !historyReady || !visible ? "Select a retained version from the current account, book and date."
        : visible.approval ? "This version already has an immutable approval."
          : visible.snapshot.snapshotId !== latest?.snapshot.snapshotId ? "A superseded version cannot be approved. Select the latest retained version."
            : visible.snapshot.createdBy.toLowerCase() === context.actor.trim().toLowerCase() ? "The creator cannot approve their own price set. An independent reviewer is required."
              : visible.snapshot.blockers.length > 0 ? "Resolve the retained source blockers through a linked correction before approval."
                : !reviewRationale.trim() ? "Enter the independent review rationale." : null;

  async function mutate(action: "prepare" | "approve") {
    if (active.current || (action === "prepare" ? prepareReason : approvalReason)) return;
    if (action === "approve") {
      capture.current = null;
      pendingApproval.current = { valuationDate: date, snapshotId: visible!.snapshot.snapshotId };
      setCaptureRecoveryReady(false); setCaptureFoundInHistory(false); setUncertainCapture(false);
    }
    if (action === "prepare" && !canRecoverCapture) {
      const request: CreateClosePriceSetRequest = {
        accountId: scope.accountId, bookId: scope.ledgerBookId, valuationDate: date,
        evaluatedAsOf: evaluatedAsOf!, knowledgeAsOf: knowledgeAsOf!, maxSourceDispersionPercent: dispersion,
        createdBy: context.actor, correctsSnapshotId: predecessorId,
        correctionRationale: predecessorId ? correctionRationale.trim() : null,
        securities: securities.map((row) => ({ securityId: row.securityId.trim(), override: row.overridePrice ? {
          price: row.overridePrice, unit: row.unit, rationale: row.rationale.trim(), evidenceReference: row.evidenceReference.trim()
        } : null }))
      };
      const fingerprint = JSON.stringify(request);
      if (capture.current?.fingerprint !== fingerprint) {
        capture.current = { fingerprint, request: { ...request, requestId: crypto.randomUUID() } };
      }
    }
    const controller = new AbortController();
    active.current = controller;
    setBusy(action); setError(null); setNotice("");
    try {
      const result = action === "prepare"
        ? await createClosePriceSet(scope, capture.current!.request, { signal: controller.signal })
        : await approveClosePriceSet(scope, {
          accountId: scope.accountId, bookId: scope.ledgerBookId, valuationDate: date,
          snapshotId: visible!.snapshot.snapshotId, reviewer: context.actor, rationale: reviewRationale.trim()
        }, { signal: controller.signal });
      if (controller.signal.aborted) return;
      if (!belongsTo(result, scope, date)) throw new Error("The write response does not match the selected account, book and date. Reload retained history to inspect the outcome.");
      if (action === "prepare" && result.snapshot.snapshotId !== capture.current?.request.requestId) throw new Error("The returned snapshot does not match the submitted capture identity. Reload history before verifying the original command.");
      setSets((current) => [result, ...current.filter((item) => item.snapshot.snapshotId !== result.snapshot.snapshotId)].sort((a, b) => b.snapshot.version - a.snapshot.version));
      setSelected(result); setReviewRationale("");
      if (action === "approve") pendingApproval.current = null;
      if (action === "prepare") {
        capture.current = null;
        setPredecessorId(null); setDraftDirty(false); setUncertainWrite(false); setUncertainCapture(false);
        setCaptureRecoveryReady(false); setCaptureFoundInHistory(false);
      }
      setNotice(action === "prepare" ? "Immutable price input retained. Inspect its evidence and blockers before independent review." : "Independent approval retained against the immutable snapshot digest.");
    } catch (failure) {
      if (controller.signal.aborted) return;
      setError(describeApiError(failure, "The close-price action could not be confirmed. Your draft is preserved."));
      if (!isApiError(failure) || failure.status >= 500) {
        setUncertainWrite(true); setUncertainCapture(action === "prepare"); setCaptureRecoveryReady(false);
      } else if (action === "approve") {
        pendingApproval.current = null;
      }
    } finally {
      if (!controller.signal.aborted) { active.current = null; setBusy(null); }
    }
  }

  const historyColumns: DenseDataTableColumn<ClosePriceSet>[] = [
    { id: "version", label: "Version", render: ({ snapshot }) => `v${snapshot.version}` },
    { id: "created", label: "Retained by / UTC", render: ({ snapshot }) => <><span>{snapshot.createdBy}</span><br /><time>{snapshot.createdAt}</time></> },
    { id: "review", label: "Review", render: (set) => set.approval ? `Approved · ${set.approval.reviewer}` : set.snapshot.blockers.length ? `${set.snapshot.blockers.length} blocker(s)` : "Awaiting independent review" },
    { id: "lineage", label: "Correction", render: ({ snapshot }) => snapshot.correctsSnapshotId ? snapshot.correctionRationale : "Original version" }
  ];

  function submit(event: FormEvent) { event.preventDefault(); void mutate("prepare"); }

  return (
    <Card className="panel-surface min-w-0" aria-labelledby="account-close-prices-title">
      <CardHeader>
        <CardTitle id="account-close-prices-title">Approved close prices</CardTitle>
        <CardDescription>Prepare an immutable account, book and date price input from the retained pricing hierarchy. Independent approval binds its exact evidence; corrections append a linked version.</CardDescription>
      </CardHeader>
      <CardContent className="flex min-w-0 flex-col gap-4">
        {!canRead ? <StatusBanner tone="warning" title={<span className="text-foreground">Close-price access unavailable</span>} detail="Select a verified account-owned book with pricing read permission." /> : <>
          <FormGrid columns={3}>
            <FormRow label="Valuation date (UTC)" labelFor="close-price-date"><Input id="close-price-date" type="date" value={date} onChange={(event) => changeDate(event.target.value)} /></FormRow>
          </FormGrid>
          <div className="flex flex-wrap items-center gap-3">
            <Button type="button" variant="outline" disabled={busy !== null || !date} onClick={() => void reload()}>{busy === "history" ? "Loading history…" : "Reload price history"}</Button>
            {!canManage ? <p className="text-xs text-muted-foreground">Read only · preparing and approving require ledger and pricing management permission.</p> : null}
          </div>
          {error ? <StatusBanner role="alert" tone="danger" title={<span className="text-foreground">{error.summary}</span>} detail={error.details.join(" ")} /> : null}
          {uncertainWrite ? <StatusBanner tone="warning" title={<span className="text-foreground">Write outcome needs verification</span>} detail={uncertainCapture && capture.current ? `A capture may have reached the server for valuation date ${capture.current.request.valuationDate}. Reload that date's history, then verify or retry the unchanged command with its retained request identity ${capture.current.request.requestId}. New captures and approval remain locked until the server confirms the original command.` : pendingApproval.current ? `An approval may have reached the server for valuation date ${pendingApproval.current.valuationDate}. Reload that date's history to verify the retained approval before approving or preparing again.` : "A request may have reached the server. Reload retained history and inspect its versions and approvals before retrying."} /> : null}
          <p role="status" aria-live="polite" className="text-xs text-muted-foreground">{notice || (busy ? "Reading or retaining authoritative close-price evidence…" : historyReady && sets.length === 0 ? "No close-price input is retained for this account, book and date." : "Select a version to inspect its retained source evidence.")}</p>
          <DenseDataTable columns={historyColumns} rows={sets} getRowId={(set) => set.snapshot.snapshotId} getRowSelectAriaLabel={(set) => `Inspect close-price version ${set.snapshot.version}`} selectedRowId={visible?.snapshot.snapshotId} onRowSelect={(set) => void selectSet(set)} ariaLabel="Retained close-price versions" emptyText={busy === "history" ? "Loading retained history…" : historyReady ? "No retained versions for this date." : "History is unavailable. Reload to continue."} />
          {visible ? <RetainedClosePriceDetails set={visible} /> : null}
          {visible ? <form onSubmit={(event) => { event.preventDefault(); void mutate("approve"); }} className="flex flex-col gap-3" aria-label="Independent close-price review">
            <FormRow label="Independent review rationale" labelFor="close-price-review"><Input id="close-price-review" value={reviewRationale} disabled={!canManage || busy !== null || Boolean(visible.approval)} onChange={(event) => { setReviewRationale(event.target.value); setNotice(""); }} /></FormRow>
            <p className="text-xs text-muted-foreground">Reviewer: {context.actor || "Authenticated session required"}. The server verifies the session identity and current version.</p>
            <div><Button type="submit" disabled={Boolean(approvalReason)} aria-describedby="close-price-approval-reason">{busy === "approve" ? "Retaining approval…" : "Approve immutable price set"}</Button></div>
            <p id="close-price-approval-reason" className="text-xs text-muted-foreground">{approvalReason || "Approve the selected retained price set with the rationale above."}</p>
          </form> : null}
          <TechnicalDetails label="Prepare a price set or linked correction" open={sets.length === 0 || uncertainWrite}>
            {latest ? <div className="mb-3 flex flex-col gap-2"><Button type="button" variant="outline" disabled={!canManage || busy !== null || uncertainWrite || !historyReady} onClick={beginCorrection}>Start correction from latest version {latest.snapshot.version}</Button><p className="text-xs text-muted-foreground">Corrections retain the latest predecessor and exact security population. Approved inputs stay immutable.</p></div> : null}
            <form onSubmit={submit} className="flex min-w-0 flex-col gap-4" aria-label="Prepare close-price input">
              <fieldset disabled={!canManage || busy !== null || uncertainWrite} className="flex min-w-0 flex-col gap-4">
                <legend className="mb-3 text-sm font-semibold">Price evidence cutoffs</legend>
                <FormGrid columns={3}>
                  <FormRow label="Close cutoff (UTC)" labelFor="close-price-cutoff"><Input id="close-price-cutoff" type="datetime-local" step="1" value={closeCutoff} onChange={(event) => { setCloseCutoff(event.target.value); edit(); }} required /></FormRow>
                  <FormRow label="Knowledge cutoff (UTC)" labelFor="close-price-knowledge"><Input id="close-price-knowledge" type="datetime-local" step="1" value={knowledgeCutoff} onChange={(event) => { setKnowledgeCutoff(event.target.value); edit(); }} required /></FormRow>
                  <FormRow label="Maximum source dispersion (%)" labelFor="close-price-dispersion"><Input id="close-price-dispersion" inputMode="decimal" value={dispersion} onChange={(event) => { setDispersion(event.target.value); edit(); }} required /></FormRow>
                </FormGrid>
                <p className="text-xs text-muted-foreground">Both cutoffs use UTC and must be at or before capture. Quote units remain explicit; a manual override cannot remove stale, unsupported or dispersion blockers.</p>
                {predecessorId ? <><p className="break-all text-xs">Corrects retained snapshot: <span className="font-mono">{predecessorId}</span></p><FormRow label="Correction rationale" labelFor="close-price-correction"><Input id="close-price-correction" value={correctionRationale} onChange={(event) => { setCorrectionRationale(event.target.value); edit(); }} required /></FormRow></> : null}
                {securities.map((row, index) => <fieldset key={row.key} className="flex min-w-0 flex-col gap-3 rounded border border-border p-3"><legend className="px-1 text-xs font-semibold">Security {index + 1}</legend>
                  <FormRow label={`Security ID ${index + 1}`} labelFor={`${row.key}-id`}><Input id={`${row.key}-id`} value={row.securityId} readOnly={Boolean(predecessorId)} onChange={(event) => updateSecurity(row.key, "securityId", event.target.value)} required autoComplete="off" /></FormRow>
                  <TechnicalDetails label={`Optional override for security ${index + 1}`}>
                    <FormGrid columns={2}>
                      <FormRow label={`Override price ${index + 1}`} labelFor={`${row.key}-price`} hint="Leave empty to retain the hierarchy selection."><Input id={`${row.key}-price`} inputMode="decimal" value={row.overridePrice} onChange={(event) => updateSecurity(row.key, "overridePrice", event.target.value)} /></FormRow>
                      <FormRow label={`Override unit ${index + 1}`} labelFor={`${row.key}-unit`}><Select id={`${row.key}-unit`} value={row.unit} onChange={(event) => updateSecurity(row.key, "unit", event.target.value)}><option value="Unspecified" disabled>Choose a supported quote unit</option><option value="CurrencyPerUnit">Currency per unit</option><option value="PercentOfPar">Percent of par</option></Select></FormRow>
                      <FormRow label={`Override rationale ${index + 1}`} labelFor={`${row.key}-rationale`}><Input id={`${row.key}-rationale`} value={row.rationale} onChange={(event) => updateSecurity(row.key, "rationale", event.target.value)} /></FormRow>
                      <FormRow label={`Override evidence reference ${index + 1}`} labelFor={`${row.key}-evidence`}><Input id={`${row.key}-evidence`} value={row.evidenceReference} onChange={(event) => updateSecurity(row.key, "evidenceReference", event.target.value)} /></FormRow>
                    </FormGrid>
                  </TechnicalDetails>
                  {!predecessorId ? <div><Button type="button" size="sm" variant="outline" disabled={securities.length === 1} onClick={() => { setSecurities((rows) => rows.filter((item) => item.key !== row.key)); edit(); }}>Remove security {index + 1}</Button></div> : null}
                </fieldset>)}
                {!predecessorId ? <div><Button type="button" variant="outline" onClick={() => { setSecurities((rows) => [...rows, newSecurity(`security-${nextKey.current++}`)]); edit(); }}>Add security</Button></div> : null}
              </fieldset>
              <div><Button type="submit" disabled={Boolean(prepareReason)} aria-describedby="close-price-prepare-reason">{busy === "prepare" ? "Retaining price input…" : canRecoverCapture ? captureFoundInHistory ? "Verify retained price capture" : "Retry same price capture" : predecessorId ? "Prepare linked correction" : "Prepare immutable price set"}</Button></div>
              <p id="close-price-prepare-reason" className="text-xs text-muted-foreground">{prepareReason || "Prepare this exact account/book/date input for independent review."}</p>
            </form>
          </TechnicalDetails>
        </>}
      </CardContent>
    </Card>
  );
}

function RetainedClosePriceDetails({ set }: { set: ClosePriceSet }) {
  const { snapshot, approval } = set;
  const columns: DenseDataTableColumn<ClosePriceLine>[] = [
    { id: "security", label: "Security ID", render: (line) => <span className="break-all font-mono">{line.securityId}</span> },
    { id: "price", label: "Retained close price", align: "right", render: (line) => <span className="font-mono">{line.closePrice ?? "Unsupported"}</span> },
    { id: "unit", label: "Quote unit", render: (line) => unitLabel(line.unit) },
    { id: "source", label: "Selected source / observation", render: (line) => line.selection ? <>{line.selection.selectedSource}<br /><span className="font-mono">{line.selection.goldenCopyPrice}</span> · {unitLabel(line.selection.unit)}<br /><time>{line.selection.priceAsOf}</time>{line.selection.isStaleFallback ? <p>Stale fallback · {line.selection.daysStale ?? "Unknown"} days</p> : null}</> : "No supported selection" },
    { id: "override", label: "Override", render: (line) => line.override ? `${line.override.price} · ${unitLabel(line.override.unit)} · ${line.override.rationale}` : "Hierarchy selection" }
  ];
  return <section className="flex min-w-0 flex-col gap-3" aria-label={`Close-price version ${snapshot.version} evidence`}>
    <p className="text-sm font-semibold">Retained version {snapshot.version} · {snapshot.valuationDate}</p>
    <p className="text-xs text-muted-foreground">Close cutoff {snapshot.evaluatedAsOf} · Knowledge cutoff {snapshot.knowledgeAsOf} · Maximum dispersion {snapshot.maxSourceDispersionPercent}%</p>
    <p className="text-xs text-muted-foreground">Created by {snapshot.createdBy} · {snapshot.createdAt}</p>
    {snapshot.correctsSnapshotId ? <p className="break-all text-xs">Corrects {snapshot.correctsSnapshotId}: {snapshot.correctionRationale}</p> : null}
    {approval ? <StatusBanner tone="success" title={<span className="text-foreground">Approved by {approval.reviewer}</span>} detail={<><p>{approval.rationale}</p><p>{approval.approvedAt}</p><p className="break-all">Snapshot SHA-256: {approval.snapshotSha256}</p></>} /> : <StatusBanner tone={snapshot.blockers.length ? "warning" : "info"} title={<span className="text-foreground">{snapshot.blockers.length ? "Approval blocked by retained source evidence" : "Awaiting independent review"}</span>} />}
    {snapshot.blockers.length ? <ul className="list-disc pl-5 text-sm" aria-label="Close-price approval blockers">{snapshot.blockers.map((blocker, index) => <li key={`${blocker.securityId}:${blocker.code}:${index}`}><span className="break-all font-mono">{blocker.securityId}</span> · {blocker.code}: {blocker.reason}</li>)}</ul> : null}
    <DenseDataTable columns={columns} rows={snapshot.lines} getRowId={(line) => line.securityId} ariaLabel="Retained selected close prices" emptyText="No retained price lines." />
    <TechnicalDetails label="Source observations and pricing hierarchy">
      <div className="flex flex-col gap-4">{snapshot.lines.map((line) => <section key={line.securityId} className="flex flex-col gap-2" aria-label={`Sources for ${line.securityId}`}>
        <h4 className="break-all font-mono text-xs font-semibold">{line.securityId}</h4>
        {line.observations.length ? <ul className="list-disc pl-5 text-xs">{line.observations.map((observation, index) => <li key={`${observation.sourceId}:${index}`}>{observation.sourceId} · <span className="font-mono">{observation.price}</span> · {unitLabel(observation.unit)} · <time>{observation.priceAsOf}</time></li>)}</ul> : <p className="text-xs">No supported retained source observations.</p>}
        {line.override ? <p className="break-all text-xs">Override evidence: {line.override.evidenceReference}. Original selected source observations remain above.</p> : null}
        {line.selection?.comparisonPrices.map((comparison) => <p className="text-xs" key={comparison.sourceId}>Comparison {comparison.sourceId}: deviation {comparison.pctDiffFromGoldenCopy === null ? "unavailable" : `${comparison.pctDiffFromGoldenCopy}%`} from selected source.</p>)}
        {line.selection?.hierarchySnapshot ? <><p className="text-xs">Hierarchy as of {line.selection.hierarchySnapshot.asOf} · maintained by {line.selection.hierarchySnapshot.updatedBy}</p><ol className="list-decimal pl-5 text-xs">{line.selection.hierarchySnapshot.entries.map((entry) => <li key={`${entry.priority}:${entry.sourceId}`}>{entry.sourceDisplayName} ({entry.sourceId}) · priority {entry.priority} · maximum {entry.maxDaysStale} days stale</li>)}</ol></> : null}
        <p className="break-all text-xs">Selection receipt: {line.selection?.selectionReceiptId ?? "Unavailable"}</p>
      </section>)}</div>
      <p className="mt-3 break-all text-xs">Snapshot identity: {snapshot.snapshotId} · Account: {snapshot.accountId} · Book: {snapshot.bookId}</p>
      {snapshot.creationCommandDigest ? <p className="mt-2 break-all text-xs">Creation command SHA-256: {snapshot.creationCommandDigest}</p> : null}
    </TechnicalDetails>
  </section>;
}
