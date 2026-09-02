#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <zentitle-openapi-json-path-or-url> <zenmeter-openapi-json-path-or-url>" >&2
  exit 1
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

dotnet tool restore

dotnet tool run nswag run \
  nswag/management-api.nswag \
  "/variables:OpenApiJson=$1,ClassName=ZentitleManagementApiGeneratedClient,Namespace=NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated,ExceptionClass=ZentitleManagementApiException,Output=$ROOT/src/NalpeironGrowthPlatformDemo/Nalpeiron/Zentitle/Generated/ZentitleManagementApiGeneratedClient.g.cs"

dotnet tool run nswag run \
  nswag/management-api.nswag \
  "/variables:OpenApiJson=$2,ClassName=ZenmeterManagementApiGeneratedClient,Namespace=NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated,ExceptionClass=ZenmeterManagementApiException,Output=$ROOT/src/NalpeironGrowthPlatformDemo/Nalpeiron/Zenmeter/Generated/ZenmeterManagementApiGeneratedClient.g.cs"
