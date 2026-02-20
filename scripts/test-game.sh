#!/bin/bash
set -e
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
GAME_NAME=$1; shift 2>/dev/null || true
[ -z "$GAME_NAME" ] && { echo "Usage: test-game.sh <GameName> [args...]"; echo "Available games:"; ls -1 "$REPO_ROOT/Games/"; exit 1; }
dotnet test "$REPO_ROOT/Games/${GAME_NAME}/${GAME_NAME}.Tests/${GAME_NAME}.Tests.csproj" "$@"
