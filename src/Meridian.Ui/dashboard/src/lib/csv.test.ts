import { describe, expect, it } from "vitest";
import { serializeCsv } from "@/lib/csv";

describe("serializeCsv", () => {
  it("escapes commas, quotes, new lines, and null cells", () => {
    expect(
      serializeCsv(
        ["symbol", "note", "empty"],
        [
          ["AAPL", "quoted \"value\"", null],
          ["MSFT", "line\nbreak, comma", undefined],
        ],
      ),
    ).toBe('symbol,note,empty\nAAPL,"quoted ""value""",\nMSFT,"line\nbreak, comma",');
  });
});
