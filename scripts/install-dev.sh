#!/bin/sh
set -eu
# Remove only this tool's own packages before packing. NuGet orders prereleases
# alphabetically, so a leftover 0.1.0-dev outranks a fresh 0.1.0-alpha.N and would
# be (re)installed instead of the current build. `dotnet pack` regenerates every
# Mdl.Cli* package, so clearing just those is safe.
rm -f tools-packages/Mdl.Cli*.nupkg
dotnet pack src/Mdl.Cli -o tools-packages --nologo -v q
dotnet tool uninstall -g Mdl.Cli >/dev/null 2>&1 || true
dotnet tool install -g Mdl.Cli --add-source ./tools-packages --prerelease
