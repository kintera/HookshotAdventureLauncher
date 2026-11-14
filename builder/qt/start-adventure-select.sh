#!/bin/sh
# Run the companion Save Profiles window from the source tree.
set -eu
script_dir=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
if [ "$#" -gt 0 ]; then
  exec python3 "$script_dir/profile_gui.py" --game-dir "$1"
fi
exec python3 "$script_dir/profile_gui.py" --game-dir "${HOOKSHOT_GAME_DIR:-$HOME/Games/Hookshot}"
