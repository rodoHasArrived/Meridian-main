import type { AppShellOperatingScopeInput } from "@/app-shell.operating-scope";

export const OPERATING_CONTEXT_STORAGE_KEY = "meridian.workstation.operatingContext.v1";

const OPERATING_SCOPE_STORAGE_FIELDS = [
  "symbol",
  "fundAccountId",
  "runId",
  "provider",
  "from",
  "to",
  "date",
  "asOf"
] as const satisfies readonly (keyof AppShellOperatingScopeInput)[];

type OperatingScopeStorageField = typeof OPERATING_SCOPE_STORAGE_FIELDS[number];

export function readStoredOperatingScope(): AppShellOperatingScopeInput {
  if (typeof window === "undefined") {
    return {};
  }

  try {
    return parseStoredOperatingScope(window.localStorage.getItem(OPERATING_CONTEXT_STORAGE_KEY));
  } catch {
    return {};
  }
}

export function parseStoredOperatingScope(raw: string | null): AppShellOperatingScopeInput {
  if (!raw) {
    return {};
  }

  let parsed: unknown;
  try {
    parsed = raw.trim().startsWith("{") ? JSON.parse(raw) : raw;
  } catch {
    return {};
  }

  if (typeof parsed === "string") {
    const symbol = readStoredScopeString(parsed);
    return symbol ? { symbol } : {};
  }

  if (!isRecord(parsed)) {
    return {};
  }

  return compactOperatingScope(Object.fromEntries(
    OPERATING_SCOPE_STORAGE_FIELDS.map((field) => [field, readStoredScopeString(parsed[field])])
  ) as Record<OperatingScopeStorageField, string | null>);
}

export function writeStoredOperatingScope(scope: AppShellOperatingScopeInput) {
  if (typeof window === "undefined") {
    return;
  }

  try {
    const nextScope = compactOperatingScope(scope);
    if (!hasOperatingScopeValues(nextScope)) {
      window.localStorage.removeItem(OPERATING_CONTEXT_STORAGE_KEY);
      return;
    }

    window.localStorage.setItem(OPERATING_CONTEXT_STORAGE_KEY, JSON.stringify(nextScope));
  } catch {
    // Browser storage can be unavailable in private or locked-down contexts.
  }
}

export function mergeOperatingScopes(
  storedScope: AppShellOperatingScopeInput,
  routeScope: AppShellOperatingScopeInput
): AppShellOperatingScopeInput {
  return compactOperatingScope({
    symbol: routeScope.symbol ?? storedScope.symbol ?? null,
    fundAccountId: routeScope.fundAccountId ?? storedScope.fundAccountId ?? null,
    runId: routeScope.runId ?? storedScope.runId ?? null,
    provider: routeScope.provider ?? storedScope.provider ?? null,
    from: routeScope.from ?? storedScope.from ?? null,
    to: routeScope.to ?? storedScope.to ?? null,
    date: routeScope.date ?? storedScope.date ?? null,
    asOf: routeScope.asOf ?? storedScope.asOf ?? null
  });
}

export function compactOperatingScope(scope: AppShellOperatingScopeInput): AppShellOperatingScopeInput {
  return {
    ...(scope.symbol ? { symbol: scope.symbol } : {}),
    ...(scope.fundAccountId ? { fundAccountId: scope.fundAccountId } : {}),
    ...(scope.runId ? { runId: scope.runId } : {}),
    ...(scope.provider ? { provider: scope.provider } : {}),
    ...(scope.from ? { from: scope.from } : {}),
    ...(scope.to ? { to: scope.to } : {}),
    ...(scope.date ? { date: scope.date } : {}),
    ...(scope.asOf ? { asOf: scope.asOf } : {})
  };
}

export function hasOperatingScopeValues(scope: AppShellOperatingScopeInput): boolean {
  return Boolean(
    scope.symbol
    || scope.fundAccountId
    || scope.runId
    || scope.provider
    || scope.from
    || scope.to
    || scope.date
    || scope.asOf
  );
}

export function operatingScopesEqual(left: AppShellOperatingScopeInput, right: AppShellOperatingScopeInput): boolean {
  const compactLeft = compactOperatingScope(left);
  const compactRight = compactOperatingScope(right);
  return JSON.stringify(compactLeft) === JSON.stringify(compactRight);
}

function readStoredScopeString(value: unknown): string | null {
  return typeof value === "string" && value.trim().length > 0 ? value : null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}
