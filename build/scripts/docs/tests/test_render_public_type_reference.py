"""Regression tests for source reference shape, provenance and regeneration."""
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(ROOT))
from tools.source_reference import public_types as ref


class PublicTypeReferenceTests(unittest.TestCase):
    def source(self, root, text, name="Sample.cs"):
        path = root / "src/Meridian.Example" / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
        return path

    def test_declarations_skip_comments_strings_and_preserve_shapes(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            path = self.source(root, '''namespace Example;
// public class CommentFake {}
public sealed record Actual(string Name, int Count = 2);
public class Holder {
    private string Fake = "public class StringFake {}";
    public string Label { get; private set; } = "ready";
    public int Size => 4;
    public int Run(int value = 5) { var local = new { Extra = 9 }; return value; }
    public class Nested { public bool Enabled { get; init; } }
}
''')
            declarations = ref.parse_file(root, path)
            self.assertEqual(["Actual", "Holder", "Nested"], [x.name for x in declarations])
            self.assertIn("int Count = 2", declarations[0].signature)
            members = "\n".join(x.signature for x in declarations[1].members)
            self.assertIn('public string Label { get; private set; } = "ready";', members)
            self.assertIn("public int Size => …;", members)
            self.assertIn("public int Run(int value = 5);", members)
            self.assertNotIn("local", members)
            self.assertNotIn("Nested", members)
            self.assertEqual("Holder", declarations[2].containing)

    def test_interface_members_summaries_enum_values_and_overloads(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            path = self.source(root, '''namespace Example;
/// <summary>Reads <see cref="Result"/> from <paramref name="id"/> or <see langword="null"/>.</summary>
[Example(
    "metadata")]
public interface IService {
    /// <summary>Current name.</summary>
    string Name { get; }
    int Read(string id);
    int Read(string id, int maximum = 4);
    private void Hidden() { }
}
public enum Flags { None = 0, Active = 1 << 3, Both = Active | 1 }
''')
            declarations = ref.parse_file(root, path)
            service = declarations[0]
            self.assertEqual("Reads Result from id or null.", service.summary)
            self.assertEqual(3, len(service.members))
            self.assertEqual("Current name.", service.members[0].summary)
            self.assertIn("1<<3", declarations[1].members[1].signature)
            self.assertIn("Active | 1", declarations[1].members[2].signature)

    def test_readable_boundaries_preserve_literals_properties_and_initializers(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            path = self.source(root, r'''public class Shapes {
    [JsonIgnore] public List<string> Symbols { get; init; } = [];
    public DateTimeOffset? StartedAt { get; init; }
    public string[] Names { get; set; } = ["literal >inside?value [x] public ( hi  "];
    public (int Min, int Max) Range { get; } = (1, 4);
    public int this[int index] { get { return index; } }
    public const string Value = "literal >inside?value [x] public ( hi  ";
    public static readonly int[] Values = new[] { 1, 2 };
}''')
            item = ref.parse_file(root, path)[0]
            members = "\n".join(x.signature for x in item.members)
            self.assertIn("[JsonIgnore] public List<string> Symbols", members)
            self.assertIn("public DateTimeOffset? StartedAt", members)
            self.assertIn("public string[] Names", members)
            self.assertIn("public (int Min, int Max) Range { get; } = (1, 4);", members)
            self.assertIn("public int this[int index] { get; }", members)
            self.assertIn('"literal >inside?value [x] public ( hi  "', members)
            self.assertIn("new[] { 1, 2 };", members)
            self.assertNotIn("return index", members)

    def test_nonpublic_containers_still_contribute_full_type_identity(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            path = self.source(root, """namespace Example;
internal class Outer<T> {
    private class Hidden {
        public class Nested {}
    }
}
""")
            item = ref.parse_file(root, path)[0]
            self.assertEqual("Example.Outer.Hidden.Nested", item.qualified_name)
            self.assertEqual("Outer`1.Hidden", item.containing_identity)

    def test_generic_arity_module_partials_and_containing_arity(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.source(root, '''namespace Example;
public class Result {}
public class Result<T> {}
public partial class Partial { public int First { get; } }
public class Outer { public class Nested {} }
public class Outer<T> { public class Nested {} }
''')
            self.source(root, 'namespace Example; public partial class Partial { public int Second { get; } }', "Other.cs")
            rendered = ref.render(root)
            index = rendered["README.md"]
            self.assertIn("**7** distinct", index)
            text = "\n".join(rendered.values())
            self.assertIn("generic arity 1", text)
            self.assertIn("First", text)
            self.assertIn("Second", text)

    def test_regeneration_check_detects_source_and_obsolete_pages(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            path = self.source(root, "public class Current { public int Count { get; } }")
            self.assertEqual(0, ref.main(["--root", str(root)]))
            before = {p.relative_to(root): p.read_bytes() for p in root.rglob("*.md")}
            self.assertEqual(0, ref.main(["--root", str(root), "--check"]))
            self.assertEqual(before, {p.relative_to(root): p.read_bytes() for p in root.rglob("*.md")})
            path.write_text("public class Changed {}", encoding="utf-8")
            self.assertEqual(1, ref.main(["--root", str(root), "--check"]))
            self.assertEqual(0, ref.main(["--root", str(root)]))
            obsolete = root / "docs/reference/source-api/obsolete.md"
            obsolete.write_text(ref.HEADER + "\nold", encoding="utf-8")
            self.assertEqual(1, ref.main(["--root", str(root), "--check"]))
            self.assertEqual(0, ref.main(["--root", str(root)]))
            self.assertFalse(obsolete.exists())
            obsolete.write_text("hand maintained", encoding="utf-8")
            with self.assertRaises(ValueError):
                ref.main(["--root", str(root)])


if __name__ == "__main__":
    unittest.main()
