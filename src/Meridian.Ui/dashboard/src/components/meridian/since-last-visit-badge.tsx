import { Badge } from "@/components/ui/badge";
import type { SinceLastVisitMark } from "@/lib/since-last-visit";

/** "New" or "Updated" beside a row that changed after the viewer's previous visit to the surface. */
export function SinceLastVisitBadge({ mark }: { mark: SinceLastVisitMark }) {
  return (
    <Badge variant="outline" className="shrink-0" data-since-last-visit={mark}>
      {mark === "new" ? "New" : "Updated"}
      <span className="sr-only"> since your last visit</span>
    </Badge>
  );
}
