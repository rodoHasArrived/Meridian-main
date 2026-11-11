// Meridian conformance rules — the machine-readable form of the design law.
//
// Every rule here was a shipped defect first. 4.1's fix list was a taxonomy, not a set of
// accidents: a duplicate hardcoded DOM id, a `transition:all`, a pinned row height, a bare
// `Intl.NumberFormat`, a storage write on every pointermove. Each is regex-grade, so each is a
// build's job rather than a reviewer's. `tests/conformance.html` runs them; `docs/conformance.card.html`
// reports the last run. Rules carry their own fixtures, so the harness proves the rule works before
// it trusts the rule's silence.
//
// Severity: "blocked" = a release may not ship it · "review" = a finding that needs a decision.
(function (root) {
  const lines = (text) => text.split("\n");
  const at = (text, index) => text.slice(0, index).split("\n").length;
  const trim = (s) => s.replace(/\s+/g, " ").trim().slice(0, 140);

  // Find every regex match with its line number, minus the paths a rule exempts.
  function scanRe(re, text) {
    const out = [];
    const r = new RegExp(re.source, re.flags.includes("g") ? re.flags : re.flags + "g");
    let m;
    while ((m = r.exec(text))) {
      out.push({ line: at(text, m.index), snippet: trim(m[0]) });
      if (m.index === r.lastIndex) r.lastIndex++;
      if (out.length > 40) break;
    }
    return out;
  }
  const isJsx = (p) => /\.(jsx|tsx)$/.test(p);
  const isSource = (p) => /\.(jsx|tsx|js)$/.test(p) && !/\.d\.ts$/.test(p);
  const isCss = (p) => /\.css$/.test(p);
  const isCard = (p) => /\.card\.html$/.test(p);
  const isTemplate = (p) => /^templates\//.test(p);
  const isTemplateEntry = (p) => isTemplate(p) && /\.dc\.html$/.test(p) && !/^templates\/_/.test(p);

  // The one non-source input a rule reads. C-16 is a cross-file rule: the defect is not IN the
  // template, it is the absence of a record ABOUT it, so the rule needs the manifest alongside the
  // file. `runStatic` hands every rule the whole file map for exactly this.
  const ELIGIBILITY = "api/template-eligibility.json";

  // Comments are prose. A hex code or a font name inside one is documentation, not a style.
  const stripComments = (t) => t.replace(/\/\*[\s\S]*?\*\//g, "").replace(/^[ \t]*\/\/.*$/gm, "");

  // Rules whose defect class is real everywhere but whose ADOPTION across the screens is a release
  // of its own: blocking in the component tree, a recorded review finding in templates. The scope is
  // 35 `.dc.html` entries across 27 templates — the harness derives template paths from the
  // eligibility record, not from the manifest's one-entry-per-template list.
  const adoption = (p) => (isTemplate(p) ? "review" : "blocked");

  // var(--token, #fallback) is the sanctioned way a component names a fallback. Strip those before
  // looking for raw hex, or every conformant component reads as a violation.
  const stripVarFallbacks = (t) => t.replace(/var\(\s*--[A-Za-z0-9-]+\s*,[^()]*\)/g, match => match.replace(/[^\r\n]/g, " "));

  const RULES = [
    {
      id: "C-01", title: "Numbers format through core/format.js", severity: adoption,
      why: "Ten modules each welded `en-US` into a formatter, so no surface could be told otherwise; precision had no owner either and 4.1 fixed decimals by hand twice.",
      fix: "Call the authority: `formatMoney/formatNumber/formatPrice…` from `core/format.js`, `useFormat()` in a component, or `MeridianDesignSystem.Format` in a template.",
      applies: (p) => (isSource(p) || isTemplate(p)) && !/components\/core\/format\.js$/.test(p) && !isCard(p),
      scan: (t) => scanRe(/new Intl\.(?:NumberFormat|PluralRules)\(|\.toLocaleString\(\s*["'][a-z]{2}-[A-Z]{2}["']/, t),
      fixtures: { bad: `const m = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2 });`, good: `const m = (v) => formatMoney(v, { currency });` },
    },
    {
      id: "C-02", title: "A calendar date never goes through toISOString()", severity: adoption,
      why: "It converts local midnight to UTC and moves the date back a day for every operator west of Greenwich — 4.1's headline defect.",
      fix: "`localDate.toISO` / `fromISO` / `dayOfInstant`.",
      applies: (p) => isSource(p) || isTemplate(p),
      // `toISOString().slice(11, 19)` is time-of-day on an INSTANT and legitimate; the defect is
      // taking the first ten characters and calling the result a calendar date.
      scan: (t) => scanRe(/toISOString\(\)\s*\.\s*(?:slice|substring)\s*\(\s*0\s*,\s*10\s*\)|toISOString\(\)\s*\.\s*split\(\s*["']T["']\s*\)\s*\[\s*0\s*\]/, t),
      fixtures: { bad: `const day = d.toISOString().slice(0, 10);`, good: `const t = d.toISOString().slice(11, 19) + "Z";` },
    },
    {
      id: "C-03", title: "No literal DOM id in a component", severity: adoption,
      why: "`Combobox` hardcoded one id for every instance, so two on a screen shared an aria relationship and the second one silently pointed at the first.",
      fix: "`React.useId()`, or an id derived from a prop.",
      applies: (p) => isJsx(p),
      scan: (t) => scanRe(/\bid=(?:"[^"${}]+"|\{\s*"[^"${}]+"\s*\})/, t),
      fixtures: { bad: `<input id="mds-combobox-input" />`, good: `<input id={\`\${uid}-input\`} />` },
    },
    {
      id: "C-04", title: "`transition: all` is not one of the four motion verbs", severity: "blocked",
      why: "The motion law names four verbs (reflow, trace, reconcile, focus) on declared properties; `all` animates layout and paint the system never agreed to move.",
      fix: "Name the properties: `transition: background-color 120ms cubic-bezier(.2,0,0,1)`.",
      applies: (p) => isSource(p) || isCss(p),
      scan: (t) => scanRe(/transition\s*:\s*all\b/, t),
      fixtures: { bad: `.x{transition:all .2s ease}`, good: `.x{transition:background-color 120ms cubic-bezier(.2,0,0,1)}` },
    },
    {
      id: "C-05", title: "Row and control heights come from density tokens", severity: "review",
      why: "`Stepper` and `TreeView` pinned row heights in px, so `DensityToggle` moved every other row on the screen and not theirs.",
      fix: "`var(--table-row-height)` / `var(--theme-row-height)` / `var(--row-review)` for rows, `var(--theme-control-height)` for controls — these are the names that exist (`tokens/theme.css`, `tokens/elevation.css`). `--row-dense`, `--row-standard` and `--control-h` are NOT tokens; this rule's fix advised them for two releases and the `decision/` components duly pinned themselves to dead names.",
      applies: (p) => isSource(p) || isCss(p),
      // Judged per line: the pinned height must be on something that is a row, not on an icon.
      // A row is named as one: the line must carry a row-ish CLASS and a pinned box height in the
      // range a row actually occupies (24–96px). `max-height` is a scroll cap, not a row height.
      scan: (t) => lines(t).map((l, i) => ({ l, i })).filter(({ l }) => /[.&_-](row|item|cell|node|option|step|lane|entry|guard)\b/i.test(l) && /(?:min-)?height\s*:\s*(?:2[4-9]|[3-8]\d|9[0-6])px/.test(l.replace(/max-height\s*:[^;}]*/g, "")))
        .map(({ l, i }) => ({ line: i + 1, snippet: trim(l) })).slice(0, 40),
      fixtures: { bad: `.mds-x__row{min-height:30px}`, good: `.mds-x__row{min-height:var(--row-standard,36px)}` },
    },
    {
      id: "C-06", title: "Every <button> declares its type", severity: adoption,
      why: "`DatePicker`'s calendar buttons had no type, so inside a form every click was a submit.",
      fix: `type="button" unless it really is the submit.`,
      applies: (p) => isJsx(p) || isTemplate(p),
      // A JSX opening tag can contain `>` inside an expression (`ref={(el) => …}`), so the tag
      // cannot be matched by "up to the next >" — look at a window instead.
      scan: (t) => {
        const out = []; const src = stripComments(t); const re = /<button\b/g; let m;
        while ((m = re.exec(src))) {
          const t2 = src;
          const win = t2.slice(m.index, m.index + 320);
          if (!/\btype=/.test(win.split("</button")[0])) out.push({ line: at(t2, m.index), snippet: trim(win.slice(0, 90)) });
          if (out.length > 40) break;
        }
        return out;
      },
      fixtures: { bad: `<button className="x" onClick={go}>Go</button>`, good: `<button type="button" onClick={go}>Go</button>` },
    },
    {
      id: "C-07", title: "No raw hex outside a var() fallback", severity: "review",
      why: "A hex literal is a color the theme cannot reach — it survives dark mode, contrast modes and white-labelling unchanged.",
      fix: "`var(--token, #fallback)`; add a token if none fits.",
      applies: (p) => isJsx(p),
      scan: (t) => scanRe(/#[0-9a-fA-F]{6}\b/, stripVarFallbacks(stripComments(t))),
      fixtures: { bad: `<div style={{ color: "#C56343" }} />`, good: `<div style={{ color: "var(--brand-signal, #C56343)" }} />` },
    },
    {
      id: "C-08", title: "No remote @import or remote url() in the token layer", severity: "blocked",
      why: "A self-hosted system that fetches a font or a stylesheet at runtime is not self-hosted, and the desk is often air-gapped.",
      fix: "Ship the asset under `assets/` and reference it relatively.",
      applies: (p) => isCss(p) || isJsx(p),
      scan: (t) => scanRe(/@import\s+(?:url\()?\s*["']?https?:|url\(\s*["']?https?:/, t),
      fixtures: { bad: `@import url("https://fonts.googleapis.com/css2?family=Inter");`, good: `@import "./fonts.css";` },
    },
    {
      id: "C-09", title: "No storage write on a continuous event", severity: "blocked",
      why: "`SplitPane` wrote localStorage on every `pointermove` — a synchronous disk write per frame while dragging.",
      fix: "Write on `pointerup`, or debounce with a trailing timer.",
      applies: (p) => isSource(p),
      scan: (t) => {
        const out = [];
        const r = /(pointermove|mousemove|onScroll|onResize)/g; let m;
        while ((m = r.exec(t))) {
          const window_ = t.slice(m.index, m.index + 600);
          if (/setItem\s*\(/.test(window_)) out.push({ line: at(t, m.index), snippet: trim(m[0] + " … setItem(") });
        }
        return out;
      },
      fixtures: { bad: `el.addEventListener("pointermove", (e) => { localStorage.setItem("k", e.x); });`, good: `el.addEventListener("pointerup", () => localStorage.setItem("k", pos));` },
    },
    {
      id: "C-10", title: "No positive tabIndex", severity: "blocked",
      why: "A positive tabIndex moves an element out of DOM order, so focus order stops matching reading order for everyone else on the screen.",
      fix: "`tabIndex={0}` or `-1` only; order by DOM position.",
      applies: (p) => isJsx(p) || isTemplate(p),
      scan: (t) => scanRe(/tabIndex=\{?\s*["']?[1-9]/, t),
      fixtures: { bad: `<div tabIndex={3} />`, good: `<div tabIndex={0} />` },
    },
    {
      id: "C-11", title: "Synthetic list rows declare their position", severity: "review",
      why: "`TreeView` rendered `role=\"treeitem\"` without `aria-setsize`/`aria-posinset`, so a screen reader announced no position in a list it could not otherwise count.",
      fix: "`aria-setsize` + `aria-posinset` on every synthetic row, or use real list semantics.",
      applies: (p) => isJsx(p),
      scan: (t) => (/role="(?:treeitem|option|row|listitem)"/.test(t) && !/aria-posinset/.test(t)
        ? scanRe(/role="(?:treeitem|option|row|listitem)"/, t).slice(0, 1) : []),
      fixtures: { bad: `<li role="treeitem" aria-expanded={open}>{label}</li>`, good: `<li role="treeitem" aria-posinset={i + 1} aria-setsize={n}>{label}</li>` },
    },
    {
      id: "C-12", title: "Fonts are IBM Plex, declared through tokens", severity: "review",
      why: "The emitted adherence ruleset forbids any other family; a stray stack is how a component stops matching the desktop app.",
      fix: "`var(--font-body)` / `var(--font-data)`.",
      applies: (p) => isJsx(p) || isCss(p),
      scan: (t) => scanRe(/font-family\s*:(?!\s*(?:var\(|inherit|initial|unset|["']?IBM Plex|sans-serif|monospace))[^;}\n]+/, stripComments(t)),
      fixtures: { bad: `.x{font-family:Inter,sans-serif}`, good: `.x{font-family:var(--font-body,"IBM Plex Sans",sans-serif)}` },
    },
    {
      id: "C-13", title: "An instruction's fate is declared, not implied", severity: "review",
      why: "A control that mutates something outside the screen and shows only a toast leaves the operator unable to tell landed from unlanded.",
      fix: "Wrap the submit path in `ActionEnvelope` / `ActionEnvelopeProvider` (guidelines/ACTION_SEMANTICS.md).",
      applies: (p) => isTemplate(p) && /\.dc\.html$/.test(p),
      scan: (t) => {
        const mutates = /onSubmit|submit\s*\(|Post\b|Enqueue|Deliver/.test(t);
        const declares = /ActionEnvelope|ActionEnvelopeProvider|ActionStateChip/.test(t);
        return mutates && !declares ? [{ line: 1, snippet: "screen submits an instruction with no declared fate" }] : [];
      },
      fixtures: { path: "templates/probe/Probe.dc.html", bad: `<button onSubmit={() => submit(order)}>Send</button>`, good: `<ActionEnvelope action="order.submit" />` },
    },
    {
      id: "C-14", title: "An `!important` states why, or does not exist", severity: "review",
      why: "The gate forbade the string outright, so the four legitimate cases (a captured pointer's cursor, print unrolling an inline drag size) read the same as the illegitimate ones — and prose saying `no !important needed` failed too.",
      fix: "Win by order or specificity; where neither can reach (inline style, universal descendant), carry `/* important: <reason> */` on the declaration.",
      applies: (p) => isJsx(p) && !isTemplate(p),
      scan: (t) => {
        const blanked = t.replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, " ")).replace(/^([ \t]*)\/\/.*$/gm, "$1").split("\n");
        const raw = t.split("\n"), out = [];
        blanked.forEach((line, i) => {
          if (!/!important/.test(line)) return;
          if (/\/\*\s*important:\s*\S/.test(raw[i])) return;
          out.push({ line: i + 1, snippet: trim(raw[i]) });
        });
        return out;
      },
      fixtures: { bad: `.x{flex:0 0 auto!important}`, good: `.x{cursor:col-resize !important} /* important: pointer is captured by body */` },
    },
    {
      id: "C-15", title: "One declaration per canonical domain union", severity: "blocked",
      why: "`DecisionFlow` kept its own `FinancialObjectType` alongside `ObjectModel`'s, minus `strategy` — so the seventeenth object existed for the header that displays it and not for the flow that approves it.",
      fix: "Import the canonical type: `import type { ObjectRef, MeridianObjectType } from \"../core/ObjectModel\"`.",
      applies: (p) => /\.d\.ts$/.test(p) && !/core\/(ObjectModel|TimeContext|Registry)\.d\.ts$/.test(p),
      scan: (t) => scanRe(/export\s+(?:type|interface)\s+\w*(?:ObjectType|ObjectRef|TimeRole|MaterialityClass)\b/, stripComments(t)),
      fixtures: { bad: `export type FinancialObjectType="security"|"position";`, good: `import type{ObjectRef}from"../core/ObjectModel";` },
    },
    {
      id: "C-16", title: "Every template is classified for primitive eligibility", severity: "blocked",
      why: "Adoption is published as conforming ÷ eligible. A template nobody classified is in neither term, so shipping one moves the printed percentage without anything having been migrated — the denominator shrinks and the number reads as progress. 14.5% is only a measurement while the classification is total.",
      fix: "Classify the slug in `api/template-eligibility.json`: its `.dc.html` `entries`, an `eligible` list, and an `exempt` map citing a declared reason code for every remaining primitive. The judgment is per screen (`guidelines/TEMPLATE_CONTRACT.md`); `scripts/check_template_eligibility.py` then checks reference counts, strays and arithmetic.",
      applies: isTemplateEntry,
      scan: (text, path, files) => {
        const raw = files && files[ELIGIBILITY];
        // Never silent: an absent manifest means the rule could not decide, which is a finding about
        // the gate rather than about the screen — but it must not read as a pass.
        if (typeof raw !== "string") return [{ line: 1, snippet: `eligibility unverifiable — ${ELIGIBILITY} was not loaded into the scan` }];
        let mf;
        try { mf = JSON.parse(raw); } catch (e) { return [{ line: 1, snippet: `${ELIGIBILITY} does not parse: ${e.message}` }]; }
        const m = path.match(/^templates\/([^/]+)\/(.+)$/);
        if (!m) return [];
        const [, slug, file] = m;
        const t = (mf.templates || {})[slug];
        if (!t) return [{ line: 1, snippet: `templates/${slug}/ is not classified — classify it before it ships or the adoption figure is a lie` }];
        const out = [];
        const entries = t.entries || [];
        if (!entries.includes(file)) out.push({ line: 1, snippet: `${slug} is classified but does not declare ${file} (declares: ${entries.join(", ") || "nothing"})` });
        const prims = Object.keys(mf.primitives || {});
        const claimed = new Set([...(t.eligible || []), ...Object.keys(t.exempt || {})]);
        const unclassified = prims.filter((k) => !claimed.has(k));
        if (unclassified.length) out.push({ line: 1, snippet: `${slug} leaves ${unclassified.join(", ")} neither eligible nor exempt` });
        const reasons = mf.exemptionReasons || {};
        const undeclared = Object.entries(t.exempt || {}).filter(([, code]) => !reasons[code]).map(([k, code]) => `${k}: ${code}`);
        if (undeclared.length) out.push({ line: 1, snippet: `${slug} cites undeclared exemption reason(s) — ${undeclared.join(", ")}` });
        return out;
      },
      fixtures: {
        files: { [ELIGIBILITY]: JSON.stringify({
          primitives: { ctx: "WorkspaceContextBar", dat: "Datum" },
          exemptionReasons: { "no-reference-line": "No zero, target or tolerance reference carries meaning here." },
          templates: { probe: { entries: ["Probe.dc.html"], eligible: ["ctx"], exempt: { dat: "no-reference-line" }, conforming: [] } },
        }) },
        badPath: "templates/unclassified/Unclassified.dc.html",
        goodPath: "templates/probe/Probe.dc.html",
        bad: `<x-dc><section>a new screen nobody classified</section></x-dc>`,
        good: `<x-dc><section>a classified screen</section></x-dc>`,
      },
    },
  ];

  /**
   * Run every applicable rule over a { path: text } map.
   * @returns {{violations: Array, byRule: Object, files: number}}
   */
  function runStatic(files) {
    const violations = [], byRule = {};
    for (const rule of RULES) byRule[rule.id] = { rule, hits: [] };
    for (const [path, text] of Object.entries(files)) {
      if (typeof text !== "string") continue;
      for (const rule of RULES) {
        if (rule.applies && !rule.applies(path)) continue;
        let hits = [];
        try { hits = rule.scan(text, path, files) || []; } catch (e) { hits = [{ line: 0, snippet: "rule threw: " + e.message }]; }
        const severity = typeof rule.severity === "function" ? rule.severity(path) : rule.severity;
        for (const h of hits) {
          const v = { rule: rule.id, severity, path, ...h };
          violations.push(v); byRule[rule.id].hits.push(v);
        }
      }
    }
    return { violations, byRule, files: Object.keys(files).length };
  }

  /** Each rule must flag its own bad fixture and stay silent on the good one. A rule that cannot do that proves nothing. */
  function selfTest() {
    const DEFAULT_PATH = "components/core/Probe.jsx";
    return RULES.map((r) => {
      // A fixture may need its own path (a template-scoped rule) or a different path for each half
      // (C-16's whole subject is which path is on record), and a cross-file rule needs its own file map.
      const f = r.fixtures;
      const badPath = f.badPath || f.path || DEFAULT_PATH;
      const goodPath = f.goodPath || f.path || DEFAULT_PATH;
      let bad = [], good = [];
      try { bad = r.scan(f.bad, badPath, f.files) || []; good = r.scan(f.good, goodPath, f.files) || []; } catch (e) { /* reported below */ }
      return { id: r.id, catches: bad.length > 0, quietOnGood: good.length === 0 };
    });
  }

  /**
   * Assertions run inside a rendered specimen card. Each returns null (pass) or a message.
   * Deliberately conservative: everything here is decidable from the DOM of a loaded card.
   */
  const CARD_ASSERTIONS = [
    { id: "mounted", label: "card mounts and renders content", run: (doc, win = doc.defaultView) => {
      if (!doc.body) return "card rendered (almost) nothing";
      // Count rendered content, not inline demo scripts or an arbitrary number
      // of wrappers. Image/type specimens are intentionally small documents.
      if (doc.body.innerText.trim().length > 40) return null;
      const name = (el) => (el.getAttribute("aria-label") ||
        (el.getAttribute("aria-labelledby") || "").split(/\s+/).map((id) => doc.getElementById(id)?.textContent || "").join(" ").trim() ||
        el.getAttribute("alt") || el.querySelector("title")?.textContent || "").trim();
      const rendered = (el) => {
        const box = el.getBoundingClientRect(), style = win.getComputedStyle(el);
        return box.width > 0 && box.height > 0 && style.display !== "none" && style.visibility !== "hidden" &&
          !el.closest('[hidden],[aria-hidden="true"]');
      };
      const image = [...doc.images].some((el) => rendered(el) && name(el) && el.complete && el.naturalWidth > 0 && el.naturalHeight > 0);
      const graphic = [...doc.querySelectorAll("svg")].some((el) => rendered(el) && name(el) && el.querySelector("path,rect,circle,line,polyline,polygon,text,use,image"));
      return image || graphic ? null : "card rendered (almost) nothing";
    } },
    { id: "bundle", label: "no component threw while evaluating", run: (doc, win) => { const ns = Object.keys(win).find((k) => /^MeridianDesignSystem_/.test(k)); const errs = ns && win[ns] && win[ns].__errors; return errs && errs.length ? `${errs.length} bundle error(s): ${errs.slice(0, 2).map((e) => e.path).join(", ")}` : null; } },
    { id: "unique-ids", label: "no duplicate DOM id", run: (doc) => { const seen = new Set(), dupes = new Set(); doc.querySelectorAll("[id]").forEach((el) => { if (seen.has(el.id)) dupes.add(el.id); seen.add(el.id); }); return dupes.size ? "duplicate id: " + [...dupes].slice(0, 3).join(", ") : null; } },
    { id: "names", label: "every control has an accessible name", run: (doc) => {
      const bad = [];
      doc.querySelectorAll("button,[role=button],a[href],input:not([type=hidden]),select,textarea").forEach((el) => {
        const label = (el.getAttribute("aria-label") || el.getAttribute("title") || el.textContent || "").trim();
        const ref = el.getAttribute("aria-labelledby");
        const labelled = ref ? ref.split(/\s+/).some((id) => doc.getElementById(id)) : false;
        const wrapped = el.closest("label") || (el.id && doc.querySelector(`label[for="${el.id}"]`));
        if (!label && !labelled && !wrapped && !el.getAttribute("placeholder")) bad.push(el.tagName.toLowerCase() + (el.className ? "." + String(el.className).split(" ")[0] : ""));
      });
      return bad.length ? `${bad.length} unnamed: ${bad.slice(0, 3).join(", ")}` : null;
    } },
    { id: "button-type", label: "every <button> has an explicit type", run: (doc) => { const bad = [...doc.querySelectorAll("button:not([type])")]; return bad.length ? `${bad.length} button(s) without type` : null; } },
    { id: "tabindex", label: "no positive tabindex", run: (doc) => { const bad = [...doc.querySelectorAll("[tabindex]")].filter((el) => Number(el.getAttribute("tabindex")) > 0); return bad.length ? `${bad.length} element(s) with a positive tabindex` : null; } },
    { id: "transition-all", label: "no active `transition-property: all`", run: (doc, win) => {
      const els = [...doc.querySelectorAll("*")].slice(0, 500);
      const bad = els.filter((el) => {
        const style = win.getComputedStyle(el);
        const properties = style.transitionProperty.split(",").map((value) => value.trim());
        const durations = style.transitionDuration.split(",").map((value) => parseFloat(value));
        return properties.some((property, i) => property === "all" && durations[i % durations.length] > 0);
      });
      return bad.length ? `${bad.length} element(s) transition every property` : null;
    } },
    { id: "dialog", label: "any dialog is modal and named", run: (doc) => { const bad = [...doc.querySelectorAll('[role=dialog],[role=alertdialog]')].filter((el) => !el.getAttribute("aria-label") && !el.getAttribute("aria-labelledby")); return bad.length ? `${bad.length} unnamed dialog(s)` : null; } },
    { id: "tokens-resolve", label: "active component token references resolve in their scope", run: (doc, win) => {
      // C-05's own fix string named --row-dense / --control-h for two releases; neither is a token,
      // so components that followed the advice were permanently pinned to their px fallbacks and no
      // static scan noticed. A card has the whole closure loaded — so ask it.
      const missing = new Map(), unverifiable = new Set(), computed = new WeakMap();
      const styleFor = (el, pseudo = "") => {
        if (!computed.has(el)) computed.set(el, new Map());
        const styles = computed.get(el);
        if (!styles.has(pseudo)) styles.set(pseudo, win.getComputedStyle(el, pseudo || null));
        return styles.get(pseudo);
      };
      const selectors = (text) => {
        const out = []; let start = 0, depth = 0, quote = null, escaped = false;
        for (let i = 0; i < text.length; i++) {
          const c = text[i];
          if (escaped) { escaped = false; continue; }
          if (c === "\\") { escaped = true; continue; }
          if (quote) { if (c === quote) quote = null; continue; }
          if (c === '"' || c === "'") { quote = c; continue; }
          if (c === "(" || c === "[") depth++;
          if (c === ")" || c === "]") depth--;
          if (c === "," && depth === 0) { out.push(text.slice(start, i).trim()); start = i + 1; }
        }
        out.push(text.slice(start).trim());
        return out.filter(Boolean);
      };
      const nest = (child, parent) => {
        let out = "", used = false, quote = null, escaped = false;
        for (const c of child) {
          if (escaped) { out += c; escaped = false; continue; }
          if (c === "\\") { out += c; escaped = true; continue; }
          if (quote) { out += c; if (c === quote) quote = null; continue; }
          if (c === '"' || c === "'") { out += c; quote = c; continue; }
          if (c === "&") { out += parent; used = true; } else out += c;
        }
        return used ? out : `${parent} ${child}`;
      };
      const references = (style, matches, owner) => {
        if (!style || !matches.length) return;
        const names = new Set();
        // CSSOM can expose an unresolved shorthand as empty individual
        // longhands. cssText retains its var() calls; quoted labels are text.
        const value = style.cssText.replace(/\/\*[\s\S]*?\*\/|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'/g, " ");
        for (const m of value.matchAll(/var\(\s*(--[A-Za-z0-9-]+)/g)) names.add(m[1]);
        for (const { el, pseudo } of matches) for (const name of names) {
          if (!styleFor(el, pseudo).getPropertyValue(name).trim() && !missing.has(name)) missing.set(name, owner);
        }
      };
      const walk = (list, owner, parents = []) => { for (const rule of list) {
        if (rule.type === win.CSSRule.MEDIA_RULE && !win.matchMedia(rule.conditionText).matches) continue;
        if (rule.type === win.CSSRule.SUPPORTS_RULE && !win.CSS.supports(rule.conditionText)) continue;
        if (rule.styleSheet) {
          if (rule.media.mediaText && !win.matchMedia(rule.media.mediaText).matches) continue;
          try { walk(rule.styleSheet.cssRules, owner, parents); }
          catch (e) { unverifiable.add(`imported stylesheet (${owner})`); }
        }
        if (rule.type === win.CSSRule.KEYFRAMES_RULE) {
          const animated = [...doc.querySelectorAll("*")].filter((el) => styleFor(el).animationName.split(",").map((name) => name.trim()).includes(rule.name));
          for (const frame of rule.cssRules) references(frame.style, animated.map((el) => ({ el, pseudo: "" })), owner);
          continue;
        }
        let resolved = parents;
        if (rule.selectorText) {
          const own = selectors(rule.selectorText);
          resolved = parents.length ? own.flatMap((child) => parents.map((parent) => nest(child, parent))) : own;
          const matches = [];
          for (const selector of resolved) {
            const pseudo = selector.match(/::[\w-]+(?:\([^)]*\))?$/)?.[0] || "";
            const target = (pseudo ? selector.slice(0, -pseudo.length) : selector).trim() || "*";
            try { doc.querySelectorAll(target).forEach((el) => matches.push({ el, pseudo })); }
            catch (e) { unverifiable.add(`${selector} (${owner})`); }
          }
          references(rule.style, matches, owner);
        }
        // Read a style rule before its nested cssRules, which can be empty.
        if (rule.cssRules) walk(rule.cssRules, owner, resolved);
      } };
      for (const style of doc.querySelectorAll("style[data-mds]")) {
        try { walk(style.sheet.cssRules, style.getAttribute("data-mds")); }
        catch (e) { unverifiable.add(`stylesheet (${style.getAttribute("data-mds")})`); }
      }
      const notes = [];
      if (missing.size) notes.push(`${missing.size} invented token(s): ` + [...missing].slice(0, 4).map(([name, owner]) => `${name} (${owner})`).join(", "));
      if (unverifiable.size) notes.push("token scope could not be checked: " + [...unverifiable].slice(0, 2).join(", "));
      return notes.length ? notes.join("; ") : null;
    } },
    { id: "eligibility", label: "a published adoption figure recomputes from its own rows", run: (doc, win) => {
      // Applies only to a card that publishes the classification it is rendering (window.__eligibility).
      // The static gate proves every template is ON the record; this proves the record's arithmetic and
      // the figure printed from it agree — a hand-typed percentage in a card is the drift that made the
      // 284/118 census outlive two releases.
      const e = win.__eligibility;
      if (!e || !e.templates) return null;
      const prims = Object.keys(e.primitives || {});
      const bad = [];
      let elig = 0, conf = 0;
      for (const [slug, t] of Object.entries(e.templates)) {
        const ee = t.eligible || [], ex = Object.keys(t.exempt || {}), cf = t.conforming || [];
        if (new Set([...ee, ...ex]).size !== prims.length) bad.push(`${slug} classifies ${new Set([...ee, ...ex]).size} of ${prims.length} primitives`);
        const overclaim = cf.filter((k) => !ee.includes(k));
        if (overclaim.length) bad.push(`${slug} conforms on ineligible ${overclaim.join(", ")}`);
        elig += ee.length; conf += cf.length;
      }
      const pct = elig ? Math.round((conf / elig) * 1000) / 10 : 0;
      const totals = e.totals || {};
      if (totals.eligibleCells !== undefined && (totals.eligibleCells !== elig || totals.conformingCells !== conf))
        bad.push(`totals declare ${totals.eligibleCells}/${totals.conformingCells}, rows recompute ${elig}/${conf}`);
      const printed = (doc.body.textContent.match(/[\d]+(?:\.[\d])?(?=\s*%)/g) || []).map(Number);
      if (!printed.some((n) => Math.abs(n - pct) < 0.05)) bad.push(`the page prints no ${pct}% — the rendered adoption figure does not match the rows below it`);
      return bad.length ? bad.slice(0, 3).join(" · ") : null;
    } },
    { id: "focus-visible", label: "focus is visible on the first control", run: (doc, win) => {
      const el = [...doc.querySelectorAll("button,[role=button],a[href],input:not([type=hidden]),select,textarea")].find((node) => {
        const style = win.getComputedStyle(node);
        return !node.matches(":disabled") && !node.closest("[inert]") && node.tabIndex >= 0 && node.getClientRects().length > 0 && style.visibility === "visible";
      });
      if (!el) return null;
      if (doc.activeElement === el) el.blur();
      const nodes = []; for (let node = el; node && node !== doc.body; node = node.parentElement) nodes.push(node);
      const indicator = (node) => {
        const s = win.getComputedStyle(node);
        return { outline: s.outlineStyle !== "none" && parseFloat(s.outlineWidth) > 0 ? `${s.outlineStyle} ${s.outlineWidth} ${s.outlineColor}` : null, shadow: s.boxShadow };
      };
      const before = nodes.map(indicator);
      // Ask for the keyboard focus appearance, rather than inheriting the mouse
      // modality of the Run button. Compound fields draw that ring on a wrapper.
      el.focus({ focusVisible: true });
      if (doc.activeElement !== el) return "first control could not take focus";
      const has = nodes.some((node, i) => {
        const after = indicator(node);
        return (after.outline && after.outline !== before[i].outline) || (after.shadow !== "none" && after.shadow !== before[i].shadow);
      });
      return has ? null : "no visible focus indicator on the first control";
    } },
  ];

  /** How a rule's severity reads in a table: a fixed word, or "scoped" when it depends on the tree. */
  const severityLabel = (rule) => (typeof rule.severity === "function" ? "scoped" : rule.severity);

  root.MeridianConformance = { RULES, runStatic, selfTest, CARD_ASSERTIONS, severityLabel };
  if (typeof module !== "undefined" && module.exports) module.exports = root.MeridianConformance;
})(typeof window !== "undefined" ? window : globalThis);
