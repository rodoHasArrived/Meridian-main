import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { Kbd } from "@/components/ui/kbd";
import {
  Sheet,
  SheetBody,
  SheetCloseButton,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle
} from "@/components/ui/sheet";
import {
  comboFromKeyEvent,
  formatKeyCombo,
  QUEUE_KEY_BINDINGS,
  type GlobalKeyBindingId,
  resetKeyBindingOverrides,
  setKeyBindingOverride,
  useGlobalKeyBindings
} from "@/lib/keymap";

export interface KeyboardShortcutsSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

function comboParts(keys: string): string[] {
  return keys.split("+").map((part) => (part === "Mod" ? "Ctrl / ⌘" : part));
}

/**
 * Lists the workstation-global shortcuts from the shared keymap registry and lets the operator
 * rebind them for this browser. While a row is capturing, the next chord is taken from a
 * window capture-phase listener that stops it reaching the shell's global handler; Esc (which
 * the sheet turns into a close request) cancels the capture instead of closing the sheet.
 */
export function KeyboardShortcutsSheet({ open, onOpenChange }: KeyboardShortcutsSheetProps) {
  const bindings = useGlobalKeyBindings();
  const [capturingId, setCapturingId] = useState<GlobalKeyBindingId | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const anyOverridden = bindings.some((binding) => binding.overridden);

  useEffect(() => {
    if (!open) {
      setCapturingId(null);
      setStatus(null);
    }
  }, [open]);

  useEffect(() => {
    if (capturingId === null || typeof window === "undefined") {
      return undefined;
    }

    const binding = bindings.find((entry) => entry.id === capturingId);
    const handleKeyDown = (event: KeyboardEvent) => {
      // Esc and Tab belong to the sheet (cancel via its close request, focus order).
      if (event.key === "Escape" || event.key === "Tab") {
        return;
      }
      event.preventDefault();
      event.stopPropagation();

      const combo = comboFromKeyEvent(event);
      if (combo === null) {
        return; // Only modifiers so far: keep waiting for the rest of the chord.
      }

      const refusal = setKeyBindingOverride(capturingId, combo);
      setCapturingId(null);
      setStatus(refusal ?? `${binding?.label ?? "Shortcut"} is now ${formatKeyCombo(combo)}.`);
    };

    window.addEventListener("keydown", handleKeyDown, true);
    return () => window.removeEventListener("keydown", handleKeyDown, true);
  }, [bindings, capturingId]);

  const handleOpenChange = (next: boolean) => {
    if (!next && capturingId !== null) {
      setCapturingId(null);
      setStatus("Change cancelled.");
      return;
    }
    onOpenChange(next);
  };

  return (
    <Sheet open={open} onOpenChange={handleOpenChange}>
      <SheetContent aria-labelledby="keyboard-shortcuts-title" aria-describedby="keyboard-shortcuts-summary" side="right">
        <SheetHeader>
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              <SheetTitle id="keyboard-shortcuts-title">Keyboard shortcuts</SheetTitle>
              <SheetDescription id="keyboard-shortcuts-summary">
                Available from anywhere in the workstation. Changes are saved for this browser.
              </SheetDescription>
            </div>
            <SheetCloseButton onClick={() => handleOpenChange(false)} />
          </div>
        </SheetHeader>
        <SheetBody>
          <ul className="divide-y divide-border/60">
            {bindings.map((binding) => {
              const capturing = binding.id === capturingId;
              return (
                <li key={binding.id} className="flex items-center justify-between gap-3 py-2.5 text-sm" data-testid={`shortcut-row-${binding.id}`}>
                  <span className="min-w-0">
                    {binding.label}
                    {binding.overridden ? (
                      <span className="ml-2 text-xs text-muted-foreground">Default: {formatKeyCombo(binding.defaultKeys)}</span>
                    ) : null}
                  </span>
                  <span className="flex shrink-0 items-center gap-2">
                    {capturing ? (
                      <span className="text-xs text-muted-foreground" aria-hidden="true">Press keys… (Esc to cancel)</span>
                    ) : (
                      <Kbd keys={comboParts(binding.keys)} />
                    )}
                    <Button
                      size="sm"
                      variant="ghost"
                      aria-pressed={capturing}
                      aria-label={capturing ? `Cancel changing ${binding.label}` : `Change shortcut for ${binding.label}`}
                      disabled={capturingId !== null && !capturing}
                      onClick={() => {
                        if (capturing) {
                          setCapturingId(null);
                          setStatus("Change cancelled.");
                        } else {
                          setCapturingId(binding.id);
                          setStatus(`Press the new shortcut for ${binding.label}, or Esc to cancel.`);
                        }
                      }}
                    >
                      {capturing ? "Cancel" : "Change"}
                    </Button>
                    {binding.overridden && !capturing ? (
                      <Button
                        size="sm"
                        variant="ghost"
                        aria-label={`Reset ${binding.label} to ${formatKeyCombo(binding.defaultKeys)}`}
                        disabled={capturingId !== null}
                        onClick={() => {
                          resetKeyBindingOverrides(binding.id);
                          setStatus(`${binding.label} is back to ${formatKeyCombo(binding.defaultKeys)}.`);
                        }}
                      >
                        Reset
                      </Button>
                    ) : null}
                  </span>
                </li>
              );
            })}
          </ul>
          <p role="status" aria-live="polite" className="mt-3 min-h-5 text-xs text-muted-foreground">
            {status}
          </p>
          {anyOverridden ? (
            <Button
              className="mt-2"
              size="sm"
              variant="outline"
              disabled={capturingId !== null}
              onClick={() => {
                resetKeyBindingOverrides();
                setStatus("All shortcuts are back to their defaults.");
              }}
            >
              Reset all to defaults
            </Button>
          ) : null}
          <h3 className="mt-4 text-sm font-semibold">In Today and the shared inbox</h3>
          <p className="text-xs text-muted-foreground">While focus is in the queue. Each action opens its review dialog, which asks for a reason. The reconciliation break queue lists its own keys: press ? there.</p>
          <ul className="divide-y divide-border/60" aria-label="Work queue shortcuts">
            {Object.entries(QUEUE_KEY_BINDINGS).map(([id, binding]) => (
              <li key={id} className="flex items-center justify-between gap-3 py-2.5 text-sm">
                <span>{binding.label}</span>
                <Kbd keys={[binding.keys]} />
              </li>
            ))}
          </ul>
        </SheetBody>
      </SheetContent>
    </Sheet>
  );
}
