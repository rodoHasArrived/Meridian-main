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
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
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
import { isOpenAccountingBreakStatus } from "./accounting-screen.close-cockpit-presenters";
import {
  buildClosePlanTaskRow,
  buildCloseTaskSignOffDetail,
  buildCloseSetupDependencyOptions,
  buildCloseSetupSignOffRoleOptions,
  buildCloseSetupSingleSignOffRequirementRow,
  buildCloseSetupTaskOptions,
  buildCloseSignOffDecisionOptions,
  buildCloseSignOffRoleOptions,
  buildCloseSignOffTaskOptions,
  closeTaskStatusTone,
  createAccountingCloseSetupDraft,
  createAccountingCloseSignOffDraft,
  createAccountingLateAdjustmentDraft,
  formatCloseTaskStatus,
  parseCloseSetupDependencyEntry,
  parseCloseSetupDependencyIds,
  parseCloseSetupDependencyReasonOverrides,
  parseCloseSetupSignOffRequirementRows,
  resolveCloseSetupDependencyReason,
  resolveCloseTaskSignOffDraftTarget,
  validateCloseSetupMaterialityDraft,
  validateCloseSetupSignOffDraft,
  validateCloseSetupTaskSelection,
  validateCloseSignOffDraft,
} from "./accounting-screen.close-cockpit-drafts";
import type {
  AccountingCloseCalendarMilestoneViewModel,
  AccountingCloseDependencyGraphRowViewModel,
  AccountingCloseEvidenceReviewRowViewModel,
  AccountingCloseOperatingCoverageRowViewModel,
  AccountingClosePostingGateViewModel,
  AccountingClosePlanTaskRowViewModel,
  AccountingCloseReportPackageServices,
  AccountingCloseReportPackageViewModel,
  AccountingCloseSetupDraftViewModel,
  AccountingCloseSignOffDecision,
  AccountingCloseSignOffDraftViewModel,
  AccountingCloseSignOffMatrixRowViewModel,
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
  CloseCommandCenterViewState,
} from "./accounting-screen.view-model";
import type {
  AccountingReportPackageBundle,
  AccountingReportPackageRequest,
  AccountingWorkspaceResponse,
  CertifyAccountingReportPackageRequest,
  CloseCalendarMilestone,
  ClosePeriodPlan,
  ClosePostingGateState,
  LateAdjustmentRequest,
  LedgerDimensionSet,
  LockClosePeriodRequest,
  OperationsContinuityWorkflow,
  ReportExportArtifact,
  ReportExportArtifactManifest,
  UpsertClosePeriodPlanConfigurationRequest,
  AccountingCertificationState,
} from "@/types";

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
  const [lockClosePeriodArmed, setLockClosePeriodArmed] = useState(false);
  const [queueClosingEntriesBusy, setQueueClosingEntriesBusy] = useState(false);
  const [queueClosingEntriesStatusText, setQueueClosingEntriesStatusText] = useState<string | null>(null);
  const [queueClosingEntriesStatusTone, setQueueClosingEntriesStatusTone] = useState<"neutral" | "success" | "danger">("neutral");
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

  useEffect(() => {
    setLockClosePeriodArmed(false);
  }, [closePlan?.closePlanId, closePlan?.isPeriodLocked, workflow?.workflowId]);

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

  const queueClosingEntries = useCallback(async () => {
    if (!workflow || !closePlan) {
      setQueueClosingEntriesStatusText("A close plan is required before queueing closing entries.");
      setQueueClosingEntriesStatusTone("danger");
      return;
    }

    if (closePlan.isPeriodLocked) {
      setQueueClosingEntriesStatusText("The period is already locked; closing entries cannot be queued.");
      setQueueClosingEntriesStatusTone("danger");
      return;
    }

    if (closePlan.closingEntriesGate?.state !== "Required") {
      const stateLabel = closePlan.closingEntriesGate
        ? formatClosePostingGateState(closePlan.closingEntriesGate.state).label
        : "Not supplied";
      setQueueClosingEntriesStatusText(`Closing entries can only be queued from Required state; current state is ${stateLabel}.`);
      setQueueClosingEntriesStatusTone("danger");
      return;
    }

    const selectedBundle = packages.find((bundle) => bundle.financialStatements.packageId === selectedPackageId) ?? packages[0] ?? null;
    setQueueClosingEntriesBusy(true);
    setQueueClosingEntriesStatusText(null);
    setQueueClosingEntriesStatusTone("neutral");
    try {
      const result = await services.lockClosePeriod(
        buildClosePeriodLockRequest(workflow, closePlan, selectedBundle, true)
      );
      if (result.plan) {
        setClosePlan(result.plan);
      }

      const blockingIssueCount = result.issues.filter((issue) => issue.severity === "Critical").length;
      const preparedState = result.plan?.closingEntriesGate?.state;
      if (blockingIssueCount > 0) {
        setQueueClosingEntriesStatusText(`Closing-entry preparation blocked by ${formatCount(blockingIssueCount, "critical issue")}.`);
        setQueueClosingEntriesStatusTone("danger");
      } else if (preparedState && preparedState !== "Required" && preparedState !== "Blocked" && preparedState !== "Unavailable") {
        setQueueClosingEntriesStatusText(
          `Queued closing entries for ${result.plan?.periodId ?? closePlan.periodId}; state is ${formatClosePostingGateState(preparedState).label}.`
        );
        setQueueClosingEntriesStatusTone("success");
      } else if (result.issues.length > 0) {
        setQueueClosingEntriesStatusText(result.issues.map((issue) => issue.message).join(" "));
        setQueueClosingEntriesStatusTone("neutral");
      } else {
        setQueueClosingEntriesStatusText("Closing-entry preparation did not advance beyond Required state.");
        setQueueClosingEntriesStatusTone("danger");
      }

      await refresh();
    } catch (error) {
      setQueueClosingEntriesStatusText(formatAccountingWorkflowError(error, "Closing entries could not be queued."));
      setQueueClosingEntriesStatusTone("danger");
    } finally {
      setQueueClosingEntriesBusy(false);
    }
  }, [closePlan, packages, refresh, selectedPackageId, services, workflow]);

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

    if (!isClosePostingGateReadyForHardLock(closePlan.closingEntriesGate ?? null)) {
      const stateLabel = closePlan.closingEntriesGate
        ? formatClosePostingGateState(closePlan.closingEntriesGate.state).label
        : "Not supplied";
      setLockClosePeriodStatusText(`Closing entries are not ready for period lock; current state is ${stateLabel}.`);
      setLockClosePeriodStatusTone("danger");
      return;
    }

    if (!lockClosePeriodArmed) {
      setLockClosePeriodArmed(true);
      setLockClosePeriodStatusText(`Locking close period ${closePlan.periodId} blocks further posting until a governed reopen. Select Confirm lock period to proceed.`);
      setLockClosePeriodStatusTone("neutral");
      return;
    }

    setLockClosePeriodArmed(false);
    const selectedBundle = packages.find((bundle) => bundle.financialStatements.packageId === selectedPackageId) ?? packages[0] ?? null;
    setLockClosePeriodBusy(true);
    setLockClosePeriodStatusText(null);
    setLockClosePeriodStatusTone("neutral");
    try {
      const result = await services.lockClosePeriod(
        buildClosePeriodLockRequest(workflow, closePlan, selectedBundle, false)
      );
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
  }, [closePlan, lockClosePeriodArmed, packages, selectedPackageId, services, workflow]);

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
      lockClosePeriodArmed,
      queueClosingEntriesBusy,
      queueClosingEntriesStatusText,
      queueClosingEntriesStatusTone,
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
      queueClosingEntries,
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
      lockClosePeriodArmed,
      lockClosePeriodBusy,
      lockClosePeriodStatusText,
      lockClosePeriodStatusTone,
      queueClosingEntries,
      queueClosingEntriesBusy,
      queueClosingEntriesStatusText,
      queueClosingEntriesStatusTone,
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
  lockClosePeriodArmed,
  queueClosingEntriesBusy,
  queueClosingEntriesStatusText,
  queueClosingEntriesStatusTone,
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
  queueClosingEntries,
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
  lockClosePeriodArmed: boolean;
  queueClosingEntriesBusy: boolean;
  queueClosingEntriesStatusText: string | null;
  queueClosingEntriesStatusTone: "neutral" | "success" | "danger";
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
  queueClosingEntries: () => Promise<void>;
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
  const closingEntriesGate = closePlan ? buildClosePostingGateViewModel(closePlan) : null;
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
    : loading || buildBusy || certifyBusy || signOffBusy || lockClosePeriodBusy || queueClosingEntriesBusy
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
          : loading || buildBusy || certifyBusy || signOffBusy || lockClosePeriodBusy || queueClosingEntriesBusy
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
          : loading || buildBusy || certifyBusy || signOffBusy || lockClosePeriodBusy || queueClosingEntriesBusy
            ? "Close/report package action is running."
            : null;
  const queueClosingEntriesDisabledReason = !workflow
    ? "A close workflow must be loaded before queueing closing entries."
    : !closePlan
      ? "A close plan must be loaded before queueing closing entries."
      : locked
        ? "The close period is already locked."
        : closePlan.closingEntriesGate?.state !== "Required"
          ? closePlan.closingEntriesGate
            ? `Closing entries can only be queued from Required state; current state is ${formatClosePostingGateState(closePlan.closingEntriesGate.state).label}.`
            : "The close plan must supply the typed closing-entry gate before entries can be queued."
          : loading || buildBusy || certifyBusy || signOffBusy || lockClosePeriodBusy || queueClosingEntriesBusy
            ? "Close/report package action is running."
            : null;
  const closingEntriesLockDisabledReason = closePlan && !isClosePostingGateReadyForHardLock(closePlan.closingEntriesGate ?? null)
    ? closePlan.closingEntriesGate
      ? closePlan.closingEntriesGate.state === "Required"
        ? "Queue and post closing entries before locking the period."
        : `Closing entries must be Posted or Not required before period lock; current state is ${formatClosePostingGateState(closePlan.closingEntriesGate.state).label}.`
      : "The close plan must supply the typed closing-entry gate before period lock."
    : null;
  const lockClosePeriodDisabledReason = !workflow
    ? "A close workflow must be loaded before locking the period."
    : !closePlan
      ? "A close plan must be loaded before locking the period."
      : locked
        ? "The close period is already locked."
        : closingEntriesLockDisabledReason
          ? closingEntriesLockDisabledReason
          : !selectedBundle
            ? "A report package must be built before locking the period."
            : loading || buildBusy || certifyBusy || signOffBusy || lockClosePeriodBusy || queueClosingEntriesBusy
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
          ?? (loading || buildBusy || certifyBusy || signOffBusy || lockClosePeriodBusy || queueClosingEntriesBusy || configureClosePlanBusy
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
              : loading || buildBusy || certifyBusy || signOffBusy || lockClosePeriodBusy || queueClosingEntriesBusy || createLateAdjustmentBusy || reviewLateAdjustmentBusy
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
          : loading || buildBusy || certifyBusy || signOffBusy || lockClosePeriodBusy || queueClosingEntriesBusy || createLateAdjustmentBusy || reviewLateAdjustmentBusy
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
          : loading || buildBusy || certifyBusy || signOffBusy || lockClosePeriodBusy || queueClosingEntriesBusy || reviewCloseEvidenceBusy
            ? "Close/report package action is running."
            : null;
  const selectedExportArtifact = selectedBundle?.exportArtifacts?.[0] ?? null;
  const exportManifestDisabledReason = !selectedBundle
    ? "A report package must be selected before export manifest inspection."
    : !selectedExportArtifact
      ? "The selected report package has no retained export artifacts."
      : loading || buildBusy || certifyBusy || signOffBusy || lockClosePeriodBusy || queueClosingEntriesBusy || reviewLateAdjustmentBusy || exportManifestBusy
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
    lockClosePeriodArmed,
    queueClosingEntriesBusy,
    queueClosingEntriesStatusText,
    queueClosingEntriesStatusTone,
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
    lockClosePeriodButtonLabel: locked ? "Period locked" : lockClosePeriodArmed ? "Confirm lock period" : "Lock period",
    lockClosePeriodDisabledReason,
    queueClosingEntriesButtonLabel: "Queue closing entries",
    queueClosingEntriesDisabledReason,
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
    closingEntriesGate,
    lateAdjustments,
    packageRows,
    selectedPackage,
    certificationSafeguards: buildAccountingReportCertificationSafeguards(closePlan, selectedBundle, criticalIssueCount),
    closeWorkflowSteps,
    validationIssues,
    liveRegionText: `Close report package ${statusLabel}. ${formatCount(openTaskCount, "open task")}. ${formatCount(packageRows.length, "package")}. ${formatCount(closeWorkflowSteps.filter((step) => step.tone === "danger" || step.tone === "warning").length, "workflow step")} needs review.${closingEntriesGate ? ` Closing entries ${closingEntriesGate.statusLabel}.` : ""}`,
    refresh,
    buildReportPackage,
    certifyPackage,
    lockClosePeriod,
    queueClosingEntries,
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
    issueLabels: (item.blockingIssues ?? []).map((issue) => `${issue.severity} | ${issue.code}${issue.targetId ? ` | ${issue.targetId}` : ""}`),
    evidenceReferences: (item.evidenceLinks ?? []).filter((link) => link.trim().length > 0)
  }));
}

function buildClosePostingGateViewModel(closePlan: ClosePeriodPlan): AccountingClosePostingGateViewModel | null {
  const gate = closePlan.closingEntriesGate;
  if (!gate) {
    return null;
  }

  const { label: statusLabel, tone: statusTone } = formatClosePostingGateState(gate.state);
  const currency = closePlan.materialityPolicy.currency;
  const closingBatchIds = gate.closingBatchJournalEntryIds ?? [];
  const reversalDraftIds = gate.reversalDraftJournalEntryIds ?? [];
  const balances = (gate.balances ?? []).map((balance, index) => ({
    rowId: `${gate.gateId}:${balance.financialAccountId?.trim() || balance.accountName}:${index}`,
    accountLabel: balance.symbol?.trim()
      ? `${balance.accountName} (${balance.symbol.trim()})`
      : balance.accountName,
    accountTypeLabel: balance.accountType,
    balanceLabel: formatCurrencyWithCode(balance.balance, currency, true),
    scopeLabel: formatClosePostingBalanceScope(balance.dimensions),
    financialAccountLabel: balance.financialAccountId?.trim() || "No financial-account id"
  }));

  return {
    gateId: gate.gateId,
    label: gate.label,
    statusLabel,
    statusTone,
    isReadyForLock: isClosePostingGateReadyForHardLock(gate),
    netIncomeRollLabel: formatCurrencyWithCode(gate.netIncomeRoll, currency, true),
    temporaryAccountBalanceLabel: formatCount(gate.temporaryAccountBalanceCount, "temporary-account balance"),
    detail: gate.detail,
    draftLabel: gate.draftJournalEntryId
      ? `Draft ${gate.draftJournalEntryId}${gate.draftStatus ? ` | ${gate.draftStatus}` : ""}`
      : "No closing-entry draft queued",
    idempotencyLabel: gate.idempotencyKey?.trim()
      ? `Idempotency ${gate.idempotencyKey.trim()}`
      : "No idempotency key returned",
    closingBatchLabel: closingBatchIds.length > 0
      ? `${formatCount(closingBatchIds.length, "closing batch journal entry")}: ${closingBatchIds.join(", ")}`
      : "No posted closing batch journal entries",
    reversalDraftLabel: reversalDraftIds.length > 0
      ? `${formatCount(reversalDraftIds.length, "reversal draft journal entry")}: ${reversalDraftIds.join(", ")}`
      : "No reversal drafts queued",
    evidenceLabel: formatCount((gate.evidenceLinks ?? []).length, "evidence link"),
    balances
  };
}

function isClosePostingGateReadyForHardLock(
  gate: NonNullable<ClosePeriodPlan["closingEntriesGate"]> | null
): boolean {
  if (!gate) {
    return false;
  }

  return gate.isReadyForLock && (gate.state === "Posted" || gate.state === "NotRequired");
}

function formatClosePostingGateState(state: ClosePostingGateState): { label: string; tone: AccountingToolingTone } {
  switch (state) {
    case "NotRequired":
      return { label: "Not required", tone: "success" };
    case "Posted":
      return { label: "Posted", tone: "success" };
    case "DraftQueued":
      return { label: "Draft queued", tone: "warning" };
    case "Submitted":
      return { label: "Submitted", tone: "warning" };
    case "Approved":
      return { label: "Approved", tone: "warning" };
    case "ReversalQueued":
      return { label: "Reversal queued", tone: "warning" };
    case "Required":
      return { label: "Required", tone: "danger" };
    case "Blocked":
      return { label: "Blocked", tone: "danger" };
    case "Unavailable":
    default:
      return { label: "Unavailable", tone: "danger" };
  }
}

function formatClosePostingBalanceScope(dimensions: LedgerDimensionSet | null | undefined): string {
  const labels = [
    ["Fund", dimensions?.fundId],
    ["Entity", dimensions?.entityId],
    ["Sleeve", dimensions?.sleeveId],
    ["Strategy", dimensions?.strategyId],
    ["Investor", dimensions?.investorId],
    ["Capital account", dimensions?.capitalAccountId],
    ["Instrument", dimensions?.instrumentId],
    ["Position", dimensions?.positionId],
    ["Tax lot", dimensions?.taxLotId],
    ["Cost center", dimensions?.costCenterId],
    ["Counterparty", dimensions?.counterpartyId],
    ["Organization", dimensions?.organizationId],
    ["Portfolio", dimensions?.portfolioId],
    ["Book", dimensions?.bookId],
    ["Account", dimensions?.accountId]
  ]
    .filter((entry): entry is [string, string] => Boolean(entry[1]?.trim()))
    .map(([label, value]) => `${label}: ${value.trim()}`);
  for (const [key, value] of Object.entries(dimensions?.externalGlDimensions ?? {}).sort(([left], [right]) => left.localeCompare(right))) {
    if (value?.trim()) {
      labels.push(`External ${key}: ${value.trim()}`);
    }
  }

  return labels.length > 0 ? labels.join(" | ") : "No scoped dimensions returned";
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
  selectedBundle: AccountingReportPackageBundle | null,
  prepareClosingEntriesOnly: boolean
): LockClosePeriodRequest {
  const reportPackId = selectedBundle?.financialStatements.packageId
    ?? workflow.reportPackReadiness.reportPackId
    ?? `report-pack-${closePlan.fundProfileId}-${closePlan.periodId}`;
  const evidenceLinks = collectAccountingCloseEvidenceLinks(workflow, closePlan);
  const actionSegment = prepareClosingEntriesOnly ? "closing-entry-preparation" : "period-lock";
  evidenceLinks.push(
    `browser://accounting/close/${actionSegment}/${workflow.workflowId}`,
    `evidence://close-package/workflow/${workflow.workflowId}/period/${closePlan.periodId}/book/${closePlan.ledgerBookId ?? "primary"}/${actionSegment}`,
    `evidence://report-package/${reportPackId}/workflow/${workflow.workflowId}/period/${closePlan.periodId}/book/${closePlan.ledgerBookId ?? "primary"}`
  );
  selectedBundle?.financialStatements.evidenceLinks.forEach((link) => evidenceLinks.push(link));
  selectedBundle?.certification.evidenceLinks.forEach((link) => evidenceLinks.push(link));
  selectedBundle?.exportArtifacts?.forEach((artifact) => artifact.evidenceLinks.forEach((link) => evidenceLinks.push(link)));

  return {
    workflowId: workflow.workflowId,
    expectedWorkflowVersion: closePlan.workflowVersion ?? workflow.version,
    actor: "browser-accounting-controller",
    rationale: prepareClosingEntriesOnly
      ? `Prepare closing entries for close period ${closePlan.periodId} before period lock.`
      : `Lock close period ${closePlan.periodId} after close checklist and report package review.`,
    reportPackId,
    evidenceLinks: Array.from(new Set(evidenceLinks)),
    checklistControlApprovals: buildClosePeriodChecklistApprovals(closePlan),
    correlationId: prepareClosingEntriesOnly
      ? `browser-close-period-closing-entries-${workflow.workflowId}`
      : `browser-close-period-lock-${workflow.workflowId}`,
    closePackageId: workflow.closePackage?.closePackageId ?? `close-package-${closePlan.periodId}`,
    closePackageManifestId: workflow.closePackage?.retainedManifestId ?? `close-manifest-${closePlan.periodId}`,
    closePackageRetainedManifestRoute: workflow.closePackage?.retainedManifestRoute
      ?? selectedBundle?.exportArtifacts?.[0]?.route
      ?? `/workstation/accounting/close/${closePlan.periodId}`,
    actionOrigin: "HumanOperator",
    prepareClosingEntriesOnly
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

export { buildCloseCommandCenterViewState } from "./accounting-screen.close-command-center.view-model";
