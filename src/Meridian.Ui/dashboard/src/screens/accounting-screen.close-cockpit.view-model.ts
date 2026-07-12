import { useCallback, useEffect, useMemo, useState } from "react";
import {
  buildLedgerAccountingReportPackage,
  certifyLedgerAccountingReportPackage,
  configureLedgerCloseManagementPeriodPlan,
  createLedgerCloseManagementLateAdjustment,
  getLedgerAccountingReportPackageExport,
  getLedgerCloseManagementPeriodPlan,
  listLedgerAccountingReportPackages,
  lockLedgerCloseManagementPeriod,
  reviewLedgerCloseManagementEvidence,
  reviewLedgerCloseManagementLateAdjustment,
  signOffLedgerCloseManagementTask,
} from "@/lib/api";
import {
  normalizeLocalWorkstationRoute,
  WORKSTATION_ROUTE_CATALOG,
} from "@/lib/workspace";
import {
  accountingWorkstreamHref,
  buildAccountingTaskMode,
  type AccountingTaskModeViewModel,
  type AccountingWorkstream,
} from "./accounting-screen.task-mode-view-model";
import {
  formatCount,
  formatCurrencyWithCode,
  formatDateOnly,
  formatDateTimeLabel,
} from "./accounting-screen.formatting";
import type {
  AccountingCloseCalendarMilestoneViewModel,
  AccountingCloseDependencyGraphRowViewModel,
  AccountingCloseEvidenceReviewRowViewModel,
  AccountingCloseOperatingCoverageRowViewModel,
  AccountingClosePlanTaskRowViewModel,
  AccountingCloseReportPackageServices,
  AccountingCloseReportPackageViewModel,
  AccountingCloseSetupDependencyOptionViewModel,
  AccountingCloseSetupDraftViewModel,
  AccountingCloseSetupSignOffRoleOptionViewModel,
  AccountingCloseSetupTaskOptionViewModel,
  AccountingCloseSignOffDecisionOptionViewModel,
  AccountingCloseSignOffDecision,
  AccountingCloseSignOffDraftViewModel,
  AccountingCloseSignOffMatrixRowViewModel,
  AccountingCloseSignOffRoleOptionViewModel,
  AccountingCloseSignOffTaskOptionViewModel,
  AccountingCloseWorkflowStepViewModel,
  AccountingConfigurationIssueViewModel,
  AccountingLateAdjustmentDraftViewModel,
  AccountingLateAdjustmentRowViewModel,
  AccountingReportCertificationSafeguardViewModel,
  AccountingReportExportManifestViewModel,
  AccountingReportPackageRowViewModel,
  AccountingToolingTone,
  AccountingWorkflowActionViewModel,
  AccountingWorkflowLaunchViewState,
  AccountingWorkflowStepViewModel,
  CloseCommandCenterActionViewModel,
  CloseCommandCenterMetricViewModel,
  CloseCommandCenterStatus,
  CloseCommandCenterViewState,
} from "./accounting-screen.view-model";
import type {
  AccountingReportPackageBundle,
  AccountingReportPackageRequest,
  AccountingWorkspaceResponse,
  CertifyAccountingReportPackageRequest,
  CloseCalendarMilestone,
  CloseDependency,
  ClosePeriodPlan,
  CloseSignOffRequirement,
  CloseTask,
  FinancialOperationsCommandCenter,
  LateAdjustmentRequest,
  LockClosePeriodRequest,
  MultiAssetCoverageSummary,
  OperationsContinuityWorkflow,
  OperationsWorkflowBlocker,
  ReportExportArtifact,
  ReportExportArtifactManifest,
  UpsertClosePeriodPlanConfigurationRequest,
  AccountingSystemImportDetail,
  AccountingSystemProvider,
  AccountingSystemReconciliationSummary,
  AccountingCertificationState,
} from "@/types";

interface CloseCommandCenterRawBlocker {
  code: string;
  category: string;
  severity: string;
  message: string;
  gate: OperationsWorkflowBlocker["gate"];
  routeHint: string | null;
}

const defaultAccountingCloseReportPackageServices: AccountingCloseReportPackageServices = {
  getClosePlan: (workflowId) => getLedgerCloseManagementPeriodPlan(workflowId),
  createLateAdjustment: (request) => createLedgerCloseManagementLateAdjustment(request),
  reviewLateAdjustment: (request) => reviewLedgerCloseManagementLateAdjustment(request),
  signOffCloseTask: (request) => signOffLedgerCloseManagementTask(request),
  reviewCloseEvidence: (request) => reviewLedgerCloseManagementEvidence(request),
  configureClosePlan: (request) => configureLedgerCloseManagementPeriodPlan(request),
  lockClosePeriod: (request) => lockLedgerCloseManagementPeriod(request),
  buildPackage: (request) => buildLedgerAccountingReportPackage(request),
  certifyPackage: (request) => certifyLedgerAccountingReportPackage(request),
  getExportManifest: (packageId, artifactId) => getLedgerAccountingReportPackageExport(packageId, artifactId),
  listPackages: (query) => listLedgerAccountingReportPackages(query)
};

export function useAccountingCloseReportPackageViewModel(
  workflow: OperationsContinuityWorkflow | null,
  services: AccountingCloseReportPackageServices = defaultAccountingCloseReportPackageServices
): AccountingCloseReportPackageViewModel {
  const [closePlan, setClosePlan] = useState<ClosePeriodPlan | null>(null);
  const [packages, setPackages] = useState<AccountingReportPackageBundle[]>([]);
  const [selectedPackageId, setSelectedPackageId] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [errorText, setErrorText] = useState<string | null>(null);
  const [buildBusy, setBuildBusy] = useState(false);
  const [buildStatusText, setBuildStatusText] = useState<string | null>(null);
  const [buildStatusTone, setBuildStatusTone] = useState<"neutral" | "success" | "danger">("neutral");
  const [certifyBusy, setCertifyBusy] = useState(false);
  const [certifyStatusText, setCertifyStatusText] = useState<string | null>(null);
  const [certifyStatusTone, setCertifyStatusTone] = useState<"neutral" | "success" | "danger">("neutral");
  const [signOffBusy, setSignOffBusy] = useState(false);
  const [signOffStatusText, setSignOffStatusText] = useState<string | null>(null);
  const [signOffStatusTone, setSignOffStatusTone] = useState<"neutral" | "success" | "danger">("neutral");
  const [lockClosePeriodBusy, setLockClosePeriodBusy] = useState(false);
  const [lockClosePeriodStatusText, setLockClosePeriodStatusText] = useState<string | null>(null);
  const [lockClosePeriodStatusTone, setLockClosePeriodStatusTone] = useState<"neutral" | "success" | "danger">("neutral");
  const [configureClosePlanBusy, setConfigureClosePlanBusy] = useState(false);
  const [configureClosePlanStatusText, setConfigureClosePlanStatusText] = useState<string | null>(null);
  const [configureClosePlanStatusTone, setConfigureClosePlanStatusTone] = useState<"neutral" | "success" | "danger">("neutral");
  const [closeSetupDraft, setCloseSetupDraft] = useState<AccountingCloseSetupDraftViewModel>(() => createAccountingCloseSetupDraft(null));
  const [closeSignOffDraft, setCloseSignOffDraft] = useState<AccountingCloseSignOffDraftViewModel>(() => createAccountingCloseSignOffDraft(null));
  const [createLateAdjustmentBusy, setCreateLateAdjustmentBusy] = useState(false);
  const [createLateAdjustmentStatusText, setCreateLateAdjustmentStatusText] = useState<string | null>(null);
  const [createLateAdjustmentStatusTone, setCreateLateAdjustmentStatusTone] = useState<"neutral" | "success" | "danger">("neutral");
  const [lateAdjustmentDraft, setLateAdjustmentDraft] = useState<AccountingLateAdjustmentDraftViewModel>(() => createAccountingLateAdjustmentDraft());
  const [reviewLateAdjustmentBusy, setReviewLateAdjustmentBusy] = useState(false);
  const [reviewLateAdjustmentStatusText, setReviewLateAdjustmentStatusText] = useState<string | null>(null);
  const [reviewLateAdjustmentStatusTone, setReviewLateAdjustmentStatusTone] = useState<"neutral" | "success" | "danger">("neutral");
  const [reviewCloseEvidenceBusy, setReviewCloseEvidenceBusy] = useState(false);
  const [reviewCloseEvidenceStatusText, setReviewCloseEvidenceStatusText] = useState<string | null>(null);
  const [reviewCloseEvidenceStatusTone, setReviewCloseEvidenceStatusTone] = useState<"neutral" | "success" | "danger">("neutral");
  const [exportManifestBusy, setExportManifestBusy] = useState(false);
  const [exportManifestStatusText, setExportManifestStatusText] = useState<string | null>(null);
  const [exportManifestStatusTone, setExportManifestStatusTone] = useState<"neutral" | "success" | "danger">("neutral");
  const [exportManifest, setExportManifest] = useState<ReportExportArtifactManifest | null>(null);

  const refresh = useCallback(async () => {
    if (!workflow) {
      setClosePlan(null);
      setPackages([]);
      setSelectedPackageId(null);
      return;
    }

    setLoading(true);
    setErrorText(null);
    try {
      const nextClosePlan = await services.getClosePlan(workflow.workflowId);
      const nextPackages = await services.listPackages({
        fundProfileId: nextClosePlan.fundProfileId || workflow.fundAccountId,
        periodId: nextClosePlan.periodId || workflow.periodId,
        ledgerBookId: nextClosePlan.ledgerBookId ?? null
      });
      setClosePlan(nextClosePlan);
      setPackages(nextPackages);
      setSelectedPackageId((current) => {
        if (current && nextPackages.some((item) => item.financialStatements.packageId === current)) {
          return current;
        }

        return nextPackages[0]?.financialStatements.packageId ?? null;
      });
      setCloseSetupDraft(createAccountingCloseSetupDraft(nextClosePlan));
      setCloseSignOffDraft(createAccountingCloseSignOffDraft(nextClosePlan));
    } catch (error) {
      setErrorText(formatAccountingWorkflowError(error, "Close/report package detail could not be loaded."));
    } finally {
      setLoading(false);
    }
  }, [services, workflow]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const buildReportPackage = useCallback(async () => {
    if (!workflow) {
      setBuildStatusText("A close workflow is required before building a report package.");
      setBuildStatusTone("danger");
      return;
    }

    setBuildBusy(true);
    setBuildStatusText(null);
    setBuildStatusTone("neutral");
    try {
      const request = buildAccountingReportPackageRequest(workflow, closePlan, packages[0] ?? null);
      const bundle = await services.buildPackage(request);
      setPackages((current) => [
        bundle,
        ...current.filter((item) => item.financialStatements.packageId !== bundle.financialStatements.packageId)
      ]);
      setSelectedPackageId(bundle.financialStatements.packageId);
      setExportManifest(null);
      setBuildStatusText(`Built report package ${bundle.financialStatements.packageId}.`);
      setBuildStatusTone("success");
    } catch (error) {
      setBuildStatusText(formatAccountingWorkflowError(error, "Report package could not be built."));
      setBuildStatusTone("danger");
    } finally {
      setBuildBusy(false);
    }
  }, [closePlan, packages, services, workflow]);

  const certifyPackage = useCallback(async () => {
    const selectedBundle = packages.find((bundle) => bundle.financialStatements.packageId === selectedPackageId) ?? packages[0] ?? null;
    if (!selectedBundle) {
      setCertifyStatusText("A ready report package is required before certification.");
      setCertifyStatusTone("danger");
      return;
    }

    setCertifyBusy(true);
    setCertifyStatusText(null);
    setCertifyStatusTone("neutral");
    try {
      const request = buildCertifyAccountingReportPackageRequest(selectedBundle);
      const certified = await services.certifyPackage(request);
      setPackages((current) => [
        certified,
        ...current.filter((item) => item.financialStatements.packageId !== certified.financialStatements.packageId)
      ]);
      setSelectedPackageId(certified.financialStatements.packageId);
      setExportManifest(null);
      setCertifyStatusText(`Certified report package ${certified.financialStatements.packageId}.`);
      setCertifyStatusTone("success");
    } catch (error) {
      setCertifyStatusText(formatAccountingWorkflowError(error, "Report package could not be certified."));
      setCertifyStatusTone("danger");
    } finally {
      setCertifyBusy(false);
    }
  }, [packages, selectedPackageId, services]);

  const signOffNextTask = useCallback(async () => {
    if (!workflow || !closePlan) {
      setSignOffStatusText("A close plan is required before signing off a checklist task.");
      setSignOffStatusTone("danger");
      return;
    }

    const signOffValidation = validateCloseSignOffDraft(closePlan, closeSignOffDraft);
    if (signOffValidation) {
      setSignOffStatusText(signOffValidation);
      setSignOffStatusTone("danger");
      return;
    }

    const signOffTarget = resolveCloseTaskSignOffDraftTarget(closePlan, closeSignOffDraft);
    if (!signOffTarget) {
      setSignOffStatusText("The selected close checklist task is not available for sign-off.");
      setSignOffStatusTone("danger");
      return;
    }

    const notes = closeSignOffDraft.notes.trim()
      || `${closeSignOffDraft.decision} ${signOffTarget.task.displayName} close checklist task from the Accounting close cockpit.`;
    setSignOffBusy(true);
    setSignOffStatusText(null);
    setSignOffStatusTone("neutral");
    try {
      const nextPlan = await services.signOffCloseTask({
        workflowId: workflow.workflowId,
        taskId: signOffTarget.task.taskId,
        role: signOffTarget.role,
        decision: closeSignOffDraft.decision,
        actor: "browser-accounting-controller",
        notes,
        evidenceLinks: [
          `browser://accounting/close/sign-off/${workflow.workflowId}/${signOffTarget.task.taskId}`,
          `browser://accounting/close/sign-off/${workflow.workflowId}/${signOffTarget.task.taskId}/${signOffTarget.role}`,
          ...signOffTarget.task.evidenceLinks
        ],
        correlationId: `browser-close-signoff-${workflow.workflowId}-${signOffTarget.task.taskId}-${closeSignOffDraft.decision.toLowerCase()}`
      });
      setClosePlan(nextPlan);
      setCloseSignOffDraft(createAccountingCloseSignOffDraft(nextPlan));
      setSignOffStatusText(`${closeSignOffDraft.decision} sign-off retained for ${signOffTarget.task.displayName}.`);
      setSignOffStatusTone("success");
    } catch (error) {
      setSignOffStatusText(formatAccountingWorkflowError(error, "Close checklist task could not be signed off."));
      setSignOffStatusTone("danger");
    } finally {
      setSignOffBusy(false);
    }
  }, [closePlan, closeSignOffDraft, services, workflow]);

  const lockClosePeriod = useCallback(async () => {
    if (!workflow || !closePlan) {
      setLockClosePeriodStatusText("A close plan is required before locking the close period.");
      setLockClosePeriodStatusTone("danger");
      return;
    }

    if (closePlan.isPeriodLocked) {
      setLockClosePeriodStatusText("The period is already locked.");
      setLockClosePeriodStatusTone("success");
      return;
    }

    const selectedBundle = packages.find((bundle) => bundle.financialStatements.packageId === selectedPackageId) ?? packages[0] ?? null;
    setLockClosePeriodBusy(true);
    setLockClosePeriodStatusText(null);
    setLockClosePeriodStatusTone("neutral");
    try {
      const result = await services.lockClosePeriod(buildClosePeriodLockRequest(workflow, closePlan, selectedBundle));
      if (result.plan) {
        setClosePlan(result.plan);
      }

      const blockingIssueCount = result.issues.filter((issue) => issue.severity === "Critical").length;
      if (result.isLocked) {
        setLockClosePeriodStatusText(`Locked close period ${result.plan?.periodId ?? closePlan.periodId}.`);
        setLockClosePeriodStatusTone("success");
      } else if (blockingIssueCount > 0) {
        setLockClosePeriodStatusText(`Close period lock blocked by ${formatCount(blockingIssueCount, "critical issue")}.`);
        setLockClosePeriodStatusTone("danger");
      } else if (result.issues.length > 0) {
        setLockClosePeriodStatusText(result.issues.map((issue) => issue.message).join(" "));
        setLockClosePeriodStatusTone("neutral");
      } else {
        setLockClosePeriodStatusText("Close period lock did not complete.");
        setLockClosePeriodStatusTone("danger");
      }
    } catch (error) {
      setLockClosePeriodStatusText(formatAccountingWorkflowError(error, "Close period could not be locked."));
      setLockClosePeriodStatusTone("danger");
    } finally {
      setLockClosePeriodBusy(false);
    }
  }, [closePlan, packages, selectedPackageId, services, workflow]);

  const configureClosePlan = useCallback(async () => {
    if (!workflow || !closePlan) {
      setConfigureClosePlanStatusText("A close plan is required before configuring close setup.");
      setConfigureClosePlanStatusTone("danger");
      return;
    }

    if (closePlan.isPeriodLocked) {
      setConfigureClosePlanStatusText("The period is locked; close-plan setup changes require a governed reopen workflow.");
      setConfigureClosePlanStatusTone("danger");
      return;
    }

    const closeSetupMaterialityValidation = validateCloseSetupMaterialityDraft(closeSetupDraft);
    if (closeSetupMaterialityValidation) {
      setConfigureClosePlanStatusText(closeSetupMaterialityValidation);
      setConfigureClosePlanStatusTone("danger");
      return;
    }

    const closeSetupTaskValidation = validateCloseSetupTaskSelection(closePlan, closeSetupDraft);
    if (closeSetupTaskValidation) {
      setConfigureClosePlanStatusText(closeSetupTaskValidation);
      setConfigureClosePlanStatusTone("danger");
      return;
    }

    const closeSetupSignOffValidation = validateCloseSetupSignOffDraft(closeSetupDraft);
    if (closeSetupSignOffValidation) {
      setConfigureClosePlanStatusText(closeSetupSignOffValidation);
      setConfigureClosePlanStatusTone("danger");
      return;
    }

    setConfigureClosePlanBusy(true);
    setConfigureClosePlanStatusText(null);
    setConfigureClosePlanStatusTone("neutral");
    try {
      const nextPlan = await services.configureClosePlan(buildClosePlanConfigurationRequest(workflow, closePlan, closeSetupDraft));
      setClosePlan(nextPlan);
      setCloseSetupDraft(createAccountingCloseSetupDraft(nextPlan));
      setCloseSignOffDraft(createAccountingCloseSignOffDraft(nextPlan, closeSignOffDraft.taskId));
      setConfigureClosePlanStatusText(`Retained close-plan setup for ${nextPlan.periodId}.`);
      setConfigureClosePlanStatusTone("success");
    } catch (error) {
      setConfigureClosePlanStatusText(formatAccountingWorkflowError(error, "Close-plan setup could not be retained."));
      setConfigureClosePlanStatusTone("danger");
    } finally {
      setConfigureClosePlanBusy(false);
    }
  }, [closePlan, closeSetupDraft, closeSignOffDraft.taskId, services, workflow]);

  const updateCloseSetupDraft = useCallback((patch: Partial<AccountingCloseSetupDraftViewModel>) => {
    setCloseSetupDraft((current) => {
      const next = { ...current, ...patch };
      const updatesLegacySignOff =
        patch.taskRequiredApprovalRole !== undefined ||
        patch.taskRequiredApprovalCount !== undefined ||
        patch.taskRequiredEvidence !== undefined;
      if (updatesLegacySignOff && patch.taskSignOffRequirements === undefined) {
        return {
          ...next,
          taskSignOffRequirements: buildCloseSetupSingleSignOffRequirementRow(next)
        };
      }

      return next;
    });
  }, []);

  const selectCloseSetupTask = useCallback((taskId: string) => {
    setCloseSetupDraft((current) => {
      if (!closePlan) {
        return current;
      }

      return createAccountingCloseSetupDraft(closePlan, taskId);
    });
  }, [closePlan]);

  const toggleCloseSetupDependency = useCallback((taskId: string) => {
    setCloseSetupDraft((current) => {
      const dependencyIds = parseCloseSetupDependencyIds(current.taskDependsOnTaskIds);
      const normalizedTaskId = taskId.trim();
      if (!normalizedTaskId) {
        return current;
      }

      const nextDependencyIds = dependencyIds.some((item) => item.toLowerCase() === normalizedTaskId.toLowerCase())
        ? dependencyIds.filter((item) => item.toLowerCase() !== normalizedTaskId.toLowerCase())
        : [...dependencyIds, normalizedTaskId];

      return {
        ...current,
        taskDependsOnTaskIds: nextDependencyIds.join(", ")
      };
    });
  }, []);

  const selectCloseSetupSignOffRole = useCallback((role: string) => {
    setCloseSetupDraft((current) => {
      const normalizedRole = role.trim();
      if (!normalizedRole) {
        return current;
      }

      return {
        ...current,
        taskRequiredApprovalRole: normalizedRole
      };
    });
  }, []);

  const updateCloseSignOffDraft = useCallback((patch: Partial<AccountingCloseSignOffDraftViewModel>) => {
    setCloseSignOffDraft((current) => ({ ...current, ...patch }));
  }, []);

  const selectCloseSignOffTask = useCallback((taskId: string) => {
    setCloseSignOffDraft((current) => createAccountingCloseSignOffDraft(closePlan, taskId, current));
  }, [closePlan]);

  const selectCloseSignOffRole = useCallback((role: string) => {
    setCloseSignOffDraft((current) => {
      const normalizedRole = role.trim();
      return normalizedRole ? { ...current, role: normalizedRole } : current;
    });
  }, []);

  const selectCloseSignOffDecision = useCallback((decision: AccountingCloseSignOffDecision) => {
    setCloseSignOffDraft((current) => ({ ...current, decision }));
  }, []);

  const updateLateAdjustmentDraft = useCallback((patch: Partial<AccountingLateAdjustmentDraftViewModel>) => {
    setLateAdjustmentDraft((current) => ({ ...current, ...patch }));
  }, []);

  const createLateAdjustment = useCallback(async () => {
    if (!workflow || !closePlan) {
      setCreateLateAdjustmentStatusText("A close plan is required before requesting a late adjustment.");
      setCreateLateAdjustmentStatusTone("danger");
      return;
    }

    if (closePlan.isPeriodLocked) {
      setCreateLateAdjustmentStatusText("The period is locked; late adjustments must use governed reopen or remediation.");
      setCreateLateAdjustmentStatusTone("danger");
      return;
    }

    const journalEntryId = lateAdjustmentDraft.journalEntryId.trim();
    const reason = lateAdjustmentDraft.reason.trim();
    const amount = Number(lateAdjustmentDraft.amount);
    const currency = (lateAdjustmentDraft.currency.trim() || closePlan.materialityPolicy.currency || "USD").toUpperCase();
    if (!journalEntryId || !reason || !Number.isFinite(amount) || amount === 0) {
      setCreateLateAdjustmentStatusText("Journal entry, non-zero amount, currency, and reason are required before requesting a late adjustment.");
      setCreateLateAdjustmentStatusTone("danger");
      return;
    }

    setCreateLateAdjustmentBusy(true);
    setCreateLateAdjustmentStatusText(null);
    setCreateLateAdjustmentStatusTone("neutral");
    try {
      const nextPlan = await services.createLateAdjustment({
        workflowId: workflow.workflowId,
        journalEntryId,
        amount,
        currency,
        reason,
        requestedBy: "browser-accounting-controller",
        evidenceLinks: [
          `browser://accounting/close/late-adjustment/${workflow.workflowId}/${journalEntryId}`,
          `browser://accounting/close/materiality-review/${workflow.workflowId}`
        ],
        correlationId: `browser-late-adjustment-${workflow.workflowId}-${journalEntryId}`
      });
      setClosePlan(nextPlan);
      setLateAdjustmentDraft(createAccountingLateAdjustmentDraft(currency));
      setCreateLateAdjustmentStatusText(`Late adjustment requested for ${journalEntryId}.`);
      setCreateLateAdjustmentStatusTone("success");
    } catch (error) {
      setCreateLateAdjustmentStatusText(formatAccountingWorkflowError(error, "Late adjustment request could not be retained."));
      setCreateLateAdjustmentStatusTone("danger");
    } finally {
      setCreateLateAdjustmentBusy(false);
    }
  }, [closePlan, lateAdjustmentDraft, services, workflow]);

  const reviewLateAdjustment = useCallback(async (requestId: string, decision: "Approved" | "Rejected") => {
    if (!workflow || !closePlan) {
      setReviewLateAdjustmentStatusText("A close plan is required before reviewing a late adjustment.");
      setReviewLateAdjustmentStatusTone("danger");
      return;
    }

    const adjustment = closePlan.lateAdjustments.find((item) => item.requestId === requestId) ?? null;
    if (!adjustment) {
      setReviewLateAdjustmentStatusText(`Late adjustment ${requestId} is no longer loaded.`);
      setReviewLateAdjustmentStatusTone("danger");
      return;
    }

    if (adjustment.approvalState === "Approved" || adjustment.approvalState === "Rejected") {
      setReviewLateAdjustmentStatusText(`Late adjustment ${requestId} is already ${adjustment.approvalState.toLowerCase()}.`);
      setReviewLateAdjustmentStatusTone("danger");
      return;
    }

    setReviewLateAdjustmentBusy(true);
    setReviewLateAdjustmentStatusText(null);
    setReviewLateAdjustmentStatusTone("neutral");
    try {
      const action = decision === "Approved" ? "approved" : "rejected";
      const nextPlan = await services.reviewLateAdjustment({
        workflowId: workflow.workflowId,
        requestId,
        decision,
        actor: "browser-accounting-controller",
        notes: `${decision} late adjustment ${requestId} from the Accounting close cockpit.`,
        evidenceLinks: [
          `browser://accounting/close/late-adjustments/review/${workflow.workflowId}/${requestId}/${decision.toLowerCase()}`,
          ...adjustment.evidenceLinks
        ],
        correlationId: `browser-late-adjustment-review-${workflow.workflowId}-${requestId}-${decision.toLowerCase()}`
      });
      setClosePlan(nextPlan);
      setReviewLateAdjustmentStatusText(`Late adjustment ${requestId} ${action}.`);
      setReviewLateAdjustmentStatusTone("success");
    } catch (error) {
      setReviewLateAdjustmentStatusText(formatAccountingWorkflowError(error, "Late adjustment review could not be retained."));
      setReviewLateAdjustmentStatusTone("danger");
    } finally {
      setReviewLateAdjustmentBusy(false);
    }
  }, [closePlan, services, workflow]);

  const reviewCloseEvidence = useCallback(async (rowId: string) => {
    if (!workflow || !closePlan) {
      setReviewCloseEvidenceStatusText("A close plan is required before retaining evidence review.");
      setReviewCloseEvidenceStatusTone("danger");
      return;
    }

    if (closePlan.isPeriodLocked) {
      setReviewCloseEvidenceStatusText("The period is locked; evidence review changes require a governed reopen workflow.");
      setReviewCloseEvidenceStatusTone("danger");
      return;
    }

    const activeIssue = closePlan.validationIssues.find((issue, index) =>
      `validation-evidence-${issue.code}-${issue.targetId ?? index}` === rowId) ?? null;
    if (!activeIssue) {
      setReviewCloseEvidenceStatusText("Select an active close blocker or evidence issue before retaining review.");
      setReviewCloseEvidenceStatusTone("danger");
      return;
    }

    const targetId = activeIssue.targetId?.trim() || closePlan.closePlanId;
    const ledgerBookId = closePlan.ledgerBookId ?? "primary";
    setReviewCloseEvidenceBusy(true);
    setReviewCloseEvidenceStatusText(null);
    setReviewCloseEvidenceStatusTone("neutral");
    try {
      const nextPlan = await services.reviewCloseEvidence({
        workflowId: workflow.workflowId,
        issueCode: activeIssue.code,
        targetId: activeIssue.targetId ?? null,
        actor: "browser-accounting-controller",
        notes: `Reviewed close blocker ${activeIssue.code} for ${targetId} from the Accounting close cockpit. ${activeIssue.message}`,
        evidenceLinks: [
          `browser://accounting/close/evidence-review/${workflow.workflowId}/${activeIssue.code}/${targetId}/book/${ledgerBookId}`,
          `evidence://close-review/workflow/${workflow.workflowId}/period/${closePlan.periodId}/book/${ledgerBookId}/issue/${activeIssue.code}/target/${targetId}`
        ],
        correlationId: `browser-close-evidence-review-${workflow.workflowId}-${activeIssue.code}-${targetId}`,
        actionOrigin: "HumanOperator"
      });
      setClosePlan(nextPlan);
      setReviewCloseEvidenceStatusText(`Retained close evidence review for ${activeIssue.code}.`);
      setReviewCloseEvidenceStatusTone("success");
    } catch (error) {
      setReviewCloseEvidenceStatusText(formatAccountingWorkflowError(error, "Close evidence review could not be retained."));
      setReviewCloseEvidenceStatusTone("danger");
    } finally {
      setReviewCloseEvidenceBusy(false);
    }
  }, [closePlan, services, workflow]);

  const inspectSelectedPackageExport = useCallback(async () => {
    const selectedBundle = packages.find((bundle) => bundle.financialStatements.packageId === selectedPackageId) ?? packages[0] ?? null;
    const artifact = selectedBundle?.exportArtifacts?.[0] ?? null;
    if (!selectedBundle || !artifact) {
      setExportManifestStatusText("A report package with retained export artifacts is required before manifest inspection.");
      setExportManifestStatusTone("danger");
      return;
    }

    setExportManifestBusy(true);
    setExportManifestStatusText(null);
    setExportManifestStatusTone("neutral");
    try {
      const manifest = await services.getExportManifest(selectedBundle.financialStatements.packageId, artifact.artifactId);
      setExportManifest(manifest);
      setExportManifestStatusText(`Loaded export manifest ${manifest.artifactId}.`);
      setExportManifestStatusTone(manifest.externalPostingAllowed ? "danger" : "success");
    } catch (error) {
      setExportManifestStatusText(formatAccountingWorkflowError(error, "Report export manifest could not be loaded."));
      setExportManifestStatusTone("danger");
    } finally {
      setExportManifestBusy(false);
    }
  }, [packages, selectedPackageId, services]);

  return useMemo(
    () => buildAccountingCloseReportPackageViewState({
      workflow,
      closePlan,
      packages,
      selectedPackageId,
      loading,
      errorText,
      buildBusy,
      buildStatusText,
      buildStatusTone,
      certifyBusy,
      certifyStatusText,
      certifyStatusTone,
      signOffBusy,
      signOffStatusText,
      signOffStatusTone,
      lockClosePeriodBusy,
      lockClosePeriodStatusText,
      lockClosePeriodStatusTone,
      configureClosePlanBusy,
      configureClosePlanStatusText,
      configureClosePlanStatusTone,
      closeSetupDraft,
      closeSignOffDraft,
      createLateAdjustmentBusy,
      createLateAdjustmentStatusText,
      createLateAdjustmentStatusTone,
      lateAdjustmentDraft,
      reviewLateAdjustmentBusy,
      reviewLateAdjustmentStatusText,
      reviewLateAdjustmentStatusTone,
      reviewCloseEvidenceBusy,
      reviewCloseEvidenceStatusText,
      reviewCloseEvidenceStatusTone,
      exportManifestBusy,
      exportManifestStatusText,
      exportManifestStatusTone,
      exportManifest,
      refresh,
      buildReportPackage,
      certifyPackage,
      lockClosePeriod,
      configureClosePlan,
      signOffNextTask,
      updateCloseSetupDraft,
      selectCloseSetupTask,
      toggleCloseSetupDependency,
      selectCloseSetupSignOffRole,
      updateCloseSignOffDraft,
      selectCloseSignOffTask,
      selectCloseSignOffRole,
      selectCloseSignOffDecision,
      updateLateAdjustmentDraft,
      createLateAdjustment,
      reviewLateAdjustment,
      reviewCloseEvidence,
      inspectSelectedPackageExport,
      selectPackage: setSelectedPackageId
    }),
    [
      buildBusy,
      buildReportPackage,
      buildStatusText,
      buildStatusTone,
      certifyBusy,
      certifyPackage,
      certifyStatusText,
      certifyStatusTone,
      closePlan,
      lockClosePeriod,
      lockClosePeriodBusy,
      lockClosePeriodStatusText,
      lockClosePeriodStatusTone,
      configureClosePlan,
      configureClosePlanBusy,
      configureClosePlanStatusText,
      configureClosePlanStatusTone,
      closeSetupDraft,
      closeSignOffDraft,
      createLateAdjustment,
      createLateAdjustmentBusy,
      createLateAdjustmentStatusText,
      createLateAdjustmentStatusTone,
      errorText,
      exportManifest,
      exportManifestBusy,
      exportManifestStatusText,
      exportManifestStatusTone,
      inspectSelectedPackageExport,
      lateAdjustmentDraft,
      loading,
      packages,
      refresh,
      reviewLateAdjustment,
      reviewLateAdjustmentBusy,
      reviewLateAdjustmentStatusText,
      reviewLateAdjustmentStatusTone,
      reviewCloseEvidence,
      reviewCloseEvidenceBusy,
      reviewCloseEvidenceStatusText,
      reviewCloseEvidenceStatusTone,
      selectCloseSetupTask,
      selectCloseSetupSignOffRole,
      selectCloseSignOffDecision,
      selectCloseSignOffRole,
      selectCloseSignOffTask,
      selectedPackageId,
      signOffBusy,
      signOffNextTask,
      signOffStatusText,
      signOffStatusTone,
      toggleCloseSetupDependency,
      updateCloseSetupDraft,
      updateCloseSignOffDraft,
      updateLateAdjustmentDraft,
      workflow
    ]
  );
}

export function buildAccountingWorkflowLaunchViewState({
  data,
  workstream,
  closeCommandCenter,
  taskMode
}: {
  data: AccountingWorkspaceResponse;
  workstream: AccountingWorkstream;
  closeCommandCenter: CloseCommandCenterViewState | null | undefined;
  taskMode?: AccountingTaskModeViewModel;
}): AccountingWorkflowLaunchViewState {
  const openBreakCount = data.breakQueue.filter((item) => isOpenAccountingBreakStatus(item.status)).length;
  const totalBreakCount = data.breakQueue.length;
  const metricRows = closeCommandCenter?.metricRows ?? [];
  const pendingAdjustmentMetric = metricRows.find((item) => item.id === "adjustments") ?? null;
  const providerMetric = metricRows.find((item) => item.id === "providers") ?? null;
  const sourceMetric = metricRows.find((item) => item.id === "source-files") ?? null;
  const reportPackMetric = metricRows.find((item) => item.id === "report-pack") ?? null;
  const pendingAdjustmentCount = parseAccountingWorkflowMetricCount(pendingAdjustmentMetric?.value);
  const sourceGapCount = parseAccountingWorkflowMetricCount(sourceMetric?.value);
  const securityGapCount = parseAccountingWorkflowMetricCount(
    data.metrics.find((item) => item.label.toLowerCase().includes("security"))?.value
  );
  const activeTaskMode = taskMode ?? buildAccountingTaskMode(accountingWorkstreamHref(workstream));
  const activeStepLabel = activeTaskMode.label;
  const workflowStatusTone = closeCommandCenter?.statusTone ?? (openBreakCount > 0 ? "warning" : "default");
  const workflowStatusLabel = closeCommandCenter?.statusLabel ?? (openBreakCount > 0 ? "Review" : "Ready");
  const steps: AccountingWorkflowStepViewModel[] = [
    buildAccountingWorkflowStep({
      id: "configure",
      label: "Governance",
      caption: "Books, chart, templates, and posting controls.",
      href: WORKSTATION_ROUTE_CATALOG.accountingConfigure,
      metricLabel: "Setup",
      metricValue: "Shared",
      statusLabel: "Connected",
      tone: "default",
      workstream
    }),
    buildAccountingWorkflowStep({
      id: "journal-entries",
      label: "Journal Entry",
      caption: "Manual JEs, evidence, Security Master, and approval submit.",
      href: WORKSTATION_ROUTE_CATALOG.accountingJournalEntries,
      metricLabel: "Pending",
      metricValue: pendingAdjustmentMetric?.value ?? "0",
      statusLabel: pendingAdjustmentCount > 0 ? "Approval review" : "Ready",
      tone: pendingAdjustmentCount > 0 ? "warning" : "success",
      workstream
    }),
    buildAccountingWorkflowStep({
      id: "capital-accounts",
      label: "Capital accounts",
      caption: "Investor evidence, allocation rules, statements, and audit support.",
      href: WORKSTATION_ROUTE_CATALOG.accountingCapitalAccounts,
      metricLabel: "Investor rows",
      metricValue: data.manualJournalWorkbench?.privateCapitalActivity?.capitalAccountCount.toLocaleString() ?? "0",
      statusLabel: (data.manualJournalWorkbench?.privateCapitalActivity?.capitalAccountCount ?? 0) > 0 ? "Evidence ready" : "No activity",
      tone: (data.manualJournalWorkbench?.privateCapitalActivity?.capitalAccountCount ?? 0) > 0 ? "success" : "warning",
      workstream
    }),
    buildAccountingWorkflowStep({
      id: "ledger",
      label: "Ledger Explorer",
      caption: "Meridian-owned trial balance and journal evidence.",
      href: WORKSTATION_ROUTE_CATALOG.accountingLedger,
      metricLabel: providerMetric?.label ?? "Truth",
      metricValue: providerMetric?.value ?? "Meridian",
      statusLabel: providerMetric?.tone === "warning" || providerMetric?.tone === "danger"
        ? "Evidence review"
        : "Ledger authority",
      tone: providerMetric?.tone ?? "default",
      workstream
    }),
    buildAccountingWorkflowStep({
      id: "reconciliation",
      label: "Reconciliation Casework",
      caption: "Statement runs, trial balance, and open break review.",
      href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
      metricLabel: "Open breaks",
      metricValue: String(openBreakCount),
      statusLabel: openBreakCount > 0 ? "Review breaks" : "Balanced queue",
      tone: openBreakCount > 0 ? "warning" : "success",
      workstream
    }),
    buildAccountingWorkflowStep({
      id: "exceptions",
      label: "Exception Casework",
      caption: "Casework, comments, evidence, and sign-off handoffs.",
      href: WORKSTATION_ROUTE_CATALOG.accountingExceptions,
      metricLabel: "Cases",
      metricValue: String(totalBreakCount),
      statusLabel: totalBreakCount > 0 ? "Casework open" : "No active cases",
      tone: totalBreakCount > 0 ? "warning" : "success",
      workstream
    }),
    buildAccountingWorkflowStep({
      id: "security-master",
      label: "Security Governance",
      caption: "Identifiers, schedules, lots, and posting readiness.",
      href: WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster,
      metricLabel: "Gaps",
      metricValue: String(securityGapCount),
      statusLabel: securityGapCount > 0 ? "Coverage gaps" : "Coverage clean",
      tone: securityGapCount > 0 ? "warning" : "success",
      workstream
    }),
    buildAccountingWorkflowStep({
      id: "approvals",
      label: "Close Cockpit",
      caption: "Signer decisions, blockers, and audit trail.",
      href: WORKSTATION_ROUTE_CATALOG.accountingApprovals,
      metricLabel: "Approvals",
      metricValue: pendingAdjustmentMetric?.value ?? "0",
      statusLabel: pendingAdjustmentCount > 0 ? "Signer review" : "No backlog",
      tone: pendingAdjustmentCount > 0 ? "warning" : "success",
      workstream
    }),
    buildAccountingWorkflowStep({
      id: "reporting",
      label: "Delivery Evidence",
      caption: "Report packs, retained manifests, exports, and audit output.",
      href: WORKSTATION_ROUTE_CATALOG.reportingEvidence,
      metricLabel: reportPackMetric?.label ?? "Profiles",
      metricValue: reportPackMetric?.value ?? String(data.reporting.profileCount),
      statusLabel: reportPackMetric?.tone === "success" ? "Ready" : "Evidence review",
      tone: reportPackMetric?.tone ?? (data.reporting.profileCount > 0 ? "default" : "warning"),
      workstream
    })
  ];
  const actionRows: AccountingWorkflowActionViewModel[] = [
    {
      id: "reconcile",
      label: openBreakCount > 0 ? "Reconcile breaks" : "Review reconciliation",
      detail: openBreakCount > 0 ? `${formatCount(openBreakCount, "open break")} require operator review.` : "Statement and run reconciliation are ready for inspection.",
      href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
      ariaLabel: "Open Accounting reconciliation workflow",
      tone: openBreakCount > 0 ? "warning" : "success"
    },
    {
      id: "journal-entry",
      label: "Enter journal entry",
      detail: pendingAdjustmentCount > 0 ? `${formatCount(pendingAdjustmentCount, "adjustment")} remain in approval scope.` : "Create or validate a controller-owned accounting adjustment.",
      href: WORKSTATION_ROUTE_CATALOG.accountingJournalEntries,
      ariaLabel: "Open Accounting journal entry workbench",
      tone: pendingAdjustmentCount > 0 ? "warning" : "default"
    },
    {
      id: "approvals",
      label: "Review approvals",
      detail: pendingAdjustmentCount > 0 ? "Signer action is needed before close release." : "Open the close approval gate and audit trail.",
      href: WORKSTATION_ROUTE_CATALOG.accountingApprovals,
      ariaLabel: "Open Accounting approvals workflow",
      tone: pendingAdjustmentCount > 0 ? "warning" : "success"
    },
    {
      id: "evidence",
      label: sourceGapCount > 0 ? "Attach evidence" : "Open evidence",
      detail: sourceGapCount > 0 ? `${formatCount(sourceGapCount, "source gap")} are visible in close readiness.` : "Inspect retained accounting-record evidence and report manifests.",
      href: WORKSTATION_ROUTE_CATALOG.reportingEvidence,
      ariaLabel: "Open retained accounting record evidence",
      tone: sourceGapCount > 0 ? "warning" : reportPackMetric?.tone ?? "default"
    }
  ];

  return {
    title: "Accounting workflow",
    description: "Books-before-broker accounting, reconciliation, approvals, and retained report evidence are grouped in one operator lane.",
    ariaLabel: "Accounting workflow launch paths",
    taskMode: activeTaskMode,
    activeLabel: `${activeStepLabel} active`,
    statusLabel: workflowStatusLabel,
    statusTone: workflowStatusTone,
    steps,
    actionRows,
    liveRegionText: `Accounting workflow ${workflowStatusLabel}. ${activeStepLabel} active. ${formatCount(openBreakCount, "open break")}.`
  };
}

function buildAccountingWorkflowStep({
  id,
  label,
  caption,
  href,
  metricLabel,
  metricValue,
  statusLabel,
  tone,
  workstream
}: {
  id: AccountingWorkstream;
  label: string;
  caption: string;
  href: string;
  metricLabel: string;
  metricValue: string;
  statusLabel: string;
  tone: AccountingToolingTone;
  workstream: AccountingWorkstream;
}): AccountingWorkflowStepViewModel {
  const isActive = id === workstream;

  return {
    id,
    label,
    caption,
    href,
    metricLabel,
    metricValue,
    statusLabel,
    tone,
    isActive,
    ariaLabel: `${label}: ${statusLabel}${isActive ? ", current Accounting workstream" : ""}`
  };
}

function parseAccountingWorkflowMetricCount(value: string | null | undefined): number {
  const normalized = value?.replace(/,/g, "").match(/-?\d+/)?.[0];
  return normalized ? Number.parseInt(normalized, 10) : 0;
}

function formatAccountingWorkflowError(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message || fallback : fallback;
}

function buildAccountingCloseReportPackageViewState({
  workflow,
  closePlan,
  packages,
  selectedPackageId,
  loading,
  errorText,
  buildBusy,
  buildStatusText,
  buildStatusTone,
  certifyBusy,
  certifyStatusText,
  certifyStatusTone,
  signOffBusy,
  signOffStatusText,
  signOffStatusTone,
  lockClosePeriodBusy,
  lockClosePeriodStatusText,
  lockClosePeriodStatusTone,
  createLateAdjustmentBusy,
  createLateAdjustmentStatusText,
  createLateAdjustmentStatusTone,
  configureClosePlanBusy,
  configureClosePlanStatusText,
  configureClosePlanStatusTone,
  closeSetupDraft,
  closeSignOffDraft,
  lateAdjustmentDraft,
  reviewLateAdjustmentBusy,
  reviewLateAdjustmentStatusText,
  reviewLateAdjustmentStatusTone,
  reviewCloseEvidenceBusy,
  reviewCloseEvidenceStatusText,
  reviewCloseEvidenceStatusTone,
  exportManifestBusy,
  exportManifestStatusText,
  exportManifestStatusTone,
  exportManifest,
  refresh,
  buildReportPackage,
  certifyPackage,
  lockClosePeriod,
      configureClosePlan,
      signOffNextTask,
      updateCloseSetupDraft,
      selectCloseSetupTask,
      toggleCloseSetupDependency,
      selectCloseSetupSignOffRole,
      updateCloseSignOffDraft,
      selectCloseSignOffTask,
      selectCloseSignOffRole,
      selectCloseSignOffDecision,
      updateLateAdjustmentDraft,
  createLateAdjustment,
  reviewLateAdjustment,
  reviewCloseEvidence,
  inspectSelectedPackageExport,
  selectPackage
}: {
  workflow: OperationsContinuityWorkflow | null;
  closePlan: ClosePeriodPlan | null;
  packages: AccountingReportPackageBundle[];
  selectedPackageId: string | null;
  loading: boolean;
  errorText: string | null;
  buildBusy: boolean;
  buildStatusText: string | null;
  buildStatusTone: "neutral" | "success" | "danger";
  certifyBusy: boolean;
  certifyStatusText: string | null;
  certifyStatusTone: "neutral" | "success" | "danger";
  signOffBusy: boolean;
  signOffStatusText: string | null;
  signOffStatusTone: "neutral" | "success" | "danger";
  lockClosePeriodBusy: boolean;
  lockClosePeriodStatusText: string | null;
  lockClosePeriodStatusTone: "neutral" | "success" | "danger";
  createLateAdjustmentBusy: boolean;
  createLateAdjustmentStatusText: string | null;
  createLateAdjustmentStatusTone: "neutral" | "success" | "danger";
  configureClosePlanBusy: boolean;
  configureClosePlanStatusText: string | null;
  configureClosePlanStatusTone: "neutral" | "success" | "danger";
  closeSetupDraft: AccountingCloseSetupDraftViewModel;
  closeSignOffDraft: AccountingCloseSignOffDraftViewModel;
  lateAdjustmentDraft: AccountingLateAdjustmentDraftViewModel;
  reviewLateAdjustmentBusy: boolean;
  reviewLateAdjustmentStatusText: string | null;
  reviewLateAdjustmentStatusTone: "neutral" | "success" | "danger";
  reviewCloseEvidenceBusy: boolean;
  reviewCloseEvidenceStatusText: string | null;
  reviewCloseEvidenceStatusTone: "neutral" | "success" | "danger";
  exportManifestBusy: boolean;
  exportManifestStatusText: string | null;
  exportManifestStatusTone: "neutral" | "success" | "danger";
  exportManifest: ReportExportArtifactManifest | null;
  refresh: () => Promise<void>;
  buildReportPackage: () => Promise<void>;
  certifyPackage: () => Promise<void>;
  lockClosePeriod: () => Promise<void>;
  configureClosePlan: () => Promise<void>;
  signOffNextTask: () => Promise<void>;
  updateCloseSetupDraft: (patch: Partial<AccountingCloseSetupDraftViewModel>) => void;
  selectCloseSetupTask: (taskId: string) => void;
  toggleCloseSetupDependency: (taskId: string) => void;
  selectCloseSetupSignOffRole: (role: string) => void;
  updateCloseSignOffDraft: (patch: Partial<AccountingCloseSignOffDraftViewModel>) => void;
  selectCloseSignOffTask: (taskId: string) => void;
  selectCloseSignOffRole: (role: string) => void;
  selectCloseSignOffDecision: (decision: AccountingCloseSignOffDecision) => void;
  updateLateAdjustmentDraft: (patch: Partial<AccountingLateAdjustmentDraftViewModel>) => void;
  createLateAdjustment: () => Promise<void>;
  reviewLateAdjustment: (requestId: string, decision: "Approved" | "Rejected") => Promise<void>;
  reviewCloseEvidence: (rowId: string) => Promise<void>;
  inspectSelectedPackageExport: () => Promise<void>;
  selectPackage: (packageId: string) => void;
}): AccountingCloseReportPackageViewModel {
  const selectedBundle = packages.find((bundle) => bundle.financialStatements.packageId === selectedPackageId) ?? packages[0] ?? null;
  const selectedPackage = selectedBundle ? buildAccountingReportPackageRow(selectedBundle, selectedBundle.financialStatements.packageId) : null;
  const packageRows = packages.map((bundle) => buildAccountingReportPackageRow(
    bundle,
    selectedBundle?.financialStatements.packageId ?? selectedPackageId
  ));
  const tasks = (closePlan?.tasks ?? []).map(buildClosePlanTaskRow);
  const closeCalendar = (closePlan?.closeCalendar ?? []).map(buildCloseCalendarMilestoneRow);
  const locked = closePlan?.isPeriodLocked === true;
  const lateAdjustments = (closePlan?.lateAdjustments ?? []).map((adjustment) => buildLateAdjustmentRow(adjustment, locked));
  const dependencyGraphRows = closePlan ? buildCloseDependencyGraphRows(closePlan) : [];
  const signOffMatrixRows = closePlan ? buildCloseSignOffMatrixRows(closePlan) : [];
  const operatingCoverageRows = closePlan ? buildCloseOperatingCoverageRows(closePlan) : [];
  const closeSetupTaskOptions = closePlan ? buildCloseSetupTaskOptions(closePlan, closeSetupDraft.taskId) : [];
  const closeSetupDependencyOptions = closePlan ? buildCloseSetupDependencyOptions(closePlan, closeSetupDraft) : [];
  const closeSetupSignOffRoleOptions = closePlan ? buildCloseSetupSignOffRoleOptions(closePlan, closeSetupDraft) : [];
  const closeSignOffTaskOptions = closePlan ? buildCloseSignOffTaskOptions(closePlan, closeSignOffDraft) : [];
  const closeSignOffRoleOptions = closePlan ? buildCloseSignOffRoleOptions(closePlan, closeSignOffDraft) : [];
  const closeSignOffDecisionOptions = buildCloseSignOffDecisionOptions(closeSignOffDraft);
  const openTaskCount = closePlan?.tasks.filter((task) => task.status !== "SignedOff").length ?? 0;
  const blockedCalendarCount = closeCalendar.filter((item) => item.statusTone === "danger").length;
  const closeValidationIssues = closePlan?.validationIssues ?? [];
  const packageValidationIssues = selectedBundle?.validationIssues ?? [];
  const validationIssues = [
    ...closeValidationIssues,
    ...packageValidationIssues
  ].map<AccountingConfigurationIssueViewModel>((issue, index) => ({
    id: `${issue.code}-${issue.targetId ?? index}`,
    label: `${issue.severity} | ${issue.code}`,
    message: issue.message,
    detail: issue.targetId ?? "No target",
    tone: issue.severity === "Critical" ? "danger" : issue.severity === "Warning" ? "warning" : "default"
  }));
  const evidenceReviewRows = buildCloseEvidenceReviewRows(closePlan, selectedBundle, validationIssues, locked);
  const packageCertification = selectedBundle?.certification.state ?? selectedBundle?.financialStatements.certificationState ?? "Draft";
  const certificationTone = accountingCertificationTone(packageCertification);
  const statusTone: AccountingToolingTone = errorText
    ? "danger"
    : !workflow
      ? "default"
      : validationIssues.length > 0 || openTaskCount > 0
        ? "warning"
        : packages.length > 0 && certificationTone === "success"
          ? "success"
          : "default";
  const statusLabel = errorText
    ? "Needs attention"
    : !workflow
      ? "Close workflow pending"
      : packages.length > 0
        ? `${formatAccountingCertificationState(packageCertification)} package`
        : "Package not built";
  const materiality = closePlan?.materialityPolicy;
  const materialityLabel = materiality
    ? `${formatCurrencyWithCode(materiality.amountThreshold, materiality.currency)} or ${materiality.percentThreshold}% review by ${materiality.reviewRole}`
    : "Materiality policy pending";
  const buildDisabledReason = !workflow
    ? "A close workflow must be loaded before building a package."
    : loading || buildBusy || certifyBusy || signOffBusy || lockClosePeriodBusy
      ? "Close/report package refresh is running."
      : null;
  const criticalIssueCount = [...closeValidationIssues, ...packageValidationIssues].filter((issue) => issue.severity === "Critical").length;
  const certifyDisabledReason = !selectedBundle
    ? "A report package must be built before certification."
    : selectedBundle.certification.state === "Certified"
      ? "The selected report package is already certified."
      : selectedBundle.certification.state !== "ReadyForReview"
        ? `Certification requires Ready for review state; current state is ${formatAccountingCertificationState(selectedBundle.certification.state)}.`
        : criticalIssueCount > 0
          ? "Critical validation issues must be cleared before certification."
          : loading || buildBusy || certifyBusy || signOffBusy || lockClosePeriodBusy
            ? "Close/report package certification is running."
            : null;
  const signOffTarget = closePlan ? resolveCloseTaskSignOffDraftTarget(closePlan, closeSignOffDraft) : null;
  const signOffDraftValidation = closePlan ? validateCloseSignOffDraft(closePlan, closeSignOffDraft) : null;
  const signOffDisabledReason = !workflow
    ? "A close workflow must be loaded before signing off a task."
    : !closePlan
      ? "A close plan must be loaded before signing off a task."
      : locked
        ? "The period is locked; close task sign-off is disabled."
        : signOffDraftValidation
          ? signOffDraftValidation
          : loading || buildBusy || certifyBusy || signOffBusy || lockClosePeriodBusy
            ? "Close/report package action is running."
            : null;
  const lockClosePeriodDisabledReason = !workflow
    ? "A close workflow must be loaded before locking the period."
    : !closePlan
      ? "A close plan must be loaded before locking the period."
      : locked
        ? "The close period is already locked."
        : !selectedBundle
          ? "A report package must be built before locking the period."
          : loading || buildBusy || certifyBusy || signOffBusy || lockClosePeriodBusy
            ? "Close/report package action is running."
            : null;
  const configureClosePlanDisabledReason = !workflow
    ? "A close workflow must be loaded before configuring close setup."
    : !closePlan
      ? "A close plan must be loaded before configuring close setup."
      : locked
        ? "The period is locked; close setup changes require a governed reopen workflow."
        : validateCloseSetupMaterialityDraft(closeSetupDraft)
          ?? validateCloseSetupTaskSelection(closePlan, closeSetupDraft)
          ?? validateCloseSetupSignOffDraft(closeSetupDraft)
          ?? (loading || buildBusy || certifyBusy || signOffBusy || lockClosePeriodBusy || configureClosePlanBusy
            ? "Close/report package action is running."
            : null);
  const createLateAdjustmentAmount = Number(lateAdjustmentDraft.amount);
  const createLateAdjustmentDisabledReason = !workflow
    ? "A close workflow must be loaded before requesting a late adjustment."
    : !closePlan
      ? "A close plan must be loaded before requesting a late adjustment."
      : locked
        ? "The period is locked; late adjustment requests are disabled."
        : !lateAdjustmentDraft.journalEntryId.trim()
          ? "Enter the journal entry id for the late adjustment."
          : !Number.isFinite(createLateAdjustmentAmount) || createLateAdjustmentAmount === 0
            ? "Enter a non-zero late adjustment amount."
            : !lateAdjustmentDraft.reason.trim()
              ? "Enter the late adjustment reason."
              : loading || buildBusy || certifyBusy || signOffBusy || lockClosePeriodBusy || createLateAdjustmentBusy || reviewLateAdjustmentBusy
                ? "Close/report package action is running."
                : null;
  const reviewLateAdjustmentDisabledReason = !workflow
    ? "A close workflow must be loaded before reviewing late adjustments."
    : !closePlan
      ? "A close plan must be loaded before reviewing late adjustments."
      : locked
        ? "The period is locked; late-adjustment review is disabled."
        : !lateAdjustments.some((adjustment) => adjustment.reviewDisabledReason === null)
          ? "No submitted late adjustment is ready for review."
          : loading || buildBusy || certifyBusy || signOffBusy || lockClosePeriodBusy || createLateAdjustmentBusy || reviewLateAdjustmentBusy
            ? "Close/report package action is running."
            : null;
  const reviewCloseEvidenceDisabledReason = !workflow
    ? "A close workflow must be loaded before retaining blocker review."
    : !closePlan
      ? "A close plan must be loaded before retaining blocker review."
      : locked
        ? "The period is locked; evidence review changes require a governed reopen workflow."
        : !evidenceReviewRows.some((row) => row.issueCode && !row.reviewDisabledReason)
          ? "No active close blocker is ready for evidence review."
          : loading || buildBusy || certifyBusy || signOffBusy || lockClosePeriodBusy || reviewCloseEvidenceBusy
            ? "Close/report package action is running."
            : null;
  const selectedExportArtifact = selectedBundle?.exportArtifacts?.[0] ?? null;
  const exportManifestDisabledReason = !selectedBundle
    ? "A report package must be selected before export manifest inspection."
    : !selectedExportArtifact
      ? "The selected report package has no retained export artifacts."
      : loading || buildBusy || certifyBusy || signOffBusy || lockClosePeriodBusy || reviewLateAdjustmentBusy || exportManifestBusy
        ? "Close/report package action is running."
        : null;
  const closeWorkflowSteps = buildAccountingCloseWorkflowSteps({
    closePlan,
    selectedBundle,
    selectedExportArtifact,
    tasks,
    lateAdjustments,
    evidenceReviewRows,
    validationIssues,
    packageRows,
    configureClosePlanDisabledReason,
    signOffDisabledReason,
    createLateAdjustmentDisabledReason,
    reviewLateAdjustmentDisabledReason,
    reviewCloseEvidenceDisabledReason,
    buildDisabledReason,
    certifyDisabledReason,
    exportManifestDisabledReason,
    lockClosePeriodDisabledReason
  });

  return {
    title: "Close and report package certification",
    description: "Close plan, period lock, materiality, late-adjustment, financial statement, investor statement, realized gain/loss, NAV, and restatement status from Meridian ledger services.",
    ariaLabel: "Accounting close and report package certification cockpit",
    statusLabel,
    statusTone,
    periodLabel: closePlan
      ? `${closePlan.periodId} (${formatDateOnly(closePlan.periodStart)} to ${formatDateOnly(closePlan.periodEnd)})`
      : workflow?.periodId ?? "Period pending",
    fundLabel: closePlan?.fundProfileId ?? workflow?.fundAccountId ?? "Fund pending",
    lockLabel: locked ? "Period locked" : closePlan ? "Period open" : "Lock state pending",
    materialityLabel,
    loading,
    loadingText: loading ? "Refreshing close plan and certified package history." : null,
    errorText,
    buildBusy,
    buildStatusText,
    buildStatusTone,
    certifyBusy,
    certifyStatusText,
    certifyStatusTone,
    signOffBusy,
    signOffStatusText,
    signOffStatusTone,
    lockClosePeriodBusy,
    lockClosePeriodStatusText,
    lockClosePeriodStatusTone,
    configureClosePlanBusy,
    configureClosePlanStatusText,
    configureClosePlanStatusTone,
    createLateAdjustmentBusy,
    createLateAdjustmentStatusText,
    createLateAdjustmentStatusTone,
    reviewLateAdjustmentBusy,
    reviewLateAdjustmentStatusText,
    reviewLateAdjustmentStatusTone,
    reviewCloseEvidenceBusy,
    reviewCloseEvidenceStatusText,
    reviewCloseEvidenceStatusTone,
    exportManifestBusy,
    exportManifestStatusText,
    exportManifestStatusTone,
    exportManifest: exportManifest ? buildAccountingReportExportManifestViewModel(exportManifest) : null,
    buildButtonLabel: packages.length > 0 ? "Rebuild package" : "Build package",
    buildDisabledReason,
    certifyButtonLabel: selectedBundle?.certification.state === "Certified" ? "Certified" : "Certify package",
    certifyDisabledReason,
    signOffButtonLabel: signOffTarget ? `${closeSignOffDraft.decision} ${signOffTarget.task.displayName}` : "Sign off selected task",
    signOffDisabledReason,
    lockClosePeriodButtonLabel: locked ? "Period locked" : "Lock period",
    lockClosePeriodDisabledReason,
    configureClosePlanButtonLabel: closePlan ? "Retain close setup" : "Configure close setup",
    configureClosePlanDisabledReason,
    createLateAdjustmentDisabledReason,
    closeSetupDraft,
    closeSetupTaskOptions,
    closeSetupDependencyOptions,
    closeSetupSignOffRoleOptions,
    closeSignOffDraft,
    closeSignOffTaskOptions,
    closeSignOffRoleOptions,
    closeSignOffDecisionOptions,
    lateAdjustmentDraft,
    exportManifestButtonLabel: selectedExportArtifact ? `Inspect ${selectedExportArtifact.displayName}` : "Inspect export manifest",
    exportManifestDisabledReason,
    metrics: [
      {
        id: "checklist",
        label: "Checklist",
        value: `${tasks.filter((task) => task.statusTone === "success").length}/${tasks.length}`,
        detail: tasks.length > 0 ? `${formatCount(openTaskCount, "task")} remain before sign-off.` : "Close checklist has not been loaded.",
        tone: openTaskCount > 0 ? "warning" : tasks.length > 0 ? "success" : "default"
      },
      {
        id: "late-adjustments",
        label: "Late adjustments",
        value: String(lateAdjustments.length),
        detail: lateAdjustments.length > 0 ? "Late adjustments require approval evidence." : "No late adjustments are surfaced for this close plan.",
        tone: lateAdjustments.length > 0 ? "warning" : "success"
      },
      {
        id: "calendar",
        label: "Calendar",
        value: String(closeCalendar.length),
        detail: closeCalendar.length > 0
          ? blockedCalendarCount > 0
            ? `${formatCount(blockedCalendarCount, "calendar milestone")} blocked.`
            : `${formatCount(closeCalendar.length, "calendar milestone")} sequenced.`
          : "Close calendar milestones have not been loaded.",
        tone: blockedCalendarCount > 0 ? "danger" : closeCalendar.length > 0 ? "success" : "default"
      },
      {
        id: "packages",
        label: "Packages",
        value: String(packageRows.length),
        detail: selectedPackage ? `${selectedPackage.packageId} is selected.` : "No certified package history is available.",
        tone: packageRows.length > 0 ? certificationTone : "default"
      },
      {
        id: "issues",
        label: "Validation",
        value: String(validationIssues.length),
        detail: validationIssues.length > 0 ? "Validation issues remain attached to the close/report package." : "No close/report validation issues are surfaced.",
        tone: validationIssues.length > 0 ? "warning" : "success"
      }
    ],
    closeCalendar,
    tasks,
    dependencyGraphRows,
    signOffMatrixRows,
    evidenceReviewRows,
    operatingCoverageRows,
    lateAdjustments,
    packageRows,
    selectedPackage,
    certificationSafeguards: buildAccountingReportCertificationSafeguards(closePlan, selectedBundle, criticalIssueCount),
    closeWorkflowSteps,
    validationIssues,
    liveRegionText: `Close report package ${statusLabel}. ${formatCount(openTaskCount, "open task")}. ${formatCount(packageRows.length, "package")}. ${formatCount(closeWorkflowSteps.filter((step) => step.tone === "danger" || step.tone === "warning").length, "workflow step")} needs review.`,
    refresh,
    buildReportPackage,
    certifyPackage,
    lockClosePeriod,
    configureClosePlan,
    signOffNextTask,
    updateCloseSetupDraft,
    selectCloseSetupTask,
    toggleCloseSetupDependency,
    selectCloseSetupSignOffRole,
    updateCloseSignOffDraft,
    selectCloseSignOffTask,
    selectCloseSignOffRole,
    selectCloseSignOffDecision,
    updateLateAdjustmentDraft,
    createLateAdjustment,
    reviewLateAdjustment,
    reviewCloseEvidence,
    inspectSelectedPackageExport,
    selectPackage
  };
}

function buildAccountingReportCertificationSafeguards(
  closePlan: ClosePeriodPlan | null,
  bundle: AccountingReportPackageBundle | null,
  criticalIssueCount: number
): AccountingReportCertificationSafeguardViewModel[] {
  const serviceOwnedReadiness = bundle?.closeReadinessItems ?? [];
  if (serviceOwnedReadiness.length > 0) {
    return serviceOwnedReadiness.map((item) => ({
      id: item.itemId,
      label: item.label,
      value: formatAccountingReadinessState(item.state),
      detail: item.blockingIssueCount > 0
        ? `${item.summary} ${item.requiredAction} ${formatCount(item.blockingIssueCount, "blocking issue")} attached.`
        : item.evidenceLinks.length > 0
          ? `${item.summary} ${formatCount(item.evidenceLinks.length, "evidence link")} retained.`
          : `${item.summary} ${item.requiredAction}`,
      tone: accountingReadinessStateTone(item.state)
    }));
  }

  const tasks = closePlan?.tasks ?? [];
  const signedOffTaskCount = tasks.filter((task) => task.status === "SignedOff").length;
  const openTaskCount = tasks.filter((task) => task.status !== "SignedOff").length;
  const exportArtifacts = bundle?.exportArtifacts ?? [];
  const certifiedExportCount = exportArtifacts.filter((artifact) => artifact.certificationState === "Certified").length;
  const restatement = bundle?.financialStatements.restatement ?? bundle?.navPackage.restatement ?? null;
  const evidenceCount = bundle ? collectAccountingReportPackageEvidenceLinks(bundle).length : 0;

  return [
    {
      id: "checklist-signoff",
      label: "Checklist sign-off",
      value: closePlan ? `${signedOffTaskCount}/${tasks.length}` : "Not loaded",
      detail: closePlan
        ? openTaskCount > 0
          ? `${formatCount(openTaskCount, "close task")} still requires retained sign-off evidence.`
          : "All close checklist tasks are signed off with retained evidence."
        : "Load the close plan before report package certification.",
      tone: closePlan
        ? openTaskCount > 0
          ? "warning"
          : "success"
        : "default"
    },
    {
      id: "period-lock",
      label: "Period lock",
      value: closePlan?.isPeriodLocked ? "Locked after close" : closePlan ? "Open for adjustments" : "Not loaded",
      detail: closePlan
        ? closePlan.isPeriodLocked
          ? "Close lock is retained; posted entries require reversal or rebook workflows."
          : "Period remains open, so late adjustments can still change the package evidence set."
        : "Load the close plan before evaluating period-lock posture.",
      tone: closePlan?.isPeriodLocked ? "success" : closePlan ? "warning" : "default"
    },
    {
      id: "critical-validation",
      label: "Critical validation blockers",
      value: criticalIssueCount > 0 ? String(criticalIssueCount) : "Clear",
      detail: criticalIssueCount > 0
        ? `${formatCount(criticalIssueCount, "critical issue")} must clear before certification.`
        : "No critical close or report package validation issues are surfaced.",
      tone: criticalIssueCount > 0 ? "danger" : "success"
    },
    {
      id: "export-certification",
      label: "Export certification",
      value: bundle
        ? exportArtifacts.length > 0
          ? `${certifiedExportCount}/${exportArtifacts.length}`
          : "No artifacts"
        : "Not loaded",
      detail: bundle
        ? exportArtifacts.length > 0
          ? certifiedExportCount === exportArtifacts.length
            ? "Every retained export artifact is certified."
            : `${formatCount(exportArtifacts.length - certifiedExportCount, "export artifact")} remains ready for review.`
          : "No retained export artifacts are attached to the selected package."
        : "Build or select a package before export certification review.",
      tone: bundle
        ? exportArtifacts.length === 0
          ? "default"
          : certifiedExportCount === exportArtifacts.length
            ? "success"
            : "warning"
        : "default"
    },
    {
      id: "restatement-workflow",
      label: "Restatement workflow",
      value: restatement ? restatement.approvalState : "No restatement",
      detail: restatement
        ? `${restatement.reasonCode} restates ${restatement.priorPackageId}; approval evidence is retained with the package.`
        : "No restatement workflow is attached to the selected package.",
      tone: restatement
        ? restatement.approvalState === "Approved"
          ? "success"
          : restatement.approvalState === "Rejected"
            ? "danger"
            : "warning"
        : "success"
    },
    {
      id: "evidence-package",
      label: "Evidence package",
      value: bundle ? formatCount(evidenceCount, "evidence link") : "Not loaded",
      detail: bundle
        ? "Certification will submit the retained financial statement, investor capital, realized gain/loss, NAV, restatement, and package evidence links."
        : "Build or select a package before evidence certification review.",
      tone: bundle ? evidenceCount > 0 ? "success" : "warning" : "default"
    }
  ];
}

function buildAccountingCloseWorkflowSteps({
  closePlan,
  selectedBundle,
  selectedExportArtifact,
  tasks,
  lateAdjustments,
  evidenceReviewRows,
  validationIssues,
  packageRows,
  configureClosePlanDisabledReason,
  signOffDisabledReason,
  createLateAdjustmentDisabledReason,
  reviewLateAdjustmentDisabledReason,
  reviewCloseEvidenceDisabledReason,
  buildDisabledReason,
  certifyDisabledReason,
  exportManifestDisabledReason,
  lockClosePeriodDisabledReason
}: {
  closePlan: ClosePeriodPlan | null;
  selectedBundle: AccountingReportPackageBundle | null;
  selectedExportArtifact: ReportExportArtifact | null;
  tasks: AccountingClosePlanTaskRowViewModel[];
  lateAdjustments: AccountingLateAdjustmentRowViewModel[];
  evidenceReviewRows: AccountingCloseEvidenceReviewRowViewModel[];
  validationIssues: AccountingConfigurationIssueViewModel[];
  packageRows: AccountingReportPackageRowViewModel[];
  configureClosePlanDisabledReason: string | null;
  signOffDisabledReason: string | null;
  createLateAdjustmentDisabledReason: string | null;
  reviewLateAdjustmentDisabledReason: string | null;
  reviewCloseEvidenceDisabledReason: string | null;
  buildDisabledReason: string | null;
  certifyDisabledReason: string | null;
  exportManifestDisabledReason: string | null;
  lockClosePeriodDisabledReason: string | null;
}): AccountingCloseWorkflowStepViewModel[] {
  const closeSetupEvidenceCount = closePlan
    ? closePlan.tasks.reduce((count, task) => count + task.evidenceLinks.length, 0)
      + closePlan.lateAdjustments.reduce((count, adjustment) => count + adjustment.evidenceLinks.length, 0)
    : 0;
  const setupRetained = Boolean(closePlan?.materialityPolicy && closePlan.tasks.length > 0);
  const openTaskCount = tasks.filter((task) => task.statusTone !== "success").length;
  const pendingLateAdjustmentCount = lateAdjustments.filter((adjustment) => adjustment.reviewDisabledReason === null).length;
  const activeReviewRows = evidenceReviewRows.filter((row) => row.issueCode && !row.reviewDisabledReason);
  const retainedReviewCount = evidenceReviewRows.filter((row) => row.statusLabel === "Review retained").length;
  const criticalIssueCount = validationIssues.filter((issue) => issue.tone === "danger").length;
  const certifiedPackage = selectedBundle?.certification.state === "Certified";
  const readyPackage = selectedBundle?.certification.state === "ReadyForReview";
  const locked = closePlan?.isPeriodLocked === true;

  return [
    {
      id: "close-setup",
      label: "Close setup",
      statusLabel: setupRetained ? "Retained" : closePlan ? "Draft loaded" : "Pending",
      detail: setupRetained
        ? "Materiality, selected task setup, dependency reasons, and sign-off requirements are retained on the shared close plan."
        : closePlan
          ? "Review materiality, dependency graph, and sign-off matrix edits before retaining setup evidence."
          : "Load a close workflow before close setup can be reviewed.",
      evidenceLabel: setupRetained
        ? formatCount(closeSetupEvidenceCount, "evidence link")
        : closePlan
          ? "Setup evidence not retained"
          : "No close plan",
      tone: setupRetained ? "success" : closePlan ? "warning" : "default",
      actionLabel: "Retain setup",
      actionId: "configure-close-plan",
      disabledReason: configureClosePlanDisabledReason
    },
    {
      id: "checklist-signoff",
      label: "Checklist sign-off",
      statusLabel: tasks.length === 0 ? "Pending" : openTaskCount === 0 ? "Signed off" : `${openTaskCount} open`,
      detail: tasks.length === 0
        ? "Shared checklist tasks have not loaded."
        : openTaskCount === 0
          ? "All loaded checklist tasks report retained sign-off posture."
          : "Retain the next ready checklist task decision through close management.",
      evidenceLabel: tasks.length === 0
        ? "No checklist rows"
        : `${tasks.filter((task) => task.statusTone === "success").length}/${tasks.length} task rows ready`,
      tone: tasks.length === 0 ? "default" : openTaskCount === 0 ? "success" : "warning",
      actionLabel: "Sign off task",
      actionId: "sign-off-task",
      disabledReason: signOffDisabledReason
    },
    {
      id: "late-adjustments",
      label: "Late adjustments",
      statusLabel: lateAdjustments.length === 0 ? "None" : pendingLateAdjustmentCount === 0 ? "Reviewed" : `${pendingLateAdjustmentCount} pending`,
      detail: lateAdjustments.length === 0
        ? "No late adjustments are retained on this close plan."
        : pendingLateAdjustmentCount === 0
          ? "Loaded late adjustments have retained review decisions or are no longer actionable."
          : "Review submitted material late adjustments before final close certification.",
      evidenceLabel: lateAdjustments.length === 0
        ? "No late-adjustment evidence"
        : formatCount(lateAdjustments.length, "late adjustment"),
      tone: pendingLateAdjustmentCount > 0 ? "warning" : "success",
      actionLabel: pendingLateAdjustmentCount > 0 ? null : "Request adjustment",
      actionId: pendingLateAdjustmentCount > 0 ? null : "request-late-adjustment",
      disabledReason: pendingLateAdjustmentCount > 0
        ? reviewLateAdjustmentDisabledReason
        : createLateAdjustmentDisabledReason
    },
    {
      id: "blocker-review",
      label: "Blocker review",
      statusLabel: activeReviewRows.length > 0 ? `${activeReviewRows.length} unreviewed` : retainedReviewCount > 0 ? "Reviewed" : validationIssues.length > 0 ? "No action" : "Clear",
      detail: activeReviewRows.length > 0
        ? "Retain operator review evidence for active blockers without clearing service-owned validation state."
        : validationIssues.length > 0
          ? "Validation issues remain, but retained blocker review is already present or not actionable from this row."
          : "No active close/report blockers are surfaced.",
      evidenceLabel: retainedReviewCount > 0
        ? formatCount(retainedReviewCount, "retained review")
        : validationIssues.length > 0
          ? formatCount(validationIssues.length, "validation issue")
          : "No blockers",
      tone: activeReviewRows.length > 0 || criticalIssueCount > 0 ? "warning" : "success",
      actionLabel: activeReviewRows.length > 0 ? "Retain review" : null,
      actionId: activeReviewRows.length > 0 ? "review-evidence" : null,
      disabledReason: reviewCloseEvidenceDisabledReason
    },
    {
      id: "report-package",
      label: "Report package",
      statusLabel: selectedBundle ? formatAccountingCertificationState(selectedBundle.certification.state) : "Not built",
      detail: selectedBundle
        ? "The selected report package carries financial statements, investor capital statements, NAV, export artifacts, and retained evidence counts."
        : "Build the accounting report package after close setup and checklist evidence are available.",
      evidenceLabel: selectedBundle
        ? `${formatCount(packageRows.length, "package")} retained`
        : "No package history",
      tone: certifiedPackage ? "success" : selectedBundle ? "warning" : "default",
      actionLabel: selectedBundle ? "Rebuild package" : "Build package",
      actionId: "build-package",
      disabledReason: buildDisabledReason
    },
    {
      id: "certification",
      label: "Certification",
      statusLabel: certifiedPackage ? "Certified" : readyPackage ? "Ready for review" : selectedBundle ? formatAccountingCertificationState(selectedBundle.certification.state) : "Pending",
      detail: certifiedPackage
        ? "The selected report package is certified with retained certification evidence."
        : readyPackage
          ? "The selected report package is ready for certification once remaining close blockers are acceptable to the service."
          : "Certification waits for a ready report package and service-owned validation posture.",
      evidenceLabel: selectedBundle
        ? formatCount(selectedBundle.certification.evidenceLinks.length, "certification evidence link")
        : "No certification evidence",
      tone: certifiedPackage ? "success" : readyPackage ? "warning" : "default",
      actionLabel: "Certify package",
      actionId: "certify-package",
      disabledReason: certifyDisabledReason
    },
    {
      id: "export-manifest",
      label: "Export manifest",
      statusLabel: selectedExportArtifact ? formatAccountingCertificationState(selectedExportArtifact.certificationState) : "Pending",
      detail: selectedExportArtifact
        ? "Inspect the retained export manifest before period lock or downstream GL handoff."
        : "The selected package has no retained export artifact to inspect.",
      evidenceLabel: selectedExportArtifact
        ? selectedExportArtifact.artifactId
        : "No export artifact",
      tone: selectedExportArtifact ? accountingCertificationTone(selectedExportArtifact.certificationState) : "default",
      actionLabel: selectedExportArtifact ? "Inspect manifest" : null,
      actionId: selectedExportArtifact ? "inspect-export" : null,
      disabledReason: exportManifestDisabledReason
    },
    {
      id: "period-lock",
      label: "Period lock",
      statusLabel: locked ? "Locked" : closePlan ? "Open" : "Pending",
      detail: locked
        ? "The close period is locked; new close mutations require a governed reopen workflow."
        : closePlan
          ? "Period lock submits workflow version, report package, checklist approvals, manifest route, and ledger-book evidence to the shared service."
          : "Load a close plan before period-lock review.",
      evidenceLabel: closePlan?.ledgerBookId ? `Book ${closePlan.ledgerBookId}` : "No ledger-book scope",
      tone: locked ? "success" : criticalIssueCount > 0 ? "warning" : closePlan ? "default" : "default",
      actionLabel: "Lock period",
      actionId: "lock-period",
      disabledReason: lockClosePeriodDisabledReason
    }
  ];
}

function formatAccountingReadinessState(state: string): string {
  const labels: Record<string, string> = {
    NotStarted: "Not started",
    NeedsAttention: "Needs attention",
    Blocked: "Blocked",
    ReadyForReview: "Ready for review",
    Certified: "Certified"
  };
  return labels[state] ?? state;
}

function accountingReadinessStateTone(state: string): AccountingToolingTone {
  if (state === "Certified" || state === "ReadyForReview") {
    return "success";
  }

  if (state === "Blocked") {
    return "danger";
  }

  if (state === "NeedsAttention") {
    return "warning";
  }

  return "default";
}

function buildAccountingReportExportManifestViewModel(
  manifest: ReportExportArtifactManifest
): AccountingReportExportManifestViewModel {
  return {
    packageId: manifest.packageId,
    artifactId: manifest.artifactId,
    displayName: manifest.displayName,
    formatLabel: `${manifest.format} | ${manifest.contentType}`,
    fileName: manifest.fileName,
    certificationLabel: formatAccountingCertificationState(manifest.certificationState),
    generatedLabel: formatDateTimeLabel(manifest.generatedAtUtc),
    hashLabel: manifest.contentHash,
    evidenceLabel: formatCount(manifest.evidenceLinks.length, "evidence link"),
    postingLabel: manifest.externalPostingAllowed ? "External posting allowed" : "External posting disabled",
    routeLabel: manifest.route
  };
}

function resolveCloseTaskSignOffTarget(closePlan: ClosePeriodPlan): { task: CloseTask; role: string } | null {
  if (closePlan.isPeriodLocked) {
    return null;
  }

  for (const task of closePlan.tasks) {
    if (task.status !== "ReadyForSignOff") {
      continue;
    }

    const unsatisfiedRequirement = (task.signOffRequirements ?? []).find(
      (requirement) => !requirement.isSatisfied && requirement.approvedCount < requirement.requiredApprovalCount
    );
    if (unsatisfiedRequirement) {
      return { task, role: unsatisfiedRequirement.role };
    }

    if ((task.signOffRequirements ?? []).length === 0 && task.signOffs.length === 0) {
      return { task, role: task.owner || "controller" };
    }
  }

  return null;
}

function resolveCloseTaskSignOffDraftTarget(
  closePlan: ClosePeriodPlan,
  draft: AccountingCloseSignOffDraftViewModel
): { task: CloseTask; role: string } | null {
  const taskId = draft.taskId.trim().toLowerCase();
  const task = closePlan.tasks.find((item) => item.taskId.toLowerCase() === taskId) ?? null;
  if (!task || task.status !== "ReadyForSignOff") {
    return null;
  }

  const role = draft.role.trim();
  return role ? { task, role } : null;
}

function createAccountingCloseSignOffDraft(
  closePlan: ClosePeriodPlan | null,
  selectedTaskId?: string,
  previousDraft?: AccountingCloseSignOffDraftViewModel
): AccountingCloseSignOffDraftViewModel {
  const selectedTask = selectedTaskId
    ? closePlan?.tasks.find((task) => task.taskId.toLowerCase() === selectedTaskId.toLowerCase()) ?? null
    : null;
  const target = selectedTask && selectedTask.status === "ReadyForSignOff"
    ? {
      task: selectedTask,
      role: resolvePreferredCloseSignOffRole(selectedTask, previousDraft?.role)
    }
    : closePlan
      ? resolveCloseTaskSignOffTarget(closePlan)
      : null;
  const decision = previousDraft?.decision === "Rejected" ? "Rejected" : "Approved";

  return {
    taskId: target?.task.taskId ?? "",
    role: target?.role ?? "",
    decision,
    notes: previousDraft?.notes ?? ""
  };
}

function resolvePreferredCloseSignOffRole(task: CloseTask, preferredRole?: string): string {
  const normalizedPreferredRole = preferredRole?.trim();
  if (normalizedPreferredRole && isCloseTaskSignOffRoleAllowed(task, normalizedPreferredRole)) {
    return normalizedPreferredRole;
  }

  const unsatisfiedRequirement = (task.signOffRequirements ?? []).find(
    (requirement) => !requirement.isSatisfied && requirement.approvedCount < requirement.requiredApprovalCount
  );
  return unsatisfiedRequirement?.role?.trim()
    || task.signOffRequirements?.[0]?.role?.trim()
    || task.signOffs[0]?.role?.trim()
    || task.owner?.trim()
    || "controller";
}

function validateCloseSignOffDraft(
  closePlan: ClosePeriodPlan,
  draft: AccountingCloseSignOffDraftViewModel
): string | null {
  const taskId = draft.taskId.trim();
  if (!taskId) {
    return "Select a close checklist task before signing off.";
  }

  const task = closePlan.tasks.find((item) => item.taskId.toLowerCase() === taskId.toLowerCase()) ?? null;
  if (!task) {
    return `Close checklist task ${taskId} is not loaded in this close plan.`;
  }

  if (task.status !== "ReadyForSignOff") {
    return `${task.displayName} is ${formatCloseTaskStatus(task.status).toLowerCase()} and is not ready for sign-off.`;
  }

  const role = draft.role.trim();
  if (!role) {
    return "Select a sign-off role before signing off.";
  }

  if (!isCloseTaskSignOffRoleAllowed(task, role)) {
    return `${role} is not retained on the selected task sign-off matrix.`;
  }

  if (draft.decision !== "Approved" && draft.decision !== "Rejected") {
    return "Select an approved or rejected close sign-off decision.";
  }

  return null;
}

function isCloseTaskSignOffRoleAllowed(task: CloseTask, role: string): boolean {
  const normalizedRole = role.trim().toLowerCase();
  if (!normalizedRole) {
    return false;
  }

  const retainedRoles = [
    ...(task.signOffRequirements ?? []).map((requirement) => requirement.role),
    ...task.signOffs.map((signOff) => signOff.role),
    task.owner
  ]
    .map((item) => item?.trim())
    .filter((item): item is string => Boolean(item));

  return retainedRoles.length === 0 || retainedRoles.some((item) => item.toLowerCase() === normalizedRole);
}

function createAccountingLateAdjustmentDraft(currency = "USD"): AccountingLateAdjustmentDraftViewModel {
  return {
    journalEntryId: "",
    amount: "",
    currency,
    reason: ""
  };
}

function createAccountingCloseSetupDraft(
  closePlan: ClosePeriodPlan | null,
  taskId: string | null = null
): AccountingCloseSetupDraftViewModel {
  const materiality = closePlan?.materialityPolicy;
  const normalizedTaskId = taskId?.trim().toLowerCase() ?? "";
  const task = normalizedTaskId
    ? closePlan?.tasks.find((item) => item.taskId.toLowerCase() === normalizedTaskId) ?? closePlan?.tasks[0] ?? null
    : closePlan?.tasks[0] ?? null;
  const signOffRequirements = task?.signOffRequirements ?? [];
  const requiredApprovalCount = Math.max(1, ...signOffRequirements.map((requirement) => requirement.requiredApprovalCount));
  const requiredApprovalRole = signOffRequirements[0]?.role?.trim() ?? "";
  const requiredEvidence = signOffRequirements
    .map((requirement) => requirement.evidenceRequirement.trim())
    .filter(Boolean)
    .join("; ");
  const signOffRequirementRows = buildCloseSetupSignOffRequirementRows(signOffRequirements);

  return {
    amountThreshold: materiality ? String(materiality.amountThreshold) : "",
    percentThreshold: materiality ? String(materiality.percentThreshold) : "",
    currency: materiality?.currency ?? "USD",
    reviewRole: materiality?.reviewRole ?? "Controller",
    requiresLateAdjustmentApproval: materiality?.requiresLateAdjustmentApproval ?? true,
    taskId: task?.taskId ?? "",
    taskDisplayName: task?.displayName ?? "",
    taskOwner: task?.owner ?? "",
    taskDueDate: task?.dueDate ?? "",
    taskRequiredApprovalCount: task ? String(requiredApprovalCount) : "1",
    taskRequiredApprovalRole: requiredApprovalRole || task?.owner || "Controller",
    taskRequiredEvidence: requiredEvidence || "Retained close checklist evidence",
    taskSignOffRequirements: signOffRequirementRows,
    taskDependsOnTaskIds: task?.dependencies.map((dependency) => dependency.dependsOnTaskId).join(", ") ?? "",
    taskDependencyReason: buildCloseSetupDependencyReason(task?.dependencies ?? [])
  };
}

function validateCloseSetupTaskSelection(
  closePlan: ClosePeriodPlan,
  setupDraft: AccountingCloseSetupDraftViewModel
): string | null {
  const taskId = setupDraft.taskId.trim();
  if (closePlan.tasks.length === 0) {
    return taskId
      ? `Close checklist task ${taskId} is not loaded in this close plan.`
      : null;
  }

  if (!taskId) {
    return "Select a retained close checklist task before saving close setup.";
  }

  if (!closePlan.tasks.some((task) => task.taskId.toLowerCase() === taskId.toLowerCase())) {
    return `Close checklist task ${taskId} is not loaded in this close plan.`;
  }

  return null;
}

function validateCloseSetupMaterialityDraft(setupDraft: AccountingCloseSetupDraftViewModel): string | null {
  if (!setupDraft.amountThreshold.trim()) {
    return "Enter a materiality amount threshold before saving close setup.";
  }

  const amountThreshold = Number(setupDraft.amountThreshold);
  if (!Number.isFinite(amountThreshold) || amountThreshold < 0) {
    return "Enter a non-negative materiality amount threshold before saving close setup.";
  }

  if (!setupDraft.percentThreshold.trim()) {
    return "Enter a materiality percent threshold before saving close setup.";
  }

  const percentThreshold = Number(setupDraft.percentThreshold);
  if (!Number.isFinite(percentThreshold) || percentThreshold < 0) {
    return "Enter a non-negative materiality percent threshold before saving close setup.";
  }

  const currency = setupDraft.currency.trim();
  if (!/^[A-Za-z]{3}$/.test(currency)) {
    return "Enter a three-letter materiality currency before saving close setup.";
  }

  if (!setupDraft.reviewRole.trim()) {
    return "Enter a materiality review role before saving close setup.";
  }

  return null;
}

function validateCloseSetupSignOffDraft(setupDraft: AccountingCloseSetupDraftViewModel): string | null {
  const signOffMatrixValidation = validateCloseSetupSignOffRequirementRows(setupDraft.taskSignOffRequirements);
  if (signOffMatrixValidation) {
    return signOffMatrixValidation;
  }

  const requiredApprovalCount = Number.parseInt(setupDraft.taskRequiredApprovalCount, 10);
  if (!Number.isFinite(requiredApprovalCount) || requiredApprovalCount <= 0) {
    return "Enter a positive required approval count before saving close setup.";
  }

  if (!setupDraft.taskRequiredApprovalRole.trim()) {
    return "Enter an approval role before saving close setup.";
  }

  if (!setupDraft.taskRequiredEvidence.trim()) {
    return "Enter required sign-off evidence before saving close setup.";
  }

  return null;
}

function validateCloseSetupSignOffRequirementRows(value: string): string | null {
  for (const entry of splitCloseSetupSignOffRequirementRows(value)) {
    const requirement = parseCloseSetupSignOffRequirementEntry(entry);
    if (!requirement.role) {
      return "Enter a role for every sign-off matrix row before saving close setup.";
    }

    if (!Number.isFinite(requirement.requiredApprovalCount) || requirement.requiredApprovalCount <= 0) {
      return `Enter a positive approval count for ${requirement.role} before saving close setup.`;
    }
  }

  return null;
}

function splitCloseSetupSignOffRequirementRows(value: string): string[] {
  return value
    .split(/[\r\n;]+/)
    .map((item) => item.trim())
    .filter(Boolean);
}

function parseCloseSetupSignOffRequirementEntry(value: string): { role: string; requiredApprovalCount: number; evidenceRequirement: string } {
  const parts = value.includes("|")
    ? value.split("|").map((part) => part.trim())
    : value.split(":").map((part) => part.trim());
  const role = parts[0] ?? "";
  const countText = parts.length > 1 ? parts[1] : "1";
  const requiredApprovalCount = Number.parseInt(countText, 10);
  return {
    role,
    requiredApprovalCount,
    evidenceRequirement: parts.slice(2).join(":").trim()
  };
}

function parseCloseSetupSignOffRequirementRows(value: string): Array<{ role: string; requiredApprovalCount: number; evidenceRequirement: string }> {
  return splitCloseSetupSignOffRequirementRows(value)
    .map((item) => parseCloseSetupSignOffRequirementEntry(item))
    .filter((requirement, index, requirements) =>
      requirement.role.length > 0 &&
      Number.isFinite(requirement.requiredApprovalCount) &&
      requirement.requiredApprovalCount > 0 &&
      requirements.findIndex((candidate) => candidate.role.toLowerCase() === requirement.role.toLowerCase()) === index);
}

function buildCloseSetupSingleSignOffRequirementRow(setupDraft: AccountingCloseSetupDraftViewModel): string {
  const requiredApprovalCount = Number.parseInt(setupDraft.taskRequiredApprovalCount, 10);
  const role = setupDraft.taskRequiredApprovalRole.trim() || "Controller";
  const evidence = setupDraft.taskRequiredEvidence.trim() || "Retained close checklist evidence";
  return `${role} | ${Number.isFinite(requiredApprovalCount) && requiredApprovalCount > 0 ? requiredApprovalCount : 1} | ${evidence}`;
}

function parseCloseSetupDependencyIds(value: string): string[] {
  return value
    .split(/[,\r\n;]+/)
    .map((item) => parseCloseSetupDependencyEntry(item).taskId)
    .filter((item, index, items) => item.length > 0 && items.findIndex((candidate) => candidate.toLowerCase() === item.toLowerCase()) === index);
}

function parseCloseSetupDependencyEntry(value: string): { taskId: string; reason: string | null } {
  const item = value.trim();
  if (!item) {
    return { taskId: "", reason: null };
  }

  const separatorIndex = [item.indexOf(":"), item.indexOf("=")]
    .filter((index) => index > 0)
    .sort((left, right) => left - right)[0];
  if (separatorIndex === undefined) {
    return { taskId: item, reason: null };
  }

  const taskId = item.slice(0, separatorIndex).trim();
  const reason = item.slice(separatorIndex + 1).trim();
  return {
    taskId,
    reason: reason.length > 0 ? reason : null
  };
}

function parseCloseSetupDependencyReasonOverrides(value: string): Map<string, string> {
  const overrides = new Map<string, string>();
  value
    .split(/[\r\n;]+/)
    .map((item) => parseCloseSetupDependencyEntry(item))
    .forEach((entry) => {
      if (entry.taskId && entry.reason && !overrides.has(entry.taskId.toLowerCase())) {
        overrides.set(entry.taskId.toLowerCase(), entry.reason);
      }
    });
  return overrides;
}

function resolveCloseSetupDependencyReason(
  dependsOnTaskId: string,
  dependencyIdReasons: Map<string, string>,
  dependencyReasonOverrides: Map<string, string>,
  fallbackReason: string,
  existingDependencies: CloseDependency[]
): string {
  const configuredReason = dependencyIdReasons.get(dependsOnTaskId.toLowerCase())
    ?? dependencyReasonOverrides.get(dependsOnTaskId.toLowerCase())
    ?? fallbackReason;
  return configuredReason
    || existingDependencies.find((dependency) =>
      dependency.dependsOnTaskId.toLowerCase() === dependsOnTaskId.toLowerCase()
    )?.reason
    || "Configured close-plan dependency.";
}

function buildCloseSetupDependencyReason(dependencies: CloseDependency[]): string {
  const reasons = dependencies
    .map((dependency) => dependency.reason?.trim())
    .filter((reason): reason is string => Boolean(reason))
    .filter((reason, index, items) => items.findIndex((candidate) => candidate.toLowerCase() === reason.toLowerCase()) === index);
  return reasons.length === 1 ? reasons[0] : "";
}

function buildCloseSetupSignOffRequirementRows(requirements: CloseSignOffRequirement[]): string {
  return requirements
    .map((requirement) => {
      const evidence = requirement.evidenceRequirement.trim() || "Retained close checklist evidence";
      return `${requirement.role} | ${Math.max(1, requirement.requiredApprovalCount)} | ${evidence}`;
    })
    .join("\n");
}

function buildCloseSetupTaskOptions(
  closePlan: ClosePeriodPlan,
  selectedTaskId: string
): AccountingCloseSetupTaskOptionViewModel[] {
  const normalizedSelectedTaskId = selectedTaskId.trim().toLowerCase();
  return closePlan.tasks.map((task) => {
    const requirements = task.signOffRequirements ?? [];
    const requiredCount = requirements.reduce((total, requirement) => total + Math.max(0, requirement.requiredApprovalCount), 0);
    const approvedRequirementCount = requirements.reduce((total, requirement) => total + Math.max(0, requirement.approvedCount), 0);
    const selected = task.taskId.toLowerCase() === normalizedSelectedTaskId;

    return {
      taskId: task.taskId,
      displayName: task.displayName,
      statusLabel: formatCloseTaskStatus(task.status),
      statusTone: closeTaskStatusTone(task.status),
      ownerLabel: task.owner || "Unassigned",
      dueDateLabel: formatDateOnly(task.dueDate),
      dependencyLabel: task.dependencies.length > 0
        ? `${formatCount(task.dependencies.length, "dependency")}: ${task.dependencies.map((dependency) => dependency.dependsOnTaskId).join(", ")}`
        : "No dependencies",
      signOffLabel: requirements.length > 0
        ? `${approvedRequirementCount}/${requiredCount} approvals`
        : "No sign-off matrix supplied",
      selected,
      selectAriaLabel: `${selected ? "Selected" : "Select"} close setup task ${task.displayName}`
    };
  });
}

function buildCloseSetupDependencyOptions(
  closePlan: ClosePeriodPlan,
  setupDraft: AccountingCloseSetupDraftViewModel
): AccountingCloseSetupDependencyOptionViewModel[] {
  const selectedTaskId = setupDraft.taskId.trim().toLowerCase();
  if (!selectedTaskId || !closePlan.tasks.some((task) => task.taskId.toLowerCase() === selectedTaskId)) {
    return [];
  }

  const selectedDependencyIds = new Set(parseCloseSetupDependencyIds(setupDraft.taskDependsOnTaskIds).map((item) => item.toLowerCase()));
  return closePlan.tasks
    .filter((task) => task.taskId.toLowerCase() !== selectedTaskId)
    .map((task) => {
      const checked = selectedDependencyIds.has(task.taskId.toLowerCase());
      return {
        taskId: task.taskId,
        displayName: task.displayName,
        statusLabel: formatCloseTaskStatus(task.status),
        statusTone: closeTaskStatusTone(task.status),
        ownerLabel: task.owner || "Unassigned",
        dueDateLabel: formatDateOnly(task.dueDate),
        checked,
        toggleAriaLabel: `${checked ? "Remove" : "Add"} ${task.displayName} as a dependency`
      };
    });
}

function buildCloseSetupSignOffRoleOptions(
  closePlan: ClosePeriodPlan,
  setupDraft: AccountingCloseSetupDraftViewModel
): AccountingCloseSetupSignOffRoleOptionViewModel[] {
  const selectedTaskId = setupDraft.taskId.trim().toLowerCase();
  const selectedTask = closePlan.tasks.find((task) => task.taskId.toLowerCase() === selectedTaskId) ?? closePlan.tasks[0] ?? null;
  if (!selectedTask) {
    return [];
  }

  const selectedRole = setupDraft.taskRequiredApprovalRole.trim().toLowerCase();
  const options: AccountingCloseSetupSignOffRoleOptionViewModel[] = [];
  const addRole = (role: string | null | undefined, sourceLabel: string) => {
    const normalizedRole = role?.trim();
    if (!normalizedRole) {
      return;
    }

    if (options.some((option) => option.role.toLowerCase() === normalizedRole.toLowerCase())) {
      return;
    }

    const selected = normalizedRole.toLowerCase() === selectedRole;
    options.push({
      role: normalizedRole,
      label: normalizedRole,
      sourceLabel,
      selected,
      selectAriaLabel: `${selected ? "Selected" : "Select"} close sign-off role ${normalizedRole}`
    });
  };

  for (const requirement of selectedTask.signOffRequirements ?? []) {
    addRole(requirement.role, "Required role");
  }

  for (const signOff of selectedTask.signOffs) {
    addRole(signOff.role, "Retained sign-off");
  }

  addRole(selectedTask.owner, "Task owner");
  addRole(closePlan.materialityPolicy.reviewRole, "Materiality reviewer");
  addRole(setupDraft.taskRequiredApprovalRole, "Draft role");

  return options;
}

function buildCloseSignOffTaskOptions(
  closePlan: ClosePeriodPlan,
  signOffDraft: AccountingCloseSignOffDraftViewModel
): AccountingCloseSignOffTaskOptionViewModel[] {
  const normalizedSelectedTaskId = signOffDraft.taskId.trim().toLowerCase();
  return closePlan.tasks
    .filter((task) => task.status === "ReadyForSignOff")
    .map((task) => {
      const row = buildClosePlanTaskRow(task);
      const selected = task.taskId.toLowerCase() === normalizedSelectedTaskId;

      return {
        taskId: task.taskId,
        displayName: task.displayName,
        statusLabel: row.statusLabel,
        statusTone: row.statusTone,
        ownerLabel: task.owner || "Unassigned",
        signOffLabel: row.signOffLabel,
        selected,
        selectAriaLabel: `${selected ? "Selected" : "Select"} close task sign-off ${task.displayName}`
      };
    });
}

function buildCloseSignOffRoleOptions(
  closePlan: ClosePeriodPlan,
  signOffDraft: AccountingCloseSignOffDraftViewModel
): AccountingCloseSignOffRoleOptionViewModel[] {
  const selectedTask = closePlan.tasks.find((task) => task.taskId.toLowerCase() === signOffDraft.taskId.trim().toLowerCase()) ?? null;
  if (!selectedTask) {
    return [];
  }

  const selectedRole = signOffDraft.role.trim().toLowerCase();
  const options: AccountingCloseSignOffRoleOptionViewModel[] = [];
  const addRole = (role: string | null | undefined, sourceLabel: string) => {
    const normalizedRole = role?.trim();
    if (!normalizedRole || options.some((option) => option.role.toLowerCase() === normalizedRole.toLowerCase())) {
      return;
    }

    const selected = normalizedRole.toLowerCase() === selectedRole;
    options.push({
      role: normalizedRole,
      label: normalizedRole,
      sourceLabel,
      selected,
      selectAriaLabel: `${selected ? "Selected" : "Select"} close task sign-off role ${normalizedRole}`
    });
  };

  for (const requirement of selectedTask.signOffRequirements ?? []) {
    addRole(requirement.role, requirement.isSatisfied ? "Satisfied role" : "Required role");
  }

  for (const signOff of selectedTask.signOffs) {
    addRole(signOff.role, "Retained sign-off");
  }

  addRole(selectedTask.owner, "Task owner");
  addRole(signOffDraft.role, "Draft role");

  return options;
}

function buildCloseSignOffDecisionOptions(
  signOffDraft: AccountingCloseSignOffDraftViewModel
): AccountingCloseSignOffDecisionOptionViewModel[] {
  return (["Approved", "Rejected"] as const).map((decision) => ({
    decision,
    label: decision,
    selected: signOffDraft.decision === decision,
    selectAriaLabel: `${signOffDraft.decision === decision ? "Selected" : "Select"} close task sign-off decision ${decision}`
  }));
}

function buildClosePlanTaskRow(task: CloseTask): AccountingClosePlanTaskRowViewModel {
  const signedOffCount = task.signOffs.filter((signOff) => signOff.approvalState === "Approved").length;
  const requirements = task.signOffRequirements ?? [];
  const requiredCount = requirements.reduce((total, requirement) => total + Math.max(0, requirement.requiredApprovalCount), 0);
  const approvedRequirementCount = requirements.reduce((total, requirement) => total + Math.max(0, requirement.approvedCount), 0);

  return {
    taskId: task.taskId,
    displayName: task.displayName,
    ownerLabel: task.owner || "Unassigned",
    dueDateLabel: formatDateOnly(task.dueDate),
    statusLabel: formatCloseTaskStatus(task.status),
    statusTone: closeTaskStatusTone(task.status),
    dependencyLabel: task.dependencies.length > 0
      ? `${formatCount(task.dependencies.length, "dependency")}: ${task.dependencies.map((dependency) => dependency.dependsOnTaskId).join(", ")}`
      : "No dependencies",
    signOffLabel: requirements.length > 0
      ? `${approvedRequirementCount}/${requiredCount} required sign-offs approved`
      : task.signOffs.length > 0
        ? `${signedOffCount}/${task.signOffs.length} sign-offs approved`
        : "No sign-off required",
    signOffDetailLabel: buildCloseTaskSignOffDetail(task),
    signOffRequirementLabel: requirements.length > 0
      ? requirements.map((requirement) => `${requirement.role}: ${requirement.approvedCount}/${requirement.requiredApprovalCount}`).join(" | ")
      : "No sign-off matrix supplied",
    evidenceLabel: formatCount(task.evidenceLinks.length, "evidence link"),
    blockerLabel: task.blockerReason?.trim() || null
  };
}

function buildCloseCalendarMilestoneRow(milestone: CloseCalendarMilestone): AccountingCloseCalendarMilestoneViewModel {
  const statusTone = closeTaskStatusTone(milestone.status);
  return {
    milestoneId: milestone.milestoneId,
    displayName: milestone.displayName,
    ownerLabel: milestone.owner || "Unassigned",
    dueDateLabel: formatDateOnly(milestone.dueDate),
    statusLabel: formatCloseTaskStatus(milestone.status),
    statusTone: milestone.isBlocked ? "danger" : milestone.isSatisfied ? "success" : statusTone,
    dependencyLabel: milestone.dependencyCount > 0
      ? formatCount(milestone.dependencyCount, "dependency")
      : "No dependencies",
    signOffLabel: milestone.requiredSignOffCount > 0
      ? `${milestone.approvedSignOffCount}/${milestone.requiredSignOffCount} sign-offs`
      : "No sign-off requirement",
    evidenceLabel: formatCount(milestone.evidenceLinks.length, "evidence link"),
    blockerLabel: milestone.blockerReason?.trim() || null,
    lockedLabel: milestone.isPeriodLocked ? "Locked after close" : "Open period"
  };
}

function buildCloseTaskSignOffDetail(task: CloseTask): string | null {
  const latest = [...task.signOffs]
    .sort((left, right) => String(right.signedAtUtc ?? "").localeCompare(String(left.signedAtUtc ?? "")))[0];
  if (!latest) {
    return null;
  }

  const actor = latest.actor?.trim() || "unassigned reviewer";
  const signedAt = latest.signedAtUtc ? ` on ${formatDateTimeLabel(latest.signedAtUtc)}` : "";
  const notes = latest.notes?.trim();
  return `${latest.approvalState} by ${actor}${signedAt}${notes ? ` | ${notes}` : ""}`;
}

function buildCloseDependencyGraphRows(closePlan: ClosePeriodPlan): AccountingCloseDependencyGraphRowViewModel[] {
  const taskById = new Map(closePlan.tasks.map((task) => [task.taskId, task]));
  return closePlan.tasks.flatMap((task) =>
    task.dependencies.map((dependency) => {
      const predecessor = taskById.get(dependency.dependsOnTaskId) ?? null;
      const isSatisfied = predecessor?.status === "SignedOff";
      const isMissing = !predecessor;
      const isBlocked = task.status === "Blocked" || task.status === "WaitingOnDependency";

      return {
        dependencyId: dependency.dependencyId,
        taskId: task.taskId,
        taskLabel: task.displayName,
        dependsOnTaskId: dependency.dependsOnTaskId,
        predecessorLabel: predecessor?.displayName ?? dependency.dependsOnTaskId,
        reason: dependency.reason?.trim() || "Dependency must clear before this task can advance.",
        statusLabel: isSatisfied
          ? "Satisfied"
          : predecessor
            ? `${formatCloseTaskStatus(predecessor.status)} predecessor`
            : "Predecessor missing",
        statusTone: isSatisfied ? "success" : isMissing ? "danger" : isBlocked ? "warning" : "default",
        blockerLabel: task.blockerReason?.trim() || null
      };
    })
  );
}

function buildCloseSignOffMatrixRows(closePlan: ClosePeriodPlan): AccountingCloseSignOffMatrixRowViewModel[] {
  return closePlan.tasks.flatMap((task) => {
    const requirements = task.signOffRequirements ?? [];
    if (requirements.length === 0) {
      const fallbackRows: AccountingCloseSignOffMatrixRowViewModel[] = [{
        rowId: `${task.taskId}-owner-signoff`,
        taskId: task.taskId,
        taskLabel: task.displayName,
        roleLabel: task.owner || "controller",
        approvedLabel: `${task.signOffs.filter((signOff) => signOff.approvalState === "Approved").length}/${Math.max(1, task.signOffs.length)}`,
        statusLabel: task.signOffs.length > 0 ? "Retained sign-off" : "No matrix supplied",
        statusTone: task.signOffs.length > 0 ? "success" : "default",
        evidenceRequirementLabel: "No sign-off matrix evidence requirement supplied",
        latestSignOffLabel: buildCloseTaskSignOffDetail(task)
      }];
      return fallbackRows;
    }

    return requirements.map<AccountingCloseSignOffMatrixRowViewModel>((requirement) => ({
      rowId: requirement.requirementId,
      taskId: task.taskId,
      taskLabel: task.displayName,
      roleLabel: requirement.role,
      approvedLabel: `${requirement.approvedCount}/${requirement.requiredApprovalCount}`,
      statusLabel: requirement.isSatisfied ? "Satisfied" : "Approval required",
      statusTone: requirement.isSatisfied ? "success" : task.status === "Blocked" ? "danger" : "warning",
      evidenceRequirementLabel: requirement.evidenceRequirement?.trim() || "Retained close sign-off evidence required",
      latestSignOffLabel: buildCloseTaskSignOffDetail(task)
    }));
  });
}

function buildCloseOperatingCoverageRows(closePlan: ClosePeriodPlan): AccountingCloseOperatingCoverageRowViewModel[] {
  return (closePlan.operatingCoverage ?? []).map((item) => ({
    controlId: item.controlId,
    label: item.label,
    statusLabel: formatAccountingReadinessState(item.state),
    statusTone: accountingReadinessStateTone(item.state),
    evidenceLabel: formatCount(item.evidenceCount, "evidence link"),
    blockerLabel: formatCount(item.blockingIssueCount, "blocking issue"),
    requiredAction: item.requiredAction,
    issueLabels: (item.blockingIssues ?? []).map((issue) =>
      `${issue.severity} | ${issue.code}${issue.targetId ? ` | ${issue.targetId}` : ""}`)
  }));
}

function buildCloseEvidenceReviewRows(
  closePlan: ClosePeriodPlan | null,
  bundle: AccountingReportPackageBundle | null,
  validationIssues: AccountingConfigurationIssueViewModel[],
  periodLocked: boolean
): AccountingCloseEvidenceReviewRowViewModel[] {
  const rows: AccountingCloseEvidenceReviewRowViewModel[] = [];
  const reviews = closePlan?.evidenceReviews ?? [];

  closePlan?.tasks.forEach((task) => {
    rows.push({
      rowId: `task-evidence-${task.taskId}`,
      issueCode: null,
      targetId: task.taskId,
      label: task.displayName,
      categoryLabel: "Checklist task",
      evidenceLabel: formatCount(task.evidenceLinks.length, "evidence link"),
      statusLabel: task.evidenceLinks.length > 0 ? "Evidence retained" : "Evidence missing",
      statusTone: task.evidenceLinks.length > 0 ? "success" : "warning",
      detailLabel: task.blockerReason?.trim() || task.signOffRequirements?.map((item) => item.evidenceRequirement).filter(Boolean).join("; ") || "Checklist evidence is inherited from the close plan.",
      latestReviewLabel: null,
      reviewDisabledReason: "Only active close blocker rows can retain review evidence."
    });
  });

  closePlan?.lateAdjustments.forEach((adjustment) => {
    rows.push({
      rowId: `late-adjustment-evidence-${adjustment.requestId}`,
      issueCode: null,
      targetId: adjustment.requestId,
      label: adjustment.journalEntryId,
      categoryLabel: "Late adjustment",
      evidenceLabel: formatCount(adjustment.evidenceLinks.length, "evidence link"),
      statusLabel: adjustment.approvalState,
      statusTone: adjustment.approvalState === "Approved"
        ? "success"
        : adjustment.approvalState === "Rejected"
          ? "danger"
          : "warning",
      detailLabel: adjustment.reason,
      latestReviewLabel: null,
      reviewDisabledReason: "Use the late-adjustment review command for adjustment decisions."
    });
  });

  if (bundle) {
    const evidenceCount = collectAccountingReportPackageEvidenceLinks(bundle).length;
    rows.push({
      rowId: `report-package-evidence-${bundle.financialStatements.packageId}`,
      issueCode: null,
      targetId: bundle.financialStatements.packageId,
      label: bundle.financialStatements.packageId,
      categoryLabel: "Report package",
      evidenceLabel: formatCount(evidenceCount, "evidence link"),
      statusLabel: formatAccountingCertificationState(bundle.certification.state),
      statusTone: accountingCertificationTone(bundle.certification.state),
      detailLabel: `${formatCount(bundle.investorCapitalStatements.length, "investor statement")}; ${formatCount(bundle.exportArtifacts?.length ?? 0, "export artifact")}`,
      latestReviewLabel: null,
      reviewDisabledReason: "Report package certification owns package review decisions."
    });
  }

  validationIssues.forEach((issue) => {
    const latestReview = reviews
      .filter((review) =>
        review.issueCode.toLowerCase() === issue.label.split(" | ")[1]?.toLowerCase() &&
        (review.targetId ?? "").toLowerCase() === (issue.detail === "No target" ? "" : issue.detail).toLowerCase())
      .sort((left, right) => String(right.reviewedAtUtc).localeCompare(String(left.reviewedAtUtc)))[0] ?? null;
    const issueCode = issue.label.split(" | ")[1] ?? issue.label;
    const targetId = issue.detail === "No target" ? null : issue.detail;
    rows.push({
      rowId: `validation-evidence-${issue.id}`,
      issueCode,
      targetId,
      label: issue.label,
      categoryLabel: "Blocker review",
      evidenceLabel: latestReview
        ? `${formatCount(latestReview.evidenceLinks.length, "review evidence link")} retained`
        : issue.detail,
      statusLabel: latestReview ? "Review retained" : "Review required",
      statusTone: latestReview ? "success" : issue.tone,
      detailLabel: issue.message,
      latestReviewLabel: latestReview
        ? `${latestReview.reviewedBy} on ${formatDateTimeLabel(latestReview.reviewedAtUtc)} | ${latestReview.notes}`
        : null,
      reviewDisabledReason: periodLocked
        ? "The period is locked; evidence review changes require a governed reopen workflow."
        : latestReview
          ? "Close evidence review is already retained for this issue."
          : null
    });
  });

  return rows;
}

function buildLateAdjustmentRow(
  adjustment: LateAdjustmentRequest,
  periodLocked: boolean
): AccountingLateAdjustmentRowViewModel {
  const materiality = adjustment.materialityPolicy;
  const absoluteAmount = Math.abs(adjustment.amount);
  const exceedsAmountThreshold = absoluteAmount > materiality.amountThreshold;
  const thresholdLabel = formatCurrencyWithCode(materiality.amountThreshold, materiality.currency);
  const materialityLabel = exceedsAmountThreshold
    ? `Material adjustment: exceeds ${thresholdLabel}; ${materiality.reviewRole} review required`
    : `Within materiality: at or below ${thresholdLabel}; ${materiality.reviewRole} review policy`;
  const reviewDisabledReason = periodLocked
    ? "The period is locked; late-adjustment review is disabled."
    : adjustment.approvalState === "Approved" || adjustment.approvalState === "Rejected"
      ? `Late adjustment is already ${adjustment.approvalState.toLowerCase()}.`
      : null;

  return {
    requestId: adjustment.requestId,
    journalEntryId: adjustment.journalEntryId,
    amountLabel: formatCurrencyWithCode(adjustment.amount, adjustment.currency, true),
    requestedByLabel: `${adjustment.requestedBy} on ${formatDateTimeLabel(adjustment.requestedAtUtc)}`,
    statusLabel: adjustment.approvalState,
    decisionLabel: adjustment.decidedBy
      ? `${adjustment.approvalState} by ${adjustment.decidedBy}${adjustment.decidedAtUtc ? ` on ${formatDateTimeLabel(adjustment.decidedAtUtc)}` : ""}`
      : null,
    evidenceLabel: formatCount(adjustment.evidenceLinks.length, "evidence link"),
    materialityLabel,
    materialityTone: exceedsAmountThreshold ? "warning" : "success",
    reason: adjustment.reason,
    reviewDisabledReason
  };
}

function buildAccountingReportPackageRow(
  bundle: AccountingReportPackageBundle,
  selectedPackageId: string | null | undefined
): AccountingReportPackageRowViewModel {
  const packageId = bundle.financialStatements.packageId;
  const certificationState = bundle.certification.state ?? bundle.financialStatements.certificationState;
  const restatement = bundle.financialStatements.restatement ?? bundle.navPackage.restatement ?? null;
  const exportArtifacts = bundle.exportArtifacts ?? [];
  const certifiedArtifactCount = exportArtifacts.filter((artifact) => artifact.certificationState === "Certified").length;
  const evidenceCount = new Set([
    ...bundle.financialStatements.evidenceLinks,
    ...bundle.investorCapitalStatements.flatMap((statement) => statement.evidenceLinks),
    ...bundle.realizedGainLoss.evidenceLinks,
    ...bundle.navPackage.evidenceLinks,
    ...bundle.certification.evidenceLinks,
    ...(restatement?.evidenceLinks ?? [])
  ]).size;

  return {
    packageId,
    periodLabel: bundle.financialStatements.periodId,
    certificationLabel: formatAccountingCertificationState(certificationState),
    certificationTone: accountingCertificationTone(certificationState),
    navLabel: formatCurrencyWithCode(bundle.navPackage.nav, bundle.navPackage.currency),
    investorStatementLabel: formatCount(bundle.investorCapitalStatements.length, "investor statement"),
    realizedGainLossLabel: formatCurrencyWithCode(
      bundle.realizedGainLoss.realizedGainLoss,
      bundle.realizedGainLoss.currency,
      true
    ),
    restatementLabel: restatement
      ? `${restatement.reasonCode} | ${restatement.approvalState}`
      : "No restatement",
    exportArtifactLabel: exportArtifacts.length > 0
      ? `${certifiedArtifactCount}/${exportArtifacts.length} exports certified`
      : "No export artifacts",
    exportArtifactTone: exportArtifacts.length === 0
      ? "default"
      : certifiedArtifactCount === exportArtifacts.length
        ? "success"
        : "warning",
    evidenceLabel: formatCount(evidenceCount, "evidence link"),
    validationLabel: formatCount(bundle.validationIssues.length, "validation issue"),
    selected: packageId === selectedPackageId
  };
}

function buildAccountingReportPackageRequest(
  workflow: OperationsContinuityWorkflow,
  closePlan: ClosePeriodPlan | null,
  seedPackage: AccountingReportPackageBundle | null
): AccountingReportPackageRequest {
  const investorStatement = seedPackage?.investorCapitalStatements[0] ?? null;

  return {
    fundProfileId: closePlan?.fundProfileId ?? workflow.fundAccountId,
    ledgerBookId: closePlan?.ledgerBookId ?? seedPackage?.financialStatements.ledgerBookId ?? null,
    periodId: closePlan?.periodId ?? workflow.periodId,
    actor: "browser-accounting-operator",
    closeWorkflowId: workflow.workflowId,
    capitalAccountId: investorStatement?.capitalAccountId ?? null,
    investorId: investorStatement?.investorId ?? null,
    beginningCapital: investorStatement?.beginningCapital ?? 0,
    contributions: investorStatement?.contributions ?? 0,
    distributions: investorStatement?.distributions ?? 0,
    realizedGainLoss: seedPackage?.realizedGainLoss.realizedGainLoss ?? investorStatement?.realizedGainLoss ?? 0,
    nav: seedPackage?.navPackage.nav ?? investorStatement?.endingCapital ?? 0,
    currency: seedPackage?.navPackage.currency ?? investorStatement?.currency ?? "USD",
    evidenceLinks: collectAccountingCloseEvidenceLinks(workflow, closePlan),
    correlationId: `browser-close-report-${workflow.workflowId}`
  };
}

function buildClosePlanConfigurationRequest(
  workflow: OperationsContinuityWorkflow,
  closePlan: ClosePeriodPlan,
  setupDraft: AccountingCloseSetupDraftViewModel
): UpsertClosePeriodPlanConfigurationRequest {
  const amountThreshold = Number(setupDraft.amountThreshold);
  const percentThreshold = Number(setupDraft.percentThreshold);
  const requiredApprovalCount = Number.parseInt(setupDraft.taskRequiredApprovalCount, 10);
  const editedTaskId = setupDraft.taskId.trim();
  const editedTaskDependsOnTaskIds = parseCloseSetupDependencyIds(setupDraft.taskDependsOnTaskIds);
  const editedTaskSignOffRequirementConfigurations = parseCloseSetupSignOffRequirementRows(setupDraft.taskSignOffRequirements);
  const editedTaskDependencyIdReasons = new Map(
    setupDraft.taskDependsOnTaskIds
      .split(/[,\r\n;]+/)
      .map((item) => parseCloseSetupDependencyEntry(item))
      .filter((entry) => entry.taskId && entry.reason)
      .map((entry) => [entry.taskId.toLowerCase(), entry.reason!])
  );
  const editedTaskDependencyReason = setupDraft.taskDependencyReason.trim();
  const editedTaskDependencyReasonOverrides = parseCloseSetupDependencyReasonOverrides(editedTaskDependencyReason);
  const editedTaskFallbackDependencyReason = editedTaskDependencyReasonOverrides.size === 0
    ? editedTaskDependencyReason
    : "";
  const taskConfigurations = closePlan.tasks.map((task) => {
    const isEditedTask = editedTaskId.length === 0 || task.taskId === editedTaskId;
    const signOffRequirements = task.signOffRequirements ?? [];
    const fallbackRequiredApprovalCount = Math.max(
      1,
      ...signOffRequirements.map((requirement) => requirement.requiredApprovalCount)
    );
    const requiredEvidence = signOffRequirements
      .map((requirement) => requirement.evidenceRequirement.trim())
      .filter(Boolean)
      .join("; ");
    const fallbackRequiredApprovalRole = signOffRequirements[0]?.role?.trim() || task.owner || "Controller";
    const fallbackSignOffRequirementConfigurations = signOffRequirements.map((requirement) => ({
      role: requirement.role,
      requiredApprovalCount: Math.max(1, requirement.requiredApprovalCount),
      evidenceRequirement: requirement.evidenceRequirement || "Retained close checklist evidence"
    }));
    const editedLegacySignOffRequirement = {
      role: setupDraft.taskRequiredApprovalRole.trim() || fallbackRequiredApprovalRole,
      requiredApprovalCount: Number.isFinite(requiredApprovalCount) && requiredApprovalCount > 0
        ? requiredApprovalCount
        : fallbackRequiredApprovalCount,
      evidenceRequirement: setupDraft.taskRequiredEvidence.trim() || requiredEvidence || "Retained close checklist evidence"
    };
    const taskSignOffRequirementConfigurations = isEditedTask
      ? editedTaskSignOffRequirementConfigurations.length > 0
        ? editedTaskSignOffRequirementConfigurations
        : [editedLegacySignOffRequirement]
      : fallbackSignOffRequirementConfigurations.length > 0
        ? fallbackSignOffRequirementConfigurations
        : [{
          role: fallbackRequiredApprovalRole,
          requiredApprovalCount: fallbackRequiredApprovalCount,
          evidenceRequirement: requiredEvidence || "Retained close checklist evidence"
        }];
    const primaryRequirement = taskSignOffRequirementConfigurations[0] ?? editedLegacySignOffRequirement;

    return {
      taskId: task.taskId,
      displayName: isEditedTask ? setupDraft.taskDisplayName.trim() || task.displayName : task.displayName,
      owner: isEditedTask ? setupDraft.taskOwner.trim() || task.owner : task.owner,
      dueDate: isEditedTask ? setupDraft.taskDueDate.trim() || task.dueDate : task.dueDate,
      requiredApprovalCount: primaryRequirement.requiredApprovalCount,
      requiredApprovalRole: primaryRequirement.role,
      requiredEvidence: primaryRequirement.evidenceRequirement,
      dependsOnTaskIds: isEditedTask
        ? editedTaskDependsOnTaskIds
        : task.dependencies.map((dependency) => dependency.dependsOnTaskId),
      dependencyConfigurations: isEditedTask
        ? editedTaskDependsOnTaskIds.map((dependsOnTaskId) => ({
          dependsOnTaskId,
          reason: resolveCloseSetupDependencyReason(
            dependsOnTaskId,
            editedTaskDependencyIdReasons,
            editedTaskDependencyReasonOverrides,
            editedTaskFallbackDependencyReason,
            task.dependencies
          )
        }))
        : task.dependencies.map((dependency) => ({
          dependsOnTaskId: dependency.dependsOnTaskId,
          reason: dependency.reason
        })),
      signOffRequirementConfigurations: taskSignOffRequirementConfigurations
    };
  });
  const evidenceLinks = collectAccountingCloseEvidenceLinks(workflow, closePlan);
  evidenceLinks.push(
    `browser://accounting/close/setup/${workflow.workflowId}`,
    `evidence://close-plan-configuration/fund/${closePlan.fundProfileId}/period/${closePlan.periodId}`
  );
  if (closePlan.ledgerBookId) {
    evidenceLinks.push(`evidence://close-plan-configuration/ledger-book/${closePlan.ledgerBookId}`);
  }

  return {
    workflowId: workflow.workflowId,
    materialityPolicy: {
      ...closePlan.materialityPolicy,
      amountThreshold: Number.isFinite(amountThreshold) && amountThreshold >= 0
        ? amountThreshold
        : closePlan.materialityPolicy.amountThreshold,
      percentThreshold: Number.isFinite(percentThreshold) && percentThreshold >= 0
        ? percentThreshold
        : closePlan.materialityPolicy.percentThreshold,
      currency: (setupDraft.currency.trim() || closePlan.materialityPolicy.currency || "USD").toUpperCase(),
      reviewRole: setupDraft.reviewRole.trim() || closePlan.materialityPolicy.reviewRole,
      requiresLateAdjustmentApproval: setupDraft.requiresLateAdjustmentApproval
    },
    taskConfigurations,
    actor: "browser-accounting-controller",
    evidenceLinks: Array.from(new Set(evidenceLinks)),
    correlationId: `browser-close-plan-configuration-${workflow.workflowId}`,
    actionOrigin: "HumanOperator",
    expectedConfiguredAtUtc: closePlan.configuration?.configuredAtUtc ?? null
  };
}

function buildClosePeriodLockRequest(
  workflow: OperationsContinuityWorkflow,
  closePlan: ClosePeriodPlan,
  selectedBundle: AccountingReportPackageBundle | null
): LockClosePeriodRequest {
  const reportPackId = selectedBundle?.financialStatements.packageId
    ?? workflow.reportPackReadiness.reportPackId
    ?? `report-pack-${closePlan.fundProfileId}-${closePlan.periodId}`;
  const evidenceLinks = collectAccountingCloseEvidenceLinks(workflow, closePlan);
  evidenceLinks.push(
    `browser://accounting/close/period-lock/${workflow.workflowId}`,
    `evidence://close-package/workflow/${workflow.workflowId}/period/${closePlan.periodId}/book/${closePlan.ledgerBookId ?? "primary"}/period-lock`,
    `evidence://report-package/${reportPackId}/workflow/${workflow.workflowId}/period/${closePlan.periodId}/book/${closePlan.ledgerBookId ?? "primary"}`
  );
  selectedBundle?.financialStatements.evidenceLinks.forEach((link) => evidenceLinks.push(link));
  selectedBundle?.certification.evidenceLinks.forEach((link) => evidenceLinks.push(link));
  selectedBundle?.exportArtifacts?.forEach((artifact) => artifact.evidenceLinks.forEach((link) => evidenceLinks.push(link)));

  return {
    workflowId: workflow.workflowId,
    expectedWorkflowVersion: workflow.version,
    actor: "browser-accounting-controller",
    rationale: `Lock close period ${closePlan.periodId} after close checklist and report package review.`,
    reportPackId,
    evidenceLinks: Array.from(new Set(evidenceLinks)),
    checklistControlApprovals: buildClosePeriodChecklistApprovals(closePlan),
    correlationId: `browser-close-period-lock-${workflow.workflowId}`,
    closePackageId: workflow.closePackage?.closePackageId ?? `close-package-${closePlan.periodId}`,
    closePackageManifestId: workflow.closePackage?.retainedManifestId ?? `close-manifest-${closePlan.periodId}`,
    closePackageRetainedManifestRoute: workflow.closePackage?.retainedManifestRoute
      ?? selectedBundle?.exportArtifacts?.[0]?.route
      ?? `/workstation/accounting/close/${closePlan.periodId}`,
    actionOrigin: "HumanOperator"
  };
}

function buildClosePeriodChecklistApprovals(closePlan: ClosePeriodPlan) {
  return closePlan.tasks.flatMap((task) =>
    task.signOffs
      .filter((signOff) => signOff.approvalState === "Approved" && signOff.actor && signOff.signedAtUtc)
      .map((signOff) => ({
        taskId: task.taskId,
        approvedBy: signOff.actor!,
        approvedAtUtc: signOff.signedAtUtc!
      }))
  );
}

function buildCertifyAccountingReportPackageRequest(
  bundle: AccountingReportPackageBundle
): CertifyAccountingReportPackageRequest {
  return {
    packageId: bundle.financialStatements.packageId,
    actor: "browser-accounting-controller",
    notes: `Certified accounting report package ${bundle.financialStatements.packageId} for ${bundle.financialStatements.periodId}.`,
    evidenceLinks: collectAccountingReportPackageEvidenceLinks(bundle),
    correlationId: `browser-certify-report-${bundle.financialStatements.packageId}`
  };
}

function collectAccountingCloseEvidenceLinks(
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

function collectAccountingReportPackageEvidenceLinks(bundle: AccountingReportPackageBundle): string[] {
  const links = new Set<string>();
  for (const link of [
    ...bundle.financialStatements.evidenceLinks,
    ...bundle.investorCapitalStatements.flatMap((statement) => statement.evidenceLinks),
    ...bundle.realizedGainLoss.evidenceLinks,
    ...bundle.navPackage.evidenceLinks,
    ...bundle.certification.evidenceLinks,
    ...(bundle.financialStatements.restatement?.evidenceLinks ?? []),
    ...(bundle.navPackage.restatement?.evidenceLinks ?? [])
  ]) {
    const trimmed = link.trim();
    if (trimmed.length > 0) {
      links.add(trimmed);
    }
  }

  links.add(`evidence:report-certification:${bundle.financialStatements.packageId}`);
  return [...links].sort((left, right) => left.localeCompare(right));
}

function formatCloseTaskStatus(status: CloseTask["status"]): string {
  const labels: Record<CloseTask["status"], string> = {
    NotStarted: "Not started",
    WaitingOnDependency: "Waiting on dependency",
    InProgress: "In progress",
    ReadyForSignOff: "Ready for sign-off",
    SignedOff: "Signed off",
    Blocked: "Blocked"
  };
  return labels[status] ?? status;
}

function closeTaskStatusTone(status: CloseTask["status"]): AccountingToolingTone {
  if (status === "SignedOff") {
    return "success";
  }

  if (status === "Blocked") {
    return "danger";
  }

  if (status === "ReadyForSignOff" || status === "WaitingOnDependency") {
    return "warning";
  }

  return "default";
}

function formatAccountingCertificationState(state: AccountingCertificationState): string {
  const labels: Record<AccountingCertificationState, string> = {
    Draft: "Draft",
    ReadyForReview: "Ready for review",
    Certified: "Certified",
    Rejected: "Rejected",
    Superseded: "Superseded"
  };
  return labels[state] ?? state;
}

function accountingCertificationTone(state: AccountingCertificationState): AccountingToolingTone {
  if (state === "Certified") {
    return "success";
  }

  if (state === "Rejected") {
    return "danger";
  }

  if (state === "ReadyForReview" || state === "Superseded") {
    return "warning";
  }

  return "default";
}

export function buildCloseCommandCenterViewState({
  data,
  commandCenter,
  commandCenterLoading,
  commandCenterError,
  workflow,
  workflowLoading,
  workflowError,
  accountingSystemProviders,
  accountingSystemImport,
  accountingSystemReconciliation,
  multiAssetCoverage
}: {
  data: AccountingWorkspaceResponse;
  commandCenter?: FinancialOperationsCommandCenter | null;
  commandCenterLoading?: boolean;
  commandCenterError?: string | null;
  workflow: OperationsContinuityWorkflow | null;
  workflowLoading: boolean;
  workflowError: string | null;
  accountingSystemProviders: AccountingSystemProvider[];
  accountingSystemImport: AccountingSystemImportDetail | null;
  accountingSystemReconciliation: AccountingSystemReconciliationSummary | null;
  multiAssetCoverage: MultiAssetCoverageSummary | null | undefined;
}): CloseCommandCenterViewState {
  if (commandCenter) {
    return buildSharedFinancialOperationsCommandCenterViewState(commandCenter, commandCenterLoading ?? workflowLoading, commandCenterError ?? workflowError);
  }

  const openBreakCount = data.breakQueue.filter((item) => isOpenAccountingBreakStatus(item.status)).length;
  const workflowOpenBreakCount = workflow?.breakCases.filter((item) => isOpenAccountingBreakStatus(item.status)).length ?? 0;
  const closeBlockers = workflow ? collectCloseCommandCenterBlockers(workflow) : [];
  const incompleteEvidenceCategories = workflow?.accountingRecordSummary?.evidenceCategories.filter((category) => !category.isComplete) ?? [];
  const missingSourceCount = incompleteEvidenceCategories.filter((category) => closeCommandCenterTextMatches(category.key, category.label, "source")).length
    + (workflow?.closeChecklist.filter((task) => !isCloseChecklistDone(task.status) && !task.evidencePointer).length ?? 0);
  const missingEvidenceCount = incompleteEvidenceCategories.length
    + (workflow?.closeReadiness?.blockers.filter((blocker) => blocker.category.toLowerCase().includes("evidence")).length ?? 0);
  const pendingApprovalCount = workflow?.approvals.filter((approval) => approval.status !== "Approved").length ?? 0;
  const unapprovedChecklistCount = workflow?.closeChecklist.filter((task) => !isCloseChecklistDone(task.status) && task.requiredApprovalCount > 0).length ?? 0;
  const unapprovedAdjustmentCount = pendingApprovalCount + unapprovedChecklistCount;
  const staleValuationCount = countCloseCommandCenterValuationIssues(multiAssetCoverage);
  const providerWarningCount = countCloseCommandCenterProviderWarnings(accountingSystemProviders, accountingSystemImport, accountingSystemReconciliation);
  const reportPackReady = workflow?.reportPackReadiness.isReady ?? false;
  const reportPackLabel = workflow
    ? reportPackReady
      ? workflow.reportPackReadiness.reportPackId ?? "Ready"
      : "Not ready"
    : "Detail pending";
  const signOffStatus = resolveCloseCommandCenterSignOffStatus(workflow);
  const hasCriticalBlocker = closeBlockers.some((blocker) => closeCommandCenterSeverityTone(blocker.severity) === "danger");
  const hasFailedRequiredGate = workflow?.gates.some((gate) => gate.isRequired && gate.status === "Blocked") ?? false;
  const isReadyToClose = workflow?.closeReadiness?.isReadyToClose === true || workflow?.status === "ReadyForClose" || workflow?.status === "Closed";
  const status: CloseCommandCenterStatus = workflowLoading && !workflow
    ? "loading"
    : hasCriticalBlocker || hasFailedRequiredGate || workflow?.status === "Blocked"
      ? "blocked"
      : isReadyToClose
        && openBreakCount === 0
        && workflowOpenBreakCount === 0
        && missingEvidenceCount === 0
        && unapprovedAdjustmentCount === 0
        && staleValuationCount === 0
        && providerWarningCount === 0
        && reportPackReady
        && signOffStatus.tone === "success"
          ? "ready"
          : "at-risk";
  const statusTone = closeCommandCenterStatusTone(status);
  const readinessLabel = workflow?.closeReadiness?.severity
    ?? workflow?.status
    ?? data.controlCenter?.closeReadiness
    ?? "Close detail pending";
  const updatedLabel = workflow?.updatedAtUtc ?? accountingSystemReconciliation?.generatedAtUtc ?? multiAssetCoverage?.asOfUtc ?? "Not refreshed";

  const metricRows: CloseCommandCenterMetricViewModel[] = [
    {
      id: "period",
      label: "Period status",
      value: readinessLabel,
      detail: workflow ? `${workflow.status} workflow ${workflow.workflowId}` : "Using workspace close status until workflow detail loads.",
      tone: statusTone,
      href: workflow ? WORKSTATION_ROUTE_CATALOG.accountingApprovals : null
    },
    {
      id: "breaks",
      label: "Open breaks",
      value: String(openBreakCount + workflowOpenBreakCount),
      detail: `${formatCount(openBreakCount, "queue break")} and ${formatCount(workflowOpenBreakCount, "workflow break")} remain open.`,
      tone: openBreakCount + workflowOpenBreakCount > 0 ? "warning" : "success",
      href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation
    },
    {
      id: "source-files",
      label: "Missing source files",
      value: String(missingSourceCount),
      detail: missingSourceCount > 0 ? "Required source evidence or checklist evidence pointers are incomplete." : "Required source evidence is retained.",
      tone: missingSourceCount > 0 ? "warning" : "success",
      href: WORKSTATION_ROUTE_CATALOG.reportingEvidence
    },
    {
      id: "adjustments",
      label: "Unapproved adjustments",
      value: String(unapprovedAdjustmentCount),
      detail: unapprovedAdjustmentCount > 0 ? "Pending approvals or checklist controls remain before sign-off." : "No pending approval controls are surfaced.",
      tone: unapprovedAdjustmentCount > 0 ? "warning" : "success",
      href: WORKSTATION_ROUTE_CATALOG.accountingApprovals
    },
    {
      id: "valuations",
      label: "Stale valuations",
      value: String(staleValuationCount),
      detail: staleValuationCount > 0 ? "Asset readiness signals include valuation or stale-data review items." : "No stale valuation blockers are surfaced.",
      tone: staleValuationCount > 0 ? "warning" : "success",
      href: multiAssetCoverage?.drillThroughRoutes.coverage ?? null
    },
    {
      id: "providers",
      label: "Provider warnings",
      value: String(providerWarningCount),
      detail: providerWarningCount > 0 ? "Provider, external GL, or import reconciliation warnings need review." : "Provider and external GL evidence is clean.",
      tone: providerWarningCount > 0 ? "warning" : "success",
      href: WORKSTATION_ROUTE_CATALOG.accountingLedger
    },
    {
      id: "report-pack",
      label: "Report-pack readiness",
      value: reportPackLabel,
      detail: workflow?.reportPackReadiness.blockingReason ?? "Report-pack readiness comes from the selected close workflow.",
      tone: reportPackReady ? "success" : workflow ? "warning" : "default",
      href: WORKSTATION_ROUTE_CATALOG.reportingEvidence
    },
    {
      id: "signoff",
      label: "Sign-off status",
      value: signOffStatus.label,
      detail: signOffStatus.detail,
      tone: signOffStatus.tone,
      href: WORKSTATION_ROUTE_CATALOG.accountingApprovals
    }
  ];

  const blockerRows = [
    ...closeBlockers.map((blocker) => ({
      id: blocker.code,
      label: blocker.category ? `${blocker.category} - ${blocker.code}` : blocker.code,
      detail: blocker.message,
      tone: closeCommandCenterSeverityTone(blocker.severity),
      href: blocker.routeHint ?? closeCommandCenterGateRoute(blocker.gate),
      statusLabel: blocker.severity,
      ownerLabel: null,
      dueLabel: null,
      evidenceLabel: "Evidence pending",
      actionLabel: "Resolve blocker before sign-off.",
      impactLabel: blocker.category
    })),
    ...incompleteEvidenceCategories.slice(0, 3).map((category) => ({
      id: `evidence-${category.key}`,
      label: category.label,
      detail: category.requiredEvidence?.length
        ? `Missing: ${category.requiredEvidence.join(", ")}`
        : category.status,
      tone: "warning" as AccountingToolingTone,
      href: category.routeHint,
      statusLabel: category.status,
      ownerLabel: null,
      dueLabel: null,
      evidenceLabel: category.requiredEvidence?.length ? formatCount(category.requiredEvidence.length, "required item") : "Evidence pending",
      actionLabel: "Attach retained evidence before sign-off.",
      impactLabel: "Retained evidence"
    })),
    ...((data.controlCenter?.alerts ?? []).slice(0, 2).map((alert, index) => ({
      id: `bootstrap-alert-${index}`,
      label: "Control-center alert",
      detail: alert.message,
      tone: alert.tone === "danger" ? "danger" as AccountingToolingTone : "warning" as AccountingToolingTone,
      href: null,
      statusLabel: alert.tone === "danger" ? "Blocked" : "Review",
      ownerLabel: null,
      dueLabel: null,
      evidenceLabel: "Control-center alert",
      actionLabel: "Review alert before close release.",
      impactLabel: "Control center"
    })))
  ].slice(0, 8);
  return {
    title: "CFO / Controller close command center",
    description: "Controller-facing period readiness, close blockers, evidence gaps, provider warnings, report-pack readiness, and sign-off status from shared Accounting read models.",
    ariaLabel: "CFO and controller close command center",
    status,
    statusLabel: status === "ready" ? "Ready" : status === "blocked" ? "Blocked" : status === "loading" ? "Loading" : "At risk",
    statusTone,
    periodLabel: workflow?.periodId ?? "Current period",
    fundAccountLabel: workflow?.fundAccountId ?? multiAssetCoverage?.fundAccountId ?? "All accounts",
    summary: buildCloseCommandCenterSummary(status, metricRows, workflowError),
    updatedLabel,
    updatedAtUtc: workflow?.updatedAtUtc ?? accountingSystemReconciliation?.generatedAtUtc ?? multiAssetCoverage?.asOfUtc ?? null,
    metricRows,
    blockerRows,
    actionRows: [
      {
        id: "reconciliation",
        label: "Review breaks",
        href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
        ariaLabel: "Open Accounting reconciliation breaks from close command center",
        tone: openBreakCount + workflowOpenBreakCount > 0 ? "warning" : "success"
      },
      {
        id: "approvals",
        label: "Open approvals",
        href: WORKSTATION_ROUTE_CATALOG.accountingApprovals,
        ariaLabel: "Open Accounting approvals from close command center",
        tone: unapprovedAdjustmentCount > 0 || signOffStatus.tone !== "success" ? "warning" : "success"
      },
      {
        id: "reporting",
        label: "Report evidence",
        href: WORKSTATION_ROUTE_CATALOG.reportingEvidence,
        ariaLabel: "Open Reporting evidence from close command center",
        tone: reportPackReady ? "success" : "warning"
      }
    ],
    loadingText: workflowLoading ? "Refreshing close workflow detail." : null,
    errorText: workflowError,
    liveRegionText: `Close command center ${status}. ${readinessLabel}. ${formatCount(blockerRows.length, "visible blocker")}.`
  };
}

function buildSharedFinancialOperationsCommandCenterViewState(
  commandCenter: FinancialOperationsCommandCenter,
  loading: boolean,
  errorText: string | null
): CloseCommandCenterViewState {
  const status = mapCommandCenterStatus(commandCenter.status, loading);
  const statusTone = closeCommandCenterStatusTone(status);
  const closeSupportDecision = commandCenter.closeSupportDecision ?? null;
  const closeSupportMetrics: CloseCommandCenterMetricViewModel[] = closeSupportDecision
    ? [
      {
        id: "close-support-period-state",
        label: "Period state",
        value: closeSupportDecision.status,
        detail: closeSupportDecision.periodState,
        tone: commandCenterMetricTone(closeSupportDecision.status),
        href: WORKSTATION_ROUTE_CATALOG.accountingApprovals
      },
      {
        id: "close-support-exceptions",
        label: "Unresolved exceptions",
        value: String(closeSupportDecision.unresolvedExceptionCount),
        detail: closeSupportDecision.lockReopenPosture,
        tone: closeSupportDecision.unresolvedExceptionCount > 0 ? "warning" : "success",
        href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation
      },
      {
        id: "close-support-approvals",
        label: "Pending approvals",
        value: String(closeSupportDecision.pendingApprovalCount),
        detail: closeSupportDecision.pendingApprovalCount > 0
          ? "Shared close-support approvals must clear before completion."
          : "No pending shared close-support approvals are surfaced.",
        tone: closeSupportDecision.pendingApprovalCount > 0 ? "warning" : "success",
        href: WORKSTATION_ROUTE_CATALOG.accountingApprovals
      },
      {
        id: "close-support-evidence",
        label: "Retained evidence gaps",
        value: String(closeSupportDecision.retainedEvidenceGapCount),
        detail: closeSupportDecision.navReportDependencyPosture,
        tone: closeSupportDecision.retainedEvidenceGapCount > 0 ? "warning" : "success",
        href: WORKSTATION_ROUTE_CATALOG.reportingEvidence
      }
    ]
    : [];
  const metricRows: CloseCommandCenterMetricViewModel[] = [
    ...commandCenter.metrics.map((metric) => ({
      id: metric.metricId,
      label: metric.label,
      value: metric.value,
      detail: metric.detail,
      tone: commandCenterMetricTone(metric.status),
      href: localCommandCenterRoute(metric.routeHint, metric.metricId)
    })),
    ...closeSupportMetrics
  ];
  const decisionBlockerRows = (closeSupportDecision?.decisions ?? [])
    .filter((decision) => decision.isBlocking)
    .map((decision) => ({
      id: decision.decisionId,
      label: `${decision.category} - ${decision.label}`,
      detail: `${decision.detail} ${decision.requiredAction}`.trim(),
      tone: "danger" as AccountingToolingTone,
      href: localCommandCenterRoute(decision.routeHint, decision.category),
      statusLabel: decision.status,
      ownerLabel: null,
      dueLabel: null,
      evidenceLabel: formatCount(decision.evidenceLinks.length, "evidence link"),
      actionLabel: decision.requiredAction,
      impactLabel: decision.category
    }));
  const queueBlockerRows = commandCenter.queueRows.map((row) => {
    const metadata = [
      row.severityLabel ? `Severity: ${row.severityLabel}.` : null,
      row.slaLabel ? `SLA: ${row.slaLabel}.` : null,
      row.blockerType ? `Blocker: ${row.blockerType}.` : null,
      row.closeReportImpact ? `Impact: ${row.closeReportImpact}.` : null
    ].filter(Boolean);

    return {
      id: row.queueId,
      label: `${row.kindLabel} - ${row.title}`,
      detail: [row.detail, row.actionLabel, ...metadata].join(" ").trim(),
      tone: row.isBlocked ? "danger" as AccountingToolingTone : "warning" as AccountingToolingTone,
      href: localCommandCenterRoute(row.routeHint, row.sourceKind),
      statusLabel: row.statusLabel,
      ownerLabel: row.ownerLabel,
      dueLabel: row.slaLabel ?? row.dueLabel,
      evidenceLabel: row.evidenceLabel,
      actionLabel: row.actionLabel,
      impactLabel: row.closeReportImpact ?? row.blockerType ?? row.kindLabel
    };
  });
  const blockerRows = [...decisionBlockerRows, ...queueBlockerRows].slice(0, 10);
  const routedRows = commandCenter.queueRows
    .filter((row) => localCommandCenterRoute(row.routeHint, row.sourceKind))
    .slice(0, 3);
  const routedDecisionRows = (closeSupportDecision?.decisions ?? [])
    .filter((decision) => localCommandCenterRoute(decision.routeHint, decision.category))
    .slice(0, 3);
  const actionRows: CloseCommandCenterActionViewModel[] = routedRows.length > 0
    ? routedRows.map((row) => ({
      id: row.queueId,
      label: row.actionLabel || row.title,
      href: localCommandCenterRoute(row.routeHint, row.sourceKind) ?? WORKSTATION_ROUTE_CATALOG.accountingApprovals,
      ariaLabel: `Open ${row.title} from close command center`,
      tone: row.isBlocked ? "warning" : "success"
    }))
    : routedDecisionRows.length > 0
      ? routedDecisionRows.map((decision) => ({
        id: decision.decisionId,
        label: decision.requiredAction || decision.label,
        href: localCommandCenterRoute(decision.routeHint, decision.category) ?? WORKSTATION_ROUTE_CATALOG.accountingApprovals,
        ariaLabel: `Open ${decision.label} close-support decision from close command center`,
        tone: decision.isBlocking ? "warning" : "success"
      }))
    : [
      {
        id: "financial-operations",
        label: commandCenter.isReadyToComplete ? "Review close evidence" : "Open operations",
        href: WORKSTATION_ROUTE_CATALOG.accountingApprovals,
        ariaLabel: "Open Accounting approvals from close command center",
        tone: commandCenter.isReadyToComplete ? "success" : "warning"
      }
    ];

  return {
    title: "CFO / Controller close command center",
    description: "Controller-facing period readiness, close blockers, evidence gaps, report-pack readiness, and sign-off status from Meridian Financial Operations review data.",
    ariaLabel: "CFO and controller close command center",
    status,
    statusLabel: status === "ready" ? "Ready" : status === "blocked" ? "Blocked" : status === "loading" ? "Loading" : "At risk",
    statusTone,
    periodLabel: commandCenter.periodId ?? "Current period",
    fundAccountLabel: commandCenter.fundAccountId ?? commandCenter.fundProfileId ?? "All accounts",
    summary: errorText ? `${closeSupportDecision?.summary ?? commandCenter.summary} ${errorText}` : closeSupportDecision?.summary ?? commandCenter.summary,
    updatedLabel: commandCenter.generatedAtUtc,
    updatedAtUtc: commandCenter.generatedAtUtc ?? null,
    metricRows,
    blockerRows,
    actionRows,
    loadingText: loading ? "Refreshing Financial Operations command center." : null,
    errorText,
    liveRegionText: `Close command center ${status}. ${closeSupportDecision?.summary ?? commandCenter.summary} ${formatCount(commandCenter.activeItemCount, "active item")}.`
  };
}

function mapCommandCenterStatus(status: string, loading: boolean): CloseCommandCenterStatus {
  if (loading) {
    return "loading";
  }

  const normalized = status.trim().toLowerCase();
  if (normalized === "ready") {
    return "ready";
  }

  if (normalized === "blocked") {
    return "blocked";
  }

  return "at-risk";
}

function commandCenterMetricTone(status: string): AccountingToolingTone {
  const normalized = status.trim().toLowerCase();
  if (normalized === "ready") {
    return "success";
  }

  if (normalized === "blocked" || normalized === "missing") {
    return "danger";
  }

  if (normalized === "review" || normalized === "reviewrequired" || normalized === "atrisk") {
    return "warning";
  }

  return "default";
}

function localCommandCenterRoute(route: string | null | undefined, sourceKind: string): string | null {
  return normalizeLocalWorkstationRoute(route)
    ?? commandCenterFallbackRoute(sourceKind);
}

function commandCenterFallbackRoute(sourceKind: string): string | null {
  const normalized = sourceKind.trim().toLowerCase();
  if (normalized.includes("break") || normalized.includes("reconciliation")) {
    return WORKSTATION_ROUTE_CATALOG.accountingReconciliation;
  }

  if (normalized.includes("approval") || normalized.includes("checklist") || normalized.includes("calendar") || normalized.includes("lock") || normalized.includes("reopen")) {
    return WORKSTATION_ROUTE_CATALOG.accountingApprovals;
  }

  if (normalized.includes("nav") || normalized.includes("private-capital")) {
    return WORKSTATION_ROUTE_CATALOG.accountingCapitalAccounts;
  }

  if (normalized.includes("evidence") || normalized.includes("report")) {
    return WORKSTATION_ROUTE_CATALOG.reportingEvidence;
  }

  return null;
}

function collectCloseCommandCenterBlockers(workflow: OperationsContinuityWorkflow): CloseCommandCenterRawBlocker[] {
  const workflowBlockers = workflow.blockers.map((blocker) => ({
    code: blocker.code,
    category: blocker.gate ?? "Workflow",
    severity: blocker.severity,
    message: blocker.message,
    gate: blocker.gate,
    routeHint: null
  }));
  const gateBlockers = workflow.gates.flatMap((gate) => gate.blockers.map((blocker) => ({
    code: blocker.code,
    category: gate.displayName,
    severity: blocker.severity,
    message: blocker.message,
    gate: blocker.gate,
    routeHint: closeCommandCenterGateRoute(gate.gateKey)
  })));
  const readinessBlockers = workflow.closeReadiness?.blockers.map((blocker) => ({
    code: blocker.code,
    category: blocker.category,
    severity: blocker.severity,
    message: blocker.message,
    gate: blocker.gate,
    routeHint: blocker.routeHint
  })) ?? [];

  const byKey = new Map<string, CloseCommandCenterRawBlocker>();
  for (const blocker of [...workflowBlockers, ...gateBlockers, ...readinessBlockers]) {
    byKey.set(`${blocker.code}:${blocker.message}`, blocker);
  }

  return [...byKey.values()];
}

function isCloseChecklistDone(status: string): boolean {
  const normalized = status.trim().toLowerCase();
  return normalized === "done" || normalized === "complete" || normalized === "completed" || normalized === "acknowledged";
}

function closeCommandCenterTextMatches(left: string | null | undefined, right: string | null | undefined, needle: string): boolean {
  const normalizedNeedle = needle.toLowerCase();
  return [left, right]
    .map((value) => value?.toLowerCase() ?? "")
    .some((value) => value.includes(normalizedNeedle));
}

function countCloseCommandCenterValuationIssues(coverage: MultiAssetCoverageSummary | null | undefined): number {
  if (!coverage) {
    return 0;
  }

  return coverage.assetClasses.filter((assetClass) => {
    const blockerMatch = assetClass.blockers.some((blocker) => (
      closeCommandCenterTextMatches(blocker.code, blocker.message, "valuation")
      || closeCommandCenterTextMatches(blocker.source, blocker.message, "stale")
    ));
    const requirementMatch = assetClass.evidenceRequirements.some((requirement) => (
      requirement.status !== "Ready"
      && (closeCommandCenterTextMatches(requirement.label, requirement.category, "valuation")
        || closeCommandCenterTextMatches(requirement.label, requirement.category, "stale"))
    ));

    return blockerMatch || requirementMatch;
  }).length;
}

function countCloseCommandCenterProviderWarnings(
  providers: AccountingSystemProvider[],
  importDetail: AccountingSystemImportDetail | null,
  reconciliation: AccountingSystemReconciliationSummary | null
): number {
  const unavailableProviders = providers.filter((provider) => provider.state !== "Available").length;
  const importWarnings = importDetail?.summary.warnings.length ?? 0;
  const reconciliationRows = reconciliation?.rows.filter((row) => row.status !== "Matched").length ?? 0;

  return unavailableProviders + importWarnings + reconciliationRows;
}

function resolveCloseCommandCenterSignOffStatus(workflow: OperationsContinuityWorkflow | null): {
  label: string;
  detail: string;
  tone: AccountingToolingTone;
} {
  if (!workflow) {
    return {
      label: "Detail pending",
      detail: "Load the close workflow before sign-off status can be confirmed.",
      tone: "default"
    };
  }

  if (workflow.closePackage) {
    return {
      label: `Signed by ${workflow.closePackage.publishedBy}`,
      detail: workflow.closePackage.signOffRationale,
      tone: "success"
    };
  }

  const approvedCount = workflow.approvals.filter((approval) => approval.status === "Approved").length;
  const totalApprovalCount = workflow.approvals.length;
  if (totalApprovalCount > 0) {
    return {
      label: `${approvedCount}/${totalApprovalCount} approved`,
      detail: approvedCount === totalApprovalCount
        ? "Approval rows are complete; publish the close package when report evidence is ready."
        : "Approval rows still need reviewer decisions before close sign-off.",
      tone: approvedCount === totalApprovalCount ? "success" : "warning"
    };
  }

  return {
    label: workflow.approvalState,
    detail: "No approval rows are attached to the selected close workflow.",
    tone: workflow.approvalState === "Approved" ? "success" : "warning"
  };
}

function closeCommandCenterSeverityTone(severity: string): AccountingToolingTone {
  const normalized = severity.trim().toLowerCase();
  if (normalized === "critical" || normalized === "blocker" || normalized === "danger") {
    return "danger";
  }

  if (normalized === "warning" || normalized === "warn" || normalized === "review") {
    return "warning";
  }

  return "default";
}

function closeCommandCenterStatusTone(status: CloseCommandCenterStatus): AccountingToolingTone {
  if (status === "ready") return "success";
  if (status === "blocked") return "danger";
  if (status === "at-risk") return "warning";
  return "default";
}

function closeCommandCenterGateRoute(gate: OperationsWorkflowBlocker["gate"]): string | null {
  if (gate === "Reconciliation") return WORKSTATION_ROUTE_CATALOG.accountingReconciliation;
  if (gate === "Approval") return WORKSTATION_ROUTE_CATALOG.accountingApprovals;
  if (gate === "LedgerPosting") return WORKSTATION_ROUTE_CATALOG.accountingLedger;
  if (gate === "SecurityMaster") return WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster;
  if (gate === "BrokerIngest") return WORKSTATION_ROUTE_CATALOG.accountingLedger;
  return null;
}

function buildCloseCommandCenterSummary(
  status: CloseCommandCenterStatus,
  metricRows: CloseCommandCenterMetricViewModel[],
  workflowError: string | null
): string {
  if (workflowError) {
    return `Close workflow detail could not be refreshed: ${workflowError}`;
  }

  const activeRows = metricRows.filter((row) => row.tone === "warning" || row.tone === "danger");
  if (status === "ready") {
    return "The close is ready: breaks, source evidence, approvals, valuations, providers, report pack, and sign-off are clear.";
  }

  if (status === "blocked") {
    return `The close is blocked by ${formatCount(activeRows.length, "active control")}; review the blocker list before sign-off.`;
  }

  if (status === "loading") {
    return "Refreshing workflow detail before confirming the close state.";
  }

  return `The close is at risk with ${formatCount(activeRows.length, "watch item")} across the command center.`;
}

function isOpenAccountingBreakStatus(status: string): boolean {
  const normalized = status.trim().toLowerCase();
  return normalized !== "resolved" && normalized !== "dismissed" && normalized !== "closed";
}
