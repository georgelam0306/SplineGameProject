#!/bin/bash
# Download glslc binaries for all platforms from Vulkan SDK
# Run this script to populate Tools/glslc/ for cross-platform builds

set -e

TOOLS_DIR="$(dirname "$0")/../Tools/glslc"
VULKAN_SDK_VERSION="1.3.296.0"

echo "Downloading glslc binaries to $TOOLS_DIR"

# macOS ARM64 - copy from local if available
if [ -f "/usr/local/bin/glslc" ]; then
    echo "Copying local glslc to osx-arm64..."
    cp /usr/local/bin/glslc "$TOOLS_DIR/osx-arm64/"
    chmod +x "$TOOLS_DIR/osx-arm64/glslc"
fi

# For other platforms, download from Vulkan SDK or use package managers
# These are placeholder instructions - actual downloads require SDK installation

echo ""
echo "=== Manual Steps for Other Platforms ==="
echo ""
echo "Windows (win-x64):"
echo "  1. Download Vulkan SDK from https://vulkan.lunarg.com/sdk/home"
echo "  2. Install and copy Bin/glslc.exe to Tools/glslc/win-x64/"
echo ""
echo "Linux (linux-x64):"
echo "  1. sudo apt install glslc  OR  sudo dnf install glslc"
echo "  2. Copy /usr/bin/glslc to Tools/glslc/linux-x64/"
echo ""
echo "macOS Intel (osx-x64):"
echo "  1. Download Vulkan SDK from https://vulkan.lunarg.com/sdk/home"
echo "  2. Extract and copy macOS/bin/glslc to Tools/glslc/osx-x64/"
echo ""

# Verify current platform binary exists
CURRENT_GLSLC=""
case "$(uname -s)-$(uname -m)" in
    Darwin-arm64) CURRENT_GLSLC="$TOOLS_DIR/osx-arm64/glslc" ;;
    Darwin-x86_64) CURRENT_GLSLC="$TOOLS_DIR/osx-x64/glslc" ;;
    Linux-x86_64) CURRENT_GLSLC="$TOOLS_DIR/linux-x64/glslc" ;;
esac

if [ -n "$CURRENT_GLSLC" ] && [ -f "$CURRENT_GLSLC" ]; then
    echo "Current platform glslc verified: $CURRENT_GLSLC"
    "$CURRENT_GLSLC" --version
else
    echo "WARNING: glslc not found for current platform"
fi
