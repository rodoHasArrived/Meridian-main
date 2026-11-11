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
import { GLOBAL_KEY_BINDINGS } from "@/lib/keymap";

export interface KeyboardShortcutsSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/** Lists the workstation-global shortcuts from the shared keymap registry. */
export function KeyboardShortcutsSheet({ open, onOpenChange }: KeyboardShortcutsSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent aria-labelledby="keyboard-shortcuts-title" aria-describedby="keyboard-shortcuts-summary" side="right">
        <SheetHeader>
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              <SheetTitle id="keyboard-shortcuts-title">Keyboard shortcuts</SheetTitle>
              <SheetDescription id="keyboard-shortcuts-summary">Available from anywhere in the workstation.</SheetDescription>
            </div>
            <SheetCloseButton onClick={() => onOpenChange(false)} />
          </div>
        </SheetHeader>
        <SheetBody>
          <ul className="divide-y divide-border/60">
            {GLOBAL_KEY_BINDINGS.map((binding) => (
              <li key={binding.id} className="flex items-center justify-between gap-3 py-2.5 text-sm">
                <span>{binding.label}</span>
                <Kbd keys={binding.keys.split("+").map((part) => (part === "Mod" ? "Ctrl / ⌘" : part))} />
              </li>
            ))}
          </ul>
        </SheetBody>
      </SheetContent>
    </Sheet>
  );
}
