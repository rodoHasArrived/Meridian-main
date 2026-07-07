/**
 * Readiness panel — mirrors `.panel-state`. A gate / readiness summary block led by
 * a {@link SeverityBadge}, with a title, detail line, optional score, optional body,
 * and a right-aligned action footer. The left rail and border tint track `state`.
 *
 * Use it to explain a readiness verdict: "Reconciliation — Review Required, 3 open
 * exceptions" with Investigate / Approve actions in the footer.
 *
 * @example
 * <ReadinessPanel
 *   state="ReviewRequired"
 *   score="86 / 100"
 *   title="Reconciliation"
 *   detail="3 open exceptions above tolerance in the custody cash lane."
 *   actions={<><Button variant="ghost">Investigate</Button><Button variant="primary">Approve</Button></>}
 * />
 */
export interface ReadinessPanelProps {
  /** Readiness status string (Ready, ReviewRequired, Blocked, …). @default "info" */
  state?: string;
  /** Override the badge label (defaults to the canonical severity name). */
  statusLabel?: React.ReactNode;
  /** Bold panel title. */
  title?: React.ReactNode;
  /** Muted supporting detail line. */
  detail?: React.ReactNode;
  /** Optional mono score / metric shown top-right (e.g. "86 / 100"). */
  score?: React.ReactNode;
  /** Right-aligned action footer (buttons). */
  actions?: React.ReactNode;
  /** Extra body content between the detail line and the footer. */
  children?: React.ReactNode;
}
export declare function ReadinessPanel(props: ReadinessPanelProps): JSX.Element;
