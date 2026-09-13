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

function resolveLatestPeriodEnd(runs: readonly RunRow[]): string | null {
  const confirmed = runs
    .map((run) => run.asOfDateLabel?.trim() ?? "")
    .filter((value) => hasRetainedReportingAsOfDateValue(value) && isIsoDate(value));

  if (confirmed.length === 0) {
    return null;
  }

  return confirmed.reduce((latest, candidate) => (compareIsoDate(candidate, latest) > 0 ? candidate : latest));
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
  evaluationAtUtc?: string
): ReportingProductionSurfaceViewModel {
  const runs: ReportingProductionRunInput[] = runRows.map((row) => ({
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

  const periodEnd = resolveLatestPeriodEnd(runRows);

  const production = buildReportingProductionModel({
    runs,
    templates,
    signals: { scheduleCount: scheduleRows.length > 0 ? scheduleRows.length : null },
    periodLabel: null,
    asOfDate: periodEnd,
    evaluationAtUtc
  });

  // Milestones beyond period end are not asserted by any current read model, so the
  // period object reports them as unset rather than deriving plausible-looking dates.
  const period = periodEnd
    ? buildReportingPeriodModel({ periodId: periodEnd, periodEnd }, evaluationAtUtc?.slice(0, 10))
    : null;

  return { production, period };
}
