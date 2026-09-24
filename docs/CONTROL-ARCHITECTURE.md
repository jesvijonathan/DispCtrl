# DispCtrl control architecture

Status: implementation in progress. This document distinguishes the target
architecture from shipped coverage; see CLI.md for executable commands.

## Public surfaces

The console CLI, native WinUI app and taskbar toolkit share operations, validation,
monitor identity and results. The CLI is a real console executable, not a GUI
executable that sometimes attaches a console. Existing commands remain compatible.
New features require CLI coverage, public documentation and appropriate tests.

DispCtrl.Control owns operations and the versioned local JSON protocol. Core owns
models and settings. Display owns Windows/hardware adapters. The engine hosts the
local broker and persistent policies. Clients prefer the broker when available;
standalone operations use the same implementation. No command retries a mutation
locally after a connection fails mid-request: its outcome is then unknown.

## Concurrency and state

Settings writes must be serialized across processes. UI edits merge changed fields
against the latest disk snapshot instead of overwriting unrelated external changes.
Operation results distinguish saved policy from verified hardware changes. All
clients use stable monitor tokens, with explicit ambiguity errors for labels.
Slow hardware calls are not part of ordinary cached status reads.

## Ordered apply target

1. Discover and match stable identities, validate the whole request.
2. Apply topology and re-enumerate, discarding stale handles.
3. Apply compatible resolution/refresh/orientation modes.
4. Apply layout/primary, then scaling; refresh identity handles as necessary.
5. Apply colour and brightness, then background policies.
6. Apply input-source and power-off actions last.
7. Verify and report per-step results, including absent/disconnected devices.

Dry runs use the same validation and planning path without mutations. Hardware
changes are not atomic; reports must say what succeeded before a failure. Do not
claim rollback where the monitor has gone away or cannot be contacted.

## Extensions and debugging

Stable JSON commands and event streams are the initial extension API. External
scripts remain isolated processes; no arbitrary DLL loading inside the engine.
Contributors can add operations behind the shared router and test fake adapters.
Diagnostic output belongs on stderr, machine-readable results on stdout. No
uploaded telemetry is needed for local debugging. Keep symbols in release artifacts.

## Delivery milestones

- [ ] Shared control API, complete settings access, strict validation and concurrency.
- [ ] Broad hardware/Windows commands, ordered batch apply, reconnection recovery.
- [ ] Broker, observable state, command parity in native app and taskbar toolkit.
- [ ] Script/event extension API, examples, diagnostic tooling and coverage matrix.
- [ ] Reproducible build, portable releases, beta/stable CI, MSIX packaging.
- [ ] Packaged installation/update/startup tests and Store certification checks.

Presets remain disabled until the shared apply pipeline is ready. Store acceptance,
package identity and signing credentials are external release requirements, not
something a successful local compile can establish.
