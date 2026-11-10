import { Link } from "react-router-dom";
import type { AppShellAttentionSignal } from "@/app-shell.attention";
import { appendOperatingScopeToRoute, type AppShellOperatingScopeState } from "@/app-shell.operating-scope";
import type { StatusRowFact } from "@/components/meridian/status-row";
import { Button } from "@/components/ui/button";

/**
 * A shell attention signal as a status-row fact, with its recovery link. Page status rows and
 * Today both build their shell facts here, so a signal reads the same wherever it is listed.
 */
export function buildAttentionSignalFact(
  signal: AppShellAttentionSignal,
  operatingScope: AppShellOperatingScopeState | null
): StatusRowFact {
  // A decision's route already carries the operating scope; source links get it added here.
  const href = signal.href && signal.kind === "source" && operatingScope
    ? appendOperatingScopeToRoute(signal.href, operatingScope)
    : signal.href;

  return {
    id: `shell-${signal.id}`,
    label: signal.label,
    value: signal.value,
    detail: signal.detail,
    tone: signal.tone,
    action: href && signal.actionLabel ? (
      <Button asChild variant="outline" size="sm">
        <Link to={href}>{signal.actionLabel}</Link>
      </Button>
    ) : null
  };
}
