import { useId, useState, type AriaRole, type FormEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useFundAccountScopeSelection, useOpenScopePicker } from "@/lib/operating-scope/scope-picker-context";
import { cn } from "@/lib/utils";

/**
 * The account step, inline, for a screen that needs exactly one fund account: in place of an
 * empty "choose an account" state the operator picks the account right here. Applying it sets the
 * fund account of the shell's operating scope (keeping every other dimension), which also puts it
 * in the URL, so the choice is the same one the scope bar shows and a copied link carries it.
 *
 * Outside the shell (isolated tests, pop-out panes) there is no scope to set, so `fallback`
 * renders instead.
 */
export function InlineFundAccountPicker({
  title,
  detail,
  normalize,
  invalidMessage = "Not a fund account id this screen can use.",
  variant = "panel",
  role,
  ariaLabel,
  fallback,
  className
}: {
  title: string;
  detail?: string;
  /** Returns the id to apply, or undefined when the text is not one this screen accepts. */
  normalize?: (value: string) => string | undefined;
  invalidMessage?: string;
  /** `panel` replaces an empty state; `row` sits at the end of a one-line status row. */
  variant?: "panel" | "row";
  /** Status semantics for the explanation (not the form), e.g. a readiness status. */
  role?: AriaRole;
  ariaLabel?: string;
  fallback: ReactNode;
  className?: string;
}) {
  const selection = useFundAccountScopeSelection();
  const openScopePicker = useOpenScopePicker();
  const id = useId();
  const [draft, setDraft] = useState("");
  const [invalid, setInvalid] = useState(false);

  if (!selection) {
    return <>{fallback}</>;
  }

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const typed = draft.trim();
    // A label chosen from the suggestions resolves to its account id.
    const matched = selection.options.find((option) => option.label === typed || option.id === typed)?.id ?? typed;
    const next = normalize ? normalize(matched) : matched || undefined;
    if (!next) {
      setInvalid(true);
      return;
    }
    setInvalid(false);
    selection.select(next);
  };

  const listId = selection.options.length > 0 ? `${id}-accounts` : undefined;
  const form = (
    <form className="flex min-w-0 flex-wrap items-center gap-2" onSubmit={submit} aria-label={`${title} form`}>
      <label className={variant === "row" ? "sr-only" : "text-xs font-medium text-foreground"} htmlFor={`${id}-account`}>Fund account</label>
      <div className={variant === "row" ? "w-56 max-w-full" : "w-72 max-w-full"}>
        <Input
          id={`${id}-account`}
          autoComplete="off"
          list={listId}
          placeholder="Fund account id"
          value={draft}
          error={invalid}
          aria-describedby={invalid ? `${id}-invalid` : undefined}
          onChange={(event) => {
            setDraft(event.target.value);
            setInvalid(false);
          }}
        />
      </div>
      {listId ? (
        <datalist id={listId}>
          {selection.options.map((option) => <option key={option.id} value={option.id}>{option.label}</option>)}
        </datalist>
      ) : null}
      <Button type="submit" size="sm" disabled={!draft.trim()} disabledReason="Enter or choose a fund account first.">
        Use account
      </Button>
      {openScopePicker ? (
        <Button type="button" size="sm" variant="ghost" onClick={openScopePicker}>All scope options</Button>
      ) : null}
      {invalid ? <p id={`${id}-invalid`} role="alert" className="basis-full text-xs text-danger">{invalidMessage}</p> : null}
    </form>
  );

  if (variant === "row") {
    return (
      <div className={cn("flex min-w-0 flex-wrap items-center gap-3", className)}>
        <p role={role} aria-label={ariaLabel} aria-live={role === "status" ? "polite" : undefined} className="min-w-0 flex-1 text-muted-foreground">
          {title}{detail ? <span> · {detail}</span> : null}
        </p>
        {form}
      </div>
    );
  }

  return (
    <section className={cn("space-y-2 rounded-[2px] border border-border bg-card px-4 py-3", className)} aria-label={title}>
      <div role={role} aria-label={ariaLabel} aria-live={role === "status" ? "polite" : undefined}>
        <p className="text-sm font-semibold text-foreground">{title}</p>
        {detail ? <p className="mt-0.5 text-xs leading-5 text-muted-foreground">{detail}</p> : null}
      </div>
      {form}
    </section>
  );
}
