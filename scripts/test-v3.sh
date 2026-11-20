#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet run --project test/v3/Snake.V3.Core.Tests.csproj
dotnet build server/Snake.Server.csproj --nologo --verbosity quiet
dotnet run --project test/local-peer-guard/PeerGuard.csproj -- server/bin/Debug/net10.0/Snake.Server.dll
node --test test/v3/*.test.js
