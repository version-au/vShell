<#
 Builds vShell into .\dist\vShell.exe
   .\build.ps1                  framework-dependent (needs the .NET 10 Desktop Runtime installed)
   .\build.ps1 -SelfContained   bundles the runtime (larger exe, nothing else to install - best for shell mode)
#>
param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $sc = if ($SelfContained) { 'true' } else { 'false' }
    dotnet publish vShell.csproj -c Release -r win-x64 --self-contained $sc `
        -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o dist
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
    $h = (Get-FileHash -Algorithm SHA256 'dist\vShell.exe').Hash.ToLower()
    Set-Content -Encoding ascii -NoNewline -Path 'dist\vShell.exe.sha256' -Value ($h + '  vShell.exe')
    & (Join-Path $PSScriptRoot 'scripts\release-notes.ps1') -Root $PSScriptRoot
    Write-Host "`nBuilt: $PSScriptRoot\dist\vShell.exe" -ForegroundColor Green
}
finally { Pop-Location }
