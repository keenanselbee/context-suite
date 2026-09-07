Shell Activation Request
========================

Status: accepted
Date: 2026-09-05


Context
-------

Convert and Optimize must receive a multiple-file Explorer selection as one
coordinated batch. Passing every path on a command line creates quoting and size
limits, while media planning and validation must remain outside Explorer.


Decision
--------

For the shell prototype, `Invoke` writes one immutable UTF-8 request beneath the
current user's local application-data directory and launches the separate host
with only that request path. The line-oriented schema contains:

- A schema marker and unique request identifier.
- One stable operation and action identifier.
- An explicit path count.
- Selected absolute paths in Explorer order.

Requests are capped at 4 MiB and 4,096 paths, written to a unique temporary file,
flushed, and atomically renamed before launch. The host accepts normal activation
files only from the expected directory, caps reads, validates every field and
path, and removes the request after reading it.

The schema is a prototype boundary. Replacing it with app activation, named
pipes, or another production IPC model requires a superseding decision and must
retain the same one-selection, one-batch semantics.

Decision 0008 adds a non-media `settings` action for Convert and Optimize within
schema 1. It carries exactly zero paths and opens the corresponding settings
section without queue admission. All media actions still require a nonempty
selection. Deploy matching shell/host versions; older hosts reject the new action.


Consequences
------------

- Batch size is independent of the Windows command-line length.
- The host can reject malformed, oversized, unknown, relative, missing, or
  non-file activation data before planning.
- Crashes can leave bounded stale request files, so production work needs an
  age-based cleanup policy.
- The prototype needs no resident broker or service.
- Request confidentiality relies on the current user's local application-data
  boundary; production security and ACL verification remain required.


Alternatives Considered
-----------------------

- **One command line containing all paths:** rejected because large selections
  and quoting are unreliable.
- **One host launch per file:** rejected because it violates coordinated batch
  progress, cancellation, settings, and result semantics.
- **Named pipe broker:** plausible for the production application, but adds a
  lifecycle component that is unnecessary to answer the shell-layout question.
