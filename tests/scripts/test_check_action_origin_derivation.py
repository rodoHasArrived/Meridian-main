from __future__ import annotations

import importlib.util
import re
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = (
    Path(__file__).resolve().parents[2]
    / "build"
    / "scripts"
    / "ci"
    / "check-action-origin-derivation.py"
)
SPEC = importlib.util.spec_from_file_location("check_action_origin_derivation", SCRIPT_PATH)
assert SPEC and SPEC.loader
guard = importlib.util.module_from_spec(SPEC)
sys.modules["check_action_origin_derivation"] = guard
SPEC.loader.exec_module(guard)

REPO_ROOT = Path(__file__).resolve().parents[2]

CONTRACT = """
namespace Meridian.Contracts.Workstation;
public sealed record CloseThingRequest(
    string Actor,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);
public sealed record UnrelatedRequest(string Actor);
"""


def scan(endpoint_files: dict[str, str], contracts: str = CONTRACT) -> dict:
    """Run the guard over a throwaway tree with these contracts and endpoint files."""
    with tempfile.TemporaryDirectory() as temp_dir:
        root = Path(temp_dir)
        contracts_root = root / "src" / "Meridian.Contracts"
        endpoints_root = root / "src" / "Meridian.Ui.Shared" / "Endpoints"
        contracts_root.mkdir(parents=True)
        endpoints_root.mkdir(parents=True)
        (contracts_root / "Dtos.cs").write_text(contracts, encoding="utf-8")
        for rel, content in endpoint_files.items():
            path = endpoints_root / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
        return guard.find_untrusted_bindings(contracts_root, endpoints_root)


class CheckActionOriginDerivationTests(unittest.TestCase):
    def test_discovers_dto_types_from_contracts(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            root.mkdir(exist_ok=True)
            (root / "Dtos.cs").write_text(CONTRACT, encoding="utf-8")

            self.assertEqual(guard.action_origin_dtos(root), {"CloseThingRequest"})

    def test_binding_without_derivation_is_flagged(self) -> None:
        found = scan(
            {
                "ThingEndpoints.cs": """
app.MapPost("/thing", async (CloseThingRequest request, HttpContext context) =>
{
    var trustedRequest = request with { Actor = ResolveMutationActor(context, request.Actor) };
    return await service.CloseAsync(trustedRequest, context.RequestAborted);
});
"""
            }
        )

        self.assertEqual(found, {"src/Meridian.Ui.Shared/Endpoints/ThingEndpoints.cs": [(2, "CloseThingRequest")]})

    def test_binding_with_derivation_is_not_flagged(self) -> None:
        found = scan(
            {
                "ThingEndpoints.cs": """
app.MapPost("/thing", async (CloseThingRequest request, HttpContext context) =>
{
    var trustedRequest = request with
    {
        Actor = ResolveMutationActor(context, request.Actor),
        ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin)
    };
    return await service.CloseAsync(trustedRequest, context.RequestAborted);
});
"""
            }
        )

        self.assertEqual(found, {})

    def test_binding_that_derives_from_the_principal_alone_is_not_flagged(self) -> None:
        """The casework adapters discard the declaration outright, because they are authoritative
        over the caller's identity. That is a derivation too, so it must not be flagged."""
        found = scan(
            {
                "ThingEndpoints.cs": """
app.MapPost("/thing", async (CloseThingRequest request, HttpContext context) =>
{
    var trustedRequest = request with
    {
        Actor = ResolveMutationActor(context, request.Actor),
        ActionOrigin = EndpointAuthorization.DeriveActionOriginFromPrincipal(context)
    };
    return await service.CloseAsync(trustedRequest, context.RequestAborted);
});
"""
            }
        )

        self.assertEqual(found, {})

    def test_forwarding_to_a_deriving_helper_is_not_flagged(self) -> None:
        found = scan(
            {
                "ThingEndpoints.cs": """
app.MapPost("/thing", async (CloseThingRequest request, HttpContext context) =>
    await ApplyReconciliationCaseworkEndpointAsync(request, context, jsonOptions));
"""
            }
        )

        self.assertEqual(found, {})

    def test_a_dto_without_action_origin_is_out_of_scope(self) -> None:
        found = scan(
            {
                "ThingEndpoints.cs": """
app.MapPost("/thing", async (UnrelatedRequest request, HttpContext context) =>
{
    return await service.DoAsync(request, context.RequestAborted);
});
"""
            }
        )

        self.assertEqual(found, {})

    def test_helper_signature_without_an_http_context_is_not_flagged(self) -> None:
        """A private helper declares the same parameter but binds nothing and has nothing to
        derive from; the route handler that calls it is checked on its own line."""
        found = scan(
            {
                "ThingEndpoints.cs": """
private static async Task<Result> BuildCommandAsync(
    IRepository repository,
    CloseThingRequest request,
    CancellationToken ct)
{
    return new Result(request.ActionOrigin);
}
"""
            }
        )

        self.assertEqual(found, {})

    def test_a_newly_declared_dto_is_in_scope_without_editing_the_guard(self) -> None:
        """The DTO set is discovered, not hard-coded, so a new governed request record is covered
        the moment it is declared."""
        contracts = CONTRACT + """
public sealed record BrandNewGovernedRequest(
    string Actor,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);
"""
        found = scan(
            {
                "NewEndpoints.cs": """
app.MapPost("/new", async (BrandNewGovernedRequest request, HttpContext context) =>
{
    return await service.DoAsync(request with { Actor = "x" }, context.RequestAborted);
});
"""
            },
            contracts=contracts,
        )

        self.assertEqual(found, {"src/Meridian.Ui.Shared/Endpoints/NewEndpoints.cs": [(2, "BrandNewGovernedRequest")]})

    def test_declared_deriving_helpers_really_derive(self) -> None:
        """DERIVING_HELPERS exempts a handler that forwards to one of these, so each must actually
        derive. A helper that stopped deriving would silently exempt its callers.

        The check is against the file that *declares* the helper, not any file mentioning it: a
        caller's own unrelated derivation would otherwise satisfy this vacuously, which is exactly
        what an earlier version of this test did."""
        endpoints = REPO_ROOT / "src" / "Meridian.Ui.Shared" / "Endpoints"
        sources = {p: p.read_text(encoding="utf-8", errors="replace") for p in endpoints.rglob("*.cs")}
        resolvers = (guard.TRUSTED_RESOLVER, guard.PRINCIPAL_RESOLVER)

        for helper in guard.DERIVING_HELPERS:
            with self.subTest(helper=helper):
                declaration = re.compile(
                    r"^[ \t]*(?:private|internal|public|protected)\b[^\n(]*?\b"
                    + re.escape(helper)
                    + r"\s*(?:<[^>(]*>)?\s*\(",
                    re.MULTILINE,
                )
                declaring = [path for path, text in sources.items() if declaration.search(text)]

                self.assertTrue(declaring, f"{helper} is declared nowhere in the endpoint surface")
                for path in declaring:
                    self.assertTrue(
                        any(resolver in sources[path] for resolver in resolvers),
                        f"{helper} is exempted as a deriving helper, but {path.name} declares it "
                        f"without calling either of {resolvers}",
                    )

    def test_repository_endpoints_derive_every_action_origin(self) -> None:
        """The live invariant: no endpoint in this repo trusts a body-supplied ActionOrigin."""
        found = guard.find_untrusted_bindings(
            REPO_ROOT / "src" / "Meridian.Contracts",
            REPO_ROOT / "src" / "Meridian.Ui.Shared" / "Endpoints",
        )

        self.assertEqual(found, {})


if __name__ == "__main__":
    unittest.main()
