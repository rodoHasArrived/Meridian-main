// Mirror of Meridian.Contracts.Workstation.OperatorPreferencesDto: the signed-in operator's
// presentation preferences, kept on the host so they follow the operator to another browser.
// Every value is optional; null means nothing is stored and the browser keeps its own value.

export interface OperatorPreferenceValues {
  appearance?: string | null;
  density?: string | null;
  displayTimeZone?: string | null;
  pageDescriptionsHidden?: boolean | null;
  keyBindings?: Record<string, string> | null;
  /**
   * WPF desktop shortcut overrides, keyed by desktop action id. Owned by the desktop: the browser
   * never applies or sends them, and the host keeps them when a save leaves them out.
   */
  desktopKeyBindings?: Record<string, OperatorDesktopShortcut> | null;
}

/** One WPF desktop shortcut's difference from its default, with WPF key and modifier names. */
export interface OperatorDesktopShortcut {
  key?: string | null;
  modifiers?: string | null;
  isEnabled?: boolean | null;
}

export interface OperatorPreferences {
  preferences: OperatorPreferenceValues;
  updatedAt: string | null;
}
