#!/usr/bin/env python3
"""Validate screenshot capture outputs for coverage, freshness, route identity, and image quality."""

from __future__ import annotations

import argparse
import json
import math
import sys
import zlib
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from urllib.parse import urlparse

try:
    from PIL import Image
except ImportError:  # pragma: no cover - exercised only on minimal Python runners
    Image = None


REPO_ROOT = Path(__file__).resolve().parents[2]
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"


@dataclass(frozen=True)
class ExpectedCapture:
    name: str
    filename: str
    route: str | None = None
    page_tag: str | None = None


@dataclass(frozen=True)
class PngMetrics:
    width: int
    height: int
    sampled_pixels: int
    unique_colors: int
    entropy: float


def repo_path(value: str | Path) -> Path:
    path = Path(value)
    return path if path.is_absolute() else REPO_ROOT / path


def load_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def parse_timestamp(value: Any) -> datetime | None:
    if not isinstance(value, str) or not value.strip():
        return None

    normalized = value.strip().replace("Z", "+00:00")
    try:
        parsed = datetime.fromisoformat(normalized)
    except ValueError:
        return None

    if parsed.tzinfo is None:
        return parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


def load_expected_web(config_path: Path) -> list[ExpectedCapture]:
    config = load_json(config_path)
    captures = config.get("captures", [])
    expected: list[ExpectedCapture] = []
    for capture in captures:
        name = str(capture.get("name", "")).strip()
        route = str(capture.get("path", "")).strip()
        if name:
            expected.append(ExpectedCapture(name=name, filename=f"{name}.png", route=route or None))
    return expected


def load_expected_desktop(workflows_path: Path, workflow_name: str) -> list[ExpectedCapture]:
    workflows = load_json(workflows_path).get("workflows", [])
    workflow = next((item for item in workflows if item.get("name") == workflow_name), None)
    if workflow is None:
        raise ValueError(f"Desktop workflow '{workflow_name}' was not found in {workflows_path}.")

    expected: list[ExpectedCapture] = []
    for index, step in enumerate(workflow.get("steps", []), start=1):
        if step.get("capture", True) is False:
            continue

        capture_name = str(step.get("captureName") or f"{index:02d}-{step.get('title', 'capture')}").strip()
        if not capture_name:
            continue

        expected.append(
            ExpectedCapture(
                name=capture_name,
                filename=f"{capture_name}.png",
                page_tag=str(step.get("pageTag") or "").strip() or None,
            )
        )
    return expected


def paeth_predictor(left: int, up: int, upper_left: int) -> int:
    p = left + up - upper_left
    pa = abs(p - left)
    pb = abs(p - up)
    pc = abs(p - upper_left)
    if pa <= pb and pa <= pc:
        return left
    if pb <= pc:
        return up
    return upper_left


def unfilter_scanline(filter_type: int, current: bytearray, previous: bytes, bytes_per_pixel: int) -> bytes:
    if filter_type == 0:
        return bytes(current)

    for index in range(len(current)):
        left = current[index - bytes_per_pixel] if index >= bytes_per_pixel else 0
        up = previous[index] if previous else 0
        upper_left = previous[index - bytes_per_pixel] if previous and index >= bytes_per_pixel else 0
        if filter_type == 1:
            current[index] = (current[index] + left) & 0xFF
        elif filter_type == 2:
            current[index] = (current[index] + up) & 0xFF
        elif filter_type == 3:
            current[index] = (current[index] + ((left + up) // 2)) & 0xFF
        elif filter_type == 4:
            current[index] = (current[index] + paeth_predictor(left, up, upper_left)) & 0xFF
        else:
            raise ValueError(f"Unsupported PNG filter type {filter_type}.")
    return bytes(current)


def read_png_metrics(path: Path, max_samples: int = 5000) -> PngMetrics:
    if Image is not None:
        with Image.open(path) as image:
            rgb = image.convert("RGB")
            width, height = rgb.size
            sample = rgb.copy()
            sample.thumbnail((96, 96))
            pixel_reader = getattr(sample, "get_flattened_data", sample.getdata)
            pixels = list(pixel_reader())
        unique_colors = len(set(pixels))
        histogram = [0] * 16
        for red, green, blue in pixels:
            luminance = int((0.2126 * red) + (0.7152 * green) + (0.0722 * blue))
            histogram[min(15, luminance // 16)] += 1
        entropy = 0.0
        for count in histogram:
            if count:
                probability = count / len(pixels)
                entropy -= probability * math.log2(probability)
        return PngMetrics(width, height, len(pixels), unique_colors, entropy)

    data = path.read_bytes()
    if not data.startswith(PNG_SIGNATURE):
        raise ValueError("file is not a PNG image")

    offset = len(PNG_SIGNATURE)
    width = height = bit_depth = color_type = None
    palette: list[tuple[int, int, int]] = []
    idat_parts: list[bytes] = []
    while offset + 8 <= len(data):
        length = int.from_bytes(data[offset : offset + 4], "big")
        chunk_type = data[offset + 4 : offset + 8]
        chunk_data = data[offset + 8 : offset + 8 + length]
        offset += 12 + length

        if chunk_type == b"IHDR":
            width = int.from_bytes(chunk_data[0:4], "big")
            height = int.from_bytes(chunk_data[4:8], "big")
            bit_depth = chunk_data[8]
            color_type = chunk_data[9]
        elif chunk_type == b"PLTE":
            palette = [
                (chunk_data[index], chunk_data[index + 1], chunk_data[index + 2])
                for index in range(0, len(chunk_data), 3)
                if index + 2 < len(chunk_data)
            ]
        elif chunk_type == b"IDAT":
            idat_parts.append(chunk_data)
        elif chunk_type == b"IEND":
            break

    if width is None or height is None or bit_depth != 8 or color_type is None:
        raise ValueError("PNG must have 8-bit IHDR metadata")

    channels_by_color_type = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}
    channels = channels_by_color_type.get(color_type)
    if channels is None:
        raise ValueError(f"Unsupported PNG color type {color_type}.")

    stride = width * channels
    raw = zlib.decompress(b"".join(idat_parts))
    row_size = 1 + stride
    if len(raw) < row_size * height:
        raise ValueError("PNG pixel data is truncated")

    step = max(1, int(math.sqrt((width * height) / max_samples)))
    unique_colors: set[tuple[int, int, int]] = set()
    histogram = [0] * 16
    sampled = 0
    previous = b""
    for y in range(height):
        row_start = y * row_size
        filter_type = raw[row_start]
        scanline = bytearray(raw[row_start + 1 : row_start + row_size])
        decoded = unfilter_scanline(filter_type, scanline, previous, channels)
        previous = decoded
        if y % step != 0:
            continue

        for x in range(0, width, step):
            index = x * channels
            if color_type == 0:
                red = green = blue = decoded[index]
            elif color_type == 2:
                red, green, blue = decoded[index : index + 3]
            elif color_type == 3:
                red, green, blue = palette[decoded[index]]
            elif color_type == 4:
                red = green = blue = decoded[index]
            else:
                red, green, blue = decoded[index : index + 3]

            unique_colors.add((red, green, blue))
            luminance = int((0.2126 * red) + (0.7152 * green) + (0.0722 * blue))
            histogram[min(15, luminance // 16)] += 1
            sampled += 1

    entropy = 0.0
    for count in histogram:
        if count:
            probability = count / sampled
            entropy -= probability * math.log2(probability)

    return PngMetrics(width, height, sampled, len(unique_colors), entropy)


def validate_quality(
    path: Path,
    min_bytes: int,
    min_unique_colors: int,
    min_entropy: float,
    min_width: int,
    min_height: int,
) -> list[str]:
    errors: list[str] = []
    size = path.stat().st_size
    if size < min_bytes:
        errors.append(f"{path.name} is {size} bytes, below minimum {min_bytes}.")

    try:
        metrics = read_png_metrics(path)
    except Exception as exc:  # noqa: BLE001 - report image decode failures as validation errors
        return [*errors, f"{path.name} could not be decoded as a supported PNG: {exc}."]

    if metrics.width < min_width or metrics.height < min_height:
        errors.append(
            f"{path.name} dimensions are {metrics.width}x{metrics.height}, below minimum {min_width}x{min_height}."
        )
    if metrics.unique_colors < min_unique_colors:
        errors.append(
            f"{path.name} has {metrics.unique_colors} sampled color(s), below minimum {min_unique_colors}; capture is likely blank."
        )
    if metrics.entropy < min_entropy:
        errors.append(
            f"{path.name} luminance entropy is {metrics.entropy:.2f}, below minimum {min_entropy:.2f}; capture is likely low-entropy."
        )
    return errors


def validate_manifest_capture_path(name: str, capture_path: Any, expected_path: Path) -> list[str]:
    manifest_path = str(capture_path or "").strip()
    if not manifest_path:
        return [f"{name} manifest path is missing."]

    if Path(manifest_path).name != expected_path.name:
        return [f"{name} manifest path does not point to {expected_path.name}."]

    resolved_capture = repo_path(manifest_path)
    try:
        matches_expected_output = resolved_capture.resolve() == expected_path.resolve()
    except OSError:
        matches_expected_output = False

    if not matches_expected_output:
        return [f"{name} manifest capture path does not match the requested output file."]

    return []


def find_latest_desktop_manifest() -> Path | None:
    root = REPO_ROOT / "artifacts/desktop-workflows"
    if not root.exists():
        return None

    manifests = sorted(root.glob("**/manifest.json"), key=lambda path: path.stat().st_mtime, reverse=True)
    return manifests[0] if manifests else None


def validate_manifest_routes(
    surface: str,
    expected: list[ExpectedCapture],
    manifest_path: Path | None,
    output_dir: Path,
) -> tuple[list[str], datetime | None]:
    if manifest_path is None or not manifest_path.exists():
        return [], None

    manifest = load_json(manifest_path)
    expected_by_name = {item.name: item for item in expected}
    errors: list[str] = []
    started_at = parse_timestamp(manifest.get("generatedAtUtc")) or parse_timestamp(manifest.get("run", {}).get("startedAt"))
    expected_status = "passed" if surface == "web" else "ok"
    observed_status = manifest.get("status") if surface == "web" else manifest.get("run", {}).get("status")
    if observed_status and observed_status != expected_status:
        errors.append(f"Manifest status is {observed_status!r}, expected {expected_status!r}.")

    if surface == "web":
        observed_names = {str(capture.get("name", "")).strip() for capture in manifest.get("captures", [])}
        missing_manifest_entries = sorted(name for name in expected_by_name if name not in observed_names)
        if missing_manifest_entries:
            errors.append(f"Manifest is missing expected web capture entries: {', '.join(missing_manifest_entries)}.")

        for capture in manifest.get("captures", []):
            name = str(capture.get("name", "")).strip()
            if not name or name not in expected_by_name:
                continue
            configured = expected_by_name[name]
            if capture.get("status") != "passed":
                errors.append(f"{name} manifest status is {capture.get('status')!r}, expected 'passed'.")
            if configured.route and capture.get("route") != configured.route:
                errors.append(f"{name} captured route {capture.get('route')!r}, expected {configured.route!r}.")
            expected_path = str(capture.get("expectedPath") or urlparse(str(capture.get("url") or "")).path or "").strip()
            actual_path = str(capture.get("actualPath") or urlparse(str(capture.get("actualUrl") or "")).path or "").strip()
            if expected_path and actual_path and actual_path != expected_path:
                errors.append(f"{name} actual browser path {actual_path!r}, expected {expected_path!r}.")
            errors.extend(
                validate_manifest_capture_path(name, capture.get("path"), output_dir / configured.filename)
            )

    if surface == "desktop":
        observed_names = {
            Path(str(step.get("capturePath") or "")).stem
            for step in manifest.get("steps", [])
            if str(step.get("capturePath") or "").strip()
        }
        missing_manifest_entries = sorted(name for name in expected_by_name if name not in observed_names)
        if missing_manifest_entries:
            errors.append(f"Manifest is missing expected desktop capture entries: {', '.join(missing_manifest_entries)}.")

        for step in manifest.get("steps", []):
            capture_path = str(step.get("capturePath") or "")
            name = Path(capture_path).stem
            if not name or name not in expected_by_name:
                continue
            configured = expected_by_name[name]
            if step.get("status") != "ok":
                errors.append(f"{name} manifest status is {step.get('status')!r}, expected 'ok'.")
            observed = step.get("observedPageTag")
            if configured.page_tag and observed != configured.page_tag:
                errors.append(f"{name} observed page tag {observed!r}, expected {configured.page_tag!r}.")
            errors.extend(validate_manifest_capture_path(name, capture_path, output_dir / configured.filename))

    return errors, started_at


def default_min_entropy(surface: str) -> float:
    return 0.45 if surface == "web" else 1.0


def validate(args: argparse.Namespace) -> int:
    output_dir = repo_path(args.output_dir)
    expected = (
        load_expected_web(repo_path(args.web_routes))
        if args.surface == "web"
        else load_expected_desktop(repo_path(args.desktop_workflows), args.desktop_workflow)
    )
    manifest_path = repo_path(args.manifest) if args.manifest else (
        find_latest_desktop_manifest() if args.surface == "desktop" else None
    )

    errors: list[str] = []
    if not output_dir.exists():
        errors.append(f"Output directory does not exist: {output_dir.relative_to(REPO_ROOT)}")
        return fail(errors)

    expected_filenames = {item.filename for item in expected}
    png_files = {path.name for path in output_dir.glob("*.png")}
    missing = sorted(expected_filenames - png_files)
    unexpected = sorted(png_files - expected_filenames)
    if missing:
        errors.append(f"Missing expected screenshots: {', '.join(missing)}")
    if unexpected:
        errors.append(f"Unexpected screenshot files: {', '.join(unexpected)}")

    manifest_errors, started_at = validate_manifest_routes(args.surface, expected, manifest_path, output_dir)
    errors.extend(manifest_errors)

    min_entropy = args.min_entropy if args.min_entropy is not None else default_min_entropy(args.surface)

    freshness_cutoff = None
    if args.require_fresh:
        if started_at is not None:
            freshness_cutoff = started_at.timestamp() - args.freshness_slack_seconds
        else:
            freshness_cutoff = datetime.now(timezone.utc).timestamp() - (args.max_age_minutes * 60)

    for item in expected:
        path = output_dir / item.filename
        if not path.exists():
            continue
        errors.extend(
            validate_quality(
                path,
                min_bytes=args.min_bytes,
                min_unique_colors=args.min_unique_colors,
                min_entropy=min_entropy,
                min_width=args.min_width,
                min_height=args.min_height,
            )
        )
        if freshness_cutoff is not None and path.stat().st_mtime < freshness_cutoff:
            errors.append(f"{item.filename} is stale; it predates the capture freshness window.")

    if errors:
        return fail(errors)

    print(
        "Screenshot capture validation passed "
        f"({args.surface}, {len(expected)} expected capture(s), {len(png_files)} PNG file(s))."
    )
    return 0


def fail(errors: list[str]) -> int:
    print("Screenshot capture validation failed:", file=sys.stderr)
    for error in errors:
        print(f"- {error}", file=sys.stderr)
    return 1


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--surface", choices=["web", "desktop"], required=True)
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--web-routes", default="scripts/dev/web-screenshot-routes.json")
    parser.add_argument("--desktop-workflows", default="scripts/dev/desktop-workflows.json")
    parser.add_argument("--desktop-workflow", default="screenshot-catalog")
    parser.add_argument("--manifest")
    parser.add_argument("--require-fresh", action="store_true")
    parser.add_argument("--max-age-minutes", type=int, default=90)
    parser.add_argument("--freshness-slack-seconds", type=int, default=10)
    parser.add_argument("--min-bytes", type=int, default=8000)
    parser.add_argument("--min-unique-colors", type=int, default=16)
    parser.add_argument("--min-entropy", type=float, default=None)
    parser.add_argument("--min-width", type=int, default=400)
    parser.add_argument("--min-height", type=int, default=300)
    return parser


if __name__ == "__main__":
    raise SystemExit(validate(build_parser().parse_args()))
