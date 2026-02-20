#!/bin/bash
set -e

# NativeAOT release build script for DerpTech games
# Produces a single native executable with no .NET runtime dependency
# Usage:
#   bash publish-game-aot.sh <GameName>              # Build for current platform only
#   bash publish-game-aot.sh <GameName> all          # Build for all platforms
#   bash publish-game-aot.sh <GameName> osx-arm64    # Build for specific RID

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"

# Get game name from first argument
GAME_NAME="$1"
shift 2>/dev/null || true

if [ -z "$GAME_NAME" ]; then
  echo "Usage: publish-game-aot.sh <GameName> [current|all|osx-arm64|osx-x64|win-x64|linux-x64]"
  echo ""
  echo "Available games:"
  ls -1 "$REPO_ROOT/Games/"
  exit 1
fi

GAME_DIR="$REPO_ROOT/Games/${GAME_NAME}/${GAME_NAME}"
if [ ! -d "$GAME_DIR" ]; then
  echo "Error: Game directory not found: $GAME_DIR"
  echo ""
  echo "Available games:"
  ls -1 "$REPO_ROOT/Games/"
  exit 1
fi

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

CONFIGURATION="Release"
OUTPUT_DIR="$REPO_ROOT/Games/${GAME_NAME}/publish-aot"
APP_NAME="$GAME_NAME"
# Convert game name to bundle ID format (lowercase, replace spaces with dots)
BUNDLE_ID="com.derptech.$(echo "$GAME_NAME" | tr '[:upper:]' '[:lower:]' | tr ' ' '.')"

cd "$GAME_DIR"

detect_rid() {
  uname_s="$(uname -s 2>/dev/null || echo unknown)"
  uname_m="$(uname -m 2>/dev/null || echo unknown)"
  case "${uname_s}" in
    Darwin)
      if [[ "${uname_m}" == "arm64" ]]; then echo "osx-arm64"; else echo "osx-x64"; fi ;;
    Linux)
      echo "linux-x64" ;;
    MINGW*|MSYS*|CYGWIN*)
      echo "win-x64" ;;
    *)
      echo "osx-arm64" ;;
  esac
}

build_release() {
  local rid="$1"
  echo -e "${GREEN}Building NativeAOT release for ${GAME_NAME} (${rid})...${NC}"
  # NativeAOT flags:
  #   -p:NativeAot=true         - Enable NativeAOT compilation (triggers PublishAot=true in csproj)
  #   -p:FullProduction=true    - Disable HOT_RELOAD
  #   -p:ProductionBuild=true   - Disable sync checks and desync detection
  dotnet publish -c "$CONFIGURATION" -r "$rid" -p:NativeAot=true -p:FullProduction=true -p:ProductionBuild=true -o "$OUTPUT_DIR/$rid/raw"
  echo -e "${GREEN}AOT compile complete for ${rid}${NC}"

  # Package for distribution
  package_for_platform "$rid"
}

package_for_platform() {
  local rid="$1"
  local raw_dir="$OUTPUT_DIR/$rid/raw"
  local dist_dir="$OUTPUT_DIR/$rid/dist"

  rm -rf "$dist_dir"
  mkdir -p "$dist_dir"

  case "$rid" in
    osx-arm64|osx-x64)
      create_macos_app "$rid" "$raw_dir" "$dist_dir"
      ;;
    win-x64)
      create_windows_zip "$rid" "$raw_dir" "$dist_dir"
      ;;
    linux-x64)
      create_linux_tar "$rid" "$raw_dir" "$dist_dir"
      ;;
  esac
}

create_macos_app() {
  local rid="$1"
  local raw_dir="$2"
  local dist_dir="$3"
  local app_dir="$dist_dir/${APP_NAME}.app"

  echo -e "${GREEN}Creating macOS .app bundle (NativeAOT)...${NC}"

  # Create .app structure
  mkdir -p "$app_dir/Contents/MacOS"
  mkdir -p "$app_dir/Contents/Resources"

  # Copy the native executable and required files
  cp "$raw_dir/${APP_NAME}" "$app_dir/Contents/MacOS/${APP_NAME}-bin"
  chmod +x "$app_dir/Contents/MacOS/${APP_NAME}-bin"

  # Copy native libraries (raylib, etc.) and resources
  for file in "$raw_dir"/*.dylib; do
    [[ -f "$file" ]] && cp "$file" "$app_dir/Contents/MacOS/"
  done

  # Copy Resources and other content
  if [ -d "$raw_dir/Resources" ]; then
    cp -r "$raw_dir/Resources" "$app_dir/Contents/MacOS/"
  fi

  # Copy any .bin data files
  for file in "$raw_dir"/*.bin; do
    [[ -f "$file" ]] && cp "$file" "$app_dir/Contents/MacOS/"
  done

  # Create launcher script for library path
  cat > "$app_dir/Contents/MacOS/${APP_NAME}" << LAUNCHER_EOF
#!/bin/bash
# Launcher script for ${APP_NAME} (NativeAOT)
# Sets library path so native binary can find libraries

cd "\$(dirname "\$0")"

# Remove quarantine attributes (first run from downloaded app)
xattr -dr com.apple.quarantine ./*.dylib ./${APP_NAME}-bin 2>/dev/null || true

# Set library search path and run the game
exec env -u DYLD_LIBRARY_PATH DYLD_FALLBACK_LIBRARY_PATH=. ./${APP_NAME}-bin "\$@"
LAUNCHER_EOF
  chmod +x "$app_dir/Contents/MacOS/${APP_NAME}"

  # Create Info.plist
  cat > "$app_dir/Contents/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>${APP_NAME}</string>
    <key>CFBundleIdentifier</key>
    <string>${BUNDLE_ID}</string>
    <key>CFBundleName</key>
    <string>${APP_NAME}</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleSignature</key>
    <string>????</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.games</string>
</dict>
</plist>
EOF

  # Ad-hoc sign
  echo -e "${GREEN}Ad-hoc signing...${NC}"
  codesign --force --sign - "$app_dir/Contents/MacOS/${APP_NAME}-bin" 2>/dev/null || true
  for dylib in "$app_dir/Contents/MacOS"/*.dylib; do
    [[ -f "$dylib" ]] && codesign --force --sign - "$dylib" 2>/dev/null || true
  done
  codesign --force --deep --sign - "$app_dir" 2>/dev/null || true

  # Create distributable zip with -aot suffix
  echo -e "${GREEN}Creating zip for distribution...${NC}"
  cd "$dist_dir"
  zip -r -q "${APP_NAME}-${rid}-aot.zip" "${APP_NAME}.app"
  cd "$GAME_DIR"

  echo -e "${GREEN}macOS NativeAOT package: ${dist_dir}/${APP_NAME}-${rid}-aot.zip${NC}"
  echo -e "${YELLOW}  Tell friends: Right-click -> Open (first time only)${NC}"
}

create_windows_zip() {
  local rid="$1"
  local raw_dir="$2"
  local dist_dir="$3"
  local game_dir="$dist_dir/$APP_NAME"

  echo -e "${GREEN}Creating Windows zip (NativeAOT)...${NC}"

  mkdir -p "$game_dir"

  # Copy native executable
  cp "$raw_dir/${APP_NAME}.exe" "$game_dir/"

  # Copy native libraries
  for file in "$raw_dir"/*.dll; do
    [[ -f "$file" ]] && cp "$file" "$game_dir/"
  done

  # Copy Resources and data files
  if [ -d "$raw_dir/Resources" ]; then
    cp -r "$raw_dir/Resources" "$game_dir/"
  fi
  for file in "$raw_dir"/*.bin; do
    [[ -f "$file" ]] && cp "$file" "$game_dir/"
  done

  cd "$dist_dir"
  if command -v powershell &> /dev/null; then
    powershell -Command "Compress-Archive -Path '$APP_NAME' -DestinationPath '${APP_NAME}-${rid}-aot.zip' -Force"
  else
    zip -r -q "${APP_NAME}-${rid}-aot.zip" "$APP_NAME"
  fi
  cd "$GAME_DIR"

  echo -e "${GREEN}Windows NativeAOT package: ${dist_dir}/${APP_NAME}-${rid}-aot.zip${NC}"
}

create_linux_tar() {
  local rid="$1"
  local raw_dir="$2"
  local dist_dir="$3"
  local game_dir="$dist_dir/$APP_NAME"

  echo -e "${GREEN}Creating Linux tarball (NativeAOT)...${NC}"

  mkdir -p "$game_dir"

  # Copy native executable
  cp "$raw_dir/${APP_NAME}" "$game_dir/"
  chmod +x "$game_dir/${APP_NAME}"

  # Copy native libraries
  for file in "$raw_dir"/*.so; do
    [[ -f "$file" ]] && cp "$file" "$game_dir/"
  done

  # Copy Resources and data files
  if [ -d "$raw_dir/Resources" ]; then
    cp -r "$raw_dir/Resources" "$game_dir/"
  fi
  for file in "$raw_dir"/*.bin; do
    [[ -f "$file" ]] && cp "$file" "$game_dir/"
  done

  # Create run script (sets library path for native libs)
  cat > "$game_dir/run.sh" << EOF
#!/bin/bash
cd "\$(dirname "\$0")"
LD_LIBRARY_PATH=. ./${APP_NAME} "\$@"
EOF
  chmod +x "$game_dir/run.sh"

  cd "$dist_dir"
  tar -czf "${APP_NAME}-${rid}-aot.tar.gz" "$APP_NAME"
  cd "$GAME_DIR"

  echo -e "${GREEN}Linux NativeAOT package: ${dist_dir}/${APP_NAME}-${rid}-aot.tar.gz${NC}"
}

MODE="${1:-current}"

# Clean previous builds
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

case "$MODE" in
  current)
    RID="$(detect_rid)"
    echo -e "${YELLOW}Building ${GAME_NAME} NativeAOT release for current platform: ${RID}${NC}"
    build_release "$RID"
    ;;
  all)
    echo -e "${YELLOW}Building ${GAME_NAME} NativeAOT release for all platforms...${NC}"
    build_release "osx-arm64"
    build_release "osx-x64"
    build_release "win-x64"
    build_release "linux-x64"
    ;;
  osx-arm64|osx-x64|win-x64|linux-x64)
    build_release "$MODE"
    ;;
  *)
    echo "Usage: publish-game-aot.sh <GameName> [current|all|osx-arm64|osx-x64|win-x64|linux-x64]"
    exit 1
    ;;
esac

echo ""
echo -e "${YELLOW}========================================${NC}"
echo -e "${YELLOW}NativeAOT build complete! Packages:${NC}"
echo -e "${YELLOW}========================================${NC}"
find "$OUTPUT_DIR" -name "*.zip" -o -name "*.tar.gz" 2>/dev/null | while read f; do
  echo -e "${GREEN}  $f${NC}"
done
