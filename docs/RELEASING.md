# Building and releasing

Requires .NET 10, Windows 11 x64 and MinGW-w64 (`DISPCTRL_CXX` points to g++.exe).
Stop the engine gracefully before rebuilding binaries it is using. Close the app.

```powershell
./build/Build.ps1 -Test
./build/Publish.ps1 -Version 0.1.0 -Channel beta
./build/Package.ps1 -DesktopDirectory ./artifacts/<build>/desktop `
  -IdentityName '<Partner Center identity name>' `
  -Publisher '<Partner Center publisher CN>' -Version 0.1.0.0
```

Publish produces self-contained CLI+engine and full desktop ZIPs, documentation,
examples, symbols, build metadata and SHA-256 checksums. Packages currently use
managed .NET: the WMI adapters have not been verified with NativeAOT. Beta/stable
channels describe release distribution; they do not enable unfinished presets.

Package copies a published desktop into a unique staging folder and validates it
with MakeAppx. It requires explicit identity parameters. The resulting MSIX is
unsigned. Use the exact Store identity for submission; local installation needs
a trusted signing certificate matching Publisher. No signing key is stored here.

The manifest includes the console alias `dispctrl.exe`, a disabled-by-default
engine startup task and `runFullTrust`. Windows controls alias conflicts and
startup permissions. See Microsoft's [packaging extensions documentation](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions).

Before release, test clean installation, CLI alias/stdout/exit codes, first launch,
startup opt-in, engine shutdown, upgrade and uninstall, mixed-DPI displays and
Explorer restart. Explorer pins the native glass helper: a different helper build
requires restarting Explorer. Check the glass status instead of assuming an old
injected helper has been replaced. Shell integration needs packaged runtime and
Store certification testing; MakeAppx success is not Store approval.

`build.yml` runs on every push and pull request: build, the hardware-free checks,
a ReadyToRun publish of both bundles and an MSIX pack with the development
identity, uploaded as a 14-day artifact - so a publish or packaging break is
caught by the commit that causes it. `release.yml` runs on `v*` tags (a tag with
a hyphen, such as `v0.2.0-beta.1`, is a beta and a prerelease) and on manual
dispatch, and prepares a draft release. Set secrets `SIGNING_CERT_BASE64` (a
base64 .pfx) and `SIGNING_CERT_PASSWORD` to sign the MSIX; the certificate
subject must equal `STORE_PUBLISHER`. Unsigned is right for Partner Center,
which signs on ingestion; sideloading needs the signed one.

Published executables are ReadyToRun: the CLI starts in about 180 ms instead of
230, and much less cold.

GitHub workflows build pull requests and prepare release drafts from version
tags, including an unsigned MSIX. Set repository variables `STORE_IDENTITY` and
`STORE_PUBLISHER` together to package with the reserved Partner Center identity;
without them, CI uses an explicitly developmental identity. The ZIP and MSIX
checksums have separate filenames to avoid release asset collisions.
They do not publish a public release or submit to Partner Center. A project
license still needs the maintainer's decision before open-source distribution.
