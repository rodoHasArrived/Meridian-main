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

export interface SampleHolding {
  symbol: string;
  quantity: number;
  averagePrice: number;
  lastPrice: number;
  marketValue: number;
  unrealizedPnl: number;
}

export interface SampleBreak {
  id: string;
  title: string;
  category: string;
  severity: string;
  variance: number;
  summary: string;
  route: string;
}

export interface SampleArtifact {
  name: string;
  status: string;
  detail: string;
  route: string;
}

export interface SampleMarketHistory {
  symbols: string[];
  sessions: number;
  route: string;
}

export interface SampleHighlight {
  label: string;
  value: string;
  detail: string;
  route: string;
}

export interface SampleWorkspace {
  headline: string;
  summary: string;
  portfolioName: string;
  portfolioValue: number;
  cash: number;
  unrealizedPnl: number;
  holdings: SampleHolding[];
  watchlist: string[];
  reconciliationBreaks: SampleBreak[];
  report: SampleArtifact;
  strategy: SampleArtifact;
  marketHistory: SampleMarketHistory;
  highlights: SampleHighlight[];
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
  sampleWorkspace: SampleWorkspace | null;
}
