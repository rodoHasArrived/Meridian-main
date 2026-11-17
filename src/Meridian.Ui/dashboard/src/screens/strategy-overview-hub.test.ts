import { describe, expect, it } from "vitest";
import { strategyRunPopulationNote } from "@/screens/strategy-overview-hub";

describe("strategyRunPopulationNote", () => {
  it("explains a Runs headline that counts more runs than the list has loaded", () => {
    expect(strategyRunPopulationNote("24", 2)).toBe("The Runs headline counts 24 retained runs; 2 are loaded in this list.");
    expect(strategyRunPopulationNote("1,024", 1)).toBe("The Runs headline counts 1024 retained runs; 1 is loaded in this list.");
  });

  it("says nothing when the counts agree or the headline is not a count", () => {
    expect(strategyRunPopulationNote("2", 2)).toBeNull();
    expect(strategyRunPopulationNote(undefined, 2)).toBeNull();
    expect(strategyRunPopulationNote("—", 2)).toBeNull();
    expect(strategyRunPopulationNote("24%", 2)).toBeNull();
  });
});
