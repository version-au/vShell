# build.cmd / plugins.cmd: the update site's files (https://vsuite.vrsion.com) in dist\site\ - copy that folder over the site's root.
#   dist\site\vshell\                 vShell.exe, vShell-setup.exe, latest.json, changelog.md
#   dist\site\vshell\plugins\<API>\   plugins.json and EVERY plugin DLL it lists (only the newest is kept on the site, so all of them -
#                                     an unchanged one is the very same file as before)
# -Pre: a pre-release, in dist\site\vshell\pre\ instead. -PluginsOnly (plugins.cmd): just the plugins folder.
param([switch]$Pre, [switch]$PluginsOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
Set-Location $root

$fv = (Get-Item 'dist\vShell.exe').VersionInfo.FileVersion
$version = [regex]::Match($fv, '^\d+\.\d+\.\d+').Value
$api = [regex]::Match((Get-Content -Raw 'dist\plugin-api.txt'), 'plugin API (\d+)').Groups[1].Value
if (-not $api) { throw 'dist\plugin-api.txt has no plugin API version' }

if (Test-Path 'dist\site') { Remove-Item -Recurse -Force 'dist\site' }
$folder = 'dist\site\vshell'
if ($Pre) { $folder = "$folder\pre" }

if (-not $PluginsOnly) {
  $setup = ''
  if (Test-Path 'dist\setup\vShell-setup.exe') { $setup = 'dist\setup\vShell-setup.exe' }
  & (Join-Path $root 'vSuite.Core\scripts\site-release.ps1') -App vShell -Version $version -Install 'dist\vShell.exe' -Installer $setup `
      -Notes 'dist\release-notes.md' -Changelog 'CHANGELOG.md' -Out 'dist\site' -Pre:$Pre -Extra ('{"plugin_api":' + $api + '}')
}

# the Plugin SDK (dist\sdk\: vShell.dll, plugin-api.txt, plugins.md) as sdk.zip - Settings > About > Plugin SDK downloads it.
# Every build and plugins.cmd makes it again (with the newest docs\plugins.md), so the one on the site always matches.
if (Test-Path 'dist\sdk\vShell.dll') {
  Copy-Item 'docs\plugins.md' 'dist\sdk\' -Force
  New-Item -ItemType Directory -Force $folder | Out-Null
  Compress-Archive -Path 'dist\sdk\*' -DestinationPath "$folder\sdk.zip" -CompressionLevel Optimal -Force
  Write-Host "Update site: $folder\sdk.zip (the plugin SDK)"
}

# the plugins: plugins.json and every DLL in it
$plugins = "$folder\plugins\$api"
New-Item -ItemType Directory -Force $plugins | Out-Null
Copy-Item 'dist\plugins.json' $plugins -Force
$n = 0
foreach ($e in (Get-Content -Raw 'dist\plugins.json' | ConvertFrom-Json).plugins) {
  $dll = "dist\$($e.file).dll"
  if (-not (Test-Path $dll)) { throw "$dll is in plugins.json but isn't in dist\" }
  Copy-Item $dll $plugins -Force
  $n++
}
Write-Host "Update site: $plugins ($n plugins)"
Write-Host ''
Write-Host "For the update site: copy everything in $root\dist\site\ over the site's root (the files there are replaced)." -ForegroundColor Green
