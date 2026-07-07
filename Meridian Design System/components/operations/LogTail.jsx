// Meridian LogTail — the evidence surface for raw process output (backfills, quality scans,
// strategy runs). Mono stream, UTC time-of-day per line, level filter chips, follow-tail that
// pauses on manual scroll. Evidence-first: never summarizes, never hides the raw line.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-logtail{display:flex;flex-direction:column;min-height:0;background:var(--bg-light,#FFFFFF);
  border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-card,2px);font-family:var(--font-body);}
.mds-logtail__bar{display:flex;align-items:center;gap:8px;padding:6px 10px;flex:0 0 auto;
  border-bottom:1px solid var(--border,#CBD3DC);background:var(--bg-medium,#F3F6F9);}
.mds-logtail__title{font-size:10px;font-weight:600;font-variant:all-small-caps;letter-spacing:.04em;
  color:var(--text-muted,#59636F);white-space:nowrap;}
.mds-logtail__chip{display:inline-flex;align-items:center;gap:5px;border:1px solid var(--border,#CBD3DC);
  background:transparent;border-radius:var(--radius-chip,2px);padding:2px 7px;cursor:pointer;
  font-family:var(--font-data);font-size:10px;font-weight:600;color:var(--text-secondary,#4D5967);line-height:1.4;}
.mds-logtail__chip:hover{border-color:var(--border-hover,#ADB8C4);}
.mds-logtail__chip--on{background:var(--bg-active,#D7E5F1);border-color:var(--accent,#2F6F8F);color:var(--text-primary,#22272E);}
.mds-logtail__chip:focus-visible{outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-logtail__body{flex:1 1 auto;min-height:0;overflow:auto;padding:6px 0;
  font-family:var(--font-data,'Cascadia Mono',monospace);font-size:11.5px;line-height:1.55;}
.mds-logtail__line{display:flex;gap:10px;padding:0 10px;white-space:pre;align-items:baseline;}
.mds-logtail--wrap .mds-logtail__line{white-space:pre-wrap;}
.mds-logtail__line:hover{background:var(--bg-hover,#EAEEF3);}
.mds-logtail__ts{color:var(--text-muted,#59636F);flex:0 0 auto;font-variant-numeric:tabular-nums;}
.mds-logtail__lvl{flex:0 0 44px;font-weight:700;font-size:10px;letter-spacing:.03em;}
.mds-logtail__src{color:var(--text-secondary,#4D5967);flex:0 0 auto;}
.mds-logtail__msg{color:var(--text-primary,#22272E);min-width:0;}
.mds-logtail__lvl--info{color:var(--text-secondary,#4D5967);}
.mds-logtail__lvl--debug{color:var(--text-disabled,#889099);}
.mds-logtail__lvl--warn{color:var(--orange-dim,#683E0B);}
.mds-logtail__lvl--error{color:var(--red-dim,#8C2F40);}
.mds-logtail__line--error{background:var(--red-a10,rgba(186,63,85,.08));}
.mds-logtail__line--warn{background:var(--orange-a10,rgba(138,82,14,.07));}
.mds-logtail__empty{padding:18px 12px;color:var(--text-muted,#59636F);font-size:12px;font-family:var(--font-body);}
.mds-logtail__resume{position:absolute;right:14px;bottom:10px;}
.mds-logtail__wrapref{position:relative;display:flex;flex-direction:column;min-height:0;flex:1;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "logtail");
  el.textContent = css;
  document.head.appendChild(el);
}

const LEVELS = ["error", "warn", "info", "debug"];
function pad(n) { return String(n).padStart(2, "0"); }
function utcTime(v) {
  const d = v instanceof Date ? v : new Date(v);
  if (isNaN(d.getTime())) return "—";
  return `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}:${pad(d.getUTCSeconds())}Z`;
}

export function LogTail({
  entries = [],
  title = "Log",
  height = 240,
  follow: followDefault = true,
  wrap = false,
  levelFilter = true,
  className = "",
  style = {},
  ...rest
}) {
  inject();
  const bodyRef = React.useRef(null);
  const [follow, setFollow] = React.useState(followDefault);
  const [levels, setLevels] = React.useState(() => new Set(LEVELS));

  const counts = React.useMemo(() => {
    const c = { error: 0, warn: 0, info: 0, debug: 0 };
    for (const e of entries) if (c[e.level] != null) c[e.level] += 1;
    return c;
  }, [entries]);

  const visible = React.useMemo(
    () => entries.filter((e) => levels.has(e.level || "info")),
    [entries, levels]
  );

  React.useLayoutEffect(() => {
    const el = bodyRef.current;
    if (el && follow) el.scrollTop = el.scrollHeight;
  }, [visible.length, follow, wrap]);

  const onScroll = () => {
    const el = bodyRef.current;
    if (!el) return;
    const atBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 6;
    if (!atBottom && follow) setFollow(false);
    else if (atBottom && !follow) setFollow(true);
  };

  const toggleLevel = (lvl) =>
    setLevels((prev) => {
      const next = new Set(prev);
      if (next.has(lvl)) next.delete(lvl); else next.add(lvl);
      return next.size === 0 ? new Set(LEVELS) : next; // never filter to nothing
    });

  return (
    <div className={`mds-logtail${wrap ? " mds-logtail--wrap" : ""}${className ? " " + className : ""}`}
      style={{ height, ...style }} {...rest}>
      <div className="mds-logtail__bar">
        <span className="mds-logtail__title">{title}</span>
        <span className="mds-logtail__title" style={{ opacity: 0.7 }}>· {entries.length.toLocaleString("en-US")} lines</span>
        <span style={{ flex: 1 }} />
        {levelFilter && LEVELS.map((lvl) => (
          <button key={lvl} type="button" aria-pressed={levels.has(lvl)}
            className={`mds-logtail__chip${levels.has(lvl) ? " mds-logtail__chip--on" : ""}`}
            onClick={() => toggleLevel(lvl)}>
            <span className={`mds-logtail__lvl--${lvl}`} style={{ fontWeight: 700 }}>{lvl.toUpperCase()}</span>
            {counts[lvl]}
          </button>
        ))}
        <button type="button" aria-pressed={follow}
          className={`mds-logtail__chip${follow ? " mds-logtail__chip--on" : ""}`}
          title="Follow the newest lines as they arrive"
          onClick={() => setFollow((f) => !f)}>
          {follow ? "Following" : "Paused"}
        </button>
      </div>
      <div className="mds-logtail__wrapref">
        <div className="mds-logtail__body" ref={bodyRef} onScroll={onScroll} role="log" aria-label={title}>
          {visible.length === 0 ? (
            <div className="mds-logtail__empty">No log lines{entries.length > 0 ? " at the selected levels" : ""}.</div>
          ) : (
            visible.map((e, i) => (
              <div key={e.id || i} className={`mds-logtail__line${e.level === "error" ? " mds-logtail__line--error" : e.level === "warn" ? " mds-logtail__line--warn" : ""}`}>
                <span className="mds-logtail__ts">{utcTime(e.ts)}</span>
                <span className={`mds-logtail__lvl mds-logtail__lvl--${e.level || "info"}`}>{(e.level || "info").toUpperCase()}</span>
                {e.source && <span className="mds-logtail__src">{e.source}</span>}
                <span className="mds-logtail__msg">{e.text}</span>
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
}

LogTail.displayName = "LogTail";
