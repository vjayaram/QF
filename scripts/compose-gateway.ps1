#!/usr/bin/env pwsh
# Composes the Fusion gateway configuration from every subgraph.
# 1. Export each subgraph's schema (no running server needed).
# 2. Pack schema + subgraph-config.json into a .fsp package.
# 3. Compose all packages into src/Gateway/Qf.Gateway/gateway.fgp.
# Composition fails on conflicting types or broken entity resolvers: that is the federation contract test.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet tool restore

$subgraphs = @(
    'src/Services/ClientRegistry/Qf.ClientRegistry.Api',
    'src/Services/Scheduling/Qf.Scheduling.Api'
)
$gateway = 'src/Gateway/Qf.Gateway/gateway.fgp'
if (Test-Path $gateway) { Remove-Item $gateway }

foreach ($sg in $subgraphs) {
    dotnet run --project $sg --no-launch-profile -- schema export --output (Join-Path $sg 'schema.graphql')
    if ($LASTEXITCODE -ne 0) { throw "Schema export failed for $sg" }
    dotnet fusion subgraph pack -w $sg
    if ($LASTEXITCODE -ne 0) { throw "Subgraph pack failed for $sg" }
}

$packages = $subgraphs | ForEach-Object { Get-ChildItem $_ -Filter *.fsp | Select-Object -First 1 -ExpandProperty FullName }
dotnet fusion compose -p $gateway -s $packages
if ($LASTEXITCODE -ne 0) { throw 'Fusion composition failed' }
Write-Host "Composed $gateway from $($packages.Count) subgraphs."

# The .fgp package (OPC/zip) carries the public composed schema; export it for APIM request validation.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $gateway))
try {
    $entry = $zip.Entries | Where-Object { $_.FullName -eq 'schema.graphql' } | Select-Object -First 1
    if (-not $entry) { throw 'schema.graphql not found in gateway.fgp' }
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path (Split-Path $gateway) 'composed-schema.graphql'), $true)
}
finally { $zip.Dispose() }

# Committed snapshot of the public schema. CI regenerates it, fails if it differs from the commit, and diffs
# main's snapshot against it with GraphQL Inspector (contracts/scripts/check-graphql-breaking.sh).
$snapshot = 'contracts/graphql/composed-schema.graphql'
New-Item -ItemType Directory -Force -Path (Split-Path $snapshot) | Out-Null
$content = (Get-Content (Join-Path (Split-Path $gateway) 'composed-schema.graphql') -Raw) -replace "`r`n", "`n"
[System.IO.File]::WriteAllText((Join-Path (Get-Location) $snapshot), $content, [System.Text.UTF8Encoding]::new($false))
Write-Host "Updated $snapshot. Commit it with your change."

