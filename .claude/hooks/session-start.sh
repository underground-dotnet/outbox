#!/bin/bash
# Starts the Docker daemon so the Testcontainers integration tests in
# test/Underground.OutboxTest can run, and warms the NuGet and image caches.
set -euo pipefail

# Local machines already have their own Docker setup.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

log() { echo "[session-start] $*"; }

if ! command -v dockerd > /dev/null 2>&1; then
  log "dockerd not found in this image; skipping Docker setup"
  exit 0
fi

if docker info > /dev/null 2>&1; then
  log "Docker daemon already running"
else
  log "starting Docker daemon"
  nohup dockerd > /tmp/dockerd.log 2>&1 &

  for _ in $(seq 1 30); do
    docker info > /dev/null 2>&1 && break
    sleep 1
  done

  if ! docker info > /dev/null 2>&1; then
    log "Docker daemon failed to start; integration tests will not run:"
    tail -20 /tmp/dockerd.log || true
    exit 0
  fi
  log "Docker daemon ready"
fi

# Pre-pull the image DatabaseTest asks for, so the first test run isn't a download.
POSTGRES_IMAGE="$(grep -oP 'new PostgreSqlBuilder\("\K[^"]+' "$CLAUDE_PROJECT_DIR/test/Underground.OutboxTest/DatabaseTest.cs" || true)"
if [ -n "$POSTGRES_IMAGE" ]; then
  log "pulling $POSTGRES_IMAGE"
  docker pull --quiet "$POSTGRES_IMAGE" > /dev/null || log "pull of $POSTGRES_IMAGE failed; Testcontainers will retry"
fi

log "restoring NuGet packages"
dotnet restore --nologo "$CLAUDE_PROJECT_DIR/Underground.slnx" > /dev/null

log "done"
