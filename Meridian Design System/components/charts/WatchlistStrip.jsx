// Meridian WatchlistStrip — a dense list of instruments where every row answers the same four
// questions at a glance: what is it, where is it, which way is it going, and where does it sit
// in today's range. Built for rails and side panels, so the row is a fixed grid — columns line
// up down the list and the eye scans a column, not a row.
// Change tone comes from the positive/negative series roles and is ALWAYS accompanied by a
// sign, never colour alone.
import React from "react";
import { Sparkline } from "./Sparkline.jsx";
import { Fmt } from "./viz.js";

const MONO = { fontFamily: "var(--font-data)", fontVariantNumeric: "slashed-zero tabular-nums" };

export function WatchlistStrip({
  items = [],                  // [{ id?, symbol, name?, last, change?, changePct?, spark?, dayRange?: [lo,hi], condition? }]
  columns = 1,
  dense = false,
  showSpark = true,
  showRange = true,
  priceFmt = Fmt.price,
  selectedId = null,
  onSelect = null,
  ariaLabel = "Watchlist",
}) {
  const pad = dense ? "4px 8px" : "7px 10px";
  const sparkW = dense ? 56 : 74;
  return (
    <div role={onSelect ? "listbox" : "list"} aria-label={ariaLabel} style={{
      display: "grid", gridTemplateColumns: `repeat(${Math.max(1, columns)},minmax(0,1fr))`,
      gap: columns > 1 ? "0 16px" : 0,
      background: "var(--surface-plate,var(--bg-light))", border: "1px solid var(--border)", borderRadius: 2,
    }}>
      {items.map((it, i) => {
        const on = selectedId != null && (it.id || it.symbol) === selectedId;
        const up = (it.change ?? it.changePct ?? 0) >= 0;
        const tone = (it.change == null && it.changePct == null) ? "var(--text-muted)" : up ? "var(--series-positive)" : "var(--series-negative)";
        const range = it.dayRange;
        const pos = range && range[1] !== range[0] ? Math.max(0, Math.min(1, (it.last - range[0]) / (range[1] - range[0]))) : null;
        const Row = onSelect ? "button" : "div";
        return (
          <Row key={it.id || it.symbol || i} type={onSelect ? "button" : undefined}
            role={onSelect ? "option" : "listitem"} aria-selected={onSelect ? on : undefined}
            onClick={onSelect ? () => onSelect(it.id || it.symbol) : undefined}
            style={{
              display: "grid", gridTemplateColumns: `minmax(0,1fr) ${showSpark ? `${sparkW}px ` : ""}auto auto`,
              alignItems: "center", gap: 10, padding: pad, width: "100%", textAlign: "left",
              font: "inherit", border: "none", borderTop: i >= columns ? "1px solid var(--border-divider,var(--border))" : "none",
              borderLeft: `2px solid ${on ? "var(--viz-select-rail,var(--selected))" : "transparent"}`,
              background: on ? "var(--viz-select-surface,var(--selected-surface))" : "transparent",
              cursor: onSelect ? "pointer" : "default",
            }}>
            <span style={{ minWidth: 0 }}>
              <span style={{ ...MONO, display: "block", fontSize: dense ? 11 : "var(--viz-type-label,12px)", fontWeight: 600, color: "var(--text-primary)", letterSpacing: ".01em" }}>{it.symbol}</span>
              {it.name && !dense && <span style={{ display: "block", fontSize: "var(--viz-type-micro,10px)", color: "var(--text-muted)", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{it.name}</span>}
            </span>
            {showSpark && (
              <span style={{ display: "block" }}>
                {it.spark && it.spark.length > 1
                  ? <Sparkline points={it.spark} width={sparkW} height={dense ? 16 : 22} variant="line" color={tone} strokeWidth={1.4} />
                  : <span style={{ display: "block", height: dense ? 16 : 22 }} />}
              </span>
            )}
            <span style={{ ...MONO, fontSize: dense ? 11 : "var(--viz-type-label,12px)", color: "var(--text-primary)", textAlign: "right", minWidth: 56 }}>{priceFmt(it.last)}</span>
            <span style={{ ...MONO, fontSize: dense ? 11 : "var(--viz-type-label,12px)", color: tone, textAlign: "right", minWidth: 62 }}>
              {it.changePct != null ? `${up ? "+" : "−"}${Math.abs(it.changePct).toFixed(2)}%` : it.change != null ? `${up ? "+" : "−"}${priceFmt(Math.abs(it.change))}` : "—"}
            </span>
            {showRange && range && (
              <span style={{ gridColumn: "1 / -1", position: "relative", height: 3, background: "var(--viz-grid,var(--chart-grid))", marginTop: 2 }} aria-hidden="true">
                {pos != null && <span style={{ position: "absolute", left: `${pos * 100}%`, top: -2, width: 2, height: 7, marginLeft: -1, background: tone }} />}
              </span>
            )}
          </Row>
        );
      })}
    </div>
  );
}

WatchlistStrip.displayName = "WatchlistStrip";
