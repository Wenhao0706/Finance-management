#!/bin/bash
# Auto-deploy poll loop. Checks origin/<branch> every POLL_INTERVAL_SECONDS
# for new commits; pulls + rebuilds backend + frontend containers when
# detected. Runs in the foreground so Docker captures stdout via its log driver.
#
# Required mounts (set in docker-compose.yml):
#   /var/run/docker.sock  — talk to host Docker daemon (rebuild containers)
#   $PROJECT_DIR          — the project directory (git pull + compose context).
#                           MUST be mounted at the same absolute path it has on
#                           the host: we drive the host's Docker daemon over the
#                           socket, so that daemon resolves relative bind-mount
#                           sources in docker-compose.yml against the HOST
#                           filesystem, not ours. Mounting the repo somewhere
#                           else (the old hardcoded /workspace) made
#                           `./backend/x` resolve to a host path that does not
#                           exist -- and Docker silently creates an empty
#                           DIRECTORY for a missing bind source instead of
#                           failing, which is how a missing Firebase key turned
#                           into 500s on every authenticated request.
#
# Required env:
#   POLL_INTERVAL_SECONDS  — polling cadence (default 300 = 5 minutes)
#   GIT_BRANCH             — branch to track (default main)
#   COMPOSE_SERVICES       — services to rebuild on change (default "backend frontend")
#   PROJECT_DIR            — absolute path of the repo, identical on the host
#                            and in here (default: the working dir)
set -eu
# git/compose output is piped through `sed` for log prefixing. Without pipefail
# the pipeline's status is sed's (always 0), so a failed fetch/pull/rebuild was
# reported as success — "rebuild complete" was logged even when the build blew
# up. Every pipeline below sits in an `if`, so pipefail cannot kill the loop.
set -o pipefail

POLL_INTERVAL_SECONDS="${POLL_INTERVAL_SECONDS:-300}"
GIT_BRANCH="${GIT_BRANCH:-main}"
COMPOSE_SERVICES="${COMPOSE_SERVICES:-backend frontend}"

PROJECT_DIR="${PROJECT_DIR:-$PWD}"
cd "${PROJECT_DIR}"

# Mark the repo as safe — Docker mounts often have UID mismatches that
# trigger git's "dubious ownership" error on otherwise-fine repos.
# --add is not idempotent and /root/.gitconfig lives in the container's
# writable layer, so a plain `--add` appended a duplicate line on every
# container restart (observed: 7 identical entries after 7 restarts).
if ! git config --global --get-all safe.directory 2>/dev/null | grep -qxF "${PROJECT_DIR}"; then
    git config --global --add safe.directory "${PROJECT_DIR}"
fi

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
