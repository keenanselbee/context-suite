Milestone 1 Foundation Validation
================================

Local verification: September 6, 2026. This covers the bounded production
foundation goal, not media processing or readiness for a paid release.
This is historical foundation evidence. The subsequent
[conversion goal](image-conversion-goal.md) records real media processing, trial
admission and the current test counts; the original empty catalog below is no
longer the current product state.

| Requirement | Evidence |
| --- | --- |
| WPF application, core, worker, private composition | Debug and Release production builds pass with no warnings; all projects appear in `ContextSuite.Production.slnx` |
| Complete selections and repeated activation | Native contracts cover all three commands and three-file handoff; managed framing covers 4,096 paths; real WPF process smoke accepts three successive three-file requests through the shipping entry point |
| One app per interactive session | First-instance pipe ownership, forwarding, duplicate IDs, and actual short-lived forwarding processes are tested |
| Bounded IPC and queue | Oversized/truncated frames, malformed clients, protocol version, peer checks, queue overflow, and slow acknowledgement readers are tested |
| On-demand worker and cancellation | Real private catalog connection, reuse, cancellation, graceful client disposal, crash recovery, and abrupt parent-exit cleanup are tested |
| No fake processing or shipping access bypass | Private catalog is empty; UI reports unsupported operations; source bytes remain unchanged; access states have no shipping provider |
| Public-only development | A copied public source tree without `proprietary/` passes component tests; both production projects reject a missing private implementation |
| Native shell preservation | Debug and Release prototype COM/manifest contracts pass; test execution does not change registration. Development packages were later explicitly switched to WPF for the manual smoke |
| Manual classic Explorer-to-WPF handoff | User screenshots confirm root icons, direct Analyze, Convert/Optimize submenu arrows, complete three-file selections for all tools, and 12 rows across four batches in one window (Optimize, Optimize, Convert, Analyze) |
| Repeatable commands and CI | Build/test/boundary scripts and public/private workflows are authored; their local commands pass; hosted workflows have not been published/run |

The complete managed integration run passes 50 contracts. The Windows process
smoke deliberately exercises the normal executable rather than a special demo
or permissive licensing mode. It caught an acknowledgement-loss race that was
fixed with a final client receipt before the server disconnects; a slow-reader
contract now covers that behavior explicitly.

Follow-up desktop automation now verifies WPF row contents for all three tools,
single-window reuse, basic keyboard focus, resize, graceful close/reopen, and
unchanged sources. See [desktop smoke tests](desktop-smoke-tests.md) for commands,
Explorer-mode limitations, and retained evidence. Automated Explorer mode still
has not passed end to end; the manual screenshot evidence is a separate pass.
Broader visual/accessibility review, modern Explorer menu presentation, high-DPI
layout, and screen-reader UX remain outstanding. Production installer registration and
media-engine execution remain later work. See [development](development.md) for
commands, protocol limits, temporary-fixture handling, and the private CI boundary.
