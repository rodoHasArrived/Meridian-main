import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";
import {
  assertHealthyScene, compositeColor, contrastRatio, designSystemMatrix, isCanceledApiRequest, parseCssColor, supportingSmokeResponse
} from "./design-system-smoke-helpers.mjs";

const appearanceKey = "meridian.workstation.appearance.v1";
const workbenchTitle = "Meridian native component workbench";
const workbenchPath = "/__design-system";
const expectedFocus = dark => dark ? "rgb(92, 159, 229)" : "rgb(45, 111, 179)";
const expectedPaper = dark => dark ? "rgb(127, 157, 184)" : "rgb(93, 114, 134)";

function sceneMonitor(page, scene, allowedOrigin) {
  scene.errors = []; scene.exceptions = []; scene.assetFailures = []; scene.unexpectedRequests = [];
  page.on("pageerror", error => scene.exceptions.push(error.message));
  page.on("console", message => {
    if (message.type() === "error") scene.errors.push(message.text());
  });
  page.on("request", request => {
    if (new URL(request.url()).origin !== allowedOrigin) scene.unexpectedRequests.push(request.url());
  });
  page.on("requestfailed", request => {
    if (!scene.closing && !isCanceledApiRequest(request)) {
      scene.assetFailures.push(request.url() + ": " + request.failure()?.errorText);
    }
  });
  page.on("response", response => {
    if (response.status() >= 400) scene.assetFailures.push("HTTP " + response.status() + " " + response.url());
  });
}

async function installFixtures(page, registry, fixtures, scene, helper) {
  const resolver = helper.createScreenshotApiResolver(registry, fixtures, { scenarioId: "accounting.normal", strict: true });
  await page.route("**/api/**", async route => {
    const request = route.request();
    const url = new URL(request.url());
    if (!url.pathname.startsWith("/api/")) return route.continue();
    try {
      const response = supportingSmokeResponse(url.href, request.method()) || resolver.resolve(url.href, request.method());
      if (response.smokeFixture) {
        scene.syntheticReadOnlyFixtures ??= [];
        scene.syntheticReadOnlyFixtures.push(response.smokeFixture);
      }
      if (response.delayMs) await new Promise(resolve => setTimeout(resolve, response.delayMs));
      await route.fulfill({ status: response.status, contentType: "application/json", body: JSON.stringify(response.body), headers: response.headers });
    } catch (error) {
      scene.unexpectedRequests.push(request.method() + " " + url.pathname + ": " + error.message);
      await route.fulfill({ status: 500, contentType: "application/json", body: JSON.stringify({ title: "Unexpected smoke request" }) });
    }
  });
}

async function localColors(page, selector = "html") {
  return page.locator(selector).first().evaluate(element => {
    const probe = document.createElement("span");
    // A single probe is reused within one frame; theme transitions must not
    // interpolate the preceding role into the next measurement.
    probe.style.transition = "none"; element.appendChild(probe);
    const read = name => {
      if (!getComputedStyle(element).getPropertyValue(name).trim()) throw new Error("Missing required color role " + name);
      probe.style.color = "var(" + name + ")";
      return getComputedStyle(probe).color;
    };
    const values = Object.fromEntries(["--interaction-focus", "--cyan-focus", "--mode-paper", "--brand-axis", "--interaction-selected", "--ws-primary-hover", "--ws-primary-pressed"].map(name => [name, read(name)]));
    probe.remove(); return values;
  });
}

async function allFontFaces(page) {
  const faces = await page.evaluate(async () => {
    const results = [];
    for (const family of ["IBM Plex Sans", "IBM Plex Mono"]) {
      for (const face of ["400", "italic 400", "500", "600", "700"]) {
        const loaded = await document.fonts.load(face + ' 14px "' + family + '"', "Meridian 0123456789");
        results.push({ family, face, loaded: loaded.length, ready: loaded.every(item => item.status === "loaded") });
      }
    }
    await document.fonts.ready;
    return { body: getComputedStyle(document.body).fontFamily, faces: results };
  });
  assert.match(faces.body, /IBM Plex Sans/, "Body uses IBM Plex Sans");
  assert(faces.faces.every(face => face.loaded > 0 && face.ready), "All ten bundled Plex faces load");
  const client = await page.context().newCDPSession(page);
  try {
    await client.send("DOM.enable"); await client.send("CSS.enable");
    const document = await client.send("DOM.getDocument");
    faces.rendered = {};
    for (const selector of ["h1", ".font-mono"]) {
      const node = await client.send("DOM.querySelector", { nodeId: document.root.nodeId, selector });
      if (!node.nodeId) continue;
      const actual = (await client.send("CSS.getPlatformFontsForNode", { nodeId: node.nodeId })).fonts;
      assert(actual.some(font => font.familyName.startsWith("IBM Plex")), "Rendered glyphs use Plex: " + selector);
      faces.rendered[selector] = actual;
    }
  } finally { await client.detach(); }
  return faces;
}

async function paperIndicator(page) {
  const indicator = page.locator('.workstation-trust-summary [data-environment="paper"]');
  assert.equal(await indicator.count(), 1, "The fixture exercises the masthead Paper indicator");
  const appearance = await indicator.evaluate(element => {
    const backgrounds = [];
    for (let current = element; current; current = current.parentElement) backgrounds.unshift(getComputedStyle(current).backgroundColor);
    return {
      label: getComputedStyle(element).color,
      dot: getComputedStyle(element.querySelector(".mds-badge__dot")).backgroundColor,
      backgrounds
    };
  });
  assert.equal(appearance.label, expectedPaper(true), "Paper uses readable slate on the dark masthead");
  assert.equal(appearance.dot, appearance.label, "Paper dot matches its slate label");
  const background = appearance.backgrounds.reduce((color, next) => compositeColor(parseCssColor(next), color), [255, 255, 255, 1]);
  appearance.contrast = contrastRatio(parseCssColor(appearance.label), background);
  assert(appearance.contrast >= 4.5, "Masthead Paper label contrast is at least4.5:1");
  return appearance;
}

async function selectedContrast(page) {
  const pairs = await page.locator('tr[aria-selected="true"], tr[data-selected="true"]').evaluateAll(rows =>
    rows.flatMap(row => [...row.querySelectorAll(".text-muted-foreground, [class*=text-xs], [class*=text-]")].filter(element =>
      element.getClientRects().length && !element.children.length && element.textContent.trim() && element.getAttribute("aria-hidden") !== "true"
    ).map(element => {
      const backgrounds = [];
      for (let current = element; current; current = current.parentElement) backgrounds.unshift(getComputedStyle(current).backgroundColor);
      return { text: element.textContent.trim().slice(0, 90), color: getComputedStyle(element).color, backgrounds };
    }))
  );
  return pairs.map(pair => {
    const background = pair.backgrounds.reduce((color, next) => compositeColor(parseCssColor(next), color), [255, 255, 255, 1]);
    const ratio = contrastRatio(compositeColor(parseCssColor(pair.color), background), background);
    assert(ratio >= 4.5, "Selected row text contrast " + ratio.toFixed(2) + ":1 for " + pair.text);
    return { text: pair.text, ratio };
  });
}

async function blueSelection(page, colors) {
  const rows = page.locator('tr[aria-selected="true"], tr[data-selected="true"]');
  assert(await rows.count() > 0, "The populated fixture must exercise a selected table row");
  const rails = await rows.evaluateAll(elements => elements.map(element => {
    const style = getComputedStyle(element);
    return { shadow: style.boxShadow, border: style.borderLeftColor };
  }));
  assert(rails.some(rail => rail.shadow.includes(colors["--interaction-selected"]) || rail.border === colors["--interaction-selected"]),
    "Selected rows use the interaction-selection rail");
  return rails;
}

async function settleTransitions(page) {
  await page.evaluate(async () => {
    const deadline = performance.now() + 2000;
    let quietFrames = 0;
    while (performance.now() < deadline) {
      await new Promise(resolve => requestAnimationFrame(resolve));
      // Inherited colors can start a descendant transition one frame after its
      // parent, even with reduced motion. Ignore infinite CSS spinner animations.
      const active = document.getAnimations().some(animation =>
        animation instanceof CSSTransition && animation.playState === "running");
      quietFrames = active ? 0 : quietFrames + 1;
      if (quietFrames === 2) return;
    }
    throw new Error("UI color transitions did not settle within two seconds");
  });
}

async function contrastAudit(page, scene, axeSource, report) {
  await settleTransitions(page);
  await page.addScriptTag({ content: axeSource });
  const result = await page.evaluate(async () => {
    const audit = await window.axe.run(document, { preload: false, runOnly: { type: "rule", values: ["color-contrast"] } });
    return {
      violations: audit.violations.map(item => ({ id: item.id, nodes: item.nodes.map(node => ({ target: node.target, summary: node.failureSummary })) })),
      incomplete: audit.incomplete.map(item => ({ id: item.id, nodes: item.nodes.length }))
    };
  });
  scene.accessibility = result;
  report.accessibility.push({ label: scene.name, ...result });
  assert.deepEqual(result.violations, [], scene.name + ": readable text on the actual surfaces");
}

export async function reconciliationInteractions(page, scene) {
  const search = page.getByRole("textbox", { name: "Search breaks", exact: true });
  await search.fill("Smoke-no-matching-record");
  await page.waitForURL(url => url.searchParams.get("reconciliationSearch") === "Smoke-no-matching-record");
  // The search writes through the existing debounced URL state. Let the
  // navigation commit before clearing it through the operator's reset control.
  await page.getByRole("button", { name: "Clear filters", exact: true }).click();
  await page.waitForURL(url => !url.searchParams.has("reconciliationSearch"));
  await page.waitForFunction(element => element.value === "", await search.elementHandle());
  await search.focus(); await page.keyboard.press("Tab");
  await settleTransitions(page);
  const focus = await page.evaluate(() => {
    const element = document.activeElement, style = getComputedStyle(element);
    return { visible: element.matches(":focus-visible"), outline: style.outline, shadow: style.boxShadow };
  });
  assert(focus.visible && [focus.outline, focus.shadow].some(value => value.includes(expectedFocus(scene.appearance.dark))),
    "Keyboard control outline or ring is interaction blue: " + JSON.stringify(focus));
  await page.getByRole("button", { name: /^Keyboard shortcuts/ }).click();
  const shortcuts = page.getByRole("dialog", { name: "Break queue shortcuts" });
  await shortcuts.waitFor(); await page.keyboard.press("Escape"); await shortcuts.waitFor({ state: "hidden" });
  await page.locator("button").filter({ hasText: /^Resolve$/ }).first().click();
  const rationale = page.getByRole("textbox", { name: "Resolve rationale", exact: true });
  await rationale.waitFor();
  const primary = page.locator('button[aria-label^="Confirm resolve for reconciliation break"]');
  assert(await primary.isDisabled(), "A consequential confirmation requires a rationale");
  await rationale.fill("Synthetic smoke proof; no instruction will be submitted.");
  await page.waitForFunction(element => !element.disabled, await primary.elementHandle());
  assert(await primary.isEnabled(), "A local valid rationale enables confirmation");
  await page.mouse.move(0, 0);
  await page.waitForTimeout(200);
  const normal = await primary.evaluate(element => ({ background: getComputedStyle(element).backgroundColor, foreground: getComputedStyle(element).color }));
  await primary.hover(); await page.waitForTimeout(200);
  const hover = await primary.evaluate(element => getComputedStyle(element).backgroundColor);
  const point = await primary.boundingBox();
  assert(point, "Confirmation button has a visible box");
  await page.mouse.move(point.x + point.width / 2, point.y + point.height / 2);
  await page.mouse.down(); await page.waitForTimeout(180);
  const pressed = await primary.evaluate(element => getComputedStyle(element).backgroundColor);
  await page.mouse.move(0, 0); await page.mouse.up();
  for (const fill of [normal.background, hover, pressed]) {
    assert(contrastRatio(parseCssColor(normal.foreground), parseCssColor(fill)) >= 4.5, "Primary state label contrast must be at least 4.5:1");
  }
  const colors = await localColors(page);
  for (const [actual, name] of [[normal.background, "--brand-axis"], [hover, "--ws-primary-hover"], [pressed, "--ws-primary-pressed"]]) {
    const a = parseCssColor(actual), b = parseCssColor(colors[name]);
    assert(a.slice(0, 3).every((value, index) => Math.abs(value - b[index]) <= 2), "Primary state uses " + name);
  }
  await page.locator('button[aria-label^="Cancel resolve for reconciliation break"]').click();
  if (scene.appearance.dark) {
    await page.locator("summary").filter({ hasText: "Readiness, source comparison, and run evidence" }).click();
    const reference = page.locator('[data-appearance="light"]').first();
    await reference.waitFor({ state: "visible" });
    const nested = await localColors(page, '[data-appearance="light"]');
    assert.equal(nested["--interaction-focus"], expectedFocus(false), "Nested explicit-light focus remains local");
    assert.equal(nested["--cyan-focus"], expectedFocus(false));
    assert.equal(nested["--mode-paper"], expectedPaper(false));
    const expected = await reference.evaluate(element => {
      const probe = document.createElement("span"); probe.style.transition = "none"; element.append(probe);
      probe.style.color = "color-mix(in srgb, #29323A 88%, #F8F7F3)";
      const hover = getComputedStyle(probe).color;
      probe.style.color = "color-mix(in srgb, #29323A 78%, #F8F7F3)";
      const pressed = getComputedStyle(probe).color; probe.remove(); return { hover, pressed };
    });
    assert.equal(nested["--ws-primary-hover"], expected.hover);
    assert.equal(nested["--ws-primary-pressed"], expected.pressed);
    scene.nestedLight = nested;
    await page.locator("summary").filter({ hasText: "Readiness, source comparison, and run evidence" }).click();
  }
  await page.keyboard.press("Control+k");
  const commandSearch = page.getByRole("searchbox", { name: "Search command palette", exact: true });
  await commandSearch.waitFor();
  assert(await commandSearch.evaluate(element => element === document.activeElement), "Palette focuses its native search field");
  await page.keyboard.press("Escape"); await commandSearch.waitFor({ state: "hidden" });
  scene.interactions = { search: true, focus, shortcuts: true, rationale: true, primary: { normal, hover, pressed }, commandPalette: true };
}

export async function startupPreference(browser, base, registry, fixtures, helper, scenes) {
  for (const [stored, os] of [["dark", "light"], ["light", "dark"]]) {
    const scene = { name: "startup-" + stored + "-on-" + os };
    scenes.push(scene);
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, colorScheme: os });
    await context.addInitScript(({ key, value }) => localStorage.setItem(key, value), { key: appearanceKey, value: stored });
    const page = await context.newPage(); sceneMonitor(page, scene, new URL(base).origin);
    await installFixtures(page, registry, fixtures, scene, helper);
    const releases = [];
    await page.route(/\/(?:src\/main\.tsx|assets\/index-[^/]+\.js)(?:\?|$)/, route =>
      new Promise(resolve => releases.push(() => route.continue().then(resolve)))
    );
    try {
      await page.goto(base + "/accounting/reconciliation?scenario=accounting.normal", { waitUntil: "commit" });
      await page.locator(".startup-shell").waitFor();
      assert.equal(await page.locator("html").getAttribute("data-theme"), stored, "Saved preference applies before application modules");
      const deadline = Date.now() + 20000;
      while (!releases.length && Date.now() < deadline) await new Promise(resolve => setTimeout(resolve, 25));
      assert(releases.length, "Application entry was intercepted before startup preference proof");
      await Promise.all(releases.map(release => release()));
      await page.locator(".workstation-frame").waitFor();
      assertHealthyScene(scene); scene.status = "passed";
    } finally {
      await Promise.all(releases.map(release => release().catch(() => {})));
      scene.closing = true; await context.close(); delete scene.closing;
    }
  }
}

export async function workbenchProof(browser, base, report, output, axeSource) {
  const scene = { name: "fixture-workbench" }; report.expanded.push(scene);
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, reducedMotion: "reduce" });
  await context.addInitScript(() => {
    localStorage.setItem("meridian.workstation.appearance.v1", "dark");
    localStorage.setItem("meridian.workstation.density.v1", "compact");
    sessionStorage.setItem("workbench-smoke-sentinel", "unchanged");
    window.__workbenchStorageOperations = [];
    for (const operation of ["getItem", "setItem", "removeItem", "clear"]) {
      const original = Storage.prototype[operation];
      Storage.prototype[operation] = function (...args) {
        window.__workbenchStorageOperations.push({ operation, key: args[0] ?? null });
        return original.apply(this, args);
      };
    }
    window.__workbenchBroadcasts = [];
    if (window.BroadcastChannel) {
      const OriginalChannel = window.BroadcastChannel;
      window.BroadcastChannel = class extends OriginalChannel {
        constructor(name) { super(name); window.__workbenchBroadcasts.push(name); }
      };
    }
  });
  const page = await context.newPage(); sceneMonitor(page, scene, new URL(base).origin);
  const api = [];
  await page.route("**/api/**", async route => {
    if (!new URL(route.request().url()).pathname.startsWith("/api/")) return route.continue();
    api.push(route.request().method() + " " + route.request().url()); await route.abort();
  });
  try {
    await page.goto(base + workbenchPath);
    await page.getByRole("heading", { name: workbenchTitle, exact: true }).waitFor();
    assert.equal(await page.locator(".workstation-frame").count(), 0, "Workbench never mounts the backend-reading application");
    const storage = await page.evaluate(() => ({ local: { ...localStorage }, session: { ...sessionStorage } }));
    assert.equal(await page.locator("html").getAttribute("data-theme"), "light", "Workbench starts from its own defaults");
    if (axeSource) await contrastAudit(page, { name: "workbench-light" }, axeSource, report);
    for (const value of ["loading", "empty", "error", "populated"]) {
      await page.getByRole("combobox", { name: "Data state", exact: true }).selectOption(value);
      await page.locator('[data-workbench-data-state="' + value + '"]').waitFor();
      if (value === "error") {
        await page.getByRole("button", { name: "Retry synthetic records", exact: true }).click();
        await page.locator('[data-workbench-data-state="populated"]').waitFor();
      }
    }
    const primary = page.getByRole("button", { name: "Primary preview", exact: true });
    await page.getByRole("combobox", { name: "Button state", exact: true }).selectOption("disabled");
    await page.waitForFunction(element => element.disabled, await primary.elementHandle());
    assert(await primary.isDisabled());
    assert(await primary.getAttribute("aria-describedby") || await primary.getAttribute("title"), "Disabled specimen explains how to unblock");
    if (axeSource) await contrastAudit(page, { name: "workbench-disabled" }, axeSource, report);
    await page.getByRole("combobox", { name: "Button state", exact: true }).selectOption("busy");
    await primary.and(page.locator('[aria-busy="true"]')).waitFor();
    assert.equal(await primary.getAttribute("aria-busy"), "true");
    if (axeSource) await contrastAudit(page, { name: "workbench-busy" }, axeSource, report);
    await page.getByRole("combobox", { name: "Button state", exact: true }).selectOption("normal");
    await primary.click();
    await page.getByRole("textbox", { name: "Sample reference", exact: true }).fill("");
    await page.getByRole("button", { name: "Save sample reference", exact: true }).click();
    await page.getByText("Enter a sample reference.", { exact: true }).waitFor();
    if (axeSource) await contrastAudit(page, { name: "workbench-validation" }, axeSource, report);
    await page.getByRole("textbox", { name: "Sample reference", exact: true }).fill("SYN-123");
    await page.getByRole("button", { name: "Save sample reference", exact: true }).click();
    await page.getByText("Sample reference saved locally. No data was sent.", { exact: true }).waitFor();
    const row = page.getByRole("row", { name: "Select Negative sample", exact: true });
    await row.focus(); await page.keyboard.press("Enter");
    const panel = page.getByRole("region", { name: "Synthetic record inspector", exact: true });
    assert(await panel.evaluate(element => document.activeElement === element), "Row activation transfers focus to inspector");
    await page.keyboard.press("Escape");
    assert(await row.evaluate(element => document.activeElement === element), "Escape returns to selected row");
    const zeroRow = page.getByRole("row", { name: "Select Zero sample", exact: true });
    await zeroRow.focus(); await page.keyboard.press("Space");
    assert(await panel.evaluate(element => document.activeElement === element), "Space transfers focus to inspector");
    await page.keyboard.press("Escape");
    assert(await zeroRow.evaluate(element => document.activeElement === element));
    await page.getByRole("radiogroup", { name: "Appearance", exact: true }).getByRole("radio", { name: "Dark", exact: true }).click();
    await page.locator('html[data-theme="dark"]').waitFor();
    if (axeSource) await contrastAudit(page, { name: "workbench-dark" }, axeSource, report);
    await page.getByRole("combobox", { name: "Specimen surface", exact: true }).selectOption("light");
    await page.locator('[data-workbench-surface="light"]').waitFor();
    const nested = await localColors(page, '[data-workbench-surface="light"]');
    assert.equal(nested["--interaction-focus"], expectedFocus(false));
    if (axeSource) await contrastAudit(page, { name: "workbench-nested-light" }, axeSource, report);
    const rowHeights = [];
    for (const density of ["Compact", "Default", "Spacious"]) {
      await page.getByRole("radiogroup", { name: "Display density", exact: true }).getByRole("radio", { name: density, exact: true }).click();
      await page.waitForFunction(value => (document.body.getAttribute("data-theme-density") || "default") === value, density.toLowerCase());
      await settleTransitions(page);
      rowHeights.push(await zeroRow.evaluate(element => element.getBoundingClientRect().height));
    }
    scene.rowHeights = rowHeights;
    assert(rowHeights[0] < rowHeights[1] && rowHeights[1] < rowHeights[2], "Native table density visibly changes row height: " + JSON.stringify(rowHeights));
    await page.getByRole("radiogroup", { name: "Appearance", exact: true }).getByRole("radio", { name: "System", exact: true }).click();
    await page.locator("html:not([data-theme])").waitFor();
    await page.emulateMedia({ colorScheme: "dark" });
    assert.equal((await localColors(page))["--interaction-focus"], expectedFocus(true), "System appearance follows a dark OS");
    await page.emulateMedia({ colorScheme: "light" });
    assert.equal((await localColors(page))["--interaction-focus"], expectedFocus(false), "System appearance follows a light OS");
    scene.storageOperations = await page.evaluate(() => window.__workbenchStorageOperations);
    assert.deepEqual(scene.storageOperations, [], "Workbench does not read or write operator storage");
    assert.deepEqual(await page.evaluate(() => window.__workbenchBroadcasts), [], "Workbench sends no companion broadcasts");
    await page.reload(); await page.getByRole("heading", { name: workbenchTitle, exact: true }).waitFor();
    assert.equal(await page.getByRole("combobox", { name: "Specimen surface", exact: true }).inputValue(), "light", "URL state restores after reload");
    await page.getByRole("button", { name: "Reset workbench", exact: true }).click();
    await page.waitForURL(url => url.search === "");
    await page.locator('[data-workbench-surface="inherited"]').waitFor();
    assert.equal(await page.getByRole("combobox", { name: "Data state", exact: true }).inputValue(), "populated");
    assert.equal(await page.getByRole("combobox", { name: "Button state", exact: true }).inputValue(), "normal");
    assert.equal(await page.getByRole("combobox", { name: "Specimen surface", exact: true }).inputValue(), "inherited");
    await page.locator('html[data-theme="light"]').waitFor();
    assert.equal(await page.locator("body").getAttribute("data-theme-density"), null, "Reset restores default density");
    assert.deepEqual(await page.evaluate(() => ({ local: { ...localStorage }, session: { ...sessionStorage } })), storage, "Operator storage stays unchanged");
    assert.deepEqual(api, [], "Workbench makes zero API requests");
    assert.deepEqual(await page.evaluate(() => window.__workbenchStorageOperations), [], "Reset and restored URL use no operator storage");
    await page.setViewportSize({ width: 1366, height: 768 });
    await settleTransitions(page);
    assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), "Workbench has no root overflow");
    await allFontFaces(page); assertHealthyScene(scene);
    scene.screenshot = "workbench.png"; await page.screenshot({ path: path.join(output, scene.screenshot) });
    scene.status = "passed";
  } finally { scene.closing = true; await context.close(); delete scene.closing; }
}

export async function forcedColorProof(browser, base, registry, fixtures, helper, report, output) {
  const scene = { name: "forced-colors-navigation" }; report.expanded.push(scene);
  const context = await browser.newContext({ viewport: { width: 1366, height: 768 }, colorScheme: "dark", forcedColors: "active", reducedMotion: "reduce" });
  const page = await context.newPage(); sceneMonitor(page, scene, new URL(base).origin);
  await installFixtures(page, registry, fixtures, scene, helper);
  try {
    await page.goto(base + "/accounting/reconciliation?scenario=accounting.normal");
    await page.getByText("Reconciliation break queue", { exact: true }).waitFor();
    await page.waitForLoadState("networkidle");
    const masks = await page.locator(".operator-nav-item__asset").evaluateAll(elements => elements.map(element => {
      const style = getComputedStyle(element);
      return { mask: style.maskImage, fill: style.backgroundColor, parent: getComputedStyle(element.closest("a")).color };
    }));
    assert.equal(masks.length, 7);
    assert(masks.every(mask => mask.mask !== "none" && mask.fill === mask.parent), "Forced-color masks inherit system navigation text");
    const link = page.locator("a.operator-nav-item").first();
    await link.focus(); await page.keyboard.press("Tab"); await page.keyboard.press("Shift+Tab");
    const focus = await link.evaluate(element => ({ visible: element.matches(":focus-visible"), outline: getComputedStyle(element).outlineWidth }));
    assert(focus.visible && parseFloat(focus.outline) > 0, "Forced-colors keyboard outline remains visible");
    assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), "Forced-colors has no root overflow");
    assertHealthyScene(scene);
    scene.status = "passed"; scene.masks = masks; scene.focus = focus; scene.screenshot = "forced-colors-navigation.png";
    await page.screenshot({ path: path.join(output, scene.screenshot) });
  } finally { scene.closing = true; await context.close(); delete scene.closing; }
}

export async function runExpandedDesignSystemSmoke({ browser, base, backendBase, productionBase, sourceRoot, output, report, axeSource, timeoutMs }) {
  const helper = await import(pathToFileURL(path.resolve(sourceRoot, "../../../scripts/dev/web-screenshot-scenarios.mjs")).href);
  const loaded = await helper.loadScreenshotScenarioModule(sourceRoot);
  const fixtures = JSON.parse(await readFile(path.resolve(sourceRoot, "../../../scripts/dev/web-screenshot-fixtures.json"), "utf8")).routes;
  report.expanded = [];
  try {
    for (const config of designSystemMatrix()) {
      const scene = { name: config.name, route: config.route.path, viewport: { width: config.width, height: config.height }, appearance: config.appearance };
      report.expanded.push(scene);
      const context = await browser.newContext({ viewport: scene.viewport, colorScheme: config.appearance.os, reducedMotion: "reduce" });
      await context.addInitScript(({ key, stored }) => localStorage.setItem(key, stored), { key: appearanceKey, stored: config.appearance.stored });
      const page = await context.newPage(); page.setDefaultTimeout(timeoutMs);
      sceneMonitor(page, scene, new URL(base).origin);
      await installFixtures(page, loaded.module, fixtures, scene, helper);
      try {
        await page.goto(base + "/" + config.route.path + "?scenario=accounting.normal");
        await page.locator(".workstation-frame").waitFor();
        if (config.route.path === "portfolio") {
          assert.equal(new URL(page.url()).pathname, new URL(base + "/portfolio").pathname, "Canonical Portfolio route");
          await page.getByRole("heading", { name: "Portfolio", level: 1, exact: true }).waitFor();
          await page.getByRole("region", { name: "Portfolio workbench context", exact: true })
            .getByRole("heading", { name: "Execution-linked holdings", exact: true }).waitFor();
        }
        await page.getByText(config.route.ready, { exact: true }).first().waitFor();
        await page.waitForLoadState("networkidle");
        assert.equal(await page.locator("vite-error-overlay").count(), 0, "No framework overlay");
        assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), scene.name + ": no root overflow");
        const colors = await localColors(page);
        assert.equal(colors["--interaction-focus"], expectedFocus(config.appearance.dark));
        assert.equal(colors["--cyan-focus"], expectedFocus(config.appearance.dark));
        assert.equal(colors["--mode-paper"], expectedPaper(config.appearance.dark));
        scene.paper = await paperIndicator(page);
        scene.fonts = await allFontFaces(page); scene.colors = colors;
        const masks = await page.locator(".operator-nav-item__asset").evaluateAll(elements => elements.map(element => {
          const style = getComputedStyle(element);
          return { mask: style.maskImage, fill: style.backgroundColor, parent: getComputedStyle(element.closest("a")).color };
        }));
        assert.equal(masks.length, 7);
        assert(masks.every(mask => mask.mask !== "none" && mask.fill === mask.parent), "All navigation masks inherit theme text");
        if (config.route.path !== "trading") {
          scene.rails = await blueSelection(page, colors);
          scene.selectedText = await selectedContrast(page);
        }
        if (config.width === 1440 && ["light", "manual-dark"].includes(config.appearance.name)) {
          await contrastAudit(page, scene, axeSource, report);
          if (config.route.path === "accounting/reconciliation") await reconciliationInteractions(page, scene);
        }
        scene.screenshot = scene.name + ".png";
        await page.screenshot({ path: path.join(output, scene.screenshot), fullPage: false });
        assertHealthyScene(scene); scene.status = "passed";
      } catch (error) {
        scene.status = "failed"; scene.failure = error.stack || String(error);
        await page.screenshot({ path: path.join(output, scene.name + "-failure.png") }).catch(() => {});
        throw new Error(scene.name + ": " + error.message, { cause: error });
      } finally { scene.closing = true; await context.close(); delete scene.closing; }
    }
    await startupPreference(browser, base, loaded.module, fixtures, helper, report.expanded);
    await workbenchProof(browser, base, report, output, axeSource);
    await forcedColorProof(browser, base, loaded.module, fixtures, helper, report, output);
    for (const [kind, target] of [["backend-connected", backendBase], ["production", productionBase]]) {
      const scene = { name: "workbench-excluded-" + kind }; report.expanded.push(scene);
      const context = await browser.newContext({ viewport: { width: 1366, height: 768 } });
      const page = await context.newPage(); sceneMonitor(page, scene, new URL(target).origin);
      await installFixtures(page, loaded.module, fixtures, scene, helper);
      try {
        await page.goto(target + workbenchPath);
        await page.locator(".workstation-frame").waitFor();
        assert.equal(await page.locator('[data-design-system-workbench="true"]').count(), 0, kind + " cannot expose workbench");
        assertHealthyScene(scene); scene.status = "passed";
      } finally { scene.closing = true; await context.close(); delete scene.closing; }
    }
    const assetDirectory = path.resolve(sourceRoot, "../wwwroot/workstation/assets");
    for (const name of await readdir(assetDirectory)) {
      if (name.endsWith(".js")) assert(!(await readFile(path.join(assetDirectory, name), "utf8")).includes(workbenchTitle), "Production excludes workbench module: " + name);
    }
  } finally { await loaded.close(); }
}
