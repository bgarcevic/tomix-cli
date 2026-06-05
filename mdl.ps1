#!/usr/bin/env pwsh
# Dev wrapper: run mdl from source without packing/installing. `.\mdl.ps1 <args>`.
dotnet run --project "$PSScriptRoot/src/Mdl.Cli" -v quiet -- @args
exit $LASTEXITCODE
