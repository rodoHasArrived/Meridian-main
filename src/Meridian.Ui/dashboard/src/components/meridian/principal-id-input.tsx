import { type ComponentProps, useId, useRef, useState } from "react";
import { Input } from "@/components/ui/input";
import { getRolePermissionCatalog, getUserAccounts } from "@/lib/api";

export type PrincipalKind = "User" | "Group" | "Company" | "";

export interface PrincipalOption {
  id: string;
  label: string;
}

interface PrincipalDirectory {
  groups: PrincipalOption[];
  users: PrincipalOption[];
}

type PrincipalIdInputProps = Omit<ComponentProps<typeof Input>, "list" | "onFocus"> & {
  /** Which kind of principal the ID names; decides which suggestions are offered. */
  principalKind: PrincipalKind;
};

/**
 * Text input for a report principal ID that suggests known principals instead of making the
 * operator recall raw IDs.
 *
 * - Group IDs are role and role-profile names (the values the server matches group
 *   principals against), read from the role catalog any signed-in user can load.
 * - User IDs are usernames. The account list needs user-management permission, so a caller
 *   without it simply gets no user suggestions.
 * - Company IDs have no list source and stay free text.
 *
 * Free text is always accepted: the server remains the authority on whether an ID is valid.
 * The directory is loaded on first focus so screens that never touch the field make no request.
 */
export function PrincipalIdInput({ principalKind, ...inputProps }: PrincipalIdInputProps) {
  const listId = useId();
  const requested = useRef(false);
  const [directory, setDirectory] = useState<PrincipalDirectory>({ groups: [], users: [] });

  function loadDirectory() {
    if (requested.current) {
      return;
    }
    requested.current = true;

    void getRolePermissionCatalog()
      .then((catalog) => setDirectory((current) => ({
        ...current,
        groups: (catalog.roles ?? []).map((role) => ({
          id: role.role,
          label: role.displayName && role.displayName !== role.role ? role.displayName : ""
        }))
      })))
      .catch(() => undefined);
    void getUserAccounts()
      .then((accounts) => setDirectory((current) => ({
        ...current,
        users: (accounts ?? []).map((account) => ({
          id: account.username,
          label: account.roleProfileName ?? account.role ?? ""
        }))
      })))
      .catch(() => undefined);
  }

  const options = principalKind === "Group"
    ? directory.groups
    : principalKind === "User"
      ? directory.users
      : [];

  return (
    <>
      <Input {...inputProps} list={options.length > 0 ? listId : undefined} onFocus={loadDirectory} autoComplete="off" />
      {options.length > 0 ? (
        <datalist id={listId} data-testid="principal-id-options">
          {options.map((option) => (
            <option key={option.id} value={option.id}>{option.label}</option>
          ))}
        </datalist>
      ) : null}
    </>
  );
}
