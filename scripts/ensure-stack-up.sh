#!/bin/bash
# Bring the finance stack up after a host boot / user logon, once the Docker
# daemon is actually reachable.
#
# Why this exists
# ---------------
# The containers all carry `restart: unless-stopped`, which is enough when the
# daemon is healthy. It is NOT enough on Docker Desktop + WSL, because the
# daemon starts accepting work before the Ubuntu distro's /var/run/docker.sock
# has been re-created. A container whose bind source is missing at CREATE time
# gets a resolved host path baked into its config:
#
#   error mounting ".../docker-desktop-bind-mounts/Ubuntu/docker.sock"
#   to rootfs at "/var/run/docker.sock": not a directory
#
# Every subsequent restart-policy retry reuses that same dead resolution, so
# the container never recovers on its own — it just fails forever. On
# 2026-09-20 the deploy-agent lost this race by 23 seconds and stayed down for
# hours while the rest of the stack looked fine.
#
# Restarting is therefore not enough; the container has to be RE-CREATED so the
# path is resolved again. This script waits for the daemon, brings the stack
# up, and force-recreates only the services that still are not running.
#
# Invoked by the "FinanceManagement-Startup" scheduled task at logon; safe to
# run by hand at any time.
set -u

PROJECT_DIR="${PROJECT_DIR:-$(cd "$(dirname "$0")/.." && pwd)}"
DOCKER_WAIT_TIMEOUT_SECONDS="${DOCKER_WAIT_TIMEOUT_SECONDS:-600}"
SETTLE_SECONDS="${SETTLE_SECONDS:-15}"
LOG_FILE="${LOG_FILE:-${PROJECT_DIR}/startup.log}"

log() {
    echo "$(date -u +%FT%TZ) [startup] $*" | tee -a "${LOG_FILE}"
}

cd "${PROJECT_DIR}" || { echo "cannot cd to ${PROJECT_DIR}"; exit 1; }

log "starting; project=${PROJECT_DIR}"

# 1. Wait for the daemon. Not just `docker version` — we specifically need the
#    socket the compose file binds into the deploy-agent to exist, since that
#    is the mount that loses the race.
waited=0
while true; do
    if docker version >/dev/null 2>&1 && [ -S /var/run/docker.sock ]; then
        log "docker daemon reachable and /var/run/docker.sock present after ${waited}s"
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

# 2. Let Docker Desktop finish wiring up WSL integration. Coming back the
#    instant the socket appears is how the race was lost in the first place.
log "letting docker settle for ${SETTLE_SECONDS}s"
sleep "${SETTLE_SECONDS}"

# 3. Normal bring-up. Starts anything stopped; no churn if all is already well.
log "docker compose up -d"
if ! docker compose up -d 2>&1 | sed 's/^/[compose] /' | tee -a "${LOG_FILE}"; then
    log "WARN: 'compose up' returned non-zero; will still check for dead services"
fi

sleep 5

# 4. Heal anything still down. A plain restart reuses the broken mount
#    resolution, so these need --force-recreate to re-resolve the bind source.
mapfile -t ALL_SERVICES < <(docker compose config --services)
DEAD=()
for svc in "${ALL_SERVICES[@]}"; do
    cid="$(docker compose ps -q "${svc}" 2>/dev/null)"
    if [ -z "${cid}" ] || [ "$(docker inspect -f '{{.State.Running}}' "${cid}" 2>/dev/null)" != "true" ]; then
        DEAD+=("${svc}")
    fi
done

if [ "${#DEAD[@]}" -gt 0 ]; then
    log "not running after bring-up: ${DEAD[*]} — force-recreating to re-resolve bind mounts"
    if docker compose up -d --force-recreate "${DEAD[@]}" 2>&1 | sed 's/^/[recreate] /' | tee -a "${LOG_FILE}"; then
        log "force-recreate finished"
    else
        log "ERROR: force-recreate failed for: ${DEAD[*]}"
    fi
else
    log "all services running"
fi

# 5. Final state, so the log alone tells you whether boot succeeded.
sleep 5
docker compose ps --format '{{.Name}}\t{{.State}}\t{{.Status}}' 2>/dev/null \
    | while IFS= read -r line; do log "final: ${line}"; done

log "done"
