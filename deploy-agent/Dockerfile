# Lightweight auto-deploy agent. Polls origin/main every POLL_INTERVAL_SECONDS;
# when a new commit is detected, pulls and rebuilds the backend + frontend
# containers. Talks to the host Docker daemon via the mounted socket.
#
# Why this design:
#   - No public webhook endpoint to secure
#   - Survives reboots automatically (Docker restart policy)
#   - Single-machine self-hosted setup — no need for image registry
#   - Polling interval of 5 minutes is a good balance: fast enough to feel
#     "automatic" without spamming GitHub
FROM docker:27-cli-alpine3.20

RUN apk add --no-cache git bash

COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

ENTRYPOINT ["/entrypoint.sh"]
