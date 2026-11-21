// Meridian EventTimeline — vertical audit trail. Where DiffView shows one change, the
// timeline shows the sequence: who did what, when, with what evidence. Severity-dotted rail,
// mono UTC times, optional day grouping. Flat — the rail is a border, not a decoration.
import React from "react";
import { Timestamp } from "../core/Timestamp";
import { EvidenceLink } from "./EvidenceLink";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-evtl{font-family:var(--font-body);display:flex;flex-direction:column;}
.mds-evtl__day{font-family:var(--font-data);font-size:10px;font-weight:700;letter-spacing:.05em;
  color:var(--text-muted,#606870);padding:10px 0 6px 0;}
.mds-evtl__day:first-child{padding-top:0;}
.mds-evtl__row{display:flex;gap:10px;position:relative;}
.mds-evtl__time{flex:0 0 66px;text-align:right;font-size:11px;padding-top:1px;color:var(--text-muted,#606870);}
.mds-evtl__rail{flex:0 0 auto;display:flex;flex-direction:column;align-items:center;width:9px;}
.mds-evtl__dot{width:9px;height:9px;border-radius:50%;flex:0 0 auto;margin-top:4px;
  background:var(--bg-light,#F8F7F3);border:2px solid var(--border-strong,#AAA49A);box-sizing:border-box;}
.mds-evtl__dot--success{border-color:var(--green,#2E7655);}
.mds-evtl__dot--warning{border-color:var(--orange,#986717);}
.mds-evtl__dot--danger{border-color:var(--red,#B54B42);background:var(--red-a10,rgba(181,75,66,.1));}
.mds-evtl__dot--accent{border-color:var(--brand-signal,#C56343);}
.mds-evtl__stem{flex:1;width:1px;background:var(--border,#D2CDC3);min-height:8px;}
.mds-evtl__row:last-child .mds-evtl__stem{background:transparent;}
.mds-evtl__body{flex:1;min-width:0;padding-bottom:14px;}
.mds-evtl--dense .mds-evtl__body{padding-bottom:8px;}
.mds-evtl__action{font-size:13px;font-weight:600;color:var(--text-primary,#20252B);line-height:1.4;}
.mds-evtl__actor{font-family:var(--font-data);font-size:10.5px;color:var(--text-secondary,#535C65);
  border:1px solid var(--border,#D2CDC3);border-radius:var(--radius-chip,2px);padding:0 5px;margin-left:7px;
  vertical-align:1px;white-space:nowrap;}
.mds-evtl__detail{font-size:12px;color:var(--text-secondary,#535C65);line-height:1.5;margin-top:2px;}
.mds-evtl__evidence{margin-top:4px;font-size:11.5px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "event-timeline");
  el.textContent = css;
  document.head.appendChild(el);
}

// Group by the OPERATOR's day, not the UTC day: toISOString() would file a 7pm New York event
// under tomorrow's heading. See core/localDate.js.
function dayKey(v) {
  const d = v instanceof Date ? v : new Date(v);
  if (isNaN(d.getTime())) return "—";
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

export function EventTimeline({
  events = [],
  groupByDay = true,
  dense = false,
  className = "",
  ...rest
}) {
  inject();
  const rows = [];
  let lastDay = null;
  for (const ev of events) {
    const day = dayKey(ev.ts);
    if (groupByDay && day !== lastDay) {
      rows.push({ type: "day", day, key: `day-${day}-${rows.length}` });
      lastDay = day;
    }
    rows.push({ type: "event", ev, key: ev.id || `ev-${rows.length}` });
  }
  return (
    <div className={`mds-evtl${dense ? " mds-evtl--dense" : ""}${className ? " " + className : ""}`}
      role="list" aria-label="Event timeline" {...rest}>
      {rows.length === 0 && (
        <div style={{ font: "12px var(--font-body)", color: "var(--text-muted,#606870)" }}>No events recorded.</div>
      )}
      {rows.map((r) =>
        r.type === "day" ? (
          <div key={r.key} className="mds-evtl__day">{r.day}</div>
        ) : (
          <div key={r.key} className="mds-evtl__row" role="listitem">
            <div className="mds-evtl__time">
              <Timestamp value={r.ev.ts} format="time" />
            </div>
            <div className="mds-evtl__rail">
              <span className={`mds-evtl__dot${r.ev.severity ? ` mds-evtl__dot--${r.ev.severity}` : ""}`} aria-hidden="true" />
              <span className="mds-evtl__stem" aria-hidden="true" />
            </div>
            <div className="mds-evtl__body">
              <div className="mds-evtl__action">
                {r.ev.action}
                {r.ev.actor && <span className="mds-evtl__actor">{r.ev.actor}</span>}
              </div>
              {r.ev.detail && <div className="mds-evtl__detail">{r.ev.detail}</div>}
              {r.ev.evidence && (
                <div className="mds-evtl__evidence">
                  <EvidenceLink label={r.ev.evidence.label} href={r.ev.evidence.href} onOpen={r.ev.evidence.onOpen} status={r.ev.evidence.status} route={r.ev.evidence.route} />
                </div>
              )}
            </div>
          </div>
        )
      )}
    </div>
  );
}

EventTimeline.displayName = "EventTimeline";
