Source Code and Configuration Catalog Review
===========================================

Revision **2026-09-11.8** reviews the general purposes of all 49 records in the
Source code and Configuration or system data families. Twenty records receive
clearer descriptions or more relevant references. The catalog retains 243 records
and unchanged IDs, names, families, extensions, exact-name hints, MIME fields,
detectors and operation permissions.

The descriptions distinguish application code, automation, type declarations,
build instructions and configuration data. They describe a format's usual role,
not what a selected file actually does. Analyze does not execute scripts, compile
programs, import modules, build containers, render pages or follow shortcut targets.


Reviewed purposes and sources
-----------------------------

Descriptions are independently phrased from the project and specification
references below, consulted on 2026-09-11. No database, source code, specification
text or executable was imported. Existing accurate descriptions are retained.

| Catalog IDs | Reviewed purpose or distinction | Evidence |
| --- | --- | --- |
| batch | Windows command-interpreter automation | [Microsoft cmd](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/cmd) |
| bibtex | Citation records used to produce bibliographies | [CTAN BibTeX package](https://ctan.org/pkg/bibtex) |
| c, cpp, objective-c | Source and declarations; shared suffixes do not prove language | [GCC input-language conventions](https://gcc.gnu.org/onlinedocs/gcc/Overall-Options.html) |
| cmake | Build configuration, reusable modules and standalone scripts | [CMake language](https://cmake.org/cmake/help/latest/manual/cmake-language.7.html) |
| csharp | Application and library source | [C# reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/) |
| css | Presentation rules for structured documents | [W3C CSS snapshot](https://www.w3.org/TR/css/) |
| dart | Dart programs, including Flutter applications | [Dart overview](https://dart.dev/overview) |
| desktop-entry | Desktop launcher and integration settings | [freedesktop specification](https://specifications.freedesktop.org/desktop-entry/latest/) |
| dockerfile | Container-image instructions; Dockerfile and Containerfile names | [Docker reference](https://docs.docker.com/reference/dockerfile/), [Podman build](https://docs.podman.io/en/latest/markdown/podman-build.1.html) |
| editorconfig | Editor formatting preferences for matching paths | [EditorConfig specification](https://spec.editorconfig.org/) |
| elixir | Application code and scripts | [Elixir introduction](https://elixir.hexdocs.pm/introduction.html) |
| environment | Named application environment settings; no universal .env grammar | [Node.js environment documentation](https://nodejs.org/api/environment_variables.html) |
| erlang | Modules and declarations | [Erlang reference](https://www.erlang.org/doc/system/reference_manual.html) |
| fortran | Scientific and numerical programs | [Fortran project](https://fortran-lang.org/), [GNU Fortran manual](https://gcc.gnu.org/onlinedocs/gfortran/) |
| fsharp | Programs, scripts and signature declarations | [F# reference](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/) |
| gitignore | Ignore patterns for untracked files, not removal of tracked files | [Git documentation](https://git-scm.com/docs/gitignore) |
| go | Programs and packages | [Go specification](https://go.dev/ref/spec) |
| haskell | Ordinary and literate source; the latter mixes prose and code | [Haskell report, literate comments](https://www.haskell.org/onlinereport/haskell2010/haskellch10.html) |
| html | Web-document structure and content | [HTML introduction](https://html.spec.whatwg.org/multipage/introduction.html) |
| ini | Sections and named settings; broad .cfg/.conf suffixes are ambiguous | [Python ConfigParser](https://docs.python.org/3/library/configparser.html) |
| java | Programs and libraries organized in compilation units | [Java language specification](https://docs.oracle.com/javase/specs/jls/se25/html/jls-7.html) |
| javascript | Programs/modules; JSX adds component markup | [ECMAScript overview](https://tc39.es/ecma262/multipage/overview.html), [React JSX explanation](https://react.dev/learn/writing-markup-with-jsx) |
| julia | Numerical computing and other programs | [Julia project](https://julialang.org/) |
| kotlin | Android, server and multiplatform application code/scripts | [Kotlin syntax](https://kotlinlang.org/docs/basic-syntax.html), [Kotlin platform overview](https://kotlinlang.org/docs/multiplatform/supported-platforms.html) |
| latex | Typesetting source with supporting classes and styles | [LaTeX introduction](https://www.latex-project.org/about/), [LaTeX documentation](https://www.latex-project.org/help/documentation/) |
| less | Stylesheet source processed into CSS | [Less documentation](https://lesscss.org/) |
| lua | Embedded application and game scripting | [Lua overview](https://www.lua.org/about.html) |
| makefile | Dependency rules and commands for updating derived files | [GNU Make manual](https://www.gnu.org/software/make/manual/make.html) |
| markdown | Lightweight formatted documentation and notes | [CommonMark specification](https://spec.commonmark.org/0.31.2/) |
| matlab-script | Computation/analysis commands; .m is shared with another language | [MathWorks scripts](https://www.mathworks.com/help/matlab/matlab_prog/create-scripts.html) |
| notebook | Code, explanatory cells and saved outputs | [Jupyter notebook format](https://nbformat.readthedocs.io/en/latest/format_description.html) |
| perl | Programs and reusable modules | [Perl reference](https://perldoc.perl.org/perl), [Perl introduction](https://perldoc.perl.org/perlintro) |
| php | Server-side programs and web templates | [PHP syntax](https://www.php.net/manual/en/language.basic-syntax.php), [PHP and HTML](https://www.php.net/manual/en/faq.html.php) |
| powershell | Scripts, reusable modules and data/module settings are different roles | [Scripts](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_scripts), [modules](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_modules), [data files](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_data_files?view=powershell-7.6) |
| properties | Named configuration values | [Java Properties](https://docs.oracle.com/en/java/javase/25/docs/api/java.base/java/util/Properties.html) |
| python | Application/automation code versus .pyi type interfaces | [Python source reference](https://docs.python.org/3/reference/lexical_analysis.html), [Python typing specification](https://typing.python.org/en/latest/spec/distributing.html) |
| r | Statistical analysis and graphics | [R language definition](https://cran.r-project.org/doc/manuals/r-release/R-lang.html) |
| ruby | Application code, automation tasks and package definitions | [Ruby syntax](https://docs.ruby-lang.org/en/master/syntax_rdoc.html), [Rake tasks](https://ruby.github.io/rake/doc/rakefile_rdoc.html), [RubyGems specifications](https://guides.rubygems.org/specification-reference/) |
| rust | Application and library source | [Rust reference](https://doc.rust-lang.org/reference/) |
| sass | SCSS and indented Sass are separate CSS-preprocessor syntaxes | [Sass syntax](https://sass-lang.com/documentation/syntax/) |
| scala | Applications/scripts across JVM, JavaScript and native runtimes | [Scala project](https://www.scala-lang.org/) |
| shell-script | Shell command sequences; interpreter dialects differ | [GNU Bash shell overview](https://www.gnu.org/software/bash/manual/html_node/What-is-a-shell_003f.html) |
| sql | Queries and schema/data operations; database dialects differ | [PostgreSQL SQL syntax](https://www.postgresql.org/docs/current/sql-syntax.html) |
| swift | Application and library source | [Swift overview](https://www.swift.org/about/) |
| typescript | Implementation versus type declarations, with optional JSX syntax | [Type declarations](https://www.typescriptlang.org/docs/handbook/2/type-declarations.html), [JSX](https://www.typescriptlang.org/docs/handbook/jsx.html) |
| vbscript | Automation source, without a claim of installed interpreter availability | [Microsoft VBScript reference](https://learn.microsoft.com/en-us/previous-versions/windows/internet-explorer/ie-developer/scripting-articles/d1wf56tt(v=vs.84)) |
| windows-url | A Windows URL shortcut; reading it does not open the destination | [Microsoft Internet shortcuts](https://learn.microsoft.com/en-us/windows/win32/lwef/internet-shortcuts) |

The changed IDs are `batch`, `bibtex`, `cmake`, `dart`, `dockerfile`, `environment`,
`fortran`, `haskell`, `html`, `ini`, `javascript`, `julia`, `kotlin`, `lua`,
`powershell`, `python`, `ruby`, `scala`, `swift` and `typescript`.

HTML and ECMAScript single-page references exceeded the retrieval tool's limits;
the catalog now links their divided documentation. The Swift language-book page
provided no substantive retrieved content; its project overview supports the
retained purpose. GNU Make/Bash, PowerShell data-file, PHP/HTML, Kotlin platform
and Haskell literate-purpose passages were available through primary-site search
text where direct retrieval failed or returned incomplete material. Those results
do not prove old URLs are broken. Other alternate overview pages supplied clearer
purpose evidence than syntax tables alone.


Scope and verification
----------------------

This review does not certify every alias, language version or dialect. CommonMark
does not define every Markdown variant; Node.js describes its own .env grammar;
ConfigParser does not establish every INI/CFG/CONF syntax. Bash documentation does
not certify Zsh compatibility. HTML/XHTML serialization differences, JavaScript
module conventions and every legacy suffix remain separate variant work. The
existing .h, .m and .ts ambiguities remain intact. The TypeScript purpose retains
the previously recorded video-stream alternative; this review does not add video
identification evidence.

No syntax validation, static program analysis, dependency discovery, compiler
support or executable capability is added. Type declarations and data files are
not all executable scripts. Conversely, calling a file configuration data is not
a safety determination: some tools evaluate expressions while loading it. Generic
text/JSON/XML inspection remains separate from a claimed language or application.

A before/after JSON comparison confirms that only `commonUses`, `source` and the
revision changed, with all 243 records retained. The reviewed set is exactly the
39 Source code and 10 Configuration or system data records. Its delta is retained
in `.codex-temp/catalog-source-code-delta.json`. Existing catalog schema/loading,
lookup and capability-separation contracts provide automated verification;
duplicate tests of the new prose were not added.

All **2,243 foundation contracts pass**; log:
`.codex-temp/catalog-source-code-foundation.log`. An independent comparison with
Git HEAD confirms the twenty-record delta and exact 49-record review-table scope.
Public-source boundary, system-theme policy, 95 documentation files and both
repositories' whitespace checks pass. No new production staging, native worker
matrix, visible UI, screen-reader, theme/DPI or installed-shell acceptance is
claimed for this description change.
