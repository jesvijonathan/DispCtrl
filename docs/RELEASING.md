# Building and releasing

## What ships

| Artifact | Built by | For |
|---|---|---|
| `DispCtrl-<v>-<channel>-win-x64-setup.exe` | `build/Installer.ps1` (Inno Setup) | most people; also what winget installs |
| `DispCtrl-<v>-<channel>-win-x64-desktop.zip` | `build/Publish.ps1` | portable use |
| `DispCtrl-<v>-<channel>-win-x64-cli.zip` | `build/Publish.ps1` | `dispctrl.exe` and the engine, no window |
| `DispCtrl-<v>.0-x64.msix` | `build/Package.ps1` (MakeAppx) | the Microsoft Store |
| `SHA256SUMS.txt`, `SETUP-SHA256SUMS.txt`, `MSIX-SHA256SUMS.txt` | each script | checking a download; separate names so release assets do not collide |

Every one of them lands in a single folder, `artifacts/<channel>-<version>/`,
beside the `cli` and `desktop` folders they were made from. Publish replaces
that folder, and removes earlier builds unless given `-KeepOld`. Package stages
its copy of the desktop folder in temp and deletes it when packed.

Every package is self-contained .NET 10, ReadyToRun (the CLI starts in about
180 ms instead of 230, and much less cold), x64 only. Managed rather than Native
AOT: the WMI adapters have not been verified under AOT (see CLAUDE.md, Build).

## Building locally

Requires Windows 11 x64, the .NET 10 SDK and MinGW-w64 (`DISPCTRL_CXX` pointing
to `g++.exe`). The MSIX needs the Windows SDK. The installer needs Inno Setup 6
or 7 (`winget install JRSoftware.InnoSetup`, or `-Iscc` pointing to any
`ISCC.exe`; the `Tools.InnoSetup` NuGet package carries a portable one). Stop
the engine gracefully and close the app before rebuilding binaries they use.

```powershell
./build/Build.ps1 -Test
./build/Publish.ps1   -Version 0.1.0 -Channel beta            # into artifacts/beta-0.1.0
./build/Installer.ps1 -DesktopDirectory ./artifacts/beta-0.1.0/desktop -Version 0.1.0 -Channel beta
./build/Package.ps1   -DesktopDirectory ./artifacts/beta-0.1.0/desktop `
  -IdentityName '<Partner Center identity name>' -Publisher '<Partner Center publisher CN>' -Version 0.1.0.0
./build/ReleaseNotes.ps1 -Version 0.1.0 -ArtifactsDirectory ./artifacts/beta-0.1.0
```

### Signing

`Publish.ps1 -Sign`, `Installer.ps1 -Sign` and `Package.ps1 -Sign` all go through
`build/Sign.ps1`, which reads the certificate from `DISPCTRL_SIGN_PFX` and
`DISPCTRL_SIGN_PASSWORD`. It reads environment variables rather than parameters
so the password never appears on a command line. Publish signs DispCtrl's own
binaries only. It does **not** sign the taskbar-glass helper: Explorer pins the
revision it has loaded, so the helper's bytes must not change after its build.

An MSIX can only be signed by a certificate whose subject equals the manifest's
`Publisher`. The release workflow checks this and leaves the MSIX unsigned when
they differ. Unsigned is right for Partner Center, which signs on ingestion.
Sideloading needs a signed one.

## The installer

Per user, unelevated, into `%LOCALAPPDATA%\Programs\DispCtrl`. Per user by
design: an elevated engine is cut off from Explorer and from every client, and
nothing else needs administrator rights.

- **Before copying**, it stops DispCtrl processes running *from the install
  folder*. The engine is stopped with `DispCtrl.Engine.exe stop`, and the
  installer waits up to 20 s for it to exit. The window and CLI are killed.
  Restart Manager is off (`CloseApplications=no`), because it would kill the
  engine and strand a hidden taskbar off-screen. If the engine will not stop,
  Setup refuses rather than overwrite it.
- **After copying**, it registers the sign-in task with
  `dispctrl startup set --engine on` (the *Start at sign-in* task, ticked by
  default) and starts the engine. It can optionally add a desktop shortcut and
  a `PATH` entry. The Start menu shortcut has the name the app itself manages,
  `DispCtrl.lnk`, so the Settings switch and the installer agree.
- **Uninstall** stops the engine the same way, then removes the sign-in task
  and the `PATH` entry. Settings in `%LOCALAPPDATA%\DispCtrl` are kept, as is
  the gamma-range registry value, which is a Windows setting.
- The sign-in task is removed only **when it starts the engine in the install
  folder**, whether by unticking the box or by uninstalling. A portable or
  development copy may own it, and must not be switched off by someone else's
  uninstaller.
- **Never change `AppId`** in `packaging/installer/DispCtrl.iss`: it is how an
  upgrade finds the installed copy.

## Continuous integration

`build.yml` runs on every push and pull request:
1. Build, and run the hardware-free checks.
2. A ReadyToRun publish of both bundles.
3. The installer.
4. An MSIX with the development identity.

Everything is uploaded as a 14-day artifact, so a publish or packaging break is
caught by the commit that causes it. `devices.yml` validates the device library.

## Cutting a release

1. Move the **Unreleased** entries in `CHANGELOG.md` under a new
   `## [x.y.z] - date` heading and update the links at the bottom.
2. Set `<DispCtrlVersion>` in `Directory.Build.props` to `x.y.z`. A stable tag
   that disagrees with it fails the release; a beta only warns.
3. Commit, then tag and push: `git tag v0.2.0 && git push origin v0.2.0`. A
   hyphen makes it a beta and a GitHub prerelease: `v0.2.0-beta.1`.
4. `release.yml` builds, tests, signs (if configured) and packages everything,
   then opens a **draft** release. Its notes are the CHANGELOG section for the
   tag (the exact version, then its numeric part, then Unreleased), followed by
   install instructions and every checksum. Re-running the same tag replaces
   the draft's assets.
5. Test the draft's files:
   - on a clean user account: install, first launch, tray, hotkeys, sign-in start;
   - upgrade over the previous version with the engine running (taskbars must
     come back and go away again);
   - uninstall;
   - the CLI: stdout and exit codes;
   - mixed-DPI displays, and an Explorer restart.
6. **Publish** the draft. For a stable release that starts `distribute.yml`.
   Prereleases stay on GitHub.

`distribute.yml` has two jobs, each inert until its variable is set:

- **winget**: `wingetcreate update` with the published installer's URL, which
  opens a pull request against `microsoft/winget-pkgs`. The **first** version
  must be submitted by hand (`wingetcreate new <installer url>`, package id
  `JesviJonathan.DispCtrl`). Winget moderators review every pull request.
- **Microsoft Store**: downloads the release's MSIX, refuses it unless its
  identity is `STORE_IDENTITY`, and submits it with the Store Developer CLI
  (`msstore publish --inputFile ... --appId ...`). It runs in the
  `microsoft-store` environment, so give that environment required reviewers:
  a submission cannot be recalled once certification starts. The job has not
  yet run against a real Partner Center account.

## One-time setup

| Where (Settings > Secrets and variables > Actions) | Name | What |
|---|---|---|
| secret | `SIGNING_CERT_BASE64` | base64 of a code-signing `.pfx`; optional |
| secret | `SIGNING_CERT_PASSWORD` | its password |
| variable | `STORE_IDENTITY` | Partner Center > Product identity > `Package/Identity/Name` |
| variable | `STORE_PUBLISHER` | the same page's `Package/Identity/Publisher` (`CN=...`) |
| variable | `STORE_PRODUCT_ID` | the Store ID (`9N...`); enables the Store job |
| secret | `PARTNER_CENTER_TENANT_ID`, `_SELLER_ID`, `_CLIENT_ID`, `_CLIENT_SECRET` | an Entra app added to Partner Center (Account settings > User management > Microsoft Entra applications, Manager role) |
| variable | `WINGET_PACKAGE_ID` | `JesviJonathan.DispCtrl`; enables the winget job |
| secret | `WINGET_TOKEN` | a classic token with `public_repo`, for the winget-pkgs fork and pull request |
| environment | `microsoft-store` | add required reviewers |

Before the first Store submission:
- Reserve the name in Partner Center.
- Fill in the listing, age rating and privacy policy. The policy can be the
  README's Privacy section: DispCtrl makes no network calls.
- Declare `runFullTrust`. It is a restricted capability, and certification asks
  why: DispCtrl is a desktop app that drives monitors over DDC/CI and the
  taskbar through the shell.

The first submission is best made by hand in Partner Center, with the MSIX from
a draft release. Automate the ones after it.

## Packaging notes

The manifest declares:
- the console alias `dispctrl.exe`;
- a disabled-by-default engine startup task;
- `runFullTrust`.

Windows controls alias conflicts and startup permissions. See Microsoft's
[packaging extensions documentation](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions).

Explorer pins the native glass helper: a different helper build needs Explorer
restarted. Check the glass status rather than assuming an old injected helper
has been replaced. Shell integration needs a packaged runtime and Store
certification testing. MakeAppx succeeding is not Store approval.

Beta and stable channels describe how a release is distributed. They do not
enable unfinished presets: that is `-p:EnableBetaPresets=true`.
