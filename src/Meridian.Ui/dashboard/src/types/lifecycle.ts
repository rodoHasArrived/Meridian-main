export type RuntimeLifecycleState =
  | "Created"
  | "Bootstrapping"
  | "Validating"
  | "StartingHost"
  | "EvaluatingReadiness"
  | "Ready"
  | "Degraded"
  | "ShutdownRequested"
  | "Draining"
  | "Flushing"
  | "StoppingHost"
  | "Stopped"
  | "Failed";

export type RuntimeReadinessStatus = "Starting" | "Ready" | "Degraded" | "NotReady" | "Stopping" | "Failed";
export type LifecycleCheckRequirement = "Required" | "Degradable";
export type LifecycleCheckStatus = "Pending" | "Passing" | "Degraded" | "Failing" | "Skipped";
export type LifecycleShutdownReason =
  | "Operator"
  | "Restart"
  | "Supervisor"
  | "HttpLocalShutdown"
  | "ConsoleCancel"
  | "ExternalCancellation"
  | "ProcessExit"
  | "StartupFailure";
export type LifecycleShutdownStage =
  | "Requested"
  | "StopAcceptingWork"
  | "Draining"
  | "Flushing"
  | "PersistingReceipt"
  | "ReleasingHost"
  | "Completed"
  | "Failed";
export type LifecycleShutdownOutcome =
  | "Pending"
  | "Succeeded"
  | "SucceededWithWarnings"
  | "TimedOut"
  | "Forced"
  | "Failed"
  | "Cancelled";

export interface RuntimeLifecycleCheck {
  id: string;
  displayName: string;
  requirement: LifecycleCheckRequirement;
  status: LifecycleCheckStatus;
  message: string;
  checkedAtUtc: string;
  durationMilliseconds: number;
}

export interface RuntimeLifecycleSnapshot {
  sessionId: string;
  state: RuntimeLifecycleState;
  readiness: RuntimeReadinessStatus;
  startedAtUtc: string;
  stateChangedAtUtc: string;
  activePhase: string;
  acceptingWork: boolean;
  shutdownRequested: boolean;
  shutdownReason: string | null;
  activeShutdownOperationId: string | null;
  processId: number | null;
  processName: string | null;
  port: number | null;
  configPath: string | null;
  uptimeSeconds: number;
  checks: RuntimeLifecycleCheck[];
}

export interface LifecycleShutdownRequest {
  reason: LifecycleShutdownReason;
  detail?: string;
  requestedBy?: string;
}

export interface LifecycleShutdownAccepted {
  accepted: boolean;
  operationId: string;
  operationUri: string;
  state: RuntimeLifecycleState;
  requestedAtUtc: string;
}

export interface LifecycleShutdownStageRecord {
  stage: LifecycleShutdownStage;
  outcome: LifecycleShutdownOutcome;
  startedAtUtc: string;
  completedAtUtc: string | null;
  message: string | null;
}

export interface LifecycleShutdownOperation {
  operationId: string;
  reason: LifecycleShutdownReason;
  detail: string | null;
  requestedBy: string | null;
  currentStage: LifecycleShutdownStage;
  outcome: LifecycleShutdownOutcome;
  requestedAtUtc: string;
  deadlineUtc: string;
  completedAtUtc: string | null;
  stages: LifecycleShutdownStageRecord[];
}

export interface LifecycleShutdownParticipantReceipt {
  participantId: string;
  stage: LifecycleShutdownStage;
  outcome: LifecycleShutdownOutcome;
  critical: boolean;
  durationMilliseconds: number;
  message: string | null;
}

export interface LifecycleShutdownReceipt {
  sessionId: string;
  operationId: string;
  reason: LifecycleShutdownReason;
  outcome: LifecycleShutdownOutcome;
  startedAtUtc: string;
  completedAtUtc: string;
  forcedTermination: boolean;
  participants: LifecycleShutdownParticipantReceipt[];
}
