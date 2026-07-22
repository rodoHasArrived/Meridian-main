const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function normalizeFundAccountGuid(value: string | null | undefined): string | undefined {
  const normalized = value?.trim();
  return normalized && guidPattern.test(normalized) ? normalized : undefined;
}
