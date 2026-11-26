import type {
  AccountingReportPackageBundle,
  AccountingReportPackageRequest,
  ClosePeriodPlan,
  OperationsContinuityWorkflow
} from "@/types";

/**
 * The close report-package request and the close evidence it carries. Split from the close cockpit
 * view model, which sits on the repository's no-new-god-file cap.
 */
export type AccountingReportPackageRequestResult =
  | { ok: true; request: AccountingReportPackageRequest }
  | { ok: false; missingInputs: string[]; message: string };

/**
 * Builds the close report-package request, or refuses. Amounts and the currency are financial
 * inputs: the request never invents 0 or "USD" for one the close workflow has not supplied,
 * because the server would certify a package against those invented figures. A refusal names
 * every missing input so the operator knows what to load first.
 */
export function buildAccountingReportPackageRequest(
  workflow: OperationsContinuityWorkflow,
  closePlan: ClosePeriodPlan | null,
  seedPackage: AccountingReportPackageBundle | null
): AccountingReportPackageRequestResult {
  const investorStatement = seedPackage?.investorCapitalStatements[0] ?? null;
  const beginningCapital = finiteOrNull(investorStatement?.beginningCapital);
  const contributions = finiteOrNull(investorStatement?.contributions);
  const distributions = finiteOrNull(investorStatement?.distributions);
  const realizedGainLoss = finiteOrNull(seedPackage?.realizedGainLoss.realizedGainLoss) ?? finiteOrNull(investorStatement?.realizedGainLoss);
  const nav = finiteOrNull(seedPackage?.navPackage.nav) ?? finiteOrNull(investorStatement?.endingCapital);
  const currency = seedPackage?.navPackage.currency?.trim() || investorStatement?.currency?.trim() || null;
  const missingInputs = ([
    ["beginning capital", beginningCapital],
    ["contributions", contributions],
    ["distributions", distributions],
    ["realized gain/loss", realizedGainLoss],
    ["NAV", nav],
    ["currency", currency]
  ] as const).filter(([, value]) => value === null).map(([label]) => label);

  if (missingInputs.length > 0
    || beginningCapital === null || contributions === null || distributions === null
    || realizedGainLoss === null || nav === null || currency === null) {
    return {
      ok: false,
      missingInputs,
      message: `Report package not built: ${missingInputs.join(", ")} ${missingInputs.length === 1 ? "is" : "are"} not available for this close. Load the investor capital statement and NAV package first; Meridian does not substitute 0 or a default currency.`
    };
  }

  return {
    ok: true,
    request: {
      fundProfileId: closePlan?.fundProfileId ?? workflow.fundAccountId,
      ledgerBookId: closePlan?.ledgerBookId ?? seedPackage?.financialStatements.ledgerBookId ?? null,
      periodId: closePlan?.periodId ?? workflow.periodId,
      actor: "browser-accounting-operator",
      closeWorkflowId: workflow.workflowId,
      capitalAccountId: investorStatement?.capitalAccountId ?? null,
      investorId: investorStatement?.investorId ?? null,
      beginningCapital,
      contributions,
      distributions,
      realizedGainLoss,
      nav,
      currency,
      evidenceLinks: collectAccountingCloseEvidenceLinks(workflow, closePlan),
      correlationId: `browser-close-report-${workflow.workflowId}`
    }
  };
}

function finiteOrNull(value: number | null | undefined): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

export function collectAccountingCloseEvidenceLinks(
  workflow: OperationsContinuityWorkflow,
  closePlan: ClosePeriodPlan | null
): string[] {
  const links = new Set<string>();
  const add = (value: string | null | undefined) => {
    const normalized = value?.trim();
    if (normalized) {
      links.add(normalized);
    }
  };
  const addOperationsEvidence = (evidence: Array<{ evidenceId: string; route: string | null }> | null | undefined) => {
    evidence?.forEach((item) => {
      add(item.route);
      add(item.evidenceId);
    });
  };

  addOperationsEvidence(workflow.evidenceLinks);
  addOperationsEvidence(workflow.reportPackReadiness.evidenceLinks);
  addOperationsEvidence(workflow.accountingRecordSummary?.evidenceLinks);
  workflow.accountingRecordSummary?.evidenceCategories.forEach((category) => addOperationsEvidence(category.evidenceLinks));
  addOperationsEvidence(workflow.closePackage?.evidenceLinks);
  add(workflow.closePackage?.retainedManifestRoute);
  add(workflow.closePackage?.evidenceHash);
  closePlan?.tasks.forEach((task) => task.evidenceLinks.forEach(add));
  closePlan?.lateAdjustments.forEach((adjustment) => adjustment.evidenceLinks.forEach(add));

  return [...links];
}
