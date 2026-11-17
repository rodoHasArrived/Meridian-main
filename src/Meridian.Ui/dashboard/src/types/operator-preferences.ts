// Mirror of Meridian.Contracts.Workstation.OperatorPreferencesDto: the signed-in operator's
// presentation preferences, kept on the host so they follow the operator to another browser.
// Every value is optional; null means nothing is stored and the browser keeps its own value.

export interface OperatorPreferenceValues {
  appearance?: string | null;
  density?: string | null;
  displayTimeZone?: string | null;
  pageDescriptionsHidden?: boolean | null;
  keyBindings?: Record<string, string> | null;
}

export interface OperatorPreferences {
  preferences: OperatorPreferenceValues;
  updatedAt: string | null;
}
