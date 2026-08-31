import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle
} from "@/components/ui/dialog";
import type { ApiErrorDisplay } from "@/lib/api-errors";

export interface SettingsMutationConfirmation {
  id: string;
  title: string;
  description: string;
  confirmLabel: string;
  confirmAriaLabel: string;
  destructive?: boolean;
  run: () => Promise<void> | void;
}

export function SettingsMutationConfirmDialog({
  confirmation,
  busy,
  error = null,
  onCancel,
  onConfirm
}: {
  confirmation: SettingsMutationConfirmation | null;
  busy: boolean;
  error?: ApiErrorDisplay | null;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const titleId = confirmation ? `settings-confirmation-${confirmation.id}-title` : "settings-confirmation-title";
  const descriptionId = confirmation ? `settings-confirmation-${confirmation.id}-description` : "settings-confirmation-description";

  return (
    <Dialog open={Boolean(confirmation)} onOpenChange={(open) => { if (!open && !busy) onCancel(); }}>
      {confirmation ? (
        <DialogContent className="max-w-md" aria-labelledby={titleId} aria-describedby={descriptionId}>
          <DialogHeader>
            <DialogTitle id={titleId}>{confirmation.title}</DialogTitle>
            <DialogDescription id={descriptionId}>{confirmation.description}</DialogDescription>
          </DialogHeader>
          {error ? (
            <div role="alert" className="rounded-[2px] border border-danger/35 bg-danger/10 px-3 py-2.5 text-sm text-danger">
              <div>{error.summary}</div>
              {error.details.length > 0 ? (
                <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                  {error.details.map((detail) => <li key={detail}>{detail}</li>)}
                </ul>
              ) : null}
            </div>
          ) : null}
          <div className="flex flex-wrap justify-end gap-2">
            <Button type="button" variant="outline" onClick={onCancel} disabled={busy}>
              Cancel
            </Button>
            <Button
              type="button"
              variant={confirmation.destructive ? "destructive" : "default"}
              onClick={onConfirm}
              disabled={busy}
              busy={busy}
              busyLabel="Confirming settings change"
              aria-label={confirmation.confirmAriaLabel}
            >
              {confirmation.confirmLabel}
            </Button>
          </div>
        </DialogContent>
      ) : null}
    </Dialog>
  );
}
