#!/usr/bin/env pwsh
# Dev wrapper: run tomix from source without packing/installing. `.\Tomix.ps1 <args>`.
dotnet run --project "$PSScriptRoot/src/Tomix.Cli" -v quiet -- @args
exit $LASTEXITCODE
