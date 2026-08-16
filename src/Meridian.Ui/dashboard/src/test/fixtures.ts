/**
 * Narrowing helpers for test fixtures.
 *
 * Most workspace fixtures are built as object literals and then cast with
 * `as unknown as SomeResponse`. The cast silences the shape check, so the fixture is only as
 * correct as whoever last edited it, while the production type it claims to be usually marks
 * collections optional. Reading `fixture.section.items[0]` is therefore unsound: if the element is
 * ever absent the value is `undefined`, and spreading `undefined` yields `{}` rather than throwing
 * — so the test keeps running and asserts against an empty object.
 *
 * These helpers make that case fail where it happens, and narrow the type as a side effect.
 */

/** Returns `items[index]`, failing with a named message when the fixture does not have it. */
export function requireAt<T>(items: readonly T[] | undefined | null, index: number, what: string): NonNullable<T> {
  const item = items?.[index];
  if (item === undefined || item === null) {
    const size = items?.length ?? 0;
    throw new Error(`Test fixture is missing ${what}[${index}] (fixture has ${size} entr${size === 1 ? "y" : "ies"}).`);
  }
  return item as NonNullable<T>;
}

/** Returns the first entry of `items`, failing with a named message when the fixture is empty. */
export function requireFirst<T>(items: readonly T[] | undefined | null, what: string): NonNullable<T> {
  return requireAt(items, 0, what);
}

/** Returns `value`, failing with a named message when the fixture left it absent. */
export function requirePresent<T>(value: T | undefined | null, what: string): NonNullable<T> {
  if (value === undefined || value === null) {
    throw new Error(`Test fixture is missing ${what}.`);
  }
  return value as NonNullable<T>;
}
