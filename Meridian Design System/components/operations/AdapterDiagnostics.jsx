// Meridian adapter diagnostics — the outbound counterpart to ConnectionState.
// 4.1.0: track F of the coverage debt (CONTRACT_AUDIT §A-5, §D-2). Five postures:
// linked · degraded · retrying · suspended · unconfigured. A failure without a next attempt is
// a suspension, and this component says so rather than hiding it.
// Law: guidelines/ADAPTER_SEMANTICS.md
import React from "react";
import { SeverityBadge } from "./SeverityBadge";

export const ADAPTER_POSTURES = [
  { id: "linked", label: "Linked", severity: "ready", says: "Round trips succeeding inside budget." },
  { id: "degraded", label: "Degraded", severity: "action", says: "Succeeding outside budget, or partially." },
  { id: "retrying", label: "Retrying", severity: "blocked", says: "Failing, with a schedule in force." },
  { id: "suspended", label: "Suspended", severity: "review", says: "Will not retry until an operator acts." },
  { id: "unconfigured", label: "Unconfigured", severity: "info", says: "No route or credential. Not a failure." },
];
const BY_ID = ADAPTER_POSTURES.reduce((m, p) => { m[p.id] = p; return m; }, {});
const ORDER = { retrying: 0, degraded: 1, suspended: 2, linked: 3, unconfigured: 4 };

export function adapterPosture(id) { return BY_ID[String(id || "").toLowerCase()] || BY_ID.degraded; }
/** Rule 1 — `retrying` with no next attempt is a contradiction, not a spinner. */
export function adapterScheduleMissing(row) {
  return String(row?.posture || "").toLowerCase() === "retrying" && !(row?.retry && row.retry.nextAt);
}

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "adapter-diagnostics");
  el.textContent = `
.mds-adpt{min-width:0;max-width:100%;box-sizing:border-box;overflow-wrap:anywhere;font-family:var(--font-body);color:var(--text-primary,#20252B);border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3)}
.mds-adpt__head{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:6px 14px;align-items:end;padding:11px 13px;border-bottom:1px solid var(--border-strong,#AAA49A);background:var(--bg-medium,#EBE8E1)}
.mds-adpt__eyebrow{font:600 9px var(--font-data,monospace);letter-spacing:.06em;text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-adpt__title{font-size:14px;font-weight:650;margin:2px 0 0;letter-spacing:-.005em}
.mds-adpt__sub{font:10.5px var(--font-data,monospace);color:var(--text-secondary,#535C65);margin-top:3px}
.mds-adpt__tally{display:flex;flex-wrap:wrap;gap:0;align-self:end}
.mds-adpt__t{display:grid;gap:1px;justify-items:end;padding:0 0 0 16px}
.mds-adpt__t b{font:600 15px var(--font-data,monospace);font-variant-numeric:tabular-nums;line-height:1.1}
.mds-adpt__t span{font:600 9px var(--font-data,monospace);letter-spacing:.05em;text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-adpt__t[data-sev="blocked"] b{color:var(--severity-blocked-fg,#883832)}
.mds-adpt__t[data-sev="action"] b{color:var(--severity-action-fg,#724D11)}
.mds-adpt__t[data-sev="review"] b{color:var(--severity-review-fg,#944A32)}
.mds-adpt__t[data-sev="ready"] b{color:var(--severity-ready-fg,#225840)}
.mds-adpt__cols,.mds-adpt__row{display:grid;grid-template-columns:minmax(64px,.8fr) minmax(90px,1.3fr) minmax(60px,.75fr) minmax(70px,.9fr) minmax(70px,.9fr) minmax(75px,1fr);gap:10px;align-items:baseline}
.mds-adpt__cols{padding:5px 13px;border-bottom:1px solid var(--border-divider,#E2DED5);background:var(--card-surface-raised,#FCFBF8);font:600 9px var(--font-data,monospace);letter-spacing:.05em;text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-adpt__cols span:nth-child(n+3){text-align:right}
.mds-adpt__row{box-sizing:border-box;width:100%;padding:8px 13px;min-height:var(--row-review,40px);border:0;border-bottom:1px solid var(--border-divider,#E2DED5);border-left:2px solid transparent;background:transparent;text-align:left;font-family:inherit;color:inherit;cursor:pointer}
.mds-adpt__row:hover{background:var(--bg-hover,#EDEFF0)}
.mds-adpt__row:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-adpt__row[aria-expanded="true"]{background:var(--interaction-selected-surface,var(--selected-surface,#E7EDF2));border-left-color:var(--interaction-selected,#496C8F)}
.mds-adpt__row[aria-expanded="true"] em,.mds-adpt__row[aria-expanded="true"] s{color:var(--text-primary,#20252B)}
.mds-adpt__row--dense{padding:5px 13px;min-height:0}
.mds-adpt__name{display:grid;gap:1px;min-width:0}
.mds-adpt__name b{font-size:12.5px;font-weight:600;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.mds-adpt__name em{font-style:normal;font:600 9px var(--font-data,monospace);letter-spacing:.05em;text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-adpt__n{font:11px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-secondary,#535C65);text-align:right;min-width:0;overflow-wrap:anywhere;white-space:normal}
.mds-adpt__n b{font-weight:600;color:var(--text-primary,#20252B)}
.mds-adpt__n s{text-decoration:none;display:block;font-size:9.5px;color:var(--text-muted,#606870)}
.mds-adpt__n[data-over="true"] b{color:var(--severity-action-fg,#724D11)}
.mds-adpt__n[data-bad="true"] b{color:var(--severity-blocked-fg,#883832)}
.mds-adpt__detail{padding:11px 13px 12px;border-bottom:1px solid var(--border-divider,#E2DED5);background:var(--card-surface-raised,#FCFBF8);display:grid;grid-template-columns:repeat(auto-fit,minmax(min(228px,100%),1fr));gap:12px 18px}
.mds-adpt__block{min-width:0;display:grid;gap:4px;align-content:start}
.mds-adpt__blabel{font:600 9px var(--font-data,monospace);letter-spacing:.06em;text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-adpt__kv{display:grid;grid-template-columns:auto 1fr;gap:2px 10px;font:11px var(--font-data,monospace);color:var(--text-secondary,#535C65)}
.mds-adpt__kv dt{color:var(--text-muted,#606870)}
.mds-adpt__kv dd{min-width:0;overflow-wrap:anywhere;margin:0;color:var(--text-primary,#20252B);font-variant-numeric:tabular-nums}
.mds-adpt__err{font:11px var(--font-data,monospace);line-height:1.5;color:var(--severity-blocked-fg,#883832);background:linear-gradient(var(--severity-blocked-bg,rgba(181,75,66,.10)),var(--severity-blocked-bg,rgba(181,75,66,.10))),var(--bg-light,#F8F7F3);border:1px solid var(--severity-blocked-bd,rgba(181, 75, 66, 0.4));padding:6px 8px;white-space:pre-wrap;word-break:break-word}
.mds-adpt__errmeta{font:9.5px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-adpt__acts{display:flex;flex-wrap:wrap;gap:6px;align-items:center}
.mds-adpt__btn{height:26px;padding:0 10px;border:1px solid var(--border-strong,#AAA49A);border-radius:2px;background:var(--bg-light,#F8F7F3);color:var(--text-primary,#20252B);font:600 11px var(--font-body);cursor:pointer;white-space:nowrap}
.mds-adpt__btn:hover:not(:disabled){background:var(--bg-hover,#EDEFF0)}
.mds-adpt__btn:disabled{opacity:.5;cursor:not-allowed}
.mds-adpt__btn:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px}
.mds-adpt__says{font-size:11.5px;line-height:1.45;color:var(--text-secondary,#535C65);text-wrap:pretty}
.mds-adpt__flag{grid-column:1/-1;font:600 10.5px var(--font-data,monospace);color:var(--severity-blocked-fg,#883832);background:linear-gradient(var(--severity-blocked-bg,rgba(181,75,66,.10)),var(--severity-blocked-bg,rgba(181,75,66,.10))),var(--bg-light,#F8F7F3);border-left:2px solid var(--severity-blocked-fg,#883832);padding:5px 8px}
.mds-adpt__stale{grid-column:1/-1;font:10.5px var(--font-data,monospace);color:var(--severity-action-fg,#724D11)}
.mds-adpt__foot{padding:7px 13px;border-top:1px solid var(--border-divider,#E2DED5);font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-adpt__empty{padding:16px 13px;font-size:12px;color:var(--text-secondary,#535C65)}
.mds-apc{display:inline-flex;align-items:center;gap:5px;height:20px;padding:0 7px;border:1px solid var(--border,#D2CDC3);border-radius:2px;background:var(--bg-light,#F8F7F3);font:600 10px var(--font-data,monospace);letter-spacing:.04em;text-transform:uppercase;color:var(--text-secondary,#535C65);white-space:nowrap}
.mds-apc__dot{width:6px;height:6px;border-radius:50%;background:var(--text-muted,#606870)}
.mds-apc[data-sev="blocked"]{color:var(--severity-blocked-fg,#883832);border-color:var(--severity-blocked-bd,rgba(181, 75, 66, 0.4));background:linear-gradient(var(--severity-blocked-bg,rgba(181,75,66,.10)),var(--severity-blocked-bg,rgba(181,75,66,.10))),var(--bg-light,#F8F7F3)}
.mds-apc[data-sev="blocked"] .mds-apc__dot{background:var(--severity-blocked-fg,#883832)}
.mds-apc[data-sev="action"]{color:var(--severity-action-fg,#724D11);border-color:var(--severity-action-bd,rgba(152, 103, 23, 0.42));background:linear-gradient(var(--severity-action-bg,rgba(152,103,23,.11)),var(--severity-action-bg,rgba(152,103,23,.11))),var(--bg-light,#F8F7F3)}
.mds-apc[data-sev="action"] .mds-apc__dot{background:var(--severity-action-fg,#724D11)}
.mds-apc[data-sev="review"]{color:var(--severity-review-fg,#944A32);border-color:var(--severity-review-bd,rgba(197, 99, 67, 0.36));background:linear-gradient(var(--severity-review-bg,rgba(197,99,67,.10)),var(--severity-review-bg,rgba(197,99,67,.10))),var(--bg-light,#F8F7F3)}
.mds-apc[data-sev="review"] .mds-apc__dot{background:var(--severity-review-fg,#944A32)}
.mds-apc[data-sev="ready"]{color:var(--severity-ready-fg,#225840);border-color:var(--severity-ready-bd,rgba(46, 118, 85, 0.36));background:linear-gradient(var(--severity-ready-bg,rgba(46,118,85,.10)),var(--severity-ready-bg,rgba(46,118,85,.10))),var(--bg-light,#F8F7F3)}
.mds-apc[data-sev="ready"] .mds-apc__dot{background:var(--severity-ready-fg,#225840)}
.mds-apc.mds-apc{color:var(--text-primary,#20252B)}
.mds-apc__next{font-weight:400;text-transform:none;letter-spacing:0;color:var(--text-primary,#20252B)}
@media print{.mds-adpt__acts,.mds-adpt__btn{display:none}.mds-adpt__row{break-inside:avoid}.mds-adpt__detail{break-inside:avoid}}
`;
  document.head.appendChild(el);
}

export function AdapterPostureChip({ posture, label, nextAttempt, className = "", ...rest }) {
  inject();
  const p = adapterPosture(posture);
  return <span className={`mds-apc${className ? " " + className : ""}`} data-sev={p.severity}
    title={label || p.says} {...rest}>
    <span className="mds-apc__dot" />{label || p.label}
    {nextAttempt ? <span className="mds-apc__next">· next {nextAttempt}</span> : null}
  </span>;
}

export function AdapterDiagnostics({
  adapters = [], asOf, scope, budgetNote, title = "Outbound adapters",
  selectedId, defaultSelectedId, onSelect,
  onRetryNow, onSuspend, onResume, onRotateKey,
  dense = false, sortByPosture = true, emptyStatement, footNote,
  className = "", ...rest
}) {
  inject();
  const uid=React.useId();
  const [internal, setInternal] = React.useState(defaultSelectedId ?? null);
  const active = selectedId !== undefined ? selectedId : internal;
  const select = (id) => {
    const next = active === id ? null : id;
    if (selectedId === undefined) setInternal(next);
    onSelect?.(next);
  };

  const rows = sortByPosture
    ? adapters.slice().sort((a, b) => (ORDER[a.posture] ?? 9) - (ORDER[b.posture] ?? 9))
    : adapters;
  const tally = ADAPTER_POSTURES
    .map((p) => ({ ...p, n: adapters.filter((a) => a.posture === p.id).length }))
    .filter((p) => p.n);

  return <section className={`mds-adpt${className ? " " + className : ""}`} aria-label={title} {...rest}>
    <header className="mds-adpt__head">
      <div>
        <div className="mds-adpt__eyebrow">Adapters{scope ? ` · ${scope}` : ""}</div>
        <h2 className="mds-adpt__title">{title}</h2>
        <div className="mds-adpt__sub">
          {adapters.length} adapter{adapters.length === 1 ? "" : "s"}
          {asOf ? ` · as of ${asOf}` : ""}{budgetNote ? ` · ${budgetNote}` : ""}
        </div>
      </div>
      {tally.length ? <div className="mds-adpt__tally">
        {tally.map((p) => <div key={p.id} className="mds-adpt__t" data-sev={p.severity}><b>{p.n}</b><span>{p.label}</span></div>)}
      </div> : null}
    </header>

    {rows.length ? <>
      <div className="mds-adpt__cols" aria-hidden="true">
        <span>Posture</span><span>Adapter</span><span>Round trip</span><span>p99 · budget</span><span>Outbound</span><span>Next attempt</span>
      </div>
      {rows.map((row) => {
        const open = active === row.id;
        const p = adapterPosture(row.posture);
        const noSchedule = adapterScheduleMissing(row);
        const over = row.latency && row.latency.budget != null && row.latency.p99 != null && row.latency.p99 > row.latency.budget;
        const queued = row.outbound?.queued ?? 0;
        const detailId = `${uid}-detail-${row.id}`;
        const descriptionId = `${uid}-description-${row.id}`;
        // The column strip is decorative for AT (the rows are not a real table), so every row
        // carries its own composed name: posture, identity, and each number with its meaning.
        const name = [
          `${row.name || row.id}${row.environment ? `, ${row.environment}` : ""}${row.venue ? `, venue ${row.venue}` : ""}`,
          `posture ${row.postureLabel || p.label}`,
          row.lastRoundTrip ? `last round trip ${row.lastRoundTrip}` : null,
          row.latency?.p99 != null ? `p99 ${row.latency.p99} milliseconds${row.latency.budget != null ? ` against a ${row.latency.budget} millisecond budget${over ? ", over budget" : ""}` : ""}` : null,
          row.outbound ? `${row.outbound.inFlight ?? 0} in flight, ${queued} queued${row.outbound.oldest ? `, oldest ${row.outbound.oldest}` : ""}` : null,
          noSchedule ? "retrying with no next attempt scheduled" : row.retry?.nextAt ? `next attempt ${row.retry.nextAt}` : null,
          row.key?.overdue ? "signing key rotation overdue" : null,
        ].filter(Boolean).join(" · ");
        return <React.Fragment key={row.id}>
          <button type="button" className={`mds-adpt__row${dense ? " mds-adpt__row--dense" : ""}`}
            aria-expanded={open} aria-controls={detailId} aria-describedby={descriptionId} onClick={() => select(row.id)}>
            <SeverityBadge status={p.severity} label={row.postureLabel || p.label} />
            <span className="mds-adpt__name">
              <b>{row.name || row.id}</b>
              <em>{[row.venue, row.environment, row.route].filter(Boolean).join(" · ") || row.id}</em>
            </span>
            <span className="mds-adpt__n"><b>{row.lastRoundTrip || "—"}</b>{row.cadence ? <s>{row.cadence}</s> : null}</span>
            <span className="mds-adpt__n" data-over={over ? "true" : undefined}>
              <b>{row.latency?.p99 != null ? `${row.latency.p99}ms` : "—"}</b>
              <s>{row.latency?.budget != null ? `budget ${row.latency.budget}ms` : ""}</s>
            </span>
            <span className="mds-adpt__n" data-bad={queued && row.outbound?.oldest ? "true" : undefined}>
              <b>{row.outbound ? `${row.outbound.inFlight ?? 0} / ${queued}` : "—"}</b>
              <s>{row.outbound?.oldest ? `oldest ${row.outbound.oldest}` : row.outbound ? "in flight / queued" : ""}</s>
            </span>
            <span className="mds-adpt__n" data-bad={noSchedule ? "true" : undefined}>
              <b>{row.retry?.nextAt || (noSchedule ? "none" : "—")}</b>
              <s>{row.retry ? `attempt ${row.retry.attempt ?? "?"}${row.retry.of ? ` of ${row.retry.of}` : ""}${row.retry.backoff ? ` · ${row.retry.backoff}` : ""}` : ""}</s>
            </span>
          </button>

          <span id={descriptionId} style={{position:"absolute",width:1,height:1,overflow:"hidden",clip:"rect(0 0 0 0)"}}>{name}</span>
          {open ? <div className="mds-adpt__detail" id={detailId} role="group" aria-label={`${row.name || row.id} — diagnostics`}>
            {noSchedule ? <p className="mds-adpt__flag">Retrying with no next attempt — this adapter is suspended in fact. Resume it or state the schedule.</p> : null}
            {row.versionStale ? <p className="mds-adpt__stale">Row version {row.version} has moved server-side — mutating controls are disabled until it is refetched.</p> : null}
            <div className="mds-adpt__block">
              <span className="mds-adpt__blabel">Posture</span>
              <p className="mds-adpt__says">{row.statement || p.says}</p>
              <dl className="mds-adpt__kv">
                {row.venue ? <><dt>Venue</dt><dd>{row.venue}{row.venueStatus ? ` · ${row.venueStatus}` : ""}</dd></> : null}
                {row.environment ? <><dt>Environment</dt><dd>{row.environment}</dd></> : null}
                {row.suspendedBy ? <><dt>Suspended by</dt><dd>{row.suspendedBy}{row.suspendedAt ? ` · ${row.suspendedAt}` : ""}</dd></> : null}
                {row.version != null ? <><dt>Version</dt><dd>{row.version}</dd></> : null}
              </dl>
            </div>
            <div className="mds-adpt__block">
              <span className="mds-adpt__blabel">Retry schedule</span>
              {row.retry ? <dl className="mds-adpt__kv">
                <dt>Attempt</dt><dd>{row.retry.attempt ?? "—"}{row.retry.of ? ` of ${row.retry.of}` : ""}</dd>
                <dt>Backoff</dt><dd>{row.retry.backoff || "—"}</dd>
                <dt>Next</dt><dd>{row.retry.nextAt || "none"}</dd>
                {row.retry.lastAttemptAt ? <><dt>Last</dt><dd>{row.retry.lastAttemptAt}</dd></> : null}
                {row.retry.onExhaustion ? <><dt>On exhaustion</dt><dd>{row.retry.onExhaustion}</dd></> : null}
              </dl> : <p className="mds-adpt__says">No schedule in force.</p>}
              {row.outbound ? <dl className="mds-adpt__kv">
                <dt>In flight</dt><dd>{row.outbound.inFlight ?? 0}</dd>
                <dt>Queued</dt><dd>{queued}{row.outbound.oldest ? ` · oldest ${row.outbound.oldest}` : ""}</dd>
                {row.outbound.onResume ? <><dt>On resume</dt><dd>{row.outbound.onResume}</dd></> : null}
              </dl> : null}
            </div>
            <div className="mds-adpt__block">
              <span className="mds-adpt__blabel">Last error</span>
              {row.lastError ? <>
                <p className="mds-adpt__err">{row.lastError.code ? `${row.lastError.code} — ` : ""}{row.lastError.message}</p>
                <span className="mds-adpt__errmeta">
                  {row.lastError.at ? `${row.lastError.at}` : ""}
                  {row.lastError.count ? ` · ${row.lastError.count} occurrence${row.lastError.count === 1 ? "" : "s"}` : ""}
                  {row.lastError.correlationId ? ` · ${row.lastError.correlationId}` : ""}
                </span>
              </> : <p className="mds-adpt__says">No error since the last successful round trip.</p>}
            </div>
            <div className="mds-adpt__block">
              <span className="mds-adpt__blabel">Request signing</span>
              {row.key ? <dl className="mds-adpt__kv">
                <dt>Key</dt><dd>{row.key.id}</dd>
                {row.key.algorithm ? <><dt>Alg</dt><dd>{row.key.algorithm}</dd></> : null}
                <dt>Age</dt><dd>{row.key.age || "—"}</dd>
                <dt>{row.key.overdue ? "Expired" : "Rotates"}</dt>
                <dd style={row.key.overdue ? { color: "var(--severity-blocked-fg,#883832)", fontWeight: 600 } : undefined}>
                  {row.key.rotatesAt || "—"}{row.key.overdue ? " · overdue" : ""}
                </dd>
              </dl> : <p className="mds-adpt__says">Unsigned route.</p>}
              {(onRetryNow || onSuspend || onResume || onRotateKey) ? <div className="mds-adpt__acts">
                {onRetryNow ? <button type="button" className="mds-adpt__btn" disabled={row.versionStale || row.posture === "unconfigured"} onClick={() => onRetryNow(row)}>Retry now</button> : null}
                {row.posture === "suspended"
                  ? onResume ? <button type="button" className="mds-adpt__btn" disabled={row.versionStale} onClick={() => onResume(row)}>Resume</button> : null
                  : onSuspend ? <button type="button" className="mds-adpt__btn" disabled={row.versionStale || row.posture === "unconfigured"} onClick={() => onSuspend(row)}>Suspend</button> : null}
                {onRotateKey && row.key ? <button type="button" className="mds-adpt__btn" disabled={row.versionStale} onClick={() => onRotateKey(row)}>Rotate key</button> : null}
              </div> : null}
            </div>
          </div> : null}
        </React.Fragment>;
      })}
    </> : <p className="mds-adpt__empty">{emptyStatement || "No outbound adapters configured for this environment."}</p>}
    {footNote ? <div className="mds-adpt__foot">{footNote}</div> : null}
  </section>;
}

/** Bundle carrier — the compiler exposes only capitalized exports. */
export const AdapterHooks = { adapterPosture, adapterScheduleMissing };
