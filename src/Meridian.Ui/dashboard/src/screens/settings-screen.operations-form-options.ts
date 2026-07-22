import type {
  LedgerMappingWorkbench,
  OperationsApprovalPolicyMatrix,
  OperationsApprovalPolicyMatrixRow,
  OperationsCloseCalendar,
  OperationsCloseCalendarItem,
  RolePermissionCatalog
} from "@/types";

export function buildLedgerMappingAssignmentDraft(workbench: LedgerMappingWorkbench | null): {
  canAssign: boolean;
  statusLabel: string;
  disabledReason: string | null;
  accountOptions: Array<{ value: string; label: string }>;
  ledgerGroupOptions: Array<{ value: string; label: string }>;
} {
  if (!workbench) {
    return {
      canAssign: false,
      statusLabel: "Workbench unavailable",
      disabledReason: "Ledger mapping workbench data has not loaded.",
      accountOptions: [],
      ledgerGroupOptions: []
    };
  }

  const accountOptions = workbench.accounts
    .filter((account) => account.mapping.requiresUserMapping)
    .map((account) => ({
      value: account.accountId,
      label: `${account.accountCode} - ${account.displayName}`
    }));
  const ledgerGroupOptions = workbench.ledgerGroups
    .filter((group) => group.ledgerGroupId.toLowerCase() !== "unassigned")
    .map((group) => ({
      value: group.ledgerGroupId,
      label: group.displayName ? `${group.displayName} (${group.ledgerGroupId})` : group.ledgerGroupId
    }));

  if (accountOptions.length === 0) {
    return {
      canAssign: false,
      statusLabel: "No unmapped accounts",
      disabledReason: "All accounts in the loaded workbench already have ledger mappings.",
      accountOptions,
      ledgerGroupOptions
    };
  }

  if (ledgerGroupOptions.length === 0) {
    return {
      canAssign: false,
      statusLabel: "No ledger groups",
      disabledReason: "Create or load at least one ledger group before assigning an account mapping.",
      accountOptions,
      ledgerGroupOptions
    };
  }

  return {
    canAssign: true,
    statusLabel: `${accountOptions.length} ready`,
    disabledReason: null,
    accountOptions,
    ledgerGroupOptions
  };
}

export function buildRolePermissionProfileDraft(catalog: RolePermissionCatalog | null): {
  canSave: boolean;
  statusLabel: string;
  disabledReason: string | null;
  baseRoleOptions: Array<{ value: string; label: string }>;
  permissionOptions: Array<{ value: string; label: string; group: string }>;
  defaultBaseRole: string;
  defaultPermissionNames: string[];
} {
  if (!catalog) {
    return {
      canSave: false,
      statusLabel: "Catalog unavailable",
      disabledReason: "Role and permission catalog data has not loaded.",
      baseRoleOptions: [],
      permissionOptions: [],
      defaultBaseRole: "",
      defaultPermissionNames: []
    };
  }

  const builtInRoles = catalog.roles.filter((role) => role.isBuiltIn);
  const defaultRole = builtInRoles.find((role) => role.role === "Accounting") ?? builtInRoles[0];
  const baseRoleOptions = builtInRoles.map((role) => ({
    value: role.role,
    label: settingsRoleDisplayLabel(role.displayName, role.role)
  }));
  const permissionOptions = catalog.permissions.map((permission) => ({
    value: permission.name,
    label: humanizeSettingsIdentifier(permission.name),
    group: humanizeSettingsIdentifier(permission.group)
  }));

  if (baseRoleOptions.length === 0) {
    return {
      canSave: false,
      statusLabel: "No base roles",
      disabledReason: "At least one built-in base role is required before creating a custom profile.",
      baseRoleOptions,
      permissionOptions,
      defaultBaseRole: "",
      defaultPermissionNames: []
    };
  }

  if (permissionOptions.length === 0) {
    return {
      canSave: false,
      statusLabel: "No permissions",
      disabledReason: "At least one permission is required before creating a custom profile.",
      baseRoleOptions,
      permissionOptions,
      defaultBaseRole: defaultRole?.role ?? "",
      defaultPermissionNames: []
    };
  }

  return {
    canSave: true,
    statusLabel: `${permissionOptions.length} grants`,
    disabledReason: null,
    baseRoleOptions,
    permissionOptions,
    defaultBaseRole: defaultRole?.role ?? baseRoleOptions[0]?.value ?? "",
    defaultPermissionNames: defaultRole?.permissions ?? []
  };
}

export function buildApprovalPolicyRuleDraft(matrix: OperationsApprovalPolicyMatrix | null): {
  canSave: boolean;
  statusLabel: string;
  disabledReason: string | null;
  rows: OperationsApprovalPolicyMatrixRow[];
  policyOptions: Array<{ value: string; label: string }>;
} {
  if (!matrix) {
    return {
      canSave: false,
      statusLabel: "Matrix unavailable",
      disabledReason: "Approval policy matrix data has not loaded.",
      rows: [],
      policyOptions: []
    };
  }

  if (matrix.rows.length === 0) {
    return {
      canSave: false,
      statusLabel: "No rules",
      disabledReason: "At least one approval policy rule is required before configuration can be saved.",
      rows: [],
      policyOptions: []
    };
  }

  return {
    canSave: true,
    statusLabel: `${matrix.rows.length} rules`,
    disabledReason: null,
    rows: matrix.rows,
    policyOptions: matrix.rows.map((row) => ({
      value: row.policyKey,
      label: `${row.action} (${row.policyKey})`
    }))
  };
}

export function buildApprovalPolicyPermissionOptions(
  catalogOptions: Array<{ value: string; label: string }>,
  requiredPermission: string
): Array<{ value: string; label: string }> {
  const normalizedPermission = requiredPermission.trim();
  if (!normalizedPermission || catalogOptions.some((option) => option.value === normalizedPermission)) {
    return catalogOptions;
  }

  return [
    {
      value: normalizedPermission,
      label: humanizeSettingsIdentifier(normalizedPermission)
    },
    ...catalogOptions
  ];
}

export function buildCloseCalendarItemDraft(calendar: OperationsCloseCalendar | null): {
  canSave: boolean;
  statusLabel: string;
  disabledReason: string | null;
  items: OperationsCloseCalendarItem[];
  workflowOptions: Array<{ value: string; label: string }>;
} {
  if (!calendar) {
    return {
      canSave: false,
      statusLabel: "Calendar unavailable",
      disabledReason: "Account close calendar data has not loaded.",
      items: [],
      workflowOptions: []
    };
  }

  const configurableItems = calendar.items.filter((item) => item.nextDueTaskId && item.nextDueDate);
  if (configurableItems.length === 0) {
    return {
      canSave: false,
      statusLabel: "No open tasks",
      disabledReason: "At least one account close workflow with a next due task is required before the calendar can be configured.",
      items: [],
      workflowOptions: []
    };
  }

  return {
    canSave: true,
    statusLabel: `${configurableItems.length} workflows`,
    disabledReason: null,
    items: configurableItems,
    workflowOptions: configurableItems.map((item) => ({
      value: item.workflowId,
      label: `${item.periodId}: ${item.nextDueLabel ?? item.nextDueTaskId}`
    }))
  };
}

export function humanizeSettingsIdentifier(value: string): string {
  return value
    .trim()
    .replace(/[._-]+/g, " ")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .replace(/\s+/g, " ");
}

export function settingsRoleDisplayLabel(displayName: string | null | undefined, role: string): string {
  const humanizedRole = humanizeSettingsIdentifier(role);
  const label = displayName?.trim();
  if (!label) {
    return humanizedRole;
  }

  return label.localeCompare(humanizedRole, undefined, { sensitivity: "accent" }) === 0
    ? label
    : `${label} (${humanizedRole})`;
}
