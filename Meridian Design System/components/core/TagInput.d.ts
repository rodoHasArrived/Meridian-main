/**
 * TagInput — chip-list entry for symbol lists, watchlist members, report recipients.
 * Controlled: `value` is a string array. Enter or comma commits the draft; Backspace on an
 * empty draft removes the last chip; blur commits. Duplicates are silently dropped.
 *
 * @example
 * <TagInput value={symbols} onChange={setSymbols} uppercase placeholder="Add symbol…"
 *   validate={(t) => /^[A-Z.]{1,8}$/.test(t)} aria-label="Watchlist symbols" />
 */
export interface TagInputProps {
  /** Current chips. @default [] */
  value?: string[];
  /** Fired with the full next array on add/remove. */
  onChange?: (next: string[]) => void;
  /** Shown only while empty. @default "Add…" */
  placeholder?: string;
  /** Uppercase drafts before committing (symbols). @default false */
  uppercase?: boolean;
  /** Reject a draft by returning false. */
  validate?: ((tag: string) => boolean) | null;
  disabled?: boolean;
  /** Accessible name for the text field. @default "Tags" */
  "aria-label"?: string;
}
export declare function TagInput(props: TagInputProps): JSX.Element;
