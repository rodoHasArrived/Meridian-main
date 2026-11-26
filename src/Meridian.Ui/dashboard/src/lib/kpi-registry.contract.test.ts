// @vitest-environment node
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { KPI_DEFINITIONS, KPI_REGISTRY, type KpiId } from "@/lib/kpi-registry";

interface FixtureDefinition { id: string; label: string; population: string; comparisonBasis: string | null }

// The same fixture WorkstationKpiDefinitionsTests reads against the C# catalog served at
// /api/workstation/kpi-definitions, so the browser and the desktop state each figure identically.
const fixture = JSON.parse(
  readFileSync(resolve(process.cwd(), "../../../tests/fixtures/kpi-definitions/kpi-definitions.json"), "utf8")
) as { definitions: FixtureDefinition[] };

describe("KPI definitions contract", () => {
  it("registers exactly the figures the shared catalog serves", () => {
    expect(Object.keys(KPI_REGISTRY).sort()).toEqual(fixture.definitions.map((definition) => definition.id).sort());
  });

  it("states each figure's label, population and comparison basis as the shared catalog does", () => {
    for (const definition of fixture.definitions) {
      const id = definition.id as KpiId;
      expect({ id, label: KPI_REGISTRY[id].label, ...KPI_DEFINITIONS[id] }).toEqual(definition);
    }
  });
});
