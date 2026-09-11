"""Retain the fixed upstream SVN revision using read-only HTTP, without SVN tools."""
import concurrent.futures
import hashlib
import html.parser
import json
import pathlib
import sys
import urllib.parse
import urllib.request
import uuid
import zipfile

BASE = "https://svn.code.sf.net/p/lame/svn/!svn/bc/6761/trunk/lame/"
MAX_FILE = 8 * 1024 * 1024
MAX_TOTAL = 64 * 1024 * 1024


class Links(html.parser.HTMLParser):
    def __init__(self):
        super().__init__()
        self.links = []

    def handle_starttag(self, tag, attrs):
        if tag == "a":
            self.links.extend(value for key, value in attrs if key == "href")


def read(relative, limit):
    url = BASE + urllib.parse.quote(relative, safe="/")
    request = urllib.request.Request(url, headers={"User-Agent": "ContextSuite-source-retention"})
    with urllib.request.urlopen(request, timeout=30) as response:
        if response.url != url:
            raise ValueError("Unexpected source redirect")
        data = response.read(limit + 1)
        if len(data) > limit:
            raise ValueError("Source response exceeds its limit")
        return data


def main():
    if len(sys.argv) != 2:
        raise ValueError("Usage: python Download-LameSource.py <repository-scratch/lame.zip>")
    if sys.version_info < (3, 14):
        raise ValueError("Source retention requires Python 3.14 or later")
    scratch = pathlib.Path(__file__).resolve().parents[2] / ".codex-temp"
    output = pathlib.Path(sys.argv[1]).absolute()
    if output.name != "lame.zip" or not output.parent.is_relative_to(scratch):
        raise ValueError("Use lame.zip within this repository's .codex-temp directory")
    for ancestor in (output, *output.parents):
        if ancestor == scratch.parent:
            break
        if ancestor.is_symlink() or ancestor.is_junction():
            raise ValueError("Linked source-cache paths are not allowed")
    if not output.parent.resolve().is_relative_to(scratch):
        raise ValueError("Resolved archive path leaves repository scratch")
    if not output.parent.is_dir() or output.exists():
        raise ValueError("Use an existing scratch directory and a new archive path")
    work = output.parent / ("lame-source-" + uuid.uuid4().hex)
    work.mkdir()
    pending = [""]
    names = set()
    files = []
    directories = 0
    while pending:
        relative = pending.pop()
        data = read(relative, 512 * 1024).decode("utf-8")
        if "Revision 6761:" not in data:
            raise ValueError("Unexpected SVN directory revision")
        listing = Links()
        listing.feed(data)
        directories += 1
        if directories > 256:
            raise ValueError("Source directory limit exceeded")
        for link in listing.links:
            if link == "../":
                continue
            is_directory = link.endswith("/")
            leaf = urllib.parse.unquote(link[:-1] if is_directory else link)
            if (not leaf or leaf in (".", "..") or leaf.endswith((".", " ")) or
                    any(ord(char) < 32 or char in '/\\:*?"<>|' for char in leaf) or
                    leaf.split(".")[0].upper() in {"CON", "PRN", "AUX", "NUL", *(f"COM{i}" for i in range(1, 10)), *(f"LPT{i}" for i in range(1, 10))}):
                raise ValueError("Unsupported source path")
            name = relative + leaf
            if name.casefold() in names or name.count("/") > 16 or len(names) >= 4096:
                raise ValueError("Duplicate source path or tree limit exceeded")
            names.add(name.casefold())
            if is_directory:
                pending.append(name + "/")
            else:
                files.append(name)
    if not {"COPYING", "LICENSE", "configure", "Makefile.MSVC", "include/lame.h"}.issubset(files):
        raise ValueError("Required LAME source entries missing")
    print(f"SVN r6761: {len(files)} files in {directories} directories", flush=True)
    total = 0
    records = []
    # Four read-only transfers at once; writing and aggregate accounting stay sequential.
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as executor:
        for name, data in zip(sorted(files), executor.map(lambda name: read(name, MAX_FILE), sorted(files), buffersize=4), strict=True):
            total += len(data)
            if total > MAX_TOTAL:
                raise ValueError("Aggregate source limit exceeded")
            target = work / name
            target.parent.mkdir(parents=True, exist_ok=True)
            with target.open("xb") as stream:
                stream.write(data)
            records.append({"path": name, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()})
    manifest = {"revision": 6761, "source": BASE, "scope": "HTTP file-content export; SVN properties and history are not exported", "files": records}
    (work / "source-inventory.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    # Stored entries and fixed metadata keep the archive independent of compression versions.
    with zipfile.ZipFile(output, "x", compression=zipfile.ZIP_STORED) as archive:
        for record in records:
            entry = zipfile.ZipInfo("lame-r6761/" + record["path"], date_time=(1980, 1, 1, 0, 0, 0))
            entry.create_system = 3
            entry.external_attr = 0o100644 << 16
            archive.writestr(entry, (work / record["path"]).read_bytes())
    print(json.dumps({"archive": str(output), "bytes": output.stat().st_size,
                      "sha256": hashlib.sha256(output.read_bytes()).hexdigest(), "inventory": str(work / "source-inventory.json")}))


if __name__ == "__main__":
    main()
