"""In-memory guards for the renderer font-query reader; no engine or profile creation."""

import importlib.util
import json
from pathlib import Path

path = Path(__file__).with_name("Inspect-OfficeFontEnvironment.py")
spec = importlib.util.spec_from_file_location("font_environment", path)
reader = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reader)


def main():
    checks = 0
    def check(condition):
        nonlocal checks
        assert condition
        checks += 1
    def encode(value):
        return json.dumps(value).encode()
    legacy = {"commandName": ".uno:CharFontName", "commandValues": {"日本語": ["6", "10.5"], "Arial": ["6", "10.5"]}}
    compact = {"commandName": ".uno:CharFontName", "FontNames": ["日本語", "Arial"], "FontSizes": ["6", "10.5"]}
    check(reader.read_families(encode(legacy)) == ["Arial", "日本語"])
    check(reader.read_families(encode(compact)) == ["Arial", "日本語"])
    check(reader.read_families(encode(legacy | {"commandValues": {"Modern No": {" 20": ["10"]}}})) == ["Modern No. 20"])
    check(reader.read_families(encode({"commandName": ".uno:CharFontName", "commandValues": ""})) == [])
    for data in (b"", b"x" * (reader.MAXIMUM_BYTES + 1), b"\xff", b"null", b"[]",
        b'{"commandName":".uno:CharFontName","commandName":".uno:CharFontName","commandValues":{}}',
        encode(legacy | {"extra": 1}), encode(legacy | {"commandName": ".uno:Other"}), encode(compact | {"FontNames": ["A", "A"]}),
        encode(compact | {"FontNames": [None]}), encode(compact | {"FontNames": ["bad\nname"]}),
        encode(compact | {"FontNames": ["\ud800"]}), encode(compact | {"FontNames": ["A" * 257]}),
        encode(compact | {"FontNames": [str(index) for index in range(4097)]}),
        encode(compact | {"FontSizes": []}), encode(compact | {"FontSizes": ["nan"]}),
        encode(compact | {"FontSizes": [10]}), encode(compact | {"FontSizes": ["0"]}),
        encode(compact | {"FontSizes": ["501"]}), encode(legacy | {"commandValues": {"A": {"B": []}}}),
        encode(legacy | {"commandValues": {"A": {}}})):
        try:
            reader.read_families(data)
        except (ValueError, UnicodeError):
            checks += 1
        else:
            raise AssertionError("Accepted malformed font-query evidence.")
    print(f"Passed {checks} font-environment reader checks; no native process or font installation.")


if __name__ == "__main__":
    main()
