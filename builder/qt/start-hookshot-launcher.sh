#!/bin/sh
# Hookshot Adventure v0.8 launcher: use installed Python; never create a venv.
set -eu
DIR=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
# texture2ddecoder is optionally installed here with pip --target.  No
# externally-managed Python files or game assets are modified.
if [ -d "$DIR/.local-python" ]; then
    export PYTHONPATH="$DIR/.local-python${PYTHONPATH:+:$PYTHONPATH}"
fi
# Avoid inheriting a separate icon-browser virtualenv from the caller's shell.
# Override with HOOKSHOT_PYTHON only when explicitly requested.
if [ -n "${HOOKSHOT_PYTHON:-}" ]; then
    PYTHON="$HOOKSHOT_PYTHON"
elif [ -x /usr/bin/python3 ]; then
    PYTHON=/usr/bin/python3
else
    PYTHON=python3
fi
exec "$PYTHON" "$DIR/hookshot_qt.py" "$@"
