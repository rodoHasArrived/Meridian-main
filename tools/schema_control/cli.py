"""Command-line orchestration for Meridian schema control."""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
from collections.abc import Mapping, Sequence
from datetime import date
from pathlib import Path
from typing import Any

from .common import Finding, fingerprint, write_json_if_changed, write_text_if_changed
from .dependencies import build_dependency_manifest
from .diffing import (
    build_manifest_diff,
    compare_artifact_trees,
    load_json_tree,
    render_diff_markdown,
)
from .migrations import (
    MigrationInventory,
    apply_migrations,
    build_migration_inventory,
    compare_immutable_migrations,
    detect_destructive_changes,
    git_base_file_reader,
)
from .render import render_snapshot


REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_CONFIG = Path("database/schema-control.json")
DEFAULT_POLICIES = Path("database/policies/schema-control.json")
DEFAULT_WAIVERS = Path("database/policies/migration-waivers.json")


def _load_json(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise ValueError(f"Required schema-control file was not found: {path}") from exc
    except json.JSONDecodeError as exc:
        raise ValueError(f"Invalid JSON in {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise ValueError(f"Expected a JSON object in {path}.")
    return value


def _resolve(root: Path, value: str | Path) -> Path:
    path = Path(value)
    return path.resolve() if path.is_absolute() else (root / path).resolve()


def _finding_sort_key(item: Mapping[str, Any]) -> tuple[str, str, str, str]:
    return (
        str(item.get("severity") or ""),
        str(item.get("rule_id") or ""),
        str(item.get("path") or item.get("object") or ""),
        str(item.get("message") or ""),
    )


def _read_waived_paths(
    waivers: Mapping[str, Any], category: str
) -> set[tuple[str, str]]:
    result: set[tuple[str, str]] = set()
    for item in waivers.get(category, []) or []:
        if not isinstance(item, Mapping):
            continue
        result.add((str(item.get("path") or ""), str(item.get("rule_id") or "")))
    return result


def _filter_waived_findings(
    findings: Sequence[Finding], waivers: Mapping[str, Any], category: str
) -> tuple[list[Finding], list[dict[str, Any]]]:
    waived_keys = _read_waived_paths(waivers, category)
    kept: list[Finding] = []
    applied: list[dict[str, Any]] = []
    raw_waivers = [
        item for item in waivers.get(category, []) or [] if isinstance(item, Mapping)
    ]
    for finding in findings:
        keys = {(finding.path or "", finding.rule_id), (finding.path or "", "")}
        if not (keys & waived_keys):
            kept.append(finding)
            continue
        matching = next(
            (
                item
                for item in raw_waivers
                if str(item.get("path") or "") == (finding.path or "")
                and str(item.get("rule_id") or "") in {"", finding.rule_id}
            ),
            {},
        )
        applied.append(
            {
                "category": category,
                "path": finding.path,
                "rule_id": finding.rule_id,
                "reason": str(
                    matching.get("reason") or "Approved schema-control waiver."
                ),
                "review_after": matching.get("review_after"),
            }
        )
    return kept, applied


def _configured_waivers(waivers: Mapping[str, Any]) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    for category in (
        "destructive_changes",
        "removed_migrations",
        "removed_migration_sets",
    ):
        for item in waivers.get(category, []) or []:
            if not isinstance(item, Mapping):
                continue
            result.append(
                {
                    "category": category,
                    "path": str(item.get("path") or ""),
                    "rule_id": str(item.get("rule_id") or ""),
                    "reason": str(
                        item.get("reason") or "Reviewed schema-control waiver."
                    ),
                    "review_after": item.get("review_after"),
                }
            )
    return sorted(
        result,
        key=lambda item: (
            str(item.get("category")),
            str(item.get("path")),
            str(item.get("rule_id")),
        ),
    )


def _waiver_findings(waivers: Mapping[str, Any]) -> list[Finding]:
    findings: list[Finding] = []
    for item in _configured_waivers(waivers):
        review_after = item.get("review_after")
        if not review_after:
            findings.append(
                Finding(
                    "migration-waiver-review-date-missing",
                    "error",
                    "Migration waiver must define an ISO review_after date.",
                    path=str(item.get("path") or ""),
                )
            )
            continue
        try:
            review_date = date.fromisoformat(str(review_after))
        except ValueError:
            findings.append(
                Finding(
                    "migration-waiver-review-date-invalid",
                    "error",
                    f"Migration waiver review_after is not a valid ISO date: {review_after}",
                    path=str(item.get("path") or ""),
                )
            )
            continue
        if review_date < date.today():
            findings.append(
                Finding(
                    "migration-waiver-review-overdue",
                    "error",
                    f"Migration waiver review was due on {review_date.isoformat()}.",
                    path=str(item.get("path") or ""),
                )
            )
    return findings


def _migration_directories(config: Mapping[str, Any]) -> dict[str, str]:
    return {
        str(item.get("directory") or "")
        .replace("\\", "/")
        .rstrip("/"): str(item.get("id") or "")
        for item in config.get("migration_sets", []) or []
        if isinstance(item, Mapping) and item.get("directory")
    }


def _base_schema_control_config(root: Path, base_ref: str) -> dict[str, Any] | None:
    result = subprocess.run(
        ["git", "show", f"{base_ref}:database/schema-control.json"],
        cwd=root,
        text=True,
        capture_output=True,
        check=False,
    )
    if result.returncode != 0:
        return None
    try:
        value = json.loads(result.stdout)
    except json.JSONDecodeError as exc:
        raise ValueError(
            f"Invalid database/schema-control.json at git base ref '{base_ref}': {exc}"
        ) from exc
    if not isinstance(value, dict):
        raise ValueError(
            f"database/schema-control.json at git base ref '{base_ref}' must be an object."
        )
    return value


def _removed_migration_set_findings(
    base_config: Mapping[str, Any] | None,
    current_config: Mapping[str, Any],
) -> list[Finding]:
    if base_config is None:
        return []
    baseline = _migration_directories(base_config)
    current = _migration_directories(current_config)
    return [
        Finding(
            "migration-set-registry-removed",
            "error",
            f"Migration set '{baseline[directory]}' was removed from the schema-control registry.",
            path=directory,
            subject=baseline[directory],
        )
        for directory in sorted(set(baseline) - set(current))
    ]


def _deleted_migration_findings(
    root: Path,
    base_ref: str,
    config: Mapping[str, Any],
    base_config: Mapping[str, Any] | None,
) -> list[Finding]:
    registered_directories = set(_migration_directories(config))
    if base_config is not None:
        registered_directories.update(_migration_directories(base_config))
    command = [
        "git",
        "diff",
        "--find-renames",
        "--name-status",
        "--diff-filter=DR",
        base_ref,
        "--",
    ]
    result = subprocess.run(
        command, cwd=root, text=True, capture_output=True, check=False
    )
    if result.returncode != 0:
        raise ValueError(
            f"Unable to inspect removed migrations against '{base_ref}': {result.stderr.strip()}"
        )
    findings: list[Finding] = []
    for line in result.stdout.splitlines():
        parts = line.split("\t")
        status = parts[0] if parts else ""
        if status == "D" and len(parts) >= 2:
            path = parts[1].replace("\\", "/")
            renamed_to = None
        elif status.startswith("R") and len(parts) >= 3:
            path = parts[1].replace("\\", "/")
            renamed_to = parts[2].replace("\\", "/")
        else:
            continue
        parent_is_migrations = "/migrations/" in f"/{path.lower()}"
        registered = any(
            path == directory or path.startswith(f"{directory}/")
            for directory in registered_directories
        )
        if not path.lower().endswith(".sql") or not (
            parent_is_migrations or registered
        ):
            continue
        action = f" renamed to '{renamed_to}'" if renamed_to else " removed"
        findings.append(
            Finding(
                (
                    "migration-immutable-file-renamed"
                    if renamed_to
                    else "migration-immutable-file-removed"
                ),
                "error",
                f"Existing migration was{action}; preserve its applied-history key or record a reviewed one-time waiver.",
                path=path,
                subject=renamed_to,
            )
        )
    return findings


def _migration_manifest(
    root: Path,
    config: Mapping[str, Any],
    waivers: Mapping[str, Any],
    base_ref: str | None,
) -> tuple[MigrationInventory, dict[str, Any]]:
    inventory = build_migration_inventory(root, config)
    findings = list(inventory.findings)
    findings.extend(_waiver_findings(waivers))
    if base_ref:
        base_config = _base_schema_control_config(root, base_ref)
        base_reader = git_base_file_reader(root, base_ref)
        findings.extend(compare_immutable_migrations(inventory, base_reader))
        destructive, _ = _filter_waived_findings(
            detect_destructive_changes(inventory, base_reader),
            waivers,
            "destructive_changes",
        )
        findings.extend(destructive)
        removed, _ = _filter_waived_findings(
            _deleted_migration_findings(root, base_ref, config, base_config),
            waivers,
            "removed_migrations",
        )
        findings.extend(removed)
        removed_sets, _ = _filter_waived_findings(
            _removed_migration_set_findings(base_config, config),
            waivers,
            "removed_migration_sets",
        )
        findings.extend(removed_sets)

    manifest = inventory.to_dict()
    configured_sets = {
        str(item.get("id")): item
        for item in config.get("migration_sets", []) or []
        if isinstance(item, Mapping)
    }
    for item in manifest.get("migration_sets", []):
        configured = configured_sets.get(str(item.get("id")), {})
        for key in ("display_name", "owner", "drift_policy", "connection_variables"):
            if key in configured:
                item[key] = configured[key]
    manifest["format"] = "meridian.migration-manifest.v1"
    manifest["findings"] = sorted(
        [item.to_dict() for item in findings], key=_finding_sort_key
    )
    manifest["waivers"] = _configured_waivers(waivers)
    manifest["summary"] = {
        "migration_sets": len(manifest.get("migration_sets", [])),
        "files": len(manifest.get("files", [])),
        "errors": sum(item["severity"] == "error" for item in manifest["findings"]),
        "warnings": sum(item["severity"] == "warning" for item in manifest["findings"]),
        "waivers": len(manifest["waivers"]),
    }
    manifest["fingerprint"] = fingerprint(
        {key: value for key, value in manifest.items() if key != "fingerprint"}
    )
    return inventory, manifest


def _schema_count(catalog: Mapping[str, Any]) -> int:
    schemas = catalog.get("schemas", [])
    return len(schemas) if isinstance(schemas, (list, dict)) else 0


def _relation_count(catalog: Mapping[str, Any]) -> int:
    schemas = catalog.get("schemas", [])
    if isinstance(schemas, Mapping):
        values = schemas.values()
    elif isinstance(schemas, list):
        values = schemas
    else:
        values = []
    count = 0
    for schema in values:
        if not isinstance(schema, Mapping):
            continue
        if isinstance(schema.get("relations"), list):
            count += len(schema["relations"])
        else:
            count += sum(
                len(schema.get(key, []) or [])
                for key in ("tables", "views", "materialized_views")
            )
    return count


def _contract_count(contracts: Mapping[str, Any]) -> int:
    for key in ("types", "objects", "contracts"):
        value = contracts.get(key)
        if isinstance(value, (list, dict)):
            return len(value)
    summary = contracts.get("summary", {})
    return (
        int(summary.get("types", summary.get("total", 0)))
        if isinstance(summary, Mapping)
        else 0
    )


def _render_summary(
    *,
    migration_manifest: Mapping[str, Any],
    catalog: Mapping[str, Any] | None = None,
    contracts: Mapping[str, Any] | None = None,
    policy_report: Mapping[str, Any] | None = None,
    drift: Mapping[str, Any] | None = None,
    status: str,
) -> str:
    migration_summary = migration_manifest.get("summary", {})
    lines = [
        "# PostgreSQL schema control",
        "",
        f"**Status:** {status}",
        "",
        f"- Migration modules: {migration_summary.get('migration_sets', 0)}",
        f"- Migration files: {migration_summary.get('files', 0)}",
        f"- Migration errors: {migration_summary.get('errors', 0)}",
        f"- Configured migration waivers: {migration_summary.get('waivers', 0)}",
    ]
    if catalog is not None:
        lines.extend(
            [
                f"- Physical schemas: {_schema_count(catalog)}",
                f"- Relations: {_relation_count(catalog)}",
            ]
        )
    if contracts is not None:
        lines.append(f"- Public contract objects: {_contract_count(contracts)}")
    if policy_report is not None:
        counts = policy_report.get("counts", policy_report.get("summary", {}))
        lines.extend(
            [
                f"- Policy errors: {counts.get('error', counts.get('errors', 0))}",
                f"- Policy warnings: {counts.get('warning', counts.get('warnings', 0))}",
            ]
        )
    if drift is not None:
        lines.append(
            f"- Committed artifacts current: {str(bool(drift.get('clean'))).lower()}"
        )
        for key in ("added", "removed", "changed"):
            values = drift.get(key, []) or []
            if values:
                lines.extend(["", f"## {key.title()} artifacts", ""])
                lines.extend(f"- `{item}`" for item in values)
    return "\n".join(lines) + "\n"


def _prepare_candidate_root(root: Path, candidate_root: Path) -> None:
    resolved_root = root.resolve()
    resolved_candidate = candidate_root.resolve()
    if resolved_candidate == resolved_root or resolved_root.is_relative_to(
        resolved_candidate
    ):
        raise ValueError(
            "Candidate root cannot be the repository root or one of its ancestors."
        )
    resolved_candidate.mkdir(parents=True, exist_ok=True)
    for relative_path in ("manifest", "docs", "reports"):
        path = resolved_candidate / relative_path
        if path.is_symlink() or path.is_file():
            path.unlink()
        elif path.is_dir():
            shutil.rmtree(path)
    render_manifest = resolved_candidate / "render-manifest.json"
    if render_manifest.is_symlink() or render_manifest.is_file():
        render_manifest.unlink()


def create_snapshot(
    *,
    root: Path,
    config: Mapping[str, Any],
    policies: Mapping[str, Any],
    waivers: Mapping[str, Any],
    database_url: str,
    candidate_root: Path,
    base_ref: str | None,
) -> tuple[dict[str, Any], bool]:
    inventory, migration_manifest = _migration_manifest(root, config, waivers, base_ref)
    migration_failed = bool(migration_manifest["summary"]["errors"])
    _prepare_candidate_root(root, candidate_root)
    if migration_failed:
        reports = candidate_root / "reports"
        write_json_if_changed(reports / "migration-report.json", migration_manifest)
        write_text_if_changed(
            reports / "summary.md",
            _render_summary(
                migration_manifest=migration_manifest,
                status="failed migration safety checks",
            ),
        )
        return {"migrations": migration_manifest}, True

    apply_result = apply_migrations(database_url, inventory)

    from .catalog import extract_catalog
    from .contracts import build_contract_manifest
    from .policies import evaluate_policies

    catalog = extract_catalog(database_url, dict(config))
    contracts = build_contract_manifest(root, dict(config))
    dependencies = build_dependency_manifest(catalog, contracts, config)
    policy_report = evaluate_policies(catalog, migration_manifest, dict(policies))
    application_report = {
        "format": "meridian.migration-application.v1",
        "applied": list(apply_result.applied),
        "skipped": list(apply_result.skipped),
    }
    application_report["fingerprint"] = fingerprint(application_report)
    write_json_if_changed(
        candidate_root / "reports" / "migration-application.json",
        application_report,
    )
    render_snapshot(
        candidate_root,
        catalog=catalog,
        migrations=migration_manifest,
        contracts=contracts,
        dependencies=dependencies,
        policy_report=policy_report,
        config=config,
    )
    failed = bool(policy_report.get("failed"))
    write_text_if_changed(
        candidate_root / "reports" / "summary.md",
        _render_summary(
            migration_manifest=migration_manifest,
            catalog=catalog,
            contracts=contracts,
            policy_report=policy_report,
            status="failed policy checks" if failed else "candidate generated",
        ),
    )
    return {
        "migrations": migration_manifest,
        "catalog": catalog,
        "contracts": contracts,
        "dependencies": dependencies,
        "policies": policy_report,
        "application": application_report,
    }, failed


def _merge_drift(
    manifest_drift: Mapping[str, Any], docs_drift: Mapping[str, Any]
) -> dict[str, Any]:
    result = {
        "format": "meridian.schema-artifact-drift.v1",
        "clean": bool(manifest_drift.get("clean")) and bool(docs_drift.get("clean")),
        "added": sorted(f"manifest/{item}" for item in manifest_drift.get("added", []))
        + sorted(f"docs/{item}" for item in docs_drift.get("added", [])),
        "removed": sorted(
            f"manifest/{item}" for item in manifest_drift.get("removed", [])
        )
        + sorted(f"docs/{item}" for item in docs_drift.get("removed", [])),
        "changed": sorted(
            f"manifest/{item}" for item in manifest_drift.get("changed", [])
        )
        + sorted(f"docs/{item}" for item in docs_drift.get("changed", [])),
    }
    result["fingerprint"] = fingerprint(result)
    return result


def _promote(root: Path, config: Mapping[str, Any], candidate_root: Path) -> None:
    outputs = config.get("outputs", {})
    if not isinstance(outputs, Mapping):
        raise ValueError("schema-control outputs must be a JSON object.")
    pairs = [
        (
            (candidate_root / "manifest").resolve(),
            _resolve(root, str(outputs.get("manifest") or "database/manifest")),
        ),
        (
            (candidate_root / "docs").resolve(),
            _resolve(root, str(outputs.get("docs") or "docs/generated/database")),
        ),
    ]
    resolved_root = root.resolve()
    for source, destination in pairs:
        if not source.is_dir():
            raise ValueError(f"Candidate artifact directory is missing: {source}")
        if destination == resolved_root or not destination.is_relative_to(
            resolved_root
        ):
            raise ValueError(
                f"Refusing to promote outside the repository: {destination}"
            )
        if (
            source == destination
            or source.is_relative_to(destination)
            or destination.is_relative_to(source)
        ):
            raise ValueError(
                f"Candidate source and tracked destination overlap: {source} and {destination}"
            )

    destinations = [destination for _, destination in pairs]
    for index, destination in enumerate(destinations):
        for other in destinations[index + 1 :]:
            if (
                destination == other
                or destination.is_relative_to(other)
                or other.is_relative_to(destination)
            ):
                raise ValueError(
                    f"Schema-control output destinations overlap: {destination} and {other}"
                )

    staged: list[tuple[Path, Path]] = []
    backups: list[tuple[Path, Path | None]] = []

    def remove_path(path: Path) -> None:
        if path.is_symlink() or path.is_file():
            path.unlink()
        elif path.is_dir():
            shutil.rmtree(path)

    try:
        for source, destination in pairs:
            destination.parent.mkdir(parents=True, exist_ok=True)
            stage = destination.with_name(f".{destination.name}.schema-control-stage")
            remove_path(stage)
            shutil.copytree(source, stage)
            staged.append((stage, destination))

        for stage, destination in staged:
            backup = destination.with_name(f".{destination.name}.schema-control-backup")
            remove_path(backup)
            had_destination = destination.exists() or destination.is_symlink()
            if had_destination:
                destination.rename(backup)
            try:
                stage.rename(destination)
            except Exception:
                if had_destination and backup.exists():
                    backup.rename(destination)
                raise
            backups.append((destination, backup if had_destination else None))
    except Exception:
        for destination, backup in reversed(backups):
            remove_path(destination)
            if backup is not None and backup.exists():
                backup.rename(destination)
        raise
    finally:
        for stage, _ in staged:
            remove_path(stage)

    for _, backup in backups:
        if backup is not None:
            remove_path(backup)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Build and verify Meridian PostgreSQL schema artifacts."
    )
    parser.add_argument("--root", default=str(REPO_ROOT), help="Repository root.")
    parser.add_argument(
        "--config", default=str(DEFAULT_CONFIG), help="Schema-control registry JSON."
    )
    parser.add_argument(
        "--policies", default=str(DEFAULT_POLICIES), help="Schema policy JSON."
    )
    parser.add_argument(
        "--waivers", default=str(DEFAULT_WAIVERS), help="Migration waiver JSON."
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    inventory = subparsers.add_parser(
        "inventory", help="Run static migration checks without PostgreSQL."
    )
    inventory.add_argument("--base-ref", default=None)
    inventory.add_argument("--output", default="build/schema-control/migrations.json")

    for name, help_text in (
        ("snapshot", "Build a candidate database and write candidate artifacts."),
        (
            "verify",
            "Build candidate artifacts and compare them with committed artifacts.",
        ),
    ):
        command = subparsers.add_parser(name, help=help_text)
        command.add_argument("--database-url", required=True)
        command.add_argument("--base-ref", default=None)
        command.add_argument(
            "--candidate-root", default="build/schema-control/candidate"
        )

    promote = subparsers.add_parser(
        "promote", help="Copy a reviewed candidate into committed outputs."
    )
    promote.add_argument("--candidate-root", default="build/schema-control/candidate")
    return parser


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    return build_parser().parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv)
    root = Path(args.root).resolve()
    try:
        config = _load_json(_resolve(root, args.config))
        policies = _load_json(_resolve(root, args.policies))
        waivers = _load_json(_resolve(root, args.waivers))
        if args.command == "inventory":
            _, manifest = _migration_manifest(root, config, waivers, args.base_ref)
            write_json_if_changed(_resolve(root, args.output), manifest)
            print(
                f"Schema migration inventory: {manifest['summary']['migration_sets']} module(s), "
                f"{manifest['summary']['files']} file(s), {manifest['summary']['errors']} error(s)."
            )
            return 1 if manifest["summary"]["errors"] else 0

        candidate_root = _resolve(root, args.candidate_root)
        if args.command == "promote":
            _promote(root, config, candidate_root)
            print("Promoted schema-control candidate manifests and docs.")
            return 0

        snapshot, failed = create_snapshot(
            root=root,
            config=config,
            policies=policies,
            waivers=waivers,
            database_url=args.database_url,
            candidate_root=candidate_root,
            base_ref=args.base_ref,
        )
        if args.command == "snapshot":
            print(f"Schema-control candidate written to {candidate_root}.")
            return 1 if failed else 0

        outputs = config.get("outputs", {})
        expected_manifest = _resolve(
            root, str(outputs.get("manifest") or "database/manifest")
        )
        expected_docs = _resolve(
            root, str(outputs.get("docs") or "docs/generated/database")
        )
        manifest_drift = compare_artifact_trees(
            expected_manifest, candidate_root / "manifest"
        )
        docs_drift = compare_artifact_trees(expected_docs, candidate_root / "docs")
        drift = _merge_drift(manifest_drift, docs_drift)
        reports_root = candidate_root / "reports"
        write_json_if_changed(reports_root / "artifact-drift.json", drift)

        base_tree = load_json_tree(expected_manifest)
        candidate_tree = load_json_tree(candidate_root / "manifest")
        structural_diff = build_manifest_diff(base_tree, candidate_tree)
        write_json_if_changed(reports_root / "schema-diff.json", structural_diff)
        write_text_if_changed(
            reports_root / "schema-diff.md", render_diff_markdown(structural_diff)
        )
        write_text_if_changed(
            reports_root / "summary.md",
            _render_summary(
                migration_manifest=snapshot.get("migrations", {}),
                catalog=snapshot.get("catalog"),
                contracts=snapshot.get("contracts"),
                policy_report=snapshot.get("policies"),
                drift=drift,
                status="passed" if not failed and drift["clean"] else "failed",
            ),
        )
        if failed:
            print("Schema-control policy validation failed.", file=sys.stderr)
            return 1
        if not drift["clean"]:
            print(
                "Committed schema-control manifests or documentation are stale.",
                file=sys.stderr,
            )
            return 1
        print("Schema-control verification passed.")
        return 0
    except (OSError, RuntimeError, ValueError) as exc:
        print(f"schema-control: {exc}", file=sys.stderr)
        return 2
