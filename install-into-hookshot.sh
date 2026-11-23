#!/usr/bin/env bash
set -euo pipefail

source_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if [[ $# != 1 || ! -d "$1" ]]; then
  printf '%s\n' 'Usage: ./install-into-hookshot.sh /path/to/Hookshot-v3.0.2' >&2
  exit 2
fi
target="$(cd -- "$1" && pwd)"
if [[ ! -f "$target/nx2/make_game.py" || ! -f "$target/gpu/vk/switches.def" ]]; then
  printf '%s\n' 'That folder is not a full Hookshot source checkout.' >&2
  exit 1
fi
for relative in builder/Adventure hookshot-dev.sh HAL_dev_launcher.sh scripts/linux/dev-launcher-deps.sh; do
  if [[ -e "$target/$relative" ]]; then
    printf 'Already exists: %s. Use a fresh compatible checkout; no files were changed.\n' "$target/$relative" >&2
    exit 1
  fi
done
revision="$(GIT_CEILING_DIRECTORIES="$(dirname -- "$target")" git -C "$target" rev-parse HEAD)"
if [[ "$revision" != a2b7f97976bb8538d6c7d5a4d01a05606c8d0cff ]]; then
  printf '%s\n' 'This development installer requires the Hookshot v3.0.2 revision a2b7f979; no files were changed.' >&2
  exit 1
fi
patch="$source_root/integration/hookshot-v3.0.2.patch"
if ! GIT_CEILING_DIRECTORIES="$(dirname -- "$target")" git -C "$target" apply --check "$patch"; then
  printf '%s\n' 'The integration patch did not match this checkout. Use the documented Hookshot revision; no files were changed.' >&2
  exit 1
fi
GIT_CEILING_DIRECTORIES="$(dirname -- "$target")" git -C "$target" apply "$patch"
mkdir -p "$target/builder" "$target/scripts/linux"
cp -a "$source_root/builder/Adventure" "$target/builder/Adventure"
cp -a "$source_root/hookshot-dev.sh" "$source_root/HAL_dev_launcher.sh" "$target/"
cp -a "$source_root/scripts/linux/dev-launcher-deps.sh" "$target/scripts/linux/"
printf '%s\n' 'Installed Adventure 0.9.02a development source. From the Hookshot checkout, run ./hookshot-dev.sh.'
