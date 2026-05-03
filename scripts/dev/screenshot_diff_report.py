#!/usr/bin/env python3
"""Generate screenshot diff classification and report for desktop screenshot updates."""

from __future__ import annotations

import argparse
import json
import os
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from PIL import Image, ImageChops, ImageOps, ImageStat


@dataclass
class Thresholds:
    review_needed: float
    blocking: float
    tolerance: int
    masks: list[dict[str, int]]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--current-root", type=Path, required=True)
    parser.add_argument("--baseline-root", type=Path, required=True)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--report-dir", type=Path, required=True)
    parser.add_argument("--changed-files", type=Path, required=True)
    parser.add_argument("--approval", choices=["approved", "pending"], default="pending")
    parser.add_argument("--approval-actor", default="")
    parser.add_argument("--approval-reason", default="")
    parser.add_argument("--output-json", type=Path, required=True)
    return parser.parse_args()


def _read_config(path: Path) -> dict[str, Any]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    if int(payload.get("version", 0)) != 1:
        raise ValueError(f"Unsupported screenshot diff config version: {payload.get('version')}")
    return payload


def _entry_thresholds(config: dict[str, Any], filename: str) -> Thresholds:
    default = config["default"]
    entry = config.get("images", {}).get(filename, {})
    masks = [*default.get("masks", []), *entry.get("masks", [])]
    return Thresholds(
        review_needed=float(entry.get("reviewNeededThreshold", default["reviewNeededThreshold"])),
        blocking=float(entry.get("blockingThreshold", default["blockingThreshold"])),
        tolerance=int(entry.get("pixelChannelTolerance", default["pixelChannelTolerance"])),
        masks=masks,
    )


def _apply_masks(diff: Image.Image, masks: list[dict[str, int]]) -> tuple[Image.Image, int]:
    masked = diff.copy()
    masked_px_count = 0
    width, height = diff.size
    for mask in masks:
        x = max(0, int(mask.get("x", 0)))
        y = max(0, int(mask.get("y", 0)))
        w = max(0, int(mask.get("width", 0)))
        h = max(0, int(mask.get("height", 0)))
        if w == 0 or h == 0:
            continue
        x2 = min(width, x + w)
        y2 = min(height, y + h)
        if x2 <= x or y2 <= y:
            continue
        masked_px_count += (x2 - x) * (y2 - y)
        mask_region = Image.new("RGB", (x2 - x, y2 - y), (0, 0, 0))
        masked.paste(mask_region, (x, y))
    return masked, masked_px_count


def _classify(diff_ratio: float, thresholds: Thresholds) -> str:
    if diff_ratio >= thresholds.blocking:
        return "blocking-regression"
    if diff_ratio >= thresholds.review_needed:
        return "review-needed"
    return "non-blocking-noise"


def _thumbnail(src: Path, target: Path, max_size: tuple[int, int] = (320, 180)) -> None:
    with Image.open(src) as img:
        frame = ImageOps.exif_transpose(img.convert("RGB"))
        frame.thumbnail(max_size)
        target.parent.mkdir(parents=True, exist_ok=True)
        frame.save(target, format="PNG")


def _make_diff_preview(current: Path, baseline: Path, output: Path, tolerance: int) -> None:
    with Image.open(current) as current_img, Image.open(baseline) as baseline_img:
        cur = ImageOps.exif_transpose(current_img.convert("RGB"))
        base = ImageOps.exif_transpose(baseline_img.convert("RGB"))
        if cur.size != base.size:
            base = base.resize(cur.size)
        diff = ImageChops.difference(cur, base)
        lum = ImageOps.grayscale(cur).convert("RGB")
        diff_px = diff.load()
        lum_px = lum.load()
        out = Image.new("RGB", cur.size)
        out_px = out.load()
        for y in range(cur.height):
            for x in range(cur.width):
                d = diff_px[x, y]
                if max(d) > tolerance:
                    out_px[x, y] = (255, 32, 32)
                else:
                    out_px[x, y] = lum_px[x, y]
        out.thumbnail((320, 180))
        output.parent.mkdir(parents=True, exist_ok=True)
        out.save(output, format="PNG")


def main() -> int:
    args = parse_args()
    config = _read_config(args.config)
    report_dir = args.report_dir
    report_dir.mkdir(parents=True, exist_ok=True)
    thumbs = report_dir / "thumbnails"
    thumbs.mkdir(parents=True, exist_ok=True)

    changed = [
        line.strip().replace("\\", "/")
        for line in args.changed_files.read_text(encoding="utf-8").splitlines()
        if line.strip().endswith(".png")
    ]

    results: list[dict[str, Any]] = []
    for rel in sorted(set(changed)):
        current_path = args.current_root / rel
        baseline_path = args.baseline_root / rel
        filename = Path(rel).name
        thresholds = _entry_thresholds(config, filename)

        if current_path.exists() and not baseline_path.exists():
            category = "non-blocking-noise"
            reason = "New screenshot file"
            diff_ratio = 0.0
            masked_pixels = 0
            with Image.open(current_path) as cur_img:
                cur = ImageOps.exif_transpose(cur_img.convert("RGB"))
                size = [cur.width, cur.height]
                valid_pixels = cur.width * cur.height
        elif not current_path.exists() or not baseline_path.exists():
            category = "blocking-regression"
            reason = "Current or baseline image missing"
            diff_ratio = 1.0
            size = None
            masked_pixels = 0
            valid_pixels = 0
        else:
            with Image.open(current_path) as cur_img, Image.open(baseline_path) as base_img:
                cur = ImageOps.exif_transpose(cur_img.convert("RGB"))
                base = ImageOps.exif_transpose(base_img.convert("RGB"))
                if cur.size != base.size:
                    category = "blocking-regression"
                    reason = f"Image dimensions differ (current={cur.size}, baseline={base.size})"
                    diff_ratio = 1.0
                    size = [cur.width, cur.height]
                    masked_pixels = 0
                    valid_pixels = cur.width * cur.height
                else:
                    diff = ImageChops.difference(cur, base)
                    diff, masked_pixels = _apply_masks(diff, thresholds.masks)
                    channels = diff.split()
                    significant = channels[0].point(lambda p: 255 if p > thresholds.tolerance else 0)
                    for ch in channels[1:]:
                        significant = ImageChops.lighter(significant, ch.point(lambda p: 255 if p > thresholds.tolerance else 0))
                    stat = ImageStat.Stat(significant)
                    changed_pixels = int((stat.sum[0] / 255.0))
                    total_pixels = cur.width * cur.height
                    valid_pixels = max(1, total_pixels - masked_pixels)
                    diff_ratio = changed_pixels / valid_pixels
                    category = _classify(diff_ratio, thresholds)
                    reason = "Threshold-based classification"
                    size = [cur.width, cur.height]

        baseline_thumb = thumbs / f"{filename}.baseline.png"
        current_thumb = thumbs / f"{filename}.current.png"
        diff_thumb = thumbs / f"{filename}.diff.png"

        if baseline_path.exists():
            _thumbnail(baseline_path, baseline_thumb)
        if current_path.exists():
            _thumbnail(current_path, current_thumb)
        if baseline_path.exists() and current_path.exists():
            _make_diff_preview(current_path, baseline_path, diff_thumb, thresholds.tolerance)

        results.append(
            {
                "path": rel,
                "filename": filename,
                "category": category,
                "diffRatio": round(diff_ratio, 6),
                "reason": reason,
                "reviewNeededThreshold": thresholds.review_needed,
                "blockingThreshold": thresholds.blocking,
                "pixelChannelTolerance": thresholds.tolerance,
                "maskedPixels": masked_pixels,
                "validPixels": valid_pixels,
                "size": size,
                "thumbnails": {
                    "baseline": os.path.relpath(baseline_thumb, report_dir).replace("\\", "/"),
                    "current": os.path.relpath(current_thumb, report_dir).replace("\\", "/"),
                    "diff": os.path.relpath(diff_thumb, report_dir).replace("\\", "/"),
                },
            }
        )

    counts = {
        "blocking-regression": sum(1 for r in results if r["category"] == "blocking-regression"),
        "review-needed": sum(1 for r in results if r["category"] == "review-needed"),
        "non-blocking-noise": sum(1 for r in results if r["category"] == "non-blocking-noise"),
    }

    gate_blocking = counts["blocking-regression"] > 0
    review_requires_approval = counts["review-needed"] > 0 and args.approval != "approved"

    payload = {
        "counts": counts,
        "gate": {
            "failCi": gate_blocking,
            "reviewApprovalRequired": review_requires_approval,
            "approvalState": args.approval,
            "approvalActor": args.approval_actor,
            "approvalReason": args.approval_reason,
        },
        "results": results,
    }
    args.output_json.parent.mkdir(parents=True, exist_ok=True)
    args.output_json.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    lines = [
        "# Screenshot Diff Report",
        "",
        f"Blocking regression: **{counts['blocking-regression']}**",
        f"Review-needed: **{counts['review-needed']}**",
        f"Non-blocking noise: **{counts['non-blocking-noise']}**",
        "",
        "| Screenshot | Category | Diff ratio | Baseline | Current | Delta |",
        "| --- | --- | ---: | --- | --- | --- |",
    ]
    for result in results:
        lines.append(
            "| `{path}` | **{category}** | `{ratio:.4%}` | ![]({base}) | ![]({cur}) | ![]({diff}) |".format(
                path=result["path"],
                category=result["category"],
                ratio=result["diffRatio"],
                base=result["thumbnails"]["baseline"],
                cur=result["thumbnails"]["current"],
                diff=result["thumbnails"]["diff"],
            )
        )

    lines.extend(
        [
            "",
            "## Approval",
            f"State: **{args.approval}**",
            f"Actor: `{args.approval_actor or 'n/a'}`",
            f"Reason: `{args.approval_reason or 'n/a'}`",
        ]
    )
    (report_dir / "report.md").write_text("\n".join(lines) + "\n", encoding="utf-8")

    return 2 if gate_blocking else 0


if __name__ == "__main__":
    raise SystemExit(main())
