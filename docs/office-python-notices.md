Office Python Dependency Notices
================================

Status: supplemental notices and source correspondence prepared on 2026-09-23.
This closes the identified pip/setuptools notice omission in the selected
Office runtime. Complete Office/PDF dependency review, source delivery,
redistribution decisions and final candidate acceptance remain open.

The 2026-10-13 size-reduction experiment adds an explicit `pythonRuntime` mode
to Office packaging. Missing mode retains the historical bundled behavior;
`none` requires a Python-free inventory and service registries, omits this
supplement, and rejects a stale Python notice. Unknown modes fail. Component
notices remain mandatory. The source-built Python-free runtime is now selected
for packaging; Office remains bundled. The
[size-reduction goal](repository-size-reduction-goal.md) tracks combined candidate
acceptance. The earlier Python supplement and its evidence remain preserved for
historical payloads. Six focused mode tests and 81 offline
notice-index checks pass, including preservation of the historical link order.


Identified omission
-------------------

The selected runtime includes pip 25.0.1, setuptools 79.0.1, pkg_resources and
_distutils_hack under `program/python-core-3.12.14/lib`. LibreOffice's file list
omits their distribution metadata directories and some vendor notices. The root
Python license does not substitute for those component texts. The pip wheel
inside the retained Python archive also omits vendor license files that are
present in pip's source distribution.

The [supplemental notice file](../assets/third-party/office/Office.Python.Notices.txt)
retains **47** original license, attribution and author texts, including pip's
vendored dependencies and setuptools' nested wheel/packaging notices. It is
180,284 bytes, SHA-256
`40AC93DD062F4F37BF78E33160CDA1AD3C948B31A7603C78197D2B02A6907BB0`.
Original text bytes, license alternatives and whitespace are preserved. No
dependency package was installed or executed to collect them.


Source and runtime correspondence
---------------------------------

The [review record](../tools/office-engine/python-notice-review.json) binds the
notice contents, two bundled wheels, source archives and current runtime
inventory. The Python archive is already in the selected Office source kit.
The additional pip archive came from the version-specific
[PyPI release metadata](https://pypi.org/pypi/pip/25.0.1/json); its bytes match the
published SHA-256. Pip's [vendoring policy](https://pip.pypa.io/en/stable/development/vendoring-policy/)
describes its component/version and accompanying-license conventions; the
actual selected source archive, rather than the current documentation version,
supplies this checkpoint's texts and code comparison.

| Input | Bytes | SHA-256 |
| --- | ---: | --- |
| Python-3.12.14.tar.xz | 20,820,300 | `5C8462AF5790BAF43A321A1559DBE0DB06D1BE4300FB85FB53C40060668E548A` |
| pip-25.0.1.tar.gz | 1,950,850 | `88F96547EA48B940A3A385494E181E29FB8637898F88D88737C5049780F196EA` |

All **675** packaged files in these four package directories match their
retained wheel sources after the two documented upstream packaging changes:
LibreOffice renames `Lorem ipsum.txt` to `Lorem_ipsum.txt` and updates that
filename in `setuptools/_vendor/jaraco/text/__init__.py`. Its selected source
contains both the unpacking recipe and `external/python3/setuptools.patch.1`.
The remaining 673 files match without adjustment. Pip's source distribution
also matches every `pip/` member of the retained wheel.

The four-member source companion is retained at
`.codex-temp/office-python-notices/7c8c131d709d4195b9d75fdaccb139ad/office-python-source-notices.zip`.
It contains both source archives, the review record and supplemental notice.
It is 22,809,931 bytes, SHA-256
`357CA3A7329824F20E3DD488AEF739A0F0C01FED3FFD5ED957A4E594BB1C2B91`.
It contains no proprietary application source and is a local review artifact,
not a published download. The Python archive duplicates a dependency already
in the Office source kit; do not add these sizes as unique source content.

Use `python -B tools/office-engine/Test-PythonNoticeSources.py <bundle>` to
verify exact archive membership, archive/wheel/notice identities and unchanged
collated text. Add `--engine-directory <engine>` to compare actual runtime files.
The verifier reads archives without extraction or dependency execution.
The retained `verification.json` records all 675 comparisons and 47 notices;
`negative-checks.json` records refusals for changed notice text, changed source
bytes and an extra ZIP member. Native engine rebuilding was unnecessary.


Packaging behavior
------------------

Office staging copies `Office.Python.Notices.txt` beside the application, outside
the immutable `office-engine` directory. Packaging verifies its exact hash and
binding to the selected runtime, refuses missing or altered text, and will not
overwrite an existing supplemental file. Runtime inventories, executable hashes,
source-kit records and reserved candidate 1.1.1 remain unchanged.

The [offline index](bundled-notices.md) exposes the additional file in its Office
group. Its expanded selection now has **59** links; **67** synthetic index checks
pass at `.codex-temp/notice-index/6b61313a152244408a623155bde30795`.
This does not claim actual browser, screen-reader or visible acceptance.

`tools/office-engine/Test-OfficePayload.py` passes **55** checks at
`.codex-temp/office-payload-tests-ef0d3aa880fd4a9ea8bf138d35a61ef6`.
These include actual staging and full production verification, missing/corrupt
notices, mismatched runtime selection, preservation of existing files, and the
previous inventory/path/copy-failure guards. All captured source, base payload
and original engine identities remain unchanged. No Office process or Windows
profile was created. The resulting image/audio/PDF/Office scratch package is
packaging evidence, not a freshly reserved or execution-tested release candidate.

The first disposable base assembly used its evidence directory rather than the
nested payload directory. Index generation stopped on a missing .NET license;
the failed attempt and log remain beside the corrected `base-payload-02`.
The corrected base passes the full image/audio/PDF packaging verifier. No retained
payload was repaired in place or verification condition relaxed.
