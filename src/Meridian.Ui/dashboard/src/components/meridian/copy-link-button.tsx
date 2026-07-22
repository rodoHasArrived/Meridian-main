import { Link2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useToast } from "@/components/ui/toast";
import { copyTextToClipboard } from "@/lib/csv";

/**
 * Copies the current browser URL so an operator can hand a teammate this exact
 * view. Scope, symbol, task mode, and any `view` state param are already
 * URL-borne, so the address itself is the shareable artifact.
 */
export function CopyLinkButton() {
  const toast = useToast();

  const handleCopy = async () => {
    const copied = await copyTextToClipboard(window.location.href);
    if (copied) {
      toast.success("Link copied", "Paste it to share this exact view.");
    } else {
      toast.danger("Copy failed", "Copy the address from the browser bar instead.");
    }
  };

  return (
    <Button
      aria-label="Copy link to this view"
      onClick={() => void handleCopy()}
      size="sm"
      title="Copy link to this view"
      type="button"
      variant="ghost"
    >
      <Link2 aria-hidden="true" className="size-4" />
    </Button>
  );
}
