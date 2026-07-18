export function humanizeCloseBlockerCode(code: string): string {
  const normalized = code.trim();
  if (!normalized) {
    return "Close control blocker";
  }

  return normalized
    .replace(/[_-]+/g, " ")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .toLowerCase()
    .replace(/^\w/, (character) => character.toUpperCase());
}

export function isCloseChecklistDone(status: string): boolean {
  const normalized = status.trim().toLowerCase();
  return normalized === "done" || normalized === "complete" || normalized === "completed" || normalized === "acknowledged";
}

export function closeCommandCenterTextMatches(left: string | null | undefined, right: string | null | undefined, needle: string): boolean {
  const normalizedNeedle = needle.toLowerCase();
  return [left, right]
    .map((value) => value?.toLowerCase() ?? "")
    .some((value) => value.includes(normalizedNeedle));
}

export function isOpenAccountingBreakStatus(status: string): boolean {
  const normalized = status.trim().toLowerCase();
  return normalized !== "resolved" && normalized !== "dismissed" && normalized !== "closed";
}
