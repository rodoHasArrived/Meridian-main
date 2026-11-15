import { useEffect, useId, useState, type AriaRole, type FormEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Combobox } from "@/components/ui/combobox";
import { accountKnownOutsideFundScope, narrowInlineAccountOptions } from "@/lib/operating-scope/fund-scope-narrowing";
import { useFundAccountScopeSelection, useOpenScopePicker } from "@/lib/operating-scope/scope-picker-context";
import { UNAVAILABLE_SCOPE_STRUCTURE } from "@/lib/operating-scope/structure-options";
import { cn } from "@/lib/utils";

/**
 * The account step, inline, for a screen that needs exactly one fund account: in place of an
 * empty "choose an account" state the operator picks the account right here. Applying it sets the
 * fund account of the shell's operating scope (keeping every other dimension), which also puts it
 * in the URL, so the choice is the same one the scope bar shows and a copied link carries it.
 *
 * With an entity or fund chosen and the organization graph loaded, the suggestions are narrowed
 * exactly as the scope picker narrows its account list (accounts with no recorded fund or entity
 * are left out and counted in the help line), and an account the graph places outside the chosen
 * fund or entity is flagged. Without the graph nothing is narrowed and the help line says why.
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
  const scopeAccount = selection?.scope?.fundAccountId ?? "";
  const preferredAccount = scopeAccount || selection?.lastUsedAccountId || "";
  const [draft, setDraft] = useState(preferredAccount);
  const [invalid, setInvalid] = useState(false);

  useEffect(() => {
    setDraft(preferredAccount);
    setInvalid(false);
  }, [preferredAccount]);

  if (!selection) {
    return <>{fallback}</>;
  }

  // Suggestions narrowed by the chosen entity and fund exactly as the scope picker narrows its
  // account list; without the organization graph nothing is narrowed.
  const structure = selection.structure ?? UNAVAILABLE_SCOPE_STRUCTURE;
  const fundSelection = { entityId: selection.scope?.entityId, fundId: selection.scope?.fundId };
  const narrowing = narrowInlineAccountOptions(selection.options, structure, fundSelection);
  const options = narrowing.options;
  const fundScopeChosen = Boolean(fundSelection.entityId?.trim() || fundSelection.fundId?.trim());
  const structureNote = !fundScopeChosen || structure.status === "ready"
    ? null
    : selection.structureLoading
      ? "Loading entities and funds…"
      : "Entity and fund lists could not be loaded, so accounts are not narrowed by entity or fund.";
  const fitDimension = fundSelection.fundId?.trim() ? "fund" : "entity";
  // Keep a retained selection visible even when a newly chosen fund narrows it out.
  // Searching never changes scope; only a listed account and Use account can apply it.
  const pickerOptions = options.map((option) => ({ value: option.id, label: option.label }));
  if (scopeAccount && !pickerOptions.some((option) => option.value === scopeAccount)) {
    pickerOptions.unshift({ value: scopeAccount, label: selection.options.find((option) => option.id === scopeAccount)?.label ?? scopeAccount });
  }
  const selectedAccount = pickerOptions.some((option) => option.value === draft) ? draft : "";
  const candidateAccountId = selectedAccount || null;
  const candidateOutside = accountKnownOutsideFundScope(candidateAccountId, selection.options, structure, fundSelection);
  const candidateLabel = [...structure.accounts, ...selection.options]
    .find((option) => option.id.toLowerCase() === candidateAccountId?.trim().toLowerCase())?.label ?? candidateAccountId;
  const outsideMessage = candidateOutside && candidateAccountId
    ? `${candidateLabel} is not under the chosen ${fitDimension}. Screens that narrow by ${fitDimension} will show none of its rows.`
    : null;

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const chosen = pickerOptions.find((option) => option.value === selectedAccount)?.value;
    const next = chosen && (normalize ? normalize(chosen) : chosen);
    if (!next) {
      setInvalid(true);
      return;
    }
    setInvalid(false);
    selection.select(next);
  };

  const helpText = [narrowing.help, structureNote].filter(Boolean).join(" ") || null;
  const describedBy = [
    invalid ? `${id}-invalid` : null,
    helpText ? `${id}-narrowing` : null,
    outsideMessage ? `${id}-outside` : null
  ].filter(Boolean).join(" ") || undefined;
  const form = (
    <form className="flex min-w-0 flex-wrap items-center gap-2" onSubmit={submit} aria-label={`${title} form`}>
      <div className={variant === "row" ? "w-56 max-w-full" : "w-72 max-w-full"}>
        <Combobox
          id={`${id}-account`}
          label="Fund account"
          options={pickerOptions}
          placeholder="Search accounts…"
          emptyText={options.length === 0 ? "No accounts available in this scope. Use All scope options to review the scope." : "No matching accounts."}
          value={selectedAccount}
          required
          aria-invalid={invalid || undefined}
          aria-describedby={describedBy}
          onChange={(value) => {
            setDraft(value);
            setInvalid(false);
          }}
        />
      </div>
      <Button type="submit" size="sm" disabled={!selectedAccount} disabledReason="Choose a fund account first.">
        Use account
      </Button>
      {openScopePicker ? (
        <Button type="button" size="sm" variant="ghost" onClick={openScopePicker}>All scope options</Button>
      ) : null}
      {invalid ? <p id={`${id}-invalid`} role="alert" className="basis-full text-xs text-danger">{invalidMessage}</p> : null}
      {helpText ? <p id={`${id}-narrowing`} className="basis-full text-xs text-muted-foreground">{helpText}</p> : null}
      {outsideMessage ? <p id={`${id}-outside`} role="status" className="basis-full text-xs text-warning">{outsideMessage}</p> : null}
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
