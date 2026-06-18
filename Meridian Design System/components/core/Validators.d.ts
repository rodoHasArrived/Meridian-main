/**
 * Validators — shared field validation functions for forms and tables.
 *
 * All validators return true for valid input, or an error message string for invalid.
 * Use in EditableCell, forms, and field validations.
 *
 * @example
 * import Validators from './Validators.js';
 *
 * // In a form:
 * const amountError = Validators.currency(inputValue);
 * if (amountError !== true) console.error(amountError);
 *
 * // In EditableCell:
 * <EditableCell
 *   value={row.amount}
 *   type="text"
 *   validation={v => Validators.currency(v)}
 * />
 *
 * // Compose multiple validators:
 * const validateAccount = Validators.compose([
 *   v => Validators.required(v),
 *   v => Validators.accountId(v)
 * ]);
 */

export interface ValidatorOptions {
  [key: string]: any;
}

export const Validators: {
  /**
   * Currency — validates positive or negative money amounts.
   * Accepts: 1000, 1,000.00, (1000.00), -1000, $1000, etc.
   * Options: { allowNegative?: boolean, allowZero?: boolean, decimals?: number }
   */
  currency: (value: any, opts?: ValidatorOptions) => true | string;

  /**
   * Date — validates ISO or locale date strings.
   * Accepts: 2026-06-15, 06/15/2026, 15.06.2026, etc.
   * Options: { format?: 'iso' | 'us' | 'eu', minDate?: string, maxDate?: string }
   */
  date: (value: any, opts?: ValidatorOptions) => true | string;

  /**
   * AccountId — validates account identifiers (GL, bank, investment accounts).
   * Format: alphanumeric + hyphens, 3-20 chars (customizable).
   */
  accountId: (value: any, opts?: ValidatorOptions) => true | string;

  /**
   * Email — validates email addresses (practical regex, not exhaustive RFC 5322).
   */
  email: (value: any, opts?: ValidatorOptions) => true | string;

  /**
   * Required — non-empty string or value.
   * Options: { fieldName?: string }
   */
  required: (value: any, opts?: ValidatorOptions) => true | string;

  /**
   * MinLength — string or value length constraint.
   * Options: { min?: number, fieldName?: string }
   */
  minLength: (value: any, opts?: ValidatorOptions) => true | string;

  /**
   * MaxLength — string or value length constraint.
   * Options: { max?: number, fieldName?: string }
   */
  maxLength: (value: any, opts?: ValidatorOptions) => true | string;

  /**
   * Compose — chain multiple validators. First error wins.
   * Usage: Validators.compose([
   *   v => Validators.required(v),
   *   v => Validators.minLength(v, { min: 5 })
   * ])
   */
  compose: (validators: Array<(value: any) => true | string>) => (value: any) => true | string;
};

export default Validators;
