#!/usr/bin/env python3
"""Validate the canonical adapter readiness inventory against current C# source."""

from __future__ import annotations

from adapter_readiness import REGISTRY_PATH, load_registry, validate_registry, yaml
from common import Finding, build_arg_parser, emit_findings, repo_root


def main() -> int:
    args = build_arg_parser(__doc__).parse_args()
    root = repo_root(args.root)
    try:
        errors = validate_registry(root, load_registry(root))
    except (OSError, ValueError, yaml.YAMLError) as exc:
        errors = [f"cannot read adapter registry: {exc}"]
    return emit_findings([Finding("error", REGISTRY_PATH, error) for error in errors], args.summary, "adapter readiness validation")


if __name__ == "__main__":
    raise SystemExit(main())
