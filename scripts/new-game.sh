#!/bin/bash
# Create a new game project from BaseTemplate
#
# Usage: bash scripts/new-game.sh <GameName> [options]
#
# Options:
#   --description, -d   Project description
#   --author, -a        Project author
#   --force, -f         Overwrite existing project
#
# Examples:
#   bash scripts/new-game.sh MyGame
#   bash scripts/new-game.sh MyGame --description "A tower defense game" --author "DerpTech"
#   bash scripts/new-game.sh MyGame --force

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

if [ $# -eq 0 ]; then
    echo "Usage: bash scripts/new-game.sh <GameName> [options]"
    echo ""
    echo "Options:"
    echo "  --description, -d   Project description"
    echo "  --author, -a        Project author"
    echo "  --force, -f         Overwrite existing project"
    echo ""
    echo "Examples:"
    echo "  bash scripts/new-game.sh MyGame"
    echo "  bash scripts/new-game.sh MyGame --description \"A tower defense game\" --author \"DerpTech\""
    exit 1
fi

cd "$REPO_ROOT"
dotnet run --project Tools/DerpTech.Cli -- new-game "$@"
