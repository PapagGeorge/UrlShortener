#!/usr/bin/env bash
# Pull-based deploy: GitHub never reaches into this box. Run by urlshortener-update.timer (and by hand for a rollback).
# Pulls the published images, restarts only what changed, waits until the stack is healthy. Never touches volumes.
set -euo pipefail

# Everything lives in a function and the script exits right after calling it: `git pull` below may rewrite this very file,
# and bash reads a script incrementally, so nothing may be left to read once the pull has run.
main() {
  cd "$(dirname "$0")"

  # One update at a time (the timer must never overlap a slow pull).
  exec 9>/tmp/urlshortener-update.lock
  flock -n 9 || { echo "another update is running, skipping"; return 0; }

  [ -f .env ] || { echo "deploy/.env is missing (copy .env.example and fill it in)"; return 1; }

  # Keep the compose file, init scripts and this script in step with main (fast-forward only; fails instead of merging).
  git -C .. pull --ff-only --quiet

  docker compose -f docker-compose.prod.yml pull --quiet
  docker compose -f docker-compose.prod.yml up -d --remove-orphans --wait --wait-timeout 180
  docker image prune -f >/dev/null

  echo "updated:"
  docker compose -f docker-compose.prod.yml ps --format '{{.Service}} {{.Image}} {{.Status}}'
}

main "$@"
exit $?
