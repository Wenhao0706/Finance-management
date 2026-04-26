#!/bin/bash
# Auto-deploy poll loop. Checks origin/<branch> every POLL_INTERVAL_SECONDS
# for new commits; pulls + rebuilds backend + frontend containers when
# detected. Runs in the foreground so Docker captures stdout via its log driver.
#
# Required mounts (set in docker-compose.yml):
#   /var/run/docker.sock  — talk to host Docker daemon (rebuild containers)
#   /workspace            — the project directory (git pull + compose context)
#
# Required env:
#   POLL_INTERVAL_SECONDS  — polling cadence (default 300 = 5 minutes)
#   GIT_BRANCH             — branch to track (default main)
#   COMPOSE_SERVICES       — services to rebuild on change (default "backend frontend")
set -eu

POLL_INTERVAL_SECONDS="${POLL_INTERVAL_SECONDS:-300}"
GIT_BRANCH="${GIT_BRANCH:-main}"
COMPOSE_SERVICES="${COMPOSE_SERVICES:-backend frontend}"

cd /workspace

# Mark workspace as safe — Docker mounts often have UID mismatches that
# trigger git's "dubious ownership" error on otherwise-fine repos.
git config --global --add safe.directory /workspace

log() {
    echo "$(date -u +%FT%TZ) [deploy-agent] $*"
}

log "starting; poll every ${POLL_INTERVAL_SECONDS}s on branch '${GIT_BRANCH}'; rebuilds: ${COMPOSE_SERVICES}"

while true; do
    if ! git fetch origin "${GIT_BRANCH}" 2>&1 | sed 's/^/[git fetch] /'; then
        log "git fetch failed; will retry on next interval"
        sleep "${POLL_INTERVAL_SECONDS}"
        continue
    fi

    LOCAL=$(git rev-parse HEAD)
    REMOTE=$(git rev-parse "origin/${GIT_BRANCH}")

    if [ "${LOCAL}" != "${REMOTE}" ]; then
        log "new commits detected (local=${LOCAL:0:7} remote=${REMOTE:0:7}); pulling + rebuilding"

        if git pull --ff-only origin "${GIT_BRANCH}" 2>&1 | sed 's/^/[git pull] /'; then
            # shellcheck disable=SC2086
            if docker compose up -d --build ${COMPOSE_SERVICES} 2>&1 | sed 's/^/[compose] /'; then
                log "rebuild complete — now at $(git rev-parse --short HEAD)"
            else
                log "rebuild failed; site may be in mixed state — manual intervention may be needed"
            fi
        else
            log "git pull failed (non-ff?); skipping rebuild"
        fi
    fi

    sleep "${POLL_INTERVAL_SECONDS}"
done
