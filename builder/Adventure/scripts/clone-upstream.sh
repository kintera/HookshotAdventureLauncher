#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p upstream
if [ -e upstream/HookshotLauncher ]; then
  echo 'upstream/HookshotLauncher already exists; will not overwrite it.' >&2
  exit 1
fi
git clone --depth 1 https://git.magicbean.dev/MagicBeanDev/HookshotLauncher.git upstream/HookshotLauncher
printf 'Upstream pinned head: '
git -C upstream/HookshotLauncher rev-parse HEAD
printf 'Inspect README/LICENSE and build upstream unmodified before integration.\n'
