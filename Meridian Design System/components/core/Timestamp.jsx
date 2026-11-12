// Meridian Timestamp — enforces the content rule for time: UTC, mono, explicit.
// "2026-06-09 20:00:00Z" (utc) · "20:00:00Z" (time) · "6m ago" (relative, auto-ticks).
// A `locale` prop renders the SAME UTC instant with locale digit order and a 24-hour
// clock ("09.06.2026, 20:00:00 UTC") — locale never changes the timezone, and the
// explicit UTC marker stays. The full UTC form is always available on hover via title;
// renders a semantic <time>.
import React from "react";

function toDate(v) {
  const d = v instanceof Date ? v : new Date(v);
  return isNaN(d.getTime()) ? null : d;
}
function pad(n) { return String(n).padStart(2, "0"); }
function utcFull(d) {
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())} ${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}:${pad(d.getUTCSeconds())}Z`;
}
function utcTime(d) { return `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}:${pad(d.getUTCSeconds())}Z`; }
function relative(d, now) {
  const s = Math.max(0, Math.round((now - d.getTime()) / 1000));
  if (s < 60) return `${s}s ago`;
  const m = Math.floor(s / 60);
  if (m < 60) return `${m}m ago`;
  const h = Math.floor(m / 60);
  if (h < 48) return `${h}h ago`;
  return `${Math.floor(h / 24)}d ago`;
}

function localized(d, locale, timeOnly) {
  const opts = timeOnly
    ? { hour: "2-digit", minute: "2-digit", second: "2-digit", hourCycle: "h23", timeZone: "UTC" }
    : { year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit", hourCycle: "h23", timeZone: "UTC" };
  try {
    return new Intl.DateTimeFormat(locale, opts).format(d) + "\u202FUTC";
  } catch (e) {
    return timeOnly ? utcTime(d) : utcFull(d);
  }
}

export function Timestamp({ value, format = "utc", locale, tickMs = 1000, style = {}, ...rest }) {
  const d = toDate(value);
  const [, tick] = React.useReducer((n) => n + 1, 0);
  React.useEffect(() => {
    if (format !== "relative" || !d) return;
    const t = setInterval(tick, tickMs);
    return () => clearInterval(t);
  }, [format, tickMs, value]);
  if (!d) return <span {...rest}>—</span>;
  const text = format === "relative" ? relative(d, Date.now())
    : locale ? localized(d, locale, format === "time")
    : format === "time" ? utcTime(d) : utcFull(d);
  return (
    <time
      dateTime={d.toISOString()}
      title={utcFull(d)}
      style={{ fontFamily: "var(--font-data)", fontVariantNumeric: "tabular-nums", whiteSpace: "nowrap", ...style }}
      {...rest}
    >
      {text}
    </time>
  );
}
