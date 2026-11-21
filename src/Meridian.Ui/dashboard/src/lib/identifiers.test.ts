import { describe, expect, it } from "vitest";
import { isOpaqueIdentifier, withoutOpaqueIdentifiers } from "./identifiers";

const GUID = "53bf0251-17f6-4fb7-8dbe-6fb4966e2749";

describe("identifiers", () => {
  it("recognises GUID-shaped values and leaves readable codes alone", () => {
    expect(isOpaqueIdentifier(GUID)).toBe(true);
    expect(isOpaqueIdentifier(` ${GUID.toUpperCase()} `)).toBe(true);
    expect(isOpaqueIdentifier("fund-alpha")).toBe(false);
    expect(isOpaqueIdentifier("2026-05")).toBe(false);
    expect(isOpaqueIdentifier(`Book ${GUID}`)).toBe(false);
    expect(isOpaqueIdentifier(null)).toBe(false);
  });

  it("drops opaque parts from a joined label and keeps readable ones", () => {
    expect(withoutOpaqueIdentifiers(`Fund · ${GUID}`)).toBe("Fund");
    expect(withoutOpaqueIdentifiers("Entity · entity-master")).toBe("Entity · entity-master");
    expect(withoutOpaqueIdentifiers(`2026-05 / ${GUID}`, " / ")).toBe("2026-05");
    expect(withoutOpaqueIdentifiers(GUID, " · ", "Selected scope")).toBe("Selected scope");
    // With no fallback the label is returned unchanged rather than emptied.
    expect(withoutOpaqueIdentifiers(GUID)).toBe(GUID);
  });
});
