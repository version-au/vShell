# build.cmd / plugins.cmd: the plugins into dist\ (each with its .sha256) - only the ones that changed since they were last built.
# An unchanged one keeps its DLL from before (the very same file, so nobody is offered an "update" that isn't one) and stays in the
# release it went into: dist\plugin-builds.txt remembers what each was built from and which vShell version's release has it
# (plugins.json says so). scripts\site.ps1 puts every plugin on the update site.
# -All builds every plugin again (build.cmd all / plugins.cmd all).
param([switch]$All)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)

if (-not (Test-Path 'dist\vShell.exe') -or -not (Test-Path 'dist\plugin-api.txt')) { Write-Host 'dist\vShell.exe or dist\plugin-api.txt is missing - run build.cmd first.'; exit 1 }
# the release these go into: the vShell in dist\ (its version) - and its plugin API (a new API needs every plugin built again)
$fv = (Get-Item 'dist\vShell.exe').VersionInfo.FileVersion
$release = [regex]::Match($fv, '^\d+\.\d+\.\d+').Value
$api = [regex]::Match((Get-Content -Raw 'dist\plugin-api.txt'), 'plugin API (\d+)').Groups[1].Value

$stateFile = 'dist\plugin-builds.txt'
$state = @{}
if (Test-Path $stateFile) { foreach ($l in Get-Content $stateFile) { $p = $l.Split('|'); if ($p.Count -eq 3) { $state[$p[0]] = $p } } }

function Hash($file) { return (Get-FileHash -Algorithm SHA256 $file).Hash.ToLower() }

# what a plugin is built from: every file in its folder (not bin\ / obj\) and the plugin API
function KeyOf($dir) {
  $base = (Resolve-Path $dir).Path
  $sb = New-Object Text.StringBuilder
  Get-ChildItem $base -Recurse -File | Where-Object { $_.FullName.Substring($base.Length) -notmatch '^\\(bin|obj)\\' } | Sort-Object FullName | ForEach-Object {
    [void]$sb.Append($_.FullName.Substring($base.Length)).Append('=').Append((Hash $_.FullName)).Append(';')
  }
  [void]$sb.Append('api=').Append($api)
  $sha = [Security.Cryptography.SHA256]::Create()
  return -join ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($sb.ToString())) | ForEach-Object { $_.ToString('x2') })
}

# a DLL now in dist\: its checksum, and remembered
function Put($dll, $key) {
  $f = "dist\$dll"
  Set-Content -Encoding ascii -NoNewline -Path "$f.sha256" -Value ((Hash $f) + "  $dll")
  $script:state[$dll] = @($dll, $key, $release)
}

# signed with the Certum certificate when build.cmd / plugins.cmd sign (their SIGN / SIGNREQ): before its checksum, so the site's is the
# signed file's. Your own plugins only - others' (third-party\) stay as their authors made them. sign.ps1 exit 2 = no certificate, not
# required: unsigned (a local build); 1 = failed.
function Sign($file) {
  if ($env:SIGN -ne '1') { return }
  $args2 = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'vSuite.Core\scripts\sign.ps1')
  if ($env:SIGNREQ) { $args2 += '-Require' }
  & powershell @args2 $file
  if ($LASTEXITCODE -eq 2) { $env:SIGN = ''; return }
  if ($LASTEXITCODE -ne 0) { Write-Host ''; Write-Host "PLUGIN SIGNING FAILED: $file - see above."; exit 1 }
}
function Unchanged($dll, $key) {
  $was = $state[$dll]
  return (-not $All) -and $was -and $was[1] -eq $key -and (Test-Path "dist\$dll") -and (Test-Path "dist\$dll.sha256")
}

$built = 0; $kept = 0
foreach ($dir in Get-ChildItem 'plugins' -Directory) {
  foreach ($proj in Get-ChildItem $dir.FullName -Filter '*.csproj') {
    $dll = $proj.BaseName + '.dll'
    $key = KeyOf $dir.FullName
    if (Unchanged $dll $key) { Write-Host "Plugin unchanged: $dll (stays in the $($state[$dll][2]) release)"; $kept++; continue }
    & dotnet build $proj.FullName -c Release -p:DebugType=none
    if ($LASTEXITCODE -ne 0) { Write-Host ''; Write-Host "PLUGIN BUILD FAILED: $($proj.FullName) - copy the errors above and send them to me."; exit 1 }
    Copy-Item (Join-Path $dir.FullName "bin\Release\net10.0-windows\$dll") 'dist\' -Force
    Sign "dist\$dll"
    Put $dll $key
    Write-Host "Plugin built: $((Resolve-Path "dist\$dll").Path)"; $built++
  }
}
# others' plugins you've accepted (third-party\*.dll): shared through the release like yours - when the file changed
if (Test-Path 'third-party\*.dll') {
  foreach ($f in Get-ChildItem 'third-party' -Filter '*.dll') {
    $key = Hash $f.FullName
    if (Unchanged $f.Name $key) { Write-Host "Third-party plugin unchanged: $($f.Name) (stays in the $($state[$f.Name][2]) release)"; $kept++; continue }
    Copy-Item $f.FullName 'dist\' -Force
    Put $f.Name $key
    Write-Host "Third-party plugin: $((Resolve-Path "dist\$($f.Name)").Path)"; $built++
  }
}

Set-Content -Encoding ascii -Path $stateFile -Value ($state.Keys | Sort-Object | ForEach-Object { $state[$_] -join '|' })
Write-Host "Plugins: $built built, $kept unchanged."
exit 0
