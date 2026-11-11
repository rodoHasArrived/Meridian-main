import type { SecurityMasterDeclaredExerciseOutcome, SecurityMasterGovernance, SecurityMasterScheduleBlocker } from "@/types";

export interface SecurityMasterPolicyEvidence {
  title: string;
  summary: string;
  fields: { label: string; value: string }[];
  messages: string[];
  blocked: boolean;
}

export function cashFlowBlockerEvidence(blocker: SecurityMasterScheduleBlocker | null): SecurityMasterPolicyEvidence | null {
  return blocker ? {
    title: "Cash-flow calculation: blocked",
    summary: "The Base calculation on the selected terms cannot supply usable projected cash flows.",
    fields: [], messages: [`${blocker.code}: ${blocker.reason}`], blocked: true
  } : null;
}

export function declaredExerciseEvidence(outcome: SecurityMasterDeclaredExerciseOutcome | null): SecurityMasterPolicyEvidence | null {
  if (!outcome) return null;
  return {
    title: `Declared call/put schedule: ${outcome.status === "Blocked" ? "blocked" : "resolved"}`,
    summary: outcome.status === "Blocked"
      ? "Declared exercise dates are unavailable. Review the reasons below before relying on this schedule."
      : `${outcome.windowCount} exercise ${outcome.windowCount === 1 ? "window" : "windows"} resolved. These dates grant no exercise authority.`,
    fields: [
      { label: "Evaluated as of", value: outcome.asOf },
      { label: "Security version", value: outcome.securityVersion === null ? "Unavailable" : String(outcome.securityVersion) }
    ],
    messages: [...outcome.blockers.map((blocker) => `${blocker.code}: ${blocker.reason}`), ...outcome.diagnostics],
    blocked: outcome.status === "Blocked"
  };
}

export function governanceEvidence(governance: SecurityMasterGovernance | null): SecurityMasterPolicyEvidence {
  if (!governance) return {
    title: "Effective governance unavailable",
    summary: "Refresh the trust snapshot to inspect the host's effective edit and review requirements.",
    fields: [], messages: [], blocked: false
  };
  const sourceLabels: Record<string, string> = {
    Default: "Default", Configuration: "Configuration", ProductionComposition: "Production composition", Latched: "Latched until restart"
  };
  return {
    title: `Effective governance: ${governance.profile}`,
    summary: `${sourceLabels[governance.profileSource] ?? governance.profileSource}.${governance.isProfileLatched ? " Institutional requirements remain in force until the host restarts." : ""}`,
    fields: [
      { label: "Governed term amendments", value: governance.requireGovernedTermAmendments ? "Required" : "Not required" },
      { label: "Independent field reviewer", value: governance.requireIndependentReviewer ? "Required" : "Not required" },
      { label: "Independent profile approver", value: governance.requireIndependentProfileApprover ? "Required" : "Not required" }
    ],
    messages: governance.contradictedKeys.map((key) => `Ignored configuration: ${key}. Effective requirements remain in force.`),
    blocked: governance.contradictedKeys.length > 0
  };
}
