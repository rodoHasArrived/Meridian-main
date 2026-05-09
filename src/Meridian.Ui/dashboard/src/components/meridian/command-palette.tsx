import { useEffect, useRef, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { X } from "lucide-react";
import {
  buildCommandPaletteViewModel,
  resolveCommandPaletteKeyCommand,
  type CommandPaletteFocusBoundary
} from "@/components/meridian/command-palette.view-model";
import { cn } from "@/lib/utils";
import type { WorkflowLibrary, WorkflowPresetLibrary } from "@/types";

/**
 * Full-screen command palette overlay for quick workspace navigation.
 *
 * Opened and closed by the parent via `open` / `onOpenChange`. The current workspace
 * item (matched from `useLocation`) is highlighted and receives initial focus on open.
 *
 * **Keyboard:** Escape closes the palette. Tab and Shift-Tab are contained inside
 * the modal command surface while it is open.
 *
 * **Backdrop:** clicking outside the panel card calls `onOpenChange(false)`.
 *
 * Route commands and descriptions are derived from `buildCommandPaletteViewModel`.
 * The view-model combines canonical workspaces with backend workflow library and
 * preset payloads when those payloads are available.
 *
 * @example
 * <CommandPalette open={paletteOpen} onOpenChange={setPaletteOpen} />
 */
interface CommandPaletteProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  workflowLibrary?: WorkflowLibrary | null;
  workflowPresets?: WorkflowPresetLibrary | null;
  workflowError?: string | null;
  onPresetUsed?: (presetId: string) => void | Promise<void>;
}

export function CommandPalette({
  open,
  onOpenChange,
  workflowLibrary,
  workflowPresets,
  workflowError,
  onPresetUsed
}: CommandPaletteProps) {
  const { pathname } = useLocation();
  const [query, setQuery] = useState("");
  const dialogRef = useRef<HTMLDivElement>(null);
  const searchInputRef = useRef<HTMLInputElement | null>(null);
  const initialCommandRef = useRef<HTMLAnchorElement | null>(null);
  const restoreFocusRef = useRef<HTMLElement | null>(null);
  const viewModel = buildCommandPaletteViewModel(
    pathname,
    undefined,
    {
      workflowLibrary,
      workflowPresets,
      workflowError
    },
    query
  );

  useEffect(() => {
    if (!open) {
      setQuery("");
    }
  }, [open]);

  useEffect(() => {
    if (!open) {
      return;
    }

    restoreFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    searchInputRef.current?.focus();

    const handleKeyDown = (event: KeyboardEvent) => {
      const command = resolveCommandPaletteKeyCommand({
        key: event.key,
        shiftKey: event.shiftKey,
        focusBoundary: getCommandPaletteFocusBoundary(dialogRef.current, document.activeElement)
      });

      if (command === "close") {
        event.preventDefault();
        closePalette();
        return;
      }

      if (command === "focus-first" || command === "focus-last") {
        event.preventDefault();
        focusCommandPaletteBoundary(dialogRef.current, command);
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [onOpenChange, open]);

  const closePalette = () => {
    onOpenChange(false);
    setQuery("");
    restoreFocusRef.current?.focus();
  };

  if (!open) {
    return null;
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center bg-background/70 px-4 py-24 backdrop-blur-sm"
      data-testid="command-palette-backdrop"
      onClick={(event) => {
        if (event.target === event.currentTarget) {
          closePalette();
        }
      }}
    >
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="command-palette-title"
        aria-describedby="command-palette-route-context"
        tabIndex={-1}
        className="command-palette-shell w-full max-w-xl outline-none"
      >
        <div className="flex items-center justify-between gap-3 border-b border-border/60 pb-3">
          <div>
            <div className="eyebrow-label">Command Palette</div>
            <h2 id="command-palette-title" className="mt-1 text-lg font-semibold">
              {viewModel.title}
            </h2>
            <p id="command-palette-route-context" className="mt-1 text-xs text-muted-foreground">
              {viewModel.routeSummary}
            </p>
          </div>
          <button
            type="button"
            className="inline-flex h-9 w-9 items-center justify-center rounded-md text-muted-foreground hover:bg-secondary hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
            onClick={closePalette}
            aria-label="Close command palette"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="command-palette-summary" aria-label={viewModel.scopeLabel}>
          <span className="command-palette-chip">{viewModel.activeWorkspaceLabel}</span>
          <span className="command-palette-chip">{viewModel.shortcutHint}</span>
          {viewModel.backendStatusLabel ? (
            <span className="command-palette-chip">{viewModel.backendStatusLabel}</span>
          ) : null}
        </div>
        <label htmlFor="command-palette-search" className="sr-only">
          {viewModel.searchInputLabel}
        </label>
        <input
          ref={searchInputRef}
          id="command-palette-search"
          type="search"
          value={query}
          autoComplete="off"
          spellCheck={false}
          placeholder={viewModel.searchPlaceholder}
          aria-label={viewModel.searchInputLabel}
          aria-describedby="command-palette-filter-count"
          className="mt-3 h-10 w-full rounded-md border border-border/80 bg-background px-3 text-sm text-foreground outline-none transition-colors placeholder:text-muted-foreground focus-visible:border-primary/70 focus-visible:ring-2 focus-visible:ring-primary/35"
          onChange={(event) => setQuery(event.target.value)}
        />
        <nav className="mt-3 grid max-h-[62vh] gap-2 overflow-y-auto pr-1" aria-label={viewModel.commandListLabel}>
          <div className="eyebrow-label">{viewModel.itemCountLabel}</div>
          <div id="command-palette-filter-count" className="text-xs text-muted-foreground" aria-live="polite">
            {viewModel.filteredItemCountLabel}
          </div>
          {viewModel.emptyState ? (
            <div className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm">
              <div className="font-semibold">{viewModel.emptyState.title}</div>
              <div className="mt-1 text-muted-foreground">{viewModel.emptyState.detail}</div>
            </div>
          ) : null}
          {viewModel.filteredItems.map((item) => (
            <Link
              key={item.id}
              ref={item.id === viewModel.initialFocusItemId ? initialCommandRef : undefined}
              to={item.route}
              data-command-id={item.id}
              aria-label={item.ariaLabel}
              aria-current={item.active ? "page" : undefined}
              className={cn(
                "command-palette-command rounded-md border px-3 py-3 text-sm transition-colors",
                "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                item.active
                  ? "border-primary/35 bg-primary/10 text-foreground"
                  : "border-transparent hover:border-border/70 hover:bg-secondary/70"
              )}
              onClick={() => {
                if (item.presetId && onPresetUsed) {
                  void Promise.resolve(onPresetUsed(item.presetId)).catch(() => undefined);
                }

                closePalette();
              }}
            >
              <span className="flex items-start justify-between gap-3">
                <span className="min-w-0">
                  <span className="block font-semibold">{item.commandLabel}</span>
                  <span className="mt-1 block text-muted-foreground">{item.description}</span>
                </span>
                <span className="flex shrink-0 flex-col items-end gap-2">
                  <span className="command-palette-route" aria-label={`Route ${item.routeLabel}`}>
                    {item.routeLabel}
                  </span>
                  <span className="rounded-sm border border-border/70 bg-secondary/55 px-2 py-1 font-mono text-[10px] uppercase tracking-[0.12em] text-muted-foreground">
                    {item.statusLabel}
                  </span>
                </span>
              </span>
            </Link>
          ))}
        </nav>
      </div>
    </div>
  );
}

const commandPaletteFocusableSelector = [
  "a[href]",
  "button:not([disabled])",
  "input:not([disabled])",
  "select:not([disabled])",
  "textarea:not([disabled])",
  "[tabindex]:not([tabindex='-1'])"
].join(",");

function getCommandPaletteFocusableElements(dialog: HTMLDivElement | null): HTMLElement[] {
  if (!dialog) {
    return [];
  }

  return Array.from(dialog.querySelectorAll<HTMLElement>(commandPaletteFocusableSelector)).filter(
    (element) => !element.hasAttribute("disabled") && element.getAttribute("aria-hidden") !== "true"
  );
}

function getCommandPaletteFocusBoundary(
  dialog: HTMLDivElement | null,
  activeElement: Element | null
): CommandPaletteFocusBoundary {
  const focusable = getCommandPaletteFocusableElements(dialog);
  if (focusable.length === 0) {
    return "none";
  }

  if (!dialog || !activeElement || !dialog.contains(activeElement)) {
    return "outside";
  }

  if (activeElement === focusable[0]) {
    return "first";
  }

  if (activeElement === focusable[focusable.length - 1]) {
    return "last";
  }

  return "middle";
}

function focusCommandPaletteBoundary(dialog: HTMLDivElement | null, command: "focus-first" | "focus-last") {
  const focusable = getCommandPaletteFocusableElements(dialog);
  const target = command === "focus-first" ? focusable[0] : focusable[focusable.length - 1];
  (target ?? dialog)?.focus();
}
