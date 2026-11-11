// Meridian Validators — shared field validation functions for forms and tables.
// Returns true for valid, or an error message string for invalid.
// Use in EditableCell, forms, and field validations.
import { fromISO, pad, toISO } from "./localDate.js";

// A validated date field holds a CALENDAR date, so every shape is parsed to local midnight via
// localDate.fromISO. The previous build fed each format to `new Date()`: "2026-6-15" parsed local,
// "06/15/2026" was rebuilt into an ISO string and parsed UTC, and min/max were parsed UTC too — so
// the same day compared true on one path and "before minDate" on another for anyone east of UTC.
const DATE_SHAPES = {
  iso: { re: /^(\d{4})-(\d{1,2})-(\d{1,2})$/, order: [1, 2, 3], hint: "YYYY-MM-DD" },
  us: { re: /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/, order: [3, 1, 2], hint: "MM/DD/YYYY" },
  eu: { re: /^(\d{1,2})\.(\d{1,2})\.(\d{4})$/, order: [3, 2, 1], hint: "DD.MM.YYYY" },
};

/** Strict parse in one named format → local Date at midnight, or null. Rejects 2026-02-30. */
const parseIn = (value, format) => {
  const shape = DATE_SHAPES[format];
  if (!shape) return null;
  const m = shape.re.exec(String(value).trim());
  if (!m) return null;
  const [y, mo, d] = shape.order.map((i) => Number(m[i]));
  return fromISO(`${y}-${pad(mo)}-${pad(d)}`);
};

/** A min/max bound: Date, "YYYY-MM-DD", or any accepted display format → local midnight. */
const parseBound = (v) => {
  if (v == null || v === "") return null;
  if (v instanceof Date) return isNaN(v.getTime()) ? null : new Date(v.getFullYear(), v.getMonth(), v.getDate());
  return parseIn(v, "iso") || parseIn(v, "us") || parseIn(v, "eu");
};

export const Validators = {
  /**
   * Currency — validates positive or negative money amounts.
   * Accepts: 1000, 1,000.00, (1000.00), -1000, $1000, etc.
   * Returns: true if valid, error message otherwise.
   */
  currency: (value, opts = {}) => {
    const { allowNegative = true, allowZero = true, decimals = 2 } = opts;
    if (typeof value === "number") {
      value = String(value);
    }
    if (!value || typeof value !== "string") {
      return "Currency amount required";
    }

    // Strip common currency symbols and whitespace
    let cleaned = value.replace(/[$\s]/g, "").trim();

    // Handle accounting parentheses (1,234.00) = negative
    let isNegative = false;
    if (cleaned.startsWith("(") && cleaned.endsWith(")")) {
      isNegative = true;
      cleaned = cleaned.slice(1, -1);
    }

    // Strip commas and parse
    cleaned = cleaned.replace(/,/g, "");
    const num = parseFloat(cleaned);

    if (isNaN(num)) {
      return "Invalid currency amount";
    }

    const finalNum = isNegative ? -num : num;

    if (!allowNegative && finalNum < 0) {
      return "Negative amounts not allowed";
    }

    if (!allowZero && finalNum === 0) {
      return "Amount cannot be zero";
    }

    // Check decimal places
    const decimalMatch = cleaned.match(/\.(\d+)/);
    if (decimalMatch && decimalMatch[1].length > decimals) {
      return `Maximum ${decimals} decimal places allowed`;
    }

    return true;
  },

  /**
   * Date — validates a calendar date in one named format (no timezone round-trip).
   * format: "iso" 2026-06-15 · "us" 06/15/2026 · "eu" 15.06.2026. Only the named shape is accepted,
   * so a datetime or a foreign format is an error rather than a silent reinterpretation.
   * minDate/maxDate are inclusive and may be a Date or a date string.
   * Returns: true if valid, error message otherwise.
   */
  date: (value, opts = {}) => {
    const { format = "iso", minDate, maxDate } = opts;
    if (!value || typeof value !== "string") {
      return "Date required";
    }

    const shape = DATE_SHAPES[format];
    if (!shape) {
      return `Unknown date format "${format}" (expected iso, us or eu)`;
    }

    const date = parseIn(value, format);
    if (!date) {
      return `Invalid date (expected ${shape.hint})`;
    }

    const min = parseBound(minDate);
    if (min && date < min) {
      return `Date must be on or after ${toISO(min)}`;
    }

    const max = parseBound(maxDate);
    if (max && date > max) {
      return `Date must be on or before ${toISO(max)}`;
    }

    return true;
  },

  /**
   * AccountID — validates account identifiers (ledger, bank, investment accounts).
   * Accepts: US-001, GL-2540, AAPL-USD, etc. Format: alphanumeric + hyphens, 3-20 chars.
   * Returns: true if valid, error message otherwise.
   */
  accountId: (value, opts = {}) => {
    const { minLength = 3, maxLength = 20 } = opts;
    if (!value || typeof value !== "string") {
      return "Account ID required";
    }

    const cleaned = value.trim().toUpperCase();

    if (cleaned.length < minLength || cleaned.length > maxLength) {
      return `Account ID must be ${minLength}-${maxLength} characters`;
    }

    if (!/^[A-Z0-9\-]+$/.test(cleaned)) {
      return "Account ID must contain only letters, numbers, and hyphens";
    }

    if (cleaned.startsWith("-") || cleaned.endsWith("-")) {
      return "Account ID cannot start or end with a hyphen";
    }

    return true;
  },

  /**
   * Email — validates email addresses.
   * Accepts: user@example.com, john.doe+tag@corp.co.uk, etc.
   * Returns: true if valid, error message otherwise.
   */
  email: (value, opts = {}) => {
    if (!value || typeof value !== "string") {
      return "Email required";
    }

    const email = value.trim().toLowerCase();
    // Simple regex — not exhaustive RFC 5322, but practical for forms
    const pattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

    if (!pattern.test(email)) {
      return "Invalid email address";
    }

    if (email.length > 254) {
      return "Email address too long";
    }

    return true;
  },

  /**
   * Required — non-empty string or value.
   * Returns: true if valid, error message otherwise.
   */
  required: (value, opts = {}) => {
    const { fieldName = "This field" } = opts;
    if (!value || (typeof value === "string" && !value.trim())) {
      return `${fieldName} is required`;
    }
    return true;
  },

  /**
   * MinLength — string or value length constraint.
   * Returns: true if valid, error message otherwise.
   */
  minLength: (value, opts = {}) => {
    const { min = 1, fieldName = "This field" } = opts;
    if (!value) return true; // Allow empty (use required() separately)
    const length = typeof value === "string" ? value.length : String(value).length;
    if (length < min) {
      return `${fieldName} must be at least ${min} character${min > 1 ? "s" : ""}`;
    }
    return true;
  },

  /**
   * MaxLength — string or value length constraint.
   * Returns: true if valid, error message otherwise.
   */
  maxLength: (value, opts = {}) => {
    const { max = 255, fieldName = "This field" } = opts;
    if (!value) return true; // Allow empty
    const length = typeof value === "string" ? value.length : String(value).length;
    if (length > max) {
      return `${fieldName} must be no more than ${max} character${max > 1 ? "s" : ""}`;
    }
    return true;
  },

  /**
   * Compose — chain multiple validators. First error wins.
   * Usage: Validators.compose([
   *   v => Validators.required(v),
   *   v => Validators.minLength(v, { min: 5 }),
   *   v => Validators.currency(v)
   * ])
   */
  compose: (validators) => (value) => {
    for (const validator of validators) {
      const result = validator(value);
      if (result !== true) {
        return result;
      }
    }
    return true;
  },
};

export default Validators;
