const credentialKeyPattern = /(?:^|[-_])(token|bearer|secret|credential|authorization|signature|sig|api[-_]?key|access[-_]?key)(?:$|[-_])/i;
const credentialUrlTextPattern = /(?:https?:\/\/|\/)[^\s<>]*[?#&](?:token|bearer|secret|client[-_]?secret|credential|authorization|signature|sig|api[-_]?key|access[-_]?(?:key|token))=[^\s<>]*/gi;
const credentialQueryTextPattern = /([?&](?:token|bearer|secret|client[-_]?secret|credential|authorization|signature|sig|api[-_]?key|access[-_]?(?:key|token))=)[^&#\s]*/gi;

export interface ReportingHrefSafetyOptions {
  requireOpaqueFragment?: boolean;
}

/**
 * Validates server-provided reporting links before they become anchors. Query credentials are
 * always rejected. Recipient links additionally require the opaque bearer in the fragment so the
 * browser clears it before the server exchange POST.
 */
export function safeReportingHref(
  value: string | null | undefined,
  options: ReportingHrefSafetyOptions = {}
): string | null {
  const candidate = value?.trim();
  if (!candidate) return null;

  try {
    const windowOrigin = typeof window === "undefined" ? null : window.location.origin;
    const base = windowOrigin && windowOrigin !== "null" ? windowOrigin : "https://meridian.invalid";
    const parsed = new URL(candidate, base);
    if (!["http:", "https:"].includes(parsed.protocol)) return null;
    if (parsed.username || parsed.password) return null;
    if (parsed.origin !== base && parsed.protocol !== "https:") return null;

    for (const [key, queryValue] of parsed.searchParams) {
      if (credentialKeyPattern.test(key) || /(?:^|\b)(bearer|token)=/i.test(queryValue)) {
        return null;
      }
    }

    const fragment = new URLSearchParams(parsed.hash.replace(/^#/, ""));
    const fragmentToken = fragment.get("token")?.trim() ?? "";
    if (options.requireOpaqueFragment) {
      if (
        parsed.origin !== base
        || parsed.search
        || !fragmentToken
        || fragment.getAll("token").length !== 1
        || [...fragment.keys()].some((key) => key.toLowerCase() !== "token")
      ) return null;
      return candidate;
    }

    for (const key of fragment.keys()) {
      if (credentialKeyPattern.test(key)) return null;
    }
    return candidate;
  } catch {
    return null;
  }
}

/** Suppresses credential-bearing URLs and redacts any remaining query secret in retained text. */
export function redactReportingCredentialText(value: string): string {
  return value
    .replace(credentialUrlTextPattern, "[reporting credential URL suppressed]")
    .replace(credentialQueryTextPattern, "$1[REDACTED]");
}
