Commit Style
============

This repository uses concise Conventional Commit-inspired messages. Use the
shortest message that accurately explains the change and split work when changes
are independently useful or revertible.


Subject Format
--------------

Use one of these forms:

```text
type(scope): summary
type: summary
type(scope)!: summary
type!: summary
```

- Use a lowercase type and scope.
- Write the summary as a present-tense action without a trailing period.
- Keep the scope optional; use it when it identifies a meaningful product or
  technical area.
- Use `!` only for an intentional breaking change to a public API, persisted
  format, configuration contract, or supported workflow.


Types
-----

| Type | Use for |
| --- | --- |
| `feat` | New user-facing behavior or capability |
| `fix` | Correctness or reliability repair |
| `refactor` | Internal restructuring without intended behavior changes |
| `docs` | Documentation-only changes |
| `style` | Formatting-only changes |
| `test` | Tests, fixtures, and test infrastructure |
| `perf` | Measurable performance improvements |
| `build` | Dependencies, build scripts, packaging, or produced artifacts |
| `ci` | Continuous integration and release automation |
| `chore` | Repository maintenance that fits no more specific type |


Scopes
------

Prefer stable domain scopes over filenames. Likely scopes include:

```text
app
analyzer
converter
optimizer
core
images
audio
metadata
queue
presets
settings
shell
cli
docs
tests
build
ci
```

Leave the scope off for a genuinely repository-wide change or when a scope adds
no useful information.


Commit Bodies
-------------

A subject-only commit is correct when the subject says enough. Add a body when
the change is substantial, risky, cross-cutting, or difficult to infer from the
diff.

When a body is useful:

- Leave one blank line after the subject.
- Prefer short bullets beginning with verbs.
- Explain behavior, important tradeoffs, compatibility effects, and
  verification-relevant details.
- Do not narrate every edited file.


Grouping
--------

- Keep unrelated changes in separate commits.
- Keep tests with the behavior they verify unless the test infrastructure is
  independently useful.
- Keep documentation with the behavior it documents when both form one coherent
  change.
- Keep intentionally tracked generated output with the source or configuration
  that produced it.
- Do not make standalone formatting or generated-output commits unless that is
  the requested change.


Examples
--------

```text
docs: establish suite design
feat(analyzer): report DDS compression details
feat(optimizer): add lossless PNG optimization
feat(converter): add PNG-to-JPEG conversion
fix(metadata): preserve orientation when stripping EXIF
fix(converter): require a matte for transparent JPEG output
feat(settings)!: replace the preset schema
```
