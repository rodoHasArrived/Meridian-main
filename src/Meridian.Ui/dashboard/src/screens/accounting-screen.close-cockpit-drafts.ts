import {
  formatCount,
  formatDateOnly,
  formatDateTimeLabel,
} from "./accounting-screen.formatting";
import type {
  AccountingClosePlanTaskRowViewModel,
  AccountingCloseSetupDependencyOptionViewModel,
  AccountingCloseSetupDraftViewModel,
  AccountingCloseSetupSignOffRoleOptionViewModel,
  AccountingCloseSetupTaskOptionViewModel,
  AccountingCloseSignOffDecisionOptionViewModel,
  AccountingCloseSignOffDraftViewModel,
  AccountingCloseSignOffRoleOptionViewModel,
  AccountingCloseSignOffTaskOptionViewModel,
  AccountingLateAdjustmentDraftViewModel,
  AccountingToolingTone,
} from "./accounting-screen.view-model";
import type {
  CloseDependency,
  ClosePeriodPlan,
  CloseSignOffRequirement,
  CloseTask,
} from "@/types";

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

export function resolveCloseTaskSignOffDraftTarget(
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

export function createAccountingCloseSignOffDraft(
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

export function validateCloseSignOffDraft(
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

export function createAccountingLateAdjustmentDraft(currency = "USD"): AccountingLateAdjustmentDraftViewModel {
  return {
    journalEntryId: "",
    amount: "",
    currency,
    reason: ""
  };
}

export function createAccountingCloseSetupDraft(
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

export function validateCloseSetupTaskSelection(
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

export function validateCloseSetupMaterialityDraft(setupDraft: AccountingCloseSetupDraftViewModel): string | null {
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

export function validateCloseSetupSignOffDraft(setupDraft: AccountingCloseSetupDraftViewModel): string | null {
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

export function parseCloseSetupSignOffRequirementRows(value: string): Array<{ role: string; requiredApprovalCount: number; evidenceRequirement: string }> {
  return splitCloseSetupSignOffRequirementRows(value)
    .map((item) => parseCloseSetupSignOffRequirementEntry(item))
    .filter((requirement, index, requirements) =>
      requirement.role.length > 0 &&
      Number.isFinite(requirement.requiredApprovalCount) &&
      requirement.requiredApprovalCount > 0 &&
      requirements.findIndex((candidate) => candidate.role.toLowerCase() === requirement.role.toLowerCase()) === index);
}

export function buildCloseSetupSingleSignOffRequirementRow(setupDraft: AccountingCloseSetupDraftViewModel): string {
  const requiredApprovalCount = Number.parseInt(setupDraft.taskRequiredApprovalCount, 10);
  const role = setupDraft.taskRequiredApprovalRole.trim() || "Controller";
  const evidence = setupDraft.taskRequiredEvidence.trim() || "Retained close checklist evidence";
  return `${role} | ${Number.isFinite(requiredApprovalCount) && requiredApprovalCount > 0 ? requiredApprovalCount : 1} | ${evidence}`;
}

export function parseCloseSetupDependencyIds(value: string): string[] {
  return value
    .split(/[,\r\n;]+/)
    .map((item) => parseCloseSetupDependencyEntry(item).taskId)
    .filter((item, index, items) => item.length > 0 && items.findIndex((candidate) => candidate.toLowerCase() === item.toLowerCase()) === index);
}

export function parseCloseSetupDependencyEntry(value: string): { taskId: string; reason: string | null } {
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

export function parseCloseSetupDependencyReasonOverrides(value: string): Map<string, string> {
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

export function resolveCloseSetupDependencyReason(
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

export function buildCloseSetupTaskOptions(
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

export function buildCloseSetupDependencyOptions(
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

export function buildCloseSetupSignOffRoleOptions(
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

export function buildCloseSignOffTaskOptions(
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

export function buildCloseSignOffRoleOptions(
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

export function buildCloseSignOffDecisionOptions(
  signOffDraft: AccountingCloseSignOffDraftViewModel
): AccountingCloseSignOffDecisionOptionViewModel[] {
  return (["Approved", "Rejected"] as const).map((decision) => ({
    decision,
    label: decision,
    selected: signOffDraft.decision === decision,
    selectAriaLabel: `${signOffDraft.decision === decision ? "Selected" : "Select"} close task sign-off decision ${decision}`
  }));
}

export function buildCloseTaskSignOffDetail(task: CloseTask): string | null {
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

export function buildClosePlanTaskRow(task: CloseTask): AccountingClosePlanTaskRowViewModel {
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


export function formatCloseTaskStatus(status: CloseTask["status"]): string {
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

export function closeTaskStatusTone(status: CloseTask["status"]): AccountingToolingTone {
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
