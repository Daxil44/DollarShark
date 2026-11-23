#!/bin/sh
# Deploy DollarShark on the server from the images GitHub Actions built (ghcr.io).
#
#   sh deploy.sh            newest build ("latest")
#   sh deploy.sh 1a2b3c4    a specific build: the commit id (7+ characters) — this is also how you ROLL BACK
#
# Steps: pull images → restart containers → health check → if unhealthy, automatically go back to the previous build.
# By hand, run "git pull" first so the compose file matches the build.
#
# GitHub Actions runs this over SSH with a key that may only run "git pull && sh deploy.sh" (docs/deployment.md § 8).
# Then the commit id arrives in SSH_ORIGINAL_COMMAND, and a registry login valid only while the job runs on standard input.
set -eu
cd "$(dirname "$0")"

TAG="${1:-${SSH_ORIGINAL_COMMAND:-latest}}"
if ! echo "$TAG" | grep -Eq '^(latest|[0-9a-f]{7,40})$'; then
    echo "Usage: sh deploy.sh [latest | commit-id]"; exit 2
fi

if [ -n "${SSH_ORIGINAL_COMMAND:-}" ]; then
    read -r REGISTRY_USER
    read -r REGISTRY_TOKEN
    printf '%s' "$REGISTRY_TOKEN" | docker login ghcr.io -u "$REGISTRY_USER" --password-stdin > /dev/null
    trap 'docker logout ghcr.io > /dev/null 2>&1 || true' EXIT
fi

[ -f .env ] || { echo "No .env here: run this from the DollarShark folder on the server."; exit 1; }

set_tag() {
    if grep -q '^IMAGE_TAG=' .env; then
        sed -i "s/^IMAGE_TAG=.*/IMAGE_TAG=$1/" .env
    else
        printf '\n# Which build is running (set by deploy.sh)\nIMAGE_TAG=%s\n' "$1" >> .env
    fi
}

# The api must answer /health (checked from inside the web container) within HEALTH_TIMEOUT seconds.
# 5 minutes by default: on a 512 MB server, .NET can take over 2 minutes to start right after new images are pulled.
HEALTH_TIMEOUT="${HEALTH_TIMEOUT:-300}"
healthy() {
    started=$(date +%s)
    while [ $(( $(date +%s) - started )) -lt "$HEALTH_TIMEOUT" ]; do
        if docker compose exec -T web wget -q -T 5 -O- http://api:8080/health 2>/dev/null | grep -q healthy; then
            echo "    healthy after $(( $(date +%s) - started ))s"
            return 0
        fi
        sleep 5
    done
    echo "    no healthy answer within ${HEALTH_TIMEOUT}s"
    return 1
}

PREVIOUS=$(sed -n 's/^IMAGE_TAG=//p' .env | tail -n 1)
echo "==> Deploying build: $TAG (running before: ${PREVIOUS:-unknown})"

set_tag "$TAG"
if ! docker compose pull --quiet api web backup; then
    echo "!! Could not download the images for $TAG. Nothing was changed."
    [ -n "$PREVIOUS" ] && set_tag "$PREVIOUS"
    exit 1
fi
docker compose up -d

echo "==> Health check..."
if healthy; then
    echo "==> Deployed $TAG — the site is healthy."
    # Free disk space: delete unused images older than 7 days (newer ones stay for quick rollbacks).
    docker image prune -af --filter "until=168h" > /dev/null || true
    exit 0
fi

echo "!! $TAG is NOT healthy. Recent api logs:"
docker compose logs --tail 30 api || true
if [ -n "$PREVIOUS" ] && [ "$PREVIOUS" != "$TAG" ]; then
    echo "==> Rolling back to $PREVIOUS..."
    set_tag "$PREVIOUS"
    docker compose up -d
    if healthy; then
        echo "==> Rolled back to $PREVIOUS — the site is healthy again. Fix the problem and deploy again."
    else
        echo "!! Rollback is not healthy either. Check: docker compose ps / docker compose logs api"
    fi
fi
exit 1
