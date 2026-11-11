// Meridian object model — the financial objects the platform actually manipulates, and the identity
// contract every one of them answers. A workstation should not invent its own identity header: it
// names the object type, and the header states type, identifier, name, context, condition, as-of,
// material value, version, evidence coverage, and open exceptions in one fixed order.
import React from "react";

/** The object ledger. Seventeen objects; every surface in Meridian is about one of them.
 *  `identifierLabel` is the primary key a human quotes. `material` is what "how much" means for
 *  this object — it is never assumed to be a currency amount. */
export const OBJECT_TYPES = [
  { id: "security", label: "Security", plural: "Securities", identifierLabel: "CUSIP", nameLabel: "Description", material: "Market value", lifecycle: ["active", "matured", "called", "delisted"], evidence: "Terms, prices, and identifier sources", context: ["entity", "book"], timeRoles: ["effective", "valuation", "asOf"] },
  { id: "position", label: "Position", plural: "Positions", identifierLabel: "Position", nameLabel: "Security", material: "Market value", lifecycle: ["open", "closed"], evidence: "Custodian holdings and pricing", context: ["entity", "book", "portfolio"], timeRoles: ["asOf", "valuation", "settlement"] },
  { id: "lot", label: "Tax lot", plural: "Tax lots", identifierLabel: "Lot", nameLabel: "Security", material: "Cost basis", lifecycle: ["open", "partially-relieved", "relieved"], evidence: "Acquisition and relief history", context: ["entity", "book"], timeRoles: ["trade", "settlement", "asOf"] },
  { id: "account", label: "Account", plural: "Accounts", identifierLabel: "Account", nameLabel: "Account name", material: "Balance", lifecycle: ["open", "restricted", "closed"], evidence: "Chart of accounts and mapping", context: ["entity", "book"], timeRoles: ["asOf", "reportingPeriod"] },
  { id: "journal", label: "Journal entry", plural: "Journal entries", identifierLabel: "Entry", nameLabel: "Description", material: "Debit total", lifecycle: ["draft", "pending", "posted", "reversed"], evidence: "Supporting documents and calculation", context: ["entity", "book"], timeRoles: ["posting", "effective", "reportingPeriod"] },
  { id: "transaction", label: "Transaction", plural: "Transactions", identifierLabel: "Transaction", nameLabel: "Type and security", material: "Net amount", lifecycle: ["pending", "settled", "cancelled"], evidence: "Confirmations and settlement records", context: ["entity", "portfolio"], timeRoles: ["trade", "settlement", "effective"] },
  { id: "portfolio", label: "Portfolio", plural: "Portfolios", identifierLabel: "Portfolio", nameLabel: "Portfolio name", material: "Net asset value", lifecycle: ["active", "closing", "closed"], evidence: "Position and valuation coverage", context: ["entity", "book"], timeRoles: ["asOf", "valuation"] },
  { id: "legalEntity", label: "Legal entity", plural: "Legal entities", identifierLabel: "Entity", nameLabel: "Registered name", material: "Total assets", lifecycle: ["active", "dormant", "dissolved"], evidence: "Ownership and registration records", context: ["entity"], timeRoles: ["fiscalPeriod", "asOf"] },
  { id: "accountingPeriod", label: "Accounting period", plural: "Accounting periods", identifierLabel: "Period", nameLabel: "Period name", material: "Entries posted", lifecycle: ["open", "soft-closed", "hard-closed", "adjustment"], evidence: "Close package and approvals", context: ["entity", "book"], timeRoles: ["reportingPeriod", "fiscalPeriod", "closeDeadline"] },
  { id: "reconciliation", label: "Reconciliation", plural: "Reconciliations", identifierLabel: "Reconciliation", nameLabel: "Sources reconciled", material: "Residual difference", lifecycle: ["open", "in-review", "accepted", "signed"], evidence: "Both sides and the explaining items", context: ["entity", "book"], timeRoles: ["asOf", "reportingPeriod", "evidenceAt"] },
  { id: "exception", label: "Exception", plural: "Exceptions", identifierLabel: "Exception", nameLabel: "Statement", material: "Residual", lifecycle: ["open", "classified", "resolved", "escalated", "waived"], evidence: "Sources, trace, and resolution record", context: ["entity", "book"], timeRoles: ["asOf", "reportingPeriod"] },
  { id: "control", label: "Control", plural: "Controls", identifierLabel: "Control", nameLabel: "Control name", material: "Dependent controls", lifecycle: ["not-started", "in-progress", "blocked", "complete", "reopened"], evidence: "Execution record and sign-off", context: ["entity", "book"], timeRoles: ["closeDeadline", "reportingPeriod"] },
  { id: "evidenceItem", label: "Evidence item", plural: "Evidence items", identifierLabel: "Document", nameLabel: "Title", material: "Extraction completeness", lifecycle: ["received", "extracted", "verified", "superseded", "missing"], evidence: "The source itself and its lineage", context: ["entity"], timeRoles: ["evidenceAt", "received"] },
  { id: "calculation", label: "Calculation", plural: "Calculations", identifierLabel: "Calculation", nameLabel: "Result label", material: "Result", lifecycle: ["current", "stale", "superseded", "failed"], evidence: "Inputs, formula, version, and engine", context: ["book", "basis"], timeRoles: ["calculatedAt", "asOf", "valuation"] },
  { id: "attestation", label: "Attestation", plural: "Attestations", identifierLabel: "Attestation", nameLabel: "Scope", material: "Gates satisfied", lifecycle: ["draft", "ready", "signed", "rejected", "reopened"], evidence: "Evidence set snapshot and signatures", context: ["entity", "book"], timeRoles: ["reportingPeriod", "closeDeadline"] },
  { id: "scenario", label: "Scenario", plural: "Scenarios", identifierLabel: "Scenario", nameLabel: "Scenario name", material: "Change versus base", lifecycle: ["draft", "published", "superseded"], evidence: "Assumptions and model version", context: ["book", "basis"], timeRoles: ["asOf", "calculatedAt"] },
  { id: "strategy", label: "Strategy", plural: "Strategies", identifierLabel: "Strategy", nameLabel: "Strategy name", material: "Capital allocated", lifecycle: ["draft", "validating", "blocked", "ready-for-review", "in-review", "approved", "changes-requested", "active", "suspended", "superseded", "retired"], evidence: "Formation snapshot, run artifacts, and attestations", context: ["entity", "book", "portfolio"], timeRoles: ["asOf", "calculatedAt", "evidenceAt"] },
];
const BY_ID = OBJECT_TYPES.reduce((map, type) => { map[type.id] = type; return map; }, {});

/** The ledger entry for a type id; a permissive fallback so an unknown type still renders. */
export function objectMeta(type) {
  return BY_ID[type] || { id: type, label: type || "Object", identifierLabel: "Id", nameLabel: "Name", material: "Value", lifecycle: [], context: [] };
}
/** Normalizes any record into the identity contract, so a header, a chip, a breadcrumb, and a
 *  search result describe the same object the same way. */
export function describeObject(object = {}) {
  const meta = objectMeta(object.type);
  return {
    type: meta.id, typeLabel: meta.label,
    identifierLabel: object.identifierLabel || meta.identifierLabel,
    id: object.id ?? null,
    name: object.name ?? null,
    context: normalizeContext(object.context),
    condition: object.condition ?? null,
    asOf: object.asOf ?? null,
    value: object.value ?? null,
    version: object.version ?? null,
    coverage: normalizeCoverage(object.coverage),
    exceptions: normalizeExceptions(object.exceptions),
    materialLabel: (object.value && object.value.label) || meta.material,
  };
}
function normalizeContext(context) {
  if (!context) return [];
  if (Array.isArray(context)) return context.filter(Boolean);
  return Object.keys(context).map((key) => ({ label: LABELS[key] || key, value: context[key] })).filter((pair) => pair.value != null && pair.value !== "");
}
const LABELS = { entity: "Entity", legalEntity: "Legal entity", portfolio: "Portfolio", book: "Book", basis: "Basis", currency: "Currency", period: "Period", scenario: "Scenario", custodian: "Custodian", owner: "Owner" };
function normalizeCoverage(coverage) {
  if (coverage == null) return null;
  if (typeof coverage === "string") return { text: coverage, complete: null };
  const { attached, required, label } = coverage;
  if (attached == null || required == null) return { text: label || String(coverage), complete: null };
  return { text: `${attached} of ${required}${label ? " " + label : ""}`, complete: attached >= required };
}
function normalizeExceptions(exceptions) {
  if (exceptions == null) return null;
  if (typeof exceptions === "number") return { count: exceptions, blocking: exceptions > 0 };
  return { count: exceptions.count ?? 0, blocking: exceptions.blocking ?? (exceptions.count ?? 0) > 0, label: exceptions.label };
}

const CONDITION_COLOR = {
  confirmed: "var(--green,#2E7655)", reconciled: "var(--green,#2E7655)",
  provisional: "var(--pending,#7A6B52)", pending: "var(--pending,#7A6B52)",
  estimated: "var(--orange,#986717)", stale: "var(--orange,#986717)", overridden: "var(--orange,#986717)",
  exception: "var(--red,#B54B42)", missing: "var(--red,#B54B42)",
  na: "var(--text-disabled,#A4ABB2)",
};

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "object-header");
  el.textContent = `
.mds-objh{display:grid;gap:8px;padding:12px 0 10px;border-bottom:1px solid var(--border-strong,#AAA49A);font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-objh--dense{padding:8px 0 7px;gap:6px}
.mds-objh__ident{display:flex;align-items:baseline;gap:10px;flex-wrap:wrap}
.mds-objh__type{font:600 11px var(--font-body);color:var(--text-muted,#606870);border:1px solid var(--border,#D2CDC3);border-radius:var(--radius-chip,2px);padding:1px 7px;white-space:nowrap}
.mds-objh__name{font:600 18px var(--font-display);color:var(--text-primary,#20252B);margin:0;text-wrap:pretty}
.mds-objh--dense .mds-objh__name{font-size:15px}
.mds-objh__key{font:12px var(--font-data,monospace);color:var(--text-secondary,#535C65);font-variant-numeric:tabular-nums;white-space:nowrap}
.mds-objh__key b{color:var(--text-primary,#20252B);font-weight:600}
.mds-objh__ver{font:600 11px var(--font-data,monospace);color:var(--text-muted,#606870);white-space:nowrap}
.mds-objh__facts{display:flex;gap:20px;flex-wrap:wrap;margin:0}
.mds-objh__fact{display:grid;gap:1px;align-content:start;border:0;background:none;padding:0;text-align:left;font-family:inherit;min-width:0}
button.mds-objh__fact{cursor:pointer}
button.mds-objh__fact:focus-visible{outline:2px solid var(--interaction-focus,#2D6FB3);outline-offset:2px}
button.mds-objh__fact:hover .mds-objh__v{text-decoration:underline;text-underline-offset:2px}
.mds-objh__l{font:11px var(--font-body);color:var(--text-muted,#606870);white-space:nowrap}
.mds-objh__v{font:600 13px var(--font-data,monospace);color:var(--text-primary,#20252B);font-variant-numeric:tabular-nums;white-space:nowrap}
.mds-objh__fact[data-material="on"] .mds-objh__v{font-size:16px}
.mds-objh__fact[data-flagged="on"] .mds-objh__v{color:var(--red-dim,#B54B42)}
.mds-objh__cond{display:inline-flex;align-items:center;gap:6px}
.mds-objh__dot{width:7px;height:7px;border-radius:50%;background:var(--text-disabled,#A4ABB2);flex:none}
.mds-objh__sub{font:11px var(--font-data,monospace);color:var(--text-muted,#606870)}
@media (max-width:720px){.mds-objh__facts{gap:14px}}
`;
  document.head.appendChild(el);
}

function Fact({ label, value, sub, flagged, material, onClick, children }) {
  const Tag = onClick ? "button" : "div";
  return <Tag type={onClick ? "button" : undefined} className="mds-objh__fact" onClick={onClick} data-flagged={flagged ? "on" : "off"} data-material={material ? "on" : "off"}>
    <span className="mds-objh__l">{label}</span>
    {children ?? <span className="mds-objh__v">{value ?? "—"}</span>}
    {sub ? <span className="mds-objh__sub">{sub}</span> : null}
  </Tag>;
}

/** The identity contract, rendered. Pass an object in the shape `describeObject` accepts; every
 *  facet the object does not carry is simply absent. Not a card — no shadow, no fill, one rule. */
export function ObjectHeader({
  type, id, name, identifierLabel, context, condition, asOf, value, version, coverage, exceptions,
  onOpenEvidence, onOpenExceptions, dense = false, className = "", ...rest
}) {
  inject();
  const o = describeObject({ type, id, name, identifierLabel, context, condition, asOf, value, version, coverage, exceptions });
  const amount = o.value && typeof o.value === "object" ? o.value : (o.value != null ? { amount: o.value } : null);
  return <header className={`mds-objh${dense ? " mds-objh--dense" : ""}${className ? " " + className : ""}`} data-object-type={o.type} {...rest}>
    <div className="mds-objh__ident">
      <span className="mds-objh__type">{o.typeLabel}</span>
      {o.name ? <h2 className="mds-objh__name">{o.name}</h2> : null}
      {o.id != null ? <span className="mds-objh__key">{o.identifierLabel} <b>{o.id}</b></span> : null}
      {o.version != null ? <span className="mds-objh__ver">Version {o.version}</span> : null}
    </div>
    <div className="mds-objh__facts">
      {o.context.map((pair) => <Fact key={pair.label} label={pair.label} value={pair.value} />)}
      {amount ? <Fact label={o.materialLabel} value={amount.amount} sub={amount.secondary} material /> : null}
      {o.condition ? <Fact label="Condition">
        <span className="mds-objh__v mds-objh__cond"><span className="mds-objh__dot" style={{ background: CONDITION_COLOR[o.condition] || CONDITION_COLOR.na }} />{o.condition}</span>
      </Fact> : null}
      {o.asOf ? <Fact label="As of" value={o.asOf} /> : null}
      {o.coverage ? <Fact label="Evidence" value={o.coverage.text} flagged={o.coverage.complete === false} onClick={onOpenEvidence} /> : null}
      {o.exceptions ? <Fact label={o.exceptions.label || "Open exceptions"} value={o.exceptions.count} flagged={o.exceptions.blocking} onClick={onOpenExceptions} /> : null}
    </div>
  </header>;
}

/** Bundle carrier — the compiler exposes only capitalized exports, so consumers reach the helpers
 *  through this object: `const { ObjectHooks } = window.<Namespace>;` */
export const ObjectHooks = { describeObject, objectMeta, OBJECT_TYPES };
/** Module carrier — `ObjectModel.Header`, `ObjectModel.describe`, `ObjectModel.TYPES`. */
export const ObjectModel = { Header: ObjectHeader, describe: describeObject, meta: objectMeta, TYPES: OBJECT_TYPES };
