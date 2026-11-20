import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { DrawerBody } from "@/components/ui/drawer";
import { AmountPassportRail } from "@/components/meridian/proof-drawer";
import { matchesLedgerAmountManifestGuard } from "@/components/meridian/ledger-amount-proof-validation";
import { AmountPassportFactsView, PassportFacts, amountPassportStatusLabels } from "@/components/meridian/number-passport";
import { amountPassportHref, amountPassportKey, getAmountPassport, parseAmountPassportKey } from "@/lib/amount-passport-api";
import { isApiError } from "@/lib/api-errors";
import type { AmountPassport, AmountPassportIdentity, AmountPassportReference, AmountOrigin } from "@/types/amount-passport";
import type { MarkFreshnessPresentation } from "@/lib/mark-freshness";

export interface AmountPassportSelection {
  label: string;
  displayValue?: string;
  reference?: AmountPassportReference | null;
  identity?: AmountPassportIdentity | null;
  category?: string;
  currency?: string | null;
  unit?: string;
  origin?: AmountOrigin;
  zero?: boolean;
  asOf?: string | null;
  basis?: string | null;
  mark?: MarkFreshnessPresentation | null;
  freshness?: string;
  unavailableReason?: string;
  restricted?: boolean;
}

type Failure = "restricted" | "scope-mismatch" | "missing" | "unavailable";
type ReadResult = { key: string; passport: AmountPassport } | { key: string; failure: Failure };
const requiredComponents = ["scope", "source", "calculation", "freshness", "evidence", "history", "permissions"];

function usePassportRead(identity: AmountPassportIdentity | null, retry: number, expected?: AmountPassportReference | null) {
  const key = identity ? amountPassportKey(identity) : null;
  const requestKey = key === null ? null : `${retry}:${key}`;
  const exactValue = expected?.exactValue;
  const currency = expected?.currency;
  const [result, setResult] = useState<ReadResult | null>(null);
  useEffect(() => {
    const requested = key === null ? null : parseAmountPassportKey(key);
    if (!key || !requestKey || !requested) return;
    const controller = new AbortController();
    void getAmountPassport(requested, { signal: controller.signal }).then(passport => {
      if (controller.signal.aborted) return;
      const scopeMatches = passport?.reference?.identity?.scope && amountPassportKey(passport.reference.identity) === key;
      const hasComponents = Array.isArray(passport?.components) && requiredComponents.every(component =>
        passport.components.filter(item => item.component === component).length === 1);
      const hasRecords = Array.isArray(passport?.sources) && Array.isArray(passport?.evidence) &&
        Array.isArray(passport?.history) && Array.isArray(passport?.gaps);
      const guardedEvidence = requested.subjectKind !== "ledger-amount" || passport?.evidence?.every(item =>
        matchesLedgerAmountManifestGuard(item.href, item.contentHash, requested.subjectId, requested.scope));
      const hasBody = passport?.permissions?.view === "allowed" && passport?.freshness?.evaluatedAt &&
        ["complete", "review-required", "stale", "blocked", "superseded", "unavailable"].includes(passport.status);
      const amountMatches = !passport?.exactValue ||
        ((exactValue == null || exactValue === passport.exactValue) && (currency === undefined || currency === passport.reference.currency));
      const complete = passport?.status !== "complete" || (typeof passport.exactValue === "string" && passport.sources?.length > 0 &&
        Boolean(passport.calculation && passport.exchangeRateRule && passport.reference?.unit && passport.freshness?.policy) &&
        passport.freshness.status === "current" && passport.components?.every(item => ["available", "not-applicable"].includes(item.status))
        && passport.gaps?.length === 0);
      setResult(scopeMatches && hasComponents && hasRecords && hasBody && amountMatches && complete && guardedEvidence
        ? { key: requestKey, passport } : { key: requestKey, failure: "scope-mismatch" });
    }).catch(error => {
      if (controller.signal.aborted) return;
      const status = isApiError(error) ? error.status : null;
      setResult({ key: requestKey, failure: status === 401 || status === 403 ? "restricted"
        : status === 409 || status === 400 ? "scope-mismatch" : status === 404 || status === 410 ? "missing" : "unavailable" });
    });
    return () => controller.abort();
  }, [key, requestKey, exactValue, currency]);
  return result?.key === requestKey ? result : null;
}

export function AmountPassportDrawer({ selection, baseline, onPin, onClearPin, onFollow, onClose, onBack, width, onWidth }: {
  selection: AmountPassportSelection;
  baseline?: AmountPassportIdentity | null;
  onPin?: (identity: AmountPassportIdentity) => void;
  onClearPin?: () => void;
  onFollow: (identity: AmountPassportIdentity) => void;
  onClose: () => void;
  onBack?: () => void;
  width: number;
  onWidth: (width: number) => void;
}) {
  const identity = selection.restricted ? null : selection.reference?.identity ?? selection.identity ?? null;
  const [retry, setRetry] = useState(0);
  const [expanded, setExpanded] = useState(false);
  const [copyNotice, setCopyNotice] = useState<string | null>(null);
  const [showLink, setShowLink] = useState(false);
  const result = usePassportRead(identity, retry, selection.reference);
  const baselineIdentity = identity && baseline && amountPassportKey(baseline) !== amountPassportKey(identity) ? baseline : null;
  const baselineResult = usePassportRead(baselineIdentity, retry);
  const heading = useRef<HTMLHeadingElement>(null);
  const linkField = useRef<HTMLInputElement>(null);
  const sourceKey = identity ? amountPassportKey(identity) : selection.label;
  // The shell stays mounted when following a source. Move focus to the newly selected amount.
  useEffect(() => {
    const body = heading.current?.closest<HTMLElement>("[data-amount-passport-body]");
    if (body) body.scrollTop = 0;
    heading.current?.focus({ preventScroll: true });
  }, [sourceKey]);
  useEffect(() => {
    if (showLink) {
      linkField.current?.focus({ preventScroll: true });
      linkField.current?.select();
    }
  }, [showLink]);
  const proof = result && "passport" in result ? result.passport : null;
  const baselineProof = baselineResult && "passport" in baselineResult ? baselineResult.passport : null;
  const href = proof ? new URL(amountPassportHref(proof.reference.identity), window.location.origin).href : null;

  async function copyLink() {
    if (!href) return;
    try {
      await navigator.clipboard.writeText(href);
      setCopyNotice("Link copied.");
    } catch {
      setShowLink(true);
      setCopyNotice("Link selected. Use your browser's copy command.");
    }
  }

  return <AmountPassportRail onClose={onClose} width={expanded ? 72 : width}>
    <DrawerBody data-amount-passport-body className="min-h-0 min-w-0 overscroll-contain">
      <header>
        <h3 ref={heading} tabIndex={-1} className="text-base font-semibold [overflow-wrap:anywhere]">
          {selection.displayValue ? selection.label : proof?.label ?? selection.label}
        </h3>
        <div className="mt-1 flex flex-wrap items-center gap-2">
          <p className="min-w-0 font-mono text-xl tabular-nums [overflow-wrap:anywhere]">
            {selection.displayValue ?? (proof?.exactValue ? `${proof.exactValue}${proof.reference.currency ? ` ${proof.reference.currency}` : ""}` : "Unavailable")}
          </p>
          {proof ? <p role="status"><Badge variant={proof.status === "complete" ? "success" : proof.status === "blocked" ? "danger" : "warning"}>
            {amountPassportStatusLabels[proof.status]}
          </Badge></p> : null}
        </div>
      </header>
      <div className="flex flex-wrap items-center gap-2">
        {onBack ? <Button size="sm" variant="outline" onClick={onBack}>Back to previous amount</Button> : null}
        {href ? <Button size="sm" variant="outline" onClick={() => void copyLink()}>Copy stable link</Button> : null}
        {proof && onPin ? <Button size="sm" variant="outline" onClick={() => onPin(proof.reference.identity)}>
          {baseline ? "Use as comparison baseline" : "Pin for comparison"}
        </Button> : null}
        <Button size="sm" variant="outline" aria-expanded={expanded} onClick={() => setExpanded(value => !value)}>
          {expanded ? "Return to rail" : "Open full investigation"}
        </Button>
      </div>
      {copyNotice ? <p role="status">{copyNotice}</p> : null}
      {showLink && href ? <input ref={linkField} aria-label="Stable passport link" readOnly value={href}
        className="w-full min-w-0 rounded-sm border border-border bg-background p-2" /> : null}
      {!expanded ? <label className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
        Rail width
        <input aria-label="Rail width" className="max-w-full accent-primary" type="range" min={22} max={46}
          value={width} onChange={event => onWidth(event.currentTarget.valueAsNumber)} />
      </label> : null}
      {baseline ? <div className="flex flex-wrap items-center justify-between gap-2 border-y border-border py-2">
        <p>Comparison baseline · {baseline.asOf}</p>
        <Button size="sm" variant="ghost" onClick={onClearPin}>Clear comparison</Button>
      </div> : null}
      {proof && baselineProof && (
        proof.reference.currency !== baselineProof.reference.currency ||
        proof.reference.unit !== baselineProof.reference.unit ||
        proof.reference.identity.scope.basis !== baselineProof.reference.identity.scope.basis
      ) ? <p role="status" className="border-l-2 border-border pl-3">Comparison values differ in currency, units, or basis. Review both scopes.</p> : null}
      <div className={baselineIdentity && expanded ? "grid gap-6 lg:grid-cols-2" : "grid gap-6"}>
        <div className="min-w-0 space-y-3" key={sourceKey}>
          {proof ? <AmountPassportFactsView passport={proof} onFollow={onFollow} />
            : <UnresolvedPassport selection={selection} identity={identity} result={result} onRetry={() => {
            setRetry(value => value + 1);
          }} />}
        </div>
        {baselineIdentity ? <section className="min-w-0 space-y-3 border-t border-border pt-3" aria-label="Comparison baseline">
          <h3 className="font-semibold">{baselineProof?.label ?? "Comparison baseline"}</h3>
          {baselineProof ? <>
            <p>{amountPassportStatusLabels[baselineProof.status]}</p>
            <AmountPassportFactsView passport={baselineProof} onFollow={onFollow} baseline />
          </> : <UnresolvedPassport selection={{ label: "Comparison baseline" }} identity={baselineIdentity} result={baselineResult} />}
        </section> : null}
      </div>
    </DrawerBody>
  </AmountPassportRail>;
}

function UnresolvedPassport({ selection, identity, result, onRetry }: {
  selection: AmountPassportSelection; identity: AmountPassportIdentity | null; result: ReadResult | null; onRetry?: () => void;
}) {
  const reference = selection.reference;
  const parsed = identity ? parseAmountPassportKey(amountPassportKey(identity)) : null;
  const failure = selection.restricted ? "restricted" : result && "failure" in result ? result.failure : null;
  const loading = !failure && parsed && !result;
  const category = selection.category ?? (identity?.subjectKind === "position-valuation" ? "Position valuation"
    : identity?.subjectId.startsWith("report:") ? "Report amount" : "Posted ledger amount");
  const reason = failure === "restricted" ? `${category} proof is restricted for the current session.`
    : failure === "scope-mismatch" ? "The retained subject differs from the declared scope, date or revision."
    : failure === "missing" ? "The original source is missing, deleted, or unavailable in the declared scope."
    : failure === "unavailable" ? "The proof service is unavailable. The displayed amount is preserved."
    : selection.unavailableReason ?? "The source did not supply an immutable amount reference for this value.";
  return <>
    {loading ? <p role="status">Loading amount passport…</p>
      : <div role={failure || selection.restricted ? "alert" : "status"} className="space-y-1 border-l-2 border-border pl-3">
        <p className="font-semibold">{amountPassportStatusLabels[failure ?? "unavailable"]}</p>
        <p>{reason}</p>
      </div>}
    {failure !== "restricted" ? <PassportFacts facts={[
      ["Currency", reference?.currency ?? selection.currency], ["Units", reference?.unit ?? selection.unit],
      ["As of", identity?.asOf ?? selection.asOf], ["Ledger book", identity?.scope.ledgerBookId],
      ["Basis", identity?.scope.basis ?? selection.basis], ["Period", identity?.scope.periodId],
      ["Fund", identity?.scope.fundProfileId], ["Entity", identity?.scope.entityId],
      ["Origin", amountPassportStatusLabels[reference?.origin ?? selection.origin ?? "unavailable"]],
      ...(selection.mark ? [
        ["Mark readiness", selection.mark.label], ["Mark evaluated for", selection.mark.valuationDate],
        ["Mark policy", selection.mark.policyVersion], ["Mark age", selection.mark.age], ["Mark decision", selection.mark.reason]
      ] as [string, string][] : [])
    ]} /> : null}
    {failure === "restricted" ? <PassportFacts facts={[["Permissions", "Cannot view proof"]]} />
      : <dl className="divide-y divide-border" aria-busy={Boolean(loading)}>
        {["Freshness", "Computation and exchange rate", "Source records", "Evidence links", "History", "Permissions"].map(label =>
          <div className="grid gap-1 py-2 sm:grid-cols-2" key={label}>
            <dt>{label}</dt>
            <dd>{loading ? <span aria-hidden="true" className="inline-block h-3 w-2/3 rounded-sm bg-muted motion-safe:animate-pulse" /> : "Unavailable"}</dd>
          </div>
        )}
      </dl>}
    {onRetry && failure === "unavailable" ? <Button size="sm" variant="outline" onClick={onRetry}>Retry proof</Button> : null}
  </>;
}
