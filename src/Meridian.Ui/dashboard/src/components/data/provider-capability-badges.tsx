import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

// Concrete language: capabilities read as desaturated semantic trios — a solid hairline
// border over an alpha-10 wash, never a solid fill. Streaming rides the one steel accent;
// backfill purple, symbol-search spruce-green, brokerage ochre. Colors resolve from tokens
// (purple is dark-mode aware via --purple) so no raw palette hexes leak into components.
const CAPABILITY_COLORS: Record<string, string> = {
  Streaming: "border-primary/40 bg-primary/[0.10] text-primary",
  Backfill:
    "border-[color:color-mix(in_srgb,var(--purple)_40%,transparent)] bg-[color:color-mix(in_srgb,var(--purple)_10%,transparent)] text-[color:var(--purple)]",
  SymbolSearch: "border-success/40 bg-success/[0.10] text-success",
  Brokerage: "border-warning/45 bg-warning/[0.11] text-warning",
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
