/**
 * SLA chip — the case clock. Mirrors the reconciliation case SLA model
 * (`ReconciliationCaseSummaryDto`: `SlaState` OnTrack | Warning | Breached,
 * `SlaDueAtUtc`, `AgeBand`, `BusinessAgeHours`). Renders a mono severity-tinted
 * chip: state dot · "SLA · on track · due 2h 14m" (or "over by 3h 05m" past due).
 * Deterministic — it never self-ticks; pass `now` to pin the countdown in demos
 * and re-render to advance it in live views.
 *
 * @example
 * <SlaChip state="OnTrack"  dueAtUtc="2026-07-05T18:00:00Z" now="2026-07-05T14:32:00Z" />
 * <SlaChip state="Breached" dueAtUtc="2026-07-05T09:00:00Z" now="2026-07-05T14:32:00Z" />
 * <SlaChip state="Warning"  ageBand="1–2 days" />
 */
export interface SlaChipProps extends React.HTMLAttributes<HTMLElement> {
  /** SLA state string (OnTrack, Warning/AtRisk, Breached, Paused, …). @default "OnTrack" */
  state?: string;
  /** ISO UTC due timestamp — renders a due-in / over-by duration. */
  dueAtUtc?: string;
  /** Pre-bucketed age label (used when no `dueAtUtc`), e.g. "1–2 days". */
  ageBand?: string;
  /** Business-hours age (used when neither `dueAtUtc` nor `ageBand` is set). */
  businessAgeHours?: number;
  /** Clock reference — ISO string or epoch ms. @default Date.now() */
  now?: string | number;
  /** Leading label. Pass "" to drop it. @default "SLA" */
  label?: string;
}
export declare function SlaChip(props: SlaChipProps): JSX.Element;
/** Format a millisecond duration as "2d 4h" / "3h 12m" / "9m". */
export declare function formatSlaDuration(ms: number): string;
