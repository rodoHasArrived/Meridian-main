import { forwardRef } from "react";
import { usesTrackedCaps } from "@/design-system/label-case";
import { cn } from "@/lib/utils";

export type LabelProps = React.LabelHTMLAttributes<HTMLLabelElement>;

// Label tier: tracked caps for one or two words, sentence case for a longer phrase.
export const Label = forwardRef<HTMLLabelElement, LabelProps>(({ className, children, ...props }, ref) => (
  <label
    ref={ref}
    className={cn(
      "text-xs font-medium text-muted-foreground",
      usesTrackedCaps(children) && "uppercase tracking-[0.14em]",
      className,
    )}
    {...props}
  >
    {children}
  </label>
));

Label.displayName = "Label";
