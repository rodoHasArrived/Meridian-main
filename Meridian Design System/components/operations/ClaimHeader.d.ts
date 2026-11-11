/**
 * Claim header — the top of Meridian's reading-path contract. The title states
 * a **claim, question, or decision**, not a topic: `The belly steepened while
 * the long end remained anchored.` beats `US Treasury yield curve`. Revise the
 * claim whenever the default selection or filter changes it.
 *
 * Carries the context path, the claim, one caveat/purpose line, the state chips
 * that qualify it (freshness, model condition, missingness), and — in
 * `children` — the controls that change the view.
 *
 * @example
 * <ClaimHeader
 *   context={["Rates", "United States Treasury", "14 May 2026"]}
 *   claim="The belly steepened while the long end remained anchored."
 *   note="Current versus one-month prior on a shared tenor scale; the scenario curve is model-derived."
 *   states={[
 *     { condition: "confirmed", label: "Prices live", detail: "42s" },
 *     { condition: "stale", label: "Reference", detail: "18m" },
 *     { condition: "estimated", label: "Scenario" },
 *   ]}>
 *   <SegmentedControl options={ranges} value={range} onChange={setRange} />
 * </ClaimHeader>
 */
export interface ClaimHeaderState {
  /** Any data-condition key or status string. */
  condition: string;
  label: string;
  /** Mono qualifier — age, count, version. */
  detail?: string;
}
export interface ClaimHeaderProps extends React.HTMLAttributes<HTMLElement> {
  /** Context path — string or segments, rendered small-caps with `/` separators. */
  context?: string | string[];
  /** The claim. One sentence, sentence case, ends in a period. */
  claim: React.ReactNode;
  /** Caveat, method, or comparison basis. One line. */
  note?: React.ReactNode;
  /** Qualifying state chips (freshness, model, missingness). */
  states?: ClaimHeaderState[];
  /** Let the claim run full width instead of the 34ch measure. */
  wide?: boolean;
  /**
   * Heading level the claim renders at. Leave at 1 when the claim is the page title; pass 2 when
   * the screen already has its own `<h1>`, so the page keeps exactly one.
   * @default 1
   */
  headingLevel?: 1 | 2 | 3 | 4 | 5 | 6;
  /** Drop the bottom rule (when the parent panel already draws one). */
  flush?: boolean;
  /** View controls, right-aligned — keep them adjacent to the evidence. */
  children?: React.ReactNode;
}
export declare function ClaimHeader(props: ClaimHeaderProps): JSX.Element;
