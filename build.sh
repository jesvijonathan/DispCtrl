#!/usr/bin/env bash
# DispCtrl on Linux, macOS or WSL: compile everything that is not the WinUI
# window, and run the checks that need no Windows API.
#
#   ./build.sh doctor      what is installed, what is missing
#   ./build.sh setup       .NET 10 SDK into .tools/dotnet (no sudo); prints the
#                          package commands for anything else
#   ./build.sh build       Core, Display, Control, CLI, engine and the checks
#   ./build.sh test        presetverify and devicecheck
#   ./build.sh clean       bin/ and obj/ folders
#
# Options: -c Debug|Release (default Release), --native (build the taskbar-glass
# helper with x86_64-w64-mingw32-g++; needs pwsh).
#
# What cannot happen here, and why: the window (DispCtrl.App) needs the WinUI
# XAML compiler, which loads Windows-only DLLs, and controlcheck, presetcheck
# and every command that touches a display call Windows itself. Building the
# installer and the MSIX needs Windows too. Use build.cmd there.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
tools="$repo/.tools"
command="${1:-help}"; [[ $# -gt 0 ]] && shift
configuration=Release
native=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    -c|--configuration) configuration="$2"; shift 2 ;;
    --native) native=true; shift ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
if [[ -x "$tools/dotnet/dotnet" ]]; then
  export DOTNET_ROOT="$tools/dotnet"; export PATH="$tools/dotnet:$PATH"
fi

have() { command -v "$1" >/dev/null 2>&1; }
has_icu() { ldconfig -p 2>/dev/null | grep -q libicuuc || ls /usr/lib/*/libicuuc.so* /usr/lib/libicuuc* /opt/homebrew/opt/icu4c/lib/libicuuc* >/dev/null 2>&1; }
sdk10() { have dotnet && dotnet --list-sdks 2>/dev/null | grep -E '^10\.' | tail -1; }
cxx() { echo "${DISPCTRL_CXX:-$(command -v x86_64-w64-mingw32-g++-posix || command -v x86_64-w64-mingw32-g++ || true)}"; }

check() { # name ok detail fix
  if [[ "$2" == 1 ]]; then printf '[ok]  %-22s %s\n' "$1" "$3"; else printf '[--]  %-22s %s\n      %s\n' "$1" "$3" "$4"; fi
}

doctor() {
  echo "DispCtrl developer check ($repo)"; echo
  local s; s="$(sdk10 || true)"
  check ".NET 10 SDK" "$([[ -n $s ]] && echo 1 || echo 0)" "${s:-not found}" "./build.sh setup"
  check "ICU" "$(has_icu && echo 1 || echo 0)" "$(has_icu && echo present || echo 'not found; .NET falls back to invariant mode')" "sudo apt install libicu-dev   (or: dnf install libicu, brew install icu4c)"
  local c; c="$(cxx)"
  check "MinGW-w64 (optional)" "$([[ -n $c ]] && echo 1 || echo 0)" "${c:-not found; engine builds without the glass helper}" "sudo apt install g++-mingw-w64-x86-64-posix   (then ./build.sh build --native)"
  check "pwsh (optional)" "$(have pwsh && echo 1 || echo 0)" "$(command -v pwsh || echo 'not found; needed only for --native')" "https://learn.microsoft.com/powershell/scripting/install/install-ubuntu"
  echo; echo "The window, the installer, the MSIX and the hardware checks need Windows: use build.cmd there."
}

setup() {
  if [[ -z "$(sdk10 || true)" ]]; then
    mkdir -p "$tools"
    echo "Installing the .NET 10 SDK into .tools/dotnet (for this repository only)..."
    curl -sSL https://dot.net/v1/dotnet-install.sh -o "$tools/dotnet-install.sh"
    bash "$tools/dotnet-install.sh" --channel 10.0 --install-dir "$tools/dotnet"
    export DOTNET_ROOT="$tools/dotnet"; export PATH="$tools/dotnet:$PATH"
  fi
  doctor
}

dotnet_env() {
  [[ -n "$(sdk10 || true)" ]] || { echo ".NET 10 SDK missing: ./build.sh setup" >&2; exit 1; }
  # Without ICU the SDK aborts at start-up; DispCtrl itself runs invariant anyway.
  has_icu || export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
}

build() {
  dotnet_env
  local glass=(-p:SkipTaskbarGlass=true)
  if [[ $native == true ]]; then
    [[ -n "$(cxx)" ]] || { echo "--native needs x86_64-w64-mingw32-g++" >&2; exit 1; }
    have pwsh || { echo "--native needs pwsh" >&2; exit 1; }
    export DISPCTRL_CXX="$(cxx)"; glass=()
  fi
  for p in src/DispCtrl.Core src/DispCtrl.Display src/DispCtrl.Control src/DispCtrl.Cli src/DispCtrl.Engine \
           tools/presetverify tools/devicecheck tools/controlcheck; do
    echo "== $p"
    dotnet build "$repo/$p" -c "$configuration" -v q --nologo -p:EnableWindowsTargeting=true "${glass[@]}"
  done
  echo; echo "Built everything but DispCtrl.App (Windows only). Binaries are win-x64: run them on Windows."
}

test_() {
  dotnet_env
  cd "$repo"
  dotnet run --project tools/presetverify -c "$configuration" --property:EnableWindowsTargeting=true
  dotnet run --project tools/devicecheck -c "$configuration" --property:EnableWindowsTargeting=true -- validate devices
  # Not "index --check": the index is regenerated after each merge, so a pull
  # request that adds a device is valid without it.
  dotnet run --project tools/devicecheck -c "$configuration" --property:EnableWindowsTargeting=true -- selftest
  echo; echo "Passed. controlcheck and presetcheck call Windows display APIs: run them with build.cmd test."
}

clean() {
  find "$repo/src" "$repo/tools" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
  echo "Clean."
}

case "$command" in
  doctor) doctor ;;
  setup) setup ;;
  build) build ;;
  test) test_ ;;
  clean) clean ;;
  help|-h|--help) sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//' ;;
  *) echo "Unknown command: $command (doctor, setup, build, test, clean)" >&2; exit 2 ;;
esac
