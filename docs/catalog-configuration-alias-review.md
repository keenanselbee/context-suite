Configuration Filename Review
=============================

Reviewed 2026-09-16. Catalog revision **2026-09-16.2** contains 245 records.
The new `configuration` record separates `.cfg` and `.conf` from `ini`, whose
stable ID and `.ini` association remain. The desktop-entry record adds
`.directory` and now explicitly describes menu directories as well as launchers
and links. No content detector, MIME identifier or executable capability changes.

This review accounts for eleven extension associations and eight exact filenames
across all eleven Configuration or system data records. These are conventions,
not exclusive assignments: a name alone establishes neither syntax nor the
application that owns a file. The descriptions are independently authored;
no third-party database, code or specification prose was imported.


Associations and primary evidence
--------------------------------

| Record | Reviewed associations | Meaning and evidence |
| --- | --- | --- |
| cmake | `.cmake`; exact `CMakeLists.txt` | Directory build descriptions, scripts and modules: [CMake language](https://cmake.org/cmake/help/latest/manual/cmake-language.7.html). |
| configuration | `.cfg`, `.conf` | Application settings or instructions with application-specific syntax. [Setuptools setup.cfg](https://setuptools.pypa.io/en/stable/userguide/declarative_config.html) is a configuration convention; [NGINX nginx.conf](https://nginx.org/en/docs/beginners_guide.html) uses directives and blocks. Neither suffix identifies one language. |
| desktop-entry | `.desktop`, `.directory` | Launchers, links and directory descriptions. [Freedesktop naming rules](https://specifications.freedesktop.org/desktop-entry/latest/file-naming.html) prescribe `.directory` for directory entries; [entry types](https://specifications.freedesktop.org/desktop-entry/latest-single/) distinguish the uses. These files are not ordinary filesystem directories. |
| dockerfile | Exact `Dockerfile`, `Containerfile` | Container image build instructions: [Podman build inputs](https://docs.podman.io/en/latest/markdown/podman-build.1.html). |
| editorconfig | Exact `.editorconfig` | Editor settings for matching files: [EditorConfig specification](https://spec.editorconfig.org/). |
| environment | `.env`; exact `.env` | Application environment settings: [Node dotenv conventions](https://nodejs.org/api/environment_variables.html). Other names and dialects exist; this catalog does not add a wildcard for every `.env.*` name. |
| gitignore | Exact `.gitignore` | Patterns for intentionally untracked files: [Git ignore documentation](https://git-scm.com/docs/gitignore). This does not change the status of already tracked files. |
| ini | `.ini` | Sections and named values, with dialect differences: [Python ConfigParser](https://docs.python.org/3/library/configparser.html). Readability alone does not validate this structure. |
| makefile | `.mk`, `.mak`; exact `Makefile`, `GNUmakefile` | Make build rules. [GNU Make](https://www.gnu.org/software/make/manual/make.html) documents default names and included `.mk` examples; [Microsoft makefiles](https://learn.microsoft.com/en-us/windows/win32/stg/makefiles) documents NMAKE-compatible `.mak` examples. These are conventions, not required suffixes or interchangeable dialects. |
| properties | `.properties` | Named Java configuration values and resource strings: [Properties](https://docs.oracle.com/en/java/javase/25/docs/api/java.base/java/util/Properties.html), [PropertyResourceBundle filename example](https://docs.oracle.com/en/java/javase/25/docs/api/java.base/java/util/PropertyResourceBundle.html). |
| windows-url | `.url` | Windows Internet shortcuts: [Microsoft shortcut API](https://learn.microsoft.com/en-us/windows/win32/lwef/internet-shortcuts), [Microsoft .url example](https://support.microsoft.com/en-us/onedrive/onedrive-shared-folders-shortcuts-showing-up-as-internet-shortcuts). Analyze does not visit their targets. |

Case-insensitive lookup retains the existing Windows policy; exact names precede
suffix hints. The catalog does not claim that every owning tool accepts every
case variant on every filesystem. Broader source-code aliases, competing uses
and additional MIME coverage remain separate work.


Verification boundary
---------------------

Authored NGINX-style, INI-style and directory-entry samples check qualified
filename identification. Binary samples retain the unverified-filename warning.
Recognized JSON, XML and PDF content takes precedence under each affected name.
These tests exercise identification, not application syntax, build execution,
shortcut activation or configuration loading.

The independent before/after audit must retain all 244 existing IDs, change only
the approved INI/desktop-entry fields, and add exactly the generic configuration
record. The catalog has 34 content-identification routes and 211 filename-only
records. Additional names do not create additional detected formats.

All **4,020 foundation contracts pass**, including 22 new checks. The canonical
Release run is retained in `.codex-temp/catalog-configuration-foundation.log`,
with `-inputs.json` and `-exit.json` receipts. The process exited successfully
and all captured inputs, including the catalog JSON, remained unchanged.
The exact data audit is `.codex-temp/catalog-configuration-delta.json`.

Native worker matrices, formal packaging and visible/accessibility acceptance
remain separate from this descriptive catalog change. See the
[broad file support goal](broad-file-support-goal.md) for remaining acceptance.
