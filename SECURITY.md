# Security policy

## Supported versions

Only the latest release receives fixes. DispCtrl is in beta, so upgrade first and
check whether the issue remains.

## Reporting a vulnerability

Please **do not open a public issue.** Use GitHub's private reporting instead:
**Security > Report a vulnerability** on
[the repository](https://github.com/jesvijonathan/Display-Control/security/advisories/new).

Include what an attacker could do, the version (`dispctrl status`), and steps to
reproduce. You should hear back within a week. Once a fix is released, the
advisory is published with credit to you, unless you would rather stay unnamed.

## What is in scope

DispCtrl runs unelevated as the signed-in user, and it has some surfaces that
matter:

- **The engine's named-pipe command broker.** It is scoped to the current user and
  session. Any way for another user, another session, or a lower integrity level
  to send it commands is in scope.
- **Monitor records.** `dispctrl contribute` and `devices share` build text meant
  for publication. Any way for a serial number, a device path or instance id, a
  file path, or the account name to survive the scrub into that text is a
  vulnerability, and treated as one. See the "Publishing device records" section
  of [CLAUDE.md](CLAUDE.md).
- **Settings and device-definition parsing.** `settings.json`, presets and
  device definitions are read from disk, and some come from other people.
- **The elevated gamma-range write**, the only operation that asks for
  administrator rights.
- **The installer and the release pipeline.**

Physical access to the monitors and the DDC/CI bus is out of scope, as is a
process already running as the same user.
