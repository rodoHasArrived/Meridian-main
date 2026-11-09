#!/usr/bin/env python3
"""Guard: no endpoint trusts a body-supplied ActionOrigin.

OperationsActionOriginDto decides whether the "reviewed automation may not perform this action; a
human operator is required" governance control applies. It is an ordinary bound member of the
request DTOs and defaults to the permissive HumanOperator, so before #2673 a caller satisfied the
gate simply by omitting the field: the control stopped only automation that declared itself
honestly. Several endpoints did derive it server-side, but by asserting the constant HumanOperator,
which stamped an API-key caller as a human and satisfied the same gate.

The fix is not to overwrite the body outright everywhere. On the governance-gated material commands
automation that declares itself honestly must still be refused -- that is the capability the control
exists for -- so ResolveTrustedActionOrigin takes the *narrower* of the declaration and the
principal's standing, and a call site there passes the declared value in.

The reconciliation break casework routes are the deliberate exception: their bodies are legacy
browser-supplied input the server is authoritative over, and two endpoint tests pin that the
declared origin is discarded there along with the declared actor. Those call
DeriveActionOriginFromPrincipal. Which family a route belongs to is a trust decision about that
route, not something inferable from the handler's shape -- most governed endpoints re-derive Actor
too. Either way the origin comes from the principal when it matters, so neither form can let a
non-interactive credential reach HumanOperator, which is what #2673 required.

The endpoints already knew the pattern -- they re-derive Actor, TenantId and CompanyId from the
authenticated principal in a `request with { ... }` block -- they just did not apply it to this one
field. So this fails CI when a route handler binds a DTO carrying ActionOrigin without the value
being re-derived through EndpointAuthorization.ResolveTrustedActionOrigin.

The DTO set is discovered from Meridian.Contracts rather than hard-coded, so a new request record
carrying ActionOrigin is in scope from the moment it is declared, and a route binding it fails until
it re-derives. Handlers that forward the bound request to a helper which re-derives are covered by
naming that helper in DERIVING_HELPERS.

Binding is not the only way a handler gets a DTO. A handler that binds the body as a JsonElement and
calls `JsonSerializer.Deserialize<TDto>`, `ReadFromJsonAsync<TDto>` or a local `TryDeserialize<TDto>`
helper has the same body-supplied origin in hand, but a parameter-only scan never sees it. That is
how the banking approve/reject/remediate/bank-evidence handlers passed this guard while handing the
default HumanOperator origin to a human-only gate, letting an API-key principal approve payments
(#3405). Manual deserialization of a governed DTO is therefore checked too, against the enclosing
handler only: the window runs from the nearest route mapping or method declaration before the call to
the next one after it, so a neighbouring handler's derivation cannot cover it.
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
CONTRACTS_ROOT = REPO_ROOT / "src" / "Meridian.Contracts"
ENDPOINTS_ROOT = REPO_ROOT / "src" / "Meridian.Ui.Shared" / "Endpoints"

TRUSTED_RESOLVER = "ResolveTrustedActionOrigin"

# The reconciliation casework adapters are authoritative over the caller's identity -- they replace
# Actor/ResolvedBy with the principal -- so they discard the declared origin outright rather than
# narrowing against it. Both entry points derive the same way, so neither can let a non-interactive
# principal reach HumanOperator; they differ only in whether a declared non-human origin is believed.
PRINCIPAL_RESOLVER = "DeriveActionOriginFromPrincipal"

# Helpers that re-derive the origin themselves, so a handler forwarding a bound request to one of
# them is covered. Each must call the resolver in its own body -- asserted by a test.
DERIVING_HELPERS = (
    "WithAccessContext",
    "ApplyReconciliationCaseworkEndpointAsync",
    "ApplyReconciliationBulkEndpointAsync",
)

# How far past the binding to look for the re-derivation. Handlers are short; a binding whose
# derivation is further away than this is worth restructuring rather than exempting.
HANDLER_WINDOW_LINES = 60

# Calls that materialize a DTO from the request body by hand rather than through parameter binding.
MANUAL_DESERIALIZERS = (
    r"\bDeserialize",  # JsonSerializer.Deserialize and any other Deserialize<T> call
    r"\bReadFromJsonAsync",
    r"\bTryDeserialize\w*",
)

# Where one handler ends and the next begins, for bounding a manual deserialization's window.
HANDLER_BOUNDARY = re.compile(
    r"\bMap(?:Get|Post|Put|Patch|Delete|Methods)\s*\("
    r"|^[ \t]*(?:private|internal|public|protected)\b[^\n;=]*\(",
    re.MULTILINE,
)

RECORD_PATTERN = re.compile(r"record\s+(?P<name>\w+)\s*\(")
EXCLUDED_DIRECTORY_NAMES = {"bin", "node_modules", "obj"}


def _iter_sources(root: Path) -> list[Path]:
    paths: list[Path] = []
    for current_root, directories, files in os.walk(root, topdown=True, followlinks=False):
        directories[:] = sorted(d for d in directories if d.lower() not in EXCLUDED_DIRECTORY_NAMES)
        paths.extend(Path(current_root) / f for f in files if f.lower().endswith(".cs"))
    return sorted(paths)


def action_origin_dtos(contracts_root: Path) -> set[str]:
    """Record types whose primary constructor declares an ActionOrigin member."""
    found: set[str] = set()
    for path in _iter_sources(contracts_root):
        text = path.read_text(encoding="utf-8", errors="replace")
        if "ActionOrigin" not in text:
            continue
        for match in RECORD_PATTERN.finditer(text):
            depth, index = 1, match.end()
            while index < len(text) and depth:
                if text[index] == "(":
                    depth += 1
                elif text[index] == ")":
                    depth -= 1
                index += 1
            if "ActionOrigin" in text[match.end() : index]:
                found.add(match.group("name"))
    return found


def find_untrusted_bindings(
    contracts_root: Path,
    endpoints_root: Path,
) -> dict[str, list[tuple[int, str]]]:
    """Maps repo-relative endpoint path -> [(line, dto type)] binding an untrusted ActionOrigin."""
    dtos = action_origin_dtos(contracts_root)
    if not dtos:
        return {}

    binding = re.compile(r"\b(" + "|".join(sorted(dtos)) + r")\??\s+\w+\s*[,)]")
    covered = (TRUSTED_RESOLVER, PRINCIPAL_RESOLVER) + DERIVING_HELPERS
    manual = re.compile(
        r"(?:" + "|".join(MANUAL_DESERIALIZERS) + r")\s*<\s*(?:global::)?(?:\w+\.)*"
        r"(?P<dto>" + "|".join(sorted(dtos)) + r")\s*\??\s*>"
    )
    repo_root = endpoints_root.parents[2]

    violations: dict[str, list[tuple[int, str]]] = {}
    for path in _iter_sources(endpoints_root):
        text = path.read_text(encoding="utf-8", errors="replace")
        lines = text.split("\n")
        for match in binding.finditer(text):
            line_index = text[: match.start()].count("\n")
            # A helper declares its parameters too; only a route handler binds from the body, and a
            # handler always has an HttpContext to derive from. Without one there is nothing to
            # derive, and the caller that does have one is checked on its own line.
            window = "\n".join(lines[line_index : line_index + HANDLER_WINDOW_LINES])
            preamble = "\n".join(lines[max(0, line_index - 6) : line_index + 8])
            if "HttpContext" not in preamble:
                continue
            if any(marker in window for marker in covered):
                continue
            rel = path.relative_to(repo_root).as_posix()
            violations.setdefault(rel, []).append((line_index + 1, match.group(1)))

        for match in manual.finditer(text):
            if any(marker in _enclosing_handler(text, match.start()) for marker in covered):
                continue
            rel = path.relative_to(repo_root).as_posix()
            line = text[: match.start()].count("\n") + 1
            violations.setdefault(rel, []).append((line, match.group("dto")))

    for sites in violations.values():
        sites.sort()
    return violations


def _enclosing_handler(text: str, position: int) -> str:
    """Text of the route handler or method around `position`, bounded by its neighbours."""
    start, end = 0, len(text)
    for boundary in HANDLER_BOUNDARY.finditer(text):
        if boundary.start() <= position:
            start = boundary.start()
        else:
            end = boundary.start()
            break
    return text[start:end]


def main() -> int:
    parser = argparse.ArgumentParser(description="Enforce server-derived ActionOrigin at endpoints.")
    parser.add_argument("--contracts-root", default=str(CONTRACTS_ROOT))
    parser.add_argument("--endpoints-root", default=str(ENDPOINTS_ROOT))
    args = parser.parse_args()

    violations = find_untrusted_bindings(Path(args.contracts_root), Path(args.endpoints_root))
    if violations:
        print("ActionOrigin derivation guard FAILED.", file=sys.stderr)
        print(
            "These route handlers bind or deserialize (JsonSerializer.Deserialize, ReadFromJsonAsync, "
            "TryDeserialize) a request DTO carrying ActionOrigin without re-deriving it "
            "from the authenticated principal, so the human-operator governance gate is whatever "
            "the caller's request body claimed (#2673):",
            file=sys.stderr,
        )
        for rel, sites in sorted(violations.items()):
            for line, dto in sites:
                print(f"  {rel}:{line}  {dto}", file=sys.stderr)
        print(
            f"\nSet `ActionOrigin = EndpointAuthorization.{TRUSTED_RESOLVER}(context, "
            "request.ActionOrigin)` in the same `request with { ... }` block that re-derives Actor "
            "and tenant scope. Pass the declared value in: the resolver returns the narrower of it "
            "and the principal's standing, so automation that declares itself is still refused.\n"
            f"\nThe reconciliation break casework routes use `{PRINCIPAL_RESOLVER}(context)` "
            "instead, which discards the declaration entirely. That is a per-route trust decision, "
            "not something to infer from the shape of the handler -- most governed endpoints also "
            "re-derive Actor, so overwriting the actor does not make a route one of them. Default "
            f"to `{TRUSTED_RESOLVER}` unless you are adding to that casework family.",
            file=sys.stderr,
        )
        return 1

    print(
        "ActionOrigin derivation guard: every endpoint binding or manual deserialization re-derives "
        "the origin server-side."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
