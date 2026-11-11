#!/usr/bin/env python3
"""Meridian — token parity gate (stdlib only, offline).

tokens/parity-manifest.json has claimed since 3.1 that this script exists. It didn't.
This is it, scoped to what is actually shared with the upstream app:

  1 NAMES   Every name in aliasContract + workingNameContract resolves in the CSS
            closure (tokens/*.css). The upstream console authors CSS against these
            names; a missing one means pasted app CSS silently resolves to nothing.
  2 ALIASES Every contract name is a var() reference, not a duplicate hex — an alias
            that hardcodes a value can drift from the token it mirrors.
  3 VALUES  Every entry in `tokens` matches tokens/*.css: the light value in the
            default theme graph, the dark value in BOTH dark blocks of
            colors-dark.css, and the light value re-asserted in the forced-light block.

  4 THEME    The ten --theme-* contract names (plus focus and chrome inputs) are Tier-1
            inputs authored as light literals. Both dark blocks must re-point each one at its flipped
            component token, and the forced-light block must state that component
            token as a literal — otherwise the name stays light on a dark screen.
  5 UPSTREAM When the package sits in the Meridian monorepo, every name the browser
            workstation (src/Meridian.Ui/dashboard/src/styles/index.css) declares in
            common with this package must resolve to the same value in light, auto
            dark, manual dark and forced light. Since the 4.3.0 migration the
            workstation carries this palette, so parity-manifest.json declares
            valuesShared:true. A deliberate difference is listed under
            upstreamValueDivergences with its reason; one that stops differing fails,
            so an exemption cannot outlive the difference it excuses.

  python scripts/check_token_parity.py          # gate
  python scripts/check_token_parity.py --names  # names only, skip the value passes
"""
import colorsys, json, pathlib, re, sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
MANIFEST = ROOT / "tokens" / "parity-manifest.json"
CLOSURE = ["colors.css", "theme.css", "colors-dark.css", "visualization.css",
           "base.css", "elevation.css", "typography.css", "fonts.css",
           "print.css", "contrast-modes.css"]

DECL = re.compile(r"(?:^|[;{])\s*(--[a-z][a-z0-9-]*)\s*:\s*([^;}]+)", re.M)
PLAIN_ROOT = re.compile(r"(?:^|\})\s*(?::root|\.meridian-root)\s*\{", re.M)


def norm(value):
    v = " ".join(value.split()).lower().rstrip(";")
    v = re.sub(r"/\*.*?\*/", "", v).strip()
    m = re.fullmatch(r"#([0-9a-f])([0-9a-f])([0-9a-f])", v)
    if m:
        return "#" + "".join(c * 2 for c in m.groups())
    return v


def strip_comments(text):
    return re.sub(r"/\*.*?\*/", " ", text, flags=re.S)


def blocks(text):
    """Yield (selector, body) for every top-level-ish rule block, comments stripped."""
    text = strip_comments(text)
    out, depth, start, sel_start = [], 0, None, 0
    for i, ch in enumerate(text):
        if ch == "{":
            depth += 1
            if depth == 1:
                out.append([text[sel_start:i].strip(), i + 1, None])
                start = i + 1
        elif ch == "}":
            if depth == 1 and out:
                out[-1][2] = i
                sel_start = i + 1
            depth = max(0, depth - 1)
    return [(s, text[a:b]) for s, a, b in out if b is not None]


def declared(body):
    return {m.group(1): norm(m.group(2)) for m in DECL.finditer(body)}


def light_graph(files):
    """Default theme graph: plain :root blocks of colors.css then theme.css, in order."""
    graph = {}
    for name in ("colors.css", "theme.css"):
        text = files.get(name, "")
        for sel, body in blocks(text):
            if sel in (":root", ".meridian-root", ":root, .meridian-root"):
                graph.update(declared(body))
        # media-wrapped or attribute-scoped blocks are other branches of the graph
    return graph


def dark_blocks(text):
    """The three declared blocks of colors-dark.css, by selector shape."""
    found = {"auto": {}, "manual": {}, "forced-light": {}}
    text = strip_comments(text)
    # 1. @media (prefers-color-scheme: dark) { :root { ... } }
    # Read through the nesting-aware walker. A greedy `{(.*)\n}` capture ran on to the end of
    # the file, so the manual block's declarations were filed under "auto" as well and a token
    # missing from the media block alone went unseen.
    for path, body in flat_blocks(text):
        parts = path.split(" >> ")
        if (len(parts) > 1 and "prefers-color-scheme:dark" in parts[0].replace(" ", "")
                and ":root" in parts[-1].replace(" ", "").split(",")):
            found["auto"].update(declared(body))
    for sel, body in blocks(text):
        s = sel.replace(" ", "")
        if 'data-theme="dark"' in s:
            found["manual"].update(declared(body))
        elif 'data-theme="light"' in s:
            found["forced-light"].update(declared(body))
    return found


UPSTREAM_INDEX = pathlib.Path("src/Meridian.Ui/dashboard/src/styles/index.css")
THEME_MIRRORS = {
    "--theme-bg-canvas": "--bg", "--theme-bg-light": "--bg-light",
    "--theme-bg-hover": "--bg-hover", "--theme-bg-active": "--bg-active",
    "--theme-border": "--border", "--theme-border-strong": "--border-strong",
    "--theme-primary-text": "--text-primary", "--theme-secondary-text": "--text-secondary",
    "--theme-muted-text": "--text-muted", "--theme-accent": "--accent",
    # Not in the ten-name theme tier, but the same kind of input with the same fault: the
    # workstation flips these and this package left them on their light values.
    "--theme-border-focus": "--border-focus",
    "--theme-topbar-bg": "--topbar-bg", "--theme-topbar-text": "--topbar-text",
    "--theme-statusbar-bg": "--statusbar-bg", "--theme-statusbar-text": "--statusbar-text",
}
HSL = re.compile(r"^(-?[\d.]+) ([\d.]+)% ([\d.]+)%$")
HEX6 = re.compile(r"^#[0-9a-f]{6}$")
VAR = re.compile(r"var\(([^()]*)\)")
REM = re.compile(r"(?<![\w.])(\d*\.?\d+)rem\b")


def flat_blocks(text):
    """Every rule block at any nesting depth as (selector path, own declarations)."""
    text = strip_comments(text)
    out = []

    def parse(i, path):
        buf, own = "", ""
        while i < len(text):
            ch = text[i]
            if ch == "{":
                head = re.split(r"[;}]", buf)[-1]
                own += buf[:len(buf) - len(head)]
                buf = ""
                i = parse(i + 1, path + [" ".join(head.split())])
            elif ch == "}":
                out.append((" >> ".join(path), own + buf))
                return i + 1
            else:
                buf += ch
                i += 1
        return i

    parse(0, [])
    return out


def mode_envs(sheets):
    """Declarations per mode, in cascade order: light, auto dark, manual dark, forced light."""
    env = {"light": {}, "auto": {}, "manual": {}, "forced-light": {}}
    for text in sheets:
        for selector, body in flat_blocks(text):
            leaf = selector.split(" >> ")[-1].replace(" ", "")
            parts = leaf.split(",")
            dark_media = "prefers-color-scheme:dark" in selector.replace(" ", "")
            if dark_media and ":root" in parts:
                env["auto"].update(declared(body))
            elif not dark_media and ":root" in parts or ".meridian-root" in parts:
                env["light"].update(declared(body))
            if ':root[data-theme="dark"]' in parts:
                env["manual"].update(declared(body))
            if ':root[data-theme="light"]' in parts and ":root" not in parts:
                env["forced-light"].update(declared(body))
    light = env["light"]
    # Forced light is what renders under an OS-dark preference with data-theme="light": the
    # media block still matches, so a token it flips stays dark unless the forced-light block
    # restates it. Leaving the auto layer out reported such a token with its light value.
    return {"light": light, "auto": {**light, **env["auto"]},
            "manual": {**light, **env["manual"]},
            "forced-light": {**light, **env["auto"], **env["forced-light"]}}


def canonical(value):
    """A resolved value in one comparable spelling; colours become ('rgb', r, g, b)."""
    v = norm(value)
    found = HSL.match(v)
    if found:
        h, s_, l = (float(found.group(1)) % 360) / 360, float(found.group(2)) / 100, float(found.group(3)) / 100
        r, g, b = colorsys.hls_to_rgb(h, l, s_)
        return ("rgb", round(r * 255), round(g * 255), round(b * 255), "hsl")
    if HEX6.match(v):
        return ("rgb", int(v[1:3], 16), int(v[3:5], 16), int(v[5:7], 16), "hex")
    v = REM.sub(lambda m: "%gpx" % (float(m.group(1)) * 16), v)
    v = re.sub(r"#[0-9a-f]{3,8}", lambda m: norm(m.group(0)), v)
    return ("text", re.sub(r"\s*,\s*", ", ", v))


def substitute_vars(value, replace):
    """Replace each innermost-first var(...) call, matching parentheses by depth.

    A regex that forbids parentheses inside the call cannot see `var(--a, rgba(0,0,0,.5))`,
    which then survives as text and compares equal to itself whatever --a holds.
    """
    out, i = "", 0
    while True:
        start = value.find("var(", i)
        if start < 0:
            return out + value[i:]
        depth, j = 0, start + 3
        while j < len(value):
            if value[j] == "(":
                depth += 1
            elif value[j] == ")":
                depth -= 1
                if depth == 0:
                    break
            j += 1
        if j >= len(value):
            return out + value[i:]
        out += value[i:start] + replace(value[start + 4:j])
        i = j + 1


def resolve_token(name, env, depth=0):
    """Follow var() references (honouring fallbacks) to a canonical value, or None."""
    value = env.get(name)
    if value is None or depth > 24:
        return None

    def substitute(inner):
        head, _, fallback = inner.partition(",")
        inner = resolve_text(head.strip(), env, depth + 1)
        if inner is None:
            return fallback.strip() if fallback.strip() else "unresolved(" + head.strip() + ")"
        return inner

    previous = None
    while previous != value and "var(" in value:
        previous = value
        value = substitute_vars(value, substitute)
    return value


def resolve_text(name, env, depth):
    return resolve_token(name, env, depth)


def same_value(a, b):
    if a[0] != b[0]:
        return False
    if a[0] == "text":
        return a[1] == b[1]
    # An HSL channel triple is quantised to whole percents; allow the 1/255 that costs.
    slack = 1 if "hsl" in (a[4], b[4]) else 0
    return all(abs(a[i] - b[i]) <= slack for i in (1, 2, 3))


def show(value):
    return "#%02X%02X%02X" % value[1:4] if value[0] == "rgb" else value[1]


def theme_findings(files):
    """Pass 4: the Tier-1 theme inputs flip in dark and stay light when light is forced."""
    out = []
    dark = dark_blocks(files["colors-dark.css"])
    for theme, mirror in THEME_MIRRORS.items():
        for label in ("auto", "manual"):
            got = dark[label].get(theme)
            if got != f"var({mirror})":
                out.append(f"THEME    {theme} must be var({mirror}) in the {label} dark block "
                           f"(found {got or 'nothing'}) — the name would stay light in dark mode")
        forced = dark["forced-light"].get(mirror, "")
        if not (forced.startswith("#") or forced.startswith("rgb")):
            out.append(f"THEME    forced-light must state {mirror} as a literal so {theme} "
                       "resolves light under an OS-dark preference")
    return out


def upstream_findings(manifest, files):
    """Pass 5: shared names carry the same value in the workstation as here."""
    index = ROOT.parent / UPSTREAM_INDEX
    if not index.exists():
        return None, []
    here = mode_envs([files[name] for name in CLOSURE if name in files])
    there = mode_envs([index.read_text(encoding="utf-8")])
    excused = {}
    for entry in manifest.get("upstreamValueDivergences", []):
        for name in entry.get("names", []):
            excused[name] = entry
    shared = sorted(set(here["light"]) & set(there["light"]))
    differing, out = set(), []
    for name in shared:
        for mode in ("light", "auto", "manual", "forced-light"):
            mine, theirs = resolve_token(name, here[mode]), resolve_token(name, there[mode])
            if mine is None or theirs is None:
                continue
            if not same_value(canonical(mine), canonical(theirs)):
                differing.add(name)
                if name not in excused:
                    out.append(f"UPSTREAM {name} {mode}: workstation {show(canonical(theirs))} "
                               f"≠ package {show(canonical(mine))}")
    for name in sorted(set(excused) - differing):
        reason = "is not declared on both sides" if name not in shared else "no longer differs"
        out.append(f"UPSTREAM {name} is listed in upstreamValueDivergences but {reason} — "
                   "drop the entry")
    return len(shared), out


def main():
    names_only = "--names" in sys.argv
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    files = {}
    for name in CLOSURE:
        path = ROOT / "tokens" / name
        if not path.exists():
            print(f"MISSING FILE  tokens/{name} (listed in the closure)")
            return 1
        files[name] = path.read_text(encoding="utf-8")

    all_decls = {}
    for name, text in files.items():
        for sel, body in blocks(strip_comments(text)):
            for token, value in declared(body).items():
                all_decls.setdefault(token, []).append((name, value))

    contract = list(manifest.get("aliasContract", []))
    tiers = {"aliasContract": list(manifest.get("aliasContract", []))}
    for tier, names in (manifest.get("workingNameContract") or {}).items():
        if tier.startswith("$"):
            continue
        tiers[tier] = list(names)
        contract += names

    findings = []
    for token in contract:
        if token not in all_decls:
            findings.append(f"NAME     {token} is in the contract but nothing in tokens/*.css defines it")

    # 2. contract names must be var() references, not duplicated literals.
    exempt = {"--ws-shadow", "--shadow-workstation", "--shadow-panel", "--shadow-soft",
              "--shadow-menu", "--radius-chip", "--radius-button", "--radius-card",
              "--green", "--red", "--orange", "--purple", "--accent-ghost", "--bg",
              "--bg-light", "--bg-medium", "--bg-hover", "--bg-active", "--text-primary",
              "--text-secondary", "--text-muted", "--border-hover", "--border-strong",
              "--border-divider", "--chart-grid-major"}
    for token in contract:
        # The theme tier is Tier-1 input: a light literal by design, held by pass 4 instead.
        if token in exempt or token in THEME_MIRRORS or token not in all_decls:
            continue
        literal = [f"{f}: {v}" for f, v in all_decls[token]
                   if v.startswith("#") or v.startswith("rgb")]
        if literal:
            findings.append(f"ALIAS    {token} hardcodes a value ({literal[0]}) — must be a var() reference")

    value_findings = []
    if not names_only:
        light = light_graph(files)
        dark = dark_blocks(files["colors-dark.css"])
        for token, rec in manifest.get("tokens", {}).items():
            want_l, want_d = norm(rec["light"]), norm(rec["dark"])
            got_l = light.get(token)
            if got_l is None:
                value_findings.append(f"VALUE    {token} recorded light {want_l} but the default theme graph does not define it")
            elif got_l != want_l:
                value_findings.append(f"VALUE    {token} light: css {got_l} ≠ manifest {want_l}")
            for label in ("auto", "manual"):
                got = dark[label].get(token)
                if got is None:
                    if want_d != want_l:
                        value_findings.append(f"VALUE    {token} flips to {want_d} but the {label} dark block does not set it")
                elif got != want_d:
                    value_findings.append(f"VALUE    {token} {label} dark: css {got} ≠ manifest {want_d}")
            got_f = dark["forced-light"].get(token)
            if got_f is not None and got_f != want_l:
                value_findings.append(f"VALUE    {token} forced-light: css {got_f} ≠ manifest light {want_l}")
        value_findings += theme_findings(files)
    shared_count, upstream = (None, []) if names_only else upstream_findings(manifest, files)
    value_findings += upstream

    print("── token parity ──")
    print(f"contract names : {len(contract)} across {len(tiers)} tiers "
          f"({', '.join(f'{k} {len(v)}' for k, v in tiers.items())})")
    print(f"flip set       : {len(manifest.get('tokens', {}))} tokens"
          + (" (value pass skipped)" if names_only else ""))
    print(f"closure        : {len(files)} files, {len(all_decls)} declared tokens")
    if not names_only:
        print("upstream       : " + (f"{shared_count} names shared with the workstation, "
              f"{len(manifest.get('upstreamValueDivergences', []))} recorded divergence group(s)"
              if shared_count is not None else "workstation not present (package used standalone)"))
    for line in findings + value_findings:
        print(line)
    total = len(findings) + len(value_findings)
    print(f"\n{'FAIL' if total else 'PASS'}  {total} finding(s)")
    for note in manifest.get("upstreamValueDivergences", []):
        print(f"  note: {', '.join(note['names'])} — recorded divergence: {note['why']}")
    return 1 if total else 0


if __name__ == "__main__":
    sys.exit(main())
