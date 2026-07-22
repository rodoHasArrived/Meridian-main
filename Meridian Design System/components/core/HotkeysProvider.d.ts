/**
 * HotkeysProvider — chord + sequence keyboard bindings, plus the built-in shortcut cheat
 * sheet ("?" or Ctrl+/ toggles it; Escape closes). Makes NavRail's "G D" hints and the
 * topbar's Ctrl+K promise actually work. Mount ONCE per screen.
 *
 * Binding syntax: `"ctrl+k"` (modifier chord — fires even while typing), `"g d"` (two-key
 * sequence, 800ms window, suppressed while typing), `"?"` (single key, suppressed while typing).
 *
 * @example
 * <HotkeysProvider bindings={[
 *   { keys: "ctrl+k", label: "Command palette", group: "General", action: openPalette },
 *   { keys: "g d", label: "Go to Dashboard", group: "Navigate", action: () => nav("dashboard") },
 *   { keys: "a", label: "Acknowledge alert", group: "Alerts", action: ack },
 * ]} />
 */
export interface HotkeyBinding {
  /** "ctrl+k" · "g d" · "?" */
  keys: string;
  /** Human label shown in the cheat sheet. */
  label: string;
  /** Cheat-sheet group heading. @default "General" */
  group?: string;
  action?: () => void;
}
export interface HotkeysProviderProps {
  bindings?: HotkeyBinding[];
  /** Render the "?" cheat-sheet overlay. @default true */
  sheet?: boolean;
  /** @default "Keyboard shortcuts" */
  sheetTitle?: string;
}
export declare function HotkeysProvider(props: HotkeysProviderProps): JSX.Element | null;
