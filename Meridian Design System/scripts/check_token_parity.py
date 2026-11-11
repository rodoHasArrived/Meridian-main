#!/usr/bin/env python3
"""Meridian — token parity gate (stdlib only, offline).

tokens/parity-manifest.json has claimed since 3.1 that this script exists. It didn't.
This is it, scoped to what is actually shared with the upstream app:

  1 NAMES   Every name in aliasContract + workingNameContract resolves in the CSS
            closure (tokens/*.css). The upstream console authors CSS against these
            names; a missing one means pasted app CSS silently resolves to nothing.
  2 ALIASES Every contract name is a var() reference, not a duplicate hex — an alias
            that hardcodes a value can drift from the token it mirrors.
  3 VALUES  Every entry in `tokens` resolves to its recorded value in the default,
            automatic-dark, manual-dark and OS-dark/forced-light theme graphs.

  4 THEME    The ten --theme-* contract names (plus focus and chrome inputs) are Tier-1
            inputs authored as light literals. Both dark blocks must re-point each one at its flipped
            component token. Forced light must resolve both input and component to
            their light identities, without cycles or reliance on an OS-light baseline.
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
    for sel, body in flat_blocks(text):
        s = sel.replace(" ", "")
        leaf = s.split(">>")[-1]
        if "prefers-color-scheme:dark" in s and leaf in (":root", ':root:not([data-theme="light"])'):
            found["auto"].update(declared(body))
        elif leaf == ':root[data-theme="dark"]':
            found["manual"].update(declared(body))
        elif leaf == ':root[data-theme="light"]':
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
    """Resolve root declaration scopes, including explicit light under an OS-dark preference.

    Scope specificity beats source order. In particular, :root:not([data-theme="light"])
    does not apply in forced light, while an unguarded OS-dark :root still does.
    Brand-specific selectors are exercised separately by the browser brand matrix.
    """
    env = {"light": {}, "auto": {}, "manual": {}, "forced-light": {}}
    ranks = {mode: {} for mode in env}
    order = 0
    for text in sheets:
        for selector, body in flat_blocks(text):
            order += 1
            leaf = selector.split(" >> ")[-1].replace(" ", "")
            parts = leaf.split(",")
            dark_media = "prefers-color-scheme:dark" in selector.replace(" ", "")
            if "@media" in selector and not dark_media:
                continue  # print, forced-colors, etc. are separate environments
            for mode in env:
                if dark_media and mode not in ("auto", "forced-light"):
                    continue
                specificity = []
                for part in parts:
                    if part in (":root", ".meridian-root"):
                        specificity.append(1)
                    elif part == ':root:not([data-theme="light"])' and mode != "forced-light":
                        specificity.append(2)
                    elif part == ':root[data-theme="dark"]' and mode == "manual":
                        specificity.append(2)
                    elif part == ':root[data-theme="light"]' and mode == "forced-light":
                        specificity.append(2)
                if not specificity:
                    continue
                rank = (max(specificity), order)
                for name, value in declared(body).items():
                    if rank >= ranks[mode].get(name, (0, 0)):
                        env[mode][name] = value
                        ranks[mode][name] = rank
    return env


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


def cyclic_tokens(env):
    """CSS invalidates every custom property in a cycle, including var() fallback edges."""
    refs = {name: re.findall(r"var\(\s*(--[\w-]+)", value) for name, value in env.items()}
    visiting, done, cyclic = [], set(), set()
    def visit(name):
        if name in visiting:
            cyclic.update(visiting[visiting.index(name):])
            return
        if name in done or name not in refs:
            return
        visiting.append(name)
        for other in refs[name]:
            visit(other)
        visiting.pop()
        done.add(name)
    for name in refs:
        visit(name)
    return cyclic


def resolve_token(name, env, depth=0, cycles=None):
    """Follow var() references (honouring fallbacks) to a canonical value, or None."""
    cycles = cyclic_tokens(env) if cycles is None else cycles
    value = env.get(name)
    if value is None or name in cycles or depth > 24:
        return None

    def substitute(inner):
        head, _, fallback = inner.partition(",")
        inner = resolve_token(head.strip(), env, depth + 1, cycles)
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
    envs = mode_envs([files[name] for name in CLOSURE if name in files])
    for theme, mirror in THEME_MIRRORS.items():
        for label in ("auto", "manual"):
            got = dark[label].get(theme)
            if got != f"var({mirror})":
                out.append(f"THEME    {theme} must be var({mirror}) in the {label} dark block "
                           f"(found {got or 'nothing'}) — the name would stay light in dark mode")
        expected = resolve_token(mirror, envs["light"])
        for name in (mirror, theme):
            forced = resolve_token(name, envs["forced-light"])
            if expected is None or forced is None or not same_value(canonical(forced), canonical(expected)):
                out.append(f"THEME    forced-light {name} must resolve to the light {mirror} "
                           "under an OS-dark preference; check aliases and cycles")
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


def token_value_findings(manifest, files):
    """Pass 3 checks computed aliases, not their syntactic spelling."""
    envs = mode_envs([files[name] for name in CLOSURE if name in files])
    out = []
    for token, rec in manifest.get("tokens", {}).items():
        for mode, label in (("light", "light"), ("auto", "auto dark"), ("manual", "manual dark"), ("forced-light", "forced-light")):
            expected = canonical(rec["dark" if mode in ("auto", "manual") else "light"])
            actual = resolve_token(token, envs[mode])
            if actual is None:
                out.append(f"VALUE    {token} {label}: missing or cyclic in the effective theme graph")
            elif not same_value(canonical(actual), expected):
                out.append(f"VALUE    {token} {label}: css {actual} ≠ manifest {show(expected)}")
    return out


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
        value_findings += token_value_findings(manifest, files)
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
