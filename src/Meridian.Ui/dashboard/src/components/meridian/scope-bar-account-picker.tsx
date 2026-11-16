import { useId, useState } from "react";
import { Combobox } from "@/components/ui/combobox";
import { narrowInlineAccountOptions } from "@/lib/operating-scope/fund-scope-narrowing";
import { useFundAccountScopeSelection } from "@/lib/operating-scope/scope-picker-context";
import { UNAVAILABLE_SCOPE_STRUCTURE } from "@/lib/operating-scope/structure-options";

/**
 * The account step inside the header scope bar, for workspaces that need exactly one fund account
 * (Trading). While no account is in scope it offers the accounts the scope picker would, narrowed
 * by the chosen entity and fund; choosing one sets only the fund-account dimension of the
 * operating scope, so the bar, the URL and every screen change together. Once an account is in
 * scope the bar itself shows it and this renders nothing. Pages do not carry a picker of their own.
 */
export function ScopeBarAccountPicker({ normalize, invalidMessage = "This account cannot be used here." }: {
  /** Returns the id to apply, or undefined when the workspace cannot use the chosen account. */
  normalize?: (value: string) => string | undefined;
  invalidMessage?: string;
}) {
  const selection = useFundAccountScopeSelection();
  const id = useId();
  const [invalid, setInvalid] = useState(false);
  if (!selection || selection.scope?.fundAccountId?.trim()) {
    return null;
  }

  const structure = selection.structure ?? UNAVAILABLE_SCOPE_STRUCTURE;
  const narrowing = narrowInlineAccountOptions(selection.options, structure, {
    entityId: selection.scope?.entityId,
    fundId: selection.scope?.fundId
  });
  const options = narrowing.options.map((option) => ({ value: option.id, label: option.label }));

  return (
    <div className="operating-scope-bar-account" data-scope-bar-account-picker>
      <Combobox
        id={`${id}-account`}
        aria-label="Account for this workspace"
        aria-invalid={invalid || undefined}
        aria-describedby={invalid ? `${id}-invalid` : narrowing.help ? `${id}-help` : undefined}
        options={options}
        placeholder="Choose account…"
        emptyText={options.length === 0 ? "No accounts available in this scope." : "No matching accounts."}
        value=""
        onChange={(value) => {
          const next = normalize ? normalize(value) : value;
          setInvalid(!next);
          if (next) selection.select(next);
        }}
      />
      {invalid ? <p id={`${id}-invalid`} role="alert" className="sr-only">{invalidMessage}</p> : null}
      {!invalid && narrowing.help ? <p id={`${id}-help`} className="sr-only">{narrowing.help}</p> : null}
    </div>
  );
}
