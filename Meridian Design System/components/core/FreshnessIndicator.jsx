// Meridian FreshnessIndicator — the live-data status primitive. A status dot + optional
// last-seen readout for any streaming surface: quote feeds, provider links, run telemetry.
// Encodes the voice rule "name the system, the time, the evidence":
//   Polygon · live      ·  Polygon stale · 14:02:11Z      ·  Polygon offline · 6m ago
// Flat by default (no animation on data); only `connecting` carries a quiet pulse.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-fresh{display:inline-flex;align-items:center;gap:7px;font-family:var(--font-data,monospace);
  font-size:11px;line-height:1.2;color:var(--text-secondary,#4D5967);white-space:nowrap;}
.mds-fresh__dot{flex:none;width:8px;height:8px;border-radius:50%;background:var(--mds-fresh-c);
  box-shadow:0 0 0 2px var(--mds-fresh-ring);}
.mds-fresh--connecting .mds-fresh__dot{animation:mds-fresh-pulse 1.1s ease-in-out infinite;}
@keyframes mds-fresh-pulse{0%,100%{opacity:1;}50%{opacity:.35;}}
.mds-fresh__src{color:var(--text-primary,#22272E);font-weight:600;}
.mds-fresh__state{color:var(--mds-fresh-c);font-weight:600;font-variant:all-small-caps;
  letter-spacing:.04em;}
.mds-fresh__sep{color:var(--text-disabled,#889099);}
.mds-fresh__time{color:var(--text-muted,#59636F);}
.mds-fresh--badge{padding:3px 8px;border:1px solid var(--mds-fresh-ring);
  background:var(--mds-fresh-wash);border-radius:var(--radius-chip,2px);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "freshness");
  el.textContent = css;
  document.head.appendChild(el);
}

const STATUS = {
  live:       { label: "live",       c: "var(--green,#16885F)",  ring: "var(--green-a20)",  wash: "var(--green-a10)" },
  fresh:      { label: "fresh",      c: "var(--green,#16885F)",  ring: "var(--green-a20)",  wash: "var(--green-a10)" },
  stale:      { label: "stale",      c: "var(--orange,#8A520E)", ring: "var(--orange-a20)", wash: "var(--orange-a10)" },
  delayed:    { label: "delayed",    c: "var(--orange,#8A520E)", ring: "var(--orange-a20)", wash: "var(--orange-a10)" },
  offline:    { label: "offline",    c: "var(--red,#BA3F55)",    ring: "var(--red-a20)",    wash: "var(--red-a10)" },
  connecting: { label: "connecting", c: "var(--accent,#2F6F8F)", ring: "var(--blue-a10)",   wash: "var(--blue-a10)" },
  idle:       { label: "idle",       c: "var(--text-disabled,#889099)", ring: "rgba(136,144,153,.25)", wash: "transparent" },
};

function relativeTime(then) {
  const t = then instanceof Date ? then.getTime() : (typeof then === "number" ? then : Date.parse(then));
  if (!t || Number.isNaN(t)) return null;
  const s = Math.max(0, Math.round((Date.now() - t) / 1000));
  if (s < 5) return "just now";
  if (s < 60) return `${s}s ago`;
  const m = Math.round(s / 60);
  if (m < 60) return `${m}m ago`;
  const h = Math.round(m / 60);
  if (h < 24) return `${h}h ago`;
  return `${Math.round(h / 24)}d ago`;
}

function utcStamp(then) {
  const d = then instanceof Date ? then : new Date(typeof then === "number" ? then : Date.parse(then));
  if (Number.isNaN(d.getTime())) return null;
  return d.toISOString().slice(11, 19) + "Z";
}

// Bare dot — for inline use beside a live number.
export function ConnectionDot({ status = "live", size = 8, title }) {
  inject();
  const s = STATUS[status] || STATUS.idle;
  return (
    <span
      className={`mds-fresh mds-fresh--${status}`}
      style={{ "--mds-fresh-c": s.c, "--mds-fresh-ring": s.ring }}
      title={title || s.label}
    >
      <span className="mds-fresh__dot" style={{ width: size, height: size }} aria-hidden="true" />
    </span>
  );
}
ConnectionDot.displayName = "ConnectionDot";

export function FreshnessIndicator({
  status = "live",
  source,
  lastSeen,
  // "relative" → "6m ago" (auto-ticks); "utc" → "14:02:11Z"; "none" → hide time
  timeFormat = "relative",
  showState = true,
  badge = false,
  tickMs = 1000,
  className = "",
  ...rest
}) {
  inject();
  const s = STATUS[status] || STATUS.idle;
  const [, force] = React.useState(0);

  React.useEffect(() => {
    if (timeFormat !== "relative" || lastSeen == null) return;
    const id = setInterval(() => force((n) => n + 1), tickMs);
    return () => clearInterval(id);
  }, [timeFormat, lastSeen, tickMs]);

  let timeStr = null;
  if (lastSeen != null && timeFormat !== "none") {
    timeStr = timeFormat === "utc" ? utcStamp(lastSeen) : relativeTime(lastSeen);
  }

  return (
    <span
      className={`mds-fresh mds-fresh--${status}${badge ? " mds-fresh--badge" : ""}${className ? " " + className : ""}`}
      style={{ "--mds-fresh-c": s.c, "--mds-fresh-ring": s.ring, "--mds-fresh-wash": s.wash }}
      role="status"
      {...rest}
    >
      <span className="mds-fresh__dot" aria-hidden="true" />
      {source && <span className="mds-fresh__src">{source}</span>}
      {showState && <span className="mds-fresh__state">{s.label}</span>}
      {timeStr && <span className="mds-fresh__sep">·</span>}
      {timeStr && <span className="mds-fresh__time">{timeStr}</span>}
    </span>
  );
}

FreshnessIndicator.displayName = "FreshnessIndicator";
