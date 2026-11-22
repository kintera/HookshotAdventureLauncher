#!/usr/bin/env bash
# Shared by the development launcher entry points. Nothing installs without a yes.
HAL_confirm_install() {
  local answer
  printf '%s [y/N] ' "$1" >&2
  if ! read -r answer || [[ ! "$answer" =~ ^[Yy]([Ee][Ss])?$ ]]; then
    printf '\nDependency setup cancelled. Install the listed dependencies and rerun the launcher.\n' >&2
    return 1
  fi
}

HAL_has_sdk() {
  local sdks
  sdks=$(dotnet --list-sdks 2>/dev/null) || return 1
  [[ "$sdks" =~ (^|$'\n')10\.0\.[0-9]+[[:space:]] ]]
}

HAL_ensure_launcher_deps() {
  local sdk_dir="${DOTNET_ROOT:-$HOME/.dotnet}" installer
  local missing=() packages=() manager=() elevate=()
  # Reuse an SDK installed by an earlier launch without editing shell profiles.
  if ! HAL_has_sdk && [[ -x "$sdk_dir/dotnet" ]]; then
    export DOTNET_ROOT="$sdk_dir"
    export PATH="$sdk_dir:$PATH"
  fi
  command -v python3 >/dev/null 2>&1 || missing+=(python3)
  if ! HAL_has_sdk; then
    command -v curl >/dev/null 2>&1 || missing+=(curl)
    command -v tar >/dev/null 2>&1 || missing+=(tar)
    command -v gzip >/dev/null 2>&1 || missing+=(gzip)
  fi
  if (( ${#missing[@]} )); then
    if command -v pacman >/dev/null 2>&1; then
      manager=(pacman -S --needed)
      for installer in "${missing[@]}"; do
        if [[ "$installer" == python3 ]]; then packages+=(python); else packages+=("$installer"); fi
      done
    elif command -v apt-get >/dev/null 2>&1; then
      manager=(apt-get install)
      packages=("${missing[@]}")
    elif command -v dnf >/dev/null 2>&1; then
      manager=(dnf install)
      packages=("${missing[@]}")
    else
      printf 'Install these dependencies with your package manager: %s\n' "${missing[*]}" >&2
      return 1
    fi
    HAL_confirm_install "Install missing system dependencies: ${packages[*]}?" || return 1
    if (( EUID != 0 )); then elevate=(sudo); fi
    "${elevate[@]}" "${manager[@]}" "${packages[@]}" || return 1
    for installer in "${missing[@]}"; do
      command -v "$installer" >/dev/null 2>&1 || return 1
    done
  fi
  if ! HAL_has_sdk; then
    HAL_confirm_install "Download and install the .NET 10 SDK from Microsoft into $sdk_dir?" || return 1
    installer=$(mktemp) || return 1
    if ! curl --fail --location --proto '=https' --proto-redir '=https' \
        https://dot.net/v1/dotnet-install.sh -o "$installer"; then
      rm -f -- "$installer"
      return 1
    fi
    local status=0
    bash "$installer" --channel 10.0 --quality GA --install-dir "$sdk_dir" --no-path || status=$?
    rm -f -- "$installer"
    (( status == 0 )) || return "$status"
    export DOTNET_ROOT="$sdk_dir"
    export PATH="$sdk_dir:$PATH"
    HAL_has_sdk || { printf 'The .NET 10 SDK is still unavailable.\n' >&2; return 1; }
  fi
}
