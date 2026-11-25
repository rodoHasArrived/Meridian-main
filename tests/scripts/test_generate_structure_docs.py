"""Include the repository-map renderer regressions in the canonical script lane."""
import importlib.util
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "canonical_structure_doc_cases", ROOT / "build/scripts/docs/tests/test_generate_structure_docs.py")
cases = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = cases
SPEC.loader.exec_module(cases)
class GenerateStructureDocsTests(cases.GenerateStructureDocsTests):
    """Keep the canonical runner module identity while reusing the regressions."""
