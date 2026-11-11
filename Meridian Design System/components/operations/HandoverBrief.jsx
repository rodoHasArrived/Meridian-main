// Meridian handover brief — what moved on the operator's objects since the last-seen mark.
// 4.1.0: track A of session continuity. Four kinds in fixed order: breached → assigned →
// changed → resolved. Acknowledgement moves the mark and nothing else.
import React from "react";
import { SeverityBadge } from "./SeverityBadge";

const KINDS = [
  { id: "breached", label: "Breached while you were out", fallback: "Blocked" },
  { id: "assigned", label: "Now yours", fallback: "ReviewRequired" },
  { id: "changed", label: "Changed under you", fallback: "NeedsAttention" },
  { id: "resolved", label: "Resolved without you", fallback: "Ready" },
];
const SEVERITY_ORDER = { blocked: 0, action: 1, review: 2, info: 3, ready: 4 };
function severityOf(status) {
  const s = String(status || "").toLowerCase();
  if (/block|critical|fail|reject|breach/.test(s)) return "blocked";
  if (/attention|warn|degrad|exception|stale|overdue/.test(s)) return "action";
  if (/review|progress|submit|running|assign/.test(s)) return "review";
  if (/ready|pass|health|complete|approv|certif|resolv|clear/.test(s)) return "ready";
  return "info";
}

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "handover-brief");
  el.textContent = `
.mds-hand{font-family:var(--font-body);color:var(--text-primary,#20252B);border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3)}
.mds-hand__head{display:grid;grid-template-columns:1fr auto;gap:8px 14px;align-items:end;padding:11px 13px;border-bottom:1px solid var(--border-strong,#AAA49A);background:var(--bg-medium,#EBE8E1)}
.mds-hand__eyebrow{font:600 9px var(--font-data,monospace);letter-spacing:.06em;text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-hand__title{font-size:14px;font-weight:650;margin:2px 0 0;letter-spacing:-.005em}
.mds-hand__window{font:10.5px var(--font-data,monospace);color:var(--text-secondary,#535C65);margin-top:3px}
.mds-hand__ack{height:26px;padding:0 11px;border:1px solid var(--border-strong,#AAA49A);border-radius:2px;background:var(--bg-light,#F8F7F3);color:var(--text-primary,#20252B);font:600 11px var(--font-body);cursor:pointer;white-space:nowrap}
.mds-hand__ack:hover{background:var(--bg-hover,#EDEFF0)}
.mds-hand__ack:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px}
.mds-hand__acked{font:10.5px var(--font-data,monospace);color:var(--text-muted,#606870);text-align:right}
.mds-hand__counts{display:flex;flex-wrap:wrap;gap:0;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-hand__count{display:grid;gap:1px;padding:7px 13px;border-right:1px solid var(--border-divider,#E2DED5);min-width:96px}
.mds-hand__count b{font:600 15px var(--font-data,monospace);font-variant-numeric:tabular-nums;line-height:1.1}
.mds-hand__count span{font:600 9px var(--font-data,monospace);letter-spacing:.05em;text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-hand__count[data-sev="blocked"] b{color:var(--severity-blocked-fg,#883832)}
.mds-hand__count[data-sev="action"] b{color:var(--severity-action-fg,#724D11)}
.mds-hand__count[data-sev="review"] b{color:var(--severity-review-fg,#8E4730)}
.mds-hand__count[data-sev="ready"] b{color:var(--severity-ready-fg,#225840)}
.mds-hand__group{display:block}
.mds-hand__glabel{display:flex;align-items:baseline;justify-content:space-between;gap:10px;padding:6px 13px;background:var(--card-surface-raised,#FCFBF8);border-bottom:1px solid var(--border-divider,#E2DED5);font:600 9.5px var(--font-data,monospace);letter-spacing:.06em;text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-hand__row{display:grid;grid-template-columns:78px 1fr auto;gap:3px 12px;align-items:start;width:100%;padding:9px 13px;min-height:var(--row-review,44px);border:0;border-bottom:1px solid var(--border-divider,#E2DED5);background:transparent;text-align:left;font-family:inherit;color:inherit}
button.mds-hand__row{cursor:pointer}
button.mds-hand__row:hover{background:var(--bg-hover,#EDEFF0)}
button.mds-hand__row:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-hand__row--dense{padding:6px 13px;min-height:0}
.mds-hand__obj{display:grid;gap:1px;min-width:0}
.mds-hand__obj b{font-size:12.5px;font-weight:600;overflow:hidden;text-overflow:ellipsis}
.mds-hand__obj em{font-style:normal;font:600 9px var(--font-data,monospace);letter-spacing:.05em;text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-hand__say{grid-column:2;font-size:11.5px;line-height:1.45;color:var(--text-secondary,#535C65);text-wrap:pretty}
.mds-hand__when{display:grid;gap:1px;justify-items:end;font:10px var(--font-data,monospace);color:var(--text-muted,#606870);white-space:nowrap}
.mds-hand__when b{font-weight:600;color:var(--text-secondary,#535C65)}
.mds-hand__act{grid-column:3;grid-row:2;justify-self:end;border:0;background:transparent;color:var(--link,#2A5F96);font:600 11px var(--font-body);cursor:pointer;padding:0}
.mds-hand__act:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px}
.mds-hand__empty{padding:16px 13px;font-size:12px;line-height:1.5;color:var(--text-secondary,#535C65)}
.mds-hand__foot{padding:7px 13px;border-top:1px solid var(--border-divider,#E2DED5);font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-hchip{display:inline-flex;align-items:center;gap:6px;height:22px;padding:0 8px;border:1px solid var(--border,#D2CDC3);border-radius:2px;background:var(--bg-light,#F8F7F3);font:600 10.5px var(--font-data,monospace);letter-spacing:.03em;color:var(--text-secondary,#535C65);cursor:pointer}
.mds-hchip:hover{background:var(--bg-hover,#EDEFF0);color:var(--text-primary,#20252B)}
.mds-hchip:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px}
.mds-hchip__dot{width:6px;height:6px;border-radius:50%;background:var(--text-muted,#606870)}
.mds-hchip[data-sev="blocked"] .mds-hchip__dot{background:var(--severity-blocked-fg,#883832)}
.mds-hchip[data-sev="action"] .mds-hchip__dot{background:var(--severity-action-fg,#724D11)}
.mds-hchip[data-sev="review"] .mds-hchip__dot{background:var(--severity-review-fg,#8E4730)}
@media print{.mds-hand__ack,.mds-hand__act{display:none}.mds-hand__row{break-inside:avoid}}
`;
  document.head.appendChild(el);
}

export function HandoverBrief({
  since, until = "now", elapsed, actor, items = [], owned, severityFloor,
  onOpen, onAcknowledge, acknowledgedAt, dense = false, kinds = KINDS,
  title = "Since you were last here", emptyStatement, footNote, className = "", ...rest
}) {
  inject();
  const floor = severityFloor ? SEVERITY_ORDER[severityFloor] ?? 3 : null;
  const visible = items.filter((item) => (floor == null ? true : (SEVERITY_ORDER[severityOf(item.status || item.kind)] ?? 3) <= floor));
  const grouped = kinds.map((kind) => ({
    ...kind,
    rows: visible.filter((item) => item.kind === kind.id)
      .slice()
      .sort((a, b) => (SEVERITY_ORDER[severityOf(a.status || kind.fallback)] ?? 3) - (SEVERITY_ORDER[severityOf(b.status || kind.fallback)] ?? 3)),
  })).filter((group) => group.rows.length);
  const counts = kinds.map((kind) => ({ id: kind.id, label: kind.id, n: visible.filter((i) => i.kind === kind.id).length, sev: severityOf(kind.fallback) })).filter((c) => c.n);

  return <section className={`mds-hand${className ? " " + className : ""}`} aria-label="Handover brief" {...rest}>
    <header className="mds-hand__head">
      <div>
        <div className="mds-hand__eyebrow">Handover{actor ? ` · ${actor}` : ""}</div>
        <h2 className="mds-hand__title">{title}</h2>
        <div className="mds-hand__window">Since {since || "—"} → {until}{elapsed ? ` · ${elapsed}` : ""}{owned != null ? ` · ${owned} objects you own` : ""}</div>
      </div>
      {onAcknowledge && !acknowledgedAt
        ? <button type="button" className="mds-hand__ack" onClick={onAcknowledge}>Acknowledge · move mark</button>
        : acknowledgedAt ? <div className="mds-hand__acked">Mark moved<br />{acknowledgedAt}</div> : null}
    </header>
    {counts.length ? <div className="mds-hand__counts">
      {counts.map((c) => <div key={c.id} className="mds-hand__count" data-sev={c.sev}><b>{c.n}</b><span>{c.label}</span></div>)}
    </div> : null}
    {grouped.length ? grouped.map((group) => <div key={group.id} className="mds-hand__group">
      <div className="mds-hand__glabel"><span>{group.label}</span><span>{group.rows.length}</span></div>
      {group.rows.map((item, i) => {
        const interactive = !!(onOpen || item.action?.onClick);
        const Tag = interactive && !item.action ? "button" : "div";
        return <Tag key={item.id || `${group.id}-${i}`} type={Tag === "button" ? "button" : undefined}
          className={`mds-hand__row${dense ? " mds-hand__row--dense" : ""}`}
          onClick={Tag === "button" ? () => onOpen?.(item) : undefined}>
          <SeverityBadge status={item.status || group.fallback} label={item.statusLabel} />
          <span className="mds-hand__obj">
            <b>{item.object?.label ?? item.object?.id ?? item.label}</b>
            {item.object?.typeLabel || item.object?.type ? <em>{item.object.typeLabel || item.object.type}{item.object.id && item.object.label ? ` · ${item.object.id}` : ""}</em> : null}
          </span>
          <span className="mds-hand__when"><b>{item.at || "—"}</b>{item.actor ? <span>{item.actor}</span> : null}</span>
          <span className="mds-hand__say">{item.statement}</span>
          {item.action ? <button type="button" className="mds-hand__act" onClick={(e) => { e.stopPropagation(); item.action.onClick?.(item); }}>{item.action.label}</button>
            : onOpen && Tag === "div" ? <button type="button" className="mds-hand__act" onClick={() => onOpen(item)}>Open</button> : null}
        </Tag>;
      })}
    </div>) : <p className="mds-hand__empty">{emptyStatement || `Nothing moved on ${owned != null ? `your ${owned} objects` : "the objects you own"} since ${since || "your last session"}.`}</p>}
    {footNote ? <div className="mds-hand__foot">{footNote}</div> : null}
  </section>;
}

export function HandoverBriefChip({ count = 0, severity, since, label = "Handover", onClick, className = "", ...rest }) {
  inject();
  return <button type="button" className={`mds-hchip${className ? " " + className : ""}`} data-sev={severity || (count ? "action" : "info")}
    onClick={onClick} aria-description={`${count} handover item${count === 1 ? "" : "s"}`} {...rest}>
    <span className="mds-hchip__dot" aria-hidden="true" />{label} {count}{since ? <span> · since {since}</span> : null}
  </button>;
}
