import { useState } from "react";
import { useLocation } from "react-router-dom";
import { BookmarkPlus } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { useToast } from "@/components/ui/toast";
import { saveWorkflowPreset } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { findWorkflowActionForRoute } from "@/lib/workflow-route-match";
import { VIEW_STATE_QUERY_KEY } from "@/lib/view-state-envelope";
import type { WorkflowLibrary, WorkflowPreset } from "@/types";

const MAX_SAVE_VIEW_NAME_LENGTH = 120;

interface SaveViewButtonProps {
  workflowLibrary: WorkflowLibrary | null;
  workflowError?: string | null;
  onPresetSaved: (preset: WorkflowPreset) => void;
}

/**
 * Saves the current view as a workflow preset. The preset carries the current
 * `view` state token (when present) so reopening it restores the exact view;
 * scope-only views are still valid saves. Enabled only when a workflow action
 * owns the current route — presets must attach to catalog workflows.
 */
export function SaveViewButton({ workflowLibrary, workflowError, onPresetSaved }: SaveViewButtonProps) {
  const { pathname, search } = useLocation();
  const toast = useToast();
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [saving, setSaving] = useState(false);

  const routeMatch = findWorkflowActionForRoute(workflowLibrary, pathname);
  const disabledReason = workflowError
    ? "Workflow library is unavailable."
    : !routeMatch
      ? "No workflow action owns this route yet."
      : null;

  const handleSave = async () => {
    if (!routeMatch || saving) {
      return;
    }

    const trimmedName = name.trim();
    if (!trimmedName) {
      return;
    }

    setSaving(true);
    try {
      const viewStateEnvelope = new URLSearchParams(search).get(VIEW_STATE_QUERY_KEY);
      const preset = await saveWorkflowPreset({
        name: trimmedName,
        description: description.trim() || null,
        workflowId: routeMatch.workflow.workflowId,
        actionId: routeMatch.action.actionId,
        tags: [],
        isPinned: false,
        viewStateEnvelope
      });
      onPresetSaved(preset);
      toast.success(`Saved view ${preset.name}.`, "Find it in the command palette presets.");
      setOpen(false);
      setName("");
      setDescription("");
    } catch (error) {
      const display = describeApiError(error, "Saving the view failed.");
      toast.danger(display.summary, display.details.join(" ") || undefined);
    } finally {
      setSaving(false);
    }
  };

  return (
    <>
      <Button
        aria-label="Save this view as a preset"
        disabled={Boolean(disabledReason)}
        disabledReason={disabledReason ?? undefined}
        onClick={() => setOpen(true)}
        size="sm"
        title="Save this view as a preset"
        type="button"
        variant="ghost"
      >
        <BookmarkPlus aria-hidden="true" className="size-4" />
      </Button>
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent aria-labelledby="save-view-title" aria-describedby="save-view-description">
          <DialogHeader>
            <DialogTitle id="save-view-title">Save this view</DialogTitle>
            <DialogDescription id="save-view-description">
              Stores the current route{routeMatch ? ` under ${routeMatch.workflow.title}` : ""} as a
              team-visible preset, including any captured view state.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-3 pt-2">
            <div>
              <label className="text-sm font-medium text-foreground" htmlFor="save-view-name">
                View name
              </label>
              <Input
                id="save-view-name"
                maxLength={MAX_SAVE_VIEW_NAME_LENGTH}
                onChange={(event) => setName(event.target.value)}
                placeholder="Month-end breaks — Fund A"
                value={name}
              />
            </div>
            <div>
              <label className="text-sm font-medium text-foreground" htmlFor="save-view-description-input">
                Description (optional)
              </label>
              <Input
                id="save-view-description-input"
                onChange={(event) => setDescription(event.target.value)}
                placeholder="What a teammate lands on when opening this view"
                value={description}
              />
            </div>
            <div className="flex justify-end gap-3 pt-2">
              <Button onClick={() => setOpen(false)} size="sm" type="button" variant="outline">
                Cancel
              </Button>
              <Button
                busy={saving}
                busyLabel="Saving view"
                disabled={!name.trim() || saving}
                onClick={() => void handleSave()}
                size="sm"
                type="button"
              >
                Save view
              </Button>
            </div>
          </div>
        </DialogContent>
      </Dialog>
    </>
  );
}
