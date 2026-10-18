"""Verify the portable Python-free Office source-notice companion.

The companion contains pinned build evidence, not source archives. A separately
retained source kit supplies those archives. No archive member is extracted.
"""

import argparse
import hashlib
import html
import importlib.util
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import tarfile
import zipfile
import xml.etree.ElementTree as ET


MAX_MEMBER = 32 * 1024 * 1024
MAX_COMPANION = 256 * 1024 * 1024
MAX_SOURCE_ARCHIVE = 128 * 1024 * 1024
HEX = re.compile(r"[0-9A-F]{64}\Z")
DEPENDENCY = re.compile(rb"workdir/(?:UnpackedTarball|ExternalProject)/([^/\s\\]+)")
V1 = Path(__file__).with_name("Test-ComponentNoticeSources.py")
DEFAULT_RECORD = Path(__file__).with_name("component-notice-review.json")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def pin(data):
    return {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest().upper()}


def safe_name(name):
    parts = name.split("/")
    return (bool(name) and "\\" not in name and not name.startswith("/") and
            all(part not in {"", ".", ".."} and ":" not in part for part in parts) and
            PurePosixPath(name).as_posix() == name)


def read_file(path, limit, expected=None):
    for ancestor in (path, *path.parents):
        if os.path.lexists(ancestor):
            attributes = getattr(ancestor.lstat(), "st_file_attributes", 0)
            require(not ancestor.is_symlink() and
                    not attributes & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0),
                    "Linked input path: " + str(ancestor))
    require(path.is_file() and not path.is_symlink(), "Missing or linked file: " + str(path))
    require(path.stat().st_size <= limit, "File exceeds verifier limit: " + str(path))
    data = path.read_bytes()
    if expected is not None:
        require(pin(data) == expected, "File identity changed: " + str(path))
    return data


def read_member(archive, name, expected=None):
    info = archive.getinfo(name)
    require(info.file_size <= MAX_MEMBER, "Companion member exceeds limit: " + name)
    with archive.open(info) as stream:
        data = stream.read(info.file_size + 1)
    require(len(data) == info.file_size, "Companion member length changed: " + name)
    if expected is not None:
        require(pin(data) == expected, "Companion member identity changed: " + name)
    return data


def python_runtime_name(name):
    parts = [part.lower() for part in name.split("/")]
    base = parts[-1]
    return (any(part.startswith(("python-core-", "pyuno", "pythonloader")) for part in parts)
            or base.endswith(".pyd") or re.fullmatch(r"pythonw?(?:\d+(?:\.\d+)*)?\.exe", base)
            or re.fullmatch(r"python\d+[^/]*\.dll", base)
            or any(part in {"pip", "setuptools", "pkg_resources", "_distutils_hack"} for part in parts))


def parse_inventory(data):
    require(data.endswith(b"\n") and b"\r" not in data, "Runtime inventory line endings differ")
    inventory = {}
    for line in data.decode("utf-8").splitlines():
        name, size, digest = line.split("\t")
        require(safe_name(name) and name not in inventory and not python_runtime_name(name)
                and HEX.fullmatch(digest) and int(size) >= 0, "Invalid runtime inventory entry")
        inventory[name] = {"bytes": int(size), "sha256": digest}
    return inventory


def verify_runtime_derivation(record, inputs, assembly):
    derivation = record["runtimeDerivation"]
    require(record.get("nativeFilesScope") == "base-runtime" and
            derivation.get("retainedBinaryBytesUnchanged") is True,
            "Derived runtime has no byte-preserving base scope")
    base_data = inputs["runtime/base-runtime-files.txt"]
    derived_data = inputs["runtime/runtime-files.txt"]
    require(pin(base_data)["sha256"] == derivation["baseInventorySha256"] ==
            assembly["InventorySha256"], "Base runtime inventory differs")
    base = parse_inventory(base_data)
    require(len(base) == assembly["Files"] and
            sum(item["bytes"] for item in base.values()) == assembly["RuntimeBytes"],
            "Base runtime totals differ")
    policy_data = inputs["receipts/runtime-reduction-policy.json"]
    receipt_data = inputs["receipts/runtime-derivation.json"]
    script_data = inputs["tooling/Derive-ReducedRuntime.py"]
    require(pin(policy_data)["sha256"] == derivation["policySha256"] and
            pin(receipt_data)["sha256"] == derivation["receiptSha256"],
            "Runtime reduction policy or receipt differs")
    policy = json.loads(policy_data)
    result = json.loads(receipt_data)
    detail = result["Derivation"]
    selected = policy["baseSelection"]["inventory"]
    require(policy["formatVersion"] == 1 and
            selected["sha256"] == derivation["baseInventorySha256"] and
            selected["bytes"] == len(base_data) and
            selected["files"] == len(base) and
            selected["runtimeBytes"] == assembly["RuntimeBytes"] and
            policy["retainedTheme"] == "share/config/images_colibre.zip" and
            result["Passed"] is True and result["SourceUnchanged"] is True and
            result["PolicySha256"] == derivation["policySha256"] and
            detail["formatVersion"] == 1 and
            detail["policySha256"] == derivation["policySha256"] and
            detail["scriptSha256"] == pin(script_data)["sha256"] and
            detail["baseInventorySha256"] == derivation["baseInventorySha256"] and
            detail["derivedInventorySha256"] == record["inventorySha256"] and
            detail["retainedBinaryBytesUnchanged"] is True and
            detail["sourceUnchanged"] is True,
            "Runtime reduction provenance differs")
    excluded = policy["excludedFiles"]
    require(len(excluded) == 32 and excluded == detail["excludedFiles"] and
            len({item["path"].casefold() for item in excluded}) == len(excluded) and
            all((item["path"].startswith(("sdk/bin/", "sdk/lib/")) or
                 re.fullmatch(r"share/config/images_[a-z_]+\.zip", item["path"])) and
                item["path"] != policy["retainedTheme"] and
                base.get(item["path"]) == {"bytes": item["bytes"], "sha256": item["sha256"]}
                for item in excluded), "Runtime reduction exclusions differ")
    require(policy["retainedTheme"] in base and
            detail["removedBytes"] == sum(item["bytes"] for item in excluded),
            "Runtime reduction removed the retained theme or totals differ")
    excluded_names = {item["path"] for item in excluded}
    expected_data = b"".join(line for line in base_data.splitlines(keepends=True)
                             if line.split(b"\t", 1)[0].decode("utf-8") not in excluded_names)
    require(derived_data == expected_data and
            pin(derived_data)["sha256"] == record["inventorySha256"],
            "Derived runtime inventory is not the exact base reduction")
    retained = parse_inventory(derived_data)
    selection = result["Selection"]["inventory"]
    require(selection["sha256"] == record["inventorySha256"] and
            selection["files"] == len(retained) and
            selection["runtimeBytes"] == sum(item["bytes"] for item in retained.values()),
            "Derived runtime selection receipt differs")
    return base, retained


def notices_text(items):
    parts = ['<!doctype html>', '<html lang="en"><head><meta charset="utf-8">',
             '<meta name="viewport" content="width=device-width, initial-scale=1">',
             '<title>Office dependency notices - Context Suite</title>',
             '<style>:root{color-scheme:light dark}body{font:1rem/1.55 system-ui,sans-serif;',
             'max-width:76rem;margin:2rem auto;padding:0 1.5rem}pre{white-space:pre-wrap;',
             'overflow-wrap:anywhere;font:0.9rem/1.5 ui-monospace,monospace}',
             'section{border-top:1px solid GrayText;margin-top:2rem}a{color:LinkText}',
             'a:focus-visible{outline:3px solid Highlight;outline-offset:3px}</style></head><body>',
             '<h1>Office dependency notices</h1>',
             '<p>These notices supplement Office runtime licenses. The source kit retains selected ',
             'and unselected build archives; a source notice does not by itself prove a component ',
             'is distributed. Python, pip and setuptools are absent from this runtime. ',
             'Original license alternatives remain unchanged.</p>', '<nav aria-label="Components"><ul>']
    components = sorted({metadata["component"] for metadata, _ in items})
    for component in components:
        parts.append(f'<li><a href="#{html.escape(component, quote=True)}">{html.escape(component)}</a></li>')
    parts.append('</ul></nav><main>')
    for component in components:
        parts.append(f'<section id="{html.escape(component, quote=True)}"><h2>{html.escape(component)}</h2>')
        for metadata, raw in items:
            if metadata["component"] != component:
                continue
            parts.append('<h3>' + html.escape(metadata["member"]) + '</h3>')
            if metadata["start"] != 0 or metadata["end"] != metadata["source"]["bytes"]:
                parts.append(f'<p>Source comment (byte offsets {metadata["start"]} to '
                             f'{metadata["end"]}, end exclusive).</p>')
            parts.append('<pre>' + html.escape(raw.decode(metadata["encoding"])) + '</pre>')
        parts.append('</section>')
    return ('\n'.join(parts + ['</main></body></html>', ''])).encode("utf-8")


def source_checker():
    spec = importlib.util.spec_from_file_location("office_notice_v1_isolated", V1)
    checker = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(checker)
    checker.EXTRA_FILES["nss"] = {"nss/lib/sqlite/sqlite3.h"}
    checker.LATIN1.update({"COPYRIGHT.html", "sac-1.3/COPYRIGHT.html"})
    return checker


def validate_source_archive(data):
    require(len(data) <= MAX_SOURCE_ARCHIVE, "Source archive exceeds verifier limit")
    names = set()
    if zipfile.is_zipfile(io.BytesIO(data)):
        with zipfile.ZipFile(io.BytesIO(data)) as source:
            members = [(member.filename, member.file_size, member.is_dir()) for member in source.infolist()]
    else:
        with tarfile.open(fileobj=io.BytesIO(data), mode="r:*") as source:
            members = [(member.name, member.size, member.isdir()) for member in source]
    for name, size, directory in members:
        normalized = name.rstrip("/") if directory else name
        require(safe_name(normalized) and normalized not in names,
                "Unsafe or duplicate source archive member: " + name)
        names.add(normalized)
        if not directory:
            require(size <= MAX_MEMBER, "Source member exceeds verifier limit: " + name)


def verify(bundle, source_kit, engine=None, record_path=DEFAULT_RECORD):
    checker = source_checker()
    selected_record = read_file(record_path, MAX_MEMBER)
    read_file(bundle, MAX_COMPANION)
    with zipfile.ZipFile(bundle) as archive:
        infos = archive.infolist()
        names = [info.filename for info in infos]
        require(len(names) == len(set(name.casefold() for name in names)) and
                all(safe_name(name) and not info.is_dir()
                and info.file_size <= MAX_MEMBER for name, info in zip(names, infos)),
                "Unsafe, duplicate, or oversized companion member")
        require(sum(info.file_size for info in infos) <= MAX_COMPANION,
                "Companion exceeds verifier limit")
        require(read_member(archive, "component-notice-review.json") == selected_record,
                "Companion record differs from selected record")
        record = json.loads(selected_record)
        require(record["formatVersion"] == 2 and record["pythonRuntime"] == "none" and
                record["sourceCorrespondenceAccepted"] is True and
                record["packagingAccepted"] is True and
                record["licensingOrAdoptionAccepted"] is False,
                "Selection is not a technically accepted Python-free v2 packet")
        checked = record["checkedInputs"]
        require(isinstance(checked, dict) and checked and all(safe_name(name) and
                name.startswith(("source/", "build/", "runtime/", "receipts/", "tooling/", "kit/"))
                for name in checked), "Checked input names must be normalized companion paths")
        require(set(names) == set(checked) | {"component-notice-review.json", "Office.Dependency.Notices.html"},
                "Companion membership differs from checked inputs")
        require("kit/source-kit.json" in checked and "runtime/runtime-files.txt" in checked and
                "source/download.lst" in checked, "Required selected inputs are missing")
        require(record.get("sourceCorrespondenceReceipt") == "receipts/source-correspondence.json"
                and "receipts/source-correspondence.json" in checked,
                "Source correspondence receipt is missing")
        inputs = {name: read_member(archive, name, identity) for name, identity in checked.items()}
        manifest_data = read_file(source_kit / "source-kit.json", MAX_MEMBER)
        require(manifest_data == inputs["kit/source-kit.json"] and
                pin(manifest_data)["sha256"] == record["sourceKitManifestSha256"],
                "Source kit manifest differs")
        manifest = json.loads(manifest_data)
        dependencies = {item["Name"]: item for item in manifest["Dependencies"]}
        require(len(dependencies) == len(manifest["Dependencies"]), "Duplicate source dependency")
        for field, path in (("assemblyReceiptSha256", "receipts/assembly.json"),
                            ("nativeReceiptSha256", "receipts/native.json"),
                            ("javaReceiptSha256", "receipts/java.json"),
                            ("fetchReceiptSha256", "receipts/fetch.json")):
            require(pin(inputs[path])["sha256"] == record[field], "Receipt differs: " + path)
        correspondence = json.loads(inputs["receipts/source-correspondence.json"])
        require(correspondence.get("AllPreparedSourceFilesMatch") is True and
                correspondence.get("InputsUnchanged") is True and
                correspondence.get("Missing") == [] and
                correspondence.get("Different") == [] and
                correspondence.get("GeneratedExtras") == [] and
                correspondence.get("SourceKitManifestSha256") == record["sourceKitManifestSha256"] and
                correspondence.get("ComparedFiles") == 148225,
                "Prepared source correspondence is incomplete")
        assembly = json.loads(inputs["receipts/assembly.json"])
        native_receipt = json.loads(inputs["receipts/native.json"])
        java = json.loads(inputs["receipts/java.json"])
        fetch = json.loads(inputs["receipts/fetch.json"])
        require(native_receipt["Passed"] is True and java["Passed"] is True and
                fetch["Passed"] is True, "Runtime or dependency acceptance receipt differs")
        inventory_data = inputs["runtime/runtime-files.txt"]
        require(pin(inventory_data)["sha256"] == record["inventorySha256"],
                "Runtime inventory differs")
        inventory = parse_inventory(inventory_data)
        if "runtimeDerivation" in record:
            base_inventory, inventory = verify_runtime_derivation(record, inputs, assembly)
        else:
            require(assembly["InventorySha256"] == record["inventorySha256"] and
                    len(inventory) == assembly["Files"] and
                    sum(item["bytes"] for item in inventory.values()) == assembly["RuntimeBytes"],
                    "Runtime inventory totals differ")
            base_inventory = inventory
            require(record.get("nativeFilesScope") in (None, "selected-runtime"),
                    "Unrecognized native-file scope")
        binaries = {name: identity for name, identity in base_inventory.items()
                    if Path(name).suffix.lower() in {".dll", ".exe", ".pyd", ".jar"}}
        require(binaries == record["nativeFiles"],
                "Native and JAR runtime coverage differs")
        service_paths = {"runtime/program/services/services.rdb", "runtime/program/services.rdb"}
        require(service_paths <= set(inputs), "Runtime service registries are missing")
        staged = assembly["StagedServices"]
        for member in service_paths:
            suffix = member.removeprefix("runtime/").replace("/", "\\").lower()
            matching = [value for path, value in staged.items()
                        if path.lower().endswith("\\runtime\\" + suffix)]
            require(len(matching) == 1 and
                    pin(inputs[member])["sha256"] == matching[0]["Sha256"],
                    "Runtime service registry receipt differs: " + member)
            root = ET.fromstring(inputs[member])
            namespace = "{http://openoffice.org/2010/uno-components}"
            actual = list(root)
            require(root.tag == namespace + "components" and
                    len(actual) == len(matching[0]["Components"]),
                    "Runtime service registry membership differs: " + member)
            for element, recorded in zip(actual, matching[0]["Components"]):
                require(element.tag == namespace + "component" and
                        element.get("loader") == recorded["Loader"] and
                        element.get("uri") == recorded["Uri"] and
                        not re.search(r"python|pyuno", recorded["Loader"] + recorded["Uri"], re.I),
                        "Python or changed service remains: " + member)
        selected = {item["Name"] for item in fetch["Selected"]}
        require(len(selected) == fetch["Files"] and selected <= set(dependencies),
                "Fetched source selection differs")
        targets = record["targets"]
        consumers = {}
        require(len(targets) == 244, "Native dependency target count differs")
        for path, target in targets.items():
            require(path.startswith("workdir/Dep/LinkTarget/") and
                    safe_name(path) and "build/" + path in inputs, "Missing native dependency record")
            data = inputs["build/" + path]
            require(pin(data) == target["pin"] and target["outputs"] and
                    all(output in binaries and Path(output).name + ".d" == Path(path).name
                        for output in target["outputs"]), "Native target/output correspondence differs")
            components = sorted({item.decode().removesuffix(".done") for item in DEPENDENCY.findall(data)})
            require(components == target["components"], "Native dependency selection differs")
            for component in components:
                consumers.setdefault(component, []).append(path)
        variables = dict(re.findall(r"^([A-Z0-9_]+)\s*:=\s*(\S+)\s*$",
                                    inputs["source/download.lst"].decode("utf-8"), re.M))
        components = record["components"]
        require(len(components) == 83 and len({item["component"] for item in components}) == 83,
                "Component selection differs")
        by_name = {item["component"]: item for item in components}
        require("embeddedSqlite" in by_name["nss"] and
                "runtimeSupport" in by_name["openssl"] and
                "runtimeSupport" in by_name["libassuan"],
                "Explicit source/runtime support evidence is missing")
        require(set(consumers) | {"libassuan", "openssl"} |
                {item["component"] for item in components if "javaDeliverable" in item} ==
                {item["component"] for item in components}, "Component evidence coverage differs")
        notices = []
        for item in components:
            component, recipe, archive_name = item["component"], item["recipe"], item["archive"]
            require(safe_name(recipe) and safe_name(archive_name) and
                    recipe.startswith("external/") and "source/" + recipe in inputs and
                    pin(inputs["source/" + recipe]) == item["recipePin"],
                    "Source recipe differs: " + component)
            match = re.search(rb"gb_UnpackedTarball_set_tarball," + re.escape(component.encode()) +
                              rb",\$\(([^)]+)\)", inputs["source/" + recipe])
            require(match is not None, "Archive recipe missing: " + component)
            value = variables[match[1].decode()]
            for _ in range(10):
                value = re.sub(r"\$\(([^)]+)\)", lambda match: variables[match[1]], value)
            require(value == archive_name and archive_name in selected and
                    dependencies.get(archive_name) == item["archivePin"] and
                    item["dependencyRecords"] == sorted(consumers.get(component, [])),
                    "Component source or dependency evidence differs: " + component)
            archive_pin = {"bytes": item["archivePin"]["Bytes"],
                           "sha256": item["archivePin"]["Sha256"]}
            source_data = read_file(source_kit / "dependencies" / archive_name,
                                    MAX_SOURCE_ARCHIVE, archive_pin)
            validate_source_archive(source_data)
            selected_notices = checker.source_notices(source_data, component)
            require(selected_notices, "Component has no selected notice: " + component)
            if "embeddedSqlite" in item:
                require(component == "nss" and item["embeddedSqlite"]["sourceMember"] ==
                        "nss-3.128/nss/lib/sqlite/sqlite3.h", "Unexpected embedded SQLite evidence")
                require(any(metadata["member"] == item["embeddedSqlite"]["sourceMember"]
                            for metadata, _ in selected_notices), "NSS SQLite disclaimer missing")
            for support_name in ("runtimeSupport", "embeddedSqlite"):
                if support_name in item:
                    support = item[support_name]
                    support_data = inputs["source/" + support["recipe"]]
                    token = ("dist/out/lib/sqlite3.dll" if support_name == "embeddedSqlite"
                             else support["token"])
                    require(pin(support_data) == support["recipePin"] and
                            token.encode() in support_data,
                            "Explicit runtime support recipe differs: " + component)
                    supported = support.get("paths", {support.get("path"): support.get("pin")})
                    require(all(binaries.get(path) == identity for path, identity in supported.items()),
                            "Explicit runtime support binary differs: " + component)
            if "javaDeliverable" in item:
                delivered = item["javaDeliverable"]
                require(binaries.get(delivered["path"]) == delivered["pin"] and
                        delivered["receiptSha256"] == record["javaReceiptSha256"] and
                        java["Jars"][Path(delivered["path"]).name]["Sha256"] == delivered["pin"]["sha256"],
                        "Java deliverable identity differs: " + component)
                chain = delivered["recipeChain"]
                for element in chain.values():
                    require(pin(inputs["source/" + element["path"]]) == element["pin"],
                            "Java source recipe changed: " + component)
                module = inputs["source/" + chain["module"]["path"]]
                module_targets = {line.strip().removesuffix(b"\\").strip()
                                  for line in module.splitlines()}
                require(all(target.encode() in module_targets for target in chain["module"]["targets"]),
                        "Java module chain differs: " + component)
                project = inputs["source/" + chain["externalProject"]["path"]]
                package = inputs["source/" + chain["externalPackage"]["path"]]
                project_name = chain["externalProject"]["name"].encode()
                package_name = chain["externalPackage"]["name"].encode()
                require(re.search(rb"gb_ExternalProject_ExternalProject," + re.escape(project_name) + rb"\)",
                                  project) is not None and
                        re.search(rb"gb_ExternalPackage_ExternalPackage," + re.escape(package_name) +
                                  b"," + re.escape(project_name) + rb"\)", package) is not None and
                        re.search(rb"gb_ExternalPackage_use_external_project," + re.escape(package_name) +
                                  b"," + re.escape(project_name) + rb"\)", package) is not None,
                        "Java package chain differs: " + component)
                mapping = re.search(rb"gb_ExternalPackage_add_file," + re.escape(package_name) +
                                    rb",\$\(LIBO_SHARE_JAVA_FOLDER\)/([^,\r\n]+),([^\r\n]+)\)\)", package)
                require(mapping is not None, "Java package output mapping missing: " + component)
                output, built = (value.decode() for value in mapping.groups())
                versions = chain.get("versionVariables", {}).get("values", {})
                if "versionVariables" in chain:
                    version_data = inputs["source/" + chain["versionVariables"]["path"]]
                    actual_versions = {key.decode(): value.decode() for key, value in
                                       re.findall(rb"^([A-Z_]+)=(\S+)$", version_data, re.M)}
                    require(versions == {name: actual_versions[name] for name in versions},
                            "Java version mapping differs: " + component)
                expand = lambda value: re.sub(r"\$\(([^)]+)\)",
                                              lambda match: versions.get(match[1], match[0]), value)
                jar = Path(delivered["path"]).name
                require(expand(output) == jar and expand(built).endswith("/" + jar),
                        "Java package output differs: " + component)
            notices.extend(selected_notices)
        require([metadata for metadata, _ in notices] == record["notices"],
                "Source notice membership or metadata differs")
        notice_data = notices_text(notices)
        require(pin(notice_data) == record["notice"] and
                notice_data == read_member(archive, "Office.Dependency.Notices.html"),
                "Source notices differ from rendered HTML")
    retained_binaries = {name: identity for name, identity in inventory.items()
                         if Path(name).suffix.lower() in {".dll", ".exe", ".pyd", ".jar"}}
    compared = None
    if engine is not None:
        require(read_file(engine / "runtime-files.txt", MAX_MEMBER) == inventory_data,
                "Engine inventory differs")
        for name, identity in retained_binaries.items():
            read_file(engine / "runtime" / name, MAX_MEMBER, identity)
        compared = len(retained_binaries)
    return {"Passed": True, "Components": len(components), "Notices": len(notices),
            "BaseNativeFiles": len(binaries), "RetainedNativeFiles": len(retained_binaries),
            "RetainedNativeFilesCompared": compared,
            "NativeRebuilt": False, "LicensingOrAdoptionAccepted": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bundle", type=Path)
    parser.add_argument("--source-kit", required=True, type=Path)
    parser.add_argument("--engine-directory", type=Path)
    parser.add_argument("--record", type=Path, default=DEFAULT_RECORD,
                        help="Independently selected review record (default: production record)")
    args = parser.parse_args()
    print(json.dumps(verify(args.bundle, args.source_kit, args.engine_directory, args.record), indent=2))


if __name__ == "__main__":
    main()
