#!/bin/bash
# Bring the finance stack up after a host boot / logon, then (with --hold) keep
# the WSL distro pinned alive for as long as the machine is running.
#
# Two separate problems are solved here.
#
# 1. WAIT FOR THE DAEMON
#    Nothing can be started until dockerd is actually accepting connections.
#    On a cold boot that can take a while.
#
# 2. KEEP THE DISTRO ALIVE  (--hold)
#    This is the important one. WSL tears down a distro's userspace once no
#    Windows process is attached to it -- systemd, dockerd and every container
#    die with it, and the next `wsl.exe` invocation silently re-inits the whole
#    thing. Observed directly on 2026-09-20: docker.service restarting every
#    ~35s, a fresh snapd PID each cycle, all five containers bouncing, while
#    `journalctl --list-boots` still showed a single boot. A 24/7 service
#    cannot live in a distro that evaporates between shell commands.
#
#    The fix is to hold one long-lived wsl.exe client open for the life of the
#    host. `--hold` does exactly that by never returning. The scheduled task
#    installed by install-startup-task.ps1 runs this script with --hold, so the
#    task process itself IS the keepalive -- do not give that task an execution
#    time limit.
#
# Safe to run by hand without --hold to just converge the stack.
set -u

HOLD=0
for arg in "$@"; do
    case "${arg}" in
        --hold) HOLD=1 ;;
        *) echo "unknown argument: ${arg}" >&2; exit 2 ;;
    esac
done

PROJECT_DIR="${PROJECT_DIR:-$(cd "$(dirname "$0")/.." && pwd)}"
DOCKER_WAIT_TIMEOUT_SECONDS="${DOCKER_WAIT_TIMEOUT_SECONDS:-600}"
LOG_FILE="${LOG_FILE:-${PROJECT_DIR}/startup.log}"

log() {
    echo "$(date -u +%FT%TZ) [startup] $*" | tee -a "${LOG_FILE}"
}

cd "${PROJECT_DIR}" || { echo "cannot cd to ${PROJECT_DIR}"; exit 1; }

log "starting; project=${PROJECT_DIR} hold=${HOLD}"

# 1. Wait for dockerd. systemd starts it at distro init, but not instantly.
waited=0
while true; do
    if docker version >/dev/null 2>&1; then
        log "docker daemon reachable after ${waited}s"
        break
    fi
    if [ "${waited}" -ge "${DOCKER_WAIT_TIMEOUT_SECONDS}" ]; then
        log "ERROR: docker unreachable after ${waited}s; giving up"
        exit 1
    fi
    if [ $((waited % 30)) -eq 0 ]; then
        log "waiting for docker daemon... (${waited}s)"
    fi
    sleep 5
    waited=$((waited + 5))
done

# 2. Converge. Starts anything stopped; no churn if all is already well.
log "docker compose up -d"
if ! docker compose up -d 2>&1 | sed 's/^/[compose] /' | tee -a "${LOG_FILE}"; then
    log "WARN: 'compose up' returned non-zero; will still check for dead services"
fi

sleep 5

# 3. Heal anything still down. Force-recreate rather than restart: a container
#    created while a bind source was missing has the bad path resolution baked
#    into its config, and a plain restart just reuses it.
mapfile -t ALL_SERVICES < <(docker compose config --services)
DEAD=()
for svc in "${ALL_SERVICES[@]}"; do
    cid="$(docker compose ps -q "${svc}" 2>/dev/null)"
    if [ -z "${cid}" ] || [ "$(docker inspect -f '{{.State.Running}}' "${cid}" 2>/dev/null)" != "true" ]; then
        DEAD+=("${svc}")
    fi
done

if [ "${#DEAD[@]}" -gt 0 ]; then
    log "not running after bring-up: ${DEAD[*]} — force-recreating"
    if docker compose up -d --force-recreate "${DEAD[@]}" 2>&1 | sed 's/^/[recreate] /' | tee -a "${LOG_FILE}"; then
        log "force-recreate finished"
    else
        log "ERROR: force-recreate failed for: ${DEAD[*]}"
    fi
else
    log "all services running"
fi

# 4. Final state, so the log alone tells you whether boot succeeded.
sleep 5
docker compose ps --format '{{.Name}}\t{{.State}}\t{{.Status}}' 2>/dev/null \
    | while IFS= read -r line; do log "final: ${line}"; done

if [ "${HOLD}" -eq 1 ]; then
    log "holding distro alive (this process must not exit)"
    # No busy loop, no log spam, no wakeups -- just never return.
    exec sleep infinity
fi

log "done"
