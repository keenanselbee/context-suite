"""Exercise Office Python-mode staging guards with small disposable fixtures."""

import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch


TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("office_payload", TOOLS / "Stage-OfficePayload.py")
payload = importlib.util.module_from_spec(spec)
spec.loader.exec_module(payload)


def pin(data):
    return {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest().upper()}


class OfficePythonModeTests(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory(dir=payload.ROOT / ".codex-temp")
        self.addCleanup(self.scratch.cleanup)
        self.root = Path(self.scratch.name)
        self.source_record = self.root / "source.json"
        self.source_record.write_text(json.dumps({"pythonRuntime": "none", "sourceKitManifestSha256": "KIT"}))

    def inventory(self, names):
        engine = self.root / "engine"
        engine.mkdir(exist_ok=True)
        rows = [f"{name}\t1\t{pin(b'x')['sha256']}" for name in sorted(names)]
        data = ("\n".join(rows) + "\n").encode()
        (engine / "runtime-files.txt").write_bytes(data)
        record = {"host": {"path": "host.exe", **pin(b'h')},
                  "inventory": {"path": "runtime-files.txt", **pin(data),
                                "files": len(names), "runtimeBytes": len(names)},
                  "hostDependencies": [], "emptyDirectories": []}
        return engine, record

    def test_unknown_mode_refused_and_legacy_mode_is_bundled(self):
        self.assertEqual(payload.python_runtime({}), "bundled")
        self.assertEqual(payload.python_runtime({"pythonRuntime": "bundled"}), "bundled")
        for mode in ("None", "disabled", None, 1):
            with self.subTest(mode=mode), self.assertRaisesRegex(ValueError, "Unsupported"):
                payload.python_runtime({"pythonRuntime": mode})
        with patch.object(payload, "SOURCE_RECORD", self.source_record):
            self.source_record.write_text(json.dumps({"pythonRuntime": "disabled"}))
            with self.assertRaisesRegex(ValueError, "Unsupported"):
                payload.selection()

    def test_python_artifacts_refused_from_pinned_inventory(self):
        for name in ("program/python-core-3.12.14/lib/os.py", "program/pyuno.dll",
                     "program/pythonloader.uno.dll", "program/python.exe", "program/pythonw.exe",
                     "program/python27.dll", "program/python312.dll",
                     "program/module.pyd", "program/pip/_vendor/file.py",
                     "program/setuptools/file.py", "program/pkg_resources/file.py"):
            with self.subTest(name=name):
                engine, record = self.inventory([name])
                with patch.object(payload, "SOURCE_RECORD", self.source_record):
                    with self.assertRaisesRegex(ValueError, "Python runtime file remains"):
                        payload.membership(engine, record)
                (engine / "runtime-files.txt").unlink()

    def test_python_service_loader_and_component_refused(self):
        engine = self.root / "engine"
        registry = engine / "runtime/program/services/services.rdb"
        registry.parent.mkdir(parents=True)
        files = {"runtime/program/services/services.rdb": {}}
        for loader, uri, name in (("com.sun.star.loader.Python", "file.dll", "normal"),
                                  ("com.sun.star.loader.SharedLibrary", "pyuno.dll", "normal"),
                                  ("com.sun.star.loader.SharedLibrary", "file.dll", "PythonThing")):
            with self.subTest(loader=loader, uri=uri, name=name):
                registry.write_text('<components xmlns="http://openoffice.org/2010/uno-components">'
                                    f'<component loader="{loader}" uri="{uri}">'
                                    f'<implementation name="{name}"/></component></components>')
                with self.assertRaisesRegex(ValueError, "Python loader or component"):
                    payload.verify_python_free_services(engine, files)
        registry.write_text('<components xmlns="http://openoffice.org/2010/uno-components">'
                            '<component loader="com.sun.star.loader.SharedLibrary" uri="file.dll"/>'
                            '</components>')
        payload.verify_python_free_services(engine, files)
        with self.assertRaisesRegex(ValueError, "no pinned services registry"):
            payload.verify_python_free_services(engine, {})

    def test_stale_python_notice_refused_before_staging(self):
        staged = self.root / "staged"
        staged.mkdir()
        (staged / payload.PACKAGED_NOTICE).write_bytes(b"stale")
        with patch.object(payload, "SOURCE_RECORD", self.source_record):
            with self.assertRaisesRegex(ValueError, "stale Python notices"):
                payload.stage(staged, self.root / "engine")
        self.assertFalse((staged / "office-engine").exists())

    def test_python_free_notices_require_component_and_reject_stale_python(self):
        staged = self.root / "staged"
        staged.mkdir()
        component = staged / payload.PACKAGED_COMPONENT_NOTICE
        with patch.object(payload, "SOURCE_RECORD", self.source_record), \
             patch.object(payload, "component_notice", return_value=pin(b"component")):
            with self.assertRaises(FileNotFoundError):
                payload.verify_notices(staged)
            component.write_bytes(b"component")
            payload.verify_notices(staged)
            (staged / payload.PACKAGED_NOTICE).write_bytes(b"stale")
            with self.assertRaisesRegex(ValueError, "stale Python notices"):
                payload.verify_notices(staged)

    def test_component_v2_requires_accepted_python_free_binding(self):
        notice = self.root / "component.html"
        notice.write_bytes(b"component")
        record_path = self.root / "component.json"
        record = {"formatVersion": 2, "inventorySha256": "INV", "sourceKitManifestSha256": "KIT",
                  "pythonRuntime": "none", "sourceCorrespondenceAccepted": True,
                  "packagingAccepted": True, "notice": pin(b"component")}
        with patch.object(payload, "SOURCE_RECORD", self.source_record), \
             patch.object(payload, "COMPONENT_NOTICE_RECORD", record_path), \
             patch.object(payload, "COMPONENT_NOTICE_FILE", notice), \
             patch.object(payload, "selection", return_value={"inventory": {"sha256": "INV"}}):
            record_path.write_text(json.dumps(record))
            self.assertEqual(payload.component_notice(), pin(b"component"))
            for field, value in (("sourceCorrespondenceAccepted", False), ("packagingAccepted", False),
                                 ("pythonRuntime", "bundled"), ("sourceKitManifestSha256", "OTHER"),
                                 ("inventorySha256", "OTHER")):
                with self.subTest(field=field):
                    changed = dict(record, **{field: value})
                    record_path.write_text(json.dumps(changed))
                    with self.assertRaisesRegex(ValueError, "component notices differ"):
                        payload.component_notice()


if __name__ == "__main__":
    unittest.main()
