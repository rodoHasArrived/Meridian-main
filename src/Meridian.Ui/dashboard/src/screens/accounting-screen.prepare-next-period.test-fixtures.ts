import type { ClosePeriodPlan, LedgerBook, LedgerPeriod, OperationsContinuityWorkflowSummary } from "@/types";
import type { ClosePlanTemplate, ClosePreparationPreview, PreparedClosePlanResult } from "@/types/close-preparation";

export const sourceWorkflow: OperationsContinuityWorkflowSummary = {
  workflowId: "10000000-0000-0000-0000-000000000001", fundAccountId: "10000000-0000-0000-0000-000000000002",
  periodId: "2026-09", ledgerBookId: "10000000-0000-0000-0000-000000000003", securityMasterSnapshotId: null,
  brokerSource: "retained", status: "Closed", version: 12, createdAtUtc: "2026-09-01T00:00:00Z", updatedAtUtc: "2026-10-02T00:00:00Z", gates: [], nextActions: [],
};
export const sourcePlan: ClosePeriodPlan = {
  closePlanId: "september-close", workflowId: sourceWorkflow.workflowId, workflowVersion: 12,
  fundProfileId: "legacy-account-identifier", fundAccountId: sourceWorkflow.fundAccountId, ledgerBookId: sourceWorkflow.ledgerBookId!,
  periodId: "2026-09", periodStart: "2026-09-01", periodEnd: "2026-09-30", closeDueDate: "2026-10-05", isPeriodLocked: true,
  tasks: [{ taskId: "reconcile", displayName: "Reconcile cash", status: "SignedOff", owner: "Alex", dueDate: "2026-10-02", dependencies: [],
    signOffs: [{ signOffId: "prior-signoff", role: "Controller", actor: "Taylor", approvalState: "Approved", signedAtUtc: "2026-10-02T00:00:00Z", evidenceLinks: ["evidence://september"] }],
    evidenceLinks: ["journal://september"], signOffRequirements: [{ requirementId: "prior-requirement", role: "Controller", requiredApprovalCount: 1, approvedCount: 1, isSatisfied: true, evidenceRequirement: "Bank statement" }] },
  { taskId: "review", displayName: "Review close", status: "SignedOff", owner: "Alex", dueDate: "2026-10-05", dependencies: [{ dependencyId: "review-dependency", dependsOnTaskId: "reconcile", reason: "Cash reconciled" }], signOffs: [], evidenceLinks: [] }],
  lateAdjustments: [], materialityPolicy: { policyId: "close-materiality", amountThreshold: 1000, percentThreshold: 1, currency: "USD", reviewRole: "Controller", requiresLateAdjustmentApproval: true }, validationIssues: [],
};
export const targetBook: LedgerBook = {
  ledgerBookId: sourceWorkflow.ledgerBookId!, fundProfileId: "actual-fund", fundStructureNodeId: "entity", fundStructureNodeKind: "Fund",
  displayName: "Operating book", baseCurrency: "USD", accountingBasis: "Primary", accountingPolicyId: "policy", accountingPolicyVersion: "2",
  createdAt: "2026-01-01T00:00:00Z", updatedAt: "2026-10-01T00:00:00Z",
};
export const targetPeriod: LedgerPeriod = {
  periodId: "10000000-0000-0000-0000-000000000004", ledgerBookId: targetBook.ledgerBookId, fiscalYear: 2026, periodNo: 10, label: "October 2026",
  startDate: "2026-10-01", endDate: "2026-10-31", status: "Open", openedAt: "2026-10-01T00:00:00Z", closedAt: null, version: 2,
  accountingBasis: "Primary", accountingPolicyId: "policy", accountingPolicyVersion: "2",
};
export const retainedTemplate: ClosePlanTemplate = {
  templateId: "10000000-0000-0000-0000-000000000005", version: 3, name: "Monthly close", sourceWorkflowId: sourceWorkflow.workflowId,
  sourceLedgerBookId: targetBook.ledgerBookId, sourcePeriodId: sourcePlan.periodId, fundAccountId: sourceWorkflow.fundAccountId, fundProfileId: targetBook.fundProfileId,
  sourceAccountingPolicyId: "policy", sourceAccountingPolicyVersion: "1", materialityPolicy: sourcePlan.materialityPolicy,
  calendar: { calendarId: "Finance calendar", version: "2026.1", weekendDays: [0, 6], holidays: ["2026-11-02"] },
  tasks: sourcePlan.tasks.map((task, index) => ({ configuration: { taskId: task.taskId, displayName: task.displayName, owner: task.owner, dependencyConfigurations: task.dependencies,
    signOffRequirementConfigurations: [{ role: "Controller", requiredApprovalCount: 1, evidenceRequirement: "Bank statement" }] }, sourceDueDate: task.dueDate,
  deadlineRule: { taskId: task.taskId, anchor: "PeriodEnd", offsetDays: index + 2, dayCount: "BusinessDays", adjustment: "FollowingBusinessDay" } })),
  capturedAtUtc: "2026-10-06T12:00:00Z", capturedBy: "Morgan", history: [{ eventType: "TemplateCaptured", occurredAtUtc: "2026-10-06T12:00:00Z", actor: "Morgan", description: "Captured configuration from September close." }],
};
export const preparationPreview: ClosePreparationPreview = {
  previewId: "10000000-0000-0000-0000-000000000006", templateId: retainedTemplate.templateId, templateVersion: retainedTemplate.version, sourceWorkflowId: sourceWorkflow.workflowId,
  targetBook, targetPeriod, calendar: retainedTemplate.calendar,
  tasks: retainedTemplate.tasks.map((task, index) => ({ taskId: task.configuration.taskId, displayName: task.configuration.displayName!, sourceOwner: "Alex", owner: "Sam", sourceDueDate: task.sourceDueDate,
    dueDate: index === 0 ? "2026-11-04" : "2026-11-05", deadlineRule: task.deadlineRule, dependencies: task.configuration.dependencyConfigurations!, signOffRequirements: task.configuration.signOffRequirementConfigurations!, ownerChanged: true })),
  issues: [], policyChanged: true, canCreate: true, createdAtUtc: "2026-10-06T12:00:00Z", expiresAtUtc: "2099-10-06T12:15:00Z",
};
export const preparedResult: PreparedClosePlanResult = {
  workflowId: "10000000-0000-0000-0000-000000000007", templateId: retainedTemplate.templateId, templateVersion: retainedTemplate.version, sourceWorkflowId: sourceWorkflow.workflowId,
  targetLedgerBookId: targetBook.ledgerBookId, targetPeriodId: targetPeriod.periodId, createdAtUtc: "2026-10-06T12:05:00Z", createdBy: "Morgan", wasAlreadyCreated: false,
  plan: { ...sourcePlan, closePlanId: "october-close", workflowId: "10000000-0000-0000-0000-000000000007", periodId: targetPeriod.periodId, periodStart: targetPeriod.startDate,
    periodEnd: targetPeriod.endDate, isPeriodLocked: false, tasks: sourcePlan.tasks.map(task => ({ ...task, status: "NotStarted", signOffs: [], evidenceLinks: [] })) },
  history: [{ eventType: "PlanPrepared", occurredAtUtc: "2026-10-06T12:05:00Z", actor: "Morgan", description: "Prepared October with fresh tasks and required sign-offs." }],
};
