# Run as Administrator. Installs a Windows Scheduled Task that runs the
# Postgres backup script nightly at 03:30. Idempotent — re-running replaces
# the existing task.

$ErrorActionPreference = "Stop"

$TaskName  = "FinanceManagement-Postgres-Backup"
$RepoRoot  = Split-Path -Parent $PSScriptRoot
$Bash      = "C:\Program Files\Git\bin\bash.exe"
$Script    = Join-Path $RepoRoot "scripts\backup-postgres.sh"

if (-not (Test-Path $Bash))   { throw "Git Bash not found at $Bash. Install Git for Windows or edit this script." }
if (-not (Test-Path $Script)) { throw "Backup script not found at $Script." }

# Convert backslashes to forward slashes for MSYS bash invocation.
$ScriptForBash = $Script -replace '\\','/'

$Action  = New-ScheduledTaskAction -Execute $Bash -Argument "-lc `"$ScriptForBash`""
$Trigger = New-ScheduledTaskTrigger -Daily -At 3:30AM
$Settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -DontStopOnIdleEnd -RestartCount 2 -RestartInterval (New-TimeSpan -Minutes 5)
$Principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType S4U -RunLevel Limited

# Replace existing task if present.
if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

Register-ScheduledTask `
    -TaskName    $TaskName `
    -Action      $Action `
    -Trigger     $Trigger `
    -Settings    $Settings `
    -Principal   $Principal `
    -Description "Nightly pg_dump of finance Postgres container, gzipped to D:\backups\finance with 30-day retention."

Write-Host "Installed scheduled task: $TaskName"
Write-Host "Next run: $((Get-ScheduledTask -TaskName $TaskName | Get-ScheduledTaskInfo).NextRunTime)"
Write-Host "To run now: Start-ScheduledTask -TaskName $TaskName"
