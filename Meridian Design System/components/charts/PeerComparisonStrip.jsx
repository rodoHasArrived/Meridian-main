// Meridian PeerComparisonStrip — "where does this one sit?" for several metrics at once.
// Each row is one metric on its own domain: a peer-range rail, tick marks for every peer,
// a dashed median, and the subject's marker with its printed value and rank. The rows stack
// so a security's whole relative profile reads in one pass — cheap to scan, hard to misread,
// and it never implies a time series where there is none.
// Domains are per row (metrics are not commensurate) but the RAIL geometry is identical, so
// marker position is comparable as position-within-peers across rows.
import React from "react";

const MONO = { fontFamily: "var(--font-data)", fontVariantNumeric: "slashed-zero tabular-nums" };
const SR = { position: "absolute", width: 1, height: 1, padding: 0, margin: -1, overflow: "hidden", clip: "rect(0 0 0 0)", whiteSpace: "nowrap", border: 0 };

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "peer-comparison-strip");
  el.textContent = `
.mds-pcs__rail{appearance:none;display:block;position:relative;width:100%;height:22px;margin:0;padding:0;border:0;background:none;color:inherit;font:inherit;cursor:pointer;border-radius:var(--radius-button,2px);}
.mds-pcs__rail:focus-visible{outline:2px solid var(--interaction-focus,var(--border-focus,#2D6FB3));outline-offset:3px;}
.mds-pcs__detail{margin:2px 0 6px;padding:6px 8px;border-left:2px solid var(--border-strong);font-size:var(--viz-type-annotation,12px);color:var(--text-secondary);}
.mds-pcs__detail dl{display:flex;flex-wrap:wrap;gap:2px 16px;margin:0;}
.mds-pcs__detail dt{color:var(--text-muted);}
.mds-pcs__detail dd{margin:0 0 0 4px;font-family:var(--font-data);font-variant-numeric:slashed-zero tabular-nums;color:var(--text-primary);}
.mds-pcs__detail div{display:flex;}
@media (forced-colors:active){
  .mds-pcs__track,.mds-pcs__peer{background:CanvasText;forced-color-adjust:none;}
  .mds-pcs__subject{background:Highlight;border-color:Canvas;forced-color-adjust:none;}
  .mds-pcs__rail:focus-visible{outline-color:Highlight;}
}
`;
  document.head.appendChild(el);
}

const median = (a) => { const s = [...a].sort((x, y) => x - y); const m = s.length >> 1; return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2; };

// Finite peers in ascending order, each with the name the caller gave it (or its position).
const namedPeers = (m, f) => (m.peers || [])
  .map((value, i) => ({ value, name: (m.peerLabels && m.peerLabels[i]) || `Peer ${i + 1}` }))
  .filter((p) => p.value != null && isFinite(p.value))
  .sort((a, b) => a.value - b.value)
  .map((p) => ({ ...p, text: f(p.value) }));

export function PeerComparisonStrip({
  metrics = [],                // [{ id?, label, value, peers?: number[], domain?: [lo,hi], median?, fmt?, higherIsBetter?, note? }]
  subjectLabel = "Subject",
  labelWidth = 168,
  valueWidth = 92,
  showRank = true,
  fmt = (v) => (Math.abs(v) >= 100 ? v.toFixed(0) : v.toFixed(2)),
  ariaLabel,
  caption,
  peerDetail = true,           // each rail is a disclosure listing its peer values; false keeps a static strip
}) {
  inject();
  const base = React.useId();
  const [open, setOpen] = React.useState(null);
  return (
    <div role={ariaLabel ? "group" : undefined} aria-label={ariaLabel} style={{ position: "relative", display: "flex", flexDirection: "column", gap: 2 }}>
      {metrics.map((m, mi) => {
        const f = m.fmt || fmt;
        const peers = m.peers || [];
        const pool = [...peers, m.value].filter((v) => v != null && isFinite(v));
        const [lo, hi] = m.domain || (pool.length ? [Math.min(...pool), Math.max(...pool)] : [0, 1]);
        const span = hi - lo || 1;
        const pct = (v) => `${(((v - lo) / span) * 100).toFixed(2)}%`;
        const med = m.median != null ? m.median : (peers.length ? median(peers) : null);
        const better = m.higherIsBetter;
        const above = med != null && m.value > med;
        const tone = better == null ? "var(--series-actual)" : (above === !!better ? "var(--series-positive)" : "var(--series-negative)");
        const rank = peers.length ? peers.filter((p) => (better === false ? p < m.value : p > m.value)).length + 1 : null;
        const rowKey = String(m.id ?? m.label ?? mi);
        const named = namedPeers(m, f);
        const interactive = peerDetail && named.length > 0;
        const expanded = interactive && open === rowKey;
        const detailId = `${base}-p${mi}`;
        const summary = `${m.label}: ${subjectLabel} ${m.value == null ? "no data" : f(m.value)}`
          + (med != null ? `, peer median ${f(med)}` : "") + (rank != null ? `, rank ${rank} of ${peers.length + 1}` : "")
          + `. ${named.length} peer value${named.length === 1 ? "" : "s"}`;
        const marks = (
          <React.Fragment>
            <span className="mds-pcs__track" style={{ position: "absolute", left: 0, right: 0, top: 10, height: 2, background: "var(--viz-grid,var(--chart-grid))" }} />
            {peers.map((p, i) => (
              <span key={i} className="mds-pcs__peer" style={{ position: "absolute", left: pct(p), top: 5, width: 1, height: 12, marginLeft: -0.5, background: "var(--series-prior)", opacity: 0.55 }} />
            ))}
            {med != null && (
              <span style={{ position: "absolute", left: pct(med), top: 1, width: 0, height: 20, marginLeft: -0.5, borderLeft: "1px dashed var(--series-benchmark)", opacity: 0.9 }} />
            )}
            {m.value != null && (
              <span className="mds-pcs__subject" style={{ position: "absolute", left: pct(m.value), top: 4, width: 14, height: 14, marginLeft: -7, borderRadius: "50%", background: tone, border: "2px solid var(--surface-plate,var(--bg-light))", boxSizing: "border-box" }} />
            )}
          </React.Fragment>
        );
        // Printed outside the button: its accessible name is the summary, so visible text inside
        // it would be a label the name does not contain.
        const ends = (
          <React.Fragment>
            <span style={{ ...MONO, position: "absolute", left: 0, top: -2, fontSize: "var(--viz-type-micro,10px)", color: "var(--text-muted)", pointerEvents: "none" }} aria-hidden="true">{f(lo)}</span>
            <span style={{ ...MONO, position: "absolute", right: 0, top: -2, fontSize: "var(--viz-type-micro,10px)", color: "var(--text-muted)", pointerEvents: "none" }} aria-hidden="true">{f(hi)}</span>
          </React.Fragment>
        );
        return (
          <React.Fragment key={m.id || m.label || mi}>
          <div style={{ display: "flex", alignItems: "center", gap: 12, padding: "7px 0", borderBottom: mi < metrics.length - 1 && !expanded ? "1px solid var(--border-divider,var(--border))" : "none" }}>
            <div style={{ width: labelWidth, flex: "0 0 auto", minWidth: 0 }}>
              <div style={{ fontSize: "var(--viz-type-label,12px)", color: "var(--text-primary)", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{m.label}</div>
              {m.note && <div style={{ fontSize: "var(--viz-type-micro,10px)", color: "var(--text-muted)", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{m.note}</div>}
            </div>
            <div style={{ flex: "1 1 auto", minWidth: 120, position: "relative", height: 22 }}>
              {interactive ? (
                <button type="button" className="mds-pcs__rail" aria-label={summary} aria-expanded={expanded} aria-controls={expanded ? detailId : undefined}
                  onClick={() => setOpen(expanded ? null : rowKey)}
                  onKeyDown={(e) => { if (e.key === "Escape" && expanded) { e.preventDefault(); e.stopPropagation(); setOpen(null); } }}>
                  <span aria-hidden="true">{marks}</span>
                </button>
              ) : (
                <div style={{ position: "relative", height: 22 }} aria-hidden="true">{marks}</div>
              )}
              {ends}
            </div>
            <div style={{ ...MONO, width: valueWidth, flex: "0 0 auto", textAlign: "right" }}>
              <div style={{ fontSize: "var(--viz-type-label,12px)", fontWeight: 600, color: tone }}>{m.value == null ? "—" : f(m.value)}</div>
              {showRank && rank != null && <div style={{ fontSize: "var(--viz-type-micro,10px)", color: "var(--text-muted)" }}>{rank} of {peers.length + 1}</div>}
            </div>
          </div>
          {expanded && (
            <div className="mds-pcs__detail" id={detailId} style={{ borderBottom: mi < metrics.length - 1 ? "1px solid var(--border-divider,var(--border))" : "none" }}>
              <dl>
                {med != null && <div><dt>Peer median</dt><dd>{f(med)}</dd></div>}
                {named.map((n, i) => <div key={i}><dt>{n.name}</dt><dd>{n.text}</dd></div>)}
              </dl>
            </div>
          )}
          </React.Fragment>
        );
      })}
      {ariaLabel && (
        <table style={SR}>
          <caption>{caption || ariaLabel}</caption>
          <thead><tr><th scope="col">Metric</th><th scope="col">{subjectLabel}</th><th scope="col">Peer median</th><th scope="col">Rank</th><th scope="col">Peer values</th></tr></thead>
          <tbody>{metrics.map((m, i) => {
            const f = m.fmt || fmt, peers = m.peers || [];
            const med = m.median != null ? m.median : (peers.length ? median(peers) : null);
            const rank = peers.length ? peers.filter((p) => (m.higherIsBetter === false ? p < m.value : p > m.value)).length + 1 : null;
            return <tr key={i}><th scope="row">{m.label}</th><td>{m.value == null ? "no data" : f(m.value)}</td><td>{med == null ? "no peers" : f(med)}</td><td>{rank == null ? "—" : `${rank} of ${peers.length + 1}`}</td><td>{namedPeers(m, f).map((n) => `${n.name} ${n.text}`).join("; ") || "no peers"}</td></tr>;
          })}</tbody>
        </table>
      )}
      {caption && <p style={{ margin: "10px 0 0", fontSize: "var(--viz-type-annotation,12px)", color: "var(--text-secondary)", maxWidth: "62ch" }}>{caption}</p>}
    </div>
  );
}

PeerComparisonStrip.displayName = "PeerComparisonStrip";
