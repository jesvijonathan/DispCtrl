#!/usr/bin/env bash
# Builds the Linux release into artifacts/linux-<version>/ (cleared first):
#
#   dispctrl-linux-<version>-linux-x64.tar.gz   self-contained, with install.sh
#   dispctrl-linux_<version>_amd64.deb          for Debian and Ubuntu
#   dispctrl-linux-<version>-SHA256SUMS.txt
#
# Self-contained: no .NET on the target. Run through ./build.cmd linux-package.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
here="$repo/build/packaging/linux"
version="${DISPCTRL_VERSION:-$(sed -n 's:.*<DispCtrlVersion>\(.*\)</DispCtrlVersion>.*:\1:p' "$repo/Directory.Build.props")}"
[[ -n $version ]] || { echo "No DispCtrlVersion in Directory.Build.props" >&2; exit 1; }

out="$repo/artifacts/linux-$version"
work="$out/work"
rm -rf "$out"
mkdir -p "$work"

lib="$work/lib"
echo "== publish $version"
# Both into one folder: they share one runtime, which halves the download.
# ReadyToRun for the command line, which starts on every command.
dotnet publish "$repo/src/DispCtrl.Linux/DispCtrl.Linux.csproj" -c Release -r linux-x64 \
  --self-contained true -p:PublishReadyToRun=true -p:Version="$version" -o "$lib" -v q --nologo
dotnet publish "$repo/src/DispCtrl.Linux.Gui/DispCtrl.Linux.Gui.csproj" -c Release -r linux-x64 \
  --self-contained true -p:Version="$version" -o "$lib" -v q --nologo
rm -f "$lib"/*.pdb
# dotnet publish leaves mixed modes; packages want 0644, and 0755 on what runs.
find "$lib" -type f -exec chmod 0644 {} +
chmod 0755 "$lib/dispctrl-linux" "$lib/dispctrl-linux-gui"
[[ -f "$lib/createdump" ]] && chmod 0755 "$lib/createdump"
"$lib/dispctrl-linux" version | grep -qx "dispctrl-linux $version" \
  || { echo "The published command line does not report $version" >&2; exit 1; }

# ---- tarball: the same files install.sh expects next to it
name="dispctrl-linux-$version-linux-x64"
tree="$work/$name"
mkdir -p "$tree/share/applications" "$tree/share/icons/hicolor/256x256/apps" "$tree/systemd" "$tree/udev"
cp -a "$lib" "$tree/lib"
cp "$here/desktop/dispctrl-linux.desktop" "$tree/share/applications/"
cp "$repo/src/DispCtrl.Linux.Gui/Assets/dispctrl.png" "$tree/share/icons/hicolor/256x256/apps/dispctrl-linux.png"
cp "$here/systemd/dispctrl-linux-engine.service" "$tree/systemd/"
cp "$here/udev/90-dispctrl-backlight.rules" "$tree/udev/"
install -m 0755 "$here/install.sh" "$tree/install.sh"
cp "$repo/LICENSE" "$tree/"
cp "$repo/docs/LINUX.md" "$tree/README.md"
chmod -R go-w "$tree"
echo "== $name.tar.gz"
tar -C "$work" --owner=0 --group=0 -czf "$out/$name.tar.gz" "$name"

# ---- .deb
deb="$work/deb"
mkdir -p "$deb/DEBIAN" "$deb/usr/lib" "$deb/usr/bin" "$deb/usr/lib/systemd/user" "$deb/usr/lib/udev/rules.d" \
         "$deb/usr/share/applications" "$deb/usr/share/icons/hicolor/256x256/apps" "$deb/usr/share/doc/dispctrl-linux"
cp -a "$lib" "$deb/usr/lib/dispctrl-linux"
ln -s ../lib/dispctrl-linux/dispctrl-linux "$deb/usr/bin/dispctrl-linux"
ln -s ../lib/dispctrl-linux/dispctrl-linux-gui "$deb/usr/bin/dispctrl-linux-gui"
cp "$here/systemd/dispctrl-linux-engine.service" "$deb/usr/lib/systemd/user/"
cp "$here/udev/90-dispctrl-backlight.rules" "$deb/usr/lib/udev/rules.d/"
cp "$here/desktop/dispctrl-linux.desktop" "$deb/usr/share/applications/"
cp "$repo/src/DispCtrl.Linux.Gui/Assets/dispctrl.png" "$deb/usr/share/icons/hicolor/256x256/apps/dispctrl-linux.png"
cp "$repo/LICENSE" "$deb/usr/share/doc/dispctrl-linux/copyright"
size=$(du -sk "$deb/usr" | cut -f1)
# Debian sorts "~" before anything, so 0.2.2~beta.1 comes before 0.2.2; a
# hyphen would read as a Debian revision and sort after it.
debversion="${version//-/\~}"
sed -e "s/@VERSION@/$debversion/" -e "s/@SIZE@/$size/" "$here/deb/control" > "$deb/DEBIAN/control"
install -m 0755 "$here/deb/postinst" "$here/deb/prerm" "$here/deb/postrm" "$deb/DEBIAN/"
chmod -R go-w "$deb"
find "$deb" -type d -exec chmod 0755 {} +
echo "== dispctrl-linux_${version}_amd64.deb"
fakeroot dpkg-deb --build -Zxz "$deb" "$out/dispctrl-linux_${version}_amd64.deb" >/dev/null

rm -rf "$work"
(cd "$out" && sha256sum -- *.tar.gz *.deb > "dispctrl-linux-$version-SHA256SUMS.txt")
echo; ls -lh "$out"
