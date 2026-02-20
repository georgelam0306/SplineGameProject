#!/usr/bin/env bash
set -euo pipefail

# Run any game from the monorepo.
# Usage:
#   bash run-game.sh <GameName> [mode] [args...]
#   bash run-game.sh Catrillion                    # desktop for current OS/RID
#   bash run-game.sh Catrillion desktop            # desktop (explicit)
#   bash run-game.sh Catrillion multi [N]          # spawn N independent clients (default 2)
#   bash run-game.sh Catrillion local-multi [N]    # spawn N clients for local multiplayer
#   bash run-game.sh Catrillion record-multi [N]   # spawn N clients with input recording
#   bash run-game.sh Catrillion replay <file>      # replay inputs from a recorded file
#
# Flags:
#   --debug, -d              Build in Debug mode
#   --full-production        Disable hot-reload for production builds
#   --port <port>            Set base port for network coordinator (default: 7778)
#   --net-profile <profile>  Simulate network conditions (none|mild|moderate|harsh)
#   --steam-matchmaking      Use Steam for matchmaking instead of Orleans
#   --local-matchmaking      Use localhost Orleans server instead of production

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"

# Parse game name
GAME_NAME="${1:-}"
shift 2>/dev/null || true

if [ -z "$GAME_NAME" ]; then
  echo "Usage: run-game.sh <GameName> [mode] [args...]"
  echo "Available games:"
  ls -1 "$REPO_ROOT/Games/"
  exit 1
fi

GAME_DIR="$REPO_ROOT/Games/${GAME_NAME}/${GAME_NAME}"

if [ ! -d "$GAME_DIR" ]; then
  echo "Error: Game directory not found: $GAME_DIR"
  echo "Available games:"
  ls -1 "$REPO_ROOT/Games/"
  exit 1
fi

cfg="Release"
tfm="net9.0"

# Global variable for child PIDs (used by cleanup)
CHILD_PIDS=""

# Cleanup function to reliably kill all child processes
cleanup_children() {
  if [[ -n "${CHILD_PIDS}" ]]; then
    echo ""
    echo "==> Stopping all clients..."
    # First try SIGTERM
    kill ${CHILD_PIDS} 2>/dev/null || true
    # Give processes a moment to terminate gracefully
    sleep 0.3
    # Force kill any remaining
    kill -9 ${CHILD_PIDS} 2>/dev/null || true
    CHILD_PIDS=""
  fi
  exit 0
}

# Parse flags
args=()
net_profile=""
net_port=""
use_steam_matchmaking=""
use_local_matchmaking=""
full_production=""
prev_arg=""
for arg in "$@"; do
  case "$arg" in
    --debug|-d)
      cfg="Debug"
      echo "==> Debug mode enabled"
      ;;
    --full-production)
      full_production="true"
      echo "==> Full production mode (hot-reload disabled)"
      ;;
    --steam-matchmaking)
      use_steam_matchmaking="true"
      echo "==> Steam matchmaking enabled"
      ;;
    --local-matchmaking)
      use_local_matchmaking="true"
      echo "==> Local matchmaking enabled (localhost)"
      ;;
    --port)
      # next arg will be the port value
      ;;
    --net-profile)
      # next arg will be the profile value
      ;;
    none|mild|moderate|harsh)
      if [[ "${prev_arg}" == "--net-profile" ]]; then
        net_profile="$arg"
        echo "==> Network profile: ${net_profile}"
      else
        args+=("$arg")
      fi
      ;;
    *)
      if [[ "${prev_arg}" == "--port" ]]; then
        net_port="$arg"
        echo "==> Using port: ${net_port}"
      else
        args+=("$arg")
      fi
      ;;
  esac
  prev_arg="$arg"
done

mode="${args[0]:-desktop}"

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

build_game() {
  local rid="$1"
  local prod_flag=""
  if [[ -n "${full_production}" ]]; then
    prod_flag="-p:FullProduction=true"
  fi
  echo "==> Building ${GAME_NAME} (${rid})"
  cd "$GAME_DIR"
  dotnet publish -c "${cfg}" -r "${rid}" --self-contained false ${prod_flag}
}

get_publish_dir() {
  local rid="$1"
  echo "${GAME_DIR}/../Outputs/${GAME_NAME}/bin/${cfg}/${tfm}/${rid}/publish"
}

prepare_macos() {
  local pub="$1"
  if [[ "$(uname -s)" == "Darwin" ]]; then
    xattr -dr com.apple.quarantine "${pub}"/libraylib*.dylib "${pub}/${GAME_NAME}" 2>/dev/null || true
    codesign --force --sign - --timestamp=none "${pub}"/libraylib*.dylib 2>/dev/null || true
    codesign --force --sign - --timestamp=none "${pub}/${GAME_NAME}" 2>/dev/null || true
  fi
}

build_matchmaking_env() {
  local env_str=""
  if [[ -n "${use_steam_matchmaking}" ]]; then
    env_str="MATCHMAKING_PROVIDER=steam"
  elif [[ -n "${use_local_matchmaking}" ]]; then
    env_str="MATCHMAKING_SERVER=localhost"
    echo "==> Using local matchmaking server (localhost)"
  fi
  echo "$env_str"
}

run_desktop() {
  local rid="$(detect_rid)"
  build_game "$rid"

  local pub="$(get_publish_dir "$rid")"
  local exe="${pub}/${GAME_NAME}"
  if [[ "${rid}" == win-* ]]; then exe="${pub}/${GAME_NAME}.exe"; fi

  local matchmaking_env="$(build_matchmaking_env)"

  echo "==> Running ${exe}"
  pushd "${pub}" >/dev/null
  prepare_macos "${pub}"

  if [[ "$(uname -s)" == "Darwin" ]]; then
    env -u DYLD_LIBRARY_PATH DYLD_FALLBACK_LIBRARY_PATH=. ${matchmaking_env} "./${GAME_NAME}"
  elif [[ "$(uname -s)" == "Linux" ]]; then
    env -u LD_LIBRARY_PATH ${matchmaking_env} "./${GAME_NAME}"
  else
    env ${matchmaking_env} "${exe}"
  fi
  popd >/dev/null
}

run_multi() {
  local count="${1:-2}"
  local rid="$(detect_rid)"
  build_game "$rid"

  local pub="$(get_publish_dir "$rid")"
  local exe="${pub}/${GAME_NAME}"
  if [[ "${rid}" == win-* ]]; then exe="${pub}/${GAME_NAME}.exe"; fi

  pushd "${pub}" >/dev/null
  prepare_macos "${pub}"

  local matchmaking_env="$(build_matchmaking_env)"

  echo "==> Spawning ${count} independent clients (Raylib will calculate window layout)"

  CHILD_PIDS=""
  for i in $(seq 1 "$count"); do
    local client_index=$((i - 1))

    echo "==> Starting client ${i} (index ${client_index})"

    if [[ "$(uname -s)" == "Darwin" ]]; then
      env -u DYLD_LIBRARY_PATH DYLD_FALLBACK_LIBRARY_PATH=. ${matchmaking_env} \
        "./${GAME_NAME}" --client-index "${client_index}" --client-count "${count}" &
    elif [[ "$(uname -s)" == "Linux" ]]; then
      env -u LD_LIBRARY_PATH ${matchmaking_env} \
        "./${GAME_NAME}" --client-index "${client_index}" --client-count "${count}" &
    else
      env ${matchmaking_env} "${exe}" --client-index "${client_index}" --client-count "${count}" &
    fi
    CHILD_PIDS="${CHILD_PIDS} $!"

    sleep 0.3
  done

  popd >/dev/null

  echo "==> All clients started. PIDs:${CHILD_PIDS}"
  echo "==> Press Ctrl+C to stop all"

  trap cleanup_children INT TERM
  wait
}

run_local_multi() {
  local count="${1:-2}"
  local rid="$(detect_rid)"
  build_game "$rid"

  local pub="$(get_publish_dir "$rid")"
  local exe="${pub}/${GAME_NAME}"
  if [[ "${rid}" == win-* ]]; then exe="${pub}/${GAME_NAME}.exe"; fi

  pushd "${pub}" >/dev/null
  prepare_macos "${pub}"

  echo "==> Spawning ${count} clients for local multiplayer (Raylib will calculate window layout)"

  CHILD_PIDS=""
  for i in $(seq 1 "$count"); do
    local client_index=$((i - 1))
    local player_slot=$((i - 1))  # 0-indexed slot

    local net_env="NET_PLAYERS=${count} NET_PLAYER_SLOT=${player_slot}"
    if [[ -n "${net_port}" ]]; then
      net_env="${net_env} NET_PORT=${net_port}"
    fi
    if [[ -n "${net_profile}" ]]; then
      net_env="${net_env} NET_PROFILE=${net_profile}"
    fi
    if [[ -n "${use_steam_matchmaking}" ]]; then
      net_env="${net_env} MATCHMAKING_PROVIDER=steam"
    elif [[ -n "${use_local_matchmaking}" ]]; then
      net_env="${net_env} MATCHMAKING_SERVER=localhost"
    fi
    if [[ "$i" -eq 1 ]]; then
      net_env="NET_HOST=true ${net_env}"
      echo "==> Starting host (slot ${player_slot})"
    else
      net_env="NET_HOST=false ${net_env}"
      echo "==> Starting client (slot ${player_slot})"
    fi

    if [[ "$(uname -s)" == "Darwin" ]]; then
      env -u DYLD_LIBRARY_PATH DYLD_FALLBACK_LIBRARY_PATH=. ${net_env} \
        "./${GAME_NAME}" --client-index "${client_index}" --client-count "${count}" --skip-menu &
    elif [[ "$(uname -s)" == "Linux" ]]; then
      env -u LD_LIBRARY_PATH ${net_env} \
        "./${GAME_NAME}" --client-index "${client_index}" --client-count "${count}" --skip-menu &
    else
      env ${net_env} "${exe}" --client-index "${client_index}" --client-count "${count}" --skip-menu &
    fi
    CHILD_PIDS="${CHILD_PIDS} $!"

    sleep 0.5
  done

  popd >/dev/null

  echo "==> All clients started. PIDs:${CHILD_PIDS}"
  echo "==> Press Ctrl+C to stop all"

  trap cleanup_children INT TERM
  wait
}

run_record_multi() {
  local count="${1:-2}"
  local rid="$(detect_rid)"
  build_game "$rid"

  local pub="$(get_publish_dir "$rid")"
  local exe="${pub}/${GAME_NAME}"
  if [[ "${rid}" == win-* ]]; then exe="${pub}/${GAME_NAME}.exe"; fi

  pushd "${pub}" >/dev/null
  prepare_macos "${pub}"

  # Create Logs directory
  mkdir -p Logs

  local timestamp=$(date +"%Y%m%d_%H%M%S")
  echo "==> Spawning ${count} clients for local multiplayer with recording (Raylib will calculate window layout)"
  echo "==> Recording to ${pub}/Logs/replay_*_${timestamp}.bin"

  CHILD_PIDS=""
  for i in $(seq 1 "$count"); do
    local client_index=$((i - 1))
    local player_slot=$((i - 1))

    local record_file="Logs/replay_player${player_slot}_${timestamp}.bin"
    local net_env="NET_PLAYERS=${count} NET_PLAYER_SLOT=${player_slot} RECORD_FILE=${record_file}"
    if [[ -n "${net_port}" ]]; then
      net_env="${net_env} NET_PORT=${net_port}"
    fi
    if [[ -n "${net_profile}" ]]; then
      net_env="${net_env} NET_PROFILE=${net_profile}"
    fi
    if [[ -n "${use_steam_matchmaking}" ]]; then
      net_env="${net_env} MATCHMAKING_PROVIDER=steam"
    elif [[ -n "${use_local_matchmaking}" ]]; then
      net_env="${net_env} MATCHMAKING_SERVER=localhost"
    fi
    if [[ "$i" -eq 1 ]]; then
      net_env="NET_HOST=true ${net_env}"
      echo "==> Starting host (slot ${player_slot}), recording to ${record_file}"
    else
      net_env="NET_HOST=false ${net_env}"
      echo "==> Starting client (slot ${player_slot}), recording to ${record_file}"
    fi

    if [[ "$(uname -s)" == "Darwin" ]]; then
      env -u DYLD_LIBRARY_PATH DYLD_FALLBACK_LIBRARY_PATH=. ${net_env} \
        "./${GAME_NAME}" --client-index "${client_index}" --client-count "${count}" --skip-menu &
    elif [[ "$(uname -s)" == "Linux" ]]; then
      env -u LD_LIBRARY_PATH ${net_env} \
        "./${GAME_NAME}" --client-index "${client_index}" --client-count "${count}" --skip-menu &
    else
      env ${net_env} "${exe}" --client-index "${client_index}" --client-count "${count}" --skip-menu &
    fi
    CHILD_PIDS="${CHILD_PIDS} $!"

    sleep 0.5
  done

  popd >/dev/null

  echo "==> All clients started. PIDs:${CHILD_PIDS}"
  echo "==> Press Ctrl+C to stop all"

  trap cleanup_children INT TERM
  wait
}

run_replay() {
  local replay_file="${1:-}"
  if [[ -z "${replay_file}" ]]; then
    echo "Usage: run-game.sh ${GAME_NAME} replay <file>"
    echo "Example: run-game.sh ${GAME_NAME} replay Logs/replay_player0_20251213_120000.bin"
    exit 1
  fi

  local rid="$(detect_rid)"
  build_game "$rid"

  local pub="$(get_publish_dir "$rid")"
  local exe="${pub}/${GAME_NAME}"
  if [[ "${rid}" == win-* ]]; then exe="${pub}/${GAME_NAME}.exe"; fi

  # Resolve absolute path for replay file
  if [[ ! "${replay_file}" = /* ]]; then
    if [[ -f "${PWD}/${replay_file}" ]]; then
      replay_file="${PWD}/${replay_file}"
    elif [[ -f "${pub}/${replay_file}" ]]; then
      replay_file="${pub}/${replay_file}"
    else
      replay_file="${PWD}/${replay_file}"  # Will fail with nice error below
    fi
  fi

  if [[ ! -f "${replay_file}" ]]; then
    echo "Error: Replay file not found: ${replay_file}"
    echo "Hint: Recordings are saved to ${pub}/Logs/"
    exit 1
  fi

  local replay_env="REPLAY_FILE=${replay_file}"
  if [[ -n "${use_steam_matchmaking}" ]]; then
    replay_env="${replay_env} MATCHMAKING_PROVIDER=steam"
  elif [[ -n "${use_local_matchmaking}" ]]; then
    replay_env="${replay_env} MATCHMAKING_SERVER=localhost"
  fi

  echo "==> Running replay from ${replay_file}"
  pushd "${pub}" >/dev/null
  prepare_macos "${pub}"

  if [[ "$(uname -s)" == "Darwin" ]]; then
    env -u DYLD_LIBRARY_PATH DYLD_FALLBACK_LIBRARY_PATH=. ${replay_env} "./${GAME_NAME}" --skip-menu
  elif [[ "$(uname -s)" == "Linux" ]]; then
    env -u LD_LIBRARY_PATH ${replay_env} "./${GAME_NAME}" --skip-menu
  else
    env ${replay_env} "${exe}" --skip-menu
  fi
  popd >/dev/null
}

case "${mode}" in
  desktop) run_desktop ;;
  multi) run_multi "${args[1]:-}" ;;
  local-multi) run_local_multi "${args[1]:-}" ;;
  record-multi) run_record_multi "${args[1]:-}" ;;
  replay) run_replay "${args[1]:-}" ;;
  *) echo "Unknown mode: ${mode}. Use 'desktop', 'multi [N]', 'local-multi [N]', 'record-multi [N]', or 'replay <file>'." ; exit 2 ;;
esac
