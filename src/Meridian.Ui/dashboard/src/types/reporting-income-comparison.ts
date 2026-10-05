import type { ReportWriterGridDiff } from "@/types";

/** Exact invariant decimal text supplied by retained server evidence; never parse as a JS number. */
export type IncomeComparisonAmount = string;

export interface IncomeComparisonMetric { column: string; label: string; sourceField: string | null }
export interface IncomeComparisonGrid { gridId: string; title: string; metrics: IncomeComparisonMetric[] }
export interface IncomeComparisonRun {
  runId: string;
  templateId: string;
  templateVersion: string | null;
  publicationLabel: string;
  revision: number | null;
  restatementOfRunId: string | null;
  status: string;
  periodId: string;
  asOfDate: string;
  population: string;
  accountingBasis: string;
  currency: string;
  grids: IncomeComparisonGrid[];
}
export interface IncomeComparisonRequest {
  baselineRunId: string;
  currentRunId: string;
  gridId: string;
  metricColumn: string;
}
export interface IncomeContribution {
  contributionId: string;
  kind: string;
  label: string;
  amount: IncomeComparisonAmount;
  recordId: string;
  sourceRunId: string;
  detail: string;
}
export interface IncomeComparison {
  comparisonId: string;
  explanationVersion: string;
  retainedAtUtc: string;
  baseline: IncomeComparisonRun;
  current: IncomeComparisonRun;
  gridId: string;
  metricColumn: string;
  status: string;
  compatible: boolean;
  baselineAmount: IncomeComparisonAmount | null;
  currentAmount: IncomeComparisonAmount | null;
  movement: IncomeComparisonAmount | null;
  explainedAmount: IncomeComparisonAmount;
  residualAmount: IncomeComparisonAmount | null;
  differences: { dimension: string; baseline: string; current: string; compatible: boolean; detail: string }[];
  contributions: IncomeContribution[];
  warnings: string[];
  gridDiff: ReportWriterGridDiff;
}
export interface IncomeContributionSupport {
  comparisonId: string;
  baselineRunId: string;
  currentRunId: string;
  contribution: IncomeContribution;
  baselineRecords: Record<string, string>[];
  currentRecords: Record<string, string>[];
  evidenceReferences: string[];
}
