// Opaque record identifiers (GUID/UUID-shaped text) support audit and support work; they are
// not operator-facing content. Screens name a record by its display name and keep the identifier
// in a title attribute, a read-only scope field or a TechnicalDetails disclosure. The screenshot
// capture harness enforces this on every route's rendered text (findCaptureContentIssues in
// scripts/dev/capture-web-screenshots.mjs).

const WHOLE_OPAQUE_IDENTIFIER = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** True when the whole value is a GUID/UUID-shaped identifier. */
export function isOpaqueIdentifier(value: string | null | undefined): boolean {
  return WHOLE_OPAQUE_IDENTIFIER.test(value?.trim() ?? "");
}

/**
 * Drops the parts of a joined label that are bare opaque identifiers, e.g.
 * "Fund · 0000…00bb" -> "Fund". Returns `fallback` when nothing readable is left.
 */
export function withoutOpaqueIdentifiers(label: string, separator = " · ", fallback = label): string {
  const readable = label
    .split(separator)
    .map((part) => part.trim())
    .filter((part) => part.length > 0 && !isOpaqueIdentifier(part));
  return readable.length > 0 ? readable.join(separator) : fallback;
}
