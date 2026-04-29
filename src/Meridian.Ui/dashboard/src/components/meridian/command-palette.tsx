import { Link } from "react-router-dom";
import { WORKSPACES, workspacePath } from "@/lib/workspace";

interface CommandPaletteProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function CommandPalette({ open, onOpenChange }: CommandPaletteProps) {
  if (!open) {
    return null;
  }

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center bg-background/70 px-4 py-24">
      <div role="dialog" aria-modal="true" className="w-full max-w-xl rounded-lg border border-border bg-card p-4 shadow-workstation">
        <div className="flex items-center justify-between gap-3 border-b border-border/60 pb-3">
          <div>
            <div className="eyebrow-label">Command Palette</div>
            <h2 className="mt-1 text-lg font-semibold">Open workspace</h2>
          </div>
          <button
            type="button"
            className="rounded-md px-3 py-1 text-sm text-muted-foreground hover:bg-secondary"
            onClick={() => onOpenChange(false)}
          >
            Close
          </button>
        </div>
        <div className="mt-3 grid gap-2">
          {WORKSPACES.map((workspace) => (
            <Link
              key={workspace.key}
              to={workspacePath(workspace.key)}
              className="rounded-md px-3 py-2 text-sm hover:bg-secondary/70"
              onClick={() => onOpenChange(false)}
            >
              <span className="font-semibold">{workspace.label}</span>
              <span className="ml-2 text-muted-foreground">{workspace.description}</span>
            </Link>
          ))}
        </div>
      </div>
    </div>
  );
}
