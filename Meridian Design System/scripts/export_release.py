#!/usr/bin/env python3
"""Validate and export a deterministic, curated Meridian design-system ZIP.

No application snapshots, user uploads, scratch screenshots, dependencies or credentials
are exported. SHA256SUMS covers every payload file plus export-inventory.json; the
adjacent .sha256 file covers the finished ZIP. No network or third-party Python required.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import io
import json
import posixpath
import re
import sys
import zipfile
from pathlib import Path, PurePosixPath

_metadata_spec = importlib.util.spec_from_file_location(
    "meridian_manifest_metadata", Path(__file__).with_name("sync_manifest_metadata.py")
)
_metadata = importlib.util.module_from_spec(_metadata_spec)
_metadata_spec.loader.exec_module(_metadata)

ROOT_FILES = {
    "package.json", "package-lock.json", "release.json", "release-build.json", "_ds_manifest.json", "LICENSE",
    "_ds_bundle.js", "_preview_runtime.js", "_preview_jsx.js", "README.md", "SKILL.md", "CHANGELOG.md",
    "PATTERNS.md", "BRAND_GUIDELINES.md", "CONTENT_FUNDAMENTALS.md", "ICONOGRAPHY.md",
    "INSPIRATION_BRIEF.md", "VISUAL_FOUNDATIONS.md", "THIRD_PARTY_NOTICES.md",
    "governance-baseline.json", "styles.css", "colors_and_type.css", "index.html",
    "support.js", "thumbnail.html", ".thumbnail", "Canvas.dc.html", "CONTRIBUTING.md", "github.md", "_adherence.oxlintrc.json",
}
ROOT_DIRS = {"api", "assets", "components", "docs", "guidelines", "preview", "scripts", "templates", "tests", "tokens", "ui_kits"}
SKIP_PARTS = {"node_modules", ".git", "__pycache__", "dist", "uploads", "scraps", "screenshots", "test-results", "playwright-report"}
SUFFIXES = {".css", ".html", ".md", ".js", ".jsx", ".mjs", ".ts", ".json", ".py", ".sh", ".svg", ".png", ".ttf", ".woff", ".woff2", ".txt"}
SECRET_NAME = re.compile(r"(?:^\.env(?:\.|$)|\.(?:pem|key|p12|pfx)$|(?:credential|secret|id_rsa))", re.I)
SECRET_CONTENT = re.compile(rb"-----BEGIN (?:[A-Z ]*PRIVATE KEY)-----|\b(?:sk-proj-|gh[pousr]_)[A-Za-z0-9_-]{24,}")


def sha256(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def canonical(data: object) -> bytes:
    return (json.dumps(data, indent=2, ensure_ascii=False, sort_keys=True) + "\n").encode("utf-8")


def safe_member(name: str) -> bool:
    path = PurePosixPath(name)
    return (bool(name) and path.as_posix() == name and not path.is_absolute()
            and not any(part in {".", ".."} for part in name.split("/"))
            and "\\" not in name and "\x00" not in name and not re.match(r"^[A-Za-z]:", name))


def payload(root: Path) -> dict[str, bytes]:
    result = {}
    for path in sorted(root.rglob("*")):
        rel = path.relative_to(root)
        if any(part in SKIP_PARTS for part in rel.parts):
            continue
        if path.is_symlink():
            raise ValueError(f"Refusing symbolic link: {rel}")
        if not path.is_file():
            continue
        included = rel.as_posix() in ROOT_FILES or (rel.parts[0] in ROOT_DIRS and (path.suffix in SUFFIXES or path.name == ".thumbnail"))
        if not included:
            continue
        if not safe_member(rel.as_posix()) or SECRET_NAME.search(path.name):
            raise ValueError(f"Unsafe export path: {rel}")
        content = path.read_bytes()
        if SECRET_CONTENT.search(content):
            raise ValueError(f"Potential credential in export: {rel}")
        result[rel.as_posix()] = content
    return result


def require_file(data: dict[str, bytes], name: str) -> bytes:
    if not safe_member(name) or name not in data:
        raise ValueError(f"Required distribution file missing: {name}")
    return data[name]


def validate(data: dict[str, bytes]) -> dict:
    package = json.loads(require_file(data, "package.json"))
    version = package["version"]
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise ValueError("Release version must be a stable semantic version")
    lock = json.loads(require_file(data, "package-lock.json"))
    if lock["version"] != version or lock["packages"][""]["version"] != version:
        raise ValueError("Package lock version does not match package.json")
    identity = json.loads(require_file(data, "release.json"))
    release = json.loads(require_file(data, "release-build.json"))
    if identity["version"] != version:
        raise ValueError("release.json authoritative version mismatch")
    manifest = json.loads(require_file(data, "_ds_manifest.json"))
    require_file(data, "scripts/sync_manifest_metadata.py")
    _metadata.require_current_manifest(manifest, data)
    bundle = require_file(data, "_ds_bundle.js")
    header_match = re.match(rb"/\* @ds-bundle: (.+?) \*/", bundle)
    if not header_match:
        raise ValueError("Bundle header missing")
    header = json.loads(header_match[1])
    for label, metadata in [("build", release), ("bundle", header)]:
        if metadata.get("version") != version or metadata.get("namespace") != "MeridianDesignSystem_205d17":
            raise ValueError(f"{label} version or namespace mismatch")
    if manifest.get("namespace") != release["namespace"]:
        raise ValueError("Manifest namespace differs")
    if manifest["components"] != header["components"]:
        raise ValueError("Manifest and bundle public exports differ")
    if header.get("sourceHashAlgorithm") != "sha256" or release["build"]["sourceHashes"] != header["sourceHashes"]:
        raise ValueError("Source hash metadata differs")
    actual_sources = {path for path in data if path.startswith(("components/", "api/")) and Path(path).suffix in {".js", ".jsx"}}
    actual_sources.update({"tests/conformance-rules.js", "tests/mock-transport.js"})
    if actual_sources != set(header["sourceHashes"]):
        raise ValueError("Component/API source inventory changed after build; rebuild required")
    bundled_sources = release["build"].get("bundledSources")
    public_sources = {component["sourcePath"] for component in manifest["components"]}
    if (not isinstance(bundled_sources, list) or len(bundled_sources) != len(set(bundled_sources))
            or not public_sources <= set(bundled_sources) <= actual_sources):
        raise ValueError("Bundled source inventory must include public modules and only retained sources")
    for path, expected in header["sourceHashes"].items():
        if sha256(require_file(data, path)) != expected:
            raise ValueError(f"Source changed after build: {path}")
    for path, expected in release["assets"].items():
        content = require_file(data, path)
        if sha256(content) != expected["sha256"] or len(content) != expected["bytes"]:
            raise ValueError(f"Generated asset changed after build: {path}")
    for record in manifest["components"]:
        require_file(data, record["sourcePath"])
    families = json.loads(require_file(data, "api/families.json"))
    assigned = [record["name"] for record in families["exports"]]
    exported = {record["name"] for record in manifest["components"]}
    if families.get("namespace") != manifest["namespace"] or set(assigned) != exported or len(assigned) != len(exported):
        raise ValueError("API family membership differs from the public manifest")
    if families.get("unclassified"):
        raise ValueError("API family membership has unclassified exports")
    for family in families["counts"]:
        barrel = require_file(data, f"api/{family}.js").decode()
        names = re.search(r"var NAMES = (\[[^;]+\]);", barrel)
        expected = {record["name"] for record in families["exports"] if record["family"] == family}
        if not names or set(json.loads(names[1])) != expected:
            raise ValueError(f"API barrel membership differs: {family}")
    for record in manifest["cards"]:
        require_file(data, record["path"])
    for record in manifest["templates"]:
        require_file(data, record["entryPath"])
        if record.get("thumbnail"):
            require_file(data, record["thumbnail"]["path"])
    for path in manifest["globalCssPaths"]:
        require_file(data, path)
    for font in manifest["fonts"]:
        for path in font.get("files", []):
            require_file(data, path)
    for path in ["assets/fonts/OFL-IBMPlexSans.txt", "assets/fonts/OFL-IBMPlexMono.txt"]:
        require_file(data, path)
    for path in ["preview/release-readiness.html", "preview/release-readiness.js", "preview/release-readiness.css", "tests/gallery.html", "tests/gallery.mjs", "tests/gallery.css", "THIRD_PARTY_NOTICES.md", "LICENSE"]:
        require_file(data, path)
    preview = data["preview/release-readiness.html"]
    for reference in re.findall(rb'(?:src|href)=["\']([^"\']+)["\']', preview):
        reference = reference.decode()
        if reference.startswith(("#", "https:", "http:", "data:", "mailto:")):
            continue
        require_file(data, posixpath.normpath(posixpath.join("preview", reference.split("#", 1)[0].split("?", 1)[0])))
    for name, content in data.items():
        if name.endswith(".css"):
            for imported in re.findall(rb'@import\s+["\']([^"\']+)["\']', content):
                target = (PurePosixPath(name).parent / imported.decode()).as_posix()
                # Normalize only local CSS closure paths; external CSS is disclosed separately.
                if ":" not in target:
                    require_file(data, posixpath.normpath(target))
    counts = release["counts"]
    for key, records in [("exports", manifest["components"]), ("cards", manifest["cards"]), ("templates", manifest["templates"]), ("tokenRecords", manifest["tokens"])]:
        if counts[key] != len(records):
            raise ValueError(f"Release inventory count differs: {key}")
    return release


def archive(data: dict[str, bytes], release: dict) -> bytes:
    for name in data:
        if not safe_member(name):
            raise ValueError(f"Unsafe archive member: {name}")
    inventory = {"name": release["name"], "version": release["version"], "namespace": release["namespace"],
                 "files": [{"path": path, "bytes": len(content), "sha256": sha256(content)} for path, content in sorted(data.items())],
                 "externalRequirements": release["externalRequirements"],
                 "excluded": ["uploads", "scraps", "screenshots", "src (embedded app snapshot)", "standalone embedded reconciliation HTML", "node_modules", "local runtime snapshots", "credentials"]}
    contents = {**data, "export-inventory.json": canonical(inventory)}
    contents["SHA256SUMS"] = "".join(f"{sha256(content)}  {path}\n" for path, content in sorted(contents.items())).encode()
    output = io.BytesIO()
    prefix = f"meridian-design-system-{release['version']}"
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zipped:
        for name, content in sorted(contents.items()):
            info = zipfile.ZipInfo(f"{prefix}/{name}", date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            zipped.writestr(info, content, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    return output.getvalue()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--check", action="store_true", help="Validate without creating an archive")
    parser.add_argument("--output", type=Path, help="ZIP output path (default: dist/meridian-design-system-VERSION.zip)")
    args = parser.parse_args()
    root = args.root.resolve()
    try:
        data = payload(root)
        release = validate(data)
        if args.check:
            print(f"Meridian {release['version']}: release checks passed; {len(data)} curated files")
            return 0
        output = args.output or root / "dist" / f"meridian-design-system-{release['version']}.zip"
        content = archive(data, release)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_bytes(content)
        output.with_suffix(output.suffix + ".sha256").write_text(f"{sha256(content)}  {output.name}\n", encoding="utf-8")
        print(f"Exported {output} ({len(content):,} bytes; {len(data)} payload files)")
        print(f"SHA256 {sha256(content)}")
        return 0
    except (ValueError, KeyError, json.JSONDecodeError) as error:
        print(f"Release validation failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
