"""Exercise release-tar guards using authored in-memory archives."""
import importlib.util
import io
import pathlib
import tarfile
import unittest


spec = importlib.util.spec_from_file_location("source_tar", pathlib.Path(__file__).with_name("Read-SourceTar.py"))
reader = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reader)


class SourceTarTests(unittest.TestCase):
    def read(self, entries, required=()):
        buffer = io.BytesIO()
        with tarfile.open(fileobj=buffer, mode="w:gz") as archive:
            for name, kind in entries:
                entry = tarfile.TarInfo(name)
                entry.type = kind
                entry.linkname = "outside"
                entry.size = 1 if kind == tarfile.REGTYPE else 0
                archive.addfile(entry, io.BytesIO(b"x") if entry.size else None)
        buffer.seek(0)
        with tarfile.open(fileobj=buffer, mode="r:gz") as archive:
            return reader.inspect(archive, {"prefix": "release/", "requiredEntries": list(required)})

    def test_regular_release(self):
        members = self.read([("release", tarfile.DIRTYPE), ("release/src", tarfile.DIRTYPE),
                             ("release/src/file.c", tarfile.REGTYPE)], ["src/file.c"])
        self.assertEqual([name for _, name in members], ["src", "src/file.c"])

    def test_links_and_special_entries(self):
        for kind in (tarfile.SYMTYPE, tarfile.LNKTYPE, tarfile.CHRTYPE, tarfile.BLKTYPE, tarfile.FIFOTYPE):
            with self.subTest(kind=kind), self.assertRaises(ValueError):
                self.read([("release/file", kind)])

    def test_unsafe_paths(self):
        for name in ("../file", "/file", "other/file", "release/../file", "release//file", "release/./file",
                     "release/C:/file", "release/file:stream", "release/a\\file", "release/NUL", "release/COM1.c",
                     "release/LPT²", "release/trailing.", "release/trailing ", "release/control\x01"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                self.read([(name, tarfile.REGTYPE)])

    def test_collisions(self):
        for names in (("release/File", "release/file"), ("release/file", "release/file"),
                      ("release/dir", "release/dir/file"), ("release/dir/file", "release/dir")):
            with self.subTest(names=names), self.assertRaises(ValueError):
                self.read([(name, tarfile.REGTYPE) for name in names])

    def test_missing_required_file(self):
        with self.assertRaises(ValueError):
            self.read([("release/COPYING", tarfile.DIRTYPE)], ["COPYING"])

    def test_limits(self):
        entry = tarfile.TarInfo("release/file")
        entry.size = 32 * 1024 * 1024 + 1
        with self.assertRaises(ValueError):
            reader.inspect([entry], {"prefix": "release/", "requiredEntries": []})
        entry.size = 1
        entry.name = "release/" + "/".join(["deep"] * 33)
        with self.assertRaises(ValueError):
            reader.inspect([entry], {"prefix": "release/", "requiredEntries": []})


if __name__ == "__main__":
    unittest.main()
