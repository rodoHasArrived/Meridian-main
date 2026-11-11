// Meridian local-date helpers — shared by DatePicker, DateRangePicker and anything else that deals
// in CALENDAR DATES rather than instants.
//
// The rule: a trade date, settlement date or accounting date is a date, not a moment. Never route
// one through `toISOString()` — that converts local midnight to UTC and moves the date back a day
// for every user west of Greenwich. Use `toISO`/`fromISO`, which stay in the user's own timezone.
//
// For genuine instants (a tick timestamp, an audit time) keep using Date/ISO datetime and format
// with `Timestamp` — this module is not for those.

export const pad = (n) => String(n).padStart(2, "0");

/** Local calendar date → "YYYY-MM-DD" (no timezone shift). */
export const toISO = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;

/** "YYYY-MM-DD" → local Date at midnight, or null if not a real date (rejects 2026-02-30). */
export const fromISO = (s) => {
  if (typeof s !== "string") return null;
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s.trim());
  if (!m) return null;
  const d = new Date(+m[1], +m[2] - 1, +m[3]);
  return d.getFullYear() === +m[1] && d.getMonth() === +m[2] - 1 && d.getDate() === +m[3] ? d : null;
};

/** Local day bucket for an instant — the operator's day, not the UTC day. */
export const dayOfInstant = (v) => {
  const d = v instanceof Date ? v : new Date(v);
  return isNaN(d.getTime()) ? null : toISO(d);
};

export const addDays = (d, n) => { const x = new Date(d); x.setDate(x.getDate() + n); return x; };

/** Month arithmetic that clamps the day — Jan 31 + 1 month is Feb 28/29, not Mar 3. */
export const addMonths = (d, n) => {
  const x = new Date(d.getFullYear(), d.getMonth() + n, 1);
  x.setDate(Math.min(d.getDate(), new Date(x.getFullYear(), x.getMonth() + 1, 0).getDate()));
  return x;
};

export const sameDay = (a, b) => !!a && !!b && toISO(a) === toISO(b);

export const clampDate = (d, min, max) => (min && d < min ? min : max && d > max ? max : d);

/** Operators paste dates as often as they click them: ISO, "26 Aug 2026", "8/26/2026". */
export const parseLoose = (raw) => {
  const s = String(raw ?? "").trim();
  if (!s) return null;
  const iso = fromISO(s);
  if (iso) return iso;
  const slash = /^(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})$/.exec(s);
  if (slash) { const d = new Date(+slash[3], +slash[1] - 1, +slash[2]); return isNaN(d) ? null : d; }
  const any = new Date(s);
  return isNaN(any) ? null : new Date(any.getFullYear(), any.getMonth(), any.getDate());
};

export const DOW = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];

export const dowLabels = (weekStartsOn = 0) => DOW.slice(weekStartsOn).concat(DOW.slice(0, weekStartsOn));

export const longDate = (d) =>
  d.toLocaleDateString(undefined, { weekday: "long", year: "numeric", month: "long", day: "numeric" });

export const monthTitle = (d) => d.toLocaleDateString(undefined, { year: "numeric", month: "long" });

/** Whole weeks covering a month — 5 or 6 rows, not a blind 42 cells. */
export const weeksFor = (cursor, weekStartsOn = 0) => {
  const first = new Date(cursor.getFullYear(), cursor.getMonth(), 1);
  const start = addDays(first, -(((first.getDay() - weekStartsOn) + 7) % 7));
  const last = new Date(cursor.getFullYear(), cursor.getMonth() + 1, 0);
  const out = [];
  let d = start;
  while (d <= last) {
    const row = [];
    for (let i = 0; i < 7; i++) { row.push(d); d = addDays(d, 1); }
    out.push(row);
    if (out.length >= 6) break;
  }
  return out;
};

/** Common desk ranges, as {start, end} ISO pairs. `today` is injectable for tests. */
export const rangePresets = (today = new Date()) => {
  const t = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  const q = Math.floor(t.getMonth() / 3) * 3;
  return [
    { id: "today", label: "Today", start: toISO(t), end: toISO(t) },
    { id: "7d", label: "Last 7d", start: toISO(addDays(t, -6)), end: toISO(t) },
    { id: "30d", label: "Last 30d", start: toISO(addDays(t, -29)), end: toISO(t) },
    { id: "mtd", label: "MTD", start: toISO(new Date(t.getFullYear(), t.getMonth(), 1)), end: toISO(t) },
    { id: "qtd", label: "QTD", start: toISO(new Date(t.getFullYear(), q, 1)), end: toISO(t) },
    { id: "ytd", label: "YTD", start: toISO(new Date(t.getFullYear(), 0, 1)), end: toISO(t) },
  ];
};
