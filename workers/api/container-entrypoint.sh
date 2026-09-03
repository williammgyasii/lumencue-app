#!/bin/sh
set -eu
if [ -n "${NEON_CONNECTION_STRING:-}" ]; then
  neon_set=yes
else
  neon_set=no
fi
echo "lumen-boot urls=${ASPNETCORE_URLS:-unset} port=${PORT:-unset} neon_set=${neon_set}"
exec dotnet ChurchProjection.Api.dll
