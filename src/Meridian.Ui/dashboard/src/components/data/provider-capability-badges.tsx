import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

const CAPABILITY_COLORS: Record<string, string> = {
  Streaming: "bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300",
  Backfill: "bg-purple-100 text-purple-800 dark:bg-purple-900/30 dark:text-purple-300",
  SymbolSearch: "bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-300",
  Brokerage: "bg-amber-100 text-amber-800 dark:bg-amber-900/30 dark:text-amber-300",
};

interface Props {
  capabilities: string[];
  className?: string;
}

export function ProviderCapabilityBadges({ capabilities, className }: Props) {
  return (
    <div className={cn("flex flex-wrap gap-1", className)}>
      {capabilities.map(cap => (
        <Badge
          key={cap}
          variant="outline"
          className={cn(CAPABILITY_COLORS[cap])}
        >
          {cap}
        </Badge>
      ))}
    </div>
  );
}
