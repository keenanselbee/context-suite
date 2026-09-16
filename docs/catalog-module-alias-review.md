Source Module and Build Filename Review
======================================

Reviewed 2026-09-16. Catalog revision **2026-09-16.1** retains all 244 records and
adds six associations: four TypeScript suffixes and the exact Ruby filenames
`Gemfile` and `Rakefile`. Two descriptions now explain the added uses and the
shared `.mts` suffix. This is descriptive Analyze coverage, not language parsing,
execution, dependency resolution or a new transformation capability.

The review accounts for all seventeen extension associations and two exact names
across the current `javascript`, `typescript`, `python` and `ruby` records. It
also corroborates the existing video meaning of `.mts`. Other source/configuration
aliases and the wider catalog review remain open; the four-record scope does not
claim exhaustive language or filename coverage.


Associations and primary evidence
--------------------------------

| Record | Reviewed associations | Meaning and evidence |
| --- | --- | --- |
| javascript | `.js`, `.mjs`, `.cjs`, `.jsx` | JavaScript source/modules and JSX component markup. Node distinguishes module handling for mjs/cjs; js depends on context. [Node package rules](https://nodejs.org/api/packages.html), [React JSX](https://react.dev/learn/writing-markup-with-jsx), [TypeScript JSX handling](https://www.typescriptlang.org/docs/handbook/jsx.html). |
| typescript | `.ts`, `.tsx`, `.d.ts`, `.mts`, `.cts`, `.d.mts`, `.d.cts` | Source, JSX-bearing source, module variants and type declarations. [TypeScript module extensions](https://www.typescriptlang.org/docs/handbook/release-notes/typescript-4-7.html), [declaration files](https://www.typescriptlang.org/docs/handbook/2/type-declarations.html), [JSX](https://www.typescriptlang.org/docs/handbook/jsx.html). |
| python | `.py`, `.pyw`, `.pyi` | Python source, the Windows windowed-script convention and type stubs. [Windows launcher specification](https://peps.python.org/pep-0397/), [typing distribution specification](https://typing.python.org/en/latest/spec/distributing.html). The historical launcher convention does not establish this computer's current associations. |
| ruby | `.rb`, `.rake`, `.gemspec`; exact `Gemfile`, `Rakefile` | Ruby code, build tasks and dependency/package declarations. [Rake file conventions](https://ruby.github.io/rake/doc/rakefile_rdoc.html), [RubyGems specifications](https://guides.rubygems.org/specification-reference/), [Gemfile reference](https://guides.rubygems.org/gemfile/). |

The Gemfile reference explicitly describes Ruby evaluation by its owning tool.
Analyze only reads its bounded sample; a description of dependencies never causes
Bundler, Ruby, a build task or an install command to run. The old Bundler URL
redirected to the current RubyGems guide above. Primary documentation was read on
the review date; descriptions are independently phrased, with no imported source
code, format database or specification prose.

The `.mts` camera-video convention is independently documented by
[Sony](https://www.sony.com/electronics/support/e-mount-body-ilce-7-series/articles/00051703).
Both `mpeg-ts` and `typescript` remain candidates for `.mts` and `.ts`. Unrecognized
binary bytes cannot select a meaning. A readable text sample can narrow to the
text-compatible TypeScript hint, but cannot validate syntax or prove a language.
Actual recognized content, such as a PDF signature under a misleading name,
retains precedence. This adds no MPEG transport-stream detector.

Longest compound-suffix lookup keeps `.d.mts` and `.d.cts` as declaration-name
hints. Exact Ruby names do not match `Gemfile.lock`, `Gemfile.txt`, `another.Gemfile`
or `Rakefile.backup`. Case-insensitive matching follows the existing Windows
catalog policy. Other valid filenames can exist; these are common conventions,
not restrictions imposed on a programming language or build tool.


Verification boundary
---------------------

The existing catalog loading/reachability checks are supplemented with ambiguity,
text-hint, compound-suffix, exact-name and content-precedence cases. A before/after
JSON comparison must preserve every other record and field, including MIME
identifiers, text compatibility, IDs, source links and operation permissions.
The four source records now contain seventeen suffix associations and two names;
the catalog still has 244 descriptions, not 250 independently detected formats.

Formal packaging, native image/audio/PDF/Office execution and visual/accessibility
acceptance are separate. This review does not close those gates or the remaining
catalog alias/MIME work in the [broad file support goal](broad-file-support-goal.md).

All **3,998 foundation contracts pass**, including 24 new alias/confidence checks.
The canonical Release foundation run is retained in
`.codex-temp/catalog-module-foundation.log`, with `-inputs.json` and `-exit.json`
receipts. Exit code is zero and all captured sources, including the embedded
catalog JSON, remain unchanged. The expected malformed-client IPC diagnostic
is retained separately from the successful process result.

The before/after record audit is `.codex-temp/catalog-module-delta.json`; it
requires exactly the two changed records and their approved fields. Repository
boundary, theme-policy and 164-document checks also pass. Native worker matrices,
formal packaging and visual/accessibility acceptance were not rerun for this
descriptive catalog change.
