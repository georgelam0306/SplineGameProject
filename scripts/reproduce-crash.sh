#!/bin/bash
set -e

# Crash Reproduction Script for Catrillion
# Usage: bash scripts/reproduce-crash.sh <repro-key>
# Example: bash scripts/reproduce-crash.sh 994ab6ac40f8:f43024d
#
# The repro key can be copied from the bug report dashboard.
# This script will:
#   1. Download the replay file from the bug report
#   2. Create a git worktree at the exact commit
#   3. Build the game
#   4. Run the game with the replay

REPRO_KEY="$1"
SERVER="${BUGREPORT_SERVER:-http://45.76.79.231:5052}"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

print_step() {
    echo -e "${BLUE}[$1/4]${NC} $2"
}

print_success() {
    echo -e "  ${GREEN}✓${NC} $1"
}

print_error() {
    echo -e "${RED}Error:${NC} $1"
}

if [ -z "$REPRO_KEY" ]; then
    echo "Usage: bash scripts/reproduce-crash.sh <repro-key>"
    echo ""
    echo "The repro key can be copied from the bug report dashboard."
    echo "Format: <reportId>:<commitHash>"
    echo "Example: 994ab6ac40f8:f43024d"
    exit 1
fi

# Parse repro key
REPORT_ID=$(echo "$REPRO_KEY" | cut -d':' -f1)
COMMIT_HASH=$(echo "$REPRO_KEY" | cut -d':' -f2)

if [ -z "$REPORT_ID" ] || [ -z "$COMMIT_HASH" ]; then
    print_error "Invalid repro key format. Expected: reportId:commitHash"
    exit 1
fi

echo ""
echo -e "${YELLOW}=== Crash Reproduction ===${NC}"
echo "Report ID: $REPORT_ID"
echo "Commit:    $COMMIT_HASH"
echo "Server:    $SERVER"
echo ""

# Ensure we're in the repo root
REPO_ROOT=$(git rev-parse --show-toplevel 2>/dev/null) || {
    print_error "Not in a git repository. Run this from within the DerpTech2026 repo."
    exit 1
}
cd "$REPO_ROOT"

# Create repro directory
REPRO_DIR=".repro/$COMMIT_HASH"
mkdir -p "$REPRO_DIR"

# Download replay
print_step 1 "Downloading replay..."
REPLAY_PATH="$REPRO_DIR/replay.bin"
HTTP_CODE=$(curl -s -w "%{http_code}" -o "$REPLAY_PATH" "$SERVER/api/reports/$REPORT_ID/replay")
if [ "$HTTP_CODE" != "200" ]; then
    print_error "Failed to download replay (HTTP $HTTP_CODE)"
    echo "  Make sure the report has a replay file attached."
    rm -f "$REPLAY_PATH"
    exit 1
fi
print_success "Downloaded: $REPLAY_PATH"

# Check if we need to create worktree
WORKTREE_PATH=".repro/worktree-$COMMIT_HASH"
print_step 2 "Setting up git worktree..."

if [ -d "$WORKTREE_PATH" ]; then
    print_success "Using existing worktree: $WORKTREE_PATH"
else
    # Try to fetch the commit if not available locally
    if ! git cat-file -e "$COMMIT_HASH" 2>/dev/null; then
        echo "  Fetching commit from origin..."
        git fetch origin --quiet 2>/dev/null || true
        git fetch --all --quiet 2>/dev/null || true
    fi

    if ! git cat-file -e "$COMMIT_HASH" 2>/dev/null; then
        print_error "Commit $COMMIT_HASH not found in repository"
        echo "  Try: git fetch --all"
        exit 1
    fi

    git worktree add "$WORKTREE_PATH" "$COMMIT_HASH" --quiet || {
        print_error "Failed to create worktree at $COMMIT_HASH"
        exit 1
    }
    print_success "Created worktree: $WORKTREE_PATH"
fi

# Build
print_step 3 "Building game..."
cd "$WORKTREE_PATH"
dotnet build Games/Catrillion/Catrillion/Catrillion.csproj -c Release --nologo -v q 2>&1 | grep -v "^$" || {
    print_error "Build failed!"
    exit 1
}
print_success "Build complete"

# Run with replay
print_step 4 "Running game with replay..."
cd "$REPO_ROOT"
echo ""

REPLAY_FILE="$(pwd)/$REPLAY_PATH" dotnet run \
    --project "$WORKTREE_PATH/Games/Catrillion/Catrillion/Catrillion.csproj" \
    -c Release \
    --no-build

echo ""
echo -e "${GREEN}=== Reproduction complete ===${NC}"
