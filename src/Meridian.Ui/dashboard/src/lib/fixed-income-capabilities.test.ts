// @vitest-environment node

import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { FIXED_INCOME_CAPABILITIES, type FixedIncomeCapabilityTrace } from "@/lib/fixed-income-capabilities";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";

const root = fileURLToPath(new URL("../../../../../", import.meta.url));
const capabilities: readonly FixedIncomeCapabilityTrace[] = FIXED_INCOME_CAPABILITIES;

describe("fixed-income operation traceability", () => {
  it("keeps service, client, screen and supporting-test references attached to existing source", () => {
    for (const capability of capabilities) {
      for (const operation of capability.operations) {
        for (const source of [operation.service, operation.client, operation.screenAction]) {
          if (!source) continue;
          expect(existsSync(resolve(root, source.file)), `${operation.id}: ${source.file}`).toBe(true);
          expect(readFileSync(resolve(root, source.file), "utf8"), `${operation.id}: ${source.symbol}`).toContain(source.symbol);
        }
        if (operation.browserAcceptance.supportingTest) {
          expect(existsSync(resolve(root, operation.browserAcceptance.supportingTest)), `${operation.id}: supporting test`).toBe(true);
        }
      }
    }
  });

  it("uses shared API route identities or an explicit legacy route with an endpoint owner", () => {
    for (const capability of capabilities) {
      for (const operation of capability.operations) {
        for (const endpoint of operation.api ?? []) {
          expect(existsSync(resolve(root, endpoint.file)), `${operation.id}: endpoint owner`).toBe(true);
          if ("routeKey" in endpoint) {
            expect(UI_API_ROUTES[endpoint.routeKey], `${operation.id}: generated route`).toMatch(/^\/api\//);
          } else {
            expect(endpoint.route).toMatch(/^\/api\//);
          }
        }
      }
    }
  });

  it("cannot present missing workflow seams or component coverage as complete browser acceptance", () => {
    const ids = new Set<string>();
    const operationIds = new Set<string>();
    for (const capability of capabilities) {
      expect(ids.has(capability.id), `duplicate capability ${capability.id}`).toBe(false);
      ids.add(capability.id);
      for (const operation of capability.operations) {
        expect(operationIds.has(operation.id), `duplicate operation ${operation.id}`).toBe(false);
        operationIds.add(operation.id);
        expect(operation.permission.trim(), `${operation.id}: permission policy`).not.toBe("");
        expect(operation.browserAcceptance.scenario.trim(), `${operation.id}: browser scenario`).not.toBe("");
        if (!operation.api || !operation.client || !operation.screenAction) {
          expect(operation.remainingGaps.length, `${operation.id}: missing seam explanation`).toBeGreaterThan(0);
        }
        if (operation.browserAcceptance.status === "synthetic-verified") {
          expect(operation.api, `${operation.id}: complete API seam required`).not.toBeNull();
          expect(operation.client, `${operation.id}: complete client seam required`).not.toBeNull();
          expect(operation.screenAction, `${operation.id}: complete operator action required`).not.toBeNull();
          expect(operation.remainingGaps, `${operation.id}: partial operation is not complete acceptance`).toHaveLength(0);
          expect(operation.browserAcceptance.browserTest, `${operation.id}: executed browser test required`).not.toBeNull();
          expect(existsSync(resolve(root, operation.browserAcceptance.browserTest!)), `${operation.id}: browser test source`).toBe(true);
        }
      }
    }
  });
});
