// @vitest-environment node
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { runInNewContext } from "node:vm";
import { describe, expect, it, vi } from "vitest";
import { APPEARANCE_STORAGE_KEY } from "@/lib/theme";

const html = readFileSync(resolve(process.cwd(), "index.html"), "utf8");
const bootstrap = html.match(/<script data-meridian-appearance>([\s\S]*?)<\/script>/)?.[1];

describe("startup appearance", () => {
  it.each(["light", "dark", "system", "invalid", null])("honors the stored %s preference before modules load", (appearance) => {
    const getItem = vi.fn(() => appearance);
    const setAttribute = vi.fn();
    expect(bootstrap).toBeTruthy();
    runInNewContext(bootstrap!, { localStorage: { getItem }, document: { documentElement: { setAttribute } } });
    expect(getItem).toHaveBeenCalledWith(APPEARANCE_STORAGE_KEY);
    if (appearance === "light" || appearance === "dark") {
      expect(setAttribute).toHaveBeenCalledWith("data-theme", appearance);
    } else {
      expect(setAttribute).not.toHaveBeenCalled();
    }
  });

  it("keeps system appearance when storage is blocked", () => {
    const setAttribute = vi.fn();
    expect(() => runInNewContext(bootstrap!, {
      localStorage: { getItem() { throw new Error("Storage blocked"); } },
      document: { documentElement: { setAttribute } }
    })).not.toThrow();
    expect(setAttribute).not.toHaveBeenCalled();
  });
});
