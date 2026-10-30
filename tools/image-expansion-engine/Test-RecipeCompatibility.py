"""Check exact historical AVIF/SVG recipe retention without builds or staging."""
import hashlib
import importlib.util
from pathlib import Path
import unittest

TOOLS = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("image_stage", TOOLS / "Stage-ImageExpansionPayload.py")
STAGE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(STAGE)


class RecipeCompatibility(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.cases = []
        for name, expected, old, new in (
            ("avif-engine/Build-AvifEngine.py", "C3043FF4E53AC12F8F76551855CD4CC7211E1E3D40437F47959D5357163C5501",
             b'BRIDGE = ROOT / "proprietary/src/ContextSuite.Avif.Native"', b'BRIDGE = ROOT / "src/ContextSuite.Avif.Native"'),
            ("svg-engine/Build-SvgEngine.ps1", "CAFFFB6C4429E6430FD2E8D7E2ED1AE5BFF64C5DC8818C0422FDF770161A45A4",
             b"$privateSource = Join-Path $repositoryRoot 'proprietary/src/ContextSuite.Svg.Native'",
             b"$privateSource = Join-Path $repositoryRoot 'src/ContextSuite.Svg.Native'"),
        ):
            current = (STAGE.TOOLS / name).read_bytes()
            if current.count(new) != 1:
                raise AssertionError("Expected one source relocation")
            archived = current.replace(new, old, 1)
            if hashlib.sha256(archived).hexdigest().upper() != expected:
                raise AssertionError("Recipe no longer maps exactly to the independently pinned historical bytes")
            cls.cases.append((name, current, archived, expected))

    def test_exact_current_recipes(self):
        for name, current, archived, expected in self.cases:
            with self.subTest(name=name):
                self.assertEqual(STAGE.recipe_bytes(name, current, hashlib.sha256(current).hexdigest().upper()), current)

    def test_returns_exact_historical_packaged_recipe(self):
        for name, current, archived, expected in self.cases:
            with self.subTest(name=name):
                self.assertEqual(STAGE.recipe_bytes(name, current, expected), archived)

    def test_changed_current_body_rejected(self):
        for name, current, archived, expected in self.cases:
            with self.subTest(name=name), self.assertRaises(ValueError):
                STAGE.recipe_bytes(name, current + b"# change\n", expected)

    def test_unreviewed_archived_hash_rejected(self):
        for name, current, archived, expected in self.cases:
            changed = hashlib.sha256(archived + b"# change\n").hexdigest().upper()
            with self.subTest(name=name), self.assertRaises(ValueError):
                STAGE.recipe_bytes(name, current, changed)

    def test_exception_does_not_apply_to_other_recipe(self):
        for name, current, archived, expected in self.cases:
            with self.subTest(name=name), self.assertRaises(ValueError):
                STAGE.recipe_bytes("other/" + name, current, expected)

    def test_duplicated_assignment_rejected(self):
        for name, current, archived, expected in self.cases:
            with self.subTest(name=name), self.assertRaises(ValueError):
                STAGE.recipe_bytes(name, current + current, expected)

    def test_arbitrary_recipe_stays_exact(self):
        current = b"ordinary recipe"
        expected = hashlib.sha256(current).hexdigest().upper()
        self.assertEqual(STAGE.recipe_bytes("other.py", current, expected), current)
        with self.assertRaises(ValueError):
            STAGE.recipe_bytes("other.py", current + b"changed", expected)


if __name__ == "__main__":
    unittest.main()
