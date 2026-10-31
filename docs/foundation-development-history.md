Foundation Development History
==============================

Historical foundation-era process/UI evidence. These observations describe the
former workspace, private build boundary, and then-current UI, not current
commands, test counts, or release acceptance. See [development](development.md)
and [the transition goal](free-access-goal.md) for current guidance and status.


Original Verification
---------------------

The original foundation evidence comprised Release builds and 50 managed
contracts; the completed conversion goal now records 280 passing integration
contracts and expanded engine/UI evidence. Foundation coverage includes full
selection transport, malformed clients, duplicate IDs, queue bounds, cancellation,
worker reuse, and restart after cancellation/crash. Native Release contracts also
pass for all three commands and complete three-file selections. Both production
projects fail with the documented error when private implementations are absent.
A public-only snapshot without `proprietary/` also passes component contracts.
The actual WPF process smoke verifies startup, three successive three-file
activations routed into one owner, request consumption, an on-demand worker,
unchanged sources, and worker exit after abrupt parent termination. The smoke
uses temporary GUID request files in the normal local activation directory and
removes them afterward; it does not install packages or automate Explorer UI.

The computer-use helper was unavailable during foundation implementation.
The later [desktop smoke harness](desktop-smoke-tests.md) adds real WPF UIA
assertions and an optional installed Explorer path. The WPF smoke has passed
locally, including row contents, focus, resize, and close/reopen. Production
Explorer automation is experimental and has not passed end to end. Subsequent
user-provided screenshots confirm the manual classic Explorer-to-WPF handoff:
all three tools receive complete three-file selections and accumulate 12 rows
across four batches in one window. The local development shell
registration was explicitly switched for local smoke work after the original
foundation validation. Neither registration nor UI test success certifies a
release installer. Window appearance, modern menu icons/order, high-DPI layout,
and screen-reader usability still require manual review.
