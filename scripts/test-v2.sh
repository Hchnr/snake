#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet run --project test/v2/Snake.Core.Tests.csproj
dotnet build server/Snake.Server.csproj --nologo --verbosity quiet
node --test test/v2/*.test.js
