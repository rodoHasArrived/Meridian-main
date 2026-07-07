/**
 * Trust strip — mirrors `.workstation-trust-strip`. The masthead readiness band: a
 * row of label/value items tinted by `state` (ready · review · blocked · pending),
 * giving operators an at-a-glance evidence posture. Per the app, `review` reads amber
 * (attention) here rather than the blue used by {@link SeverityBadge}.
 *
 * @example
 * <TrustStrip items={[
 *   { label: "Readiness", value: "86 / 100", state: "review" },
 *   { label: "Recon",     value: "3 breaks", state: "blocked" },
 *   { label: "Providers", value: "4 live",   state: "ready" },
 *   { label: "Approval",  value: "Pending",  state: "pending" },
 * ]} />
 */
export interface TrustStripItem {
  /** Small-caps label. */
  label: React.ReactNode;
  /** Mono/short value. */
  value: React.ReactNode;
  /** State tint — Ready | ReviewRequired | Blocked | Pending (and aliases). */
  state?: string;
}
export interface TrustStripProps {
  items: TrustStripItem[];
  className?: string;
}
export declare function TrustStrip(props: TrustStripProps): JSX.Element;
