/**
 * Journal-entry form — balanced double-entry input. A header (date / reference / memo) sits over
 * a line grid of Account · Debit · Credit with live column totals and a balance gauge that stays
 * red ("Out by …") until Σdebit = Σcredit, then turns green ("Balanced"). Add/remove lines; an
 * optional account list drives autocomplete; the Post button is gated on a balanced entry.
 * Controlled-output via onChange; self-contained styling.
 */
export interface JournalLine {
  account?: string;
  debit?: number | string;
  credit?: number | string;
}
export interface JournalHeader {
  date?: string;
  ref?: string;
  memo?: string;
}
export interface JournalEntryFormProps {
  /** Seed lines. @default two blank lines */
  initialLines?: JournalLine[];
  /** Seed header fields. */
  initialHeader?: JournalHeader;
  /** Account names for the input datalist (autocomplete). */
  accounts?: string[];
  /** @default "USD" */
  currency?: string;
  /** Absolute debit−credit difference treated as balanced. @default 0.005 */
  tolerance?: number;
  /** Fires on every edit with the full { header, lines } state. */
  onChange?: (entry: { header: JournalHeader; lines: JournalLine[] }) => void;
  /** When set, renders a Post button (enabled only when balanced). */
  onPost?: (entry: { header: JournalHeader; lines: JournalLine[] }) => void;
}
export declare function JournalEntryForm(props: JournalEntryFormProps): JSX.Element;
