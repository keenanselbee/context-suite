Public And Proprietary Builds
============================

Status: accepted
Date: 2026-09-06


Context
-------

Context Suite serves both as a co-op portfolio project and a potential paid
Windows product. Most engineering should be reviewable publicly, while building
the complete commercial application requires private implementations.


Decision
--------

Keep the private `context-suite-private` repository checked out at
`proprietary/` inside the public repository. It has its own Git history and
remote. The parent ignores the entire directory; it is not a Git submodule.
Initially use direct project references rather than a private package feed.

Maintain one production application build using the public checkout and a
compatible private checkout. A public checkout alone cannot build the complete
application. Do not create a separate public review/demo edition, substitute
media engines for a demonstration app, or maintain a second licensing mode.

Portfolio evaluation is primarily source browsing, architecture documentation,
tests, screenshots, and demo video. People who want to run the application will
use the downloadable commercial trial when available. Public libraries and
their tests may build independently where their dependencies permit; this does
not require a separate application or a promise that every public project can
build alone. Test doubles belong in tests, not a shipping demonstration edition.

The production build is not implemented yet. The existing native shell prototype
continues to build independently through the documented commands.

Public code owns shared contracts, Explorer integration, presentation, batch
coordination, output safety, the DDS parser, and their tests. Private code owns
production media adapters, the built-in optimization policy catalog, and
commercial service integration, as refined in
[decision 0007](0007-production-ui-and-processes.md). Exact adapters and policies
must be listed when their engines are selected; this decision does not classify
third-party engines as proprietary code.

Public core projects depend on public interfaces, never private project types.
The application composition layer connects those interfaces to private
implementations. Missing private dependencies must produce a clear build error;
the build must never silently substitute demonstration implementations.


Consequences
------------

- Public CI checks documentation, source boundaries, and independently buildable
  public components and tests without private credentials. It does not claim to
  verify the complete application.
- Private release CI checks out compatible revisions of both repositories,
  tests the complete product, and records both commit IDs in release metadata.
- Public source archives use explicit inclusion rules and exclude private
  sources, private symbols with embedded source, and credentials. A Git ignore
  rule alone does not define the packaging boundary.
- Parent-repository checks must reject tracked paths beneath `proprietary/`.
- Git operations and agent instructions distinguish the two repositories;
  staging or committing the parent does not authorize committing the child.
- Payment credentials and release/license-signing private keys remain outside
  both repositories, in protected service or release configuration.
- Public-source license terms and private-code distribution terms remain open.
  Repository visibility alone does not define permission to reuse the code.
- Missing private functionality limits reproduction of the complete product;
  this is not a guarantee against replacement implementations or binary patches.


Alternatives Considered
-----------------------

- A separate public demonstration application: rejected because its substitute
  implementations, packaging, and test matrix add maintenance without serving
  the primary source-browsing portfolio workflow.
- A private package feed: defer until versioned package distribution is useful.
- A missing secret or trivial build check: does not retain meaningful private
  functionality and makes review unnecessarily difficult.
- Publishing the entire production implementation: does not meet the selected
  public/private source boundary.
