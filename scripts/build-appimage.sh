#!/usr/bin/env bash
# Build Impasto-x86_64.AppImage from a self-contained linux-x64 publish.
#
# GirCore dlopen's the GTK4 stack by soname at runtime, so nothing in the
# apphost's ELF dependency graph references it. This names libgtk-4 and
# libadwaita-1 explicitly and lets linuxdeploy pull their transitive closure,
# then adds the pieces linuxdeploy cannot see on its own: gdk-pixbuf image
# loaders, the compiled GSettings schemas libadwaita reads at startup, and the
# Adwaita icon theme GTK widget chrome draws from.
#
# Host expectations (a Debian/Ubuntu CI runner, or any distro with equivalents):
# GTK4 + libadwaita runtimes, gsettings-desktop-schemas, adwaita-icon-theme,
# the gdk-pixbuf loaders and gdk-pixbuf-query-loaders, dotnet, wget.
#
# ponytail: fetches linuxdeploy and appimagetool from their rolling "continuous"
# builds because neither ships tagged releases. Pin to a mirrored copy if a
# reproducible build matters more than tracking upstream fixes.
set -euo pipefail

repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
work=${1:-$repo/appimage-build}
tools=$work/tools
publish=$work/publish
appdir=$work/AppDir

version=$(sed -n 's/^AC_INIT(\[impasto\], \[\(.*\)\])/\1/p' "$repo/configure.ac")
[ -n "$version" ] || { echo "could not read version from configure.ac" >&2; exit 1; }

rm -rf "$appdir" "$publish"
mkdir -p "$appdir/usr/bin" "$appdir/usr/share/applications" \
         "$appdir/usr/share/metainfo" "$appdir/usr/share/glib-2.0/schemas" \
         "$appdir/usr/share/icons" "$tools"

# --- 1. Publish --------------------------------------------------------------
# Staged outside the AppDir: linuxdeploy (step 6) resolves dependencies for
# every ELF it finds under the AppDir, and .NET's optional tracing shims pull
# libraries a plain host does not have (liblttng-ust). The full tree is folded
# into usr/bin after linuxdeploy has run; the .NET host probes its own directory
# for the runtime, so no rpath help is needed.
dotnet publish "$repo/Impasto/Impasto.csproj" -c Release -r linux-x64 \
  --self-contained true -p:PublishDir="$publish/"
find "$publish" -name '*.pdb' -delete

# --- 2. Desktop entry + AppStream metainfo ---------------------------------
# The same de-intltool transforms the flatpak manifest applies; the launcher is
# the apphost itself, reachable inside the image as "Impasto".
sed -e 's/^_//' -e 's/^TryExec=.*/TryExec=Impasto/' -e 's/^Exec=.*/Exec=Impasto %F/' \
  "$repo/xdg/com.github.zbcoding.Impasto.desktop.in" \
  > "$appdir/usr/share/applications/com.github.zbcoding.Impasto.desktop"
sed 's|<\(/\?\)_|<\1|g' "$repo/xdg/com.github.zbcoding.Impasto.metainfo.xml.in" \
  > "$appdir/usr/share/metainfo/com.github.zbcoding.Impasto.metainfo.xml"

# --- 3. Icons -------------------------------------------------------------
# The app ships its own hicolor tree in the publish output; GTK chrome needs
# Adwaita on top.
cp -r "$publish/icons/hicolor" "$appdir/usr/share/icons/"
[ -d /usr/share/icons/Adwaita ] && cp -r /usr/share/icons/Adwaita "$appdir/usr/share/icons/"
icon=""
for size in 512x512 256x256 128x128 96x96 64x64 48x48; do
  f=$appdir/usr/share/icons/hicolor/$size/apps/com.github.zbcoding.Impasto.png
  [ -f "$f" ] && { icon=$f; break; }
done
[ -n "$icon" ] || icon=$appdir/usr/share/icons/hicolor/scalable/apps/com.github.zbcoding.Impasto.svg
[ -f "$icon" ] || { echo "app icon missing from publish output" >&2; exit 1; }

# --- 4. GSettings schemas -----------------------------------------------
# libadwaita reads org.gnome.desktop.* at startup; ship the host's compiled set.
cp /usr/share/glib-2.0/schemas/gschemas.compiled \
  "$appdir/usr/share/glib-2.0/schemas/"

# --- 5. gdk-pixbuf loaders --------------------------------------------
# SVG icon rendering and file-chooser thumbnails go through these plugins. On
# Debian/Ubuntu each is a separate dlopen'd .so - bundle the set and repoint the
# cache at the AppDir copy. Distros that compile the common loaders into
# libgdk_pixbuf itself (e.g. Arch) have nothing to copy; GTK uses those
# built-ins and the AppRun leaves the pixbuf env vars unset.
dest_moduledir=$appdir/usr/lib/gdk-pixbuf-2.0/2.10.0/loaders
png_loader=$(find /usr/lib -name 'libpixbufloader-png.so' -print -quit 2>/dev/null || true)
bundled_loaders=0
if [ -n "$png_loader" ]; then
  src_moduledir=$(dirname "$png_loader")
  mkdir -p "$dest_moduledir"
  cp "$src_moduledir"/*.so "$dest_moduledir/"
  GDK_PIXBUF_MODULEDIR=$dest_moduledir gdk-pixbuf-query-loaders \
    > "$appdir/usr/lib/gdk-pixbuf-2.0/2.10.0/loaders.cache"
  sed -i "s|$dest_moduledir/|loaders/|" \
    "$appdir/usr/lib/gdk-pixbuf-2.0/2.10.0/loaders.cache"
  bundled_loaders=1
fi

# --- 6. Bundle native libraries -----------------------------------------
wget -qO "$tools/linuxdeploy" \
  https://github.com/linuxdeploy/linuxdeploy/releases/download/continuous/linuxdeploy-x86_64.AppImage
wget -qO "$tools/appimagetool" \
  https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x "$tools/linuxdeploy" "$tools/appimagetool"
export APPIMAGE_EXTRACT_AND_RUN=1   # CI runners have no FUSE

# libgtk-4 / libadwaita-1: the runtime GirCore dlopen's. The pixbuf loader .so
# files and their codec deps (webp, rsvg, ...) are dlopen'd too, so each is
# listed for its transitive closure. .NET ships no ICU and dlopen's the host's
# OpenSSL for TLS (the add-in gallery, update checks) - add both.
libs=()
for stem in libgtk-4.so.1 libadwaita-1.so.0 \
            libicui18n.so libicuuc.so libicudata.so \
            libssl.so.3 libcrypto.so.3; do
  # awk reads all input (no early exit) so ldconfig never takes SIGPIPE under
  # `set -o pipefail`.
  path=$(ldconfig -p | awk -v s="$stem" 'index($1, s)==1 && !seen {print $NF; seen=1}')
  [ -n "$path" ] && libs+=(--library "$path")
done
if [ "$bundled_loaders" = 1 ]; then
  for so in "$dest_moduledir"/*.so; do libs+=(--library "$so"); done
fi

"$tools/linuxdeploy" --appdir "$appdir" \
  --executable "$publish/Impasto" \
  --desktop-file "$appdir/usr/share/applications/com.github.zbcoding.Impasto.desktop" \
  --icon-file "$icon" \
  "${libs[@]}"

# .NET's ICU shim probes unversioned sonames first; linuxdeploy keeps only the
# versioned files it resolved.
for stem in libicui18n libicuuc libicudata; do
  real=$(find "$appdir/usr/lib" -maxdepth 1 -name "$stem.so.*" -print -quit)
  [ -n "$real" ] && ln -sf "$(basename "$real")" "$appdir/usr/lib/$stem.so"
done

# Fold the rest of the .NET publish in beside the apphost linuxdeploy placed.
cp -a "$publish/." "$appdir/usr/bin/"

# --- 7. Third-party licenses ---------------------------------------------
# The GTK/GLib/Pango/gdk-pixbuf/librsvg stack is LGPL: shipping the binaries is
# fine as long as the notices travel with them and the user can swap a bundled
# .so for their own (they can - unpack the AppImage, replace usr/lib/<lib>,
# repack). Copy each bundled library's distro copyright file when the host is
# Debian-family; always leave a pointer to unmodified upstream sources.
docdir=$appdir/usr/share/doc/impasto
mkdir -p "$docdir/third-party"
if command -v dpkg-query >/dev/null 2>&1; then
  find "$appdir/usr/lib" -maxdepth 1 -name '*.so*' -type f -printf '%f\n' \
  | while read -r lib; do
      pkg=$(dpkg-query -S "*/$lib" 2>/dev/null | awk -F: 'NR==1 {print $1}') || continue
      src=/usr/share/doc/$pkg/copyright
      [ -n "$pkg" ] && [ -f "$src" ] && cp "$src" "$docdir/third-party/$pkg.copyright"
    done
fi
cat > "$docdir/THIRD-PARTY.AppImage.md" <<'EOF'
# Bundled libraries

This AppImage carries the GTK 4 / libadwaita runtime that Impasto loads at run
time, plus its supporting stack (GLib, Pango, Cairo, gdk-pixbuf, Graphene,
HarfBuzz, librsvg, FreeType, Fontconfig, pixman, ICU and the usual image
codecs), and the .NET runtime.

Every bundled library is an unmodified build taken from the Ubuntu 24.04
archive. Per-library license texts are in ./third-party/. Corresponding source
is the matching `deb-src` entry for Ubuntu 24.04 (noble); the .NET runtime is
MIT, https://github.com/dotnet/runtime.

The LGPL libraries are dynamically linked and kept as separate files under
usr/lib/ inside the image. To use your own build of one: extract the AppImage
(`./Impasto-x86_64.AppImage --appimage-extract`), replace the file in
`squashfs-root/usr/lib/`, and repack with `appimagetool squashfs-root`.
EOF

# --- 8. AppRun + pack --------------------------------------------------
# linuxdeploy left AppRun as a symlink to usr/bin/Impasto; drop it so the
# heredoc writes a real file instead of overwriting the apphost through it.
rm -f "$appdir/AppRun"
cat > "$appdir/AppRun" <<'EOF'
#!/bin/sh
here=$(dirname "$(readlink -f "$0")")
export LD_LIBRARY_PATH="$here/usr/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export GSETTINGS_SCHEMA_DIR="$here/usr/share/glib-2.0/schemas"
export XDG_DATA_DIRS="$here/usr/share:${XDG_DATA_DIRS:-/usr/local/share:/usr/share}"
loader_cache="$here/usr/lib/gdk-pixbuf-2.0/2.10.0/loaders.cache"
if [ -f "$loader_cache" ]; then
  export GDK_PIXBUF_MODULEDIR="$here/usr/lib/gdk-pixbuf-2.0/2.10.0/loaders"
  export GDK_PIXBUF_MODULE_FILE="$loader_cache"
fi
exec "$here/usr/bin/Impasto" "$@"
EOF
chmod +x "$appdir/AppRun"

# Guard against the apphost being clobbered (e.g. a heredoc following the AppRun
# symlink linuxdeploy leaves) - the AppImage would still build and only fail when
# a user runs it.
file "$appdir/usr/bin/Impasto" | grep -q ELF \
  || { echo "usr/bin/Impasto is not an ELF binary" >&2; exit 1; }
file "$appdir/AppRun" | grep -q 'shell script' \
  || { echo "AppRun is not a script" >&2; exit 1; }

ARCH=x86_64 "$tools/appimagetool" "$appdir" "$repo/Impasto-x86_64.AppImage"
echo "built: $repo/Impasto-x86_64.AppImage  (version $version)"
