import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Select } from "@/components/ui/select";
import type { CorporateAction } from "@/types";
import type { AssetResource } from "./asset-detail-resource";

export function corporateActionDateGroup(
  action: CorporateAction,
  today: string
): "Upcoming" | "Past dates" | "Cancelled" | "Dates unavailable" {
  if (action.lifecycleState?.toLowerCase() === "cancelled") return "Cancelled";
  const dates = [action.exDate, action.recordDate, action.payDate].filter((date): date is string =>
    Boolean(date && /^\d{4}-\d{2}-\d{2}/.test(date) && Number.isFinite(Date.parse(date)))
  );
  if (dates.length === 0) return "Dates unavailable";
  return dates.some((date) => date.slice(0, 10) >= today) ? "Upcoming" : "Past dates";
}

export function AssetDetailEvents({
  resource,
  retry,
  compact = false,
  onOpenEvents
}: {
  resource: AssetResource<CorporateAction[]>;
  retry: () => void;
  compact?: boolean;
  onOpenEvents?: () => void;
}) {
  const [filter, setFilter] = useState("All");
  const today = new Date().toISOString().slice(0, 10);
  if (resource.status === "loading")
    return (
      <p role="status" className="text-sm text-muted-foreground">
        Loading corporate actions…
      </p>
    );
  if (resource.status === "error")
    return (
      <div role="alert" className="space-y-2 text-sm">
        <p>Couldn’t load corporate actions. Event availability is unknown.</p>
        <Button size="sm" variant="outline" onClick={retry}>
          Retry corporate actions
        </Button>
      </div>
    );
  if (resource.data.length === 0)
    return <p className="text-sm text-muted-foreground">No corporate actions recorded for this security.</p>;
  const groups = ["Upcoming", "Past dates", "Cancelled", "Dates unavailable"];
  const sorted = [...resource.data].sort((a, b) => {
    const ag = corporateActionDateGroup(a, today);
    const bg = corporateActionDateGroup(b, today);
    const groupOrder = groups.indexOf(ag) - groups.indexOf(bg);
    const dateOrder = a.exDate.localeCompare(b.exDate);
    return groupOrder || (ag === "Upcoming" ? dateOrder : -dateOrder) || a.corpActId.localeCompare(b.corpActId);
  });
  const filtered = sorted.filter((action) => filter === "All" || corporateActionDateGroup(action, today) === filter);
  const visible = compact ? sorted.slice(0, 3) : filtered;
  return (
    <div className="space-y-3">
      {!compact ? (
        <label className="flex items-center gap-2 text-sm">
          Event dates
          <Select
            value={filter}
            onChange={(event) => setFilter(event.target.value)}
          >
            {["All", ...groups].map((group) => (
              <option key={group}>{group}</option>
            ))}
          </Select>
        </label>
      ) : null}
      <p className="text-xs text-muted-foreground">
        Dates grouped as of {today} UTC. A past payment date does not establish payment or posting.
      </p>
      {visible.length === 0 ? (
        <p className="text-sm text-muted-foreground">No events match this date group.</p>
      ) : (
        <ol className="divide-y divide-border border-y border-border" aria-label="Corporate actions">
          {visible.map((action) => (
            <li key={action.corpActId} className="py-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <h4 className="text-sm font-semibold">{action.eventType}</h4>
                <Badge variant="outline">{corporateActionDateGroup(action, today)}</Badge>
              </div>
              <dl className="mt-2 grid grid-cols-3 gap-3 text-xs">
                <div>
                  <dt className="text-muted-foreground">Ex date</dt>
                  <dd className="mt-1 font-mono">{action.exDate || "Not supplied"}</dd>
                </div>
                <div>
                  <dt className="text-muted-foreground">Record date</dt>
                  <dd className="mt-1 font-mono">{action.recordDate ?? "Not supplied"}</dd>
                </div>
                <div>
                  <dt className="text-muted-foreground">Pay date</dt>
                  <dd className="mt-1 font-mono">{action.payDate ?? "Not supplied"}</dd>
                </div>
              </dl>
              {action.dividendPerShare != null ? (
                <p className="mt-2 text-sm tabular-nums">
                  Dividend: {action.dividendPerShare} {action.currency ?? "(currency not supplied)"} per share
                </p>
              ) : null}
              {action.splitRatio != null ? (
                <p className="mt-2 text-sm tabular-nums">Split ratio: {action.splitRatio}</p>
              ) : null}
              {!compact ? (
                <details className="mt-2 text-xs">
                  <summary className="cursor-pointer text-primary focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary">
                    Event evidence
                  </summary>
                  <dl className="mt-2 grid gap-2">
                    <div>
                      <dt className="text-muted-foreground">Source event ID</dt>
                      <dd className="break-all font-mono">{action.corpActId}</dd>
                    </div>
                    <div>
                      <dt className="text-muted-foreground">Recorded lifecycle state</dt>
                      <dd>{action.lifecycleState ?? "Not supplied"}</dd>
                    </div>
                    <div>
                      <dt className="text-muted-foreground">Supersedes event</dt>
                      <dd className="break-all font-mono">{action.supersedesCorpActId ?? "None supplied"}</dd>
                    </div>
                    <div>
                      <dt className="text-muted-foreground">Related security</dt>
                      <dd className="break-all font-mono">
                        {action.newSecurityId ?? action.acquirerSecurityId ?? "None supplied"}
                      </dd>
                    </div>
                  </dl>
                </details>
              ) : null}
            </li>
          ))}
        </ol>
      )}
      {compact && onOpenEvents ? (
        <Button variant="ghost" size="sm" onClick={onOpenEvents}>
          View all {resource.data.length} events and evidence
        </Button>
      ) : null}
    </div>
  );
}
