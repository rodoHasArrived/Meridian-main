export type DynamicFixturePattern = {
  pattern: RegExp;
  resolve: (cleanPath: string, path: string) => unknown | undefined;
};

export function resolveFixtureFromMaps<T>(
  path: string,
  fixtures: Record<string, unknown>,
  dynamicFixturePatterns: DynamicFixturePattern[]
): T | undefined {
  const cleanPath = path.split("?")[0];
  const exact = fixtures[cleanPath];
  if (exact !== undefined) {
    return cloneFixture(exact as T);
  }

  for (const { pattern, resolve } of dynamicFixturePatterns) {
    if (pattern.test(cleanPath)) {
      const fixture = resolve(cleanPath, path);
      if (fixture !== undefined) {
        return cloneFixture(fixture as T);
      }
    }
  }

  return undefined;
}

export function readSymbolFromPath(cleanPath: string, segmentFromEnd = 0): string {
  const rawSymbol = cleanPath.split("/").at(-1 - segmentFromEnd) ?? "AAPL";
  try {
    return decodeURIComponent(rawSymbol).trim().toUpperCase() || "AAPL";
  } catch {
    return rawSymbol.trim().toUpperCase() || "AAPL";
  }
}

export function readDecodedPathSegment(cleanPath: string, segmentFromEnd = 0): string {
  const rawSegment = cleanPath.split("/").at(-1 - segmentFromEnd) ?? "";
  try {
    return decodeURIComponent(rawSegment).trim();
  } catch {
    return rawSegment.trim();
  }
}

export function apiRoutePattern(baseRoute: string, suffixPattern = ""): RegExp {
  return new RegExp(`^${escapeRegExp(baseRoute)}${suffixPattern}$`);
}

export function readFixtureSearchParams(path: string): URLSearchParams {
  try {
    return new URL(path, "http://meridian.local").searchParams;
  } catch {
    return new URLSearchParams();
  }
}

export function cloneFixture<T>(fixture: T): T {
  if (typeof structuredClone === "function") {
    return structuredClone(fixture);
  }

  return JSON.parse(JSON.stringify(fixture)) as T;
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
