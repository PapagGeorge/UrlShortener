#!/bin/sh
# Runs once, on an empty data directory (the official Postgres image executes /docker-entrypoint-initdb.d).
# Fills the placeholders of roles.sql from the environment and applies it. Passwords: letters and digits only.
set -eu

: "${POSTGRES_DB:?}" "${WRITE_DB_PASSWORD:?}" "${READ_DB_PASSWORD:?}"

sed -e "s/{{DATABASE}}/${POSTGRES_DB}/g" \
    -e "s/{{WRITE_PASSWORD}}/${WRITE_DB_PASSWORD}/g" \
    -e "s/{{READ_PASSWORD}}/${READ_DB_PASSWORD}/g" \
    /templates/roles.sql \
  | psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB"
