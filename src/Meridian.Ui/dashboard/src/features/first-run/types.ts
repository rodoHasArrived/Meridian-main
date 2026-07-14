export interface StarterWorkspace {
  id: string;
  name: string;
  goal: string;
  description: string;
  defaultRoute: string;
}

export interface ActivationOutcome {
  key: string;
  label: string;
  actionLabel: string;
  route: string;
  isComplete: boolean;
  completedAtUtc: string | null;
}

export interface FirstRunStatus {
  isComplete: boolean;
  goal: string | null;
  starterKitId: string | null;
  dataChoice: string | null;
  workspace: {
    id: string;
    name: string;
    isSample: boolean;
    badge: string;
    safetyMessage: string;
    samplePackVersion: string;
  };
  starterKits: StarterWorkspace[];
  outcomes: ActivationOutcome[];
  recommendedActions: Array<{ label: string; route: string; description: string }>;
}
