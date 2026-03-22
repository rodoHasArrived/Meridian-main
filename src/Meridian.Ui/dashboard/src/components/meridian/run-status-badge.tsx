import { Badge } from "@/components/ui/badge";
import type { ResearchRunRecord } from "@/types";

interface RunStatusBadgeProps {
  status: ResearchRunRecord["status"];
  mode?: ResearchRunRecord["mode"];
}

export function RunStatusBadge({ status, mode }: RunStatusBadgeProps) {
  const statusVariant =
    status === "Completed" ? "success" : status === "Needs Review" ? "warning" : status === "Running" ? "live" : "outline";

  return (
    <div className="flex flex-wrap items-center gap-2">
      <Badge variant={statusVariant}>{status}</Badge>
      {mode ? <Badge variant={mode}>{mode.toUpperCase()}</Badge> : null}
    </div>
  );
}
