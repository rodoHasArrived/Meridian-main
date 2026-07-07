/**
 * AsyncCombobox — type-ahead picker over a large or server-backed option set (the "one of
 * 40k instruments" control). Debounced async loading, a windowed option list (only visible
 * rows render — handles tens of thousands), keyboard nav, and a quiet loading/empty/error
 * footer. Controlled: you own `value` and the `fetchOptions` fetcher.
 *
 * @example
 * <AsyncCombobox
 *   value={sym}
 *   onChange={setSym}
 *   fetchOptions={async (q, signal) => (await api.searchSymbols(q, { signal }))}
 *   getKey={(o) => o.symbol} getLabel={(o) => o.name} getSecondary={(o) => o.name}
 *   minChars={1} placeholder="Symbol…" />
 */
export interface AsyncComboboxProps<O = any> {
  value?: O | null;
  onChange?: (option: O) => void;
  /** Debounced, cancelable. Return the option array (sliced to maxRows). */
  fetchOptions: (query: string, signal?: AbortSignal) => Promise<O[]>;
  getKey?: (option: O) => string;
  getLabel?: (option: O) => React.ReactNode;
  /** Optional dimmer second line (e.g. the instrument name beside its ticker). */
  getSecondary?: (option: O) => React.ReactNode;
  placeholder?: string;
  /** @default 220 */
  debounceMs?: number;
  /** Don't query until this many chars typed. @default 0 */
  minChars?: number;
  emptyLabel?: string;
  /** Cap rows kept in memory. @default 200 */
  maxRows?: number;
}
export declare function AsyncCombobox<O = any>(props: AsyncComboboxProps<O>): JSX.Element;
