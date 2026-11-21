#!/bin/bash
# Restores NuGet packages and starts the Docker daemon so the Testcontainers
# integration tests in test/Underground.OutboxTest can run.
set -euo pipefail

# Local machines already have their own Docker setup.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

log() { echo "[session-start] $*"; }

# global.json needs SDK 11; the image's SDK 10 stays, as the runtime for the net10.0 tests.
if ! dotnet --list-sdks | grep -q '^11\.'; then
  log "installing .NET SDK 11"
  dotnet_dir="$(dirname "$(readlink -f "$(command -v dotnet)")")"
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 11.0 --install-dir "$dotnet_dir" > /dev/null
fi

log "restoring NuGet packages"
dotnet restore "$CLAUDE_PROJECT_DIR/Underground.slnx" -v quiet

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

# Matches PostgresFixture; a failed pull only means the first test run pulls it instead.
log "pulling postgres image"
docker pull -q postgres:18.1 > /dev/null || log "image pull failed; tests will pull on demand"

log "done"
