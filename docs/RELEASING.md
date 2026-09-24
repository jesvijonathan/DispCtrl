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
  -Version 0.1.0.0   # Store identity from AppxManifest.xml; -IdentityName/-Publisher override it
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

## Workflows

Six workflows, each with a summary page (links, files, sizes, status) and a
**Run workflow** button with options:

| Workflow | Runs on | Does |
|---|---|---|
| **Build and verify** (`build.yml`) | pushes and pull requests to the product | build, the hardware-free checks, then zips, installer and a development-identity MSIX; kept 7 days. Options: what to package, channel, version, native helper, days to keep. Website, device-library and docs changes do not start it. |
| **Release** (`release.yml`) | a `v*` tag, or by hand | everything below; options: version, channel, publish now or draft, create the tag, packages, signing, extra notes |
| **Distribute** (`distribute.yml`) | publishing a stable release, or by hand | winget and the Microsoft Store; options: tag, which targets, dry run |
| **Device library** (`devices.yml`) | changes under `devices/`, and issues carrying a device share | validate, regenerate the index after a merge, and turn a share into a pull request; by hand: validate, reindex, intake one issue or every open share without a pull request, self-test. A share whose pull request Actions may not open (setting 2 below) leaves its branch pushed and a link to open it in the run summary |
| **Website** (`pages.yml`) | changes under `site/` | publishes GitHub Pages |
| **Housekeeping** (`housekeeping.yml`) | Sundays, or by hand | deletes old artifacts, run records, unused caches and stale release drafts, and reports the space freed; a manual run is a dry run unless you untick it |

### Who can change them

Only accounts with write access can edit a workflow, and `.github/CODEOWNERS`
names the owner for everything, workflows included. Three settings finish the
job and are set once, by hand:

1. **Settings > Actions > General > Fork pull request workflows from outside
   collaborators: "Require approval for all outside collaborators".** A
   stranger's pull request then runs nothing until you approve it.
2. **Settings > Actions > General > Workflow permissions: "Read repository
   contents"**, with "Allow GitHub Actions to create and approve pull
   requests" ticked (the device intake opens pull requests). Each workflow
   asks for exactly what it needs on top.
3. **Settings > Rules > Rulesets > New branch ruleset** for `master`: block
   force pushes and deletion, and require a pull request with code-owner review
   for everyone except you (add yourself as a bypass actor). The index commit
   the Device library workflow pushes needs "GitHub Actions" in the bypass
   list too, or turn that step into a pull request.

A pull request from a fork always runs its workflows with a read-only token and
no secrets, and the Device library workflow's token cannot change files under
`.github/workflows/` at all, so an issue cannot rewrite the pipeline.

## Cutting a release

1. Move the **Unreleased** entries in `CHANGELOG.md` under a new
   `## [x.y.z] - date` heading and update the links at the bottom.
2. Set `<DispCtrlVersion>` in `Directory.Build.props` to `x.y.z`. A stable tag
   that disagrees with it fails the release; a beta only warns.
3. Commit, then tag and push: `git tag v0.2.0 && git push origin v0.2.0`. A
   hyphen makes it a beta and a GitHub prerelease: `v0.2.0-beta.1`.
4. **Release** builds, tests, signs (if configured) and packages everything,
   then opens a **draft** release. Started from Actions instead, it can create
   the tag itself and publish at once. Its notes are the CHANGELOG section for the
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
6. **Publish** the draft. For a stable release that starts **Distribute**
   (a release the workflow publishes itself starts it explicitly). Prereleases
   stay on GitHub.

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
| variable | `STORE_IDENTITY` | optional: overrides the Store identity in `build/packaging/AppxManifest.xml` |
| variable | `STORE_PUBLISHER` | optional, with `STORE_IDENTITY`: overrides its publisher (`CN=...`) |
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
