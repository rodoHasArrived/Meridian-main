// Meridian Delta — signed change value. Enforces the content rules for deltas: mono,
// tabular, EXPLICIT sign, dim semantic tone (green up · red down · muted flat). The sign
// carries the meaning; the optional arrow is decoration and aria-hidden.
import React from "react";

export function Delta({ value, decimals = 2, suffix = "", arrow = false, tone, locale, style = {}, ...rest }) {
  const n = typeof value === "number" ? value : typeof value === "string" && value.trim() ? Number(value) : NaN;
  if (!Number.isFinite(n)) return <span {...rest}>—</span>;
  const dir = n > 0 ? "up" : n < 0 ? "down" : "flat";
  const resolved = tone || dir;
  const color =
    resolved === "up" ? "var(--green-dim)" :
    resolved === "down" ? "var(--red-dim)" :
    "var(--text-muted)";
  const mag = locale
    ? Math.abs(n).toLocaleString(locale, { minimumFractionDigits: decimals, maximumFractionDigits: decimals })
    : Math.abs(n).toFixed(decimals);
  const text = `${n > 0 ? "+" : n < 0 ? "-" : "±"}${mag}${suffix}`;
  return (
    <span
      style={{ fontFamily: "var(--font-data)", fontVariantNumeric: "slashed-zero tabular-nums", color, whiteSpace: "nowrap", ...style }}
      {...rest}
    >
      {arrow && dir !== "flat" && <span aria-hidden="true">{dir === "up" ? "▲ " : "▼ "}</span>}
      {text}
    </span>
  );
}
