# build.cmd: compare this build's plugin API (dist\plugin-api.txt) with the saved one (docs\plugin-api.txt).
# Something gone or changed = plugins built for this API version would break: stop, unless the API version went up on purpose.
param([string]$New = 'dist\plugin-api.txt', [string]$Saved = 'docs\plugin-api.txt')
if (-not (Test-Path $New)) { Write-Host 'PLUGIN API: dist\plugin-api.txt was not written'; exit 1 }
$n = @(Get-Content $New)
if (-not (Test-Path $Saved)) { Copy-Item $New $Saved; Write-Host "Plugin API: saved as $Saved (first time)"; exit 0 }
$s = @(Get-Content $Saved)
$apiNew = ($n[0] -replace '^# vShell plugin API (\d+).*$', '$1'); $apiSaved = ($s[0] -replace '^# vShell plugin API (\d+).*$', '$1')
$nb = $n | Select-Object -Skip 1; $sb = $s | Select-Object -Skip 1
# -c: case matters (Fg and FG are different members to .NET)
$gone = @($sb | Where-Object { $nb -cnotcontains $_ })
$added = @($nb | Where-Object { $sb -cnotcontains $_ })
if ($gone.Count -gt 0 -and $apiNew -eq $apiSaved) {
  Write-Host ''
  Write-Host "PLUGIN API CHANGED - plugins built for API $apiSaved use these, and they went or changed:"
  $gone | ForEach-Object { Write-Host "  - $_" }
  if ($added.Count -gt 0) { Write-Host 'Now there is:'; $added | ForEach-Object { Write-Host "  + $_" } }
  Write-Host ''
  Write-Host 'Put them back (add the new form next to the old one), or if it has to break: raise PluginApi.Version and add the version to'
  Write-Host 'PluginApi.Since (src\Core\PluginManifest.cs) - every plugin then needs a rebuild.'
  exit 1
}
Copy-Item $New $Saved -Force
if ($apiNew -ne $apiSaved) { Write-Host "Plugin API: now version $apiNew (was $apiSaved) - plugins need a rebuild for it" }
elseif ($added.Count -gt 0) { Write-Host ("Plugin API: " + @($added).Count + " new (saved in $Saved - commit it)") }
else { Write-Host 'Plugin API: unchanged' }
exit 0
