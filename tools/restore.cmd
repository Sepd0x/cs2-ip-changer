@echo off
rem Removes the Windows Firewall rules left by CS2 IP Changer (ServerRouteTool v9 - ...).
rem Use it if the app is gone or won't open. Routes are temporary: a restart clears them.

fltmc >nul 2>&1
if errorlevel 1 (
  echo Run this as administrator: right-click restore.cmd and pick "Run as administrator".
  pause
  exit /b 1
)

"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -Command "$r = Get-NetFirewallRule -DisplayName 'ServerRouteTool v9*' -ErrorAction SilentlyContinue; if ($r) { $r | Remove-NetFirewallRule; Write-Host ('Removed ' + @($r).Count + ' rule(s).') } else { Write-Host 'No CS2 IP Changer rules found.' }"

echo Restart the PC to clear any routes the app added.
pause
