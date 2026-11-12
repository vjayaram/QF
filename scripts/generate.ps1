#!/usr/bin/env pwsh
# Regenerates code and SQL from models/*.model.json. Run after editing a model; commit the result.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet run --project tools/Qf.ModelGen -- --all
