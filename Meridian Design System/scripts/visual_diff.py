#!/usr/bin/env python3
"""Screenshot-diff harness for Meridian @dsCard demos.

Renders every ``*.card.html`` at its declared ``@dsCard`` viewport with headless Chromium
(Playwright) and either (a) writes baseline PNGs, or (b) compares fresh captures against the
baselines and writes a per-card diff image for anything that moved.

Why the hosting dance: cards reference the design system with ``../../`` paths
(``../../styles.css``, ``../../_ds_bundle.js``) — the same convention the DS card viewer uses,
which serves every card from a path exactly two directories below the package root. We
replicate that faithfully by hosting each card, in turn, at ``<root>/.dsviewer/host/index.html``
(two levels deep) behind a static server rooted at the package root, so every ``../../`` in the
card resolves to the real repo file regardless of where the card physically lives. The
``.dsviewer/`` scratch dir is removed on exit.

Usage::

    python scripts/visual_diff.py --update            # (re)write tests/baselines/*.png
    python scripts/visual_diff.py                      # compare; exit 1 on any drift
    python scripts/visual_diff.py --threshold 0.004    # looser per-card change budget
    python scripts/visual_diff.py --only trading       # substring filter on card path

Requires: ``pip install playwright pillow`` then ``playwright install chromium``.
"""

from __future__ import annotations

import argparse
import functools
import http.server
import re
import shutil
import socketserver
import sys
import threading
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BASELINE_DIR = ROOT / "tests" / "baselines"
DIFF_DIR = ROOT / "tests" / "diff"
HOST_DIR = ROOT / ".dsviewer" / "host"          # two levels deep → ../../ resolves to ROOT
SKIP_PARTS = {"node_modules", ".git", "uploads", ".dsviewer"}
VIEWPORT_RE = re.compile(r'viewport="(\d+)x(\d+)"', re.IGNORECASE)
DEFAULT_VIEWPORT = (1200, 800)
SETTLE_MS = 700                                 # let React/bundle cards paint


def discover_cards() -> list[Path]:
    cards = [
        p for p in ROOT.rglob("*.card.html")
        if not any(part in SKIP_PARTS for part in p.parts)
    ]
    return sorted(cards)


def parse_viewport(card: Path) -> tuple[int, int]:
    head = card.read_text(encoding="utf-8")[:400]
    m = VIEWPORT_RE.search(head)
    return (int(m.group(1)), int(m.group(2))) if m else DEFAULT_VIEWPORT


def slug_for(card: Path) -> str:
    return card.relative_to(ROOT).as_posix().replace("/", "__")[: -len(".card.html")]


def start_server() -> tuple[socketserver.TCPServer, int]:
    handler = functools.partial(http.server.SimpleHTTPRequestHandler, directory=str(ROOT))
    handler = _quiet(handler)
    httpd = socketserver.TCPServer(("127.0.0.1", 0), handler)
    port = httpd.server_address[1]
    threading.Thread(target=httpd.serve_forever, daemon=True).start()
    return httpd, port


def _quiet(handler_cls):
    class Quiet(handler_cls):  # type: ignore[misc, valid-type]
        def log_message(self, *args):  # noqa: D401 - silence per-request logging
            pass
    return Quiet


def capture_all(update: bool, threshold: float, only: str | None) -> int:
    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        print("visual_diff: Playwright is not installed — `pip install playwright && playwright install chromium`", file=sys.stderr)
        return 2
    try:
        from PIL import Image, ImageChops
    except ImportError:
        print("visual_diff: Pillow is not installed — `pip install pillow`", file=sys.stderr)
        return 2

    cards = [c for c in discover_cards() if not only or only in c.relative_to(ROOT).as_posix()]
    if not cards:
        print("visual_diff: no cards matched")
        return 0

    (BASELINE_DIR if update else DIFF_DIR).mkdir(parents=True, exist_ok=True)
    HOST_DIR.mkdir(parents=True, exist_ok=True)
    host_file = HOST_DIR / "index.html"
    httpd, port = start_server()
    drifted: list[tuple[str, float]] = []
    missing_baseline: list[str] = []

    try:
        with sync_playwright() as pw:
            browser = pw.chromium.launch()
            for card in cards:
                slug = slug_for(card)
                w, h = parse_viewport(card)
                host_file.write_text(card.read_text(encoding="utf-8"), encoding="utf-8")
                page = browser.new_page(viewport={"width": w, "height": h}, device_scale_factor=1)
                try:
                    page.goto(f"http://127.0.0.1:{port}/.dsviewer/host/index.html", wait_until="networkidle", timeout=15000)
                except Exception as exc:  # noqa: BLE001 - report and continue
                    print(f"  ! {slug}: load error {exc}")
                    page.close()
                    continue
                page.wait_for_timeout(SETTLE_MS)
                shot = (BASELINE_DIR if update else DIFF_DIR) / f"{slug}.png"
                current = shot if update else DIFF_DIR / f"{slug}.current.png"
                page.screenshot(path=str(current), clip={"x": 0, "y": 0, "width": w, "height": h})
                page.close()

                if update:
                    print(f"  = baseline {slug} ({w}x{h})")
                    continue

                baseline = BASELINE_DIR / f"{slug}.png"
                if not baseline.exists():
                    missing_baseline.append(slug)
                    print(f"  ? {slug}: no baseline (run --update)")
                    continue
                a = Image.open(baseline).convert("RGB")
                b = Image.open(current).convert("RGB")
                if a.size != b.size:
                    drifted.append((slug, 1.0))
                    print(f"  ✗ {slug}: size {a.size} → {b.size}")
                    continue
                diff = ImageChops.difference(a, b)
                bbox = diff.getbbox()
                if bbox is None:
                    current.unlink(missing_ok=True)
                    continue
                # fraction of pixels that changed at all
                changed = sum(1 for px in diff.getdata() if px != (0, 0, 0))
                ratio = changed / (a.size[0] * a.size[1])
                if ratio > threshold:
                    drifted.append((slug, ratio))
                    diff.save(DIFF_DIR / f"{slug}.diff.png")
                    print(f"  ✗ {slug}: {ratio:.4%} changed (> {threshold:.4%})")
                else:
                    current.unlink(missing_ok=True)
            browser.close()
    finally:
        httpd.shutdown()
        shutil.rmtree(ROOT / ".dsviewer", ignore_errors=True)

    if update:
        print(f"visual_diff: wrote {len(cards)} baselines to {BASELINE_DIR.relative_to(ROOT)}")
        return 0
    if drifted:
        print(f"\nvisual_diff: FAIL — {len(drifted)} card(s) drifted; diffs in {DIFF_DIR.relative_to(ROOT)}")
        return 1
    if missing_baseline:
        print(f"\nvisual_diff: {len(missing_baseline)} card(s) have no baseline — run with --update")
        return 1
    print(f"visual_diff: PASS — {len(cards)} cards match baseline")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Screenshot-diff the @dsCard demos.")
    parser.add_argument("--update", action="store_true", help="write baselines instead of comparing")
    parser.add_argument("--threshold", type=float, default=0.002, help="per-card changed-pixel fraction budget (default 0.002)")
    parser.add_argument("--only", help="substring filter on card path")
    args = parser.parse_args(argv)
    return capture_all(update=args.update, threshold=args.threshold, only=args.only)


if __name__ == "__main__":
    sys.exit(main())
