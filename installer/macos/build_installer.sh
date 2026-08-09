#!/bin/sh
set -e

# ponytail: this fork has no Apple Developer ID, so the build is always
# unsigned and un-notarized - Gatekeeper needs a right-click > Open on first
# launch. Re-add codesign/notarytool here if the project ever gets its own
# certificate; do not reuse upstream Pinta's identity.
runtimeid=$1

if [ "$runtimeid" != "osx-x64" ] && [ "$runtimeid" != "osx-arm64" ]; then
    echo "Invalid runtime identifier (should be osx-x64 or osx-arm64)"
    echo "Usage: ./build_installer.sh runtimeid"
    exit 1
fi

MAC_APP_DIR="$PWD/package/Impasto.app"
MAC_APP_BIN_DIR="${MAC_APP_DIR}/Contents/MacOS/"
MAC_APP_RESOURCE_DIR="${MAC_APP_DIR}/Contents/Resources/"
MAC_APP_SHARE_DIR="${MAC_APP_RESOURCE_DIR}/share"

GTK_UPDATE_ICON_CACHE="$(brew --prefix gtk4)/bin/gtk4-update-icon-cache -f"

mkdir -p ${MAC_APP_BIN_DIR} ${MAC_APP_RESOURCE_DIR} ${MAC_APP_SHARE_DIR}

dotnet publish ../../Impasto/Impasto.csproj -p:PublishDir=${MAC_APP_BIN_DIR} -p:BuildTranslations=true -c Release -r $runtimeid --self-contained true

# Remove stuff we don't need.
rm ${MAC_APP_BIN_DIR}/*.pdb

# Move resources files out of the MacOS folder (needed for code signing).
# TODO - this could be done in the .csproj publish rule instead?
mv ${MAC_APP_BIN_DIR}/locale ${MAC_APP_SHARE_DIR}/locale
mv ${MAC_APP_BIN_DIR}/icons ${MAC_APP_SHARE_DIR}/icons
cp hicolor.index.theme ${MAC_APP_SHARE_DIR}/icons/hicolor/index.theme

cp Info.plist ${MAC_APP_DIR}/Contents
cp impasto.icns ${MAC_APP_DIR}/Contents/Resources

# Install the GTK dependencies.
echo "Bundling GTK..."
./bundle_gtk.py --runtime $runtimeid --resource_dir ${MAC_APP_RESOURCE_DIR}
# Add the GTK lib dir to the library search path (for dlopen()), as an alternative to $DYLD_LIBRARY_PATH.
install_name_tool -add_rpath "@executable_path/../Resources/lib" ${MAC_APP_BIN_DIR}/Impasto

# Generate the icon theme cache.
${GTK_UPDATE_ICON_CACHE} ${MAC_APP_SHARE_DIR}/icons/hicolor
${GTK_UPDATE_ICON_CACHE} ${MAC_APP_SHARE_DIR}/icons/Adwaita

touch ${MAC_APP_DIR}

# Create the .dmg image, and include a link to drag the app into /Applications
echo "Creating dmg..."
ln -s /Applications package/Applications
hdiutil create -quiet -srcFolder package -volname "Impasto Installer" -o Impasto.dmg
