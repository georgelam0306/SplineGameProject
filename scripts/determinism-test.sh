#!/bin/bash
set -e
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
GAME_NAME=$1
REPLAY_FILE=$2
shift 2 2>/dev/null || true
[ -z "$GAME_NAME" ] || [ -z "$REPLAY_FILE" ] && { echo "Usage: determinism-test.sh <GameName> <replay.bin> [--rollback]"; echo "Available games:"; ls -1 "$REPO_ROOT/Games/"; exit 1; }
dotnet run -c Release --project "$REPO_ROOT/Games/${GAME_NAME}/${GAME_NAME}.Headless/${GAME_NAME}.Headless.csproj" -- "$REPLAY_FILE" "$@"
