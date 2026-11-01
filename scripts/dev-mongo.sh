#!/usr/bin/env bash
# Starts the local Mongo container for 006 (idempotent). Data lives in the gallery-mongo volume.
set -euo pipefail

NAME=gallery-mongo

if docker container inspect "$NAME" >/dev/null 2>&1; then
  if [ "$(docker container inspect -f '{{.State.Running}}' "$NAME")" != "true" ]; then
    docker start "$NAME" >/dev/null
  fi
else
  docker run -d --name "$NAME" -p 27017:27017 -v gallery-mongo:/data/db mongo:8 >/dev/null
fi

for _ in $(seq 1 30); do
  if [ "$(docker exec "$NAME" mongosh --quiet --eval 'db.runCommand({ping:1}).ok' 2>/dev/null)" = "1" ]; then
    echo "gallery-mongo ready on localhost:27017"
    exit 0
  fi
  sleep 1
done

echo "gallery-mongo did not answer ping within 30 s" >&2
exit 1
