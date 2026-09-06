Foundation Fixture Provenance
============================

The managed contract harness creates a small UTF-8 text file under the supplied
repository-local scratch directory and removes it in `finally`. Its complete
content is authored in `tests/ContextSuite.Core.ContractTests/Program.cs`.
It is deliberately not media. No conversion or file-analysis success is inferred
from this fixture; it tests selection, transport, and lifecycle contracts only.

No third-party fixtures or assets are redistributed by these tests. DDS and
image fixtures, provenance, and semantic expectations belong to their later
implementation milestones.
