#!/usr/bin/env bash
# Root entry point for the Adventure development launcher.
set -euo pipefail
HAL_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

case "${1:-}" in
  -h|--help)
    printf '%s\n' 'Usage: ./hookshot-dev.sh [--builtin|--builtin-launch [GAME_FOLDER]|--test]' \
      '  No option: build and open the Avalonia development launcher.' \
      '  --builtin: open the stock Linux Hookshot Builder from this checkout.' \
      '  --builtin-launch: open the stock Linux launcher for GAME_FOLDER or the last built game.' \
      '  --test:    run launcher core smoke tests without opening a window.'
    exit 0
    ;;
  '') HAL_SCRIPT=run-adventure.sh ;;
  --builtin) HAL_SCRIPT=builtin ;;
  --builtin-launch) HAL_SCRIPT=builtin-launch ;;
  --test) HAL_SCRIPT=test-core.sh ;;
  *) printf 'Unknown option: %s\n' "$1" >&2; exit 2 ;;
esac
if (( $# > 1 )) && [[ "$HAL_SCRIPT" != builtin-launch || $# -gt 2 ]]; then
  printf '%s\n' 'Expected at most one option; use --help.' >&2
  exit 2
fi
if [[ "$HAL_SCRIPT" == builtin || "$HAL_SCRIPT" == builtin-launch ]]; then
  HAL_ARGS=()
  if [[ "$HAL_SCRIPT" == builtin-launch ]]; then
    HAL_GAME="${2:-}"
    if [[ -z "$HAL_GAME" ]]; then
      HAL_CONFIG="${XDG_CONFIG_HOME:-$HOME/.config}/hookshot/builder.ini"
      if [[ -f "$HAL_CONFIG" ]]; then
        while IFS= read -r HAL_LINE || [[ -n "$HAL_LINE" ]]; do
          [[ "$HAL_LINE" == out=* ]] && HAL_GAME="${HAL_LINE#out=}"
        done < "$HAL_CONFIG"
      fi
    fi
    if [[ -z "$HAL_GAME" || ! -f "$HAL_GAME/image.bin" || ( ! -x "$HAL_GAME/bin/hookshot" && ! -f "$HAL_GAME/hookshot" ) ]]; then
      printf '%s\n' 'No built game found. Pass its folder to --builtin-launch, or build one with --builtin.' >&2
      exit 1
    fi
    HAL_ARGS=(--launch "$HAL_GAME")
  fi
  for HAL_DEPS in "$HOME/.cache/hookshot-builder-dev/deps" "$HAL_ROOT/build-linux/deps"; do
    if [[ -x "$HAL_DEPS/python/bin/python3" && -x "$HAL_DEPS/emsdk/upstream/bin/clang" && \
          -x "$HAL_DEPS/emsdk/upstream/bin/clang++" && -f "$HAL_DEPS/ffmpeg/lib/libavcodec.a" && \
          -f "$HAL_DEPS/sdl2/libSDL2-2.0.so.0" && -d "$HAL_DEPS/cpm/vulkanheaders" && \
          -d "$HAL_DEPS/cpm/spirv-headers" && -d "$HAL_DEPS/cpm/vulkanmemoryallocator" && \
          -f "$HAL_DEPS/.python" && -f "$HAL_DEPS/.llvm" && -f "$HAL_DEPS/.meson" && \
          -f "$HAL_DEPS/.ffmpeg" && -f "$HAL_DEPS/.cpm" ]]; then
      export EMSDK="$HAL_DEPS/emsdk" RX_CPM="$HAL_DEPS/cpm"
      export HOOKSHOT_FFMPEG="$HAL_DEPS/ffmpeg" HOOKSHOT_SDL2="$HAL_DEPS/sdl2/libSDL2-2.0.so.0"
      exec "$HAL_DEPS/python/bin/python3" -I "$HAL_ROOT/builder/linux/hookshot_builder.py" "${HAL_ARGS[@]}"
    fi
  done
  printf '%s\n' 'Prepared build tools not found; using system Python and build dependencies.' >&2
  exec python3 -I "$HAL_ROOT/builder/linux/hookshot_builder.py" "${HAL_ARGS[@]}"
fi
source "$HAL_ROOT/scripts/linux/dev-launcher-deps.sh"
HAL_ensure_launcher_deps
exec bash "$HAL_ROOT/builder/Adventure/scripts/$HAL_SCRIPT"
