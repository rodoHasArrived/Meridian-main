import { useEffect, useRef } from "react";
import { Link, useLocation } from "react-router-dom";
import { X } from "lucide-react";
import { buildCommandPaletteViewModel } from "@/components/meridian/command-palette.view-model";
import { cn } from "@/lib/utils";

interface CommandPaletteProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function CommandPalette({ open, onOpenChange }: CommandPaletteProps) {
  const { pathname } = useLocation();
  const dialogRef = useRef<HTMLDivElement>(null);
  const initialCommandRef = useRef<HTMLAnchorElement | null>(null);
  const viewModel = buildCommandPaletteViewModel(pathname);

  useEffect(() => {
    if (!open) {
      return;
    }

    (initialCommandRef.current ?? dialogRef.current)?.focus();

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        onOpenChange(false);
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [onOpenChange, open]);

  if (!open) {
    return null;
  }

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center bg-background/70 px-4 py-24 backdrop-blur-sm">
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="command-palette-title"
        aria-describedby="command-palette-route-context"
        tabIndex={-1}
        className="w-full max-w-xl rounded-lg border border-border bg-card p-4 shadow-float outline-none"
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
            className="inline-flex h-9 w-9 items-center justify-center rounded-md text-muted-foreground hover:bg-secondary hover:text-foreground"
            onClick={() => onOpenChange(false)}
            aria-label="Close command palette"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
        <nav className="mt-3 grid gap-2" aria-label={viewModel.commandListLabel}>
          <div className="eyebrow-label">{viewModel.itemCountLabel}</div>
          {viewModel.emptyState ? (
            <div className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm">
              <div className="font-semibold">{viewModel.emptyState.title}</div>
              <div className="mt-1 text-muted-foreground">{viewModel.emptyState.detail}</div>
            </div>
          ) : null}
          {viewModel.items.map((item) => (
            <Link
              key={item.id}
              ref={item.id === viewModel.initialFocusItemId ? initialCommandRef : undefined}
              to={item.route}
              data-command-id={item.id}
              aria-label={item.ariaLabel}
              aria-current={item.active ? "page" : undefined}
              className={cn(
                "rounded-md border px-3 py-3 text-sm transition-colors",
                item.active
                  ? "border-primary/35 bg-primary/10 text-foreground"
                  : "border-transparent hover:border-border/70 hover:bg-secondary/70"
              )}
              onClick={() => onOpenChange(false)}
            >
              <span className="flex items-start justify-between gap-3">
                <span>
                  <span className="block font-semibold">{item.commandLabel}</span>
                  <span className="mt-1 block text-muted-foreground">{item.description}</span>
                </span>
                <span className="shrink-0 rounded-sm border border-border/70 bg-secondary/55 px-2 py-1 font-mono text-[10px] uppercase tracking-[0.12em] text-muted-foreground">
                  {item.statusLabel}
                </span>
              </span>
            </Link>
          ))}
        </nav>
      </div>
    </div>
  );
}
