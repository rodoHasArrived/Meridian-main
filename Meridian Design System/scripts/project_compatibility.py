#!/usr/bin/env python3
"""Assemble and validate reviewed project compatibility without inferring test success.

The public manifest owns export/card identity, families owns maturity, and template
eligibility owns retained entry points. Human/source review and executed browser
evidence remain separate from mechanically discovered imports and consumers.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parent
INVENTORY = ROOT / "api/project-compatibility.json"
KINDS = {"component", "provider", "hook-carrier", "utility", "constant"}
DECISIONS = {"adopt", "retain-existing", "no-current-use", "repair-before-use"}
IMPORT = re.compile(r"(?:from\s*|import\s*)[\"']([^\"']+)[\"']")


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def companion(source: Path, suffix: str):
    # Lowercase helpers and uppercase carriers can share a stem on Windows.
    # An exact-extension companion avoids localDate.d.ts / LocalDate.d.ts collisions.
    candidates = [source.with_name(source.name + suffix), source.with_suffix(suffix)]
    return next((path for path in candidates if path.is_file()), None)


def records(value, key=None):
    if isinstance(value, list):
        return value
    if isinstance(value, dict):
        if key and key in value:
            return records(value[key])
        for candidate in ("exports", "reviews", "records", "entries", "screens", "templates"):
            if candidate in value:
                return records(value[candidate])
        return [dict(v, **({key: k} if key and key not in v else {}))
                for k, v in value.items() if isinstance(v, dict)]
    return []


def dependencies(source: Path):
    result = []
    for specifier in sorted(set(IMPORT.findall(source.read_text(encoding="utf-8")))):
        if not specifier.startswith("."):
            result.append({"specifier": specifier, "kind": "peer"})
            continue
        target = source.parent / specifier
        candidates = [target] + [Path(str(target) + suffix) for suffix in
                                  (".js", ".jsx", ".ts", ".tsx", ".json", ".css")]
        resolved = next((p.resolve() for p in candidates if p.is_file()), None)
        result.append({"specifier": specifier, "kind": "source",
                       "path": resolved.relative_to(ROOT).as_posix() if resolved and resolved.is_relative_to(ROOT) else None})
    return result


def screen_paths(repo=REPO):
    dashboard = repo / "src/Meridian.Ui/dashboard/src"
    return sorted(p.relative_to(repo).as_posix()
                  for folder in ("screens", "components", "design-system", "dev")
                  for p in (dashboard / folder).rglob("*.tsx")
                  if not p.name.endswith((".test.tsx", ".spec.tsx")))


def template_paths(eligibility):
    return sorted(f"templates/{slug}/{entry}"
                  for slug, item in eligibility["templates"].items()
                  for entry in item["entries"])


def merge_reviews(directory: Path):
    exports, screens, templates = {}, {}, {}
    for filename in ("core-review.json", "domain-review.json"):
        path = directory / filename
        if path.exists():
            for item in records(read_json(path), "name"):
                if item.get("name") in exports:
                    raise ValueError(f"Duplicate reviewer ownership: {item['name']}")
                exports[item.get("name")] = item
    for filename in ("ui-primary-review.json", "ui-remaining-review.json", "ui-extra-review.json"):
        path = directory / filename
        if path.exists():
            for item in records(read_json(path), "path"):
                key = item.get("path", item.get("file"))
                if key in screens:
                    raise ValueError(f"Duplicate screen reviewer ownership: {key}")
                screens[key] = item
    path = directory / "governance-review.json"
    governance = read_json(path) if path.exists() else {}
    if isinstance(governance, dict):
        for item in records(governance.get("templateReviews", governance.get("templates", [])), "path"):
            templates[item.get("path", item.get("entryPath"))] = item
    return exports, screens, templates, governance


def assemble(directory: Path, browser_path: Path | None = None):
    manifest = read_json(ROOT / "_ds_manifest.json")
    families = read_json(ROOT / "api/families.json")
    eligibility = read_json(ROOT / "api/template-eligibility.json")
    family = {item["name"]: item for item in families["exports"]}
    export_reviews, screen_reviews, template_reviews, governance = merge_reviews(directory)
    behavior = {}
    for filename in ("core-behavior-coverage.json", "domain-behavior-coverage.json"):
        path = ROOT / "tests" / filename
        if path.is_file():
            for record in read_json(path)["exports"]:
                if record["name"] in behavior:
                    raise ValueError(f"Duplicate behavior ownership: {record['name']}")
                behavior[record["name"]] = record
    browser = read_json(browser_path) if browser_path and browser_path.exists() else None
    exports = []
    for item in sorted(manifest["components"], key=lambda x: x["name"]):
        source = ROOT / item["sourcePath"]
        declaration = companion(source, ".d.ts")
        guide = companion(source, ".prompt.md")
        exports.append({
            "name": item["name"], "source": item["sourcePath"],
            "sourceHash": digest(source), "family": family[item["name"]]["family"],
            "maturity": family[item["name"]]["stability"],
            "declaration": declaration.relative_to(ROOT).as_posix() if declaration else None,
            "guide": guide.relative_to(ROOT).as_posix() if guide else None,
            "dependencies": dependencies(source), "review": export_reviews.get(item["name"]),
            "behaviorCoverage": behavior.get(item["name"]),
        })
    published = {item["entryPath"] for item in manifest["templates"]}
    templates = [{"path": path, "sourceHash": digest(ROOT / path),
                  "publication": "published" if path in published else "retained-entry",
                  "review": template_reviews.get(path)} for path in template_paths(eligibility)]
    cards = [{"path": item["path"], "group": item.get("group"),
              "sourceHash": digest(ROOT / item["path"])} for item in manifest["cards"]]
    screens = [{"path": path, "sourceHash": digest(REPO / path),
                "review": screen_reviews.get(path)} for path in screen_paths()]
    return {
        "schemaVersion": 1, "namespace": manifest["namespace"],
        "release": read_json(ROOT / "release.json")["version"],
        "basis": {path: digest(ROOT / path) for path in
                  ("_ds_manifest.json", "api/families.json", "api/template-eligibility.json")},
        "counts": {"exports": len(exports), "modules": len({x["source"] for x in exports}),
                   "cards": len(cards), "publishedTemplates": len(published),
                   "templateDirectories": len(eligibility["templates"]),
                   "templateEntries": len(templates), "browserSurfaces": len(screens)},
        "evidencePolicy": {
            "sourceReviewIsBehaviorProof": False,
            "renderObservationIsInteractionProof": False,
            "data": "synthetic", "nativeWindowsAndAssistiveTechnology": "not-claimed",
        },
        "exports": exports, "cards": cards, "templates": templates,
        "browserSurfaces": screens, "governance": governance, "browserEvidence": browser,
        "sourceEvidence": read_json(ROOT / "api/source-contract-results.json")
                          if (ROOT / "api/source-contract-results.json").is_file() else None,
        "dashboardEvidence": read_json(ROOT / "api/dashboard-validation.json")
                             if (ROOT / "api/dashboard-validation.json").is_file() else None,
    }


def validate(inventory, root=ROOT, repo=REPO):
    errors = []
    manifest = read_json(root / "_ds_manifest.json")
    families = {x["name"]: x for x in read_json(root / "api/families.json")["exports"]}
    eligibility = read_json(root / "api/template-eligibility.json")
    sources = {x["name"]: x["sourcePath"] for x in manifest["components"]}
    published = {x["entryPath"] for x in manifest["templates"]}
    if inventory.get("schemaVersion") != 1:
        errors.append("Unsupported compatibility schema")
    if inventory.get("namespace") != manifest["namespace"]:
        errors.append("Namespace differs from public manifest")
    expected_counts = {
        "exports": len(sources), "modules": len(set(sources.values())),
        "cards": len(manifest["cards"]), "publishedTemplates": len(published),
        "templateDirectories": len(eligibility["templates"]),
        "templateEntries": len(template_paths(eligibility)), "browserSurfaces": len(screen_paths(repo)),
    }
    if inventory.get("counts") != expected_counts:
        errors.append("Compatibility census differs from source contracts")
    if (root / "release.json").is_file() and inventory.get("release") != read_json(root / "release.json")["version"]:
        errors.append("Compatibility release differs from release identity")
    required_basis = {"_ds_manifest.json", "api/families.json", "api/template-eligibility.json"}
    if set(inventory.get("basis", {})) != required_basis:
        errors.append("Missing compatibility source-of-truth basis")
    for path, expected in inventory.get("basis", {}).items():
        if not (root / path).is_file() or digest(root / path) != expected:
            errors.append(f"Stale source-of-truth hash: {path}")
    for key, identity, expected in (
        ("exports", "name", {x["name"] for x in manifest["components"]}),
        ("cards", "path", {x["path"] for x in manifest["cards"]}),
        ("templates", "path", set(template_paths(eligibility))),
        ("browserSurfaces", "path", set(screen_paths(repo))),
    ):
        items = inventory.get(key, [])
        actual = [item.get(identity) for item in items]
        if len(actual) != len(set(actual)):
            errors.append(f"Duplicate {key} identities")
        if set(actual) != expected:
            errors.append(f"{key} coverage differs: missing={sorted(expected-set(actual))}, extra={sorted(set(actual)-expected)}")
        for item in items:
            path = item.get("source", item.get("path"))
            base = repo if key == "browserSurfaces" else root
            if not path or not (base / path).is_file():
                errors.append(f"Missing {key} source: {path}")
                continue
            if item.get("sourceHash") != digest(base / path):
                errors.append(f"Stale {key} source: {path}")
            if key == "cards":
                continue
            review = item.get("review")
            if not isinstance(review, dict):
                errors.append(f"Unreviewed {key}: {item.get(identity)}")
                continue
            reviewed_hash = review.get("reviewedSourceHash")
            if not reviewed_hash:
                errors.append(f"Missing reviewed-source hash: {item.get(identity)}")
            elif reviewed_hash != item["sourceHash"]:
                errors.append(f"Review predates source change: {item.get(identity)}")
            if key == "exports":
                coverage = item.get("behaviorCoverage")
                if not isinstance(coverage, dict):
                    errors.append(f"Missing per-export behavior coverage: {item['name']}")
                else:
                    if coverage.get("reviewedSourceHash") != item["sourceHash"]:
                        errors.append(f"Behavior coverage predates source change: {item['name']}")
                    contract_only = coverage.get("coverageStatus") == "declaration-contract-only"
                    stateful = coverage.get("ownsInteraction") or coverage.get("ownsStateApi") or coverage.get("ownsState")
                    if coverage.get("remainingContractRequirements") or (stateful and coverage.get("remainingInteractionStates")):
                        errors.append(f"Incomplete owned export contracts: {item['name']}")
                    if contract_only:
                        declaration = companion(base / path, ".d.ts")
                        proofs = coverage.get("contractEvidence", [])
                        valid_contract = declaration and any(
                            proof.get("kind") == "declaration-contract" and proof.get("status") == "passed"
                            and proof.get("declarationSha256") == digest(declaration)
                            and proof.get("compileSummary", {}).get("status") == "passed"
                            and proof.get("compileSummary", {}).get("exports") == len(sources)
                            and proof.get("behaviorStateCredit") is False for proof in proofs)
                        if stateful or not valid_contract:
                            errors.append(f"Declaration-only proof cannot cover owned behavior: {item['name']}")
                    elif coverage.get("coverageStatus") not in {"complete", "covered-contract-states"} or coverage.get("remainingApplicableStates"):
                        errors.append(f"Incomplete applicable-state coverage: {item['name']}")
                    cases = coverage.get("cases", [])
                    if any(case.get("status") != "passed" for case in cases) or (not contract_only and not cases):
                        errors.append(f"Missing successful export contract cases: {item['name']}")
                    references = [case.get("path") or case.get("evidence", {}).get("path") or case.get("reference") for case in cases]
                    if coverage.get("ownsInteraction") and not any(reference and reference != "tests/public-contract-types.mjs" for reference in references):
                        errors.append(f"Type compilation is not interaction proof: {item['name']}")
                    if not contract_only and set(coverage.get("applicableStates", [])) - set(coverage.get("testedStates", [])):
                        errors.append(f"Untested declared export states: {item['name']}")
                for field, suffix, review_keys in (
                    ("declaration", ".d.ts", ("reviewedDeclarationHash", "declarationHash")),
                    ("guide", ".prompt.md", ("reviewedGuidanceHash", "guideHash")),
                ):
                    expected_companion = companion(base / path, suffix)
                    if expected_companion:
                        expected_path = expected_companion.relative_to(root).as_posix()
                        if item.get(field) != expected_path:
                            errors.append(f"Companion contract differs: {item['name']} {field}")
                        reviewed = next((review.get(k) for k in review_keys if review.get(k)), None)
                        if reviewed != digest(expected_companion):
                            errors.append(f"Review predates {field} change: {item['name']}")
                if item.get("source") != sources.get(item["name"]):
                    errors.append(f"Export source differs from public manifest: {item['name']}")
                if review.get("kind") not in KINDS:
                    errors.append(f"Missing export kind: {item['name']}")
                if review.get("decision") not in DECISIONS or not review.get("rationale"):
                    errors.append(f"Missing adoption decision/rationale: {item['name']}")
                if not isinstance(review.get("applicableStates"), list):
                    errors.append(f"Missing state applicability: {item['name']}")
                f = families[item["name"]]
                if (item.get("family"), item.get("maturity")) != (f["family"], f["stability"]):
                    errors.append(f"Family/maturity mismatch: {item['name']}")
                for dependency in item.get("dependencies", []):
                    if dependency["kind"] == "source" and not dependency.get("path"):
                        errors.append(f"Unresolved import in {item['name']}: {dependency['specifier']}")
                if review.get("decision") == "adopt" and any(x.get("status") == "open" for x in review.get("issues", [])):
                    errors.append(f"Cannot adopt component with open defects: {item['name']}")
            elif key == "templates":
                publication = "published" if path in published else "retained-entry"
                if item.get("publication") != publication:
                    errors.append(f"Template publication differs from manifest: {path}")
                for field in ("kind", "maturity", "classification", "applicableStates", "decision", "rationale", "dependencies"):
                    if not review.get(field):
                        errors.append(f"Missing template review contract {field}: {path}")
                for dependency in review.get("dependencies", {}).get("reviewedSourceDependencies", []):
                    target = root / dependency["path"]
                    if not target.is_file() or dependency.get("sha256") != digest(target):
                        errors.append(f"Template review predates dependency change: {path}: {dependency['path']}")
            elif not review.get("decision") or not review.get("rationale"):
                errors.append(f"Missing browser adoption decision: {path}")
            evidence = review.get("evidence", [])
            for case in evidence:
                if case.get("kind") == "source-review" and case.get("status") == "passed":
                    errors.append(f"Source review mislabeled as behavioral pass: {item.get(identity)}")
    errors.extend(validate_browser(inventory.get("browserEvidence"), manifest, eligibility, root))
    errors.extend(validate_source_evidence(inventory.get("sourceEvidence"), root))
    errors.extend(validate_dashboard_evidence(inventory.get("dashboardEvidence"), repo))
    return errors


def validate_dashboard_evidence(evidence, repo):
    source = repo / "src/Meridian.Ui/dashboard/src"
    expected = {p.relative_to(repo).as_posix() for p in source.rglob("*") if p.is_file()
                and re.search(r"\.(?:test|spec)\.tsx?$", p.name)}
    if not expected and not screen_paths(repo):
        return []  # Isolated validator unit fixture, without an application checkout.
    if not isinstance(evidence, dict):
        return ["Missing full dashboard evidence"]
    errors = []
    results = evidence.get("results", [])
    if evidence.get("counts") != {"files": len(results), "passed": sum(x.get("passed", 0) for x in results), "failed": 0, "skipped": 0}:
        errors.append("Dashboard result census differs")
    if {x.get("path") for x in results} != expected or len(results) != len(expected):
        errors.append("Full dashboard test-file coverage differs")
    for result in results:
        path = repo / result["path"]
        if not path.is_file() or result.get("sha256") != digest(path):
            errors.append(f"Stale dashboard test evidence: {result['path']}")
        if result.get("status") != "passed" or result.get("failed") != 0 or result.get("skipped") != 0 or not result.get("passed"):
            errors.append(f"Incomplete dashboard test evidence: {result['path']}")
    hashes = evidence.get("sourceHashes", {})
    expected_sources = {p.relative_to(repo).as_posix() for p in source.rglob("*")
                        if p.is_file() and p.suffix in {".ts", ".tsx", ".css"}}
    if set(hashes) != expected_sources:
        errors.append("Dashboard source evidence coverage differs")
    for path, expected_hash in hashes.items():
        if not (repo / path).is_file() or digest(repo / path) != expected_hash:
            errors.append(f"Stale dashboard source evidence: {path}")
    maintained = evidence.get("maintainedBrowser", {})
    if (maintained.get("result") != "PASS" or maintained.get("accessibilityViolations") != 0
            or any(maintained.get(key) for key in ("exceptions", "assetFailures", "unexpectedConsoleErrors"))):
        errors.append("Missing or failed maintained workstation browser proof")
    primary = evidence.get("primaryAdoptionBrowser", {})
    summary = primary.get("summary", {})
    if (summary.get("cases", 0) < 24 or summary.get("axeScans", 0) < 24
            or any(summary.get(key, -1) != 0 for key in ("failed", "axeViolations", "exceptions", "networkFailures"))
            or any(case.get("status") != "passed" for case in primary.get("cases", []))
            or primary.get("layoutFailures")):
        errors.append("Missing or failed primary adoption browser proof")
    for record in primary.get("reviewedSources", []):
        path = repo / record["path"]
        if not path.is_file() or record.get("reviewedSourceHash") != digest(path):
            errors.append(f"Stale primary adoption browser proof: {record['path']}")
    return errors


def validate_source_evidence(evidence, root):
    if not isinstance(evidence, dict):
        return ["Missing executed source-contract evidence"]
    errors = []
    if evidence.get("status") != "passed" or evidence.get("sourceStable") is not True or evidence.get("changedSources"):
        errors.append("Source contracts failed or changed during execution")
    expected = {p.relative_to(root).as_posix() for p in (root / "tests").glob("*-regression.mjs")}
    expected |= {f"tests/{name}" for name in ("public-contract-types.mjs", "token-defaults.test.mjs")}
    suites = evidence.get("suites", [])
    if {x.get("path") for x in suites} != expected or len(suites) != len(expected):
        errors.append("Source-contract suite coverage differs")
    if any(x.get("status") != "passed" or x.get("exitCode") != 0 for x in suites):
        errors.append("Failed source-contract suite")
    hashes = evidence.get("sourceHashes", {})
    expected_sources = {p.relative_to(root).as_posix() for p in source_evidence_paths(root)}
    if set(hashes) != expected_sources:
        errors.append("Source-contract dependency coverage differs")
    for path, expected_hash in hashes.items():
        if not (root / path).is_file() or digest(root / path) != expected_hash:
            errors.append(f"Stale source-contract dependency: {path}")
    return errors


def source_evidence_paths(root):
    outputs = {"api/source-contract-results.json", "api/project-compatibility.json", "api/dashboard-validation.json"}
    paths = {p for directory in ("components", "tests", "scripts", "templates", "api")
             for p in (root / directory).rglob("*") if p.is_file()
             and p.suffix in {".js", ".jsx", ".ts", ".mjs", ".html", ".css", ".json", ".md", ".py", ".sh"}
             and p.relative_to(root).as_posix() not in outputs}
    paths |= {root / name for name in ("package.json", "package-lock.json", "_ds_manifest.json", "_ds_bundle.js", "_preview_runtime.js", "release.json", "release-build.json", "styles.css") if (root / name).is_file()}
    return paths


def validate_browser(browser, manifest, eligibility, root):
    """Partial, stale, or failed observations cannot certify the complete audit."""
    errors = []
    if not isinstance(browser, dict):
        return ["Missing executed browser evidence"]
    source = browser.get("source", {})
    integrity = browser.get("integrity", {}).get("cases", [])
    if len(integrity) < 4 or any(case.get("status") != "passed" for case in integrity):
        errors.append("Missing or failed browser integrity/zoom controls")
    zoom_control = root / "tests/desktop-zoom-control.html"
    if zoom_control.is_file() and source.get("zoomControlSha256") != digest(zoom_control):
        errors.append("Stale browser zoom control")
    run = source.get("run", {})
    if run.get("kind") != "all" or run.get("matrix") != "full" or run.get("only") or run.get("limit"):
        errors.append("Filtered browser runs cannot certify full compatibility")
    suffixes = {".js", ".jsx", ".html", ".css", ".svg", ".woff2", ".woff", ".ttf",
                ".png", ".jpg", ".jpeg", ".webp", ".avif", ".gif", ".ico", ".thumbnail"}
    expected_runtime = {p.relative_to(root).as_posix() for directory in
                        ("components", "tokens", "templates", "docs", "guidelines", "assets")
                        for p in (root / directory).rglob("*")
                        if p.is_file() and (p.suffix.lower() in suffixes or p.name == ".thumbnail")}
    expected_runtime |= {path for path in ("styles.css", "_preview_runtime.js") if (root / path).is_file()}
    runtime = source.get("runtimeSourceHashes", {})
    if set(runtime) != expected_runtime:
        errors.append("Browser runtime dependency coverage differs")
    for path, expected in runtime.items():
        if not (root / path).is_file() or digest(root / path) != expected:
            errors.append(f"Stale browser runtime dependency: {path}")
    if source.get("runtimeSourceHashesStable") is not True or source.get("changedRuntimeSources"):
        errors.append("Browser sources changed during execution")
    for field, path in (("manifestSha256", "_ds_manifest.json"), ("bundleSha256", "_ds_bundle.js")):
        if not (root / path).is_file() or source.get(field) != digest(root / path):
            errors.append(f"Stale browser evidence: {path}")
    expected_groups = {
        "cards": {x["path"] for x in manifest["cards"]},
        "templates": set(template_paths(eligibility)),
        "exports": {x["name"] for x in manifest["components"]},
        "harnesses": {f"tests/{name}.html" for name in
                      ("unit", "semantic-safety", "books-loop", "action-contract", "conformance", "a11y-run")},
    }
    for group, expected in expected_groups.items():
        actual = browser.get(group, {})
        if set(actual) != expected:
            errors.append(f"Browser {group} coverage differs: missing={sorted(expected-set(actual))}, extra={sorted(set(actual)-expected)}")
        for identity, record in actual.items():
            if group != "exports":
                path = root / identity
                if not path.is_file() or record.get("sourceSha256") != digest(path):
                    errors.append(f"Stale browser {group} evidence: {identity}")
            if not record.get("cases"):
                errors.append(f"Missing executed browser cases: {identity}")
            for case in record.get("cases", []):
                if case.get("status") not in {"passed", "blocked"}:
                    errors.append(f"Failed browser case: {identity}: {case.get('name')}")
                if case.get("status") == "blocked" and not case.get("detail"):
                    errors.append(f"Unexplained blocked browser case: {identity}")
            if group == "templates":
                journey = record.get("primaryJourney", {})
                if journey.get("status") != "passed" or not journey.get("totalSteps") or journey.get("completedSteps") != journey.get("totalSteps"):
                    errors.append(f"Incomplete primary template journey: {identity}")
                recipe = root / "tests/template-journeys.json"
                if not recipe.is_file() or journey.get("recipeFileSha256") != digest(recipe):
                    errors.append(f"Stale template journey recipe: {identity}")
    themes = browser.get("themes", [])
    expected_themes = {x["selector"] for x in manifest.get("themes", [])}
    if {x.get("selector") for x in themes} != expected_themes or len(themes) != len(expected_themes):
        errors.append("Browser theme coverage differs from manifest")
    if any(x.get("status") != "passed" or x.get("violations") for x in themes):
        errors.append("Failed browser theme contract")
    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true")
    parser.add_argument("--reviews-dir", type=Path)
    parser.add_argument("--browser-results", type=Path)
    parser.add_argument("--inventory", type=Path, default=INVENTORY)
    args = parser.parse_args()
    if args.write:
        if not args.reviews_dir:
            parser.error("--write requires --reviews-dir; reviews are never invented")
        value = assemble(args.reviews_dir, args.browser_results)
        args.inventory.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    elif not args.inventory.exists():
        parser.error("Compatibility inventory is missing; assemble real review records first")
    value = read_json(args.inventory)
    errors = validate(value)
    for error in errors:
        print("FAIL", error)
    print(json.dumps({"counts": value.get("counts"), "errors": len(errors)}, sort_keys=True))
    return int(bool(errors))


if __name__ == "__main__":
    raise SystemExit(main())
