import { useEffect, useRef, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { X } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  buildCommandPaletteViewModel,
  resolveCommandPaletteKeyCommand,
  type CommandPaletteFocusAction,
  type CommandPaletteFocusBoundary,
  type CommandPaletteFocusTarget
} from "@/components/meridian/command-palette.view-model";
import { COMMAND_PALETTE_DIALOG_ID } from "@/app-shell.view-model";
import type { AppShellOperatingScopeInput } from "@/lib/app-shell-operating-scope";
import { cn } from "@/lib/utils";
import type { WorkflowLibrary, WorkflowPresetLibrary } from "@/types";

/**
 * Full-screen command palette overlay for quick workspace navigation.
 *
 * Opened and closed by the parent via `open` / `onOpenChange`. The current workspace
 * item (matched from `useLocation`) is highlighted and receives initial focus on open.
 *
 * **Keyboard:** Escape closes the palette. Arrow Up/Down moves across command
 * results. Tab and Shift-Tab are contained inside the modal command surface while
 * it is open.
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
  operatorFocusItems?: CommandPaletteFocusAction[] | null;
  operatingContextSymbol?: string | null;
  operatingScope?: AppShellOperatingScopeInput | null;
  onPresetUsed?: (presetId: string) => void | Promise<void>;
}

export function CommandPalette({
  open,
  onOpenChange,
  workflowLibrary,
  workflowPresets,
  workflowError,
  operatorFocusItems,
  operatingContextSymbol,
  operatingScope,
  onPresetUsed
}: CommandPaletteProps) {
  const { pathname, search, hash } = useLocation();
  const [query, setQuery] = useState("");
  const dialogRef = useRef<HTMLDivElement>(null);
  const searchInputRef = useRef<HTMLInputElement | null>(null);
  const initialCommandRef = useRef<HTMLAnchorElement | null>(null);
  const restoreFocusRef = useRef<HTMLElement | null>(null);
  const currentRoute = `${pathname}${search}${hash}`;
  const viewModel = buildCommandPaletteViewModel(
    currentRoute,
    undefined,
    {
      workflowLibrary,
      workflowPresets,
      workflowError,
      operatorFocusItems
    },
    query,
    operatingContextSymbol ?? null,
    operatingScope ?? null
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
        focusBoundary: getCommandPaletteFocusBoundary(dialogRef.current, document.activeElement),
        focusTarget: getCommandPaletteFocusTarget(searchInputRef.current, document.activeElement)
      });

      if (command === "close") {
        event.preventDefault();
        closePalette();
        return;
      }

      if (command === "focus-first" || command === "focus-last") {
        event.preventDefault();
        focusCommandPaletteBoundary(dialogRef.current, command);
        return;
      }

      if (command === "activate-first-command") {
        event.preventDefault();
        activateFirstCommandPaletteCommand(dialogRef.current);
        return;
      }

      if (command === "focus-next-command" || command === "focus-previous-command") {
        event.preventDefault();
        focusCommandPaletteCommand(dialogRef.current, document.activeElement, command);
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
        id={COMMAND_PALETTE_DIALOG_ID}
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
          {viewModel.operatingContextLabel ? (
            <span className="command-palette-chip">{viewModel.operatingContextLabel}</span>
          ) : null}
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
          aria-describedby={viewModel.searchDescribedBy}
          className="mt-3 h-10 w-full rounded-md border border-border/80 bg-background px-3 text-sm text-foreground outline-none transition-colors placeholder:text-muted-foreground focus-visible:border-primary/70 focus-visible:ring-2 focus-visible:ring-primary/35"
          onChange={(event) => setQuery(event.target.value)}
        />
        <nav className="mt-3 max-h-[62vh] overflow-y-auto pr-1" aria-label={viewModel.commandListLabel}>
          <div className="flex items-center justify-between gap-3 pb-2">
            <div className="eyebrow-label">{viewModel.itemCountLabel}</div>
            <div id="command-palette-filter-count" className="text-xs text-muted-foreground" aria-live="polite">
              {viewModel.filteredItemCountLabel}
            </div>
          </div>
          {query.trim() === "" && viewModel.recommendedItems.length > 0 ? (
            <section className="mb-3 rounded-md border border-primary/25 bg-primary/5 px-3 py-3" aria-label={viewModel.recommendedItemsLabel}>
              <div className="mb-2 flex items-center justify-between gap-3">
                <div className="eyebrow-label">{viewModel.recommendedItemsLabel}</div>
                <div className="font-mono text-[10px] text-muted-foreground">{viewModel.recommendedItemsCountLabel}</div>
              </div>
              <div className="grid gap-1 sm:grid-cols-2">
                {viewModel.recommendedItems.map((item) => (
                  <Link
                    key={`recommended:${item.id}`}
                    to={item.route}
                    data-command-id={`recommended:${item.id}`}
                    aria-label={`Recommended command: ${item.ariaLabel}`}
                    className="command-palette-command rounded-md border border-border/60 bg-background/70 px-3 py-2 text-sm transition-colors hover:border-primary/35 hover:bg-primary/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                    onClick={() => {
                      if (item.presetId && onPresetUsed) {
                        void Promise.resolve(onPresetUsed(item.presetId)).catch(() => undefined);
                      }

                      closePalette();
                    }}
                  >
                    <span className="flex items-start justify-between gap-3">
                      <span className="min-w-0">
                        <span className="block truncate font-semibold leading-snug">{item.commandLabel}</span>
                        <span className="mt-0.5 block truncate text-xs text-muted-foreground">{item.description}</span>
                      </span>
                      <span className="command-palette-route shrink-0" aria-label={`Route ${item.routeLabel}`}>
                        {item.routeLabel}
                      </span>
                    </span>
                  </Link>
                ))}
              </div>
            </section>
          ) : null}
          {viewModel.emptyState ? (
            <section
              id={viewModel.emptyState.id}
              aria-labelledby={viewModel.emptyState.titleId}
              className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm"
            >
              <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div className="min-w-0">
                  <div className="inline-flex rounded-sm border border-border/70 bg-background/70 px-2 py-1 font-mono text-[10px] uppercase tracking-[0.12em] text-muted-foreground">
                    {viewModel.emptyState.statusLabel}
                  </div>
                  <div
                    id={viewModel.emptyState.titleId}
                    className="mt-2 font-semibold"
                  >
                    {viewModel.emptyState.title}
                  </div>
                  <div
                    id={viewModel.emptyState.detailId}
                    role="status"
                    aria-live="polite"
                    aria-atomic="true"
                    className="mt-1 text-muted-foreground"
                  >
                    {viewModel.emptyState.detail}
                  </div>
                </div>
                {viewModel.emptyState.canClearSearch && viewModel.emptyState.actionLabel ? (
                  <Button
                    id={viewModel.emptyState.actionId ?? undefined}
                    type="button"
                    variant="outline"
                    size="sm"
                    className="shrink-0"
                    aria-label={viewModel.emptyState.actionAriaLabel ?? viewModel.emptyState.actionLabel}
                    onClick={() => {
                      setQuery("");
                      searchInputRef.current?.focus();
                    }}
                  >
                    <X className="h-3.5 w-3.5" aria-hidden="true" />
                    {viewModel.emptyState.actionLabel}
                  </Button>
                ) : null}
              </div>
            </section>
          ) : null}
          {viewModel.commandGroups.map((group) => (
            <div key={group.kind} className="command-palette-group">
              <div className="command-palette-group-label" aria-label={group.ariaLabel}>
                <span>{group.label}</span>
                <span className="font-mono text-[10px] text-muted-foreground">{group.countLabel}</span>
              </div>
              <div className="grid gap-1">
                {group.items.map((item) => (
                  <Link
                    key={item.id}
                    ref={item.id === viewModel.initialFocusItemId ? initialCommandRef : undefined}
                    to={item.route}
                    data-command-id={item.id}
                    data-command-list-item="true"
                    aria-label={item.ariaLabel}
                    aria-current={item.active ? "page" : undefined}
                    className={cn(
                      "command-palette-command rounded-md border px-3 py-2.5 text-sm transition-colors",
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
                        <span className="block font-semibold leading-snug">{item.commandLabel}</span>
                        <span className="mt-0.5 block text-xs text-muted-foreground">{item.description}</span>
                      </span>
                      <span className="flex shrink-0 flex-col items-end gap-1.5">
                        <span className="command-palette-route" aria-label={`Route ${item.routeLabel}`}>
                          {item.routeLabel}
                        </span>
                        {(item.active || item.statusVisible) && (
                          <span
                            className={cn(
                              "command-palette-status",
                              `command-palette-status-${item.statusTone}`
                            )}
                          >
                            {item.statusLabel}
                          </span>
                        )}
                      </span>
                    </span>
                  </Link>
                ))}
              </div>
            </div>
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

function getCommandPaletteCommandElements(dialog: HTMLDivElement | null): HTMLAnchorElement[] {
  if (!dialog) {
    return [];
  }

  return Array.from(dialog.querySelectorAll<HTMLAnchorElement>("[data-command-list-item='true']"));
}

function getCommandPaletteFocusTarget(
  searchInput: HTMLInputElement | null,
  activeElement: Element | null
): CommandPaletteFocusTarget {
  if (activeElement === searchInput) {
    return "search";
  }

  return activeElement?.classList.contains("command-palette-command") ? "command" : "other";
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

function activateFirstCommandPaletteCommand(dialog: HTMLDivElement | null) {
  getCommandPaletteCommandElements(dialog)[0]?.click();
}

function focusCommandPaletteCommand(
  dialog: HTMLDivElement | null,
  activeElement: Element | null,
  command: "focus-next-command" | "focus-previous-command"
) {
  const commands = getCommandPaletteCommandElements(dialog);
  if (commands.length === 0) {
    return;
  }

  const currentIndex = activeElement ? commands.findIndex((element) => element === activeElement) : -1;
  const nextIndex =
    command === "focus-next-command"
      ? currentIndex < 0
        ? 0
        : (currentIndex + 1) % commands.length
      : currentIndex < 0
        ? commands.length - 1
        : (currentIndex - 1 + commands.length) % commands.length;

  commands[nextIndex]?.focus();
}
