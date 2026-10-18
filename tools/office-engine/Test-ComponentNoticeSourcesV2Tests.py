"""Focused refusal tests for the portable Office notice verifier."""

import argparse
import importlib.util
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest
import zipfile
import sys


SCRIPT = Path(__file__).with_name("Test-ComponentNoticeSourcesV2.py")
SPEC = importlib.util.spec_from_file_location("office_notice_v2", SCRIPT)
CHECKER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CHECKER)
ARGUMENTS = argparse.ArgumentParser(add_help=False)
ARGUMENTS.add_argument("--experiment-root", type=Path)
OPTIONS, UNITTEST_ARGUMENTS = ARGUMENTS.parse_known_args()


class CompanionRefusalTests(unittest.TestCase):
    def test_unsafe_member_names_refused(self):
        for name in ("../outside", "/absolute", "C:/drive", "a//b", "a/./b", "a\\b"):
            with self.subTest(name=name):
                self.assertFalse(CHECKER.safe_name(name))

    def test_duplicate_and_unsafe_zip_members_refused(self):
        with tempfile.TemporaryDirectory(dir=SCRIPT.parents[2] / ".codex-temp") as directory:
            for names in (("component-notice-review.json", "component-notice-review.json"),
                          ("component-notice-review.json", "COMPONENT-NOTICE-REVIEW.JSON"),
                          ("component-notice-review.json", "../escape")):
                path = Path(directory) / "companion.zip"
                with zipfile.ZipFile(path, "w") as archive:
                    for name in names:
                        archive.writestr(name, b"{}")
                with self.subTest(names=names), self.assertRaisesRegex(ValueError, "Unsafe, duplicate"):
                    CHECKER.verify(path, Path(directory))

    def test_zip_record_must_match_independent_selection(self):
        with tempfile.TemporaryDirectory(dir=SCRIPT.parents[2] / ".codex-temp") as directory:
            root = Path(directory)
            selected = root / "selected.json"
            selected.write_bytes(b'{"formatVersion":2}')
            bundle = root / "companion.zip"
            with zipfile.ZipFile(bundle, "w") as archive:
                archive.writestr("component-notice-review.json", b'{"formatVersion":1}')
            with self.assertRaisesRegex(ValueError, "record differs from selected"):
                CHECKER.verify(bundle, root, record_path=selected)

    def test_source_archive_duplicate_and_traversal_refused(self):
        for names in (("root/LICENSE", "root/LICENSE"), ("root/LICENSE", "../outside")):
            stream = io.BytesIO()
            with tarfile.open(fileobj=stream, mode="w") as archive:
                for name in names:
                    member = tarfile.TarInfo(name)
                    member.size = 1
                    archive.addfile(member, io.BytesIO(b"x"))
            with self.subTest(names=names), self.assertRaisesRegex(ValueError, "Unsafe or duplicate"):
                CHECKER.validate_source_archive(stream.getvalue())

    def test_checker_extension_does_not_mutate_v1_defaults(self):
        original_spec = importlib.util.spec_from_file_location("office_notice_v1_test", CHECKER.V1)
        original = importlib.util.module_from_spec(original_spec)
        original_spec.loader.exec_module(original)
        isolated = CHECKER.source_checker()
        self.assertIn("nss/lib/sqlite/sqlite3.h", isolated.EXTRA_FILES["nss"])
        self.assertIn("sac-1.3/COPYRIGHT.html", isolated.LATIN1)
        self.assertIsNot(original.EXTRA_FILES, isolated.EXTRA_FILES)
        self.assertNotIn("nss", original.EXTRA_FILES)

    def test_python_inventory_names_refused(self):
        for name in ("program/python311.dll", "program/python27.dll", "program/python.exe",
                     "program/pythonw.exe", "program/foo.pyd",
                     "share/python-core-3.11/x", "share/pip/foo", "program/pyuno.dll"):
            with self.subTest(name=name):
                self.assertTrue(CHECKER.python_runtime_name(name))
        self.assertFalse(CHECKER.python_runtime_name("program/pythonific.dll"))

    def test_latin1_notice_round_trips_without_replacement(self):
        original = b"Copyright \xa9 example\n"
        stream = io.BytesIO()
        with tarfile.open(fileobj=stream, mode="w") as archive:
            member = tarfile.TarInfo("sac-1.3/COPYRIGHT.html")
            member.size = len(original)
            archive.addfile(member, io.BytesIO(original))
        selected = CHECKER.source_checker().source_notices(stream.getvalue(), "jfreereport_sac")
        self.assertEqual(len(selected), 1)
        self.assertEqual(selected[0][0]["encoding"], "iso-8859-1")
        self.assertEqual(selected[0][1], original)
        self.assertIn("Copyright © example".encode(), CHECKER.notices_text(selected))


class ActualRuntimeDerivationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if OPTIONS.experiment_root is None:
            raise unittest.SkipTest("Pass --experiment-root to run actual reduction evidence tests")
        cls.ROOT = OPTIONS.experiment_root
        if not cls.ROOT.is_dir():
            raise ValueError("Experiment root is missing: " + str(cls.ROOT))
        evidence = cls.ROOT / "evidence/python-free-reduction-01"
        cls.inputs = {
            "runtime/base-runtime-files.txt": (cls.ROOT / "tools/python-free-runtime-full-01/runtime-files.txt").read_bytes(),
            "runtime/runtime-files.txt": (cls.ROOT / "a/.codex-temp/python-free-reduction-01/office-engine/runtime-files.txt").read_bytes(),
            "receipts/runtime-reduction-policy.json": (evidence / "runtime-reduction-policy.json").read_bytes(),
            "receipts/runtime-derivation.json": (evidence / "results.json").read_bytes(),
            "tooling/Derive-ReducedRuntime.py": (cls.ROOT / "a/tools/office-engine/Derive-ReducedRuntime.py").read_bytes(),
        }
        cls.assembly = json.loads((cls.ROOT / "evidence/python-free-runtime-full-01.json").read_bytes())
        cls.record = {"inventorySha256": CHECKER.pin(cls.inputs["runtime/runtime-files.txt"])["sha256"],
                      "nativeFilesScope": "base-runtime",
                      "runtimeDerivation": {
                          "baseInventorySha256": CHECKER.pin(cls.inputs["runtime/base-runtime-files.txt"])["sha256"],
                          "policySha256": CHECKER.pin(cls.inputs["receipts/runtime-reduction-policy.json"])["sha256"],
                          "receiptSha256": CHECKER.pin(cls.inputs["receipts/runtime-derivation.json"])["sha256"],
                          "retainedBinaryBytesUnchanged": True}}

    def test_actual_reduction_exactly_removes_pinned_files(self):
        base, retained = CHECKER.verify_runtime_derivation(self.record, self.inputs, self.assembly)
        self.assertEqual((len(base), len(retained)), (2380, 2348))
        self.assertIn("share/config/images_colibre.zip", retained)

    def test_changed_retained_binary_identity_refused(self):
        inputs = dict(self.inputs)
        record = json.loads(json.dumps(self.record))
        lines = inputs["runtime/runtime-files.txt"].splitlines(keepends=True)
        index = next(index for index, line in enumerate(lines) if line.startswith(b"program/soffice.bin\t"))
        name, size, digest = lines[index].rstrip(b"\n").split(b"\t")
        lines[index] = b"\t".join((name, size, b"0" * 64)) + b"\n"
        inputs["runtime/runtime-files.txt"] = b"".join(lines)
        record["inventorySha256"] = CHECKER.pin(inputs["runtime/runtime-files.txt"])["sha256"]
        receipt = json.loads(inputs["receipts/runtime-derivation.json"])
        receipt["Derivation"]["derivedInventorySha256"] = record["inventorySha256"]
        inputs["receipts/runtime-derivation.json"] = json.dumps(receipt).encode()
        record["runtimeDerivation"]["receiptSha256"] = CHECKER.pin(inputs["receipts/runtime-derivation.json"])["sha256"]
        with self.assertRaisesRegex(ValueError, "exact base reduction"):
            CHECKER.verify_runtime_derivation(record, inputs, self.assembly)

    def test_extra_exclusion_or_colibre_removal_refused(self):
        for name in ("CREDITS.fodt", "share/config/images_colibre.zip"):
            record = json.loads(json.dumps(self.record))
            policy = json.loads(self.inputs["receipts/runtime-reduction-policy.json"])
            base = CHECKER.parse_inventory(self.inputs["runtime/base-runtime-files.txt"])
            if name == "CREDITS.fodt":
                policy["excludedFiles"].append({"path": name, **base[name]})
            else:
                policy["excludedFiles"][-1] = {"path": name, **base[name]}
            inputs = dict(self.inputs)
            inputs["receipts/runtime-reduction-policy.json"] = json.dumps(policy).encode()
            record["runtimeDerivation"]["policySha256"] = CHECKER.pin(
                inputs["receipts/runtime-reduction-policy.json"])["sha256"]
            receipt = json.loads(inputs["receipts/runtime-derivation.json"])
            receipt["PolicySha256"] = record["runtimeDerivation"]["policySha256"]
            receipt["Derivation"]["policySha256"] = record["runtimeDerivation"]["policySha256"]
            receipt["Derivation"]["excludedFiles"] = policy["excludedFiles"]
            receipt["Derivation"]["removedBytes"] = sum(item["bytes"] for item in policy["excludedFiles"])
            inputs["receipts/runtime-derivation.json"] = json.dumps(receipt).encode()
            record["runtimeDerivation"]["receiptSha256"] = CHECKER.pin(
                inputs["receipts/runtime-derivation.json"])["sha256"]
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, "exclusions differ"):
                CHECKER.verify_runtime_derivation(record, inputs, self.assembly)

    def test_derivation_script_identity_refused(self):
        inputs = dict(self.inputs)
        inputs["tooling/Derive-ReducedRuntime.py"] += b"\n# changed\n"
        with self.assertRaisesRegex(ValueError, "provenance differs"):
            CHECKER.verify_runtime_derivation(self.record, inputs, self.assembly)


if __name__ == "__main__":
    unittest.main(argv=[sys.argv[0], *UNITTEST_ARGUMENTS])
