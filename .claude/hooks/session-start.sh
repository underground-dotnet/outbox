#!/bin/bash
# Starts the Docker daemon so the Testcontainers integration tests in
# test/Underground.OutboxTest can run, and warms the NuGet and image caches.
set -euo pipefail

# Local machines already have their own Docker setup.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

# Set when Claude Code invokes the hook; derived from the script path when run by hand.
PROJECT_DIR="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"

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

# Pre-pull the image PostgresFixture asks for, so the first test run isn't a download.
# Searched across the test project so moving the builder call between files does not silently skip the pull.
POSTGRES_IMAGE="$(grep -rhoP 'new PostgreSqlBuilder\("\K[^"]+' "$PROJECT_DIR/test/Underground.OutboxTest" | head -1 || true)"
if [ -n "$POSTGRES_IMAGE" ]; then
  log "pulling $POSTGRES_IMAGE"
  docker pull --quiet "$POSTGRES_IMAGE" > /dev/null || log "pull of $POSTGRES_IMAGE failed; Testcontainers will retry"
else
  log "no PostgreSqlBuilder image found; Testcontainers will pull on demand"
fi

log "restoring NuGet packages"
dotnet restore --nologo "$PROJECT_DIR/Underground.slnx" > /dev/null

log "done"
