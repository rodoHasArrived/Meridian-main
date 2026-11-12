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

const median = (a) => { const s = [...a].sort((x, y) => x - y); const m = s.length >> 1; return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2; };
const finite = (value) => typeof value === "number" && Number.isFinite(value);
function comparison(metric) {
  const value = finite(metric.value) ? metric.value : null;
  const peers = (metric.peers || []).filter(finite);
  const pool = value === null ? peers : [...peers, value];
  const declared = metric.domain;
  const domain = declared && finite(declared[0]) && finite(declared[1]) && declared[1] >= declared[0]
    ? declared : pool.length ? [Math.min(...pool), Math.max(...pool)] : [0, 1];
  const med = finite(metric.median) ? metric.median : peers.length ? median(peers) : null;
  const rank = value !== null && peers.length
    ? peers.filter((peer) => metric.higherIsBetter === false ? peer < value : peer > value).length + 1 : null;
  return { value, peers, domain, med, rank };
}

export function PeerComparisonStrip({
  metrics = [],                // [{ id?, label, value, peers?: number[], domain?: [lo,hi], median?, fmt?, higherIsBetter?, note? }]
  subjectLabel = "Subject",
  labelWidth = 168,
  valueWidth = 92,
  showRank = true,
  fmt = (v) => (Math.abs(v) >= 100 ? v.toFixed(0) : v.toFixed(2)),
  ariaLabel,
  caption,
}) {
  return (
    <div role={ariaLabel ? "group" : undefined} aria-label={ariaLabel} style={{ position: "relative", display: "flex", flexDirection: "column", gap: 2 }}>
      {metrics.map((m, mi) => {
        const f = m.fmt || fmt;
        const { value, peers, domain: [lo, hi], med, rank } = comparison(m);
        const span = hi - lo || 1;
        const pct = (v) => `${(((v - lo) / span) * 100).toFixed(2)}%`;
        const better = m.higherIsBetter;
        const above = value !== null && med !== null && value > med;
        const tone = better == null || value === null || med === null || value === med ? "var(--series-actual)" : (above === !!better ? "var(--series-positive)" : "var(--series-negative)");
        return (
          <div key={m.id || m.label || mi} style={{ display: "flex", alignItems: "center", gap: 12, padding: "7px 0", borderBottom: mi < metrics.length - 1 ? "1px solid var(--border-divider,var(--border))" : "none" }}>
            <div style={{ width: labelWidth, flex: "0 0 auto", minWidth: 0 }}>
              <div style={{ fontSize: "var(--viz-type-label,12px)", color: "var(--text-primary)", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{m.label}</div>
              {m.note && <div style={{ fontSize: "var(--viz-type-micro,10px)", color: "var(--text-muted)", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{m.note}</div>}
            </div>
            <div style={{ flex: "1 1 auto", minWidth: 120, position: "relative", height: 22 }}>
              <div style={{ position: "absolute", left: 0, right: 0, top: 10, height: 2, background: "var(--viz-grid,var(--chart-grid))" }} />
              {peers.map((p, i) => (
                <span key={i} title={`peer ${f(p)}`} style={{ position: "absolute", left: pct(p), top: 5, width: 1, height: 12, marginLeft: -0.5, background: "var(--series-prior)", opacity: 0.55 }} />
              ))}
              {med != null && (
                <span title={`median ${f(med)}`} style={{ position: "absolute", left: pct(med), top: 1, width: 0, height: 20, marginLeft: -0.5, borderLeft: "1px dashed var(--series-benchmark)", opacity: 0.9 }} />
              )}
              {value !== null && (
                <span title={`${subjectLabel} ${f(value)}`} style={{ position: "absolute", left: pct(value), top: 4, width: 14, height: 14, marginLeft: -7, borderRadius: "50%", background: tone, border: "2px solid var(--surface-plate,var(--bg-light))", boxSizing: "border-box" }} />
              )}
              <span style={{ ...MONO, position: "absolute", left: 0, top: -2, fontSize: "var(--viz-type-micro,10px)", color: "var(--text-muted)" }} aria-hidden="true">{f(lo)}</span>
              <span style={{ ...MONO, position: "absolute", right: 0, top: -2, fontSize: "var(--viz-type-micro,10px)", color: "var(--text-muted)" }} aria-hidden="true">{f(hi)}</span>
            </div>
            <div style={{ ...MONO, width: valueWidth, flex: "0 0 auto", textAlign: "right" }}>
              <div style={{ fontSize: "var(--viz-type-label,12px)", fontWeight: 600, color: tone }}>{value === null ? "—" : f(value)}</div>
              {showRank && rank != null && <div style={{ fontSize: "var(--viz-type-micro,10px)", color: "var(--text-muted)" }}>{rank} of {peers.length + 1}</div>}
            </div>
          </div>
        );
      })}
      {ariaLabel && (
        <table style={SR}>
          <caption>{caption || ariaLabel}</caption>
          <thead><tr><th scope="col">Metric</th><th scope="col">{subjectLabel}</th><th scope="col">Peer median</th><th scope="col">Rank</th><th scope="col">Peer values</th></tr></thead>
          <tbody>{metrics.map((m, i) => {
            const f = m.fmt || fmt;
            const { value, peers, med, rank } = comparison(m);
            return <tr key={i}><th scope="row">{m.label}</th><td>{value === null ? "no data" : f(value)}</td><td>{med === null ? "no peers" : f(med)}</td><td>{!showRank || rank === null ? "—" : `${rank} of ${peers.length + 1}`}</td><td>{peers.length ? peers.map(f).join(", ") : "no peers"}</td></tr>;
          })}</tbody>
        </table>
      )}
      {caption && <p style={{ margin: "10px 0 0", fontSize: "var(--viz-type-annotation,12px)", color: "var(--text-secondary)", maxWidth: "62ch" }}>{caption}</p>}
    </div>
  );
}

PeerComparisonStrip.displayName = "PeerComparisonStrip";
