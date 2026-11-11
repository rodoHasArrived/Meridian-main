// Meridian ScenarioSelector — pick one scenario from a small set, with the consequence of the
// pick visible on the option itself (inline trace + headline metric), so the choice is made
// from evidence rather than from a name in a dropdown.
// Controlled by contract: the caller owns `value` and `onChange`, matching the rest of the kit.
// Omit `onChange` and the group renders inert (a printed record of the selected scenario).
// A scenario trace is model output, so it carries the `scenario` role dash — never a plain
// solid line that would read as observed data.
import React from "react";
import { Sparkline } from "./Sparkline.jsx";

const MONO = { fontFamily: "var(--font-data)", fontVariantNumeric: "slashed-zero tabular-nums" };
const toneColor = (t) => (t === "positive" ? "var(--series-positive)" : t === "negative" ? "var(--series-negative)" : t === "warning" ? "var(--series-warning)" : "var(--text-primary)");

export function ScenarioSelector({
  scenarios = [],              // [{ id, label, sub?, headline?, tone?, spark?: number[], metrics?: [{label,value,tone?}] }]
  value = null,
  onChange = null,
  layout = "rows",             // "rows" | "columns"
  sparkWidth = 104,
  note = null,                 // the caveat line — what the scenario model is and is not
  ariaLabel = "Scenario",
}) {
  const inert = typeof onChange !== "function";
  const ids = scenarios.map((s) => s.id);
  const optionRefs = React.useRef([]);
  const tabStop = Math.max(0, ids.indexOf(value));
  const onKeyDown = (e, index) => {
    if (inert || e.target !== e.currentTarget || e.altKey || e.ctrlKey || e.metaKey) return;
    let nextIndex;
    if (e.key === "ArrowDown" || e.key === "ArrowRight") nextIndex = (index + 1) % ids.length;
    else if (e.key === "ArrowUp" || e.key === "ArrowLeft") nextIndex = (index - 1 + ids.length) % ids.length;
    else if (e.key === " " || e.key === "Enter") {
      e.preventDefault();
      onChange(ids[index]);
      return;
    } else return;
    e.preventDefault();
    optionRefs.current[nextIndex]?.focus();
    onChange(ids[nextIndex]);
  };

  return (
    <div>
      <div role="radiogroup" aria-label={ariaLabel} aria-orientation={layout === "columns" ? "horizontal" : "vertical"} style={{
        display: layout === "columns" ? "grid" : "flex",
        gridTemplateColumns: layout === "columns" ? `repeat(${Math.max(1, scenarios.length)},minmax(0,1fr))` : undefined,
        flexDirection: layout === "columns" ? undefined : "column",
        gap: 6,
      }}>
        {scenarios.map((s, index) => {
          const on = s.id === value;
          return (
            <div key={s.id} role="radio" aria-checked={on} aria-disabled={inert || undefined}
              ref={(node) => { optionRefs.current[index] = node; }}
              tabIndex={inert ? -1 : index === tabStop ? 0 : -1}
              onKeyDown={(e) => onKeyDown(e, index)}
              onClick={inert ? undefined : (e) => { e.currentTarget.focus(); onChange(s.id); }}
              style={{
                display: "flex", alignItems: "center", gap: 12, minWidth: 0,
                padding: "10px 12px 10px 10px",
                background: on ? "var(--viz-select-surface,var(--selected-surface))" : "var(--surface-plate,var(--bg-light))",
                border: `1px solid ${on ? "var(--viz-select-rail,var(--selected))" : "var(--border)"}`,
                borderLeft: `var(--inspector-rail,4px) solid ${on ? "var(--viz-select-rail,var(--selected))" : "transparent"}`,
                borderRadius: 2, cursor: inert ? "default" : "pointer",
                outlineOffset: 2,
              }}>
              <span aria-hidden="true" style={{
                width: 12, height: 12, flex: "0 0 auto", borderRadius: "50%",
                border: `1.5px solid ${on ? "var(--viz-select-rail,var(--selected))" : "var(--border-strong,var(--border))"}`,
                background: on ? "radial-gradient(circle, var(--viz-select-rail,var(--selected)) 0 42%, transparent 43%)" : "transparent",
              }} />
              <span style={{ minWidth: 0, flex: "1 1 auto" }}>
                <span style={{ display: "block", fontSize: "var(--viz-type-label,12px)", fontWeight: on ? 600 : 500, color: "var(--text-primary)", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{s.label}</span>
                {s.sub && <span style={{ display: "block", fontSize: "var(--viz-type-micro,10px)", color: "var(--text-secondary)", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{s.sub}</span>}
              </span>
              {s.spark && s.spark.length > 1 && (
                <span style={{ flex: "0 0 auto", opacity: on ? 1 : 0.72 }}>
                  <Sparkline points={s.spark} width={sparkWidth} height={26} variant="line"
                    color={on ? "var(--series-scenario)" : "var(--series-prior)"} strokeWidth={on ? 1.8 : 1.3} />
                </span>
              )}
              {s.metrics && s.metrics.map((m, i) => (
                <span key={i} style={{ flex: "0 0 auto", textAlign: "right", minWidth: 68 }}>
                  <span style={{ display: "block", fontSize: "var(--viz-type-micro,10px)", color: "var(--text-muted)", whiteSpace: "nowrap" }}>{m.label}</span>
                  <span style={{ ...MONO, display: "block", fontSize: "var(--viz-type-label,12px)", color: toneColor(m.tone) }}>{m.value}</span>
                </span>
              ))}
              {s.headline != null && (
                <span style={{ ...MONO, flex: "0 0 auto", fontSize: 15, fontWeight: 600, color: toneColor(s.tone), minWidth: 78, textAlign: "right" }}>{s.headline}</span>
              )}
            </div>
          );
        })}
      </div>
      {note && <p style={{ margin: "10px 0 0", fontSize: "var(--viz-type-annotation,12px)", color: "var(--text-secondary)", maxWidth: "62ch" }}>{note}</p>}
    </div>
  );
}

ScenarioSelector.displayName = "ScenarioSelector";
