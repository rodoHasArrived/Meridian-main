import { useCallback, useMemo, useState } from "react";
import type { ResolveReconciliationBreakRequest } from "@/types";
import type { ReconciliationBreakCommand } from "./accounting-screen.view-model";

export type ReconciliationBreakResolutionStatus = ResolveReconciliationBreakRequest["status"];

export interface ReconciliationResolveDialogState {
  breakId: string;
  status: ReconciliationBreakResolutionStatus;
  rationale: string;
  inputId: string;
  helpId: string;
  formAriaLabel: string;
  label: string;
  placeholder: string;
  helpText: string;
  submitLabel: string;
  submitAriaLabel: string;
  submitDisabledReason: string | null;
  cancelLabel: string;
  cancelAriaLabel: string;
  isSubmitDisabled: boolean;
}

export interface ReconciliationResolveDialogViewModel {
  active: ReconciliationResolveDialogState | null;
  open: (breakId: string, status: ReconciliationBreakResolutionStatus) => void;
  close: () => void;
  updateRationale: (value: string) => void;
  submit: () => Promise<void>;
  isOpenFor: (breakId: string) => boolean;
  getActionDisabledReason: (
    breakId: string,
    command: ReconciliationBreakCommand,
    baseDisabledReason?: string | null
  ) => string | null;
}

export function buildReconciliationResolveDialogState(
  breakId: string,
  status: ReconciliationBreakResolutionStatus,
  rationale: string
): ReconciliationResolveDialogState {
  const command = status === "Resolved" ? "resolve" : "dismiss";
  const commandLabel = status === "Resolved" ? "Resolve" : "Dismiss";
  const inputId = `rationale-${breakId}`;
  const helpId = `rationale-help-${breakId}`;

  return {
    breakId,
    status,
    rationale,
    inputId,
    helpId,
    formAriaLabel: `${commandLabel} reconciliation break ${breakId}`,
    label: `${commandLabel} rationale`,
    placeholder: `Describe why this break is being ${command === "resolve" ? "resolved" : "dismissed"}...`,
    helpText: "A rationale is required before this queue action can be submitted.",
    submitLabel: `Confirm ${command}`,
    submitAriaLabel: `Confirm ${command} for reconciliation break ${breakId}`,
    submitDisabledReason: rationale.trim()
      ? null
      : "Enter an operator rationale before confirming this queue action.",
    cancelLabel: "Cancel",
    cancelAriaLabel: `Cancel ${command} for reconciliation break ${breakId}`,
    isSubmitDisabled: !rationale.trim()
  };
}

export function useReconciliationResolveDialogViewModel(
  resolveBreak: (
    breakId: string,
    status: ReconciliationBreakResolutionStatus,
    operatorRationale: string
  ) => Promise<void>
): ReconciliationResolveDialogViewModel {
  const [dialog, setDialog] = useState<{ breakId: string; status: ReconciliationBreakResolutionStatus } | null>(null);
  const [rationale, setRationale] = useState("");

  const close = useCallback(() => {
    setDialog(null);
    setRationale("");
  }, []);

  const open = useCallback((breakId: string, status: ReconciliationBreakResolutionStatus) => {
    setDialog({ breakId, status });
    setRationale("");
  }, []);

  const submit = useCallback(async () => {
    if (!dialog || !rationale.trim()) {
      return;
    }

    await resolveBreak(dialog.breakId, dialog.status, rationale);
    close();
  }, [close, dialog, rationale, resolveBreak]);

  const active = useMemo(
    () => (dialog ? buildReconciliationResolveDialogState(dialog.breakId, dialog.status, rationale) : null),
    [dialog, rationale]
  );

  const isOpenFor = useCallback((breakId: string) => dialog?.breakId === breakId, [dialog]);
  const getActionDisabledReason = useCallback((
    breakId: string,
    command: ReconciliationBreakCommand,
    baseDisabledReason: string | null = null
  ) => {
    if (baseDisabledReason) {
      return baseDisabledReason;
    }

    if (dialog?.breakId === breakId && (command === "resolve" || command === "dismiss")) {
      return "Enter the rationale or cancel the open queue action before choosing another action.";
    }

    return null;
  }, [dialog]);

  return {
    active,
    open,
    close,
    updateRationale: setRationale,
    submit,
    isOpenFor,
    getActionDisabledReason
  };
}
