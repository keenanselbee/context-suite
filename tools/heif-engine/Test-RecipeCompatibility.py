"""Verify the exact HEIF source-recipe compatibility exception without build inputs."""
import importlib.util
from pathlib import Path
import unittest


TOOLS = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("heif_distribution", TOOLS / "Review-HeifDistribution.py")
DISTRIBUTION = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(DISTRIBUTION)


class RecipeCompatibility(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.current = (TOOLS / "Build-HeifEngine.py").read_bytes()
        cls.old_line = b'    bridge_source = ROOT / "proprietary/src/ContextSuite.Heif.Native"'
        cls.new_line = b'    bridge_source = ROOT / "src/ContextSuite.Heif.Native"'
        if cls.current.count(cls.new_line) != 1:
            raise AssertionError("Expected one current bridge assignment")
        cls.archived = cls.current.replace(cls.new_line, cls.old_line, 1)
        if DISTRIBUTION.sha(cls.archived) != "30FDE2FB752BE633096F07032A0EFF0A58CD4105C5A1B55DF7FDC5E69F1AD793":
            raise AssertionError("Current recipe no longer maps exactly to the reviewed historical recipe")

    def test_exact_current_recipe(self):
        DISTRIBUTION.verify_recipe("Build-HeifEngine.py", self.current, self.current)

    def test_other_recipe_remains_exact(self):
        DISTRIBUTION.verify_recipe("source-inputs.json", b"same", b"same")
        with self.assertRaises(ValueError):
            DISTRIBUTION.verify_recipe("source-inputs.json", b"old", b"changed")

    def test_exact_pinned_historical_relocation(self):
        DISTRIBUTION.verify_recipe("Build-HeifEngine.py", self.archived, self.current)

    def test_changed_archived_recipe(self):
        with self.assertRaises(ValueError):
            DISTRIBUTION.verify_recipe("Build-HeifEngine.py", self.archived + b"\n", self.current)

    def test_changed_current_recipe(self):
        with self.assertRaises(ValueError):
            DISTRIBUTION.verify_recipe("Build-HeifEngine.py", self.archived, self.current + b"\n")

    def test_matching_unreviewed_change_still_rejected(self):
        with self.assertRaises(ValueError):
            DISTRIBUTION.verify_recipe("Build-HeifEngine.py", self.archived + b"# changed\n", self.current + b"# changed\n")

    def test_exception_is_named_recipe_only(self):
        with self.assertRaises(ValueError):
            DISTRIBUTION.verify_recipe("other.py", self.archived, self.current)

    def test_historical_line_endings_are_identity(self):
        with self.assertRaises(ValueError):
            DISTRIBUTION.verify_recipe("Build-HeifEngine.py", self.archived.replace(b"\n", b"\r\n"), self.current)


if __name__ == "__main__":
    unittest.main()
