# Installs a Windows Scheduled Task that brings the finance stack up at logon,
# once Docker Desktop's WSL socket actually exists. Idempotent -- re-running
# replaces the existing task.
#
# Why a task and not just `restart: unless-stopped`:
#   Docker Desktop starts accepting commands before the Ubuntu distro's
#   /var/run/docker.sock is re-created. A container created in that window
#   bakes a dead bind-mount resolution into its config and then fails on every
#   restart-policy retry, forever. Healing it requires a force-recreate, which
#   is what scripts/ensure-stack-up.sh does.
#
# Run elevated if you can: only an Administrator may register the AtStartup
# trigger and the S4U principal, which together let the stack come up without
# an interactive logon. Unelevated still works, but only once someone logs in.

param(
    # Absolute path of the repo INSIDE the WSL distro (not a \\wsl.localhost UNC
    # path -- the task invokes wsl.exe, so the distro's own view is what counts).
    [string]$RepoPath = "/home/wenhao/Finance-management",
    [string]$Distro   = "Ubuntu"
)

$ErrorActionPreference = "Stop"

$TaskName = "FinanceManagement-Startup"
$Wsl      = "$env:SystemRoot\System32\wsl.exe"
$Script   = "$RepoPath/scripts/ensure-stack-up.sh"

if (-not (Test-Path $Wsl)) { throw "wsl.exe not found at $Wsl." }

# Fail loudly now rather than silently at 3am after a reboot.
& $Wsl -d $Distro -e test -f $Script
if ($LASTEXITCODE -ne 0) { throw "Startup script not found in distro '$Distro' at $Script." }

$IsAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

$Action = New-ScheduledTaskAction -Execute $Wsl -Argument "-d $Distro -e bash $Script"

# Docker Desktop can take minutes on a cold boot; the script polls for up to
# 10 minutes, so do not let the task engine kill it early.
$Settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -DontStopOnIdleEnd `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 30) `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 2)

if ($IsAdmin) {
    # AtStartup fires without anyone logging in; S4U lets the task run in that
    # state. Both require elevation to register.
    $Triggers  = @((New-ScheduledTaskTrigger -AtLogOn), (New-ScheduledTaskTrigger -AtStartup))
    $Principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType S4U -RunLevel Limited
    $Mode      = "elevated (AtLogOn + AtStartup, runs without an interactive logon)"
} else {
    $Triggers  = @((New-ScheduledTaskTrigger -AtLogOn))
    $Principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
    $Mode      = "unelevated (AtLogOn only -- the stack stays down until someone logs in)"
}

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

Register-ScheduledTask `
    -TaskName  $TaskName `
    -Action    $Action `
    -Trigger   $Triggers `
    -Settings  $Settings `
    -Principal $Principal `
    -Description "Waits for the Docker daemon after boot/logon, then brings the finance stack up and force-recreates any service that lost the WSL docker.sock race." | Out-Null

# Register-ScheduledTask can report failure without terminating, so confirm.
if (-not (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue)) {
    throw "Task '$TaskName' was not registered. Re-run this script as Administrator."
}

Write-Host "Installed scheduled task: $TaskName"
Write-Host "  mode: $Mode"
Write-Host "  runs: $Wsl -d $Distro -e bash $Script"
Write-Host "  log:  $RepoPath/startup.log"
if (-not $IsAdmin) {
    Write-Host ""
    Write-Host "NOTE: re-run this elevated to add the AtStartup trigger, so an unattended" -ForegroundColor Yellow
    Write-Host "      reboot brings the stack up with nobody logged in." -ForegroundColor Yellow
}
Write-Host ""
Write-Host "To test now: Start-ScheduledTask -TaskName $TaskName"
