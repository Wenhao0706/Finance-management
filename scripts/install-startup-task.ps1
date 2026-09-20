# Installs the Windows Scheduled Task that runs the finance stack.
# Idempotent -- re-running replaces the existing task.
#
# The task does two jobs, both essential:
#
#   1. Brings the stack up once dockerd is reachable.
#   2. STAYS RUNNING FOREVER as the WSL keepalive. WSL tears a distro's
#      userspace down when no Windows process is attached to it, taking
#      systemd, dockerd and every container with it. The task process holds
#      one wsl.exe client open so that never happens. This is why the task has
#      NO execution time limit and MUST NOT be "fixed" to exit.
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

$Action = New-ScheduledTaskAction -Execute $Wsl -Argument "-d $Distro -e bash $Script --hold"

# ExecutionTimeLimit 0 == run forever. Required: this task IS the keepalive.
# MultipleInstances IgnoreNew so a logon after a startup-trigger run does not
# spawn a second copy.
$Settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -DontStopOnIdleEnd `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -MultipleInstances IgnoreNew `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 2)

# Keep running on battery too -- this machine is a server, not a laptop.
$Settings.DisallowStartIfOnBatteries = $false
$Settings.StopIfGoingOnBatteries     = $false

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

# Replace existing task if present.
if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

Register-ScheduledTask `
    -TaskName  $TaskName `
    -Action    $Action `
    -Trigger   $Triggers `
    -Settings  $Settings `
    -Principal $Principal `
    -Description "Brings the finance stack up after boot, then stays running as the WSL keepalive so the distro (and dockerd, and every container) is never torn down." | Out-Null

# Register-ScheduledTask can report failure without terminating, so confirm.
if (-not (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue)) {
    throw "Task '$TaskName' was not registered. Re-run this script as Administrator."
}

Write-Host "Installed scheduled task: $TaskName"
Write-Host "  mode: $Mode"
Write-Host "  runs: $Wsl -d $Distro -e bash $Script --hold"
Write-Host "  log:  $RepoPath/startup.log"
Write-Host ""
Write-Host "This task is expected to stay in the Running state forever -- that is the"
Write-Host "keepalive holding the WSL distro open. If it shows Ready, the stack is down."
if (-not $IsAdmin) {
    Write-Host ""
    Write-Host "NOTE: re-run this elevated to add the AtStartup trigger, so an unattended" -ForegroundColor Yellow
    Write-Host "      reboot brings the stack up with nobody logged in." -ForegroundColor Yellow
}
Write-Host ""
Write-Host "To start it now: Start-ScheduledTask -TaskName $TaskName"
