import { Children, cloneElement, forwardRef, isValidElement } from "react";
import { Loader2 } from "lucide-react";
import { cn } from "@/lib/utils";
import { buildButtonCommandViewModel } from "@/components/ui/button.view-model";

/**
 * Primary interactive element for user actions.
 *
 * **Variants:** `"default"` (cyan primary), `"secondary"` (muted surface),
 * `"outline"` (transparent with border), `"ghost"` (text-only), `"destructive"` (danger-toned).
 *
 * **Sizes:** `"sm"` (32 px), `"default"` (36 px), `"lg"` (44 px, for hero/full-width CTAs),
 * `"icon"` (36×36 px square, no horizontal padding — use for icon-only buttons).
 *
 * **Busy state:** pass `busy` to replace the label with a spinner and set `aria-busy`.
 * Pair with `busyLabel` to provide an accessible announcement (e.g. `"Saving…"`).
 *
 * **Disabled reason:** pass `disabledReason` to surface an explanatory tooltip when the
 * button is disabled, without changing visible text.
 *
 * **AsChild:** set `asChild` to render the button styles and accessibility props on a child
 * `<Link>` or `<a>` instead of a native `<button>`. The child must accept `className`.
 *
 * @example
 * <Button size="sm" variant="outline" busy={isLoading} busyLabel="Saving…">
 *   Save
 * </Button>
 */
export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  asChild?: boolean;
  variant?: "default" | "secondary" | "outline" | "ghost" | "destructive";
  /** `"sm"` — compact (32 px min-height). `"default"` — standard (36 px). `"lg"` — full-width CTA or hero action (44 px). `"icon"` — square 36×36 px, no padding, for icon-only buttons. */
  size?: "sm" | "default" | "lg" | "icon";
  busy?: boolean;
  busyLabel?: string | null;
  disabledReason?: string | null;
}

type ButtonChildProps = React.HTMLAttributes<HTMLElement> & {
  className?: string;
  "aria-busy"?: true;
  "aria-disabled"?: true;
  tabIndex?: number;
  title?: string;
};

const variantClasses: Record<NonNullable<ButtonProps["variant"]>, string> = {
  default: "border-primary bg-primary text-primary-foreground hover:bg-primary/85 active:bg-[#255B75]",
  secondary: "border-border bg-secondary text-secondary-foreground hover:border-[#ADB8C4] hover:bg-[#EAEEF3] active:bg-[#D7E5F1]",
  outline: "border-border bg-transparent text-foreground hover:border-[#ADB8C4] hover:bg-[#EAEEF3] active:bg-[#D7E5F1]",
  ghost: "border-transparent bg-transparent text-muted-foreground hover:bg-[#EAEEF3] hover:text-foreground active:bg-[#D7E5F1]",
  destructive: "border-danger/60 bg-danger/10 text-danger hover:bg-danger/15"
};

const sizeClasses: Record<NonNullable<ButtonProps["size"]>, string> = {
  sm: "min-h-8 px-3 py-1.5 text-xs",
  default: "min-h-9 px-4 py-2 text-sm",
  lg: "min-h-11 px-6 py-3 text-base",
  icon: "h-9 w-9 p-0 text-sm"
};

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  ({
    asChild = false,
    children,
    className,
    variant = "default",
    size = "default",
    busy = false,
    busyLabel = null,
    disabled = false,
    disabledReason = null,
    title,
    onClick,
    tabIndex,
    ...props
  }, ref) => {
    const vm = buildButtonCommandViewModel({ disabled, busy, busyLabel, disabledReason, title });
    const classes = cn(
      "inline-flex items-center justify-center gap-2 rounded-[2px] border font-semibold transition-[background-color,border-color,color] duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-50",
      variantClasses[variant],
      sizeClasses[size],
      vm.disabled && asChild && "pointer-events-none opacity-50",
      className
    );

    if (asChild && isValidElement(children)) {
      const child = Children.only(children) as React.ReactElement<ButtonChildProps>;
      const childOnClick = child.props.onClick;
      const handleClick: React.MouseEventHandler<HTMLElement> = (event) => {
        if (vm.disabled) {
          event.preventDefault();
          event.stopPropagation();
          return;
        }

        childOnClick?.(event);

        if (!event.defaultPrevented) {
          onClick?.(event as unknown as React.MouseEvent<HTMLButtonElement>);
        }
      };

      return cloneElement(child, {
        ...props,
        className: cn(classes, child.props.className),
        "aria-busy": vm.ariaBusy,
        "aria-disabled": vm.ariaDisabled,
        tabIndex: vm.asChildTabIndex ?? tabIndex ?? child.props.tabIndex,
        title: vm.title ?? child.props.title,
        onClick: handleClick
      });
    }

    return (
      <button
        ref={ref}
        className={classes}
        disabled={vm.disabled}
        aria-busy={vm.ariaBusy}
        onClick={onClick}
        tabIndex={tabIndex}
        title={vm.title}
        {...props}
      >
        {vm.showBusyIndicator && <Loader2 className="h-4 w-4 animate-spin" aria-hidden={vm.iconAriaHidden} />}
        {vm.displayBusyLabel ?? children}
      </button>
    );
  }
);

Button.displayName = "Button";
