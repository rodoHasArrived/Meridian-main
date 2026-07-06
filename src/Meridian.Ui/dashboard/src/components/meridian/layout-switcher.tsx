import { useMemo, useRef, useState } from "react";
import { Check, LayoutGrid, Plus, Trash2, Users } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Popover } from "@/components/ui/popover";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { useToast } from "@/components/ui/toast";
import { COMPANION_PANES } from "@/lib/companion-pane/pane-window";
import { readOpenCompanionPaneIds } from "@/lib/companion-pane/open-registry";
import { DEFAULT_DENSITY, readStoredDensity, type Density } from "@/lib/density";
import {
  MAX_LAYOUT_DESCRIPTION_LENGTH,
  MAX_LAYOUT_NAME_LENGTH,
  buildLayoutRestorePlan,
  captureLayoutState,
  createUserLayout,
  generateLayoutId,
  listTeamLayouts,
  readUserLayouts,
  removeUserLayout,
  upsertUserLayout,
  type LayoutRestorePlan,
  type SavedLayout
} from "@/lib/saved-layouts";
import type { AppShellOperatingScopeInput } from "@/app-shell.operating-scope";

export interface LayoutSwitcherProps {
  /** Current route (pathname + search) to snapshot when saving a layout. */
  route: string;
  operatingScope: AppShellOperatingScopeInput;
  onRestore: (plan: LayoutRestorePlan) => void;
  /** Test seams. */
  storage?: Storage | null;
  readDensity?: () => Density;
  readPanes?: () => string[];
  generateId?: () => string;
}

/**
 * Header workspace switcher. Lists the operator's saved layouts alongside
 * read-only shared team presets, and captures the current arrangement — route,
 * operating scope, density, and popped-out companion panes — as a new named
 * layout. Restoring hands a plan back to the shell, which replays it.
 */
export function LayoutSwitcher({
  route,
  operatingScope,
  onRestore,
  storage,
  readDensity = () => readStoredDensity(undefined, storage),
  readPanes = () => readOpenCompanionPaneIds(storage),
  generateId = generateLayoutId
}: LayoutSwitcherProps) {
  const toast = useToast();
  const anchorRef = useRef<HTMLButtonElement>(null);
  const [open, setOpen] = useState(false);
  const [saveOpen, setSaveOpen] = useState(false);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [userLayouts, setUserLayouts] = useState<SavedLayout[]>(() => readUserLayouts(storage));
  const teamLayouts = useMemo(() => listTeamLayouts(), []);

  const handleRestore = (layout: SavedLayout) => {
    onRestore(buildLayoutRestorePlan(layout));
    setOpen(false);
    toast.success(`Restored ${layout.name}.`, describeLayoutState(layout));
  };

  const handleDelete = (layout: SavedLayout) => {
    setUserLayouts(removeUserLayout(layout.id, storage));
    toast.info(`Removed ${layout.name}.`);
  };

  const handleSave = () => {
    const trimmed = name.trim();
    if (!trimmed) {
      return;
    }
    const layout = createUserLayout({
      id: generateId(),
      name: trimmed,
      description,
      state: captureLayoutState({
        route,
        operatingScope,
        density: readDensity(),
        panes: readPanes()
      })
    });
    if (!layout) {
      return;
    }
    setUserLayouts(upsertUserLayout(layout, storage));
    toast.success(`Saved layout ${layout.name}.`, describeLayoutState(layout));
    setSaveOpen(false);
    setName("");
    setDescription("");
  };

  return (
    <>
      <Button
        ref={anchorRef}
        aria-label="Saved workspace layouts"
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={() => setOpen((current) => !current)}
        size="sm"
        title="Saved workspace layouts"
        type="button"
        variant="ghost"
      >
        <LayoutGrid aria-hidden="true" className="size-4" />
        <span className="hidden sm:inline">Workspaces</span>
      </Button>

      <Popover
        open={open}
        onClose={() => setOpen(false)}
        anchorEl={anchorRef.current}
        position="bottom"
        className="min-w-[260px]"
      >
        <div className="max-h-[70vh] overflow-y-auto" role="menu" aria-label="Saved workspace layouts">
          <LayoutSection title="Your layouts" icon={<LayoutGrid aria-hidden="true" className="size-3.5" />}>
            {userLayouts.length === 0 ? (
              <p className="px-2 py-2 text-xs text-muted-foreground">
                No saved layouts yet. Arrange your workspace, then save it below.
              </p>
            ) : (
              userLayouts.map((layout) => (
                <LayoutRow
                  key={layout.id}
                  layout={layout}
                  onRestore={() => handleRestore(layout)}
                  onDelete={() => handleDelete(layout)}
                />
              ))
            )}
          </LayoutSection>

          <LayoutSection
            title="Shared team presets"
            icon={<Users aria-hidden="true" className="size-3.5" />}
          >
            {teamLayouts.map((layout) => (
              <LayoutRow key={layout.id} layout={layout} onRestore={() => handleRestore(layout)} />
            ))}
          </LayoutSection>

          <div className="mt-1 border-t border-border pt-2">
            <button
              type="button"
              role="menuitem"
              className="flex w-full items-center gap-2 rounded-[2px] px-2 py-2 text-left text-sm font-medium text-foreground transition-colors hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
              onClick={() => {
                setOpen(false);
                setSaveOpen(true);
              }}
            >
              <Plus aria-hidden="true" className="size-4" />
              Save current layout…
            </button>
          </div>
        </div>
      </Popover>

      <Dialog open={saveOpen} onOpenChange={setSaveOpen}>
        <DialogContent aria-labelledby="save-layout-title" aria-describedby="save-layout-description">
          <DialogHeader>
            <DialogTitle id="save-layout-title">Save this layout</DialogTitle>
            <DialogDescription id="save-layout-description">
              Captures the current route, operating scope, display density, and any popped-out companion
              panes as a named layout you can restore in one click.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-3 pt-2">
            <div>
              <label className="text-sm font-medium text-foreground" htmlFor="save-layout-name">
                Layout name
              </label>
              <Input
                id="save-layout-name"
                maxLength={MAX_LAYOUT_NAME_LENGTH}
                onChange={(event) => setName(event.target.value)}
                placeholder="Month-End Close"
                value={name}
              />
            </div>
            <div>
              <label className="text-sm font-medium text-foreground" htmlFor="save-layout-description">
                Description (optional)
              </label>
              <Input
                id="save-layout-description"
                maxLength={MAX_LAYOUT_DESCRIPTION_LENGTH}
                onChange={(event) => setDescription(event.target.value)}
                placeholder="What this arrangement is for"
                value={description}
              />
            </div>
            <div className="flex justify-end gap-3 pt-2">
              <Button onClick={() => setSaveOpen(false)} size="sm" type="button" variant="outline">
                Cancel
              </Button>
              <Button disabled={!name.trim()} onClick={handleSave} size="sm" type="button">
                Save layout
              </Button>
            </div>
          </div>
        </DialogContent>
      </Dialog>
    </>
  );
}

function LayoutSection({
  title,
  icon,
  children
}: {
  title: string;
  icon: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <div className="mb-1">
      <div className="flex items-center gap-1.5 px-2 py-1 text-[11px] font-semibold uppercase tracking-[0.08em] text-muted-foreground">
        {icon}
        {title}
      </div>
      {children}
    </div>
  );
}

function LayoutRow({
  layout,
  onRestore,
  onDelete
}: {
  layout: SavedLayout;
  onRestore: () => void;
  onDelete?: () => void;
}) {
  return (
    <div className="group flex items-center gap-1 rounded-[2px] hover:bg-secondary">
      <button
        type="button"
        role="menuitem"
        className="flex min-w-0 flex-1 flex-col gap-0.5 rounded-[2px] px-2 py-1.5 text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
        onClick={onRestore}
        title={`Restore ${layout.name}`}
      >
        <span className="flex items-center gap-1.5 truncate text-sm font-medium text-foreground">
          <Check aria-hidden="true" className="size-3.5 shrink-0 opacity-0" />
          {layout.name}
        </span>
        {layout.description ? (
          <span className="truncate pl-5 text-xs text-muted-foreground">{layout.description}</span>
        ) : null}
      </button>
      {onDelete ? (
        <button
          type="button"
          className="mr-1 shrink-0 rounded-[2px] p-1 text-muted-foreground opacity-0 transition-opacity hover:text-danger focus-visible:opacity-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 group-hover:opacity-100"
          onClick={onDelete}
          aria-label={`Delete layout ${layout.name}`}
          title={`Delete ${layout.name}`}
        >
          <Trash2 aria-hidden="true" className="size-3.5" />
        </button>
      ) : null}
    </div>
  );
}

function describeLayoutState(layout: SavedLayout): string {
  const paneCount = layout.state.panes.length;
  const paneLabel = paneCount === 0
    ? "no popped-out panes"
    : layout.state.panes.map((id) => COMPANION_PANES[id]?.title ?? id).join(", ");
  const density = layout.state.density === DEFAULT_DENSITY ? "default density" : `${layout.state.density} density`;
  return `${density} · ${paneLabel}`;
}
