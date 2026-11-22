#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if ! command -v dotnet >/dev/null 2>&1; then
  echo 'Install the .NET 10 SDK first; see README.md. No game files will be changed.' >&2
  exit 1
fi
dotnet run --project src/HookshotAdventure.App/HookshotAdventure.App.csproj
