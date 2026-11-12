#!/usr/bin/env pwsh
# Builds the DACPACs and publishes them to the SQL Server started by docker-compose.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet tool restore
foreach ($db in 'ClientRegistry', 'Scheduling') {
    dotnet build "database/$db/$db.sqlproj" -c Debug
    dotnet sqlpackage /Action:Publish /SourceFile:"database/$db/bin/Debug/$db.dacpac" `
        /TargetConnectionString:"Server=localhost,1433;Database=$db;User Id=sa;Password=Your_strong_password1;TrustServerCertificate=True" `
        /v:AppIdentityName=local-not-used
}
