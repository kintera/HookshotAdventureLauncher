#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet run --project tests/HookshotAdventure.SmokeTests/HookshotAdventure.SmokeTests.csproj
