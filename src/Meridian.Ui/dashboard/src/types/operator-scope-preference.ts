// Mirror of Meridian.Contracts.Workstation.OperatorScopePreferenceDto (C1): the signed-in operator's
// default operating scope, applied when a session opens with no scope in its link or browser.

export interface OperatingScopeValues {
  symbol?: string | null;
  fundAccountId?: string | null;
  runId?: string | null;
  provider?: string | null;
  from?: string | null;
  to?: string | null;
  date?: string | null;
  asOf?: string | null;
}

export interface OperatorScopePreference {
  defaultScope: OperatingScopeValues | null;
  updatedAt: string | null;
}
