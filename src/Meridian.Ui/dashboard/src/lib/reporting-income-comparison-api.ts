import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import type { IncomeComparison, IncomeComparisonRequest, IncomeComparisonRun, IncomeContributionSupport } from "@/types/reporting-income-comparison";

const root = "/api/fund-structure/reporting/comparisons";
const authoritative = (options: ApiRequestOptions): ApiRequestOptions => ({ ...options, allowDevelopmentFallback: false });

export function getIncomeComparisonCandidates(options: ApiRequestOptions = {}) {
  return apiGetJson<IncomeComparisonRun[]>(`${root}/candidates`, authoritative(options)).then((runs) => {
    if (!Array.isArray(runs) || !runs.every(validRun)) throw new Error("Invalid retained comparison candidates");
    return runs;
  });
}
export function createIncomeComparison(request: IncomeComparisonRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<IncomeComparison>(root, request, authoritative(options)).then(validateComparison);
}
export function getIncomeComparison(comparisonId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<IncomeComparison>(`${root}/${encodeURIComponent(comparisonId)}`, authoritative(options)).then(validateComparison);
}
export function getIncomeContributionSupport(comparisonId: string, contributionId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<IncomeContributionSupport>(
    `${root}/${encodeURIComponent(comparisonId)}/contributions/${encodeURIComponent(contributionId)}`,
    authoritative(options)
  ).then((support) => {
    if (!support || !support.contribution || !Array.isArray(support.baselineRecords) || !Array.isArray(support.currentRecords)
      || ![...support.baselineRecords, ...support.currentRecords].every((record) => !!record && typeof record === "object" && Object.values(record).every((value) => typeof value === "string"))
      || !Array.isArray(support.evidenceReferences) || !support.evidenceReferences.every((value) => typeof value === "string")) {
      throw new Error("Invalid retained contribution support");
    }
    return support;
  });
}

function validRun(run: IncomeComparisonRun | null | undefined): run is IncomeComparisonRun {
  return !!run && typeof run.runId === "string" && typeof run.publicationLabel === "string"
    && typeof run.currency === "string" && typeof run.periodId === "string"
    && Array.isArray(run.grids) && run.grids.every((grid) => !!grid && typeof grid.gridId === "string"
      && Array.isArray(grid.metrics) && grid.metrics.every((metric) => !!metric && typeof metric.column === "string"));
}

function validateComparison(value: IncomeComparison): IncomeComparison {
  if (!value || typeof value.comparisonId !== "string" || !validRun(value.baseline) || !validRun(value.current)
    || typeof value.compatible !== "boolean" || typeof value.status !== "string"
    || ![value.baselineAmount, value.currentAmount, value.movement, value.residualAmount].every((amount) => amount === null || (typeof amount === "number" && Number.isFinite(amount)))
    || typeof value.explainedAmount !== "number" || !Number.isFinite(value.explainedAmount)
    || !Array.isArray(value.differences) || !value.differences.every((item) => !!item && typeof item.dimension === "string" && typeof item.compatible === "boolean")
    || !Array.isArray(value.contributions) || !value.contributions.every((item) => !!item && typeof item.contributionId === "string" && typeof item.kind === "string" && typeof item.amount === "number" && Number.isFinite(item.amount))
    || !Array.isArray(value.warnings) || !value.warnings.every((warning) => typeof warning === "string")
    || !value.gridDiff || !Array.isArray(value.gridDiff.rows) || !value.gridDiff.rows.every((row) => !!row && Array.isArray(row.cells))
    || !Array.isArray(value.gridDiff.columns) || !Array.isArray(value.gridDiff.warnings)) {
    throw new Error("Invalid retained income comparison");
  }
  return value;
}
