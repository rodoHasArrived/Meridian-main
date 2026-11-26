import { useEffect, useState, type FormEvent } from "react";
import { DenseDataTable } from "@/components/meridian/ui-kit-primitives";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { TextArea } from "@/components/ui/text-area";
import {
  decidePostingPackage, getPostingPackage, getPostingSetup, listPostingMappings, listPostingPackages, postingAuditUrl,
  preparePostingPackage, savePostingPolicy, type DocumentProgress, type GlStatus, type PostingDocument,
  type PostingPackage, type PostingPackageSummary, type PostingPurpose, type PostingSetup
} from "@/lib/api/external-gl-posting.api";
import { formatMoney, formatTimestamp, VALUE_STATE_LABELS } from "@/lib/format";
import { getLedgerBooks, getLedgerPeriods } from "@/lib/ledger-reports-api";
import { formatApprovalError } from "@/screens/accounting-screen.approvals";
import type { ExternalGlMappingProfile, LedgerBook, LedgerPeriod } from "@/types";

const glStatusLabel: Record<GlStatus, string> = {
  Pending: "Pending",
  FullyAccepted: "Fully accepted",
  PartiallyAccepted: "Partially accepted",
  Rejected: "Rejected",
  OutcomeUnknown: "Outcome unknown"
};

function statusTone(status: GlStatus) {
  if (status === "FullyAccepted") return "success";
  if (status === "Rejected") return "danger";
  if (status === "Pending") return "outline";
  return "warning";
}

function glAmount(amount: string) {
  // JSON strings preserve exact ledger decimals, including totals outside JavaScript's safe integer range.
  const fractional = amount.split(".")[1]?.replace(/0+$/, "") ?? "";
  return formatMoney(amount, { currency: null, decimals: Math.max(2, fractional.length) });
}

const stamp = (value: string) => formatTimestamp(value, { precision: "second" });
const words = (value: string) => value.replace(/([a-z])([A-Z])/g, "$1 $2");

function PostingError({ message }: { message: string | null }) {
  return message ? <StatusBanner role="alert" tone="danger" title={message} /> : null;
}

export function ExternalGlPostingPanel() {
  const [books, setBooks] = useState<LedgerBook[] | null>(null);
  const [bookId, setBookId] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    void getLedgerBooks({ signal: controller.signal, allowDevelopmentFallback: false }).then(setBooks).catch((reason: unknown) => {
      if (!controller.signal.aborted) setError(formatApprovalError(reason, "Ledger books are unavailable."));
    });
    return () => controller.abort();
  }, []);

  const book = books?.find(row => row.ledgerBookId === bookId) ?? (books?.length === 1 ? books[0] : undefined);
  return (
    <Card id="external-gl-posting" className="panel-surface scroll-mt-6" role="region" aria-label="External GL posting">
      <CardHeader>
        <CardTitle className="text-base">External GL posting</CardTitle>
        <CardDescription>Review and approve exact Meridian journals for export and reconcile provider receipts.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <PostingError message={error} />
        {books?.length ? (
          <FormRow className="max-w-lg" label="Ledger book" labelFor="gl-posting-book">
            <Select id="gl-posting-book" value={book?.ledgerBookId ?? ""} placeholder="Select ledger book" onChange={event => setBookId(event.target.value)}>
              {books.map(row => <option key={row.ledgerBookId} value={row.ledgerBookId}>{row.displayName} · {row.fundProfileId} · {row.baseCurrency}</option>)}
            </Select>
          </FormRow>
        ) : !error && <p className="text-sm text-muted-foreground" role="status">{books ? "No ledger books are available." : "Loading ledger books…"}</p>}
        {book ? <PostingBook key={book.ledgerBookId} book={book} /> : null}
      </CardContent>
    </Card>
  );
}

function PostingBook({ book }: { book: LedgerBook }) {
  const [setup, setSetup] = useState<PostingSetup | null>(null);
  const [periods, setPeriods] = useState<LedgerPeriod[] | null>(null);
  const [periodId, setPeriodId] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    const options = { signal: controller.signal, allowDevelopmentFallback: false };
    void Promise.all([getPostingSetup(book, options), getLedgerPeriods({ ledgerBookId: book.ledgerBookId }, options)])
      .then(([nextSetup, nextPeriods]) => {
        setSetup(nextSetup);
        setPeriods(nextPeriods);
      }).catch((reason: unknown) => {
        if (!controller.signal.aborted) setError(formatApprovalError(reason, "External GL posting setup is unavailable."));
      });
    return () => controller.abort();
  }, [book]);

  if (!setup || !periods) return error ? <PostingError message={error} /> : <p role="status">Loading posting setup…</p>;
  const period = periods.find(row => row.periodId === periodId) ?? (periods.length === 1 ? periods[0] : undefined);
  const policy = setup.policy;
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2 text-sm">
        <Badge variant={policy?.confirmationRequired ? "warning" : "outline"}>
          {policy?.confirmationRequired ? "Confirmation required for final close and reports" : "Confirmation optional"}
        </Badge>
        <span>{policy ? `QuickBooks Online · ${policy.destination.environment} · realm ${policy.destination.externalLedgerId}` : "No external destination configured"}</span>
      </div>
      {setup.canConfigure ? (
        <TechnicalDetails label="Book posting policy">
          <PostingPolicyForm book={book} setup={setup} onSaved={saved => setSetup(current => current ? { ...current, policy: saved } : current)} />
        </TechnicalDetails>
      ) : null}
      {periods.length > 0 ? (
        <FormRow className="max-w-lg" label="Accounting period" labelFor="gl-posting-period">
          <Select id="gl-posting-period" value={period?.periodId ?? ""} placeholder="Select period" onChange={event => setPeriodId(event.target.value)}>
            {periods.map(row => <option key={row.periodId} value={row.periodId}>{row.label} · {row.startDate} to {row.endDate}</option>)}
          </Select>
        </FormRow>
      ) : <p className="text-sm text-muted-foreground">This book has no retained accounting periods.</p>}
      {period ? <PostingPeriod key={period.periodId} book={book} period={period} periods={periods} setup={setup} onPeriodChange={setPeriodId} /> : null}
    </div>
  );
}

function PostingPolicyForm({ book, setup, onSaved }: {
  book: LedgerBook;
  setup: PostingSetup;
  onSaved: (policy: NonNullable<PostingSetup["policy"]>) => void;
}) {
  const [connection, setConnection] = useState(setup.policy?.destination.connectionId ?? "");
  const [required, setRequired] = useState(setup.policy?.confirmationRequired ?? false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setBusy(true);
    setSaved(false);
    try {
      const policy = await savePostingPolicy(book, connection.trim(), required);
      onSaved(policy);
      setConnection(policy.destination.connectionId);
      setSaved(true);
    } catch (reason) {
      setError(formatApprovalError(reason, "The book posting policy could not be saved."));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={event => void submit(event)} className="space-y-3">
      <FormGrid>
        <FormRow label="QuickBooks connection ID" labelFor="gl-posting-connection" hint="Use a verified, tenant-owned connection from Settings.">
          <Input id="gl-posting-connection" required value={connection} onChange={event => setConnection(event.target.value)} disabled={busy} />
        </FormRow>
        <label className="flex items-center gap-2 self-start pt-6 text-sm">
          <input type="checkbox" className="h-4 w-4 accent-primary" checked={required} disabled={busy} onChange={event => setRequired(event.target.checked)} />
          Require external GL confirmation for this book
        </label>
      </FormGrid>
      <Button type="submit" size="sm" variant="outline" disabled={busy || !connection.trim()}>Save book posting policy</Button>
      <PostingError message={error} />
      {saved ? <p className="text-sm" role="status">Book posting policy saved.</p> : null}
    </form>
  );
}

function PostingPeriod({ book, period, periods, setup, onPeriodChange }: {
  book: LedgerBook;
  period: LedgerPeriod;
  periods: LedgerPeriod[];
  setup: PostingSetup;
  onPeriodChange: (id: string) => void;
}) {
  const [history, setHistory] = useState<PostingPackageSummary[] | null>(null);
  const [profiles, setProfiles] = useState<ExternalGlMappingProfile[]>([]);
  const [packageId, setPackageId] = useState("");
  const [selected, setSelected] = useState<PostingPackage | null>(null);
  const [intent, setIntent] = useState<PostingPurpose>("Export");
  const [error, setError] = useState<string | null>(null);
  const [mappingError, setMappingError] = useState<string | null>(null);
  const providerId = setup.policy?.destination.providerId;
  const selectedId = packageId || history?.[0]?.packageId || "";
  const visible = selected?.packageId === selectedId ? selected : null;
  const waitingForReceipt = Boolean(visible?.approval && (visible.status === "Pending" || visible.status === "OutcomeUnknown"));

  useEffect(() => {
    const controller = new AbortController();
    void listPostingPackages(book, period.periodId, { signal: controller.signal }).then(setHistory).catch((reason: unknown) => {
      if (!controller.signal.aborted) setError(formatApprovalError(reason, "Posting packages could not be loaded."));
    });
    return () => controller.abort();
  }, [book, period.periodId]);

  useEffect(() => {
    if (!providerId || !setup.canManage) return;
    const controller = new AbortController();
    void listPostingMappings(book, { signal: controller.signal }).then(result => {
      setProfiles(result);
      setMappingError(null);
    }).catch((reason: unknown) => {
      if (!controller.signal.aborted) setMappingError(formatApprovalError(reason, "Certified mappings are unavailable."));
    });
    return () => controller.abort();
  }, [book, providerId, setup.canManage]);

  useEffect(() => {
    if (!selectedId) return;
    const controller = new AbortController();
    void getPostingPackage(book, period.periodId, selectedId, { signal: controller.signal }).then(setSelected).catch((reason: unknown) => {
      if (!controller.signal.aborted) setError(formatApprovalError(reason, "The posting package could not be loaded."));
    });
    return () => controller.abort();
  }, [book, period.periodId, selectedId]);

  useEffect(() => {
    if (!waitingForReceipt) return;
    const controller = new AbortController();
    let inFlight = false;
    const timer = window.setInterval(() => {
      if (inFlight) return;
      inFlight = true;
      void getPostingPackage(book, period.periodId, selectedId, { signal: controller.signal }).then(result => {
        setSelected(result);
        setError(null);
      }).catch((reason: unknown) => {
        if (!controller.signal.aborted) setError(formatApprovalError(reason, "Receipt status could not be refreshed."));
      }).finally(() => { inFlight = false; });
    }, 8000);
    return () => {
      controller.abort();
      window.clearInterval(timer);
    };
  }, [book, period.periodId, selectedId, waitingForReceipt]);

  function showPackage(pkg: PostingPackage) {
    if (pkg.payload.scope.periodId !== period.periodId) {
      onPeriodChange(pkg.payload.scope.periodId);
      return;
    }
    setPackageId(pkg.packageId);
    setSelected(pkg);
    setIntent("Export");
    setHistory(current => [{
      packageId: pkg.packageId, purpose: pkg.payload.purpose, status: pkg.status,
      preparedAtUtc: pkg.preparedAtUtc, certified: Boolean(pkg.certification), approved: Boolean(pkg.approval)
    }, ...(current ?? []).filter(row => row.packageId !== pkg.packageId)]);
  }

  const hasRejection = visible?.progress.some(row => row.status === "Rejected");
  const hasAcceptance = visible?.progress.some(row => row.status === "FullyAccepted");
  return (
    <section className="space-y-4" aria-label="External GL posting packages">
      <PostingError message={error} />
      {history?.length ? (
        <FormRow label="Retained package" labelFor="gl-posting-package" className="max-w-2xl">
          <Select id="gl-posting-package" value={selectedId} onChange={event => {
            setPackageId(event.target.value);
            setIntent("Export");
            setError(null);
          }}>
            {history.map(row => <option key={row.packageId} value={row.packageId}>
              {row.purpose} · {stamp(row.preparedAtUtc)} · {row.approved ? glStatusLabel[row.packageId === visible?.packageId ? visible.status : row.status] : row.certified ? "Certified" : "Draft"}
            </option>)}
          </Select>
        </FormRow>
      ) : !error && <p role="status" className="text-sm text-muted-foreground">{history ? "No posting packages for this period." : "Loading posting packages…"}</p>}
      {visible ? (
        <PostingPackageReview key={visible.packageId} book={book} period={period} pkg={visible} setup={setup} onDecision={showPackage} />
      ) : selectedId && !error ? <p role="status" className="text-sm text-muted-foreground">Loading posting package…</p> : null}
      {visible?.approval && setup.canManage && (hasRejection || hasAcceptance) ? (
        <div className="flex flex-wrap gap-2">
          {hasRejection ? <Button type="button" size="sm" variant="outline" onClick={() => setIntent("Correction")}>Create corrective package</Button> : null}
          {hasAcceptance ? <Button type="button" size="sm" variant="outline" onClick={() => setIntent("Reversal")}>Create reversal package</Button> : null}
          {intent !== "Export" ? <Button type="button" size="sm" variant="ghost" onClick={() => setIntent("Export")}>Cancel follow-up</Button> : null}
        </div>
      ) : null}
      {setup.policy && setup.canManage ? (
        <PostingPrepareForm
          key={`${intent}:${intent === "Export" ? "new" : selectedId}:${providerId}`}
          book={book} period={period} periods={periods} profiles={profiles} mappingError={mappingError} purpose={intent}
          predecessor={intent === "Export" ? null : visible} onPrepared={showPackage}
        />
      ) : null}
    </section>
  );
}

function PostingPrepareForm({ book, period, periods, profiles, mappingError, purpose, predecessor, onPrepared }: {
  book: LedgerBook;
  period: LedgerPeriod;
  periods: LedgerPeriod[];
  profiles: ExternalGlMappingProfile[];
  mappingError: string | null;
  purpose: PostingPurpose;
  predecessor: PostingPackage | null;
  onPrepared: (pkg: PostingPackage) => void;
}) {
  const [profileId, setProfileId] = useState("");
  const [rationale, setRationale] = useState("");
  const [journalList, setJournalList] = useState("");
  const [targetPeriod, setTargetPeriod] = useState(period.periodId);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const isReversal = purpose === "Reversal";
  const usesPredecessorMapping = isReversal || (purpose === "Correction" &&
    Boolean(predecessor?.payload.documents.some(doc => doc.reversesJournalId)));
  const mappingId = profileId || (profiles.length === 1 ? profiles[0].profileId : "");
  const title = purpose === "Export" ? "Prepare export package" : purpose === "Correction" ? "Prepare corrective package" : "Prepare reversal package";

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const pkg = await preparePostingPackage(book, targetPeriod, {
        mappingProfileId: usesPredecessorMapping ? undefined : mappingId,
        journalIds: journalList.split(/[\s,]+/).filter(Boolean),
        rationale: rationale.trim(),
        purpose,
        predecessorPackageId: predecessor?.packageId,
        predecessorPeriodId: predecessor ? period.periodId : undefined
      });
      onPrepared(pkg);
      setRationale("");
      setJournalList("");
    } catch (reason) {
      setError(formatApprovalError(reason, "The package could not be prepared."));
    } finally {
      setBusy(false);
    }
  }

  return (
    <TechnicalDetails label={title} open={purpose !== "Export" ? true : undefined}>
      <form onSubmit={event => void submit(event)} aria-label={title} className="space-y-3">
        <FormGrid>
          {isReversal ? (
            <FormRow label="Reversal period" labelFor="gl-posting-reversal-period">
              <Select id="gl-posting-reversal-period" value={targetPeriod} disabled={busy} onChange={event => setTargetPeriod(event.target.value)}>
                {periods.map(row => <option key={row.periodId} value={row.periodId}>{row.label}</option>)}
              </Select>
            </FormRow>
          ) : usesPredecessorMapping ? <p className="text-sm text-muted-foreground">This correction retains the previously approved reversal mapping.</p> : (
            <FormRow label="Certified mapping profile" labelFor="gl-posting-mapping" hint={profiles.length ? null : "Certify an account and dimension mapping for this book and external destination."}>
              <Select id="gl-posting-mapping" value={mappingId} disabled={busy || !profiles.length} placeholder="Select a certified mapping" onChange={event => setProfileId(event.target.value)} required>
                {profiles.map(profile => <option key={profile.profileId} value={profile.profileId}>{profile.displayName}</option>)}
              </Select>
            </FormRow>
          )}
          <FormRow label={isReversal ? "Posted Meridian reversal journal IDs" : "Meridian journal IDs (optional)"} labelFor="gl-posting-journals"
            hint={isReversal ? "Use approved, posted reversals linked to fully accepted journals in the selected package."
              : purpose === "Correction" ? "Leave blank to include every definitively rejected journal in the selected package."
                : "Leave blank to include unassigned originals and rebooks. Start reversals from the original package."}>
            <Input id="gl-posting-journals" value={journalList} required={isReversal} disabled={busy} onChange={event => setJournalList(event.target.value)} />
          </FormRow>
          <FormRow label="Rationale" labelFor="gl-posting-rationale" span={2}>
            <TextArea id="gl-posting-rationale" value={rationale} disabled={busy} required rows={2} onChange={event => setRationale(event.target.value)} />
          </FormRow>
        </FormGrid>
        <PostingError message={error ?? (usesPredecessorMapping ? null : mappingError)} />
        <Button type="submit" size="sm" disabled={busy || !rationale.trim() || (isReversal ? !journalList.trim() : !usesPredecessorMapping && !mappingId)}>{title}</Button>
      </form>
    </TechnicalDetails>
  );
}

function PostingPackageReview({ book, period, pkg, setup, onDecision }: {
  book: LedgerBook;
  period: LedgerPeriod;
  pkg: PostingPackage;
  setup: PostingSetup;
  onDecision: (pkg: PostingPackage) => void;
}) {
  const [notes, setNotes] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const { documents, mapping, source, destination } = pkg.payload;
  const approverIsPreparer = setup.actor.toLowerCase() === pkg.preparedBy.toLowerCase();
  const progress = new Map(pkg.progress.map(row => [row.idempotencyKey, row]));
  const approvalLabel = pkg.approval ? glStatusLabel[pkg.status] : pkg.certification ? "Certified · awaiting approval" : "Draft";

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setBusy(true);
    try {
      onDecision(await decidePostingPackage(book, period.periodId, pkg, Boolean(pkg.certification), notes.trim()));
      setNotes("");
    } catch (reason) {
      setError(formatApprovalError(reason, "The package decision could not be recorded."));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section aria-label="Posting package review" className="space-y-3 border-y border-border py-4">
      <div className="flex flex-wrap items-center gap-3">
        <h3 className="text-sm font-semibold">{pkg.payload.purpose} package</h3>
        <span role="status"><Badge variant={pkg.approval ? statusTone(pkg.status) : "outline"}>{approvalLabel}</Badge></span>
        <Button asChild size="sm" variant="outline">
          <a href={postingAuditUrl(book, period.periodId, pkg.packageId)}>Download audit package</a>
        </Button>
      </div>
      <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm lg:grid-cols-4">
        <div><dt className="text-xs text-muted-foreground">Mapping version</dt><dd>{mapping.profile.displayName} <code>{mapping.version.slice(0, 12)}</code></dd></div>
        <div><dt className="text-xs text-muted-foreground">Posting period</dt><dd>{source.periodStart} to {source.periodEnd}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Debit total</dt><dd className="tabular-nums">{documents[0]?.currency} {glAmount(pkg.payload.totalDebit)}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Credit total</dt><dd className="tabular-nums">{documents[0]?.currency} {glAmount(pkg.payload.totalCredit)}</dd></div>
      </dl>
      <p className="text-sm">{pkg.payload.rationale}</p>
      <TechnicalDetails label="Package provenance">
        <dl className="grid gap-2 text-xs sm:grid-cols-2">
          <div><dt>Package ID</dt><dd className="break-all font-mono">{pkg.packageId}</dd></div>
          <div><dt>Approved payload SHA-256</dt><dd className="break-all font-mono">{pkg.payloadHash}</dd></div>
          <div><dt>Mapping SHA-256</dt><dd className="break-all font-mono">{mapping.version}</dd></div>
          <div><dt>Source SHA-256</dt><dd className="break-all font-mono">{source.contentHash}</dd></div>
          <div><dt>Last source sequence</dt><dd>{source.lastJournalSequence}</dd></div>
          <div><dt>Target</dt><dd>{destination.providerId} · {destination.connectionId} · {destination.externalLedgerId} · {destination.environment}</dd></div>
          <div><dt>Prepared</dt><dd>{pkg.preparedBy} · {stamp(pkg.preparedAtUtc)}</dd></div>
          <div><dt>Certified mapping</dt><dd>{mapping.certifiedBy} · {stamp(mapping.profile.updatedAtUtc)}</dd></div>
          {pkg.certification ? <div><dt>Certification</dt><dd>{pkg.certification.actor} · {stamp(pkg.certification.recordedAtUtc)} · {pkg.certification.notes}</dd></div> : null}
          {pkg.approval ? <div><dt>Approval</dt><dd>{pkg.approval.actor} · {stamp(pkg.approval.recordedAtUtc)} · {pkg.approval.notes}</dd></div> : null}
          {pkg.payload.predecessorPackageId ? <div><dt>Predecessor package</dt><dd className="break-all font-mono">{pkg.payload.predecessorPackageId}</dd></div> : null}
        </dl>
      </TechnicalDetails>
      <div className="space-y-2">
        {documents.map((doc, index) => <PostingJournal key={doc.idempotencyKey} doc={doc} progress={progress.get(doc.idempotencyKey)} initiallyOpen={index === 0} />)}
      </div>
      {setup.canManage && !pkg.approval ? approverIsPreparer && pkg.certification ? (
        <p className="text-sm text-muted-foreground">Approval requires an operator other than the preparer.</p>
      ) : (
        <form onSubmit={event => void submit(event)} className="space-y-2">
          <FormRow label={pkg.certification ? "Approval notes" : "Certification notes"} labelFor="gl-posting-decision">
            <TextArea id="gl-posting-decision" required rows={2} value={notes} disabled={busy} onChange={event => setNotes(event.target.value)} />
          </FormRow>
          <PostingError message={error} />
          <Button type="submit" size="sm" disabled={busy || !notes.trim()}>{pkg.certification ? "Approve and queue posting" : "Certify exact package"}</Button>
        </form>
      ) : null}
    </section>
  );
}

function PostingJournal({ doc, progress, initiallyOpen }: { doc: PostingDocument; progress?: DocumentProgress; initiallyOpen: boolean }) {
  const latest = progress?.observations.at(-1);
  const receipt = latest?.receipt;
  const latestDocumentId = progress?.observations.slice().reverse().find(item => item.receipt.providerDocumentId)?.receipt.providerDocumentId;
  const lines = new Map(receipt?.lines.map(line => [line.meridianLineId, line]));
  const label = `${doc.effectiveDate} · Journal ${doc.meridianJournalId} · ${glStatusLabel[progress?.status ?? "Pending"]}`;
  return (
    <TechnicalDetails label={label} open={initiallyOpen}>
      <dl className="mb-3 grid gap-2 text-xs sm:grid-cols-3">
        <div><dt>Provider document</dt><dd>{latestDocumentId ?? "Awaiting receipt"}</dd></div>
        <div><dt>Provider batch</dt><dd>{receipt?.providerBatchId ?? VALUE_STATE_LABELS.unavailable}</dd></div>
        <div><dt>Receipt observed</dt><dd>{latest ? stamp(latest.observedAtUtc) : VALUE_STATE_LABELS.unavailable}</dd></div>
      </dl>
      {receipt?.errorCode ? <StatusBanner className="mb-3" tone="warning" title={words(receipt.errorCode)}
        detail={receipt.providerErrorCode ? `Provider code ${receipt.providerErrorCode}` : undefined} /> : null}
      <DenseDataTable
        ariaLabel="Journal lines and provider results"
        emptyText="No journal lines"
        tableClassName="min-w-[760px]"
        rows={doc.lines}
        getRowId={line => line.meridianLineId}
        columns={[
          {
            id: "line", label: "Meridian line", rowHeader: true,
            render: line => <div className="max-w-56">
              {line.meridianAccount}
              <code className="block break-all text-muted-foreground">{line.meridianLineId}</code>
            </div>
          },
          { id: "account", label: "External account", render: line => <code>{line.externalAccountId}</code> },
          {
            id: "dimensions", label: "External dimensions",
            render: line => Object.entries(line.externalDimensions).map(([key, value]) => `${key} ${value}`).join(", ") || VALUE_STATE_LABELS.unavailable
          },
          { id: "debit", label: "Debit", numeric: true, render: line => glAmount(line.debit) },
          { id: "credit", label: "Credit", numeric: true, render: line => glAmount(line.credit) },
          {
            id: "receipt", label: "Provider result",
            render: line => {
              const result = lines.get(line.meridianLineId);
              const resultState = result ? words(result.status) : progress?.status === "OutcomeUnknown" ? "Outcome unknown" : "Awaiting receipt";
              return <>
                <div>{resultState}{result?.providerLineId ? ` · line ${result.providerLineId}` : ""}</div>
                {result?.errorCode ? <div className="text-warning">{words(result.errorCode)}</div> : null}
                {result?.status === "OutcomeUnknown" && result.externalAccountId && result.debit != null && result.credit != null ? (
                  <div className="text-muted-foreground">
                    Received account {result.externalAccountId} · debit {glAmount(result.debit)} · credit {glAmount(result.credit)}
                  </div>
                ) : null}
              </>;
            }
          }
        ]}
      />
      <TechnicalDetails label="Receipt history" className="mt-3">
        <p className="break-all font-mono text-xs">Idempotency key {doc.idempotencyKey}</p>
        {progress?.lastPostAttemptAtUtc ? <p className="text-xs">Last submission attempt {stamp(progress.lastPostAttemptAtUtc)} · {progress.postAttempts} total</p> : null}
        {doc.supersedesIdempotencyKey ? <p className="break-all font-mono text-xs">Corrects {doc.supersedesIdempotencyKey}</p> : null}
        {doc.reversesJournalId ? <p className="break-all font-mono text-xs">Reverses {doc.reversesJournalId}</p> : null}
        {progress?.observations.length ? (
          <ol className="mt-2 space-y-1 text-xs">
            {progress.observations.map((item, index) => <li key={`${item.observedAtUtc}:${index}`}>
              {stamp(item.observedAtUtc)} · {item.isLookup ? "Lookup" : "Submission"} · {words(item.receipt.kind)}
              {item.receipt.providerTraceId ? ` · trace ${item.receipt.providerTraceId}` : ""}
              {item.receipt.providerRecordedAtUtc ? ` · recorded ${stamp(item.receipt.providerRecordedAtUtc)}` : ""}
            </li>)}
          </ol>
        ) : null}
      </TechnicalDetails>
    </TechnicalDetails>
  );
}
