<# Removes the per-user shell override so Windows starts Explorer again at the next sign-in. #>
$key = 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Winlogon'
Remove-ItemProperty -Path $key -Name 'Shell' -ErrorAction SilentlyContinue
Write-Host "Shell override removed. Sign out and back in to get Explorer back." -ForegroundColor Green
Write-Host "If you are running vShell right now, use its right-click menu: 'Exit and start Windows Explorer'."
