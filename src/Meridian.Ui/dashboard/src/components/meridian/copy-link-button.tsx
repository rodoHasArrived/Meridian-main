import { Link2 } from "lucide-react";
import type { AppShellOperatingScopeInput } from "@/app-shell.operating-scope";
import { Button } from "@/components/ui/button";
import { useToast } from "@/components/ui/toast";
import { copyTextToClipboard } from "@/lib/csv";

const SHAREABLE_SCOPE_FIELDS = ["symbol", "fundAccountId", "runId", "provider", "from", "to", "date", "asOf"] as const;

/**
 * The current address plus every operating-scope dimension it does not already carry (C1). A
 * route only writes the dimensions it filters by into its URL, so without this a link from
 * Settings dropped the symbol and dates the sender had in scope; the receiver's shell reads every
 * dimension from the link and restores the same scope in a fresh browser.
 */
export function buildShareableLink(href: string, operatingScope: AppShellOperatingScopeInput | undefined): string {
  if (!operatingScope) {
    return href;
  }
  const url = new URL(href);
  for (const field of SHAREABLE_SCOPE_FIELDS) {
    const value = operatingScope[field];
    if (value && !url.searchParams.has(field)) {
      url.searchParams.set(field, value);
    }
  }
  return url.toString();
}

/**
 * Copies a link to this exact view: the URL (task mode, `view` table state, route scope) plus
 * the rest of the operator's scope, so a teammate opening it sees what the sender saw.
 */
export function CopyLinkButton({ operatingScope }: { operatingScope?: AppShellOperatingScopeInput } = {}) {
  const toast = useToast();

  const handleCopy = async () => {
    const copied = await copyTextToClipboard(buildShareableLink(window.location.href, operatingScope));
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
      <span>Copy link</span>
    </Button>
  );
}
