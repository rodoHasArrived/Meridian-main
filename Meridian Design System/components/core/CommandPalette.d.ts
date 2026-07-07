/**
 * CommandPalette — the Ctrl-K operator command surface the WorkstationTopbar advertises.
 * Grouped, keyboard-first results: a 4px left-inset accent marks the selected row (no full
 * background swap), shortcuts render as mono `kbd`s, every group carries a small-caps header,
 * and the list scrolls past ~6 rows. The only elevation is the float shadow.
 *
 * Pair with `useCommandPalette()`, which wires the global Ctrl/Cmd+K toggle:
 *   const { open, setOpen, closePalette } = useCommandPalette();
 *   <CommandPalette open={open} onClose={closePalette} commands={commands} />
 *
 * Or use it self-contained — it registers its own `hotkey` (default Ctrl/Cmd+K) and manages
 * open state internally, so the lone requirement is the command list:
 *   <CommandPalette commands={commands} />
 */
import * as React from "react";

export interface Command {
  /** Stable unique id (React key). */
  id: string;
  /** Visible label. */
  label: string;
  /** Section header this command files under. @default "Commands" */
  group?: string;
  /** Right-aligned mono hint (e.g. "→", "open"). Ignored when `shortcut` is set. */
  hint?: string;
  /** Keyboard shortcut, as "Ctrl+K" / "G then P" or ["⌘", "K"] — rendered as kbd chips. */
  shortcut?: string | string[];
  /** Optional 16px leading icon node. */
  icon?: React.ReactNode;
  /** Extra terms folded into the fuzzy filter (synonyms, ticker, route). */
  keywords?: string[];
  /** Styles the row as destructive (red accent + label). */
  danger?: boolean;
  /** Invoked on Enter / click. The palette closes first, then calls this. */
  run?: () => void;
}

export interface CommandPaletteProps {
  /** Controlled open state. Omit to let the palette manage its own (uncontrolled). */
  open?: boolean;
  /** Initial open state when uncontrolled. @default false */
  defaultOpen?: boolean;
  /** Fires on every open/close (both modes). */
  onOpenChange?: (open: boolean) => void;
  onClose?: () => void;
  /** Global toggle hotkey, e.g. "mod+k" (Ctrl/Cmd), "ctrl+k", "/". null disables. @default "mod+k" */
  hotkey?: string | null;
  commands: Command[];
  /** @default "Search symbols, runs, commands…" */
  placeholder?: string;
  /** @default "No matches" */
  emptyText?: string;
  /** Force section order; otherwise groups appear in first-seen order. */
  groupOrder?: string[] | null;
  /** Show the ↑↓ / ↵ / toggle footer. @default true */
  footer?: boolean;
}

export declare function CommandPalette(props: CommandPaletteProps): JSX.Element | null;

export interface UseCommandPalette {
  open: boolean;
  setOpen: React.Dispatch<React.SetStateAction<boolean>>;
  openPalette: () => void;
  closePalette: () => void;
}
/**
 * Optional convenience hook that binds a document-level Ctrl/Cmd+K listener. Lowercase, so it
 * is NOT on the `window.<Namespace>` export surface — reach for it only when importing the
 * component source directly. The component's own `hotkey` prop covers the common case.
 */
export declare function useCommandPalette(initial?: boolean): UseCommandPalette;
