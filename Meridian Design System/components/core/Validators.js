// Meridian Validators — shared field validation functions for forms and tables.
// Returns true for valid, or an error message string for invalid.
// Use in EditableCell, forms, and field validations.

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
   * Date — validates ISO or locale date strings.
   * Accepts: 2026-06-15, 06/15/2026, 15.06.2026, etc.
   * Returns: true if valid, error message otherwise.
   */
  date: (value, opts = {}) => {
    const { format = "iso", minDate, maxDate } = opts;
    if (!value || typeof value !== "string") {
      return "Date required";
    }

    let date = null;

    if (format === "iso") {
      date = new Date(value);
    } else if (format === "us") {
      // MM/DD/YYYY
      const [m, d, y] = value.split("/");
      date = new Date(`${y}-${String(m).padStart(2, "0")}-${String(d).padStart(2, "0")}`);
    } else if (format === "eu") {
      // DD.MM.YYYY
      const [d, m, y] = value.split(".");
      date = new Date(`${y}-${String(m).padStart(2, "0")}-${String(d).padStart(2, "0")}`);
    }

    if (!date || isNaN(date.getTime())) {
      return `Invalid date (expected ${format})`;
    }

    if (minDate && date < new Date(minDate)) {
      return `Date must be on or after ${minDate}`;
    }

    if (maxDate && date > new Date(maxDate)) {
      return `Date must be on or before ${maxDate}`;
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
