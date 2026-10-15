#!/usr/bin/env bash
# The dev compose (builds from source) and the prod compose (pulls images) must stay the same stack:
# same services, environment keys, dependencies, healthchecks, ports and volume targets. Only images/builds and host paths may differ.
# Needs docker compose and jq. Usage: scripts/check-compose-drift.sh <dev.env> <prod.env>
set -euo pipefail

normalise() { # compose file, env file
  docker compose -f "$1" --env-file "$2" config --format json | jq -S '
    .services | map_values({
      environment: (.environment // {} | keys),
      depends_on: (.depends_on // {} | keys),
      healthcheck: .healthcheck,
      command: .command,
      ports: [.ports[]? | {published, target}],
      volumes: [.volumes[]? | .target]
    })'
}

diff <(normalise docker-compose.yml "$1") <(normalise deploy/docker-compose.prod.yml "$2") \
  && echo "dev and prod compose describe the same stack"
