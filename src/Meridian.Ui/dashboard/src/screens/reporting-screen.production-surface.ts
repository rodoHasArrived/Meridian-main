/**
 * Adapts the reporting screen view-model onto the production control surface.
 *
 * Everything here is a projection of data the workspace already returns. Where a
 * governed fact is genuinely absent - report owner, accounting close, reporting
 * cutoff - it is left unset so the surface can say "not set" rather than implying
 * a milestone or an owner that no system has actually asserted.
 */
import {
  buildReportingProductionModel,
  type ReportingDailyWorkInput,
  type ReportingProductionModel,
  type ReportingProductionRunInput,
  type ReportingProductionTemplateInput
} from "@/lib/reporting-production";
import {
  buildReportingPeriodModel,
  type ReportingPeriodModel
} from "@/lib/reporting-period-object";
import { compareIsoDate, hasRetainedReportingAsOfDateValue, isIsoDate } from "@/lib/reporting-periods";
import type {
  ReportingRunStatusRow,
  ReportingScheduleRow,
  ReportingTemplateRow
} from "@/screens/reporting-screen.view-model";

/** Structural subset of the run row the production surface needs. */
type RunRow = Pick<
  ReportingRunStatusRow,
  "id" | "templateId" | "family" | "status" | "asOfDateLabel" | "isLatestGenerated" | "failureReason"
>;

/** Structural subset of the template row the production surface needs. */
type TemplateRow = Pick<ReportingTemplateRow, "id" | "name" | "family">;

export interface ReportingProductionSurfaceViewModel {
  production: ReportingProductionModel;
  /** Null when no run carries a confirmed reporting period. */
  period: ReportingPeriodModel | null;
}

const MONTH_TOKEN_PATTERN = /^(\d{4})-(0[1-9]|1[0-2])$/;

/** The reporting period a run belongs to, or null when none was retained. */
function periodTokenOf(run: RunRow): string | null {
  const value = run.asOfDateLabel?.trim() ?? "";
  return hasRetainedReportingAsOfDateValue(value) ? value : null;
}

/**
 * Maps a retained period token onto a comparable calendar date.
 *
 * Governed report packs retain a period identifier rather than a date -
 * `ReportPackRunReadService` projects `record.Period`, whose values include month
 * tokens (`2026-06`), period numbers (`2026-P03`) and relative tokens
 * (`CurrentMonth`). Month tokens carry a well-defined period end and are
 * normalized to it; the rest are retained but not orderable, so they return null
 * and are ranked by the source's own ordering instead of being discarded.
 */
function comparablePeriodEnd(token: string): string | null {
  if (isIsoDate(token)) {
    return token;
  }
  const month = MONTH_TOKEN_PATTERN.exec(token);
  if (!month) {
    return null;
  }
  const [, year, monthOfYear] = month;
  const lastDay = new Date(Date.UTC(Number(year), Number(monthOfYear), 0)).getUTCDate();
  return `${year}-${monthOfYear}-${String(lastDay).padStart(2, "0")}`;
}

interface ResolvedReportingPeriod {
  /** The raw token as the source retained it, used to scope the register. */
  token: string;
  /** Calendar period end, when the token maps onto one. */
  periodEnd: string | null;
}

/**
 * Resolves the period the surface is reporting on.
 *
 * `recentRuns` is an update-ordered history spanning several periods, so the
 * latest orderable period wins; when no token is orderable the source's own
 * ordering decides, its first entry being the most recently updated.
 */
function resolveReportingPeriod(runs: readonly RunRow[]): ResolvedReportingPeriod | null {
  let best: ResolvedReportingPeriod | null = null;

  for (const run of runs) {
    const token = periodTokenOf(run);
    if (token === null) {
      continue;
    }

    const periodEnd = comparablePeriodEnd(token);
    if (best === null) {
      best = { token, periodEnd };
      continue;
    }

    if (periodEnd !== null && (best.periodEnd === null || compareIsoDate(periodEnd, best.periodEnd) > 0)) {
      best = { token, periodEnd };
    }
  }

  return best;
}

/**
 * Builds the production control surface model from the reporting view-model rows.
 *
 * `evaluationAtUtc` is injectable so the overdue calculation stays deterministic
 * under test.
 */
export function buildReportingProductionSurfaceViewModel(
  runRows: readonly RunRow[],
  templateRows: readonly TemplateRow[],
  scheduleRows: readonly Pick<ReportingScheduleRow, "id">[] = [],
  evaluationAtUtc?: string,
  dailyWorkRows: readonly ReportingDailyWorkInput[] = []
): ReportingProductionSurfaceViewModel {
  const resolvedPeriod = resolveReportingPeriod(runRows);

  // The register is a register of one reporting period. `recentRuns` spans
  // several, and `isLatestGenerated` is scoped per run series rather than per
  // period, so passing the whole history would let an older-period run sit under
  // the current period's heading or displace the current run for its template.
  // Runs that retained no period are kept - they are visibly guarded rather than
  // hidden - but a run belonging to a different, identifiable period is not.
  const periodScopedRuns = resolvedPeriod === null
    ? runRows
    : runRows.filter((row) => {
        const token = periodTokenOf(row);
        return token === null || token === resolvedPeriod.token;
      });

  const runs: ReportingProductionRunInput[] = periodScopedRuns.map((row) => ({
    runId: row.id,
    templateId: row.templateId,
    family: row.family,
    status: row.status,
    asOfDate: row.asOfDateLabel,
    isLatestGenerated: row.isLatestGenerated,
    blockingReasons: row.failureReason ? [row.failureReason] : []
  }));

  const templates: ReportingProductionTemplateInput[] = templateRows.map((row) => ({
    templateId: row.id,
    name: row.name,
    family: row.family
  }));

  const production = buildReportingProductionModel({
    runs,
    templates,
    signals: {
      scheduleCount: scheduleRows.length > 0 ? scheduleRows.length : null,
      dailyWork: dailyWorkRows
    },
    periodLabel: null,
    asOfDate: resolvedPeriod?.token ?? null,
    evaluationAtUtc
  });

  // The period object needs a calendar period end. Tokens that do not map onto
  // one (period numbers, relative tokens) still scope the register and label the
  // surface, but cannot drive milestones, so no period strip is claimed for them.
  // Milestones beyond period end are not asserted by any current read model, so
  // the period object reports them as unset rather than inventing plausible dates.
  const periodEnd = resolvedPeriod?.periodEnd ?? null;
  const period = periodEnd
    ? buildReportingPeriodModel({ periodId: periodEnd, periodEnd }, evaluationAtUtc?.slice(0, 10))
    : null;

  return { production, period };
}
