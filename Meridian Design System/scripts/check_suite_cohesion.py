#!/usr/bin/env python3
"""Meridian — suite cohesion gate (stdlib only, offline).

The structural half of the template contract (PascalCase entry, <main>, <h1>, $preview, the four
tweaks, DataStateSwitch) is checked by the design-system compiler and
scripts/check_design_system_governance.py. Those gates ask "is this screen well-formed?".

This one asks the question docs/SUITE_COHESION_4_4.md raised: **do the 27 screens add up to one
workstation?** A suite can pass every structural gate and still be 27 dioramas — nine rails whose
onSelect is a no-op, a search field inert on every screen, six frozen clocks, three business dates.

Rules (4.4 · wave 1–2):
  env        every `environment` is LIVE / PAPER / FIXTURE, or a bound expression — never a
             fourth mode, and never a literal that contradicts the screen's own tweak
  nav        no rail is rendered with a no-op onSelect / onNav — a dead rail is worse than none
  map        rails and routes come from templates/_scaffold/app-map.js, not a hand-rolled table
  clock      no screen hardcodes a wall clock; the masthead reads the shared world clock
  search     the masthead search resolves — ds-base.js is present and loads the scaffold spine
  world      no screen retypes the fund's name or the business date as a bare literal
  label      a screen's masthead name matches its app-map label — one name per screen, so the
             rail that links to it and the masthead you land on agree
  prefs      applyChrome routes theme / density / environment through MeridianPrefs, so an
             operator's display choice survives a navigation instead of resetting per screen
  operator   the masthead shows who is signed in — no screen may suppress it except pre-auth

ADVISORY by default: this reports and exits 0, because the suite is mid-migration and a red build
on a known backlog teaches people to ignore the build. Run with --strict once a wave is complete
to make it a gate.

Usage:
  python3 scripts/check_suite_cohesion.py             # report, always exit 0
  python3 scripts/check_suite_cohesion.py --strict    # exit 1 on any finding
  python3 scripts/check_suite_cohesion.py --rule nav  # one rule only
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TEMPLATES = ROOT / "templates"
SCAFFOLD = TEMPLATES / "_scaffold"

MODES = {"LIVE", "PAPER", "FIXTURE"}

# Screens that carry no rail on purpose, with the reason. A rail-less screen is a decision the
# suite has to be able to state; anything not listed here and missing a rail is a finding.
RAIL_EXEMPT = {
    "session-start": "pre-auth — you are not in the app yet",
    "ops-wall": "wallboard on a shared monitor — nobody clicks it",
    "interaction-depth": "pattern specimen, not a screen",
    "report-pack": "print artifact",
    "asset-sheet": "print artifact",
}

# The masthead operator chip is suppressible only where there is no session yet.
OPERATOR_EXEMPT = {"session-start": "pre-auth — nobody is signed in yet"}

# A frozen wall clock beside a live pulse dot is the fastest way to read as a mock.
CLOCK_RX = re.compile(r'clock\s*[:=]\s*"(\d{2}:\d{2}:\d{2}[^"]*)"')
# The masthead name, however the screen mounts the topbar, and the rail id it claims to be.
MODULE_ATTR_RX = re.compile(r'module-label="([^"{}]+)"')
MODULE_PROP_RX = re.compile(r'moduleLabel:\s*"([^"]+)"')
ACTIVE_ATTR_RX = re.compile(r'active-id="([a-z0-9-]+)"')
ACTIVE_PROP_RX = re.compile(r'activeId:\s*"([a-z0-9-]+)"')
# app-map.js entries: { id: "dashboard", label: "Portfolio Overview", … }
MAP_ENTRY_RX = re.compile(r'\{\s*id:\s*"([a-z0-9-]+)",\s*label:\s*"([^"]+)"')
# Chrome attributes must go through MeridianPrefs (track D) — a raw setAttribute re-applies the
# screen's own default on every render and overwrites the operator's choice.
RAW_CHROME_RX = re.compile(r'setAttribute\(\s*"data-(?:theme|environment|theme-density)"\s*,\s*(?!\(?window\.MeridianPrefs)')
SUPPRESS_OPERATOR_RX = re.compile(r'show-?[Oo]perator\s*[:=]\s*"?\{?\{?\s*false|operator\s*[:=]\s*"?\{?\{?\s*null')
ENV_ATTR_RX = re.compile(r'environment="([^"{}]+)"')
ENV_PROP_RX = re.compile(r'environment:\s*"([A-Za-z]+)"')
DEAD_NAV_RX = re.compile(r'on-?[Ss]elect\s*[:=]\s*"?\{?\{?\s*(noop)\s*\}?\}?"?|on(?:Select|Nav):\s*\(\s*\)\s*=>\s*\{\s*\}')
HAND_ROUTES_RX = re.compile(r'^\s*(?:this\.)?ROUTES\s*=\s*\{', re.M)
NAVRAIL_RX = re.compile(r'NavRail')
FUND_LITERAL_RX = re.compile(r'Meridian (?:Fund I LP|Capital · Fund I)')

findings = []


def find(rule, slug, entry, msg):
    findings.append((rule, slug, entry, msg))


def app_map_labels():
    """id → label from the one address book. Empty dict if it is missing (the `map` rule reports)."""
    p = SCAFFOLD / "app-map.js"
    if not p.exists():
        return {}
    src = p.read_text(encoding="utf-8", errors="replace")
    return {m.group(1): m.group(2) for m in MAP_ENTRY_RX.finditer(src)}


def screens():
    for folder in sorted(p for p in TEMPLATES.iterdir() if p.is_dir() and not p.name.startswith("_")):
        for entry in sorted(folder.glob("*.dc.html")):
            yield folder.name, entry


def main():
    only = None
    if "--rule" in sys.argv:
        only = sys.argv[sys.argv.index("--rule") + 1]

    if not (SCAFFOLD / "app-map.js").exists():
        find("map", "_scaffold", "app-map.js", "missing — the suite has no address book")
    if not (SCAFFOLD / "world.js").exists():
        find("world", "_scaffold", "world.js", "missing — the suite has no shared clock or operator")
    if not (SCAFFOLD / "prefs.js").exists():
        find("prefs", "_scaffold", "prefs.js", "missing — display choices cannot survive a navigation")

    labels = app_map_labels()

    slugs_seen = set()
    for slug, entry in screens():
        slugs_seen.add(slug)
        src = entry.read_text(encoding="utf-8", errors="replace")
        name = entry.name

        # env — a fourth mode is a contract violation, not a new mode
        for m in ENV_ATTR_RX.finditer(src):
            v = m.group(1).strip().upper()
            if v not in MODES:
                find("env", slug, name, f'environment="{m.group(1)}" is not LIVE / PAPER / FIXTURE')
        for m in ENV_PROP_RX.finditer(src):
            v = m.group(1).strip().upper()
            if v not in MODES:
                find("env", slug, name, f'environment: "{m.group(1)}" is not LIVE / PAPER / FIXTURE')
        # a literal environment on the topbar while the screen also declares the tweak
        if 'environment="PAPER"' in src or 'environment="LIVE"' in src or 'environment="FIXTURE"' in src:
            if "data-environment" in src or "props.environment" in src:
                find("env", slug, name, "topbar hardcodes an environment while the screen's own tweak moves <html data-environment> — set the tweak to LIVE and the badge still says PAPER")

        # nav — a rail that does nothing, with no disabled state to admit it
        if NAVRAIL_RX.search(src) and DEAD_NAV_RX.search(src):
            find("nav", slug, name, "renders a rail whose onSelect is a no-op — clicking a destination does nothing")

        # map — hand-rolled route tables drift from the one address book
        if HAND_ROUTES_RX.search(src):
            find("map", slug, name, "declares its own ROUTES table — use MeridianAppMap.go / .open so one address book stays true")

        # clock — one instant for the whole suite
        for m in CLOCK_RX.finditer(src):
            find("clock", slug, name, f'hardcodes the wall clock "{m.group(1)}" — read MeridianWorld.clock()')

        # world — retyped entity names drift between screens that hand off to each other
        for m in FUND_LITERAL_RX.finditer(src):
            find("world", slug, name, f'retypes the fund name "{m.group(0)}" — use MeridianWorld.entityLabel()')

        # label — the masthead name and the rail entry that links to it must be the same words.
        # Retyped labels are how "Accounting", "PlotTool" and "Entities" appeared on screens no
        # rail could name. Compared, not interpolated: a {{ hole }} would delay the paint.
        ids = ACTIVE_ATTR_RX.findall(src) + ACTIVE_PROP_RX.findall(src)
        mods = MODULE_ATTR_RX.findall(src) + MODULE_PROP_RX.findall(src)
        if labels and ids and mods:
            claimed = [i for i in ids if i in labels]
            if claimed:
                want = {labels[i] for i in claimed}
                for got in set(mods):
                    if got not in want:
                        find("label", slug, name,
                             f'masthead says "{got}" but the app map calls this screen '
                             + " / ".join(sorted(want)))

        # prefs — tweak seeds, operator wins. A raw setAttribute resets the operator's choice.
        for m in RAW_CHROME_RX.finditer(src):
            line = src[:m.start()].count("\n") + 1
            find("prefs", slug, name,
                 f"line {line}: sets a chrome attribute without MeridianPrefs — the operator's "
                 "theme/density choice is overwritten on every render")

        # operator — who is signed in, everywhere there is a session
        if "WorkstationTopbar" in src and slug not in OPERATOR_EXEMPT and SUPPRESS_OPERATOR_RX.search(src):
            find("operator", slug, name, "suppresses the masthead operator chip outside the pre-auth gate")

        # search — the field is only real if the loader is in place
        if not (entry.parent / "ds-base.js").exists():
            find("search", slug, name, "no ds-base.js in the folder — no theme, no bundle, no live search field")

    # rail coverage — a screen with no rail must say why
    for slug in sorted(slugs_seen):
        if slug in RAIL_EXEMPT:
            continue
        folder = TEMPLATES / slug
        src = "".join(p.read_text(encoding="utf-8", errors="replace") for p in folder.glob("*.dc.html"))
        if not NAVRAIL_RX.search(src):
            find("nav", slug, "—", "renders no rail and is not a declared exemption — the frame changes shape as the operator moves")

    rows = [f for f in findings if not only or f[0] == only]
    by_rule = {}
    for rule, slug, entry, msg in rows:
        by_rule.setdefault(rule, []).append((slug, entry, msg))

    order = ["env", "nav", "map", "label", "clock", "prefs", "operator", "search", "world"]
    for rule in order:
        items = by_rule.get(rule)
        if not items:
            continue
        print(f"\n{rule.upper()}  ({len(items)})")
        for slug, entry, msg in items:
            print(f"  {slug}/{entry}: {msg}")

    total = len(rows)
    n_screens = len(slugs_seen)
    clean = "OK" if not total else ("FAIL" if "--strict" in sys.argv else "ADVISORY")
    print(f"\n{clean}  suite cohesion — {n_screens} templates, {total} finding{'' if total == 1 else 's'}"
          + ("" if total else " — the suite agrees with itself"))
    if total and "--strict" not in sys.argv:
        print("      advisory run; add --strict to make these a gate")
    return 1 if (total and "--strict" in sys.argv) else 0


if __name__ == "__main__":
    sys.exit(main())
