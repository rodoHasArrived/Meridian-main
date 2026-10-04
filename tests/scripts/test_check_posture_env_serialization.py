from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = (
    Path(__file__).resolve().parents[2]
    / "build"
    / "scripts"
    / "ci"
    / "check-posture-env-serialization.py"
)
SPEC = importlib.util.spec_from_file_location("check_posture_env_serialization", SCRIPT_PATH)
assert SPEC and SPEC.loader
guard = importlib.util.module_from_spec(SPEC)
sys.modules["check_posture_env_serialization"] = guard
SPEC.loader.exec_module(guard)


SEQUENTIAL_COLLECTION = """
using Xunit;
namespace Meridian.Tests;
[CollectionDefinition("Sequential", DisableParallelization = true)]
public sealed class SequentialCollection { }
"""


def scan(files: dict[str, str]) -> dict[str, list[str]]:
    """Run the guard over a throwaway tests/ tree holding exactly these files."""
    with tempfile.TemporaryDirectory() as temp_dir:
        root = Path(temp_dir) / "tests"
        for rel, content in files.items():
            path = root / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
        return guard.find_unserialized_polluters(root)


class CheckPostureEnvSerializationTests(unittest.TestCase):
    def test_unserialized_direct_mutation_is_flagged(self) -> None:
        found = scan(
            {
                "TestCollections.cs": SEQUENTIAL_COLLECTION,
                "Polluter.cs": """
public sealed class PolluterTests
{
    public void Fact() => Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
}
""",
            }
        )

        self.assertEqual(found, {"tests/Polluter.cs": ["PolluterTests"]})

    def test_serialized_mutation_is_not_flagged(self) -> None:
        found = scan(
            {
                "TestCollections.cs": SEQUENTIAL_COLLECTION,
                "Polluter.cs": """
[Collection("Sequential")]
public sealed class PolluterTests
{
    public void Fact() => Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
}
""",
            }
        )

        self.assertEqual(found, {})

    def test_reading_a_posture_variable_is_not_a_mutation(self) -> None:
        found = scan(
            {
                "TestCollections.cs": SEQUENTIAL_COLLECTION,
                "Reader.cs": """
public sealed class ReaderTests
{
    public void Fact() => _ = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
}
""",
            }
        )

        self.assertEqual(found, {})

    def test_mutating_a_non_posture_variable_is_not_flagged(self) -> None:
        found = scan(
            {
                "TestCollections.cs": SEQUENTIAL_COLLECTION,
                "Other.cs": """
public sealed class OtherTests
{
    public void Fact() => Environment.SetEnvironmentVariable("POLYGON_API_KEY", "k");
}
""",
            }
        )

        self.assertEqual(found, {})

    def test_pollution_propagates_through_fixture_and_base_class(self) -> None:
        """The #2680 shape that a direct-mutation scan misses: the class names no posture
        variable at all -- it derives from a base that consumes the fixture that mutates."""
        found = scan(
            {
                "TestCollections.cs": SEQUENTIAL_COLLECTION,
                "Fixture.cs": """
public sealed class EndpointTestFixture
{
    public EndpointTestFixture() => Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Test");
}
""",
                "Base.cs": """
public abstract class EndpointIntegrationTestBase : IClassFixture<EndpointTestFixture>
{
}
""",
                "Leaf.cs": """
public sealed class EndpointReadDeclarationTests : EndpointIntegrationTestBase
{
}
""",
            }
        )

        self.assertEqual(found, {"tests/Leaf.cs": ["EndpointReadDeclarationTests"]})

    def test_fixture_and_abstract_base_are_not_themselves_flagged(self) -> None:
        """A fixture carries no collection attribute of its own, so reporting it would name a
        file where the fix cannot go. The concrete consumer is the violation site."""
        found = scan(
            {
                "TestCollections.cs": SEQUENTIAL_COLLECTION,
                "Fixture.cs": """
public sealed class EndpointTestFixture
{
    public EndpointTestFixture() => Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Test");
}
""",
                "Base.cs": """
public abstract class EndpointIntegrationTestBase : IClassFixture<EndpointTestFixture>
{
}
""",
                "Leaf.cs": """
[Collection("Sequential")]
public sealed class EndpointReadDeclarationTests : EndpointIntegrationTestBase
{
}
""",
            }
        )

        self.assertEqual(found, {})

    def test_collection_named_through_a_constant_is_resolved(self) -> None:
        found = scan(
            {
                "AlpacaCollection.cs": """
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AlpacaCredentialEnvironmentCollection
{
    public const string Name = "AlpacaCredentialEnvironment";
}
""",
                "Polluter.cs": """
[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class PolluterTests
{
    public void Fact() => Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Test");
}
""",
            }
        )

        self.assertEqual(found, {})

    def test_parallel_collection_does_not_satisfy_the_guard(self) -> None:
        """A collection is only a serialization boundary when it disables parallelization."""
        found = scan(
            {
                "Collections.cs": """
[CollectionDefinition("Grouped")]
public sealed class GroupedCollection { }
""",
                "Polluter.cs": """
[Collection("Grouped")]
public sealed class PolluterTests
{
    public void Fact() => Environment.SetEnvironmentVariable("MERIDIAN_ENVIRONMENT", "Production");
}
""",
            }
        )

        self.assertEqual(found, {"tests/Polluter.cs": ["PolluterTests"]})

    def test_doc_comment_prose_is_not_read_as_a_declaration(self) -> None:
        found = scan(
            {
                "TestCollections.cs": SEQUENTIAL_COLLECTION,
                "Fixture.cs": """
/// <summary>
/// Per-class test fixture that sets up an in-memory test server.
/// </summary>
[Collection("Sequential")]
public sealed class SomeTests
{
    public void Fact() => Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Test");
}
""",
            }
        )

        self.assertEqual(found, {})

    def test_repository_tree_has_no_unserialized_posture_mutations(self) -> None:
        """The live invariant: every posture-mutating test class in this repo is serialized."""
        tests_root = Path(__file__).resolve().parents[1]

        self.assertEqual(guard.find_unserialized_polluters(tests_root), {})


if __name__ == "__main__":
    unittest.main()
