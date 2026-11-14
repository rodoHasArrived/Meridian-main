import { createContext, useContext, useEffect, useId, useMemo, useRef, useState, type ReactNode } from "react";
import { Info } from "lucide-react";
import { NumberPassport } from "@/components/meridian/number-passport";
import { hasExactRetainedSource, isCurrentRetainedEvidence } from "@/components/meridian/ledger-amount-proof-validation";
import { apiGetJson } from "@/lib/api";
import { workstationEvidencePacketEndpoint } from "@/lib/workstation-endpoints";
import { metricBasisLabel } from "@/lib/metric-context";
import type { EvidencePacket, MetricSnapshot, NumberPassportReference } from "@/types";
import type { LedgerAmountProof } from "@/types/ledger-amount-proof";

const MetricSessionContext = createContext<unknown>(null);
/** Invalidation only. The server remains the authority for every evidence read. */
export function MetricPassportSessionProvider({ children, scopeKey, sessionIdentity }: {
  children: ReactNode; scopeKey: string; sessionIdentity: unknown;
}) {
  const identity = useMemo(() => ({ scopeKey, sessionIdentity }), [scopeKey, sessionIdentity]);
  return <MetricSessionContext.Provider value={identity}>{children}</MetricSessionContext.Provider>;
}

function decimalIdentity(value: string): string | null {
  if (!/^[+-]?\d+(?:\.\d+)?$/.test(value)) return null;
  const negative = value.startsWith("-");
  const [whole = "", fraction = ""] = value.replace(/^[+-]/, "").split(".");
  const integer = whole.replace(/^0+(?=\d)/, "");
  const tail = fraction.replace(/0+$/, "");
  return `${negative && (integer !== "0" || tail) ? "-" : ""}${integer}${tail ? `.${tail}` : ""}`;
}

export function validateMetricPassport(reference: NumberPassportReference, packet: EvidencePacket): { proof: LedgerAmountProof | null; error: string | null } {
  const reject = (error: string) => ({ proof: null, error });
  if (reference.schemaVersion !== 1 || packet?.subject?.subjectKind !== reference.subjectType || packet.subject.subjectId !== reference.subjectId)
    return reject("The returned evidence does not identify this exact figure subject.");
  if (reference.subjectType !== "ledger-amount")
    return reject("This evidence contract does not verify the figure's amount and complete scope. Review its source workspace.");
  if (reference.subjectId?.startsWith("report:"))
    return reject("Report totals require the exact retained contributor binding. Open the report's retained amount drawer to inspect its proof.");
  const scope = reference.scope;
  const proof = packet.ledgerAmount;
  const keys = ["tenantId", "companyId", "fundProfileId", "ledgerBookId", "periodId"] as const;
  if (!scope || !proof || keys.some(key => !scope[key] || proof.scope?.[key] !== scope[key])
    || [scope.organizationId, scope.entityId, scope.accountId, scope.runId].some(Boolean))
    return reject("The evidence does not declare the complete selected figure scope.");
  if (!reference.amount || !reference.currency || proof.subjectId !== reference.subjectId || proof.currency !== reference.currency
    || !Number.isFinite(proof.amount) || decimalIdentity(String(proof.amount)) === null
    || decimalIdentity(reference.amount) === null || decimalIdentity(String(proof.amount)) !== decimalIdentity(reference.amount))
    return reject("The retained amount or currency differs from the figure reference. No proof is shown.");
  if (packet.completeness?.status !== proof.status || !Array.isArray(proof.evidence)
    || new Set(proof.evidence.map(item => item.evidenceId)).size !== proof.evidence.length
    || proof.evidence.some(item => !item.evidenceId))
    return reject("The amount evidence has missing or ambiguous retained identities.");
  if (proof.evidence.some(item => item.kind !== "ledger-record" && isCurrentRetainedEvidence(item) && !hasExactRetainedSource(packet, proof, item)))
    return reject("Supporting evidence is not bound to this exact retained amount and scope.");
  const incomplete = !proof.evidence.some(item => item.kind !== "ledger-record") || proof.evidence.some(item => !isCurrentRetainedEvidence(item));
  return { proof: incomplete && proof.status === "Ready" ? { ...proof, status: "ReviewRequired",
    warnings: [...proof.warnings, "Retained supporting evidence is incomplete; review is required."] } : proof, error: null };
}

/** Context opens on hover, keyboard focus or click; absence of proof remains explicit. */
export function MetricPassport({ metric }: { metric: MetricSnapshot }) {
  const session = useContext(MetricSessionContext);
  const id = useId();
  const container = useRef<HTMLSpanElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const [open, setOpen] = useState(false);
  useEffect(() => {
    if (!open) return;
    const dismiss = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !event.defaultPrevented) setOpen(false);
    };
    document.addEventListener("keydown", dismiss);
    return () => document.removeEventListener("keydown", dismiss);
  }, [open]);
  const key = JSON.stringify([metric.id, metric.value, metric.numberPassport]);
  const [result, setResult] = useState<{ key: string; session: unknown; proof: LedgerAmountProof | null; error: string | null } | null>(null);
  useEffect(() => {
    const reference = metric.numberPassport;
    if (!open || !reference?.subjectId || !reference.subjectType || reference.unavailableReason) return;
    const controller = new AbortController();
    let current = true;
    const query = new URLSearchParams();
    if (reference.subjectType === "ledger-amount") {
      const scope = reference.scope;
      if (!scope?.fundProfileId || !scope.ledgerBookId || !scope.periodId || !scope.tenantId || !scope.companyId) {
        setResult({ key, session, proof: null, error: "Retained amount scope is incomplete. No evidence was requested." });
        return () => controller.abort();
      }
      query.set("fundProfileId", scope.fundProfileId); query.set("ledgerBookId", scope.ledgerBookId); query.set("periodId", scope.periodId);
    }
    const endpoint = workstationEvidencePacketEndpoint(reference.subjectType, reference.subjectId);
    void apiGetJson<EvidencePacket>(`${endpoint}${query.size ? `?${query}` : ""}`, { signal: controller.signal, allowDevelopmentFallback: false })
      .then(packet => { if (current) setResult({ key, session, ...validateMetricPassport(reference, packet) }); })
      .catch(() => { if (current) setResult({ key, session, proof: null, error: "Retained evidence is unavailable. This figure remains unverified." }); });
    return () => { current = false; controller.abort(); };
  }, [open, key, metric.numberPassport, session]);
  const current = result?.key === key && result.session === session ? result : null;
  const unavailable = metric.numberPassport?.unavailableReason || (!metric.numberPassport?.subjectId || !metric.numberPassport.subjectType
    ? "Unverified: this source has not supplied a retained evidence subject for the figure." : null);
  const close = () => setOpen(false);
  return <span ref={container} className="relative inline-flex" onMouseEnter={() => setOpen(true)}
    onMouseLeave={() => { if (!container.current?.contains(document.activeElement)) close(); }}
    onBlur={event => { if (!event.currentTarget.contains(event.relatedTarget as Node | null)) close(); }}
    onKeyDown={event => { if (event.key === "Escape" && open) { event.preventDefault(); event.stopPropagation(); close(); } }}>
    <button ref={trigger} type="button" className="inline-flex h-6 w-6 items-center justify-center text-muted-foreground focus-visible:outline focus-visible:outline-2"
      aria-label={`Inspect ${metric.label} basis and evidence`} aria-expanded={open} aria-controls={id}
      onFocus={() => setOpen(true)} onClick={() => setOpen(true)}><Info size={12} aria-hidden="true" /></button>
    {open ? <span id={id} role="dialog" aria-label={`${metric.label} figure context`}
      style={{ left: Math.max(8 - (trigger.current?.getBoundingClientRect().left ?? 8),
        Math.min(0, window.innerWidth - (trigger.current?.getBoundingClientRect().left ?? 0) - Math.min(416, window.innerWidth * 0.7) - 8)) }}
      className="absolute left-0 top-full z-40 block max-h-[65vh] w-[min(26rem,70vw)] overflow-auto border border-border bg-card p-3 text-left text-xs shadow-md">
      <span className="block font-semibold">{metric.label}: <span className="tabular-nums">{metric.value}</span></span>
      <span className="mt-1 block">{metricBasisLabel(metric)}</span>
      <span className="mt-1 block">Source: {metric.source?.trim() || "Not supplied"}</span>
      {unavailable ? <span className="mt-2 block text-warning">{unavailable}</span>
        : !current ? <span role="status" className="mt-2 block">Loading retained figure evidence…</span>
          : current.error ? <span role="alert" className="mt-2 block text-warning">{current.error}</span>
            : current.proof ? <>
              {!current.proof.evidence.some(item => item.kind !== "ledger-record") || current.proof.evidence.some(item => !isCurrentRetainedEvidence(item))
                ? <span className="mt-2 block text-warning">Review required: retained supporting evidence is incomplete.</span> : null}
              <NumberPassport proof={current.proof} title={metric.label} inline />
            </> : null}
    </span> : null}
  </span>;
}
