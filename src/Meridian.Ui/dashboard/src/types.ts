export type WorkspaceKey = "research" | "trading" | "data-operations" | "governance";

export interface SessionInfo {
  displayName: string;
  role: string;
  environment: "paper" | "live";
  activeWorkspace: WorkspaceKey;
  commandCount: number;
}

export interface WorkspaceSummary {
  key: WorkspaceKey;
  label: string;
  description: string;
  status: string;
}

export interface MetricSnapshot {
  id: string;
  label: string;
  value: string;
  delta: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface ResearchRunRecord {
  id: string;
  strategyName: string;
  engine: "Meridian Native" | "Lean";
  mode: "paper" | "live";
  status: "Running" | "Queued" | "Needs Review" | "Completed";
  dataset: string;
  window: string;
  pnl: string;
  sharpe: string;
  lastUpdated: string;
  notes: string;
}

export interface ResearchWorkspaceResponse {
  metrics: MetricSnapshot[];
  runs: ResearchRunRecord[];
}
