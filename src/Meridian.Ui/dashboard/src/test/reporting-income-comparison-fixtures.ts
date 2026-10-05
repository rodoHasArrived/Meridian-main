import type { IncomeComparison, IncomeComparisonRun, IncomeContributionSupport } from "@/types/reporting-income-comparison";

/** Retained test evidence only; never registered as a development fallback. */
export const publishedIncomeRun: IncomeComparisonRun = {
  runId: "income-published-1",
  templateId: "investment-income",
  templateVersion: "3",
  publicationLabel: "Originally published · revision 1",
  revision: 1,
  restatementOfRunId: null,
  status: "Released",
  periodId: "2026-06",
  asOfDate: "2026-06-30",
  population: "portfolio-credit · book-primary",
  accountingBasis: "Gaap",
  currency: "USD",
  grids: [{ gridId: "investment-income", title: "Investment income", metrics: [{ column: "Income", label: "Investment income amount", sourceField: "InvestmentIncome" }] }]
};

export const restatedIncomeRun: IncomeComparisonRun = {
  ...publishedIncomeRun,
  runId: "income-restated-2",
  publicationLabel: "Restated · revision 2",
  revision: 2,
  restatementOfRunId: publishedIncomeRun.runId
};

export const currentIncomeRun: IncomeComparisonRun = {
  ...restatedIncomeRun,
  runId: "income-current-3",
  publicationLabel: "Restated · revision 3",
  revision: 3
};

export const incomeComparisonCandidates = [publishedIncomeRun, restatedIncomeRun, currentIncomeRun];

export function buildIncomeComparison(overrides: Partial<IncomeComparison> = {}): IncomeComparison {
  return {
    comparisonId: "comparison-retained-1",
    explanationVersion: "1",
    retainedAtUtc: "2026-07-02T10:15:00Z",
    baseline: publishedIncomeRun,
    current: currentIncomeRun,
    gridId: "investment-income",
    metricColumn: "Income",
    status: "Reconciled",
    compatible: true,
    baselineAmount: 1000,
    currentAmount: 1150,
    movement: 150,
    explainedAmount: 150,
    residualAmount: 0,
    differences: [
      { dimension: "Period", baseline: "2026-06", current: "2026-06", compatible: true, detail: "Same retained reporting period." },
      { dimension: "Population", baseline: "portfolio-credit · book-primary", current: "portfolio-credit · book-primary", compatible: true, detail: "Population membership changes require retained evidence." },
      { dimension: "Accounting basis", baseline: "Gaap", current: "Gaap", compatible: true, detail: "Same accounting basis." },
      { dimension: "Currency", baseline: "USD", current: "USD", compatible: true, detail: "Same presentation currency." }
    ],
    contributions: [
      { contributionId: "journal:late-accrual-17", kind: "Journal", label: "Late June interest accrual", amount: 125, recordId: "journal-late-accrual-17", sourceRunId: currentIncomeRun.runId, detail: "Retained posted interest income journal: 125 USD." },
      { contributionId: "population:loan-42", kind: "Population", label: "Added loan 42", amount: 30, recordId: "population-loan-42", sourceRunId: currentIncomeRun.runId, detail: "Income from an added retained population member." },
      { contributionId: "methodology:day-count-2", kind: "Methodology", label: "Documented day-count correction", amount: -5, recordId: "methodology-day-count-2", sourceRunId: currentIncomeRun.runId, detail: "Documented correction supported by retained methodology evidence." }
    ],
    warnings: [],
    gridDiff: {
      gridId: "investment-income",
      title: "Investment income",
      columns: [{ key: "Income", label: "Investment income", role: "Measure" }],
      rows: [{ rowKey: "credit-income", state: "Changed", priorValues: { Income: "1000" }, currentValues: { Income: "1150" }, cells: [{ column: "Income", priorValue: "1000", currentValue: "1150", delta: "150", direction: "Up" }] }],
      warnings: [],
      addedRowCount: 0,
      removedRowCount: 0,
      changedRowCount: 1,
      unchangedRowCount: 0
    },
    ...overrides
  };
}

export function buildIncomeContributionSupport(comparison = buildIncomeComparison(), contributionIndex = 0): IncomeContributionSupport {
  const contribution = comparison.contributions[contributionIndex];
  return {
    comparisonId: comparison.comparisonId,
    baselineRunId: comparison.baseline.runId,
    currentRunId: comparison.current.runId,
    contribution,
    baselineRecords: [],
    currentRecords: [{ RecordId: contribution.recordId, Income: String(contribution.amount), PostingDate: "2026-06-30", JournalLineId: "income-line-2", Account: "Interest income" }],
    evidenceReferences: [`retained-manifest://${comparison.current.runId}/${contribution.recordId}`]
  };
}
